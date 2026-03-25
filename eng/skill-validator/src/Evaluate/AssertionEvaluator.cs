using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.FileSystemGlobbing.Abstractions;

namespace SkillValidator.Evaluate;

public static class AssertionEvaluator
{
    public static async Task<List<AssertionResult>> EvaluateAssertions(
        IReadOnlyList<Assertion> assertions,
        string agentOutput,
        string workDir,
        int scenarioTimeoutSeconds = EvalSchema.DefaultScenarioTimeoutSeconds)
    {
        var results = new List<AssertionResult>();
        foreach (var assertion in assertions)
        {
            var result = await EvaluateAssertion(assertion, agentOutput, workDir, scenarioTimeoutSeconds);
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
        string workDir,
        int scenarioTimeoutSeconds)
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
            AssertionType.RunCommandAndAssert => await EvalRunCommandAndAssert(assertion, workDir, scenarioTimeoutSeconds),
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

    private const int MaxOutputLength = 4096;

    private static string TruncateOutput(string output)
    {
        if (output.Length <= MaxOutputLength)
            return output;
        return $"{output[..MaxOutputLength]}... [truncated, {output.Length} chars total]";
    }

    private static async Task<AssertionResult> EvalRunCommandAndAssert(Assertion a, string workDir, int scenarioTimeoutSeconds)
    {
        var cmd = a.CommandArgs ?? throw new UnreachableException();
        var command = cmd.CommandToRun;
        var timeoutSeconds = cmd.Timeout ?? scenarioTimeoutSeconds;

        if (timeoutSeconds <= 0)
        {
            return new AssertionResult(a, false, $"Invalid timeout value {timeoutSeconds}s. Timeout must be greater than 0.");
        }

        var processStartInfo = new ProcessStartInfo(command, cmd.CommandArguments ?? string.Empty)
        {
            WorkingDirectory = workDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        AgentRunner.ScrubSensitiveEnvironment(processStartInfo);

        Process process;
        try
        {
            var started = Process.Start(processStartInfo);
            if (started is null)
            {
                return new AssertionResult(a, false, $"Failed to start process '{command}' {cmd.CommandArguments}");
            }
            process = started;
        }
        catch (Exception ex)
        {
            return new AssertionResult(a, false, $"Failed to start process '{command}' {cmd.CommandArguments}: {ex.Message}");
        }

        using (process)
        {
            var stdOutTask = process.StandardOutput.ReadToEndAsync();
            var stdErrTask = process.StandardError.ReadToEndAsync();

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
            try
            {
                await process.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                return new AssertionResult(a, false, $"Command timed out after {timeoutSeconds}s");
            }

            var actualStdOut = await stdOutTask;
            var actualStdErr = await stdErrTask;

            var actualExitCode = process.ExitCode;
            if (cmd.ExpectedExitCode.HasValue && cmd.ExpectedExitCode.Value != actualExitCode)
            {
                return new AssertionResult(a, false, $"Command exited with code {actualExitCode} but expected {cmd.ExpectedExitCode.Value}. Stdout: {TruncateOutput(actualStdOut)} Stderr: {TruncateOutput(actualStdErr)}");
            }

            if (cmd.ExpectedStdOutContains is not null)
            {
                if (!actualStdOut.Contains(cmd.ExpectedStdOutContains, StringComparison.OrdinalIgnoreCase))
                {
                    return new AssertionResult(a, false, $"Command stdout did not contain expected value. Stdout: {TruncateOutput(actualStdOut)}");
                }
            }

            if (cmd.ExpectedStdOutMatches is not null)
            {
                try
                {
                    if (!Regex.IsMatch(actualStdOut, cmd.ExpectedStdOutMatches, RegexOptions.IgnoreCase, RegexTimeout))
                    {
                        return new AssertionResult(a, false, $"Command stdout did not match pattern '{cmd.ExpectedStdOutMatches}'. Stdout: {TruncateOutput(actualStdOut)}");
                    }
                }
                catch (RegexMatchTimeoutException)
                {
                    return new AssertionResult(a, false, $"Regex pattern '{cmd.ExpectedStdOutMatches}' timed out after {RegexTimeout.TotalSeconds}s");
                }
            }

            if (cmd.ExpectedStdErrorContains is not null)
            {
                if (!actualStdErr.Contains(cmd.ExpectedStdErrorContains, StringComparison.OrdinalIgnoreCase))
                {
                    return new AssertionResult(a, false, $"Command stderr did not contain expected value. Stderr: {TruncateOutput(actualStdErr)}");
                }
            }

            if (cmd.ExpectedStdErrorMatches is not null)
            {
                try
                {
                    if (!Regex.IsMatch(actualStdErr, cmd.ExpectedStdErrorMatches, RegexOptions.IgnoreCase, RegexTimeout))
                    {
                        return new AssertionResult(a, false, $"Command stderr did not match pattern '{cmd.ExpectedStdErrorMatches}'. Stderr: {TruncateOutput(actualStdErr)}");
                    }
                }
                catch (RegexMatchTimeoutException)
                {
                    return new AssertionResult(a, false, $"Regex pattern '{cmd.ExpectedStdErrorMatches}' timed out after {RegexTimeout.TotalSeconds}s");
                }
            }

            return new AssertionResult(a, true, string.Empty);
        }
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
