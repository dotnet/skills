using System.Text.Json.Nodes;
using SkillValidator.Evaluate;
using SkillValidator.Shared;

namespace SkillValidator.Tests;

public class ExtractSkillActivationTests
{
    private static AgentEvent MakeEvent(string type, Dictionary<string, JsonNode?>? data = null)
    {
        return new AgentEvent(type, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), data ?? new Dictionary<string, JsonNode?>());
    }

    private static Dictionary<string, JsonNode?> D(params (string Key, JsonNode? Value)[] entries)
    {
        var dict = new Dictionary<string, JsonNode?>();
        foreach (var (key, value) in entries)
            dict[key] = value;
        return dict;
    }

    [Fact]
    public void DetectsActivationFromSkillSessionEvents()
    {
        var events = new List<AgentEvent>
        {
            MakeEvent("skill.loaded", D(("skillName", JsonValue.Create("my-skill")))),
            MakeEvent("assistant.message", D(("content", JsonValue.Create("hello")))),
            MakeEvent("tool.execution_start", D(("toolName", JsonValue.Create("bash")))),
        };

        var result = MetricsCollector.ExtractSkillActivation(events, new Dictionary<string, int> { ["bash"] = 1 });

        Assert.True(result.Activated);
        Assert.Equal(["my-skill"], result.DetectedSkills);
        Assert.Equal(1, result.SkillEventCount);
        Assert.Empty(result.ExtraTools);
    }

    [Fact]
    public void DetectsActivationFromInstructionEvents()
    {
        var events = new List<AgentEvent>
        {
            MakeEvent("instruction.attached", D(("name", JsonValue.Create("build-helper")))),
            MakeEvent("tool.execution_start", D(("toolName", JsonValue.Create("read")))),
        };

        var result = MetricsCollector.ExtractSkillActivation(events, new Dictionary<string, int> { ["read"] = 1 });

        Assert.True(result.Activated);
        Assert.Equal(["build-helper"], result.DetectedSkills);
        Assert.Equal(1, result.SkillEventCount);
    }

    [Fact]
    public void DetectsActivationFromExtraToolsNotInBaseline()
    {
        var events = new List<AgentEvent>
        {
            MakeEvent("tool.execution_start", D(("toolName", JsonValue.Create("bash")))),
            MakeEvent("tool.execution_start", D(("toolName", JsonValue.Create("msbuild_analyze")))),
            MakeEvent("assistant.message", D(("content", JsonValue.Create("done")))),
        };

        var result = MetricsCollector.ExtractSkillActivation(events, new Dictionary<string, int> { ["bash"] = 3 });

        Assert.True(result.Activated);
        Assert.Empty(result.DetectedSkills);
        Assert.Equal(["msbuild_analyze"], result.ExtraTools);
        Assert.Equal(0, result.SkillEventCount);
    }

    [Fact]
    public void ReportsNotActivatedWhenNoSkillEventsAndNoExtraTools()
    {
        var events = new List<AgentEvent>
        {
            MakeEvent("tool.execution_start", D(("toolName", JsonValue.Create("bash")))),
            MakeEvent("assistant.message", D(("content", JsonValue.Create("done")))),
        };

        var result = MetricsCollector.ExtractSkillActivation(events, new Dictionary<string, int> { ["bash"] = 1 });

        Assert.False(result.Activated);
        Assert.Empty(result.DetectedSkills);
        Assert.Empty(result.ExtraTools);
        Assert.Equal(0, result.SkillEventCount);
    }

    [Fact]
    public void HandlesEmptyEventsArray()
    {
        var result = MetricsCollector.ExtractSkillActivation([], new Dictionary<string, int>());

        Assert.False(result.Activated);
        Assert.Empty(result.DetectedSkills);
        Assert.Empty(result.ExtraTools);
        Assert.Equal(0, result.SkillEventCount);
    }

    [Fact]
    public void HandlesEmptyBaselineToolBreakdown()
    {
        var events = new List<AgentEvent>
        {
            MakeEvent("tool.execution_start", D(("toolName", JsonValue.Create("bash")))),
        };

        var result = MetricsCollector.ExtractSkillActivation(events, new Dictionary<string, int>());

        Assert.True(result.Activated);
        Assert.Equal(["bash"], result.ExtraTools);
    }

    [Fact]
    public void DeduplicatesDetectedSkillNames()
    {
        var events = new List<AgentEvent>
        {
            MakeEvent("skill.loaded", D(("skillName", JsonValue.Create("my-skill")))),
            MakeEvent("skill.activated", D(("skillName", JsonValue.Create("my-skill")))),
            MakeEvent("skill.loaded", D(("skillName", JsonValue.Create("other-skill")))),
        };

        var result = MetricsCollector.ExtractSkillActivation(events, new Dictionary<string, int>());

        Assert.Equal(new[] { "my-skill", "other-skill" }, result.DetectedSkills);
        Assert.Equal(3, result.SkillEventCount);
    }

    [Fact]
    public void HandlesMissingSkillNameInEventsGracefully()
    {
        var events = new List<AgentEvent>
        {
            MakeEvent("skill.loaded"),
            MakeEvent("skill.loaded", D(("skillName", JsonValue.Create("")))),
        };

        var result = MetricsCollector.ExtractSkillActivation(events, new Dictionary<string, int>());

        Assert.True(result.Activated);
        Assert.Empty(result.DetectedSkills);
        Assert.Equal(2, result.SkillEventCount);
    }

    [Fact]
    public void CombinesBothHeuristicsSkillEventsAndExtraTools()
    {
        var events = new List<AgentEvent>
        {
            MakeEvent("skill.loaded", D(("skillName", JsonValue.Create("build-cache")))),
            MakeEvent("tool.execution_start", D(("toolName", JsonValue.Create("bash")))),
            MakeEvent("tool.execution_start", D(("toolName", JsonValue.Create("msbuild_diag")))),
        };

        var result = MetricsCollector.ExtractSkillActivation(events, new Dictionary<string, int> { ["bash"] = 2 });

        Assert.True(result.Activated);
        Assert.Equal(["build-cache"], result.DetectedSkills);
        Assert.Equal(["msbuild_diag"], result.ExtraTools);
        Assert.Equal(1, result.SkillEventCount);
    }

    [Fact]
    public void DoesNotCountNonSkillEventsAsSkillEvents()
    {
        var events = new List<AgentEvent>
        {
            MakeEvent("assistant.message", D(("content", JsonValue.Create("I used a skill")))),
            MakeEvent("session.idle"),
            MakeEvent("tool.execution_start", D(("toolName", JsonValue.Create("bash")))),
            MakeEvent("session.error", D(("message", JsonValue.Create("failed")))),
        };

        var result = MetricsCollector.ExtractSkillActivation(events, new Dictionary<string, int> { ["bash"] = 1 });

        Assert.False(result.Activated);
        Assert.Equal(0, result.SkillEventCount);
    }

    [Fact]
    public void DetectsSkillFromSkillInvokedEvent()
    {
        // SkillInvokedEvent has type "skill.invoked" and Data with "name" property
        var events = new List<AgentEvent>
        {
            MakeEvent("skill.invoked", D(("name", JsonValue.Create("binlog-failure-analysis")), ("path", JsonValue.Create("/skills/binlog-failure-analysis")))),
            MakeEvent("tool.execution_start", D(("toolName", JsonValue.Create("bash")))),
        };

        var result = MetricsCollector.ExtractSkillActivation(events, new Dictionary<string, int> { ["bash"] = 1 });

        Assert.True(result.Activated);
        Assert.Equal(["binlog-failure-analysis"], result.DetectedSkills);
        Assert.Equal(1, result.SkillEventCount);
    }

    // --- Targeted skill activation (targetSkillName parameter) tests ---

    [Fact]
    public void TargetSkillName_ActivatedWhenTargetSkillDetected()
    {
        var events = new List<AgentEvent>
        {
            MakeEvent("skill.invoked", D(("name", JsonValue.Create("build-perf")))),
            MakeEvent("tool.execution_start", D(("toolName", JsonValue.Create("bash")))),
        };

        var result = MetricsCollector.ExtractSkillActivation(
            events, new Dictionary<string, int> { ["bash"] = 1 }, targetSkillName: "build-perf");

        Assert.True(result.Activated);
        Assert.Equal(["build-perf"], result.DetectedSkills);
    }

    [Fact]
    public void TargetSkillName_NotActivatedWhenSiblingSkillFires()
    {
        // In a plugin run, a sibling skill fires but not the target skill
        var events = new List<AgentEvent>
        {
            MakeEvent("skill.invoked", D(("name", JsonValue.Create("sibling-skill")))),
            MakeEvent("tool.execution_start", D(("toolName", JsonValue.Create("bash")))),
        };

        var result = MetricsCollector.ExtractSkillActivation(
            events, new Dictionary<string, int> { ["bash"] = 1 }, targetSkillName: "build-perf");

        Assert.False(result.Activated);
        Assert.Equal(["sibling-skill"], result.DetectedSkills);
        Assert.Equal(1, result.SkillEventCount);
    }

    [Fact]
    public void TargetSkillName_CaseInsensitiveMatch()
    {
        var events = new List<AgentEvent>
        {
            MakeEvent("skill.invoked", D(("name", JsonValue.Create("Build-Perf")))),
        };

        var result = MetricsCollector.ExtractSkillActivation(
            events, new Dictionary<string, int>(), targetSkillName: "build-perf");

        Assert.True(result.Activated);
    }

    [Fact]
    public void TargetSkillName_NotActivatedEvenWithExtraToolsWhenNoTargetDetected()
    {
        // Extra tools present but target skill not detected — NOT activated.
        // We control the SDK; it always emits SkillInvokedEvent. Extra tools
        // alone are not proof the target skill was loaded.
        var events = new List<AgentEvent>
        {
            MakeEvent("tool.execution_start", D(("toolName", JsonValue.Create("msbuild_analyze")))),
        };

        var result = MetricsCollector.ExtractSkillActivation(
            events, new Dictionary<string, int>(), targetSkillName: "build-perf");

        Assert.False(result.Activated);
        Assert.Equal(["msbuild_analyze"], result.ExtraTools);
    }

    [Fact]
    public void TargetSkillName_ExtraToolsIgnoredWhenSiblingSkillEventsExist()
    {
        // Sibling skill fired (skill events exist) plus extra tools — NOT activated.
        // The event system works, so extra tools likely came from the sibling, not
        // the target skill. This is the false-positive scenario from plugin runs.
        var events = new List<AgentEvent>
        {
            MakeEvent("skill.invoked", D(("name", JsonValue.Create("sibling-skill")))),
            MakeEvent("tool.execution_start", D(("toolName", JsonValue.Create("view")))),
        };

        var result = MetricsCollector.ExtractSkillActivation(
            events, new Dictionary<string, int>(), targetSkillName: "nuget-trusted-publishing");

        Assert.False(result.Activated);
        Assert.Equal(["sibling-skill"], result.DetectedSkills);
        Assert.Equal(["view"], result.ExtraTools);
    }

    [Fact]
    public void TargetSkillName_NullBehavesAsOriginal()
    {
        // When targetSkillName is null, any skill event counts as activation (original behavior)
        var events = new List<AgentEvent>
        {
            MakeEvent("skill.invoked", D(("name", JsonValue.Create("sibling-skill")))),
        };

        var result = MetricsCollector.ExtractSkillActivation(
            events, new Dictionary<string, int>(), targetSkillName: null);

        Assert.True(result.Activated);
        Assert.Equal(["sibling-skill"], result.DetectedSkills);
    }

    [Fact]
    public void TargetSkillName_NotActivatedWhenNoEventsAndNoExtraTools()
    {
        var events = new List<AgentEvent>
        {
            MakeEvent("tool.execution_start", D(("toolName", JsonValue.Create("bash")))),
        };

        var result = MetricsCollector.ExtractSkillActivation(
            events, new Dictionary<string, int> { ["bash"] = 1 }, targetSkillName: "build-perf");

        Assert.False(result.Activated);
    }

    [Fact]
    public void TargetSkillName_ActivatedWhenTargetAmongMultipleSkills()
    {
        // Multiple skills fire in a plugin run, including the target
        var events = new List<AgentEvent>
        {
            MakeEvent("skill.invoked", D(("name", JsonValue.Create("sibling-skill")))),
            MakeEvent("skill.invoked", D(("name", JsonValue.Create("build-perf")))),
            MakeEvent("skill.invoked", D(("name", JsonValue.Create("another-skill")))),
        };

        var result = MetricsCollector.ExtractSkillActivation(
            events, new Dictionary<string, int>(), targetSkillName: "build-perf");

        Assert.True(result.Activated);
        Assert.Equal(3, result.SkillEventCount);
        Assert.Contains("build-perf", result.DetectedSkills);
    }
}

public class CollectMetricsTests
{
    private static AgentEvent MakeEvent(string type, Dictionary<string, JsonNode?>? data = null)
    {
        return new AgentEvent(type, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), data ?? new Dictionary<string, JsonNode?>());
    }

    private static Dictionary<string, JsonNode?> D(params (string Key, JsonNode? Value)[] entries)
    {
        var dict = new Dictionary<string, JsonNode?>();
        foreach (var (key, value) in entries)
            dict[key] = value;
        return dict;
    }

    [Fact]
    public void CountsToolCallsAndBreakdown()
    {
        var events = new List<AgentEvent>
        {
            MakeEvent("tool.execution_start", D(("toolName", JsonValue.Create("bash")))),
            MakeEvent("tool.execution_start", D(("toolName", JsonValue.Create("view")))),
            MakeEvent("tool.execution_start", D(("toolName", JsonValue.Create("bash")))),
            MakeEvent("assistant.message", D(("content", JsonValue.Create("done")))),
        };

        var result = MetricsCollector.CollectMetrics(events, "done", 1000, "/tmp/work");

        Assert.Equal(3, result.ToolCallCount);
        Assert.Equal(2, result.ToolCallBreakdown["bash"]);
        Assert.Equal(1, result.ToolCallBreakdown["view"]);
    }

    [Fact]
    public void UsesRealTokenCountsFromAssistantUsageEvents()
    {
        var events = new List<AgentEvent>
        {
            MakeEvent("assistant.usage", D(("inputTokens", JsonValue.Create(500)), ("outputTokens", JsonValue.Create(200)))),
            MakeEvent("assistant.message", D(("content", JsonValue.Create("hello world")))),
            MakeEvent("assistant.usage", D(("inputTokens", JsonValue.Create(300)), ("outputTokens", JsonValue.Create(100)))),
        };

        var result = MetricsCollector.CollectMetrics(events, "hello world", 5000, "/tmp/work");

        // Should use real token counts: (500+200) + (300+100) = 1100
        Assert.Equal(1100, result.TokenEstimate);
    }

    [Fact]
    public void FallsBackToCharEstimationWhenNoUsageEvents()
    {
        var events = new List<AgentEvent>
        {
            MakeEvent("assistant.message", D(("content", JsonValue.Create("hello world!!")))), // 13 chars -> ceil(13/4) = 4
        };

        var result = MetricsCollector.CollectMetrics(events, "hello world!!", 5000, "/tmp/work");

        Assert.Equal((int)Math.Ceiling(13.0 / 4.0), result.TokenEstimate);
    }

    [Fact]
    public void CountsTurnsFromAssistantMessageEvents()
    {
        var events = new List<AgentEvent>
        {
            MakeEvent("assistant.message", D(("content", JsonValue.Create("turn 1")))),
            MakeEvent("assistant.message", D(("content", JsonValue.Create("turn 2")))),
        };

        var result = MetricsCollector.CollectMetrics(events, "turn 2", 1000, "/tmp/work");

        Assert.Equal(2, result.TurnCount);
    }

    [Fact]
    public void CountsErrors()
    {
        var events = new List<AgentEvent>
        {
            MakeEvent("session.error", D(("message", JsonValue.Create("something went wrong")))),
            MakeEvent("runner.error", D(("message", JsonValue.Create("timeout")))),
        };

        var result = MetricsCollector.CollectMetrics(events, "", 1000, "/tmp/work");

        Assert.Equal(2, result.ErrorCount);
    }

    [Fact]
    public void PreservesWallTimeAndWorkDir()
    {
        var result = MetricsCollector.CollectMetrics([], "output", 42000, "/tmp/my-work");

        Assert.Equal(42000, result.WallTimeMs);
        Assert.Equal("/tmp/my-work", result.WorkDir);
        Assert.Equal("output", result.AgentOutput);
    }

    [Fact]
    public void FallbackTokenEstimationIncludesUserMessages()
    {
        var events = new List<AgentEvent>
        {
            MakeEvent("user.message", D(("content", JsonValue.Create("test")))), // 4 chars -> ceil(4/4) = 1
            MakeEvent("assistant.message", D(("content", JsonValue.Create("response")))), // 8 chars -> ceil(8/4) = 2
        };

        var result = MetricsCollector.CollectMetrics(events, "response", 1000, "/tmp/work");

        // Fallback estimation: ceil(4/4) + ceil(8/4) = 1 + 2 = 3
        Assert.Equal(3, result.TokenEstimate);
    }

    [Fact]
    public void SetsTimedOutToTrueWhenRunnerTimeoutEventIsPresent()
    {
        var events = new List<AgentEvent>
        {
            MakeEvent("assistant.message", D(("content", JsonValue.Create("working...")))),
            MakeEvent("runner.timeout", D(("message", JsonValue.Create("Scenario timed out after 120s")))),
        };

        var result = MetricsCollector.CollectMetrics(events, "", 120000, "/tmp/work");

        Assert.True(result.TimedOut);
        Assert.Equal(1, result.ErrorCount);
    }

    [Fact]
    public void SetsTimedOutToFalseWhenNoTimeoutEventIsPresent()
    {
        var events = new List<AgentEvent>
        {
            MakeEvent("assistant.message", D(("content", JsonValue.Create("done")))),
            MakeEvent("tool.execution_start", D(("toolName", JsonValue.Create("bash")))),
        };

        var result = MetricsCollector.CollectMetrics(events, "", 5000, "/tmp/work");

        Assert.False(result.TimedOut);
        Assert.Equal(0, result.ErrorCount);
    }

    [Fact]
    public void SetsTimedOutToFalseWhenOnlyRunnerErrorEventsArePresent()
    {
        var events = new List<AgentEvent>
        {
            MakeEvent("runner.error", D(("message", JsonValue.Create("Something went wrong")))),
        };

        var result = MetricsCollector.CollectMetrics(events, "", 3000, "/tmp/work");

        Assert.False(result.TimedOut);
        Assert.Equal(1, result.ErrorCount);
    }

    [Fact]
    public void CountsBothRunnerTimeoutAndRunnerErrorInErrorCount()
    {
        var events = new List<AgentEvent>
        {
            MakeEvent("runner.error", D(("message", JsonValue.Create("file not found")))),
            MakeEvent("runner.timeout", D(("message", JsonValue.Create("Scenario timed out after 120s")))),
        };

        var result = MetricsCollector.CollectMetrics(events, "", 120000, "/tmp/work");

        Assert.True(result.TimedOut);
        Assert.Equal(2, result.ErrorCount);
    }
}

public class ExtractSubagentActivationTests
{
    private static AgentEvent MakeEvent(string type, Dictionary<string, JsonNode?>? data = null)
    {
        return new AgentEvent(type, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), data ?? new Dictionary<string, JsonNode?>());
    }

    private static Dictionary<string, JsonNode?> D(params (string Key, JsonNode? Value)[] entries)
    {
        var dict = new Dictionary<string, JsonNode?>();
        foreach (var (key, value) in entries)
            dict[key] = value;
        return dict;
    }

    [Fact]
    public void DetectsSubagentFromStartedEvent()
    {
        var events = new List<AgentEvent>
        {
            MakeEvent("subagent.started", D(("agentName", JsonValue.Create("build-perf")), ("agentDisplayName", JsonValue.Create("Build Perf")))),
            MakeEvent("subagent.completed", D(("agentName", JsonValue.Create("build-perf")))),
        };

        var result = MetricsCollector.ExtractSubagentActivation(events);

        Assert.Equal(["build-perf"], result.InvokedAgents);
        Assert.Equal(2, result.SubagentEventCount);
    }

    [Fact]
    public void DeduplicatesAgentNames()
    {
        var events = new List<AgentEvent>
        {
            MakeEvent("subagent.started", D(("agentName", JsonValue.Create("build-perf")))),
            MakeEvent("subagent.completed", D(("agentName", JsonValue.Create("build-perf")))),
            MakeEvent("subagent.started", D(("agentName", JsonValue.Create("build-perf")))),
            MakeEvent("subagent.completed", D(("agentName", JsonValue.Create("build-perf")))),
        };

        var result = MetricsCollector.ExtractSubagentActivation(events);

        Assert.Single(result.InvokedAgents);
        Assert.Equal("build-perf", result.InvokedAgents[0]);
        Assert.Equal(4, result.SubagentEventCount);
    }

    [Fact]
    public void DetectsMultipleDistinctSubagents()
    {
        var events = new List<AgentEvent>
        {
            MakeEvent("subagent.started", D(("agentName", JsonValue.Create("build-perf")))),
            MakeEvent("subagent.started", D(("agentName", JsonValue.Create("msbuild-code-review")))),
            MakeEvent("subagent.completed", D(("agentName", JsonValue.Create("build-perf")))),
        };

        var result = MetricsCollector.ExtractSubagentActivation(events);

        Assert.Equal(2, result.InvokedAgents.Count);
        Assert.Contains("build-perf", result.InvokedAgents);
        Assert.Contains("msbuild-code-review", result.InvokedAgents);
        Assert.Equal(3, result.SubagentEventCount);
    }

    [Fact]
    public void ReturnsEmptyWhenNoSubagentEvents()
    {
        var events = new List<AgentEvent>
        {
            MakeEvent("tool.execution_start", D(("toolName", JsonValue.Create("bash")))),
            MakeEvent("assistant.message", D(("content", JsonValue.Create("done")))),
        };

        var result = MetricsCollector.ExtractSubagentActivation(events);

        Assert.Empty(result.InvokedAgents);
        Assert.Equal(0, result.SubagentEventCount);
    }

    [Fact]
    public void HandlesEmptyEventsArray()
    {
        var result = MetricsCollector.ExtractSubagentActivation([]);

        Assert.Empty(result.InvokedAgents);
        Assert.Equal(0, result.SubagentEventCount);
    }

    [Fact]
    public void HandlesSubagentEventWithEmptyName()
    {
        var events = new List<AgentEvent>
        {
            MakeEvent("subagent.started", D(("agentName", JsonValue.Create("")))),
            MakeEvent("subagent.selected", D(("agentName", JsonValue.Create("build-perf")))),
        };

        var result = MetricsCollector.ExtractSubagentActivation(events);

        Assert.Single(result.InvokedAgents);
        Assert.Equal("build-perf", result.InvokedAgents[0]);
        Assert.Equal(2, result.SubagentEventCount);
    }

    [Fact]
    public void HandlesSubagentFailedEvent()
    {
        var events = new List<AgentEvent>
        {
            MakeEvent("subagent.started", D(("agentName", JsonValue.Create("build-perf")))),
            MakeEvent("subagent.failed", D(("agentName", JsonValue.Create("build-perf")), ("error", JsonValue.Create("timeout")))),
        };

        var result = MetricsCollector.ExtractSubagentActivation(events);

        Assert.Equal(["build-perf"], result.InvokedAgents);
        Assert.Equal(2, result.SubagentEventCount);
    }

    [Fact]
    public void CaseInsensitiveDeduplication()
    {
        var events = new List<AgentEvent>
        {
            MakeEvent("subagent.started", D(("agentName", JsonValue.Create("Build-Perf")))),
            MakeEvent("subagent.completed", D(("agentName", JsonValue.Create("build-perf")))),
        };

        var result = MetricsCollector.ExtractSubagentActivation(events);

        Assert.Single(result.InvokedAgents);
        Assert.Equal(2, result.SubagentEventCount);
    }

    [Fact]
    public void IgnoresNonSubagentEvents()
    {
        var events = new List<AgentEvent>
        {
            MakeEvent("skill.invoked", D(("name", JsonValue.Create("my-skill")))),
            MakeEvent("tool.execution_start", D(("toolName", JsonValue.Create("bash")))),
            MakeEvent("assistant.message", D(("content", JsonValue.Create("done")))),
        };

        var result = MetricsCollector.ExtractSubagentActivation(events);

        Assert.Empty(result.InvokedAgents);
        Assert.Equal(0, result.SubagentEventCount);
    }
}
