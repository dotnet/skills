using System.Text.Json;
using SkillValidator;
using SkillValidator.Evaluate;

namespace SkillValidator.Tests;

[TestClass]
[DoNotParallelize]
public class BaselineStoreTests
{
    private const string Model = "model-x";
    private const string Judge = "judge-x";

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

    [TestMethod]
    public void ComputePromptSha_IsDeterministicAndPromptSensitive()
    {
        var a = BaselineStore.ComputePromptSha("do the thing");
        var b = BaselineStore.ComputePromptSha("do the thing");
        var c = BaselineStore.ComputePromptSha("do something else");

        Assert.AreEqual(a, b);
        Assert.AreNotEqual(a, c);
        Assert.AreEqual(64, a.Length); // SHA-256 hex
    }

    [TestMethod]
    public void SaveThenLoad_RoundTripsBaselinePerScenario()
    {
        var path = TempPath();
        try
        {
            var store = BaselineStore.ForWrite(Model, Judge);
            var s1 = Scenario("alpha", "prompt one");
            var s2 = Scenario("beta", "prompt two");
            store.Record(s1, runs: 5, MakeBaseline(overallScore: 4, output: "out-1"));
            store.Record(s2, runs: 5, MakeBaseline(overallScore: 2, output: "out-2"));
            store.Save(path);

            Assert.IsTrue(File.Exists(path));

            var loaded = BaselineStore.Load(path, Model, Judge);
            Assert.IsTrue(loaded.IsReuse);
            Assert.AreEqual(2, loaded.Count);

            var b1 = loaded.TryGetBaseline(s1);
            var b2 = loaded.TryGetBaseline(s2);
            Assert.IsNotNull(b1);
            Assert.IsNotNull(b2);
            Assert.AreEqual("out-1", b1!.Metrics.AgentOutput);
            Assert.AreEqual(4, b1.JudgeResult.OverallScore);
            Assert.AreEqual("out-2", b2!.Metrics.AgentOutput);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void ComputeScenarioKey_IsDeterministicAndSensitive()
    {
        var a = BaselineStore.ComputeScenarioKey(Scenario("s", "prompt one"), null);
        var a2 = BaselineStore.ComputeScenarioKey(Scenario("s", "prompt one"), null);
        var diffPrompt = BaselineStore.ComputeScenarioKey(Scenario("s", "prompt two"), null);
        var diffTarget = BaselineStore.ComputeScenarioKey(Scenario("s", "prompt one") with { Rubric = ["x"] }, null);

        Assert.AreEqual(a, a2);                  // stable for identical inputs
        Assert.AreNotEqual(a, diffPrompt);       // sensitive to prompt (prompt SHA)
        Assert.AreNotEqual(a, diffTarget);       // sensitive to evaluation criteria (target SHA)

        // The key embeds both the prompt SHA and the target SHA.
        Assert.Contains(BaselineStore.ComputePromptSha("prompt one"), a);
        Assert.Contains(BaselineStore.ComputeTargetSha(Scenario("s", "prompt one"), null), a);
    }

    [TestMethod]
    public void ComputeScenarioKeyCached_MatchesStaticAndIsStableAcrossCalls()
    {
        var cache = BaselineStore.ForKeyCache();
        var scenario = Scenario("s", "prompt one");

        var expected = BaselineStore.ComputeScenarioKey(scenario, null);
        var first = cache.ComputeScenarioKeyCached(scenario, null);
        var second = cache.ComputeScenarioKeyCached(scenario, null);

        Assert.AreEqual(expected, first);   // cached variant equals the canonical static computation
        Assert.AreEqual(first, second);     // repeated lookups (memoized) stay identical
    }

    [TestMethod]
    public void Load_ThrowsOnModelMismatch()
    {
        var path = TempPath();
        try
        {
            var store = BaselineStore.ForWrite(Model, Judge);
            store.Record(Scenario("alpha", "prompt one"), runs: 3, MakeBaseline());
            store.Save(path);

            var ex = Assert.ThrowsExactly<InvalidOperationException>(() => BaselineStore.Load(path, "model-y", Judge));
            Assert.Contains(Model, ex.Message);
            Assert.Contains("model-y", ex.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void Load_ThrowsOnJudgeModelMismatch()
    {
        var path = TempPath();
        try
        {
            var store = BaselineStore.ForWrite(Model, Judge);
            store.Record(Scenario("alpha", "prompt one"), runs: 3, MakeBaseline());
            store.Save(path);

            var ex = Assert.ThrowsExactly<InvalidOperationException>(() => BaselineStore.Load(path, Model, "judge-y"));
            Assert.Contains(Judge, ex.Message);
            Assert.Contains("judge-y", ex.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void Load_ThrowsOnUnsupportedVersion()
    {
        var path = TempPath();
        try
        {
            var file = new BaselineFile(
                Version: BaselineStore.CurrentVersion + 1,
                Model: Model,
                JudgeModel: Judge,
                ValidatorVersion: "9.9.9",
                CreatedAt: DateTime.UtcNow.ToString("o"),
                Scenarios: []);
            File.WriteAllText(path, JsonSerializer.Serialize(file, SkillValidatorJsonContext.Default.BaselineFile));

            var ex = Assert.ThrowsExactly<InvalidOperationException>(() => BaselineStore.Load(path, Model, Judge));
            Assert.Contains("unsupported version", ex.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void Load_ThrowsWhenFileMissing()
    {
        Assert.ThrowsExactly<FileNotFoundException>(() => BaselineStore.Load(TempPath(), Model, Judge));
    }

    [TestMethod]
    public void FindMissingScenarios_ReturnsScenariosWithoutCachedBaseline()
    {
        var path = TempPath();
        try
        {
            var store = BaselineStore.ForWrite(Model, Judge);
            var present = Scenario("alpha", "prompt one");
            store.Record(present, runs: 5, MakeBaseline());
            store.Save(path);

            var loaded = BaselineStore.Load(path, Model, Judge);
            var missing = loaded.FindMissingScenarios([(present, null), (Scenario("beta", "prompt two"), null)]);

            Assert.ContainsSingle(missing);
            Assert.StartsWith("beta", missing[0]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void WriteStore_IsNotReuse()
    {
        var store = BaselineStore.ForWrite(Model, Judge);
        Assert.IsFalse(store.IsReuse);
        Assert.IsNull(store.TryGetBaseline(Scenario("alpha", "prompt one")));
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

    [TestMethod]
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

            Assert.AreEqual(shaA1, shaA2);     // stable for identical inputs
            Assert.AreNotEqual(shaA1, shaB);   // sensitive to fixture content
            Assert.AreEqual(64, shaA1.Length);

            // No setup → a stable, distinct constant.
            var noSetup = BaselineStore.ComputeTargetSha(Scenario("s", "investigate build.binlog"), evalA);
            Assert.AreNotEqual(shaA1, noSetup);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(evalA)!, recursive: true);
            Directory.Delete(Path.GetDirectoryName(evalB)!, recursive: true);
        }
    }

    [TestMethod]
    public void ComputeTargetSha_DiffersByEvaluationCriteria()
    {
        const string prompt = "investigate the failure";
        var baseScenario = Scenario("s", prompt);
        var withRubric = baseScenario with { Rubric = ["Did it find the root cause?"] };
        var withAssertion = baseScenario with { Assertions = [new Assertion(AssertionType.OutputContains, Value: "error")] };
        var withTurns = baseScenario with { MaxTurns = 5 };
        var withExpectTools = baseScenario with { ExpectTools = ["bash"] };

        var shaBase = BaselineStore.ComputeTargetSha(baseScenario, null);

        // Each criterion that shapes the cached result must change the identity.
        Assert.AreNotEqual(shaBase, BaselineStore.ComputeTargetSha(withRubric, null));
        Assert.AreNotEqual(shaBase, BaselineStore.ComputeTargetSha(withAssertion, null));
        Assert.AreNotEqual(shaBase, BaselineStore.ComputeTargetSha(withTurns, null));
        Assert.AreNotEqual(shaBase, BaselineStore.ComputeTargetSha(withExpectTools, null));

        // Same criteria → stable identity.
        Assert.AreEqual(
            BaselineStore.ComputeTargetSha(withRubric, null),
            BaselineStore.ComputeTargetSha(baseScenario with { Rubric = ["Did it find the root cause?"] }, null));
    }

    [TestMethod]
    public void Record_IsFirstWriterWins_ForSameScenarioIdentity()
    {
        var path = TempPath();
        try
        {
            var store = BaselineStore.ForWrite(Model, Judge);
            var scenario = Scenario("alpha", "prompt one");

            // Same identity recorded twice (e.g. two parallel targets sharing a scenario)
            // with differing run-to-run results: the first record must win so --baseline-out
            // is deterministic regardless of completion order.
            store.Record(scenario, runs: 5, MakeBaseline(output: "first"));
            store.Record(scenario, runs: 5, MakeBaseline(output: "second"));

            Assert.AreEqual(1, store.Count);
            store.Save(path);
            var loaded = BaselineStore.Load(path, Model, Judge);
            Assert.AreEqual("first", loaded.TryGetBaseline(scenario)!.Metrics.AgentOutput);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void ComputeTargetSha_IncludesNestedFixtureFiles()
    {
        // copy_test_files copies subdirectories recursively, so nested fixture content
        // must participate in the target identity (mirrors AgentRunner.CopyDirectory).
        var evalPath = MakeEvalDirWithFixture("top.txt", "top");
        var evalDir = Path.GetDirectoryName(evalPath)!;
        var nestedDir = Path.Combine(evalDir, "sub");
        Directory.CreateDirectory(nestedDir);
        var nestedFile = Path.Combine(nestedDir, "data.bin");
        File.WriteAllText(nestedFile, "v1");
        try
        {
            var scenario = FixtureScenario("s", "investigate");
            var before = BaselineStore.ComputeTargetSha(scenario, evalPath);

            File.WriteAllText(nestedFile, "v2");
            var after = BaselineStore.ComputeTargetSha(scenario, evalPath);

            Assert.AreNotEqual(before, after); // nested file change invalidates reuse
        }
        finally
        {
            Directory.Delete(evalDir, recursive: true);
        }
    }

    [TestMethod]
    public void ComputeTargetSha_HashesFixtures_WhenEvalPathIsBareFilename()
    {
        // A bare filename (no directory component) must still hash sibling fixtures:
        // Path.GetDirectoryName returns "" for "eval.yaml", so without normalization
        // fixture hashing is silently skipped and distinct fixtures collide.
        var evalPath = MakeEvalDirWithFixture("build.binlog", "AAAA");
        var evalDir = Path.GetDirectoryName(evalPath)!;
        var originalCwd = Directory.GetCurrentDirectory();
        try
        {
            Directory.SetCurrentDirectory(evalDir);
            var scenario = FixtureScenario("s", "investigate build.binlog");

            var shaA = BaselineStore.ComputeTargetSha(scenario, "eval.yaml");
            File.WriteAllText(Path.Combine(evalDir, "build.binlog"), "BBBB");
            var shaB = BaselineStore.ComputeTargetSha(scenario, "eval.yaml");

            Assert.AreNotEqual(shaA, shaB); // fixture content participates in identity
        }
        finally
        {
            Directory.SetCurrentDirectory(originalCwd);
            Directory.Delete(evalDir, recursive: true);
        }
    }

    [TestMethod]
    public void ComputeTargetSha_IncludesExplicitDirectorySourceContents()
    {
        var root = Path.Combine(Path.GetTempPath(), $"sv-explicit-dir-{Guid.NewGuid():N}");
        var evalDir = Path.Combine(root, "tests", "demo", "agent.router");
        var sourceDir = Path.Combine(evalDir, "fixtures", "project");
        Directory.CreateDirectory(Path.Combine(sourceDir, "nested"));
        var evalPath = Path.Combine(evalDir, "eval.yaml");
        File.WriteAllText(evalPath, "stimuli: []");
        var nestedFile = Path.Combine(sourceDir, "nested", "data.txt");
        File.WriteAllText(nestedFile, "v1");
        var scenario = new EvalScenario(
            "s",
            "inspect project",
            new SetupConfig(Files: [new SetupFile("Project", "fixtures/project")]));
        try
        {
            var before = BaselineStore.ComputeTargetSha(scenario, evalPath);
            File.WriteAllText(nestedFile, "v2");
            var after = BaselineStore.ComputeTargetSha(scenario, evalPath);

            Assert.AreNotEqual(before, after);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void ComputeTargetSha_DistinguishesReplacementDirectorySources()
    {
        var root = Path.Combine(Path.GetTempPath(), $"sv-replacement-dir-{Guid.NewGuid():N}");
        var evalDir = Path.Combine(root, "tests", "demo", "agent.router");
        var sourceA = Path.Combine(evalDir, "fixtures", "project-a");
        var sourceB = Path.Combine(evalDir, "fixtures", "project-b");
        Directory.CreateDirectory(sourceA);
        Directory.CreateDirectory(sourceB);
        var evalPath = Path.Combine(evalDir, "eval.yaml");
        File.WriteAllText(evalPath, "stimuli: []");
        File.WriteAllText(Path.Combine(sourceA, "data.txt"), "A");
        File.WriteAllText(Path.Combine(sourceB, "data.txt"), "B");
        try
        {
            var scenarioA = new EvalScenario(
                "s",
                "inspect project",
                new SetupConfig(Files: [new SetupFile("Project", "fixtures/project-a")]));
            var scenarioB = scenarioA with
            {
                Setup = new SetupConfig(Files: [new SetupFile("Project", "fixtures/project-b")]),
            };

            Assert.AreNotEqual(
                BaselineStore.ComputeTargetSha(scenarioA, evalPath),
                BaselineStore.ComputeTargetSha(scenarioB, evalPath));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void Clone_ProducesIndependentCopy()
    {
        var source = MakeBaseline(output: "src").Metrics;
        source.JudgeInputTokens = 10;
        source.ToolCallBreakdown["bash"] = 4;

        var clone = source.Clone();
        clone.JudgeInputTokens = 99;
        clone.ToolCallBreakdown["bash"] = 1;
        clone.AssertionResults.Add(new AssertionResult(new Assertion(AssertionType.OutputContains, Value: "x"), true, ""));

        // Mutating the clone must not leak back into the source — the cached baseline
        // can be reused concurrently across parallel target evaluations.
        Assert.AreEqual(10, source.JudgeInputTokens);
        Assert.AreEqual(4, source.ToolCallBreakdown["bash"]);
        Assert.IsEmpty(source.AssertionResults);
        Assert.AreNotSame(source.ToolCallBreakdown, clone.ToolCallBreakdown);
        Assert.AreNotSame(source.AssertionResults, clone.AssertionResults);
    }

    [TestMethod]
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
            var store = BaselineStore.ForWrite(Model, Judge);
            store.Record(scenarioA, runs: 5, MakeBaseline(output: "A-baseline"), evalA);
            store.Save(path);

            var loaded = BaselineStore.Load(path, Model, Judge);

            // Case A reuses its baseline; case B must NOT (different targetSha).
            Assert.IsNotNull(loaded.TryGetBaseline(scenarioA, evalA));
            Assert.AreEqual("A-baseline", loaded.TryGetBaseline(scenarioA, evalA)!.Metrics.AgentOutput);
            Assert.IsNull(loaded.TryGetBaseline(scenarioB, evalB));

            // FindMissingScenarios surfaces case B (with its eval path) despite the shared prompt.
            var missing = loaded.FindMissingScenarios([(scenarioA, evalA), (scenarioB, evalB)]);
            Assert.ContainsSingle(missing);
            Assert.StartsWith("case-B", missing[0]);
            Assert.Contains(evalB, missing[0]);
        }
        finally
        {
            File.Delete(path);
            Directory.Delete(Path.GetDirectoryName(evalA)!, recursive: true);
            Directory.Delete(Path.GetDirectoryName(evalB)!, recursive: true);
        }
    }
}
