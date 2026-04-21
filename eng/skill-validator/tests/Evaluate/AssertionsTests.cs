using SkillValidator.Evaluate;
using SkillValidator.Shared;

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

public class RunCommandAndAssertTests : IDisposable
{
    private readonly string _tmpDir;

    public RunCommandAndAssertTests()
    {
        _tmpDir = Path.Combine(Path.GetTempPath(), $"run-cmd-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tmpDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tmpDir, true); } catch { }
    }

    private static string Shell => OperatingSystem.IsWindows() ? "cmd" : "/bin/sh";

    private static string ShellArgs(string command) =>
        OperatingSystem.IsWindows()
            ? $"/c {command}"
            : $"-c \"{command}\"";

    private static Assertion CmdAssertion(
        string? commandArguments = null,
        int? expectedExitCode = null,
        string? expectedStdOutContains = null,
        string? expectedStdErrorContains = null,
        string? expectedStdOutMatches = null,
        string? expectedStdErrorMatches = null,
        int? timeout = null) =>
        new(AssertionType.RunCommandAndAssert,
            CommandArgs: new CommandAssertionArgs(
                Shell,
                commandArguments,
                expectedExitCode,
                expectedStdOutContains,
                expectedStdErrorContains,
                expectedStdOutMatches,
                expectedStdErrorMatches,
                timeout));

    [Fact]
    public async Task PassesWhenExitCodeMatches()
    {
        var results = await AssertionEvaluator.EvaluateAssertions(
            [CmdAssertion(commandArguments: ShellArgs("exit 0"), expectedExitCode: 0)],
            "", _tmpDir);
        Assert.True(results[0].Passed);
    }

    [Fact]
    public async Task FailsWhenExitCodeDoesNotMatch()
    {
        var results = await AssertionEvaluator.EvaluateAssertions(
            [CmdAssertion(commandArguments: ShellArgs("exit 1"), expectedExitCode: 0)],
            "", _tmpDir);
        Assert.False(results[0].Passed);
        Assert.Contains("exited with code 1 but expected 0", results[0].Message);
    }

    [Fact]
    public async Task PassesWithNonZeroExpectedExitCode()
    {
        var results = await AssertionEvaluator.EvaluateAssertions(
            [CmdAssertion(commandArguments: ShellArgs("exit 42"), expectedExitCode: 42)],
            "", _tmpDir);
        Assert.True(results[0].Passed);
    }

    [Fact]
    public async Task PassesWhenStdOutContainsExpectedValue()
    {
        var results = await AssertionEvaluator.EvaluateAssertions(
            [CmdAssertion(commandArguments: ShellArgs("echo hello_world"), expectedStdOutContains: "hello_world")],
            "", _tmpDir);
        Assert.True(results[0].Passed);
    }

    [Fact]
    public async Task FailsWhenStdOutDoesNotContainExpectedValue()
    {
        var results = await AssertionEvaluator.EvaluateAssertions(
            [CmdAssertion(commandArguments: ShellArgs("echo hello"), expectedStdOutContains: "goodbye")],
            "", _tmpDir);
        Assert.False(results[0].Passed);
        Assert.Contains("stdout did not contain expected value", results[0].Message);
    }

    [Fact]
    public async Task PassesWhenStdErrContainsExpectedValue()
    {
        var results = await AssertionEvaluator.EvaluateAssertions(
            [CmdAssertion(commandArguments: ShellArgs("echo error_marker 1>&2"), expectedStdErrorContains: "error_marker")],
            "", _tmpDir);
        Assert.True(results[0].Passed);
    }

    [Fact]
    public async Task FailsWhenStdErrDoesNotContainExpectedValue()
    {
        var results = await AssertionEvaluator.EvaluateAssertions(
            [CmdAssertion(commandArguments: ShellArgs("echo some_error 1>&2"), expectedStdErrorContains: "different_error")],
            "", _tmpDir);
        Assert.False(results[0].Passed);
        Assert.Contains("stderr did not contain expected value", results[0].Message);
    }

    [Fact]
    public async Task PassesWhenStdOutMatchesRegex()
    {
        var results = await AssertionEvaluator.EvaluateAssertions(
            [CmdAssertion(commandArguments: ShellArgs("echo build_v2.3.1_ok"), expectedStdOutMatches: @"build_v\d+\.\d+\.\d+_ok")],
            "", _tmpDir);
        Assert.True(results[0].Passed);
    }

    [Fact]
    public async Task FailsWhenStdOutDoesNotMatchRegex()
    {
        var results = await AssertionEvaluator.EvaluateAssertions(
            [CmdAssertion(commandArguments: ShellArgs("echo no_version_here"), expectedStdOutMatches: @"v\d+\.\d+\.\d+")],
            "", _tmpDir);
        Assert.False(results[0].Passed);
        Assert.Contains("stdout did not match pattern", results[0].Message);
    }

    [Fact]
    public async Task PassesWhenStdErrMatchesRegex()
    {
        var results = await AssertionEvaluator.EvaluateAssertions(
            [CmdAssertion(commandArguments: ShellArgs("echo warn_code_42 1>&2"), expectedStdErrorMatches: @"warn_code_\d+")],
            "", _tmpDir);
        Assert.True(results[0].Passed);
    }

    [Fact]
    public async Task FailsWhenStdErrDoesNotMatchRegex()
    {
        var results = await AssertionEvaluator.EvaluateAssertions(
            [CmdAssertion(commandArguments: ShellArgs("echo some_text 1>&2"), expectedStdErrorMatches: @"error_\d+")],
            "", _tmpDir);
        Assert.False(results[0].Passed);
        Assert.Contains("stderr did not match pattern", results[0].Message);
    }

    [Fact]
    public async Task PassesWhenAllChecksPass()
    {
        var results = await AssertionEvaluator.EvaluateAssertions(
            [CmdAssertion(
                commandArguments: ShellArgs("echo stdout_text && echo stderr_text 1>&2"),
                expectedExitCode: 0,
                expectedStdOutContains: "stdout_text",
                expectedStdErrorContains: "stderr_text")],
            "", _tmpDir);
        Assert.True(results[0].Passed);
    }

    [Fact]
    public async Task ExitCodeFailureShortCircuitsOtherChecks()
    {
        var results = await AssertionEvaluator.EvaluateAssertions(
            [CmdAssertion(
                commandArguments: ShellArgs("echo hello && exit 1"),
                expectedExitCode: 0,
                expectedStdOutContains: "hello")],
            "", _tmpDir);
        Assert.False(results[0].Passed);
        Assert.Contains("exited with code 1 but expected 0", results[0].Message);
    }

    [Fact]
    public async Task StdOutFailureShortCircuitsStdErrCheck()
    {
        var results = await AssertionEvaluator.EvaluateAssertions(
            [CmdAssertion(
                commandArguments: ShellArgs("echo wrong && echo expected_error 1>&2"),
                expectedStdOutContains: "expected_output",
                expectedStdErrorContains: "expected_error")],
            "", _tmpDir);
        Assert.False(results[0].Passed);
        Assert.Contains("stdout did not contain expected value", results[0].Message);
    }

    [Fact]
    public async Task IgnoresExitCodeWhenNotSpecified()
    {
        var results = await AssertionEvaluator.EvaluateAssertions(
            [CmdAssertion(
                commandArguments: ShellArgs("echo output_text && exit 1"),
                expectedStdOutContains: "output_text")],
            "", _tmpDir);
        Assert.True(results[0].Passed);
    }

    [Fact]
    public async Task UsesWorkDirAsProcessWorkingDirectory()
    {
        File.WriteAllText(Path.Combine(_tmpDir, "marker.txt"), "test_content");
        var catCmd = OperatingSystem.IsWindows() ? "type marker.txt" : "cat marker.txt";
        var results = await AssertionEvaluator.EvaluateAssertions(
            [CmdAssertion(commandArguments: ShellArgs(catCmd), expectedStdOutContains: "test_content")],
            "", _tmpDir);
        Assert.True(results[0].Passed);
    }

    [Fact]
    public async Task UsesCustomTimeoutFromAssertion()
    {
        // Use a very short timeout (1 second) so a long-running command times out.
        // Note: 'timeout' is not reliable in non-interactive contexts (exits immediately on CI),
        // so we use 'ping -n 11 127.0.0.1' which sleeps ~10s reliably everywhere.
        var sleepCmd = OperatingSystem.IsWindows() ? "ping -n 11 127.0.0.1 >nul" : "sleep 10";
        var results = await AssertionEvaluator.EvaluateAssertions(
            [CmdAssertion(commandArguments: ShellArgs(sleepCmd), expectedExitCode: 0, timeout: 1)],
            "", _tmpDir);
        Assert.False(results[0].Passed);
        Assert.Contains("Command timed out after 1s", results[0].Message);
    }

    [Fact]
    public async Task FallsBackToScenarioTimeout()
    {
        // Pass a short scenario timeout (1 second) - the command should time out.
        // Note: 'timeout' is not reliable in non-interactive contexts (exits immediately on CI),
        // so we use 'ping -n 11 127.0.0.1' which sleeps ~10s reliably everywhere.
        var sleepCmd = OperatingSystem.IsWindows() ? "ping -n 11 127.0.0.1 >nul" : "sleep 10";
        var results = await AssertionEvaluator.EvaluateAssertions(
            [CmdAssertion(commandArguments: ShellArgs(sleepCmd), expectedExitCode: 0)],
            "", _tmpDir, scenarioTimeoutSeconds: 1);
        Assert.False(results[0].Passed);
        Assert.Contains("Command timed out after 1s", results[0].Message);
    }
}
