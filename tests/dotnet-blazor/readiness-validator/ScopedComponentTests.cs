using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using BlazorComponentReadiness.Validator.Assessment;
using BlazorComponentReadiness.Validator.Cli;
using BlazorComponentReadiness.Validator.Contracts;
using BlazorComponentReadiness.Validator.Evidence;
using BlazorComponentReadiness.Validator.Inputs;
using BlazorComponentReadiness.Validator.IO;
using BlazorComponentReadiness.Validator.Rendering;
using BlazorComponentReadiness.Validator.Validation;

internal static class ScopedComponentTests
{
    private static readonly string[] Retired =
        ["CI-02", "CI-03", "CI-04", "CI-09", "CI-10", "PERF-07", "PERF-08", "PERF-09", "PERF-10"];

    public static void Run(string repositoryRoot, string pluginRoot)
    {
        var root = Path.Combine(Environment.GetEnvironmentVariable("READINESS_TEST_ARTIFACTS") ??
            Path.Combine(repositoryRoot, "artifacts"), $"component-scope-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var source = AssessmentTests.CreateInputFixture(root);
        var input = source.Confirmed with { OwnerInputs = [] };
        var inputBytes = InputManifestService.Serialize(input);
        var inputPath = Path.Combine(root, "component.input.json");
        File.WriteAllBytes(inputPath, inputBytes);
        var assessment = AssessmentService.Initialize("component", root, input, inputBytes, "fancy-tree");
        var rubric = RubricLoader.Load();
        Assert(assessment.Rows.Count == 52 && assessment.Rows.All(row => row.Scope == "component-specific") &&
            assessment.SelectedIds.All(id => !Retired.Contains(id, StringComparer.Ordinal)) &&
            assessment.SelectedIds.Contains("BEQ-05", StringComparer.Ordinal), "closed standalone component selection");
        Assert(assessment.PackageReference is null, "no package prerequisite");

        TestRetiredInputs(root, input, inputBytes);
        TestSelection(root, input, assessment);

        var evidence = AssessmentTests.BuildEvidence(assessment.Identity);
        assessment = Complete(assessment, evidence);
        var assessmentPath = Path.Combine(root, "component.assessment.json");
        var evidencePath = Path.Combine(root, "component.evidence.json");
        File.WriteAllBytes(assessmentPath, AssessmentService.Serialize(assessment));
        File.WriteAllBytes(evidencePath, CanonicalEvidenceJson.SerializeBundle(evidence));
        var revisions = Path.Combine(root, "revisions");
        string[] render = ["report", "render", "--root", root, "--input", inputPath,
            "--assessment", assessmentPath, "--evidence", evidencePath, "--output", revisions];
        Cli(render, 0);
        var revisionPath = Path.Combine(revisions, "0001");
        var revision = RevisionService.VerifyRevision(root, revisionPath, null, null, true);
        var reader = Path.Combine(root, "reader");
        string[] readerRender = ["reader", "render", "--root", root, "--revision", revisionPath, "--output", reader];
        Cli(readerRender, 0);
        Cli(["reader", "verify", "--root", root, "--revision", revisionPath, "--output", reader], 0);
        TestExport(revision, reader);
        TestDisclosure(root, input, assessment, assessmentPath, evidence, evidencePath, render);
        TestSelectedCompanion(root, input, assessment);
        TestPolicyIsNotEvidence(root, input, pluginRoot);
        TestPublicationRaces(root, inputPath, assessmentPath, evidencePath, render, revisionPath);
        foreach (var version in new[] { "1.0.0", "1.0.1", "unknown" })
        {
            Reject(() => ReaderService.Build(root, revision, null, null, readerVersion: version),
                "old or unknown reader version");
        }
        Console.WriteLine("Standalone component scope, retired profiles, disclosure, selected evidence and publication controls passed.");
    }

    private static void TestRetiredInputs(string root, InputManifest input, byte[] inputBytes)
    {
        foreach (var (kind, basename) in new[]
        {
            ("scoped-component-profile-v1", "requirement-basis.json"),
            ("scoped-package-context-v1", "scoped-package.validation.json"),
            ("scoped-component-profile-v999", "unknown-profile.json"),
            ("untyped", "scoped-package.validation.json")
        })
        {
            var path = Path.Combine(root, basename);
            File.WriteAllText(path, "{}");
            var bytes = File.ReadAllBytes(path);
            var altered = input with
            {
                EvidenceInputs = input.EvidenceInputs.Append(
                    new(basename, kind, ContractJson.RawDigest(bytes), bytes.LongLength))
                    .OrderBy(item => item.Basename, StringComparer.Ordinal).ToArray()
            };
            var alteredBytes = InputManifestService.Serialize(altered);
            Reject(() => InputManifestService.Parse(alteredBytes), "retired profile/context cannot parse as current input");
            Reject(() => InputManifestService.Validate(altered, root, true), "direct input validation rejects retired metadata");
            Reject(() => AssessmentService.Initialize("component", root, altered, alteredBytes, "fancy-tree"),
                "retired marker cannot fall back to standalone selection");
        }

        var inputPath = Path.Combine(root, "retired-options.input.json");
        File.WriteAllBytes(inputPath, inputBytes);
        foreach (var option in new[] { "--package-context-revision", "--package-context-feedback" })
        {
            var output = Path.Combine(root, $"retired-option-{Guid.NewGuid():N}.json");
            Cli(["assessment", "init", "--kind", "component", "--root", root, "--input", inputPath,
                "--component", "fancy-tree", "--output", output, option,
                option.EndsWith("feedback", StringComparison.Ordinal) ? inputPath : root], 1);
            Assert(!File.Exists(output), "retired option publishes nothing");
        }
    }

    private static void TestSelection(string root, InputManifest input, ReadinessAssessment assessment)
    {
        var evidence = AssessmentTests.BuildEvidence(assessment.Identity);
        foreach (var invalid in new[]
        {
            assessment with { Rows = assessment.Rows.Skip(1).ToArray(), SelectedIds = assessment.SelectedIds.Skip(1).ToArray() },
            assessment with { Rows = [.. assessment.Rows, assessment.Rows[0]] },
            assessment with { Rows = assessment.Rows.Reverse().ToArray(), SelectedIds = assessment.SelectedIds.Reverse().ToArray() },
            assessment with { Rows = [assessment.Rows[0] with { Scope = "repository-wide" }, .. assessment.Rows.Skip(1)] }
        })
        {
            Reject(() => Validate(root, input, invalid, evidence), "selection drift");
            Reject(() => ReportService.RenderMarkdown(invalid, input, evidence), "direct renderer rejects selection drift");
        }
        foreach (var id in Retired.Concat(["LP-01", "TA-08"]))
        {
            var altered = assessment with
            {
                SelectedIds = [id, .. assessment.SelectedIds.Skip(1)],
                Rows = [assessment.Rows[0] with { Id = id }, .. assessment.Rows.Skip(1)]
            };
            Reject(() => Validate(root, input, altered, evidence), "retired or package row cannot replace component row");
        }
    }

    private static void TestExport(RevisionArtifacts revision, string reader)
    {
        var mapping = JsonNode.Parse(File.ReadAllBytes(Path.Combine(reader, "mapping.json")))!;
        var checks = mapping["groups"]!.AsArray().SelectMany(group => group!["checks"]!.AsArray()).ToArray();
        Assert(checks.Length == 52 &&
            checks.Select(row => row!["id"]!.GetValue<string>()).ToHashSet(StringComparer.Ordinal)
                .SetEquals(revision.Assessment.SelectedIds),
            "mapping preserves every selected check exactly once");
        var originals = JsonNode.Parse(revision.AssessmentBytes)!["rows"]!.AsArray()
            .ToDictionary(row => row!["id"]!.GetValue<string>(), row => row!.ToJsonString());
        foreach (var check in checks)
        {
            Assert(check!.ToJsonString() == originals[check["id"]!.GetValue<string>()], "all per-check fields preserved");
        }
        var files = Directory.GetFiles(reader, "*", SearchOption.AllDirectories);
        Assert(files.All(path => !path.EndsWith(".bin", StringComparison.Ordinal) &&
            !path.EndsWith(".nupkg", StringComparison.Ordinal) &&
            Path.GetFileName(path) is not ("input-manifest.json" or "component.evidence.json" or "requirement-basis.json")),
            "raw inputs and internal validation closure are not exported");
        Assert(File.ReadAllBytes(Path.Combine(reader, "technical", "component.assessment.json"))
            .SequenceEqual(revision.AssessmentBytes), "canonical assessment remains byte-identical");
        Assert(File.ReadAllBytes(Path.Combine(reader, "technical", "selected.evidence.json"))
            .SequenceEqual(revision.EvidenceBytes), "all-selected companion preserves exact current bundle");
        var report = File.ReadAllText(Path.Combine(reader, "report.md"));
        Assert(report.Contains("Check accounting", StringComparison.Ordinal) &&
            report.Contains("not a self-contained", StringComparison.OrdinalIgnoreCase),
            "accounting and retained-workspace limits are explicit");
    }

    private static void TestDisclosure(
        string root, InputManifest input, ReadinessAssessment assessment, string assessmentPath,
        EvidenceBundle evidence, string evidencePath, string[] render)
    {
        var original = File.ReadAllBytes(assessmentPath);
        foreach (var id in Retired.Concat(["LP-01", "TA-08"]))
        foreach (var text in new[]
        {
            id, id.ToLowerInvariant(), id.Replace("-", "&#45;", StringComparison.Ordinal),
            id.Replace("-", "&amp;#45;", StringComparison.Ordinal),
            id.Replace("-", "\\u002d", StringComparison.Ordinal),
            id.Replace("-", "\\-", StringComparison.Ordinal), id.Replace("-", "\u2011", StringComparison.Ordinal)
        })
        {
            var changed = assessment with
            {
                Rows = [assessment.Rows[0] with { Observation = text + " verified." }, .. assessment.Rows.Skip(1)]
            };
            Reject(() => ReportService.RenderMarkdown(changed, input, evidence), "encoded excluded material");
            File.WriteAllBytes(assessmentPath, AssessmentService.Serialize(changed));
            var output = Path.Combine(root, $"forbidden-{Guid.NewGuid():N}");
            Cli(ReplaceOutput(render, output), 1);
            Assert(!Directory.Exists(Path.Combine(output, "0001")), "disclosure failure creates no fallback report");
        }
        File.WriteAllBytes(assessmentPath, original);

        var broaderClaim = assessment with
        {
            Rows = [assessment.Rows[0] with { Observation = "All 112 checks are verified." }, .. assessment.Rows.Skip(1)]
        };
        Reject(() => ReportService.RenderMarkdown(broaderClaim, input, evidence),
            "current full catalog cannot be presented as component coverage");

        var feedback = Path.Combine(root, "excluded-feedback.md");
        File.WriteAllText(feedback, "# Assessment feedback\n\n| Requirement IDs | Feedback |\n|---|---|\n" +
            "| `SEC-10` | CI\\-09 verified. |\n");
        var feedbackOutput = Path.Combine(root, "forbidden-feedback");
        Cli([.. ReplaceOutput(render, feedbackOutput), "--feedback", feedback], 1);
        Assert(!Directory.Exists(Path.Combine(feedbackOutput, "0001")), "feedback cannot broaden output scope");

        var record = evidence.SourceLedgers.Single().Ledger.Records.Single();
        var changedDraft = new EvidenceRecordDraft("CI-09 was verified.", record.Applicability,
            record.Provenance, record.Supersedes);
        var changedLedger = EvidenceLedgerBuilder.BuildComponentLedger(assessment.Identity, [changedDraft]);
        var changedEvidence = EvidenceLedgerBuilder.BuildBundle(assessment.Identity,
            [changedLedger], [changedLedger.Records.Single().StableId]);
        var changedAssessment = Complete(assessment, changedEvidence);
        var changedOutput = Path.Combine(root, "selected-evidence-disclosure");
        File.WriteAllBytes(assessmentPath, AssessmentService.Serialize(changedAssessment));
        File.WriteAllBytes(evidencePath, CanonicalEvidenceJson.SerializeBundle(changedEvidence));
        Cli(ReplaceOutput(render, changedOutput), 0);
        var rejectedReader = Path.Combine(root, "forbidden-evidence-reader");
        Cli(["reader", "render", "--root", root, "--revision", Path.Combine(changedOutput, "0001"),
            "--output", rejectedReader], 1);
        Assert(!Directory.Exists(rejectedReader), "selected evidence cannot leak excluded material");
        File.WriteAllBytes(assessmentPath, original);
        File.WriteAllBytes(evidencePath, CanonicalEvidenceJson.SerializeBundle(evidence));
    }

    private static void TestSelectedCompanion(string root, InputManifest input, ReadinessAssessment assessment)
    {
        var bytes = Encoding.UTF8.GetBytes("Synthetic retained analysis with unexported dependencies.");
        File.WriteAllBytes(Path.Combine(root, "companion-analysis.txt"), bytes);
        input = input with
        {
            EvidenceInputs = input.EvidenceInputs.Append(new("companion-analysis.txt", "analysis",
                ContractJson.RawDigest(bytes), bytes.LongLength)).OrderBy(item => item.Basename, StringComparer.Ordinal).ToArray()
        };
        assessment = AssessmentService.Initialize("component", root, input, InputManifestService.Serialize(input), "fancy-tree");
        var draft = new EvidenceRecordDraft("The earlier synthetic observation was retained.",
            new("component-specific", "fancy-tree"),
            new(EvidenceIdentity.ReviewerGeneratedAnalysis, "companion-analysis.txt", "Synthetic analysis, no product execution.",
                "2026-09-21T12:00:00Z", ContractJson.RawDigest(bytes), "commitment-only"), []);
        var first = EvidenceLedgerBuilder.BuildComponentLedger(assessment.Identity, [draft]).Records.Single();
        var ledger = EvidenceLedgerBuilder.BuildComponentLedger(assessment.Identity,
            [draft, draft with { Claim = "The later observation corrects the earlier one.", Supersedes = [first.StableId] }]);
        var successor = ledger.Records.Single(record => record.Supersedes.Count != 0);
        var evidence = EvidenceLedgerBuilder.BuildBundle(assessment.Identity, [ledger], [successor.StableId]);
        assessment = Complete(assessment, evidence);
        Validate(root, input, assessment, evidence);
        var scope = ComponentReportScope.For(assessment)!;
        Assert(scope.BuildSelectedCompanion(evidence) is null,
            "an unexported supersession ancestor produces omission, not an invalid cropped ledger");
        var assessmentBytes = AssessmentService.Serialize(assessment);
        var inputBytes = InputManifestService.Serialize(input);
        var evidenceBytes = CanonicalEvidenceJson.SerializeBundle(evidence);
        var report = ReportService.RenderMarkdown(assessment, input, evidence);
        var manifest = ReportService.CreateManifest(assessment, assessmentBytes, input, inputBytes,
            evidence, evidenceBytes, report);
        var artifacts = new RevisionArtifacts(root, "component", input, inputBytes, assessment, assessmentBytes,
            evidence, evidenceBytes, report, manifest, ReportService.SerializeManifest(manifest));
        var output = ReaderService.Build(root, artifacts, null, null);
        Assert(!output.ContainsKey("technical/selected.evidence.json") &&
            Encoding.UTF8.GetString(output["evidence.md"]).Contains("omitted", StringComparison.OrdinalIgnoreCase),
            "reader truthfully discloses omitted dependency closure");
        Assert(!output.Values.Any(value => Encoding.UTF8.GetString(value).Contains(
            "Synthetic retained analysis with unexported dependencies.", StringComparison.Ordinal)),
            "raw analysis bytes stay internal");
    }

    private static void TestPolicyIsNotEvidence(string root, InputManifest input, string pluginRoot)
    {
        foreach (var definition in new[] { "rubric.json", "requirement-basis.json" })
        {
            var bytes = File.ReadAllBytes(Path.Combine(pluginRoot, "skills", "blazor-component-readiness", "references", definition));
            var basename = "renamed-definition-" + definition;
            File.WriteAllBytes(Path.Combine(root, basename), bytes);
            var policyInput = input with
            {
                EvidenceInputs = input.EvidenceInputs.Append(new(basename, "reference-document",
                    ContractJson.RawDigest(bytes), bytes.LongLength))
                    .OrderBy(item => item.Basename, StringComparer.Ordinal).ToArray()
            };
            var assessment = AssessmentService.Initialize("component", root, policyInput,
                InputManifestService.Serialize(policyInput), "fancy-tree");
            var draft = new EvidenceRecordDraft("The supplied definition is incorrectly claimed as product evidence.",
                new("component-specific", "fancy-tree"),
                new(EvidenceIdentity.ReviewerGeneratedAnalysis, basename, "Read the supplied requirement definition.",
                    "2026-09-22T12:00:00Z", ContractJson.RawDigest(bytes), "commitment-only"), []);
            var ledger = EvidenceLedgerBuilder.BuildComponentLedger(assessment.Identity, [draft]);
            var evidence = EvidenceLedgerBuilder.BuildBundle(assessment.Identity, [ledger], [ledger.Records.Single().StableId]);
            assessment = Complete(assessment, evidence);
            Reject(() => Validate(root, policyInput, assessment, evidence), "policy bytes cannot satisfy component requirements");
            Reject(() => ReportService.RenderMarkdown(assessment, policyInput, evidence), "direct report rejects definition evidence");
        }
    }

    private static void TestPublicationRaces(
        string root, string inputPath, string assessmentPath, string evidencePath,
        string[] render, string revision)
    {
        var mutable = Path.Combine(root, "docs.html");
        var bytes = File.ReadAllBytes(mutable);
        var output = Path.Combine(root, "race-report");
        try
        {
            ReportCommand.BeforePublishForTests = () => File.AppendAllText(mutable, "changed");
            Cli(ReplaceOutput(render, output), 1);
            Assert(!Directory.Exists(Path.Combine(output, "0001")), "input mutation publishes no revision");
        }
        finally
        {
            ReportCommand.BeforePublishForTests = null;
            File.WriteAllBytes(mutable, bytes);
        }
        var reader = Path.Combine(root, "race-reader");
        try
        {
            ReaderCommand.BeforePublishForTests = () => File.AppendAllText(mutable, "changed");
            Cli(["reader", "render", "--root", root, "--revision", revision, "--output", reader], 1);
            Assert(!Directory.Exists(reader), "input mutation publishes no reader");
        }
        finally
        {
            ReaderCommand.BeforePublishForTests = null;
            File.WriteAllBytes(mutable, bytes);
        }
    }

    private static ReadinessAssessment Complete(ReadinessAssessment assessment, EvidenceBundle evidence) =>
        assessment with
        {
            Rows = assessment.Rows.Select((row, index) => row with
            {
                Status = "not tested", AssessmentFollowUp = "Synthetic unperformed check.",
                EvidenceIds = index == 0 ? evidence.Selection.Select(item => item.EvidenceId).ToArray() : []
            }).ToArray(),
            CompletionState = "complete"
        };

    private static void Validate(string root, InputManifest input, ReadinessAssessment assessment, EvidenceBundle evidence) =>
        AssessmentService.Validate(root, assessment, AssessmentService.Serialize(assessment),
            input, InputManifestService.Serialize(input), evidence);

    private static string[] ReplaceOutput(string[] command, string output)
    {
        var copy = command.ToArray();
        copy[Array.IndexOf(copy, "--output") + 1] = output;
        return copy;
    }

    private static void Cli(string[] arguments, int expected)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        Assert(CliApplication.Run(arguments, output, error) == expected,
            $"Expected {expected} for {string.Join(' ', arguments.Take(2))}: {error}");
    }

    private static void Reject(Action action, string name)
    {
        try
        {
            action();
        }
        catch (DeterministicValidationException)
        {
            return;
        }
        throw new InvalidOperationException("Expected rejection: " + name);
    }

    private static void Assert(bool condition, string name)
    {
        if (!condition)
        {
            throw new InvalidOperationException(name);
        }
    }
}
