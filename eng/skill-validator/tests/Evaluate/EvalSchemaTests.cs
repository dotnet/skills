using SkillValidator.Evaluate;
using SkillValidator.Shared;

namespace SkillValidator.Tests;

public class ParseEvalConfigTests
{
    [Fact]
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

        Assert.Single(config.Scenarios);
        Assert.Equal("Test scenario", config.Scenarios[0].Name);
        Assert.Equal(60, config.Scenarios[0].Timeout);
    }

    [Fact]
    public void AppliesDefaultTimeout()
    {
        var yaml = """
            scenarios:
              - name: "Test"
                prompt: "Do it"
            """;
        var config = EvalSchema.ParseEvalConfig(yaml);

        Assert.Equal(120, config.Scenarios[0].Timeout);
    }

    [Fact]
    public void RejectsEmptyScenarios()
    {
        var yaml = "scenarios: []";
        var ex = Assert.Throws<InvalidOperationException>(() => EvalSchema.ParseEvalConfig(yaml));
        Assert.Contains("at least one scenario", ex.Message);
    }

    [Fact]
    public void RejectsMissingPrompt()
    {
        var yaml = """
            scenarios:
              - name: "Test"
            """;
        Assert.Throws<InvalidOperationException>(() => EvalSchema.ParseEvalConfig(yaml));
    }

    [Fact]
    public void RejectsInvalidAssertionType()
    {
        var yaml = """
            scenarios:
              - name: "Test"
                prompt: "Do it"
                assertions:
                  - type: "invalid_type"
            """;
        Assert.Throws<InvalidOperationException>(() => EvalSchema.ParseEvalConfig(yaml));
    }

    [Fact]
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
        Assert.Equal(AssertionType.FileContains, config.Scenarios[0].Assertions![0].Type);
    }

    [Fact]
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
        Assert.Equal(["bash"], s.ExpectTools);
        Assert.Equal(["create_file"], s.RejectTools);
        Assert.Equal(10, s.MaxTurns);
        Assert.Equal(5000, s.MaxTokens);
    }

    [Fact]
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
        Assert.NotNull(setup);
        Assert.True(setup!.CopyTestFiles);
        Assert.NotNull(setup.Commands);
        Assert.Equal(2, setup.Commands!.Count);
        Assert.Equal("dotnet build /bl:build.binlog", setup.Commands[0]);
    }

    [Fact]
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

        Assert.Equal(1, config.MaxParallelScenarios);
        Assert.Equal(2, config.MaxParallelRuns);
        Assert.Single(config.Scenarios);
    }

    [Fact]
    public void ConfigSectionIsOptional()
    {
        var yaml = """
            scenarios:
              - name: "Test"
                prompt: "Do it"
            """;
        var config = EvalSchema.ParseEvalConfig(yaml);

        Assert.Null(config.MaxParallelScenarios);
        Assert.Null(config.MaxParallelRuns);
    }

    [Fact]
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

        Assert.Equal(2, config.MaxParallelScenarios);
        Assert.Null(config.MaxParallelRuns);
    }

    [Fact]
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
        Assert.Equal(AssertionType.RunCommandAndAssert, assertion.Type);
        Assert.NotNull(assertion.CommandArgs);
        var cmd = assertion.CommandArgs!;
        Assert.Equal("dotnet", cmd.CommandToRun);
        Assert.Equal("build", cmd.CommandArguments);
        Assert.Equal(0, cmd.ExpectedExitCode);
        Assert.Equal("Build succeeded", cmd.ExpectedStdOutContains);
        Assert.Equal("warning", cmd.ExpectedStdErrorContains);
        Assert.Equal("Build \\w+", cmd.ExpectedStdOutMatches);
        Assert.Equal("warn.*", cmd.ExpectedStdErrorMatches);
        Assert.Equal(60, cmd.Timeout);
    }

    [Fact]
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
        var ex = Assert.Throws<InvalidOperationException>(() => EvalSchema.ParseEvalConfig(yaml));
        Assert.Contains("command_to_run", ex.Message);
    }

    [Fact]
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
        var ex = Assert.Throws<InvalidOperationException>(() => EvalSchema.ParseEvalConfig(yaml));
        Assert.Contains("expected_exit_code", ex.Message);
        Assert.Contains("expected_std_output_contains", ex.Message);
    }

    [Fact]
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

        Assert.NotNull(config);
        var scenario = Assert.Single(config!.Scenarios);
        Assert.Equal(1200, scenario.Timeout);
        Assert.False(scenario.ExpectActivation);
        Assert.Equal(["bash"], scenario.ExpectTools);
        Assert.Equal(["web"], scenario.RejectTools);
        Assert.Equal(12, scenario.MaxTurns);
        Assert.Equal(4000, scenario.MaxTokens);
        Assert.Equal("../../plugins/demo/skills/migrate", Assert.Single(scenario.Setup!.AdditionalRequiredSkills!));
        Assert.Equal("helper-agent", Assert.Single(scenario.Setup.AdditionalRequiredAgents!));
        var file = Assert.Single(scenario.Setup.Files!);
        Assert.Equal("fixtures/project", file.Source);
        Assert.Equal("Project", file.Path);
        Assert.Equal("git init -q", Assert.Single(scenario.Setup.Commands!));
        Assert.Equal(2, scenario.Assertions!.Count);
        Assert.Equal(AssertionType.FileContains, scenario.Assertions[0].Type);
        Assert.Equal(AssertionType.RunCommandAndAssert, scenario.Assertions[1].Type);
        var command = scenario.Assertions[1].CommandArgs;
        Assert.NotNull(command);
        Assert.Equal(300, command!.Timeout);
        Assert.Equal("Passed!", command.ExpectedStdOutContains);
        Assert.Equal("Passed", command.ExpectedStdOutMatches);
        if (OperatingSystem.IsWindows())
        {
            Assert.Equal("/d /s /c \"dotnet test Project\"", command.CommandArguments);
            Assert.Null(command.ArgumentList);
        }
        else
        {
            Assert.Equal(["-c", "dotnet test Project"], command.ArgumentList!);
            Assert.Null(command.CommandArguments);
        }
    }

    [Fact]
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
        var assertion = Assert.Single(Assert.Single(config!.Scenarios).Assertions!);
        var command = assertion.CommandArgs;
        Assert.NotNull(command);
        if (OperatingSystem.IsWindows())
            Assert.Contains(shellCommand, command!.CommandArguments);
        else
            Assert.Equal(shellCommand, command!.ArgumentList![1]);

        var workDir = Path.Combine(Path.GetTempPath(), $"nested-command-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workDir);
        try
        {
            var result = Assert.Single(await AssertionEvaluator.EvaluateAssertions(
                [assertion], "", workDir));
            Assert.True(result.Passed, result.Message);
        }
        finally
        {
            Directory.Delete(workDir, true);
        }
    }

    [Theory]
    [InlineData("""powershell -NoLogo -NoProfile -Command "exit 7" """)]
    [InlineData("""powershell -NoLogo -NoProfile -Command "throw 'must fail'" """)]
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
        var assertion = Assert.Single(Assert.Single(config!.Scenarios).Assertions!);
        var workDir = Path.Combine(Path.GetTempPath(), $"quoted-command-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workDir);
        try
        {
            var result = Assert.Single(await AssertionEvaluator.EvaluateAssertions(
                [assertion], "", workDir));
            Assert.False(result.Passed, result.Message);
        }
        finally
        {
            Directory.Delete(workDir, true);
        }
    }

    [Theory]
    [InlineData("90", 90)]
    [InlineData("1500ms", 2)]
    [InlineData("2m", 120)]
    [InlineData("1h", 3600)]
    public void ParsesVallyDurations(string value, int expectedSeconds)
    {
        Assert.Equal(expectedSeconds, EvalSchema.ParseDurationSeconds(value));
    }

    [Theory]
    [InlineData("2147483648s")]
    [InlineData("9223372036854775807m")]
    [InlineData("9223372036854775807h")]
    public void RejectsVallyDurationsThatOverflowSeconds(string value)
    {
        var error = Assert.Throws<InvalidOperationException>(
            () => EvalSchema.ParseDurationSeconds(value));

        Assert.Contains("exceeds the supported maximum", error.Message);
    }
}

public class ValidateEvalConfigTests
{
    [Fact]
    public void ReturnsSuccessForValidConfig()
    {
        var yaml = """
            scenarios:
              - name: "Test"
                prompt: "Do it"
            """;
        var result = EvalSchema.ValidateEvalConfig(yaml);
        Assert.True(result.Success);
    }

    [Fact]
    public void ReturnsErrorsForInvalidConfig()
    {
        var yaml = "scenarios: \"not-an-array\"";
        var result = EvalSchema.ValidateEvalConfig(yaml);
        Assert.False(result.Success);
        Assert.NotNull(result.Errors);
        Assert.True(result.Errors!.Count > 0);
    }
}
