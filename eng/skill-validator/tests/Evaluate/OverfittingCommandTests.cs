using System.Text.Json;
using SkillValidator;
using SkillValidator.Evaluate;

namespace SkillValidator.Tests;

public class OverfittingCommandTests
{
    [Theory]
    [InlineData("tests/dotnet-msbuild/build-perf-baseline/eval.yaml", "dotnet-msbuild", "build-perf-baseline")]
    [InlineData("tests/dotnet/csharp-scripts/eval.yaml", "dotnet", "csharp-scripts")]
    public void DeriveIdentity_ExtractsPluginAndSkillFromNestedEvalPath(string evalPath, string expectedPlugin, string expectedSkill)
    {
        // Normalize to the platform separator so the test runs on Windows and Linux.
        var native = evalPath.Replace('/', Path.DirectorySeparatorChar);

        var (plugin, skill) = OverfittingCommand.DeriveIdentity(native);

        Assert.Equal(expectedPlugin, plugin);
        Assert.Equal(expectedSkill, skill);
    }

    [Fact]
    public void OverfittingEntry_SerializesToCamelCaseWithStringSeverity()
    {
        var result = new OverfittingResult(
            Score: 0.42,
            Severity: OverfittingSeverity.Moderate,
            RubricAssessments: new List<RubricOverfitAssessment>
            {
                new("sc1", "criterion1", "vocabulary", 0.8, "Tests exact wording"),
            },
            AssertionAssessments: new List<AssertionOverfitAssessment>
            {
                new("sc1", "output_matches: foo", "narrow", 0.9, "Narrow match"),
            },
            PromptAssessments: new List<PromptOverfitAssessment>(),
            CrossScenarioIssues: new List<string>(),
            OverallReasoning: "example reasoning");

        var entry = new OverfittingCommand.OverfittingEntry("dotnet-msbuild", "build-perf-baseline", result);

        var json = JsonSerializer.Serialize(entry, SkillValidatorJsonContext.Default.OverfittingEntry);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // Top-level keys are camelCase.
        Assert.Equal("dotnet-msbuild", root.GetProperty("plugin").GetString());
        Assert.Equal("build-perf-baseline", root.GetProperty("skill").GetString());

        var overfit = root.GetProperty("overfittingResult");
        Assert.Equal(0.42, overfit.GetProperty("score").GetDouble(), 3);

        // Severity must serialize as a string, not a number (dashboard reads it as a string).
        var severity = overfit.GetProperty("severity");
        Assert.Equal(JsonValueKind.String, severity.ValueKind);
        Assert.Equal("Moderate", severity.GetString());

        // Nested collections use camelCase and preserve the rubric scenario field.
        var rubric = overfit.GetProperty("rubricAssessments");
        Assert.Equal(JsonValueKind.Array, rubric.ValueKind);
        Assert.Equal("sc1", rubric[0].GetProperty("scenario").GetString());
    }

    [Fact]
    public void OverfittingEntryList_SerializesAsArray()
    {
        var result = new OverfittingResult(
            0.1,
            OverfittingSeverity.Low,
            new List<RubricOverfitAssessment>(),
            new List<AssertionOverfitAssessment>(),
            new List<PromptOverfitAssessment>(),
            new List<string>(),
            "ok");

        var list = new List<OverfittingCommand.OverfittingEntry>
        {
            new("plugin-a", "skill-a", result),
        };

        var json = JsonSerializer.Serialize(list, SkillValidatorJsonContext.Default.ListOverfittingEntry);

        using var doc = JsonDocument.Parse(json);
        Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);
        Assert.Equal("skill-a", doc.RootElement[0].GetProperty("skill").GetString());
        Assert.Equal("Low", doc.RootElement[0].GetProperty("overfittingResult").GetProperty("severity").GetString());
    }

    [Fact]
    public void ParseEvalConfigFlexible_ReadsVallyStimuliFormat()
    {
        // The current on-disk eval.yaml schema (Vally-native): stimuli with a
        // per-stimulus prompt, graders, and rubric. The legacy ParseEvalConfig
        // rejects this ("must have at least one scenario"); the flexible parser
        // must map it so the overfitting judge can run.
        const string yaml = """
            name: sample
            description: A sample eval
            type: capability
            config:
              timeout: 10m
            stimuli:
              - name: First stimulus
                prompt: Do the thing without naming the skill.
                graders:
                  - type: output-contains
                    config:
                      substring: global.json
                  - type: output-matches
                    config:
                      pattern: (paths|committed)
                  - type: prompt
                rubric:
                  - The agent achieved the outcome
                  - The agent explained cleanup
              - name: Second stimulus
                prompt: Another request.
                graders:
                  - type: output-contains
                    config:
                      substring: dotnet-install
            """;

        var cfg = EvalSchema.ParseEvalConfigFlexible(yaml);

        Assert.NotNull(cfg);
        Assert.Equal(2, cfg!.Scenarios.Count);

        var first = cfg.Scenarios[0];
        Assert.Equal("First stimulus", first.Name);
        Assert.Equal("Do the thing without naming the skill.", first.Prompt);

        // Rubric maps straight through (the judge classifies these for overfitting).
        Assert.NotNull(first.Rubric);
        Assert.Equal(2, first.Rubric!.Count);
        Assert.Contains("The agent achieved the outcome", first.Rubric);

        // Output graders map to assertions; the LLM-rubric "prompt" grader is skipped.
        Assert.NotNull(first.Assertions);
        Assert.Equal(2, first.Assertions!.Count);
        Assert.Equal(AssertionType.OutputContains, first.Assertions[0].Type);
        Assert.Equal("global.json", first.Assertions[0].Value);
        Assert.Equal(AssertionType.OutputMatches, first.Assertions[1].Type);
        Assert.Equal("(paths|committed)", first.Assertions[1].Pattern);
    }

    [Fact]
    public void ParseEvalConfigFlexible_ReturnsNullWhenNoStimuliOrScenarios()
    {
        const string yaml = """
            name: sample
            description: An eval with neither stimuli nor scenarios
            type: capability
            """;

        Assert.Null(EvalSchema.ParseEvalConfigFlexible(yaml));
    }
}
