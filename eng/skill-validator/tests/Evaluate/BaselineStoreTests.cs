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
            var missing = loaded.FindMissingScenarios([present, Scenario("beta", "prompt two")]);

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
}
