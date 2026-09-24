using System.Text.Json;
using SkillValidator.Evaluate;

namespace SkillValidator.Tests;

[TestClass]
public class ResultsSchemaTests
{
    public TestContext TestContext { get; set; } = null!;

    private const string UnversionedResultsJson = """
        {
          "model": "executor",
          "judgeModel": "judge",
          "timestamp": "2026-08-27T00:00:00Z",
          "verdicts": [
            {
              "skillName": "example",
              "skillPath": "plugins/example/skills/example",
              "passed": false,
              "scenarios": [],
              "overallImprovementScore": 0,
              "reason": "legacy"
            }
          ]
        }
        """;

    [TestMethod]
    public void SerializedResultsIdentifyTheLegacySkillValidatorSchema()
    {
        var verdict = CreateVerdict();
        var output = new ResultsOutput
        {
            Model = "executor",
            JudgeModel = "judge",
            Timestamp = "2026-08-27T00:00:00.0000000Z",
            Verdicts = [verdict],
        };

        var json = JsonSerializer.Serialize(output, SkillValidatorJsonContext.Default.ResultsOutput);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.AreEqual(LegacySkillValidatorResultsSchema.Owner, root.GetProperty("schemaOwner").GetString());
        Assert.AreEqual(LegacySkillValidatorResultsSchema.CurrentVersion, root.GetProperty("schemaVersion").GetInt32());
        var serializedVerdict = root.GetProperty("verdicts")[0];
        Assert.AreEqual(LegacySkillValidatorResultsSchema.Owner, serializedVerdict.GetProperty("schemaOwner").GetString());
        Assert.AreEqual(
            LegacySkillValidatorResultsSchema.CurrentVersion,
            serializedVerdict.GetProperty("schemaVersion").GetInt32());
    }

    [TestMethod]
    public void UnversionedLegacyResultsRemainSupported()
    {
        var data = JsonSerializer.Deserialize(
            UnversionedResultsJson,
            SkillValidatorJsonContext.Default.ConsolidateData);

        Assert.IsNotNull(data);
        LegacySkillValidatorResultsSchema.EnsureSupported(data.SchemaOwner, data.SchemaVersion);
        var verdict = Assert.ContainsSingle(data.Verdicts!);
        Assert.AreEqual(LegacySkillValidatorResultsSchema.Owner, verdict.SchemaOwner);
        Assert.AreEqual(LegacySkillValidatorResultsSchema.CurrentVersion, verdict.SchemaVersion);
    }

    [TestMethod]
    public async Task ConsolidationAcceptsUnversionedLegacyResults()
    {
        var paths = CreateTempPaths();
        try
        {
            await File.WriteAllTextAsync(paths.Input, UnversionedResultsJson, TestContext.CancellationToken);

            var exitCode = await ConsolidateCommand.Consolidate([paths.Input], paths.Output);

            Assert.AreEqual(0, exitCode);
            var markdown = await File.ReadAllTextAsync(paths.Output, TestContext.CancellationToken);
            Assert.Contains("Skill Validation Results", markdown);
        }
        finally
        {
            DeleteTempPaths(paths);
        }
    }

    [TestMethod]
    [DataRow(3)]
    [DataRow(4)]
    public void VallyAdapterSchemasAreRejectedByLegacyConsolidation(int schemaVersion)
    {
        var error = Assert.ThrowsExactly<InvalidDataException>(
            () => LegacySkillValidatorResultsSchema.EnsureSupported(null, schemaVersion));

        Assert.Contains("Vally adapter results use a separate schema", error.Message);
    }

    [TestMethod]
    public void ExplicitSchemaVersionZeroIsRejected()
    {
        var json = """
            {
              "schemaOwner": "skill-validator",
              "schemaVersion": 0,
              "skillName": "example",
              "skillPath": "plugins/example/skills/example",
              "passed": false,
              "scenarios": [],
              "overallImprovementScore": 0,
              "reason": "invalid version"
            }
            """;
        var verdict = JsonSerializer.Deserialize(json, SkillValidatorJsonContext.Default.SkillVerdict);

        Assert.IsNotNull(verdict);
        var error = Assert.ThrowsExactly<InvalidDataException>(
            () => LegacySkillValidatorResultsSchema.EnsureSupported(
                verdict.SchemaOwner,
                verdict.SchemaVersion));
        Assert.Contains("'0'", error.Message);
    }

    [TestMethod]
    public async Task ConsolidationFailsForVallyAdapterResults()
    {
        var paths = CreateTempPaths();
        try
        {
            await File.WriteAllTextAsync(
                paths.Input,
                """{"schemaVersion":3,"verdicts":[]}""",
                TestContext.CancellationToken);

            var exitCode = await ConsolidateCommand.Consolidate([paths.Input], paths.Output);

            Assert.AreEqual(1, exitCode);
        }
        finally
        {
            DeleteTempPaths(paths);
        }
    }

    [TestMethod]
    public async Task ConsolidationFailsForNullJsonRoot()
    {
        var paths = CreateTempPaths();
        try
        {
            await File.WriteAllTextAsync(
                paths.Input,
                "null",
                TestContext.CancellationToken);

            var exitCode = await ConsolidateCommand.Consolidate([paths.Input], paths.Output);

            Assert.AreEqual(1, exitCode);
        }
        finally
        {
            DeleteTempPaths(paths);
        }
    }

    private static SkillVerdict CreateVerdict() => new()
    {
        SkillName = "example",
        SkillPath = "plugins/example/skills/example",
        Passed = false,
        Scenarios = [],
        OverallImprovementScore = 0,
        Reason = "test",
    };

    private static (string Directory, string Input, string Output) CreateTempPaths()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"skill-validator-schema-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return (directory, Path.Combine(directory, "results.json"), Path.Combine(directory, "summary.md"));
    }

    private static void DeleteTempPaths((string Directory, string Input, string Output) paths)
    {
        if (Directory.Exists(paths.Directory))
            Directory.Delete(paths.Directory, recursive: true);
    }
}
