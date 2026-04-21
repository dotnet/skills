using System.Text.Json;
using System.Text.Json.Nodes;
using SkillValidator.Shared;
using GitHub.Copilot.SDK;

namespace SkillValidator.Evaluate;

public sealed record PairwiseJudgeOptions(
    string Model,
    bool Verbose,
    int Timeout,
    string WorkDir,
    string? SkillPath = null,
    string? SkilledWorkDir = null);

public static class PairwiseJudge
{
    /// <summary>
    /// Run a pairwise comparison with position-swap bias mitigation.
    /// Calls the judge twice (A-then-B and B-then-A) and checks consistency.
    /// </summary>
    public static async Task<(PairwiseJudgeResult Result, TokenUsage Tokens)> Judge(
        EvalScenario scenario,
        RunMetrics baselineMetrics,
        RunMetrics withSkillMetrics,
        PairwiseJudgeOptions options,
        Action<string>? log,
        CancellationToken cancellationToken = default)
    {
        var results = await Task.WhenAll(
            JudgeOnce(scenario, baselineMetrics, withSkillMetrics, options, "forward", log, cancellationToken),
            JudgeOnce(scenario, withSkillMetrics, baselineMetrics, options, "reverse", log, cancellationToken));
        var (forwardResult, forwardTokens) = results[0];
        var (reverseResult, reverseTokens) = results[1];
        var totalTokens = forwardTokens + reverseTokens;

        bool consistent = forwardResult.OverallWinner == reverseResult.OverallWinner;

        if (consistent)
            return (forwardResult with { PositionSwapConsistent = true }, totalTokens);

        if (options.Verbose)
        {
            Console.Error.WriteLine(
                $"      ⚠️  Position-swap inconsistency for \"{scenario.Name}\" " +
                $"(forward: {forwardResult.OverallWinner}, reverse: {reverseResult.OverallWinner})");
        }

        return (MergeInconsistentResults(forwardResult, reverseResult), totalTokens);
    }

    private static Task<(PairwiseJudgeResult Result, TokenUsage Tokens)> JudgeOnce(
        EvalScenario scenario,
        RunMetrics metricsA,
        RunMetrics metricsB,
        PairwiseJudgeOptions options,
        string direction,
        Action<string>? log,
        CancellationToken cancellationToken = default)
    {
        return RetryHelper.ExecuteWithRetry(
            (ct) => JudgeCall(scenario, metricsA, metricsB, options, direction, log, ct),
            $"Pairwise judge ({direction}) for \"{scenario.Name}\"",
            cancellationToken: cancellationToken);
    }

    private static async Task<(PairwiseJudgeResult Result, TokenUsage Tokens)> JudgeCall(
        EvalScenario scenario,
        RunMetrics metricsA,
        RunMetrics metricsB,
        PairwiseJudgeOptions options,
        string direction,
        Action<string>? log,
        CancellationToken cancellationToken)
    {
        var rubric = scenario.Rubric ?? [];

        var response = await LlmSession.SendAsync(
            model: options.Model,
            systemPrompt: BuildPairwiseSystemPrompt(),
            userPrompt: BuildPairwiseUserPrompt(scenario, metricsA, metricsB, rubric),
            workDir: options.WorkDir,
            timeoutMs: options.Timeout,
            verbose: options.Verbose,
            timeoutLabel: $"Pairwise judge ({direction})",
            onPermissionRequest: (request, invocation) =>
            {
                // Pairwise judge sessions: deny all tool permissions. The judge
                // should operate purely on the provided text — no tool execution.
                return Task.FromResult(new PermissionRequestResult
                {
                    Kind = PermissionRequestResultKind.DeniedByRules,
                });
            },
            cancellationToken: cancellationToken);

        return (ParsePairwiseResponse(response.Content, rubric, direction), response.Tokens);
    }

    private static string BuildPairwiseSystemPrompt() =>
        """
        You are an expert evaluator comparing two AI agent runs on the same task.
        You will see the task prompt, then TWO agent runs (Response A and Response B) with their outputs, metrics, and full session timelines.

        Your job is to determine which response is better and by how much.

        For each rubric criterion, decide:
        - "winner": "A" or "B" or "tie"
        - "magnitude": one of "much-better", "slightly-better", "equal", "slightly-worse", "much-worse"
          (from the perspective of the winner — "much-better" means the winner is much better)
        - "reasoning": brief explanation

        Also provide an overall verdict with the same fields.

        Focus on the QUALITY of the final result, not operational efficiency:
        - Quality and correctness of the final output
        - Did it recover from errors or get stuck?
        - Was the approach methodical or haphazard?
        - Do NOT factor in token count, number of tool calls, or execution speed — those are scored separately

        Respond in JSON format:
        {
          "rubric_results": [
            {"criterion": "...", "winner": "A"|"B"|"tie", "magnitude": "...", "reasoning": "..."},
            ...
          ],
          "overall_winner": "A"|"B"|"tie",
          "overall_magnitude": "much-better"|"slightly-better"|"equal"|"slightly-worse"|"much-worse",
          "overall_reasoning": "..."
        }

        Be thorough and critical. Only say "much-better" for genuinely large quality gaps.
        """;

    private static string BuildPairwiseUserPrompt(
        EvalScenario scenario,
        RunMetrics metricsA,
        RunMetrics metricsB,
        IReadOnlyList<string> rubric)
    {
        var sections = new List<string>
        {
            $"## Task Prompt\n{scenario.Prompt}",
            FormatRunSection("A", metricsA),
            FormatRunSection("B", metricsB),
        };

        if (rubric.Count > 0)
        {
            sections.Add($"## Rubric Criteria\n{string.Join("\n", rubric.Select((r, i) => $"{i + 1}. {r}"))}");
        }
        else
        {
            sections.Add("## Rubric Criteria\n1. The agent completed the requested task correctly\n2. The output is clear and well-structured");
        }

        return string.Join("\n\n", sections);
    }

    private static string FormatRunSection(string label, RunMetrics metrics)
    {
        var toolsUsed = metrics.ToolCallBreakdown.Count > 0
            ? string.Join(", ", metrics.ToolCallBreakdown.Select(kv => $"{kv.Key}({kv.Value})"))
            : "none";

        return $"""
            ## Response {label}

            ### Output
            {(metrics.AgentOutput.Length > 0 ? metrics.AgentOutput : "(no output)")}

            ### Metrics
            - Tools used: {toolsUsed}
            - Errors: {metrics.ErrorCount}

            ### Session Timeline
            {FormatTimelineCompact(metrics.Events)}
            """;
    }

    /// <summary>Maximum number of timeline events to include in pairwise prompts.</summary>
    private const int MaxTimelineEvents = 80;
    /// <summary>Maximum total characters for each formatted timeline.</summary>
    private const int MaxTimelineChars = 30_000;

    private static string FormatTimelineCompact(IReadOnlyList<AgentEvent> events)
    {
        var relevantTypes = new HashSet<string>
        {
            "user.message", "assistant.message", "tool.execution_start",
            "tool.execution_complete", "session.error", "runner.error",
        };

        var errorTypes = new HashSet<string> { "session.error", "runner.error" };

        var relevant = events.Where(e => relevantTypes.Contains(e.Type)).ToList();
        if (relevant.Count == 0) return "(no events recorded)";

        // Cap the event count — keep head and tail with all error events preserved.
        if (relevant.Count > MaxTimelineEvents)
        {
            var errors = relevant.Where(e => errorTypes.Contains(e.Type)).ToList();
            var nonErrors = relevant.Where(e => !errorTypes.Contains(e.Type)).ToList();

            if (errors.Count >= MaxTimelineEvents)
            {
                var errorBudget = Math.Max(0, MaxTimelineEvents - 1);
                var keptErrors = errors.Take(errorBudget).ToList();
                var omittedErrors = errors.Count - keptErrors.Count;

                relevant = [new AgentEvent(
                    "summary", 0,
                    new Dictionary<string, JsonNode?> { ["message"] = JsonValue.Create(
                        $"... ({nonErrors.Count} non-error events omitted, {omittedErrors} error(s) omitted, {keptErrors.Count} error(s) shown) ...") })];
                relevant.AddRange(keptErrors);
            }
            else
            {
                var budget = MaxTimelineEvents - errors.Count;
                var headCount = Math.Max(0, budget / 2);
                var tailCount = Math.Max(0, budget - headCount);
                var omitted = nonErrors.Count - headCount - tailCount;

                var head = nonErrors.Take(headCount).ToList();
                var tail = nonErrors.Skip(Math.Max(0, nonErrors.Count - tailCount)).ToList();

                var omittedEvents = nonErrors.Skip(headCount).Take(Math.Max(0, omitted)).ToList();
                var omittedToolCalls = omittedEvents.Count(e => e.Type is "tool.execution_start" or "tool.execution_complete");
                var omittedMessages = omittedEvents.Count(e => e.Type is "user.message" or "assistant.message");

                var summaryParts = new List<string> { $"{omitted} events omitted" };
                if (omittedToolCalls > 0) summaryParts.Add($"{omittedToolCalls} tool events");
                if (omittedMessages > 0) summaryParts.Add($"{omittedMessages} messages");
                if (errors.Count > 0) summaryParts.Add($"{errors.Count} error(s) preserved");

                relevant = [..head];
                relevant.Add(new AgentEvent(
                    "summary", 0,
                    new Dictionary<string, JsonNode?> { ["message"] = JsonValue.Create($"... ({string.Join(", ", summaryParts)}) ...") }));
                relevant.AddRange(errors);
                relevant.AddRange(tail);
            }
        }

        var sb = new System.Text.StringBuilder();
        foreach (var e in relevant)
        {
            if (e.Type == "summary")
            {
                sb.AppendLine(GetStr(e.Data, "message"));
                continue;
            }
            var line = e.Type switch
            {
                "user.message" => $"[USER] {Trunc(GetStr(e.Data, "content"), 200)}",
                "assistant.message" => FormatAssistantTimeline(e),
                "tool.execution_start" => $"[TOOL START] {GetStr(e.Data, "toolName")}: {Trunc(GetStr(e.Data, "arguments"), 200)}",
                "tool.execution_complete" => $"[TOOL {(GetStr(e.Data, "success") is "True" or "true" ? "OK" : "FAIL")}] {Trunc(GetStr(e.Data, "result"), 200)}",
                "session.error" or "runner.error" => $"[ERROR] {GetStr(e.Data, "message")}",
                _ => $"[{e.Type}]",
            };
            if (sb.Length + line.Length > MaxTimelineChars)
            {
                sb.AppendLine($"... (timeline truncated at {MaxTimelineChars} chars) ...");
                break;
            }
            sb.AppendLine(line);
        }
        return sb.ToString().TrimEnd();
    }

    private static string FormatAssistantTimeline(AgentEvent e)
    {
        var content = GetStr(e.Data, "content");
        var parts = new List<string>();
        if (content.Length > 0) parts.Add(Trunc(content, 400));

        if (e.Data.TryGetValue("toolRequests", out var toolReqs) && toolReqs is JsonArray toolArr)
        {
            var tools = string.Join(", ", toolArr
                .Select(t => t?["name"]?.GetValue<string>() ?? ""));
            if (tools.Length > 0) parts.Add($"(called tools: {tools})");
        }
        return $"[ASSISTANT] {string.Join(" ", parts)}";
    }

    /// <summary>Exported for testing only.</summary>
    internal static PairwiseJudgeResult ParsePairwiseResponse(
        string content,
        IReadOnlyList<string> rubric,
        string direction)
    {
        var jsonStr = LlmJson.ExtractJson(content)
            ?? throw new InvalidOperationException($"Pairwise judge response contained no JSON ({direction})");

        var parsed = LlmJson.ParseLlmJson(jsonStr, $"pairwise judge ({direction})");

        var rubricResults = new List<PairwiseRubricResult>();
        if (parsed.TryGetProperty("rubric_results", out var resultsEl) && resultsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var r in resultsEl.EnumerateArray())
            {
                var rawWinner = (r.TryGetProperty("winner", out var w) ? w.GetString() ?? "tie" : "tie").ToUpperInvariant();
                var magnitude = NormalizeMagnitude(r.TryGetProperty("magnitude", out var m) ? m.GetString() : null);

                string winner;
                if (rawWinner is "TIE" or "EQUAL")
                    winner = "tie";
                else if (direction == "forward")
                    winner = rawWinner == "A" ? "baseline" : "skill";
                else
                    winner = rawWinner == "A" ? "skill" : "baseline";

                rubricResults.Add(new PairwiseRubricResult(
                    r.TryGetProperty("criterion", out var c) ? c.GetString() ?? "" : "",
                    winner,
                    magnitude,
                    r.TryGetProperty("reasoning", out var re) ? re.GetString() ?? "" : ""));
            }
        }

        var rawOverallWinner = (parsed.TryGetProperty("overall_winner", out var ow) ? ow.GetString() ?? "tie" : "tie").ToUpperInvariant();
        string overallWinner;
        if (rawOverallWinner is "TIE" or "EQUAL")
            overallWinner = "tie";
        else if (direction == "forward")
            overallWinner = rawOverallWinner == "A" ? "baseline" : "skill";
        else
            overallWinner = rawOverallWinner == "A" ? "skill" : "baseline";

        return new PairwiseJudgeResult(
            RubricResults: rubricResults,
            OverallWinner: overallWinner,
            OverallMagnitude: NormalizeMagnitude(parsed.TryGetProperty("overall_magnitude", out var om) ? om.GetString() : null),
            OverallReasoning: parsed.TryGetProperty("overall_reasoning", out var orr) ? orr.GetString() ?? "" : "",
            PositionSwapConsistent: true);
    }

    /// <summary>
    /// Convert a PairwiseJudgeResult into a quality improvement score in [-1, 1].
    /// </summary>
    public static (double QualityImprovement, double OverallImprovement) PairwiseToQualityScore(PairwiseJudgeResult result)
    {
        double overallScore = PairwiseMagnitudeScores.GetScore(result.OverallMagnitude);
        if (result.OverallWinner == "baseline")
            overallScore = -Math.Abs(overallScore);
        else if (result.OverallWinner == "tie")
            overallScore = 0;
        else
            overallScore = Math.Abs(overallScore);

        double rubricSum = 0;
        foreach (var r in result.RubricResults)
        {
            double score = PairwiseMagnitudeScores.GetScore(r.Magnitude);
            if (r.Winner == "baseline")
                score = -Math.Abs(score);
            else if (r.Winner == "tie")
                score = 0;
            else
                score = Math.Abs(score);
            rubricSum += score;
        }

        double qualityScore = result.RubricResults.Count > 0
            ? rubricSum / result.RubricResults.Count
            : 0;

        return (qualityScore, overallScore);
    }

    internal static PairwiseMagnitude NormalizeMagnitude(string? raw)
    {
        var s = (raw ?? "equal").ToLowerInvariant().Replace('_', '-');
        return s switch
        {
            "much-better" => PairwiseMagnitude.MuchBetter,
            "slightly-better" => PairwiseMagnitude.SlightlyBetter,
            "equal" => PairwiseMagnitude.Equal,
            "slightly-worse" => PairwiseMagnitude.SlightlyWorse,
            "much-worse" => PairwiseMagnitude.MuchWorse,
            _ => PairwiseMagnitude.Equal,
        };
    }

    private static PairwiseJudgeResult MergeInconsistentResults(
        PairwiseJudgeResult forward,
        PairwiseJudgeResult reverse)
    {
        var merged = forward.RubricResults.Select((fr, i) =>
        {
            var rr = i < reverse.RubricResults.Count ? reverse.RubricResults[i] : null;
            if (rr is null || fr.Winner != rr.Winner)
            {
                return new PairwiseRubricResult(
                    fr.Criterion,
                    "tie",
                    PairwiseMagnitude.Equal,
                    $"Position-swap inconsistent: forward={fr.Winner}, reverse={rr?.Winner ?? "unknown"}");
            }
            return fr;
        }).ToList();

        return new PairwiseJudgeResult(
            RubricResults: merged,
            OverallWinner: "tie",
            OverallMagnitude: PairwiseMagnitude.Equal,
            OverallReasoning: $"Position-swap inconsistent (forward: {forward.OverallWinner}, reverse: {reverse.OverallWinner}). Defaulting to tie.",
            PositionSwapConsistent: false);
    }

    private static string Trunc(string s, int max) =>
        s.Length > max ? s[..(max - 3)] + "..." : s;

    private static string GetStr(Dictionary<string, JsonNode?> data, string key) =>
        data.TryGetValue(key, out var v) && v is not null ? v.ToString() ?? "" : "";
}
