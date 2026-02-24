using SkillValidator.Models;
using SkillValidator.Services;

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
    public void AcceptsEmptyScenarios()
    {
        var yaml = "scenarios: []";
        var result = EvalSchema.ParseEvalConfig(yaml);
        Assert.Empty(result.Scenarios);
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
