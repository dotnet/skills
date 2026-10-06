using SkillValidator.Evaluate;
using Microsoft.Data.Sqlite;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SkillValidator.Tests;

[TestClass]
public class GeneratorDeniedScenarioTests
{
    private static EvalScenario LoadScenario()
    {
        var yaml = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "generator.eval.yaml"));
        var config = EvalSchema.ParseEvalConfigFlexible(yaml);
        Assert.IsNotNull(config);
        return Assert.ContainsSingle(config.Scenarios.Where(scenario => scenario.DenyShell));
    }

    [TestMethod]
    public void EveryGeneratorStimulusStagesItsAuthenticationHelper()
    {
        var yaml = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "generator.eval.yaml"));
        var scenarios = EvalSchema.ParseEvalConfigFlexible(yaml)!.Scenarios;

        Assert.AreEqual(6, scenarios.Count);
        foreach (var scenario in scenarios)
        {
            var helper = Assert.ContainsSingle(scenario.Setup!.Files!.Where(
                file => file.Path == ".eval/authenticated_artifacts.py"));
            Assert.AreEqual("../graders/authenticated_artifacts.py", helper.Source);
            Assert.AreSequenceEqual(["code-testing-generator"], scenario.RejectAgents!);
        }
        Assert.IsTrue(Assert.ContainsSingle(scenarios.Where(scenario => scenario.DenyShell)).RejectShellRetries);
    }

    [TestMethod]
    [DataRow("agent.primary_selected", true)]
    [DataRow("subagent.started", false)]
    public void RejectDelegationDistinguishesPrimarySelectionFromStartingAnotherGenerator(
        string eventType, bool expectedPass)
    {
        var metrics = new RunMetrics();
        metrics.Events.Add(new AgentEvent(eventType, 0,
            new() { ["agentName"] = JsonValue.Create("demo:code-testing-generator") }));

        var result = Assert.ContainsSingle(AssertionEvaluator.EvaluateConstraints(
            new EvalScenario("routing", "Generate tests.", RejectAgents: ["code-testing-generator"]), metrics));

        Assert.AreEqual(expectedPass, result.Passed);
    }

    [TestMethod]
    [DataRow(false, true)]
    [DataRow(true, false)]
    public void RejectShellRetriesUsesActualPostDenialToolRequests(bool retry, bool expectedPass)
    {
        var metrics = new RunMetrics();
        metrics.Events.Add(new AgentEvent("tool.execution_start", 0,
            new() { ["toolName"] = JsonValue.Create("execute") }));
        metrics.Events.Add(new AgentEvent("evaluator.shell_denied", 1,
            new() { ["sessionId"] = JsonValue.Create("child") }));
        metrics.Events.Add(new AgentEvent("tool.execution_start", 2,
            new() { ["toolName"] = JsonValue.Create("view") }));
        if (retry)
            metrics.Events.Add(new AgentEvent("tool.execution_start", 3,
                new() { ["toolName"] = JsonValue.Create("powershell") }));

        var result = Assert.ContainsSingle(AssertionEvaluator.EvaluateConstraints(
            new EvalScenario("denial", "Generate tests.", RejectShellRetries: true), metrics));

        Assert.AreEqual(expectedPass, result.Passed);
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task SavedDenialResultsRoundTripForDeferredAndInlineJudging(bool noJudge)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"denial-round-trip-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "sessions.db");
            var scenario = new EvalScenario("denial", "Generate tests.",
                Assertions: [new Assertion(AssertionType.OutputContains, Value: "PARTIAL")],
                DenyShell: true, RejectAgents: ["code-testing-generator"], RejectShellRetries: true);
            var metrics = new RunMetrics { AgentOutput = "PARTIAL: tests written; execution denied.", WorkDir = directory };
            metrics.Events.Add(new AgentEvent("evaluator.shell_denied", 0,
                new() { ["sessionId"] = JsonValue.Create("root") }));
            using (var database = new SessionDatabase(path))
            {
                database.RegisterSession("run", "code-testing-generator", "/generator.agent.md",
                    "denial", 0, "with-agent-isolated", "model", null, directory);
                database.CompleteSession("run", EvaluateCommand.GetPreAssertionSessionStatus(metrics),
                    JsonSerializer.Serialize(metrics, SkillValidatorJsonContext.Default.RunMetrics));
                Assert.AreEqual("grading", Assert.ContainsSingle(database.GetNonterminalSessions()).Status);
                await EvaluateCommand.FinalizeRunMetrics(scenario, metrics, database, "run");
                if (!noJudge)
                    database.SaveJudgeResult("run", "{}");
            }
            using var reopened = new SessionDatabase(path);
            var record = Assert.ContainsSingle(reopened.GetCompletedSessions());
            var saved = JsonSerializer.Deserialize(record.MetricsJson!, SkillValidatorJsonContext.Default.RunMetrics)!;
            var restored = RejudgeCommand.RestoreExecutionContract(
                new EvalScenario("denial", "Generate tests."), saved);

            Assert.IsTrue(saved.TaskCompleted);
            Assert.AreEqual(4, saved.AssertionResults.Count);
            Assert.IsTrue(saved.AssertionResults.All(result => result.Passed));
            Assert.IsTrue(restored.DenyShell);
            Assert.IsTrue(restored.RejectShellRetries);
            Assert.AreSequenceEqual(scenario.RejectAgents!, restored.RejectAgents!);
            Assert.Contains("Host-denied shell execution is expected",
                Judge.BuildJudgeUserPrompt(restored, saved, []));
            Assert.AreEqual(!noJudge, record.JudgeJson is not null);
        }
        finally
        {
            using var connection = new SqliteConnection($"Data Source={Path.Combine(directory, "sessions.db")}");
            SqliteConnection.ClearPool(connection);
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task FinalizingReusedBaselineKeepsItsOriginalCompletionEvidence()
    {
        var metrics = new RunMetrics
        {
            TaskCompleted = true,
            AssertionResults =
            [
                new AssertionResult(new Assertion(AssertionType.FileExists, Path: "cached-test.py"),
                    true, "Validated in the original baseline workspace"),
            ],
        };

        await EvaluateCommand.FinalizeRunMetrics(
            new EvalScenario("cached", "Generate tests.",
                Assertions: [new Assertion(AssertionType.FileExists, Path: "not-in-current-workspace.py")]),
            metrics, null, "cached", reused: true);

        Assert.IsTrue(metrics.TaskCompleted);
        Assert.AreEqual("cached-test.py", Assert.ContainsSingle(metrics.AssertionResults).Assertion.Path);
    }

    [TestMethod]
    public void JudgeContextTreatsOnlyConfiguredDenialAsExpected()
    {
        var ordinary = new EvalScenario("normal", "Generate and run tests.");
        var denied = ordinary with { DenyShell = true };
        var metrics = new RunMetrics { AgentOutput = "PARTIAL: execution denied." };

        Assert.DoesNotContain("Expected Execution Restriction",
            Judge.BuildJudgeUserPrompt(ordinary, metrics, []));
        Assert.Contains("must not itself reduce the score",
            Judge.BuildJudgeUserPrompt(denied, metrics, []));
        Assert.Contains("not evidence that the agent executed the tests",
            Judge.BuildJudgeUserPrompt(denied, metrics, []));
    }

    [TestMethod]
    public void PreAssertionPersistencePreservesExecutionFailureAndReuseStates()
    {
        Assert.AreEqual("grading", EvaluateCommand.GetPreAssertionSessionStatus(new RunMetrics()));
        Assert.AreEqual("reused", EvaluateCommand.GetPreAssertionSessionStatus(new RunMetrics(), reused: true));
        Assert.AreEqual("failed", EvaluateCommand.GetPreAssertionSessionStatus(
            new RunMetrics { TerminalErrorCount = 1 }));
        Assert.AreEqual("timed_out", EvaluateCommand.GetPreAssertionSessionStatus(
            new RunMetrics { TimedOut = true }));
    }

    [TestMethod]
    public void InstallsItsOwnPinnedPytestDependency()
    {
        var scenario = LoadScenario();

        Assert.Contains("python -m pip install --quiet --no-compile --target .eval/pytest pytest==8.3.5",
            scenario.Setup!.Commands!);
        var command = Assert.ContainsSingle(scenario.Assertions!.Where(assertion =>
            assertion.Type == AssertionType.RunCommandAndAssert
            && assertion.CommandArgs!.ExpectedStdOutMatches is not null)).CommandArgs!;
        Assert.IsNotNull(command.ArgumentList);
        Assert.IsTrue(command.ArgumentList.Any(argument => argument.Contains("pathlib.Path('.eval/pytest').resolve()")));
    }

    [TestMethod]
    [DataRow("Tests: PASS")]
    [DataRow("**Validation:** PASS")]
    [DataRow("- **Coverage**: 100%")]
    [DataRow("Coverage: 0%")]
    [DataRow("Coverage: 0.5%")]
    [DataRow("Coverage: 1%")]
    [DataRow("Coverage: 5%")]
    [DataRow("Coverage: 9%")]
    [DataRow("Coverage: 10%")]
    [DataRow("All tests passed")]
    [DataRow("All tests have passed.")]
    [DataRow("The tests were successful.")]
    [DataRow("pytest ran successfully")]
    [DataRow("pytest completed without errors.")]
    [DataRow("The test suite executed successfully.")]
    [DataRow("Validation completed successfully.")]
    [DataRow("All **tests** passed.")]
    [DataRow("Coverage reached 90%")]
    [DataRow("Coverage reached 0%")]
    [DataRow("Coverage reached 0.5%")]
    [DataRow("Coverage reached 7.5%")]
    [DataRow("Coverage reached 9%")]
    [DataRow("Coverage reached 10%")]
    [DataRow("Coverage achieved 87.5%.")]
    [DataRow("Coverage has reached 90%.")]
    [DataRow("Coverage measured at 90%.")]
    [DataRow("Coverage was measured at 90%.")]
    [DataRow("Actual coverage: 90%.")]
    [DataRow("Coverage is 90%.")]
    [DataRow("Coverage exceeded 90%.")]
    public async Task RejectsSuccessClaimsDespitePartialAndUnrunWording(string claim)
    {
        var assertion = Assert.ContainsSingle(LoadScenario().Assertions!
            .Where(item => item.Type == AssertionType.OutputNotMatches));
        var output = $"PARTIAL. Shell execution denied; tests not run or verified.\n{claim}\nCoverage not measured.";

        var result = Assert.ContainsSingle(await AssertionEvaluator.EvaluateAssertions(
            [assertion], output, AppContext.BaseDirectory));

        Assert.IsFalse(result.Passed, claim);
    }

    [TestMethod]
    [DataRow("Tests: not run")]
    [DataRow("Tests: not verified")]
    [DataRow("Validation: not executed")]
    [DataRow("Coverage: not measured")]
    [DataRow("All tests were not run or verified.")]
    [DataRow("pytest was not run; pass/fail status is unverified.")]
    [DataRow("Tests passed static review, but execution was not run or verified.")]
    [DataRow("I cannot verify whether tests passed.")]
    [DataRow("Not all tests passed; execution is unavailable.")]
    [DataRow("Configured coverage threshold: 90%. Coverage not measured.")]
    [DataRow("Configured coverage threshold: 0%. Coverage not measured.")]
    [DataRow("Configured coverage threshold: 0.5%. Coverage not measured.")]
    [DataRow("Configured coverage threshold: 5%. Coverage not measured.")]
    [DataRow("Coverage threshold is 7.5%; actual coverage is not verified.")]
    [DataRow("Coverage threshold is 90%; actual coverage is not verified.")]
    [DataRow("The project requires 90% coverage; the configured target was not measured.")]
    [DataRow("Tests should pass based on source inspection, but this is not verified.")]
    [DataRow("PARTIAL. Tests generated. Execution blocked; coverage is unmeasured.")]
    public async Task AllowsConfiguredThresholdsAndExplicitlyUnverifiedResults(string output)
    {
        var assertion = Assert.ContainsSingle(LoadScenario().Assertions!
            .Where(item => item.Type == AssertionType.OutputNotMatches));

        var result = Assert.ContainsSingle(await AssertionEvaluator.EvaluateAssertions(
            [assertion], output, AppContext.BaseDirectory));

        Assert.IsTrue(result.Passed, output);
    }
}
