using SkillValidator.Shared;
using SkillValidator.Evaluate;

namespace SkillValidator.Tests;

[TestClass]
public class WorkflowDiscoveryTests
{
    private sealed class PackageFixture : IDisposable
    {
        internal string Root { get; } = Path.Combine(Path.GetTempPath(), $"workflow-eval-{Guid.NewGuid():N}");
        internal string Package => Path.Combine(Root, "agentic-workflows", "demo");
        internal string Manifest => Path.Combine(Package, "aw.yml");

        internal PackageFixture()
        {
            Write("aw.yml", """
                manifest-version: "1"
                name: Example
                description: Reviews changes
                includes:
                  - workflows/demo.md
                  - workflows/shared.md
                  - agents/demo.agent.md
                """);
            Write("workflows/demo.md", "---\non: workflow_dispatch\nimports:\n  - shared.md\n---\nMain prompt\n");
            Write("workflows/shared.md", "---\npermissions:\n  contents: read\n---\nImported prompt\n");
            Write("agents/demo.agent.md", "---\nname: demo\ndescription: Analyst\n---\nAnalyst prompt\n");
        }

        internal void Write(string relative, string content)
        {
            var path = Path.Combine(Package, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }

    [TestMethod]
    public async Task LoadsRealImportsResourcesAndDistinctPackageAgent()
    {
        using var fixture = new PackageFixture();
        var workflow = await WorkflowDiscovery.Load(fixture.Manifest);
        Assert.AreEqual("demo", workflow.Name);
        Assert.AreEqual("workflow.demo", workflow.Agent.Name);
        StringAssert.Contains(workflow.Agent.AgentMdContent, "Imported prompt");
        StringAssert.Contains(workflow.Agent.AgentMdContent, "Main prompt");
        Assert.IsFalse(workflow.Agent.AgentMdContent.Contains("contents: read"));
        Assert.AreEqual("demo", workflow.Agents.Single().Name);
        Assert.IsTrue(workflow.Resources.ContainsKey(".github/agents/demo.agent.md"));
    }

    [TestMethod]
    [DataRow("build-failure-analysis")]
    [DataRow("msbuild-quality-review")]
    [DataRow("test-failure-analysis")]
    [DataRow("unskip-closed-tests")]
    public async Task ShippingPackagesLoadThroughNativeEvaluator(string package)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !Directory.Exists(Path.Combine(root.FullName, "agentic-workflows")))
            root = root.Parent;
        Assert.IsNotNull(root, "Repository package sources must be available to integration tests");
        var workflow = await WorkflowDiscovery.Load(Path.Combine(root.FullName, "agentic-workflows", package, "aw.yml"));
        Assert.AreEqual(package, workflow.Name);
        Assert.IsTrue(workflow.Resources.Count >= 3);
        Assert.IsTrue(workflow.Agent.AgentMdContent.Length > 100);
        Assert.IsTrue(workflow.Agents.Count > 0);
    }

    [TestMethod]
    public async Task HashIncludesSharedPromptAndResources()
    {
        using var fixture = new PackageFixture();
        var before = await WorkflowDiscovery.Load(fixture.Manifest);
        fixture.Write("workflows/shared.md", "---\n---\nChanged prompt\n");
        var after = await WorkflowDiscovery.Load(fixture.Manifest);
        Assert.AreNotEqual(before.ContentSha, after.ContentSha);
    }

    [TestMethod]
    public async Task MissingImportFailsInsteadOfEvaluatingOnlyMainPrompt()
    {
        using var fixture = new PackageFixture();
        fixture.Write("workflows/demo.md", "---\non: workflow_dispatch\nimports:\n  - missing.md\n---\nMain\n");
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => WorkflowDiscovery.Load(fixture.Manifest));
    }

    [TestMethod]
    public async Task CyclicImportsFail()
    {
        using var fixture = new PackageFixture();
        fixture.Write("workflows/shared.md", "---\nimports:\n  - demo.md\n---\nShared\n");
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => WorkflowDiscovery.Load(fixture.Manifest));
    }

    [TestMethod]
    public async Task TraversalAndDuplicateResourcesFail()
    {
        using var fixture = new PackageFixture();
        fixture.Write("aw.yml", "manifest-version: '1'\nincludes:\n  - ../outside.md\n");
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => WorkflowDiscovery.Load(fixture.Manifest));
        fixture.Write("aw.yml", "manifest-version: '1'\nincludes:\n  - workflows/demo.md\n  - workflows/demo.md\n");
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => WorkflowDiscovery.Load(fixture.Manifest));
    }

    [TestMethod]
    public async Task UnsupportedManifestVersionFailsExplicitly()
    {
        using var fixture = new PackageFixture();
        fixture.Write("aw.yml", "manifest-version: '2'\nincludes:\n  - workflows/demo.md\n");
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => WorkflowDiscovery.Load(fixture.Manifest));
    }

    [TestMethod]
    public async Task MultipleOrMissingEntryWorkflowsFail()
    {
        using var fixture = new PackageFixture();
        fixture.Write("workflows/shared.md", "---\non: workflow_dispatch\n---\nShared\n");
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => WorkflowDiscovery.Load(fixture.Manifest));
        fixture.Write("workflows/shared.md", "---\n---\nShared\n");
        fixture.Write("workflows/demo.md", "---\n---\nMain\n");
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => WorkflowDiscovery.Load(fixture.Manifest));
    }

    [TestMethod]
    public async Task StagesInstalledLayoutAndRejectsFixtureOverwrite()
    {
        using var fixture = new PackageFixture();
        var workflow = await WorkflowDiscovery.Load(fixture.Manifest);
        var consumer = Path.Combine(fixture.Root, "consumer");
        Directory.CreateDirectory(consumer);
        WorkflowDiscovery.StageResources(workflow, consumer);
        var installed = Path.Combine(consumer, ".github", "agents", "demo.agent.md");
        Assert.IsTrue(File.Exists(installed));
        Assert.ThrowsExactly<InvalidOperationException>(() => WorkflowDiscovery.StageResources(workflow, consumer));
    }

    [TestMethod]
    public async Task AutoInstallsRootRelativeGrader()
    {
        using var fixture = new PackageFixture();
        var graderPath = Path.Combine(fixture.Root, ".github", "graders", "check.sh");
        Directory.CreateDirectory(Path.GetDirectoryName(graderPath)!);
        File.WriteAllText(graderPath, "#!/bin/sh\nexit 0\n");
        fixture.Write("workflows/demo.md", """
            ---
            on: workflow_dispatch
            graders:
              operational-value:
                run: .github/graders/check.sh
            ---
            Main
            """);
        var workflow = await WorkflowDiscovery.Load(fixture.Manifest);
        Assert.IsTrue(workflow.Resources.ContainsKey(".github/graders/check.sh"));
    }

    [TestMethod]
    public async Task LocalGraderRemainsRelativeToDeclaringWorkflow()
    {
        using var fixture = new PackageFixture();
        fixture.Write("aw.yml", "manifest-version: '1'\nincludes:\n  - workflows/nested/main.md\n");
        fixture.Write("workflows/nested/main.md",
            "---\non: workflow_dispatch\ngraders:\n  operational-value:\n    run: ./check.sh\n---\nMain\n");
        fixture.Write("workflows/nested/check.sh", "#!/bin/sh\nexit 0\n");
        var workflow = await WorkflowDiscovery.Load(fixture.Manifest);
        Assert.IsTrue(workflow.Resources.ContainsKey(".github/workflows/nested/check.sh"));
        Assert.IsFalse(workflow.Resources.ContainsKey(".github/workflows/check.sh"));
    }

    [TestMethod]
    public void ContextIsExplicitAndMissingValuesFail()
    {
        var root = Path.Combine(Path.GetTempPath(), $"workflow-context-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "workflow-context.json"), """{"github.repository":"owner/repo"}""");
            Assert.AreEqual("Review owner/repo",
                WorkflowDiscovery.RenderPrompt("Review ${{ github.repository }}", root));
            Assert.ThrowsExactly<InvalidOperationException>(() =>
                WorkflowDiscovery.RenderPrompt("Review ${{ github.event.pull_request.base.sha }}", root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void RejudgePreservesWorkflowIdentityAndPersonaActivation()
    {
        var run = new RunResult(new RunMetrics { TaskCompleted = true }, new JudgeResult([], 5, "done"));
        var comparison = new ScenarioComparison
        {
            ScenarioName = "Review",
            Baseline = run,
            SkilledIsolated = run,
            SkilledPlugin = run,
            ImprovementScore = 0.5,
            Breakdown = new MetricBreakdown(0, 0, 0, 0, 0, 0, 0),
            SubagentActivationIsolated = new SubagentActivationInfo(["workflow.demo"], 1),
            SubagentActivationPlugin = new SubagentActivationInfo(["workflow.demo"], 1),
            ExpectActivation = true,
        };
        var verdict = RejudgeCommand.ComputeRejudgeVerdict(
            "workflow.demo", Path.Combine("agentic-workflows", "demo", "aw.yml"),
            [comparison], true, 0.1, true, 0.95);
        Assert.AreEqual("demo", verdict.SkillName);
        Assert.AreEqual("workflow", verdict.SkillKind);
        Assert.IsFalse(verdict.SkillNotActivated);
    }

    [TestMethod]
    public void ProposedOutputIsRetainedAsJudgeAndPersistenceEvidence()
    {
        using var fixture = new PackageFixture();
        var proposal = """{"action":"noop","reason":"No applicable change"}""";
        File.WriteAllText(Path.Combine(fixture.Root, "result.json"), proposal);
        var metrics = new RunMetrics { WorkDir = fixture.Root, AgentOutput = "Done" };
        AgentRunner.CaptureWorkflowProposal(metrics);
        Assert.AreEqual(proposal, metrics.WorkflowProposalJson);
        StringAssert.Contains(metrics.AgentOutput, proposal);
        Assert.AreEqual(proposal, metrics.Clone().WorkflowProposalJson);
    }

    [TestMethod]
    public void MissingOrMalformedProposalRemainsCompletionEvidence()
    {
        using var fixture = new PackageFixture();
        var metrics = new RunMetrics { WorkDir = fixture.Root, AgentOutput = "No output" };
        AgentRunner.CaptureWorkflowProposal(metrics);
        Assert.IsNull(metrics.WorkflowProposalJson);
        File.WriteAllText(Path.Combine(fixture.Root, "result.json"), "{malformed");
        AgentRunner.CaptureWorkflowProposal(metrics);
        Assert.AreEqual("{malformed", metrics.WorkflowProposalJson);
    }

    [TestMethod]
    public void ProposalEvidenceLimitUsesExactByteThreshold()
    {
        using var fixture = new PackageFixture();
        var path = Path.Combine(fixture.Root, "result.json");
        var metrics = new RunMetrics { WorkDir = fixture.Root };
        File.WriteAllText(path, new string('a', 1_048_576));
        AgentRunner.CaptureWorkflowProposal(metrics);
        Assert.AreEqual(1_048_576, metrics.WorkflowProposalJson!.Length);
        File.AppendAllText(path, "a");
        Assert.ThrowsExactly<InvalidOperationException>(() => AgentRunner.CaptureWorkflowProposal(metrics));
    }
}
