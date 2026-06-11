using System.Text.Json;
using SkillValidator;
using SkillValidator.Evaluate;

namespace SkillValidator.Tests;

public class BaselineStoreTests
{
    private static RunResult MakeBaseline(double overallScore = 3, string output = "baseline output") =>
        new(
            new RunMetrics
            {
                TokenEstimate = 1000,
                ToolCallCount = 4,
                ToolCallBreakdown = new Dictionary<string, int> { ["bash"] = 4 },
                AgentOutput = output,
                TaskCompleted = true,
                Events = [],
            },
            new JudgeResult([new RubricScore("Quality", overallScore, "ok")], overallScore, "fine"));

    private static EvalScenario Scenario(string name, string prompt) => new(name, prompt);

    private static string TempPath() =>
        Path.Combine(Path.GetTempPath(), $"sv-baseline-test-{Guid.NewGuid():N}.json");

    [Fact]
    public void ComputePromptSha_IsDeterministicAndPromptSensitive()
    {
        var a = BaselineStore.ComputePromptSha("do the thing");
        var b = BaselineStore.ComputePromptSha("do the thing");
        var c = BaselineStore.ComputePromptSha("do something else");

        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
        Assert.Equal(64, a.Length); // SHA-256 hex
    }

    [Fact]
    public void SaveThenLoad_RoundTripsBaselinePerScenario()
    {
        var path = TempPath();
        try
        {
            var store = BaselineStore.ForWrite("model-x");
            var s1 = Scenario("alpha", "prompt one");
            var s2 = Scenario("beta", "prompt two");
            store.Record(s1, runs: 5, MakeBaseline(overallScore: 4, output: "out-1"));
            store.Record(s2, runs: 5, MakeBaseline(overallScore: 2, output: "out-2"));
            store.Save(path);

            Assert.True(File.Exists(path));

            var loaded = BaselineStore.Load(path, "model-x");
            Assert.True(loaded.IsReuse);
            Assert.Equal(2, loaded.Count);

            var b1 = loaded.TryGetBaseline(s1);
            var b2 = loaded.TryGetBaseline(s2);
            Assert.NotNull(b1);
            Assert.NotNull(b2);
            Assert.Equal("out-1", b1!.Metrics.AgentOutput);
            Assert.Equal(4, b1.JudgeResult.OverallScore);
            Assert.Equal("out-2", b2!.Metrics.AgentOutput);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_ThrowsOnModelMismatch()
    {
        var path = TempPath();
        try
        {
            var store = BaselineStore.ForWrite("model-x");
            store.Record(Scenario("alpha", "prompt one"), runs: 3, MakeBaseline());
            store.Save(path);

            var ex = Assert.Throws<InvalidOperationException>(() => BaselineStore.Load(path, "model-y"));
            Assert.Contains("model-x", ex.Message);
            Assert.Contains("model-y", ex.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_ThrowsOnUnsupportedVersion()
    {
        var path = TempPath();
        try
        {
            var file = new BaselineFile(
                Version: BaselineStore.CurrentVersion + 1,
                Model: "model-x",
                ValidatorVersion: "9.9.9",
                CreatedAt: DateTime.UtcNow.ToString("o"),
                Scenarios: []);
            File.WriteAllText(path, JsonSerializer.Serialize(file, SkillValidatorJsonContext.Default.BaselineFile));

            var ex = Assert.Throws<InvalidOperationException>(() => BaselineStore.Load(path, "model-x"));
            Assert.Contains("unsupported version", ex.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_ThrowsWhenFileMissing()
    {
        Assert.Throws<FileNotFoundException>(() => BaselineStore.Load(TempPath(), "model-x"));
    }

    [Fact]
    public void FindMissingScenarios_ReturnsScenariosWithoutCachedBaseline()
    {
        var path = TempPath();
        try
        {
            var store = BaselineStore.ForWrite("model-x");
            var present = Scenario("alpha", "prompt one");
            store.Record(present, runs: 5, MakeBaseline());
            store.Save(path);

            var loaded = BaselineStore.Load(path, "model-x");
            var missing = loaded.FindMissingScenarios([(present, null), (Scenario("beta", "prompt two"), null)]);

            Assert.Single(missing);
            Assert.Equal("beta", missing[0]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void WriteStore_IsNotReuse()
    {
        var store = BaselineStore.ForWrite("model-x");
        Assert.False(store.IsReuse);
        Assert.Null(store.TryGetBaseline(Scenario("alpha", "prompt one")));
    }

    private static string MakeEvalDirWithFixture(string fixtureName, string fixtureContent)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"sv-baseline-fixture-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "eval.yaml"), "scenarios: []");
        File.WriteAllText(Path.Combine(dir, fixtureName), fixtureContent);
        return Path.Combine(dir, "eval.yaml");
    }

    private static EvalScenario FixtureScenario(string name, string prompt) =>
        new(name, prompt, new SetupConfig(CopyTestFiles: true));

    [Fact]
    public void ComputeTargetSha_DiffersByFixtureContentAndIsStable()
    {
        var evalA = MakeEvalDirWithFixture("build.binlog", "AAAA");
        var evalB = MakeEvalDirWithFixture("build.binlog", "BBBB");
        try
        {
            var scenario = FixtureScenario("s", "investigate build.binlog");

            var shaA1 = BaselineStore.ComputeTargetSha(scenario, evalA);
            var shaA2 = BaselineStore.ComputeTargetSha(scenario, evalA);
            var shaB = BaselineStore.ComputeTargetSha(scenario, evalB);

            Assert.Equal(shaA1, shaA2);     // stable for identical inputs
            Assert.NotEqual(shaA1, shaB);   // sensitive to fixture content
            Assert.Equal(64, shaA1.Length);

            // No setup → a stable, distinct constant.
            var noSetup = BaselineStore.ComputeTargetSha(Scenario("s", "investigate build.binlog"), evalA);
            Assert.NotEqual(shaA1, noSetup);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(evalA)!, recursive: true);
            Directory.Delete(Path.GetDirectoryName(evalB)!, recursive: true);
        }
    }

    [Fact]
    public void SamePromptDifferentFixture_DoesNotReuseBaseline()
    {
        var path = TempPath();
        var evalA = MakeEvalDirWithFixture("build.binlog", "case-A-binlog");
        var evalB = MakeEvalDirWithFixture("build.binlog", "case-B-binlog");
        try
        {
            // Two cases share an identical prompt but feed different fixtures.
            const string sharedPrompt = "The binlog is at build.binlog. What went wrong?";
            var scenarioA = FixtureScenario("case-A", sharedPrompt);
            var scenarioB = FixtureScenario("case-B", sharedPrompt);

            // Persist a baseline only for case A.
            var store = BaselineStore.ForWrite("model-x");
            store.Record(scenarioA, runs: 5, MakeBaseline(output: "A-baseline"), evalA);
            store.Save(path);

            var loaded = BaselineStore.Load(path, "model-x");

            // Case A reuses its baseline; case B must NOT (different targetSha).
            Assert.NotNull(loaded.TryGetBaseline(scenarioA, evalA));
            Assert.Equal("A-baseline", loaded.TryGetBaseline(scenarioA, evalA)!.Metrics.AgentOutput);
            Assert.Null(loaded.TryGetBaseline(scenarioB, evalB));

            // FindMissingScenarios surfaces case B by name despite the shared prompt.
            var missing = loaded.FindMissingScenarios([(scenarioA, evalA), (scenarioB, evalB)]);
            Assert.Single(missing);
            Assert.Equal("case-B", missing[0]);
        }
        finally
        {
            File.Delete(path);
            Directory.Delete(Path.GetDirectoryName(evalA)!, recursive: true);
            Directory.Delete(Path.GetDirectoryName(evalB)!, recursive: true);
        }
    }
}
