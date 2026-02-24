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
}
