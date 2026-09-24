using System.Text.Json;
using SkillValidator;
using SkillValidator.Evaluate;

namespace SkillValidator.Tests;

[TestClass]
public class OverfittingCommandTests
{
    [TestMethod]
    [DataRow("tests/dotnet-msbuild/build-perf-baseline/eval.yaml", "dotnet-msbuild", "build-perf-baseline")]
    [DataRow("tests/dotnet/csharp-scripts/eval.yaml", "dotnet", "csharp-scripts")]
    public void DeriveIdentity_ExtractsPluginAndSkillFromNestedEvalPath(string evalPath, string expectedPlugin, string expectedSkill)
    {
        // Normalize to the platform separator so the test runs on Windows and Linux.
        var native = evalPath.Replace('/', Path.DirectorySeparatorChar);

        var (plugin, skill) = OverfittingCommand.DeriveIdentity(native);

        Assert.AreEqual(expectedPlugin, plugin);
        Assert.AreEqual(expectedSkill, skill);
    }

    [TestMethod]
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
        Assert.AreEqual("dotnet-msbuild", root.GetProperty("plugin").GetString());
        Assert.AreEqual("build-perf-baseline", root.GetProperty("skill").GetString());

        var overfit = root.GetProperty("overfittingResult");
        Assert.AreEqual(0.42, Math.Round(overfit.GetProperty("score").GetDouble(), 3));

        // Severity must serialize as a string, not a number (dashboard reads it as a string).
        var severity = overfit.GetProperty("severity");
        Assert.AreEqual(JsonValueKind.String, severity.ValueKind);
        Assert.AreEqual("Moderate", severity.GetString());

        // Nested collections use camelCase and preserve the rubric scenario field.
        var rubric = overfit.GetProperty("rubricAssessments");
        Assert.AreEqual(JsonValueKind.Array, rubric.ValueKind);
        Assert.AreEqual("sc1", rubric[0].GetProperty("scenario").GetString());
    }

    [TestMethod]
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
        Assert.AreEqual(JsonValueKind.Array, doc.RootElement.ValueKind);
        Assert.AreEqual("skill-a", doc.RootElement[0].GetProperty("skill").GetString());
        Assert.AreEqual("Low", doc.RootElement[0].GetProperty("overfittingResult").GetProperty("severity").GetString());
    }

    [TestMethod]
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

        Assert.IsNotNull(cfg);
        Assert.AreEqual(2, cfg!.Scenarios.Count);

        var first = cfg.Scenarios[0];
        Assert.AreEqual("First stimulus", first.Name);
        Assert.AreEqual("Do the thing without naming the skill.", first.Prompt);

        // Rubric maps straight through (the judge classifies these for overfitting).
        Assert.IsNotNull(first.Rubric);
        Assert.AreEqual(2, first.Rubric!.Count);
        Assert.Contains("The agent achieved the outcome", first.Rubric);

        // Output graders map to assertions; the LLM-rubric "prompt" grader is skipped.
        Assert.IsNotNull(first.Assertions);
        Assert.AreEqual(2, first.Assertions!.Count);
        Assert.AreEqual(AssertionType.OutputContains, first.Assertions[0].Type);
        Assert.AreEqual("global.json", first.Assertions[0].Value);
        Assert.AreEqual(AssertionType.OutputMatches, first.Assertions[1].Type);
        Assert.AreEqual("(paths|committed)", first.Assertions[1].Pattern);
    }

    [TestMethod]
    public void ParseEvalConfigFlexible_ReturnsNullWhenNoStimuliOrScenarios()
    {
        const string yaml = """
            name: sample
            description: An eval with neither stimuli nor scenarios
            type: capability
            """;

        Assert.IsNull(EvalSchema.ParseEvalConfigFlexible(yaml));
    }
}
