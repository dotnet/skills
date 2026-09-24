using SkillValidator.Evaluate;
using SkillValidator.Shared;

namespace SkillValidator.Tests;

[TestClass]
public class ParseEvalConfigTests
{
    [TestMethod]
    public void ParsesValidEvalConfig()
    {
        var yaml = """
            scenarios:
              - name: "Test scenario"
                prompt: "Do something"
                assertions:
                  - type: "output_contains"
                    value: "hello"
                rubric:
                  - "Output is correct"
                timeout: 60
            """;
        var config = EvalSchema.ParseEvalConfig(yaml);

        Assert.ContainsSingle(config.Scenarios);
        Assert.AreEqual("Test scenario", config.Scenarios[0].Name);
        Assert.AreEqual(60, config.Scenarios[0].Timeout);
    }

    [TestMethod]
    public void AppliesDefaultTimeout()
    {
        var yaml = """
            scenarios:
              - name: "Test"
                prompt: "Do it"
            """;
        var config = EvalSchema.ParseEvalConfig(yaml);

        Assert.AreEqual(120, config.Scenarios[0].Timeout);
    }

    [TestMethod]
    public void RejectsEmptyScenarios()
    {
        var yaml = "scenarios: []";
        var ex = Assert.ThrowsExactly<InvalidOperationException>(() => EvalSchema.ParseEvalConfig(yaml));
        Assert.Contains("at least one scenario", ex.Message);
    }

    [TestMethod]
    public void RejectsMissingPrompt()
    {
        var yaml = """
            scenarios:
              - name: "Test"
            """;
        Assert.ThrowsExactly<InvalidOperationException>(() => EvalSchema.ParseEvalConfig(yaml));
    }

    [TestMethod]
    public void RejectsInvalidAssertionType()
    {
        var yaml = """
            scenarios:
              - name: "Test"
                prompt: "Do it"
                assertions:
                  - type: "invalid_type"
            """;
        Assert.ThrowsExactly<InvalidOperationException>(() => EvalSchema.ParseEvalConfig(yaml));
    }

    [TestMethod]
    public void ParsesFileContainsAssertion()
    {
        var yaml = """
            scenarios:
              - name: "Test"
                prompt: "Do it"
                assertions:
                  - type: "file_contains"
                    path: "*.cs"
                    value: "stackalloc"
            """;
        var config = EvalSchema.ParseEvalConfig(yaml);
        Assert.AreEqual(AssertionType.FileContains, config.Scenarios[0].Assertions![0].Type);
    }

    [TestMethod]
    public void ParsesScenarioLevelConstraints()
    {
        var yaml = """
            scenarios:
              - name: "Test"
                prompt: "Do it"
                expect_tools:
                  - "bash"
                reject_tools:
                  - "create_file"
                max_turns: 10
                max_tokens: 5000
            """;
        var config = EvalSchema.ParseEvalConfig(yaml);
        var s = config.Scenarios[0];
        Assert.AreSequenceEqual(["bash"], s.ExpectTools);
        Assert.AreSequenceEqual(["create_file"], s.RejectTools);
        Assert.AreEqual(10, s.MaxTurns);
        Assert.AreEqual(5000, s.MaxTokens);
    }

    [TestMethod]
    public void ParsesSetupCommands()
    {
        var yaml = """
            scenarios:
              - name: "Build first"
                prompt: "Fix the build"
                setup:
                  copy_test_files: true
                  commands:
                    - "dotnet build /bl:build.binlog"
                    - "rm -rf src/"
            """;
        var config = EvalSchema.ParseEvalConfig(yaml);
        var setup = config.Scenarios[0].Setup;
        Assert.IsNotNull(setup);
        Assert.IsTrue(setup!.CopyTestFiles);
        Assert.IsNotNull(setup.Commands);
        Assert.AreEqual(2, setup.Commands!.Count);
        Assert.AreEqual("dotnet build /bl:build.binlog", setup.Commands[0]);
    }

    [TestMethod]
    public void ParsesConfigSection()
    {
        var yaml = """
            config:
              max_parallel_scenarios: 1
              max_parallel_runs: 2
            scenarios:
              - name: "Test"
                prompt: "Do it"
            """;
        var config = EvalSchema.ParseEvalConfig(yaml);

        Assert.AreEqual(1, config.MaxParallelScenarios);
        Assert.AreEqual(2, config.MaxParallelRuns);
        Assert.ContainsSingle(config.Scenarios);
    }

    [TestMethod]
    public void ConfigSectionIsOptional()
    {
        var yaml = """
            scenarios:
              - name: "Test"
                prompt: "Do it"
            """;
        var config = EvalSchema.ParseEvalConfig(yaml);

        Assert.IsNull(config.MaxParallelScenarios);
        Assert.IsNull(config.MaxParallelRuns);
    }

    [TestMethod]
    public void ParsesPartialConfigSection()
    {
        var yaml = """
            config:
              max_parallel_scenarios: 2
            scenarios:
              - name: "Test"
                prompt: "Do it"
            """;
        var config = EvalSchema.ParseEvalConfig(yaml);

        Assert.AreEqual(2, config.MaxParallelScenarios);
        Assert.IsNull(config.MaxParallelRuns);
    }

    [TestMethod]
    public void ParsesRunCommandAndAssertAssertion()
    {
        var yaml = """
            scenarios:
              - name: "Build check"
                prompt: "Build the project"
                assertions:
                  - type: "run_command_and_assert"
                    command_to_run: "dotnet"
                    command_arguments: "build"
                    expected_exit_code: 0
                    expected_std_output_contains: "Build succeeded"
                    expected_std_error_contains: "warning"
                    expected_std_output_matches: "Build \\w+"
                    expected_std_error_matches: "warn.*"
                    command_timeout: 60
            """;
        var config = EvalSchema.ParseEvalConfig(yaml);
        var assertion = config.Scenarios[0].Assertions![0];
        Assert.AreEqual(AssertionType.RunCommandAndAssert, assertion.Type);
        Assert.IsNotNull(assertion.CommandArgs);
        var cmd = assertion.CommandArgs!;
        Assert.AreEqual("dotnet", cmd.CommandToRun);
        Assert.AreEqual("build", cmd.CommandArguments);
        Assert.AreEqual(0, cmd.ExpectedExitCode);
        Assert.AreEqual("Build succeeded", cmd.ExpectedStdOutContains);
        Assert.AreEqual("warning", cmd.ExpectedStdErrorContains);
        Assert.AreEqual("Build \\w+", cmd.ExpectedStdOutMatches);
        Assert.AreEqual("warn.*", cmd.ExpectedStdErrorMatches);
        Assert.AreEqual(60, cmd.Timeout);
    }

    [TestMethod]
    public void RejectsRunCommandAndAssertWithoutCommandToRun()
    {
        var yaml = """
            scenarios:
              - name: "Test"
                prompt: "Do it"
                assertions:
                  - type: "run_command_and_assert"
                    expected_exit_code: 0
            """;
        var ex = Assert.ThrowsExactly<InvalidOperationException>(() => EvalSchema.ParseEvalConfig(yaml));
        Assert.Contains("command_to_run", ex.Message);
    }

    [TestMethod]
    public void RejectsRunCommandAndAssertWithoutAnyExpectedChecks()
    {
        var yaml = """
            scenarios:
              - name: "Test"
                prompt: "Do it"
                assertions:
                  - type: "run_command_and_assert"
                    command_to_run: "dotnet"
            """;
        var ex = Assert.ThrowsExactly<InvalidOperationException>(() => EvalSchema.ParseEvalConfig(yaml));
        Assert.Contains("expected_exit_code", ex.Message);
        Assert.Contains("expected_std_output_contains", ex.Message);
    }

    [TestMethod]
    public void ParsesVallyAgentEvalForNativeExecution()
    {
        var yaml = """
            name: agent.sample
            defaults:
              timeout: 20m
            stimuli:
              - name: Migrate the project
                prompt: Apply the requested migration.
                expect_activation: false
                environment:
                  files:
                    - src: fixtures/project
                      dest: Project
                  commands:
                    - git init -q
                  skills:
                    - ../../plugins/demo/skills/migrate
                  agents:
                    - helper-agent
                constraints:
                  expect_tools: [bash]
                  reject_tools: [web]
                  max_turns: 12
                  max_tokens: 4000
                graders:
                  - type: file-contains
                    config:
                      path: "**/*.csproj"
                      value: "4."
                  - type: run-command
                    config:
                      command: dotnet test Project
                      expected_exit_code: 0
                      timeout: 5m
                      stdout_contains: Passed!
                      stdout_matches: Passed
                  - type: prompt
                rubric:
                  - Completed the migration
            """;

        var config = EvalSchema.ParseEvalConfigFlexible(yaml);

        Assert.IsNotNull(config);
        var scenario = Assert.ContainsSingle(config!.Scenarios);
        Assert.AreEqual(1200, scenario.Timeout);
        Assert.IsFalse(scenario.ExpectActivation);
        Assert.AreSequenceEqual(["bash"], scenario.ExpectTools);
        Assert.AreSequenceEqual(["web"], scenario.RejectTools);
        Assert.AreEqual(12, scenario.MaxTurns);
        Assert.AreEqual(4000, scenario.MaxTokens);
        Assert.AreEqual("../../plugins/demo/skills/migrate", Assert.ContainsSingle(scenario.Setup!.AdditionalRequiredSkills!));
        Assert.AreEqual("helper-agent", Assert.ContainsSingle(scenario.Setup.AdditionalRequiredAgents!));
        var file = Assert.ContainsSingle(scenario.Setup.Files!);
        Assert.AreEqual("fixtures/project", file.Source);
        Assert.AreEqual("Project", file.Path);
        Assert.AreEqual("git init -q", Assert.ContainsSingle(scenario.Setup.Commands!));
        Assert.AreEqual(2, scenario.Assertions!.Count);
        Assert.AreEqual(AssertionType.FileContains, scenario.Assertions[0].Type);
        Assert.AreEqual(AssertionType.RunCommandAndAssert, scenario.Assertions[1].Type);
        var command = scenario.Assertions[1].CommandArgs;
        Assert.IsNotNull(command);
        Assert.AreEqual(300, command!.Timeout);
        Assert.AreEqual("Passed!", command.ExpectedStdOutContains);
        Assert.AreEqual("Passed", command.ExpectedStdOutMatches);
        if (OperatingSystem.IsWindows())
        {
            Assert.AreEqual("/d /s /c \"dotnet test Project\"", command.CommandArguments);
            Assert.IsNull(command.ArgumentList);
        }
        else
        {
            Assert.AreSequenceEqual(["-c", "dotnet test Project"], command.ArgumentList!);
            Assert.IsNull(command.CommandArguments);
        }
    }

    [TestMethod]
    public async Task VallyRunCommandPreservesNestedQuotes()
    {
        var shellCommand = OperatingSystem.IsWindows()
            ? """powershell -NoLogo -NoProfile -Command "$value = 'quoted value'; if ($value -ne 'quoted value') { exit 1 }" """
            : """sh -c "test \"quoted value\" = \"quoted value\"" """;
        var yaml = $$"""
            name: nested-quotes
            stimuli:
              - name: Execute nested quotes
                prompt: Run the check.
                graders:
                  - type: run-command
                    config:
                      command: >-
                        {{shellCommand}}
                      expected_exit_code: 0
            """;
        var config = EvalSchema.ParseEvalConfigFlexible(yaml);
        var assertion = Assert.ContainsSingle(Assert.ContainsSingle(config!.Scenarios).Assertions!);
        var command = assertion.CommandArgs;
        Assert.IsNotNull(command);
        if (OperatingSystem.IsWindows())
            Assert.Contains(shellCommand, command!.CommandArguments!);
        else
            Assert.AreEqual(shellCommand, command!.ArgumentList![1]);

        var workDir = Path.Combine(Path.GetTempPath(), $"nested-command-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workDir);
        try
        {
            var result = Assert.ContainsSingle(await AssertionEvaluator.EvaluateAssertions(
                [assertion], "", workDir));
            Assert.IsTrue(result.Passed, result.Message);
        }
        finally
        {
            Directory.Delete(workDir, true);
        }
    }

    [TestMethod]
    [DataRow("""powershell -NoLogo -NoProfile -Command "exit 7" """)]
    [DataRow("""powershell -NoLogo -NoProfile -Command "throw 'must fail'" """)]
    public async Task VallyRunCommandPreservesQuotedWindowsFailures(string shellCommand)
    {
        if (!OperatingSystem.IsWindows())
            return;

        var yaml = $$"""
            name: quoted-windows-failure
            stimuli:
              - name: Execute quoted failure
                prompt: Run the check.
                graders:
                  - type: run-command
                    config:
                      command: >-
                        {{shellCommand}}
            """;
        var config = EvalSchema.ParseEvalConfigFlexible(yaml);
        var assertion = Assert.ContainsSingle(Assert.ContainsSingle(config!.Scenarios).Assertions!);
        var workDir = Path.Combine(Path.GetTempPath(), $"quoted-command-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workDir);
        try
        {
            var result = Assert.ContainsSingle(await AssertionEvaluator.EvaluateAssertions(
                [assertion], "", workDir));
            Assert.IsFalse(result.Passed, result.Message);
        }
        finally
        {
            Directory.Delete(workDir, true);
        }
    }

    [TestMethod]
    [DataRow("90", 90)]
    [DataRow("1500ms", 2)]
    [DataRow("2m", 120)]
    [DataRow("1h", 3600)]
    public void ParsesVallyDurations(string value, int expectedSeconds)
    {
        Assert.AreEqual(expectedSeconds, EvalSchema.ParseDurationSeconds(value));
    }

    [TestMethod]
    [DataRow("2147483648s")]
    [DataRow("9223372036854775807m")]
    [DataRow("9223372036854775807h")]
    public void RejectsVallyDurationsThatOverflowSeconds(string value)
    {
        var error = Assert.ThrowsExactly<InvalidOperationException>(
            () => EvalSchema.ParseDurationSeconds(value));

        Assert.Contains("exceeds the supported maximum", error.Message);
    }
}

[TestClass]
public class ValidateEvalConfigTests
{
    [TestMethod]
    public void ReturnsSuccessForValidConfig()
    {
        var yaml = """
            scenarios:
              - name: "Test"
                prompt: "Do it"
            """;
        var result = EvalSchema.ValidateEvalConfig(yaml);
        Assert.IsTrue(result.Success);
    }

    [TestMethod]
    public void ReturnsErrorsForInvalidConfig()
    {
        var yaml = "scenarios: \"not-an-array\"";
        var result = EvalSchema.ValidateEvalConfig(yaml);
        Assert.IsFalse(result.Success);
        Assert.IsNotNull(result.Errors);
        Assert.IsTrue(result.Errors!.Count > 0);
    }
}
