using SkillValidator.Evaluate;
using Microsoft.Data.Sqlite;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SkillValidator.Tests;

[TestClass]
public class GeneratorDeniedScenarioTests
{
    [TestMethod]
    [DataRow("generator.eval.yaml", "Review focused assertions without a second audit agent")]
    [DataRow("auditor.eval.yaml", "Comprehensive test quality audit of weak test suite")]
    [DataRow("auditor.eval.yaml", "Assertion quality analysis")]
    public async Task ReadOnlyReviewGradersRequireCorrectPerTestAssessments(string fixture, string name)
    {
        var config = EvalSchema.ParseEvalConfigFlexible(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", fixture)));
        Assert.IsNotNull(config);
        var scenario = Assert.ContainsSingle(config.Scenarios.Where(item => item.Name == name));
        var assertions = scenario.Assertions!.Where(
            assertion => assertion.Type is AssertionType.OutputMatches or AssertionType.OutputNotMatches).ToList();
        const string report = """
            summary: weak, limited assertion variety; two of six tests have meaningful checks. priority: repair hollow tests.
            | Test | Assessment |
            | AddItem_Works | assertion-free; no assertion rejects a no-op |
            | AddItem_ItemIsAdded | only checks non-null Items; an empty cart passes |
            | GetTotal_ReturnsValue | tautology; compares total to itself |
            | AddItem_NegativePrice_Throws | catch-and-swallow; passes with no exception or any exception |
            | ItemCount_AfterAdd | meaningful count check; pins ItemCount to 1 |
            | GetTotal_WithMultipleItems | meaningful total check; pins the total to 25.00 |
            Use Assert.AreEqual, IsNotNull guards, and explicit exception assertions.
            RemoveItem and GetTotalWithDiscount have gaps; assert collection state and quantity.
            """;
        const string summary = "two of six tests have meaningful checks";
        foreach (var validSummary in new[]
        {
            summary,
            "exactly 2 out of 6 tests contain meaningful assertions",
            "two of the six tests have meaningful checks",
            "two of    six tests have meaningful checks",
            "only two tests have meaningful checks",
            "2/6 tests have meaningful checks",
            "two meaningful tests",
            "Meaningful tests: two",
            "Tests with meaningful checks: 2",
            "Meaningful checks in two tests",
            "| Meaningful tests | 2/6 |",
            "| Meaningful tests | two |",
            "| Tests with meaningful checks | 2 (33%) |",
            "some tests have meaningful checks; the suite is weak overall",
            "not all six tests have meaningful checks",
            "not  all six tests have meaningful checks",
            "not every test is meaningful",
            "all six tests were reviewed; four assertion calls include two meaningful checks",
            "four hollow tests need repair; two tests protect behavior",
            "one assertion checks count 1; another asserts total 25.00",
            "no tests have been run; execution and coverage were not measured",
            "three new tests should have meaningful checks",
            "add three meaningful tests for missing behavior",
            "at least one test has meaningful checks",
            "at most six tests have meaningful checks",
            "more than one test has meaningful checks",
            "fewer than three tests have meaningful checks",
            "no more than two tests have meaningful checks",
            "no fewer than two tests have meaningful checks",
            "four tests lack meaningful assertions",
            "four tests have no meaningful checks",
        })
        {
            var results = await AssertionEvaluator.EvaluateAssertions(assertions,
                report.Replace(summary, validSummary, StringComparison.Ordinal), AppContext.BaseDirectory);
            Assert.IsTrue(results.All(result => result.Passed),
                $"{validSummary}: {string.Join('\n', results.Where(result => !result.Passed).Select(result => result.Message))}");
        }

        var invalidSummaries = new List<string>
        {
            "only one of six tests has meaningful checks",
            "none of six tests have meaningful checks",
            "no tests have meaningful assertions",
            "only a single test has meaningful checks",
            "all six tests have meaningful checks",
            "all tests are meaningful",
            "every test contains meaningful assertions",
            "most of the tests have meaningful checks",
            "half of the six tests have meaningful checks",
            "a majority of the tests have meaningful checks",
            "0/6 tests have meaningful checks",
            "3/6 tests have meaningful checks",
            "there are no meaningful tests",
            "three meaningful tests",
            "Meaningful tests: 0",
            "Meaningful tests: three",
            "Tests with meaningful checks: 1",
            "Meaningful checks in six tests",
            "at least three tests have meaningful checks",
            "at least 6 tests contain meaningful assertions",
            "at most one test has meaningful checks",
            "at most 0 tests have meaningful checks",
            "more than two tests have meaningful checks",
            "more than 2 tests have meaningful checks",
            "fewer than two tests have meaningful checks",
            "less than 2 tests have meaningful checks",
            "no more than one test has meaningful checks",
            "no fewer than three tests have meaningful checks",
        };
        foreach (var count in new[] { "zero", "one", "three", "four", "five", "six", "0", "1", "3", "4", "5", "6" })
            foreach (var scope in new[] { "of six", "out of 6" })
                foreach (var assessment in new[] { "have meaningful checks", "meaningfully protect behavior" })
                    invalidSummaries.Add($"{count} {scope} tests {assessment}");
        foreach (var count in new[] { "zero", "one", "three", "four", "five", "six", "0", "1", "3", "4", "5", "6", "none", "all" })
            invalidSummaries.Add($"| Meaningful tests | {count}/6 |");

        var brokenReports = new[]
        {
            string.Join('\n', new[] { "AddItem_Works", "AddItem_ItemIsAdded", "GetTotal_ReturnsValue",
                "AddItem_NegativePrice_Throws", "ItemCount_AfterAdd", "GetTotal_WithMultipleItems" }),
            report.Replace("AddItem_Works", "SWAP", StringComparison.Ordinal)
                .Replace("ItemCount_AfterAdd", "AddItem_Works", StringComparison.Ordinal)
                .Replace("SWAP", "ItemCount_AfterAdd", StringComparison.Ordinal),
        }.Concat(invalidSummaries.Select(invalid => report.Replace(summary, invalid, StringComparison.Ordinal)));
        foreach (var broken in brokenReports)
        {
            var results = await AssertionEvaluator.EvaluateAssertions(assertions, broken, AppContext.BaseDirectory);
            Assert.IsFalse(results.All(result => result.Passed), broken);
        }
    }

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

        Assert.AreEqual(10, scenarios.Count);
        foreach (var scenario in scenarios)
        {
            var helper = Assert.ContainsSingle(scenario.Setup!.Files!.Where(
                file => file.Path == ".eval/authenticated_artifacts.py"));
            Assert.AreEqual("../graders/authenticated_artifacts.py", helper.Source);
            Assert.Contains("test-engineer", scenario.RejectAgents!);
        }
        Assert.IsTrue(Assert.ContainsSingle(scenarios.Where(scenario => scenario.DenyShell)).RejectShellRetries);
        var focusedReview = Assert.ContainsSingle(scenarios.Where(
            scenario => scenario.Name == "Review focused assertions without a second audit agent"));
        Assert.AreSequenceEqual(["test-engineer", "test-quality-auditor"], focusedReview.RejectAgents!);
    }

    [TestMethod]
    [DataRow("agent.primary_selected", true)]
    [DataRow("subagent.started", false)]
    public void RejectDelegationDistinguishesPrimarySelectionFromStartingAnotherGenerator(
        string eventType, bool expectedPass)
    {
        var metrics = new RunMetrics();
        metrics.Events.Add(new AgentEvent(eventType, 0,
            new() { ["agentName"] = JsonValue.Create("demo:test-engineer") }));

        var result = Assert.ContainsSingle(AssertionEvaluator.EvaluateConstraints(
            new EvalScenario("routing", "Generate tests.", RejectAgents: ["test-engineer"]), metrics));

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
                DenyShell: true, RejectAgents: ["test-engineer"], RejectShellRetries: true);
            var metrics = new RunMetrics { AgentOutput = "PARTIAL: tests written; execution denied.", WorkDir = directory };
            metrics.Events.Add(new AgentEvent("evaluator.shell_denied", 0,
                new() { ["sessionId"] = JsonValue.Create("root") }));
            using (var database = new SessionDatabase(path))
            {
                database.RegisterSession("run", "test-engineer", "/test-engineer.agent.md",
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
