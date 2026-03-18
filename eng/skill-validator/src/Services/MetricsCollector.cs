using System.Text.Json.Nodes;
using SkillValidator.Models;

namespace SkillValidator.Services;

public static class MetricsCollector
{
    /// <summary>
    /// Analyse events from a "with-skill" run to determine whether the skill was activated.
    /// When <paramref name="targetSkillName"/> is set, only counts as "Activated" if the
    /// target skill was specifically detected — prevents false positives in plugin runs
    /// where sibling skills may fire instead of the skill under test.
    /// </summary>
    public static SkillActivationInfo ExtractSkillActivation(
        IReadOnlyList<AgentEvent> skilledEvents,
        Dictionary<string, int> baselineToolBreakdown,
        string? targetSkillName = null)
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

        // When targetSkillName is set, activation is determined solely by whether
        // the target skill was specifically detected (case-insensitive). ExtraTools
        // and sibling skill names are still populated for diagnostic purposes but
        // do NOT contribute to the Activated flag — we control the SDK and it always
        // emits SkillInvokedEvent when a skill is loaded.
        bool activated = targetSkillName is null
            ? skillEventCount > 0 || extraTools.Count > 0
            : detectedSkills.Any(s =>
                s.Equals(targetSkillName, StringComparison.OrdinalIgnoreCase));

        return new SkillActivationInfo(
            Activated: activated,
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
        int inputTokens = 0;
        int outputTokens = 0;
        int cacheReadTokens = 0;
        int cacheWriteTokens = 0;
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
                    var cacheRead = GetIntValue(evt.Data, "cacheReadTokens");
                    var cacheWrite = GetIntValue(evt.Data, "cacheWriteTokens");
                    if (input > 0 || output > 0 || cacheRead > 0 || cacheWrite > 0)
                    {
                        hasRealTokenCounts = true;
                        // When a provider reports cache activity without populating
                        // inputTokens, fall back to cacheRead so the estimate isn't
                        // zeroed-out.
                        tokenEstimate += (input > 0 ? input : cacheRead) + output;
                        inputTokens += input;
                        outputTokens += output;
                        cacheReadTokens += cacheRead;
                        cacheWriteTokens += cacheWrite;
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
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            CacheReadTokens = cacheReadTokens,
            CacheWriteTokens = cacheWriteTokens,
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

    private static string? GetStringValue(Dictionary<string, JsonNode?> data, string key)
    {
        if (data.TryGetValue(key, out var value) && value is not null)
            return value.ToString();
        return null;
    }

    private static int GetIntValue(Dictionary<string, JsonNode?> data, string key)
    {
        if (data.TryGetValue(key, out var value) && value is not null)
        {
            try { return value.GetValue<int>(); } catch { }
            try { return (int)value.GetValue<long>(); } catch { }
            try { return (int)value.GetValue<double>(); } catch { }
            if (int.TryParse(value.ToString(), out var parsed)) return parsed;
        }
        return 0;
    }
}
