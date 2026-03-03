using SkillValidator.Models;
using SkillValidator.Services;

namespace SkillValidator.Tests;

public class EvaluateAssertionsTests
{
    private const string WorkDir = "C:\\temp\\test-workdir";

    [Fact]
    public async Task OutputContainsPassesWhenValueIsPresent()
    {
        var assertions = new List<Assertion> { new(AssertionType.OutputContains, Value: "hello") };
        var results = await AssertionEvaluator.EvaluateAssertions(assertions, "hello world", WorkDir);
        Assert.True(results[0].Passed);
    }

    [Fact]
    public async Task OutputContainsIsCaseInsensitive()
    {
        var assertions = new List<Assertion> { new(AssertionType.OutputContains, Value: "Hello") };
        var results = await AssertionEvaluator.EvaluateAssertions(assertions, "HELLO WORLD", WorkDir);
        Assert.True(results[0].Passed);
    }

    [Fact]
    public async Task OutputContainsFailsWhenValueIsMissing()
    {
        var assertions = new List<Assertion> { new(AssertionType.OutputContains, Value: "missing") };
        var results = await AssertionEvaluator.EvaluateAssertions(assertions, "hello world", WorkDir);
        Assert.False(results[0].Passed);
    }

    [Fact]
    public async Task OutputMatchesPassesWhenPatternMatches()
    {
        var assertions = new List<Assertion> { new(AssertionType.OutputMatches, Pattern: "\\d{3}-\\d{4}") };
        var results = await AssertionEvaluator.EvaluateAssertions(assertions, "Call 555-1234", WorkDir);
        Assert.True(results[0].Passed);
    }

    [Fact]
    public async Task OutputMatchesFailsWhenPatternDoesNotMatch()
    {
        var assertions = new List<Assertion> { new(AssertionType.OutputMatches, Pattern: "^exact$") };
        var results = await AssertionEvaluator.EvaluateAssertions(assertions, "not exact match", WorkDir);
        Assert.False(results[0].Passed);
    }

    [Fact]
    public async Task ExitSuccessPassesWithNonEmptyOutput()
    {
        var assertions = new List<Assertion> { new(AssertionType.ExitSuccess) };
        var results = await AssertionEvaluator.EvaluateAssertions(assertions, "some output", WorkDir);
        Assert.True(results[0].Passed);
    }

    [Fact]
    public async Task ExitSuccessFailsWithEmptyOutput()
    {
        var assertions = new List<Assertion> { new(AssertionType.ExitSuccess) };
        var results = await AssertionEvaluator.EvaluateAssertions(assertions, "", WorkDir);
        Assert.False(results[0].Passed);
    }

    [Fact]
    public async Task HandlesMultipleAssertions()
    {
        var assertions = new List<Assertion>
        {
            new(AssertionType.OutputContains, Value: "hello"),
            new(AssertionType.OutputContains, Value: "world"),
            new(AssertionType.OutputContains, Value: "missing"),
        };
        var results = await AssertionEvaluator.EvaluateAssertions(assertions, "hello world", WorkDir);
        Assert.True(results[0].Passed);
        Assert.True(results[1].Passed);
        Assert.False(results[2].Passed);
    }
}

public class FileContainsAssertionTests : IDisposable
{
    private readonly string _tmpDir;

    public FileContainsAssertionTests()
    {
        _tmpDir = Path.Combine(Path.GetTempPath(), $"assertions-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tmpDir);
        File.WriteAllText(Path.Combine(_tmpDir, "hello.cs"), "using System;\nstackalloc Span<nint> data;");
        File.WriteAllText(Path.Combine(_tmpDir, "readme.md"), "# README\nThis is a test.");
    }

    public void Dispose()
    {
        try { Directory.Delete(_tmpDir, true); } catch { }
    }

    [Fact]
    public async Task PassesWhenFileContainsTheValue()
    {
        var results = await AssertionEvaluator.EvaluateAssertions(
            [new Assertion(AssertionType.FileContains, Path: "*.cs", Value: "stackalloc")],
            "",
            _tmpDir);
        Assert.True(results[0].Passed);
        Assert.Contains("hello.cs", results[0].Message);
    }

    [Fact]
    public async Task FailsWhenFileDoesNotContainTheValue()
    {
        var results = await AssertionEvaluator.EvaluateAssertions(
            [new Assertion(AssertionType.FileContains, Path: "*.cs", Value: "notfound")],
            "",
            _tmpDir);
        Assert.False(results[0].Passed);
    }

    [Fact]
    public async Task FailsWhenNoFilesMatchTheGlob()
    {
        var results = await AssertionEvaluator.EvaluateAssertions(
            [new Assertion(AssertionType.FileContains, Path: "*.py", Value: "import")],
            "",
            _tmpDir);
        Assert.False(results[0].Passed);
        Assert.Contains("No file matching", results[0].Message);
    }
}

public class FileNotContainsAssertionTests : IDisposable
{
    private readonly string _tmpDir;

    public FileNotContainsAssertionTests()
    {
        _tmpDir = Path.Combine(Path.GetTempPath(), $"assertions-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tmpDir);
        File.WriteAllText(Path.Combine(_tmpDir, "hello.cs"), "using System;\nstackalloc Span<nint> data;");
        File.WriteAllText(Path.Combine(_tmpDir, "readme.md"), "# README\nThis is a test.");
    }

    public void Dispose()
    {
        try { Directory.Delete(_tmpDir, true); } catch { }
    }

    [Fact]
    public async Task PassesWhenFileDoesNotContainTheValue()
    {
        var results = await AssertionEvaluator.EvaluateAssertions(
            [new Assertion(AssertionType.FileNotContains, Path: "*.cs", Value: "notfound")],
            "",
            _tmpDir);
        Assert.True(results[0].Passed);
    }

    [Fact]
    public async Task FailsWhenFileContainsTheValue()
    {
        var results = await AssertionEvaluator.EvaluateAssertions(
            [new Assertion(AssertionType.FileNotContains, Path: "*.cs", Value: "stackalloc")],
            "",
            _tmpDir);
        Assert.False(results[0].Passed);
        Assert.Contains("hello.cs", results[0].Message);
    }

    [Fact]
    public async Task FailsWhenNoFilesMatchTheGlob()
    {
        var results = await AssertionEvaluator.EvaluateAssertions(
            [new Assertion(AssertionType.FileNotContains, Path: "*.py", Value: "import")],
            "",
            _tmpDir);
        Assert.False(results[0].Passed);
        Assert.Contains("No file matching", results[0].Message);
    }
}

public class EvaluateConstraintsTests
{
    private static RunMetrics MakeMetrics(
        int tokenEstimate = 1000,
        int toolCallCount = 3,
        Dictionary<string, int>? toolCallBreakdown = null,
        int turnCount = 5,
        long wallTimeMs = 10000,
        int errorCount = 0,
        bool taskCompleted = true)
    {
        return new RunMetrics
        {
            TokenEstimate = tokenEstimate,
            ToolCallCount = toolCallCount,
            ToolCallBreakdown = toolCallBreakdown ?? new Dictionary<string, int> { ["bash"] = 2, ["create_file"] = 1 },
            TurnCount = turnCount,
            WallTimeMs = wallTimeMs,
            ErrorCount = errorCount,
            TaskCompleted = taskCompleted,
            AgentOutput = "output",
            Events = [],
            WorkDir = "/tmp/test",
        };
    }

    private static EvalScenario MakeScenario(
        IReadOnlyList<string>? expectTools = null,
        IReadOnlyList<string>? rejectTools = null,
        int? maxTurns = null,
        int? maxTokens = null)
    {
        return new EvalScenario(
            Name: "test",
            Prompt: "do something",
            ExpectTools: expectTools,
            RejectTools: rejectTools,
            MaxTurns: maxTurns,
            MaxTokens: maxTokens);
    }

    [Fact]
    public void ReturnsEmptyWhenNoConstraintsSpecified()
    {
        var results = AssertionEvaluator.EvaluateConstraints(MakeScenario(), MakeMetrics());
        Assert.Empty(results);
    }

    [Fact]
    public void ExpectToolsPassesWhenToolWasUsed()
    {
        var results = AssertionEvaluator.EvaluateConstraints(
            MakeScenario(expectTools: ["bash"]),
            MakeMetrics());
        Assert.Single(results);
        Assert.True(results[0].Passed);
        Assert.Contains("'bash' was used", results[0].Message);
    }

    [Fact]
    public void ExpectToolsFailsWhenToolWasNotUsed()
    {
        var results = AssertionEvaluator.EvaluateConstraints(
            MakeScenario(expectTools: ["python"]),
            MakeMetrics());
        Assert.False(results[0].Passed);
        Assert.Contains("'python' was not used", results[0].Message);
    }

    [Fact]
    public void RejectToolsPassesWhenToolWasNotUsed()
    {
        var results = AssertionEvaluator.EvaluateConstraints(
            MakeScenario(rejectTools: ["python"]),
            MakeMetrics());
        Assert.True(results[0].Passed);
    }

    [Fact]
    public void RejectToolsFailsWhenToolWasUsed()
    {
        var results = AssertionEvaluator.EvaluateConstraints(
            MakeScenario(rejectTools: ["create_file"]),
            MakeMetrics());
        Assert.False(results[0].Passed);
        Assert.Contains("'create_file' was used but should not be", results[0].Message);
    }

    [Fact]
    public void MaxTurnsPassesWhenUnderLimit()
    {
        var results = AssertionEvaluator.EvaluateConstraints(
            MakeScenario(maxTurns: 10),
            MakeMetrics(turnCount: 5));
        Assert.True(results[0].Passed);
    }

    [Fact]
    public void MaxTurnsFailsWhenOverLimit()
    {
        var results = AssertionEvaluator.EvaluateConstraints(
            MakeScenario(maxTurns: 3),
            MakeMetrics(turnCount: 5));
        Assert.False(results[0].Passed);
        Assert.Contains("exceeds max_turns 3", results[0].Message);
    }

    [Fact]
    public void MaxTokensPassesWhenUnderLimit()
    {
        var results = AssertionEvaluator.EvaluateConstraints(
            MakeScenario(maxTokens: 5000),
            MakeMetrics(tokenEstimate: 1000));
        Assert.True(results[0].Passed);
    }

    [Fact]
    public void MaxTokensFailsWhenOverLimit()
    {
        var results = AssertionEvaluator.EvaluateConstraints(
            MakeScenario(maxTokens: 500),
            MakeMetrics(tokenEstimate: 1000));
        Assert.False(results[0].Passed);
        Assert.Contains("exceeds max_tokens 500", results[0].Message);
    }

    [Fact]
    public void EvaluatesMultipleConstraintsTogether()
    {
        var results = AssertionEvaluator.EvaluateConstraints(
            MakeScenario(expectTools: ["bash"], rejectTools: ["python"], maxTurns: 10, maxTokens: 5000),
            MakeMetrics());
        Assert.Equal(4, results.Count);
        Assert.True(results.All(r => r.Passed));
    }

    [Fact]
    public void ExpectToolsChecksEachToolIndependently()
    {
        var results = AssertionEvaluator.EvaluateConstraints(
            MakeScenario(expectTools: ["bash", "python", "create_file"]),
            MakeMetrics());
        Assert.Equal(3, results.Count);
        Assert.True(results[0].Passed);   // bash: used
        Assert.False(results[1].Passed);  // python: not used
        Assert.True(results[2].Passed);   // create_file: used
    }
}
