using System.Text.Json;
using System.Text.Json.Nodes;
using SkillValidator.Shared;
using GitHub.Copilot.SDK;

namespace SkillValidator.Evaluate;

public sealed record JudgeOptions(
    string Model,
    bool Verbose,
    int Timeout,
    string WorkDir,
    string? SkillPath = null);

public static class Judge
{
    public static Task<(JudgeResult Result, TokenUsage Tokens)> JudgeRun(
        EvalScenario scenario,
        RunMetrics metrics,
        JudgeOptions options,
        Action<string>? log)
    {
        return RetryHelper.ExecuteWithRetry(
            (ct) => JudgeRunOnce(scenario, metrics, scenario.Rubric ?? [], options, log, ct),
            $"Judge for \"{scenario.Name}\"");
    }

    private static async Task<(JudgeResult Result, TokenUsage Tokens)> JudgeRunOnce(
        EvalScenario scenario,
        RunMetrics metrics,
        IReadOnlyList<string> rubric,
        JudgeOptions options,
        Action<string>? log,
        CancellationToken cancellationToken)
    {
        var response = await LlmSession.SendAsync(
            model: options.Model,
            systemPrompt: BuildJudgeSystemPrompt(),
            userPrompt: BuildJudgeUserPrompt(scenario, metrics, rubric),
            workDir: options.WorkDir,
            timeoutMs: options.Timeout,
            verbose: options.Verbose,
            timeoutLabel: "Judge — try --judge-timeout with a higher value, or check --verbose for stuck permission requests",
            onPermissionRequest: request =>
            {
                var result = AgentRunner.CheckPermission(request, options.WorkDir, options.SkillPath, options.Verbose ? log : null, "judge");
                return Task.FromResult(new PermissionRequestResult
                {
                    Kind = result ? PermissionRequestResultKind.Approved : PermissionRequestResultKind.DeniedByRules,
                });
            },
            cancellationToken: cancellationToken);

        return (ParseJudgeResponse(response.Content, rubric), response.Tokens);
    }

    private static string BuildJudgeSystemPrompt() =>
        """
        You are an expert evaluator assessing the quality of an AI agent's work.
        You will be given:
        1. The task prompt the agent was asked to perform
        2. The agent's final output
        3. Metrics about the agent's execution (tool calls, timing, errors)
        4. A full session timeline showing every step the agent took — messages, tool calls, tool results, and errors
        5. A rubric of criteria to evaluate

        Use the session timeline to understand the agent's full reasoning process, not just its final output. Consider:
        - Did the agent take an efficient path or waste steps?
        - Did it recover from errors or get stuck?
        - Did tool calls produce useful results that informed the output?
        - Was the agent's approach methodical or haphazard?

        For each rubric criterion, provide an integer score from 1-5:
          1 = Very poor, criterion not met at all
          2 = Poor, significant issues
          3 = Acceptable, meets basic expectations
          4 = Good, meets expectations well
          5 = Excellent, exceeds expectations

        Also provide an overall quality integer score (1-5) assessing the holistic quality, correctness, and completeness of the output.

        All scores must be integers (1, 2, 3, 4, or 5). Do not use decimals.

        Respond in JSON format:
        {
          "rubric_scores": [
            {"criterion": "...", "score": N, "reasoning": "..."},
            ...
          ],
          "overall_score": N,
          "overall_reasoning": "..."
        }

        Be thorough and critical. A score of 3 is average/acceptable. Only give 5 for truly excellent work.
        """;

    private static string BuildJudgeUserPrompt(
        EvalScenario scenario,
        RunMetrics metrics,
        IReadOnlyList<string> rubric)
    {
        var toolsUsed = metrics.ToolCallBreakdown.Count > 0
            ? string.Join(", ", metrics.ToolCallBreakdown.Select(kv => $"{kv.Key}({kv.Value})"))
            : "none";

        var sections = new List<string>
        {
            $"## Task Prompt\n{scenario.Prompt}",
            $"## Agent Output\n{(metrics.AgentOutput.Length > 0 ? metrics.AgentOutput : "(no output)")}",
            $"""
            ## Execution Metrics
            - Tool calls: {metrics.ToolCallCount}
            - Tools used: {toolsUsed}
            - Turns: {metrics.TurnCount}
            - Time: {metrics.WallTimeMs / 1000.0:F1}s
            - Errors: {metrics.ErrorCount}
            - Estimated tokens: {metrics.TokenEstimate}
            """,
            $"## Session Timeline\n{FormatSessionTimeline(metrics.Events)}",
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

    /// <summary>Maximum number of timeline events sent to the judge to avoid prompt explosion on large scenarios.</summary>
    private const int MaxTimelineEvents = 100;
    /// <summary>Maximum total characters for the formatted timeline string.</summary>
    private const int MaxTimelineChars = 40_000;

    private static string FormatSessionTimeline(IReadOnlyList<AgentEvent> events)
    {
        var relevantTypes = new HashSet<string>
        {
            "user.message", "assistant.message", "tool.execution_start",
            "tool.execution_complete", "session.error", "runner.error",
        };

        var relevant = events.Where(e => relevantTypes.Contains(e.Type)).ToList();
        if (relevant.Count == 0) return "(no events recorded)";

        // When the timeline is very large (e.g. hundreds of tool calls on a big
        // project), keep the first and last events with a summary in between so
        // the judge prompt stays within a reasonable size.
        if (relevant.Count > MaxTimelineEvents)
        {
            var headCount = MaxTimelineEvents / 2;
            var tailCount = MaxTimelineEvents - headCount;
            var omitted = relevant.Count - headCount - tailCount;
            var head = relevant.Take(headCount).ToList();
            var tail = relevant.Skip(relevant.Count - tailCount).ToList();
            relevant = [..head, ..tail];
            // Insert a synthetic marker so the judge knows events were trimmed
            relevant.Insert(headCount, new AgentEvent(
                "summary",
                0,
                new Dictionary<string, JsonNode?> { ["message"] = JsonValue.Create($"... ({omitted} events omitted for brevity) ...") }));
        }

        var sb = new System.Text.StringBuilder();
        foreach (var e in relevant)
        {
            if (e.Type == "summary")
            {
                sb.AppendLine(GetStr(e.Data, "message"));
                continue;
            }
            var line = FormatTimelineEntry(e);
            if (sb.Length + line.Length > MaxTimelineChars)
            {
                sb.AppendLine($"... (timeline truncated at {MaxTimelineChars} chars; {relevant.Count - relevant.IndexOf(e)} events remaining) ...");
                break;
            }
            sb.AppendLine(line);
        }
        return sb.ToString().TrimEnd();
    }

    private static string FormatTimelineEntry(AgentEvent e) => e.Type switch
    {
        "user.message" => $"[USER] {Truncate(GetStr(e.Data, "content"), 200)}",
        "assistant.message" => FormatAssistantEvent(e),
        "tool.execution_start" => $"[TOOL START] {GetStr(e.Data, "toolName")}: {Truncate(GetStr(e.Data, "arguments"), 200)}",
        "tool.execution_complete" => FormatToolComplete(e),
        "session.error" or "runner.error" => $"[ERROR] {GetStr(e.Data, "message")}",
        _ => $"[{e.Type}]",
    };

    private static string FormatAssistantEvent(AgentEvent e)
    {
        var content = GetStr(e.Data, "content");
        var parts = new List<string>();
        if (content.Length > 0) parts.Add(Truncate(content, 500));

        if (e.Data.TryGetValue("toolRequests", out var toolReqs) && toolReqs is JsonArray toolArr)
        {
            var tools = string.Join(", ", toolArr
                .Select(t => t?["name"]?.GetValue<string>() ?? ""));
            if (tools.Length > 0) parts.Add($"(called tools: {tools})");
        }
        return $"[ASSISTANT] {string.Join(" ", parts)}";
    }

    private static string FormatToolComplete(AgentEvent e)
    {
        var success = GetStr(e.Data, "success");
        bool isOk = success is "True" or "true";
        var result = Truncate(GetStr(e.Data, "result"), 300);
        return $"[TOOL {(isOk ? "OK" : "FAIL")}] {result}";
    }

    /// <summary>Exported for testing only.</summary>
    internal static JudgeResult ParseJudgeResponse(string content, IReadOnlyList<string> rubric)
    {
        var jsonStr = LlmJson.ExtractJson(content)
            ?? throw new InvalidOperationException($"Judge response contained no JSON. Raw response:\n{content[..Math.Min(500, content.Length)]}");

        var parsed = LlmJson.ParseLlmJson(jsonStr, "judge response");

        var rubricScores = new List<RubricScore>();
        if (parsed.TryGetProperty("rubric_scores", out var scoresEl) && scoresEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var s in scoresEl.EnumerateArray())
            {
                var criterion = s.GetProperty("criterion").GetString() ?? "";
                var score = Math.Round(Math.Max(1, Math.Min(5, s.GetProperty("score").GetDouble())) * 10) / 10;
                var reasoning = s.TryGetProperty("reasoning", out var r) ? r.GetString() ?? "" : "";
                rubricScores.Add(new RubricScore(criterion, score, reasoning));
            }
        }

        var overallScore = Math.Round(
            Math.Max(1, Math.Min(5, parsed.GetProperty("overall_score").GetDouble())) * 10) / 10;
        var overallReasoning = parsed.TryGetProperty("overall_reasoning", out var or) ? or.GetString() ?? "" : "";

        return new JudgeResult(rubricScores, overallScore, overallReasoning);
    }

    private static string Truncate(string s, int max) =>
        s.Length > max ? s[..(max - 3)] + "..." : s;

    private static string GetStr(Dictionary<string, JsonNode?> data, string key) =>
        data.TryGetValue(key, out var v) && v is not null ? v.ToString() ?? "" : "";
}
