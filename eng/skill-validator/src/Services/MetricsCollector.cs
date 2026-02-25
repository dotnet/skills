using SkillValidator.Models;

namespace SkillValidator.Services;

public static class MetricsCollector
{
    /// <summary>
    /// Analyse events from a "with-skill" run to determine whether the skill was activated.
    /// </summary>
    public static SkillActivationInfo ExtractSkillActivation(
        IReadOnlyList<AgentEvent> skilledEvents,
        Dictionary<string, int> baselineToolBreakdown)
    {
        var detectedSkills = new List<string>();
        int skillEventCount = 0;

        foreach (var evt in skilledEvents)
        {
            var t = evt.Type.ToLowerInvariant();
            if (t.Contains("skill") || t.Contains("instruction"))
            {
                skillEventCount++;
                var name = GetStringValue(evt.Data, "skillName")
                    ?? GetStringValue(evt.Data, "name")
                    ?? "";
                if (name.Length > 0 && !detectedSkills.Contains(name))
                    detectedSkills.Add(name);
            }
        }

        // Build tool breakdown for the skilled run
        var skilledTools = new Dictionary<string, int>();
        foreach (var evt in skilledEvents)
        {
            if (evt.Type == "tool.execution_start")
            {
                var name = GetStringValue(evt.Data, "toolName") ?? "unknown";
                skilledTools[name] = skilledTools.GetValueOrDefault(name) + 1;
            }
        }

        var extraTools = skilledTools.Keys
            .Where(tool => !baselineToolBreakdown.ContainsKey(tool))
            .ToList();

        return new SkillActivationInfo(
            Activated: skillEventCount > 0 || extraTools.Count > 0,
            DetectedSkills: detectedSkills,
            ExtraTools: extraTools,
            SkillEventCount: skillEventCount);
    }

    public static RunMetrics CollectMetrics(
        List<AgentEvent> events,
        string agentOutput,
        long wallTimeMs,
        string workDir)
    {
        int tokenEstimate = 0;
        bool hasRealTokenCounts = false;
        int toolCallCount = 0;
        var toolCallBreakdown = new Dictionary<string, int>();
        int turnCount = 0;
        int errorCount = 0;

        foreach (var evt in events)
        {
            switch (evt.Type)
            {
                case "tool.execution_start":
                {
                    toolCallCount++;
                    var toolName = GetStringValue(evt.Data, "toolName") ?? "unknown";
                    toolCallBreakdown[toolName] = toolCallBreakdown.GetValueOrDefault(toolName) + 1;
                    break;
                }

                case "assistant.message":
                {
                    turnCount++;
                    break;
                }

                case "assistant.usage":
                {
                    var input = GetIntValue(evt.Data, "inputTokens");
                    var output = GetIntValue(evt.Data, "outputTokens");
                    if (input > 0 || output > 0)
                    {
                        hasRealTokenCounts = true;
                        tokenEstimate += input + output;
                    }
                    break;
                }

                case "runner.timeout":
                case "session.error":
                case "runner.error":
                {
                    errorCount++;
                    break;
                }
            }
        }

        // Fallback to character-based estimation if no usage events
        if (!hasRealTokenCounts)
        {
            foreach (var evt in events)
            {
                if (evt.Type is "assistant.message" or "user.message")
                {
                    var content = GetStringValue(evt.Data, "content") ?? "";
                    tokenEstimate += (int)Math.Ceiling(content.Length / 4.0);
                }
            }
        }

        return new RunMetrics
        {
            TokenEstimate = tokenEstimate,
            ToolCallCount = toolCallCount,
            ToolCallBreakdown = toolCallBreakdown,
            TurnCount = turnCount,
            WallTimeMs = wallTimeMs,
            ErrorCount = errorCount,
            TimedOut = events.Any(e => e.Type == "runner.timeout"),
            AgentOutput = agentOutput,
            Events = events,
            WorkDir = workDir,
        };
    }

    private static string? GetStringValue(Dictionary<string, object?> data, string key)
    {
        if (data.TryGetValue(key, out var value) && value is not null)
            return value.ToString();
        return null;
    }

    private static int GetIntValue(Dictionary<string, object?> data, string key)
    {
        if (data.TryGetValue(key, out var value) && value is not null)
        {
            if (value is int i) return i;
            if (value is long l) return (int)l;
            if (value is double d) return (int)d;
            if (int.TryParse(value.ToString(), out var parsed)) return parsed;
        }
        return 0;
    }
}
