using SkillValidator.Models;
using SkillValidator.Services;

namespace SkillValidator.Tests;

public class ExtractSkillActivationTests
{
    private static AgentEvent MakeEvent(string type, Dictionary<string, object?>? data = null)
    {
        return new AgentEvent(type, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), data ?? new Dictionary<string, object?>());
    }

    [Fact]
    public void DetectsActivationFromSkillSessionEvents()
    {
        var events = new List<AgentEvent>
        {
            MakeEvent("skill.loaded", new() { ["skillName"] = "my-skill" }),
            MakeEvent("assistant.message", new() { ["content"] = "hello" }),
            MakeEvent("tool.execution_start", new() { ["toolName"] = "bash" }),
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
            MakeEvent("instruction.attached", new() { ["name"] = "build-helper" }),
            MakeEvent("tool.execution_start", new() { ["toolName"] = "read" }),
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
            MakeEvent("tool.execution_start", new() { ["toolName"] = "bash" }),
            MakeEvent("tool.execution_start", new() { ["toolName"] = "msbuild_analyze" }),
            MakeEvent("assistant.message", new() { ["content"] = "done" }),
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
            MakeEvent("tool.execution_start", new() { ["toolName"] = "bash" }),
            MakeEvent("assistant.message", new() { ["content"] = "done" }),
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
            MakeEvent("tool.execution_start", new() { ["toolName"] = "bash" }),
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
            MakeEvent("skill.loaded", new() { ["skillName"] = "my-skill" }),
            MakeEvent("skill.activated", new() { ["skillName"] = "my-skill" }),
            MakeEvent("skill.loaded", new() { ["skillName"] = "other-skill" }),
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
            MakeEvent("skill.loaded", new()),
            MakeEvent("skill.loaded", new() { ["skillName"] = "" }),
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
            MakeEvent("skill.loaded", new() { ["skillName"] = "build-cache" }),
            MakeEvent("tool.execution_start", new() { ["toolName"] = "bash" }),
            MakeEvent("tool.execution_start", new() { ["toolName"] = "msbuild_diag" }),
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
            MakeEvent("assistant.message", new() { ["content"] = "I used a skill" }),
            MakeEvent("session.idle", new()),
            MakeEvent("tool.execution_start", new() { ["toolName"] = "bash" }),
            MakeEvent("session.error", new() { ["message"] = "failed" }),
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
            MakeEvent("skill.invoked", new() { ["name"] = "binlog-failure-analysis", ["path"] = "/skills/binlog-failure-analysis" }),
            MakeEvent("tool.execution_start", new() { ["toolName"] = "bash" }),
        };

        var result = MetricsCollector.ExtractSkillActivation(events, new Dictionary<string, int> { ["bash"] = 1 });

        Assert.True(result.Activated);
        Assert.Equal(["binlog-failure-analysis"], result.DetectedSkills);
        Assert.Equal(1, result.SkillEventCount);
    }
}

public class CollectMetricsTests
{
    private static AgentEvent MakeEvent(string type, Dictionary<string, object?>? data = null)
    {
        return new AgentEvent(type, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), data ?? new Dictionary<string, object?>());
    }

    [Fact]
    public void CountsToolCallsAndBreakdown()
    {
        var events = new List<AgentEvent>
        {
            MakeEvent("tool.execution_start", new() { ["toolName"] = "bash" }),
            MakeEvent("tool.execution_start", new() { ["toolName"] = "view" }),
            MakeEvent("tool.execution_start", new() { ["toolName"] = "bash" }),
            MakeEvent("assistant.message", new() { ["content"] = "done" }),
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
            MakeEvent("assistant.usage", new() { ["inputTokens"] = 500, ["outputTokens"] = 200 }),
            MakeEvent("assistant.message", new() { ["content"] = "hello world" }),
            MakeEvent("assistant.usage", new() { ["inputTokens"] = 300, ["outputTokens"] = 100 }),
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
            MakeEvent("assistant.message", new() { ["content"] = "hello world!!" }), // 13 chars -> ceil(13/4) = 4
        };

        var result = MetricsCollector.CollectMetrics(events, "hello world!!", 5000, "/tmp/work");

        Assert.Equal((int)Math.Ceiling(13.0 / 4.0), result.TokenEstimate);
    }

    [Fact]
    public void CountsTurnsFromAssistantMessageEvents()
    {
        var events = new List<AgentEvent>
        {
            MakeEvent("assistant.message", new() { ["content"] = "turn 1" }),
            MakeEvent("assistant.message", new() { ["content"] = "turn 2" }),
        };

        var result = MetricsCollector.CollectMetrics(events, "turn 2", 1000, "/tmp/work");

        Assert.Equal(2, result.TurnCount);
    }

    [Fact]
    public void CountsErrors()
    {
        var events = new List<AgentEvent>
        {
            MakeEvent("session.error", new() { ["message"] = "something went wrong" }),
            MakeEvent("runner.error", new() { ["message"] = "timeout" }),
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
            MakeEvent("user.message", new() { ["content"] = "test" }), // 4 chars -> ceil(4/4) = 1
            MakeEvent("assistant.message", new() { ["content"] = "response" }), // 8 chars -> ceil(8/4) = 2
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
            MakeEvent("assistant.message", new() { ["content"] = "working..." }),
            MakeEvent("runner.timeout", new() { ["message"] = "Scenario timed out after 120s" }),
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
            MakeEvent("assistant.message", new() { ["content"] = "done" }),
            MakeEvent("tool.execution_start", new() { ["toolName"] = "bash" }),
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
            MakeEvent("runner.error", new() { ["message"] = "Something went wrong" }),
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
            MakeEvent("runner.error", new() { ["message"] = "file not found" }),
            MakeEvent("runner.timeout", new() { ["message"] = "Scenario timed out after 120s" }),
        };

        var result = MetricsCollector.CollectMetrics(events, "", 120000, "/tmp/work");

        Assert.True(result.TimedOut);
        Assert.Equal(2, result.ErrorCount);
    }
}
