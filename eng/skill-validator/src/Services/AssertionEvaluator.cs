using System.Text.RegularExpressions;
using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.FileSystemGlobbing.Abstractions;
using SkillValidator.Models;

namespace SkillValidator.Services;

public static class AssertionEvaluator
{
    public static async Task<List<AssertionResult>> EvaluateAssertions(
        IReadOnlyList<Assertion> assertions,
        string agentOutput,
        string workDir)
    {
        var results = new List<AssertionResult>();
        foreach (var assertion in assertions)
        {
            var result = await EvaluateAssertion(assertion, agentOutput, workDir);
            results.Add(result);
        }
        return results;
    }

    public static List<AssertionResult> EvaluateConstraints(
        EvalScenario scenario,
        RunMetrics metrics)
    {
        var results = new List<AssertionResult>();
        var usedTools = metrics.ToolCallBreakdown.Keys.ToList();

        if (scenario.ExpectTools is not null)
        {
            foreach (var tool in scenario.ExpectTools)
            {
                bool used = usedTools.Contains(tool);
                results.Add(new AssertionResult(
                    new Assertion(AssertionType.ExpectTools, Value: tool),
                    used,
                    used
                        ? $"Tool '{tool}' was used"
                        : $"Expected tool '{tool}' was not used (tools used: {(usedTools.Count > 0 ? string.Join(", ", usedTools) : "none")})"));
            }
        }

        if (scenario.RejectTools is not null)
        {
            foreach (var tool in scenario.RejectTools)
            {
                bool used = usedTools.Contains(tool);
                results.Add(new AssertionResult(
                    new Assertion(AssertionType.RejectTools, Value: tool),
                    !used,
                    !used
                        ? $"Tool '{tool}' was not used (expected)"
                        : $"Tool '{tool}' was used but should not be"));
            }
        }

        if (scenario.MaxTurns is { } maxTurns)
        {
            bool passed = metrics.TurnCount <= maxTurns;
            results.Add(new AssertionResult(
                new Assertion(AssertionType.MaxTurns, Value: maxTurns.ToString()),
                passed,
                passed
                    ? $"Turn count {metrics.TurnCount} ≤ {maxTurns}"
                    : $"Turn count {metrics.TurnCount} exceeds max_turns {maxTurns}"));
        }

        if (scenario.MaxTokens is { } maxTokens)
        {
            bool passed = metrics.TokenEstimate <= maxTokens;
            results.Add(new AssertionResult(
                new Assertion(AssertionType.MaxTokens, Value: maxTokens.ToString()),
                passed,
                passed
                    ? $"Token usage {metrics.TokenEstimate} ≤ {maxTokens}"
                    : $"Token usage {metrics.TokenEstimate} exceeds max_tokens {maxTokens}"));
        }

        return results;
    }

    private static async Task<AssertionResult> EvaluateAssertion(
        Assertion assertion,
        string agentOutput,
        string workDir)
    {
        return assertion.Type switch
        {
            AssertionType.FileExists => await EvalFileExists(assertion, workDir),
            AssertionType.FileNotExists => await EvalFileNotExists(assertion, workDir),
            AssertionType.FileContains => await EvalFileContains(assertion, workDir),
            AssertionType.FileNotContains => await EvalFileNotContains(assertion, workDir),
            AssertionType.OutputContains => EvalOutputContains(assertion, agentOutput),
            AssertionType.OutputNotContains => EvalOutputNotContains(assertion, agentOutput),
            AssertionType.OutputMatches => EvalOutputMatches(assertion, agentOutput),
            AssertionType.OutputNotMatches => EvalOutputNotMatches(assertion, agentOutput),
            AssertionType.ExitSuccess => EvalExitSuccess(assertion, agentOutput),
            _ => new AssertionResult(assertion, false, $"Unknown assertion type: {assertion.Type}"),
        };
    }

    private static async Task<AssertionResult> EvalFileExists(Assertion a, string workDir)
    {
        var pattern = a.Path ?? "";
        bool exists = await FileExistsGlob(pattern, workDir);
        return new AssertionResult(a, exists,
            exists
                ? $"File matching '{pattern}' found"
                : $"No file matching '{pattern}' found in {workDir}");
    }

    private static async Task<AssertionResult> EvalFileNotExists(Assertion a, string workDir)
    {
        var pattern = a.Path ?? "";
        bool exists = await FileExistsGlob(pattern, workDir);
        return new AssertionResult(a, !exists,
            !exists
                ? $"No file matching '{pattern}' found (expected)"
                : $"File matching '{pattern}' found but should not exist");
    }

    private static async Task<AssertionResult> EvalFileContains(Assertion a, string workDir)
    {
        var pattern = a.Path ?? "";
        var value = a.Value ?? "";
        var files = FindMatchingFiles(pattern, workDir);
        if (files.Count == 0)
            return new AssertionResult(a, false, $"No file matching '{pattern}' found");

        foreach (var file in files)
        {
            try
            {
                var content = await File.ReadAllTextAsync(Path.Combine(workDir, file));
                if (content.Contains(value))
                    return new AssertionResult(a, true, $"File '{file}' contains '{value}'");
            }
            catch
            {
                // skip unreadable files
            }
        }
        return new AssertionResult(a, false, $"No file matching '{pattern}' contains '{value}'");
    }

    private static async Task<AssertionResult> EvalFileNotContains(Assertion a, string workDir)
    {
        var pattern = a.Path ?? "";
        var value = a.Value ?? "";
        var files = FindMatchingFiles(pattern, workDir);
        if (files.Count == 0)
            return new AssertionResult(a, false, $"No file matching '{pattern}' found");

        foreach (var file in files)
        {
            try
            {
                var content = await File.ReadAllTextAsync(Path.Combine(workDir, file));
                if (content.Contains(value))
                    return new AssertionResult(a, false, $"File '{file}' contains '{value}' but should not");
            }
            catch
            {
                // skip unreadable files
            }
        }
        return new AssertionResult(a, true, $"No file matching '{pattern}' contains '{value}' (expected)");
    }

    private static AssertionResult EvalOutputContains(Assertion a, string agentOutput)
    {
        var value = a.Value ?? "";
        bool contains = agentOutput.Contains(value, StringComparison.OrdinalIgnoreCase);
        return new AssertionResult(a, contains,
            contains
                ? $"Output contains '{value}'"
                : $"Output does not contain '{value}'");
    }

    private static AssertionResult EvalOutputNotContains(Assertion a, string agentOutput)
    {
        var value = a.Value ?? "";
        bool contains = agentOutput.Contains(value, StringComparison.OrdinalIgnoreCase);
        return new AssertionResult(a, !contains,
            !contains
                ? $"Output does not contain '{value}' (expected)"
                : $"Output contains '{value}' but should not");
    }

    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(5);

    private static AssertionResult EvalOutputMatches(Assertion a, string agentOutput)
    {
        var pattern = a.Pattern ?? "";
        try
        {
            bool matches = Regex.IsMatch(agentOutput, pattern, RegexOptions.IgnoreCase, RegexTimeout);
            return new AssertionResult(a, matches,
                matches
                    ? $"Output matches pattern '{pattern}'"
                    : $"Output does not match pattern '{pattern}'");
        }
        catch (RegexMatchTimeoutException)
        {
            return new AssertionResult(a, false,
                $"Regex pattern '{pattern}' timed out after {RegexTimeout.TotalSeconds}s (possible catastrophic backtracking)");
        }
    }

    private static AssertionResult EvalOutputNotMatches(Assertion a, string agentOutput)
    {
        var pattern = a.Pattern ?? "";
        try
        {
            bool matches = Regex.IsMatch(agentOutput, pattern, RegexOptions.IgnoreCase, RegexTimeout);
            return new AssertionResult(a, !matches,
                !matches
                    ? $"Output does not match pattern '{pattern}' (expected)"
                    : $"Output matches pattern '{pattern}' but should not");
        }
        catch (RegexMatchTimeoutException)
        {
            return new AssertionResult(a, false,
                $"Regex pattern '{pattern}' timed out after {RegexTimeout.TotalSeconds}s (possible catastrophic backtracking)");
        }
    }

    private static AssertionResult EvalExitSuccess(Assertion a, string agentOutput)
    {
        bool success = agentOutput.Length > 0;
        return new AssertionResult(a, success,
            success
                ? "Agent completed successfully"
                : "Agent produced no output");
    }

    private static Task<bool> FileExistsGlob(string pattern, string workDir)
    {
        try
        {
            var matcher = new Matcher();
            matcher.AddInclude(pattern);
            var result = matcher.Execute(new DirectoryInfoWrapper(new DirectoryInfo(workDir)));
            if (result.HasMatches) return Task.FromResult(true);
        }
        catch
        {
            // Fall back to direct file check
        }

        try
        {
            return Task.FromResult(File.Exists(Path.Combine(workDir, pattern)));
        }
        catch
        {
            return Task.FromResult(false);
        }
    }

    private static List<string> FindMatchingFiles(string pattern, string workDir)
    {
        try
        {
            var matcher = new Matcher();
            matcher.AddInclude(pattern);
            var result = matcher.Execute(new DirectoryInfoWrapper(new DirectoryInfo(workDir)));
            return result.Files.Select(f => f.Path).ToList();
        }
        catch
        {
            return [];
        }
    }
}
