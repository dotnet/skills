using SkillValidator.Evaluate;

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
    [DataRow("All tests passed")]
    [DataRow("All tests have passed.")]
    [DataRow("The tests were successful.")]
    [DataRow("pytest ran successfully")]
    [DataRow("pytest completed without errors.")]
    [DataRow("The test suite executed successfully.")]
    [DataRow("Validation completed successfully.")]
    [DataRow("All **tests** passed.")]
    [DataRow("Coverage reached 90%")]
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
