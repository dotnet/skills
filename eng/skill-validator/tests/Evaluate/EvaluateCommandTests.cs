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
    public async Task ResolveAdditionalAgentsAcceptsAgentFilePath()
    {
        var repoRoot = Path.Combine(Path.GetTempPath(), $"agent-file-dep-{Guid.NewGuid():N}");
        var pluginRoot = Path.Combine(repoRoot, "plugins", "demo");
        var agentsDir = Path.Combine(pluginRoot, "agents");
        var evalDir = Path.Combine(repoRoot, "tests", "demo", "agent.router");
        Directory.CreateDirectory(agentsDir);
        Directory.CreateDirectory(evalDir);
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
            File.WriteAllText(Path.Combine(agentsDir, "helper.agent.md"), """
                ---
                name: helper
                description: Helper agent.
                ---
                Help.
                """);
            var evalPath = Path.Combine(evalDir, "eval.yaml");
            File.WriteAllText(evalPath, "stimuli: []");

            var agents = await EvaluateCommand.ResolveAdditionalAgents(
                ["../../plugins/demo/agents/helper.agent.md"], pluginRoot, evalPath);

            Assert.Equal("helper", Assert.Single(agents!).Name);
        }
        finally
        {
            Directory.Delete(repoRoot, true);
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

    [Fact]
    public async Task ResolveAdditionalSkillsRejectsLinkedDirectory()
    {
        var repoRoot = Path.Combine(Path.GetTempPath(), $"skill-dir-link-{Guid.NewGuid():N}");
        var targetPlugin = Path.Combine(repoRoot, "plugins", "target");
        var sharedPlugin = Path.Combine(repoRoot, "plugins", "shared");
        var outsideSkill = Path.Combine(repoRoot, "outside", "helper");
        var evalDir = Path.Combine(repoRoot, "tests", "target", "agent.router");
        Directory.CreateDirectory(targetPlugin);
        Directory.CreateDirectory(sharedPlugin);
        Directory.CreateDirectory(outsideSkill);
        Directory.CreateDirectory(evalDir);
        File.WriteAllText(Path.Combine(targetPlugin, "plugin.json"), """
            {"name":"target","version":"1.0.0","description":"Target","skills":["./skills/"]}
            """);
        File.WriteAllText(Path.Combine(outsideSkill, "SKILL.md"), """
            ---
            name: helper
            description: External helper.
            ---
            Help.
            """);
        Directory.CreateSymbolicLink(Path.Combine(sharedPlugin, "linked"), outsideSkill);
        var evalPath = Path.Combine(evalDir, "eval.yaml");
        File.WriteAllText(evalPath, "stimuli: []");
        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                EvaluateCommand.ResolveAdditionalSkills(
                    ["../../plugins/shared/linked"], targetPlugin, evalPath));
        }
        finally
        {
            Directory.Delete(repoRoot, true);
        }
    }

    [Fact]
    public async Task ResolveAdditionalSkillsRejectsLinkedSkillFile()
    {
        var repoRoot = Path.Combine(Path.GetTempPath(), $"skill-file-link-{Guid.NewGuid():N}");
        var targetPlugin = Path.Combine(repoRoot, "plugins", "target");
        var dependency = Path.Combine(repoRoot, "plugins", "shared", "skills", "helper");
        var outsideFile = Path.Combine(repoRoot, "outside", "SKILL.md");
        var evalDir = Path.Combine(repoRoot, "tests", "target", "agent.router");
        Directory.CreateDirectory(targetPlugin);
        Directory.CreateDirectory(dependency);
        Directory.CreateDirectory(Path.GetDirectoryName(outsideFile)!);
        Directory.CreateDirectory(evalDir);
        File.WriteAllText(Path.Combine(targetPlugin, "plugin.json"), """
            {"name":"target","version":"1.0.0","description":"Target","skills":["./skills/"]}
            """);
        File.WriteAllText(outsideFile, """
            ---
            name: helper
            description: External helper.
            ---
            Help.
            """);
        File.CreateSymbolicLink(Path.Combine(dependency, "SKILL.md"), outsideFile);
        var evalPath = Path.Combine(evalDir, "eval.yaml");
        File.WriteAllText(evalPath, "stimuli: []");
        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                EvaluateCommand.ResolveAdditionalSkills(
                    ["../../plugins/shared/skills/helper"], targetPlugin, evalPath));
        }
        finally
        {
            Directory.Delete(repoRoot, true);
        }
    }

    [Fact]
    public async Task ResolveAdditionalAgentsRejectsLinkedAgentFile()
    {
        var repoRoot = Path.Combine(Path.GetTempPath(), $"agent-dep-link-{Guid.NewGuid():N}");
        var pluginRoot = Path.Combine(repoRoot, "plugins", "demo");
        var agentsDir = Path.Combine(pluginRoot, "agents");
        var outsideFile = Path.Combine(repoRoot, "outside", "helper.agent.md");
        var evalDir = Path.Combine(repoRoot, "tests", "demo", "agent.router");
        Directory.CreateDirectory(agentsDir);
        Directory.CreateDirectory(Path.GetDirectoryName(outsideFile)!);
        Directory.CreateDirectory(evalDir);
        File.WriteAllText(Path.Combine(pluginRoot, "plugin.json"), """
            {"name":"demo","version":"1.0.0","description":"Demo","agents":["./agents/"]}
            """);
        File.WriteAllText(outsideFile, """
            ---
            name: helper
            description: External helper.
            ---
            Help.
            """);
        File.CreateSymbolicLink(Path.Combine(agentsDir, "helper.agent.md"), outsideFile);
        var evalPath = Path.Combine(evalDir, "eval.yaml");
        File.WriteAllText(evalPath, "stimuli: []");
        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                EvaluateCommand.ResolveAdditionalAgents(
                    ["../../plugins/demo/agents/helper.agent.md"], pluginRoot, evalPath));
        }
        finally
        {
            Directory.Delete(repoRoot, true);
        }
    }
}
