using SkillValidator.Evaluate;

namespace SkillValidator.Tests;

public class EvaluateCommandTests
{
    // These options are judging-dependent. Under --no-judge they cannot run, so Run must reject
    // them up front (before any model/network call) rather than silently ignoring them. Each case
    // short-circuits at the early validation, so no agent client is ever created.

    [Fact]
    public async Task Run_RejectsNoJudgeWithNoiseSkillsDir()
    {
        var config = new ValidatorConfig { NoJudge = true, NoiseSkillsDir = "some/dir" };

        var exitCode = await EvaluateCommand.Run(config, TestContext.Current.CancellationToken);

        Assert.Equal(1, exitCode);
    }

    [Fact]
    public async Task Run_RejectsNoJudgeWithOverfittingFix()
    {
        var config = new ValidatorConfig { NoJudge = true, OverfittingFix = true };

        var exitCode = await EvaluateCommand.Run(config, TestContext.Current.CancellationToken);

        Assert.Equal(1, exitCode);
    }

    [Fact]
    public async Task Run_RejectsNoJudgeWithBaselineFrom()
    {
        var config = new ValidatorConfig { NoJudge = true, BaselineFrom = "baseline.json" };

        var exitCode = await EvaluateCommand.Run(config, TestContext.Current.CancellationToken);

        Assert.Equal(1, exitCode);
    }

    [Fact]
    public async Task ResolveAdditionalAgentsIncludesTransitiveDeclaredDependencies()
    {
        var pluginRoot = Path.Combine(Path.GetTempPath(), $"agent-deps-{Guid.NewGuid():N}");
        var agentsDir = Path.Combine(pluginRoot, "agents");
        Directory.CreateDirectory(agentsDir);
        try
        {
            File.WriteAllText(Path.Combine(pluginRoot, "plugin.json"), """
                {
                  "name": "demo",
                  "version": "1.0.0",
                  "description": "Demo",
                  "agents": ["./agents/"]
                }
                """);
            File.WriteAllText(Path.Combine(agentsDir, "coordinator.agent.md"), """
                ---
                name: coordinator
                description: Coordinates work.
                agents:
                  - worker
                ---
                Coordinate.
                """);
            File.WriteAllText(Path.Combine(agentsDir, "worker.agent.md"), """
                ---
                name: worker
                description: Does work.
                ---
                Work.
                """);

            var agents = await EvaluateCommand.ResolveAdditionalAgents(
                ["coordinator"], pluginRoot);

            Assert.Equal(
                ["coordinator", "worker"],
                agents!.Select(agent => agent.Name));
        }
        finally
        {
            Directory.Delete(pluginRoot, true);
        }
    }

    [Fact]
    public async Task ResolveAdditionalSkillsAcceptsTrackedStyleCrossPluginPath()
    {
        var repoRoot = Path.Combine(Path.GetTempPath(), $"skill-deps-{Guid.NewGuid():N}");
        var targetPlugin = Path.Combine(repoRoot, "plugins", "target");
        var dependency = Path.Combine(repoRoot, "plugins", "shared", "skills", "helper");
        var evalDir = Path.Combine(repoRoot, "tests", "target", "agent.router");
        Directory.CreateDirectory(targetPlugin);
        Directory.CreateDirectory(dependency);
        Directory.CreateDirectory(evalDir);
        try
        {
            File.WriteAllText(Path.Combine(targetPlugin, "plugin.json"), """
                {
                  "name": "target",
                  "version": "1.0.0",
                  "description": "Target",
                  "skills": ["./skills/"]
                }
                """);
            File.WriteAllText(Path.Combine(dependency, "SKILL.md"), """
                ---
                name: helper
                description: Helper skill.
                ---
                Help.
                """);
            var evalPath = Path.Combine(evalDir, "eval.yaml");
            File.WriteAllText(evalPath, "stimuli: []");

            var skills = await EvaluateCommand.ResolveAdditionalSkills(
                ["../../plugins/shared/skills/helper"], targetPlugin, evalPath);

            Assert.Equal("helper", Assert.Single(skills!).Name);
        }
        finally
        {
            Directory.Delete(repoRoot, true);
        }
    }
}
