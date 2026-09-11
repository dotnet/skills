using System.Net;
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
    private const string ProfileKind = "scoped-component-profile-v1";
    private const string ContextKind = "scoped-package-context-v1";
    private const string ProfileName = "requirement-basis.json";
    private const string ContextName = "scoped-package.validation.json";
    private const string Marker = "TOOLING TESTS / synthetic";
    private const string SelectedDigest = "2f0c862398f58aa0cef426cb8a2b4c46fa33e2d907a294bd46d7f1fa3b1f4122";
    private static readonly Sha256Digest ZeroDigest = new("sha256", new string('0', 64));
    private static readonly string[] Excluded =
        ["BEQ-05", "PERF-07", "PERF-08", "PERF-09", "PERF-10", "CI-02", "CI-03", "CI-04", "CI-09", "CI-10", "TA-08"];
    private static readonly string[] ExpectedIds =
    [
        "SEC-10", "SEC-11", "SEC-12", "SEC-13",
        "A11Y-01", "A11Y-02", "A11Y-03", "A11Y-04", "A11Y-05", "A11Y-06",
        "A11Y-07", "A11Y-08", "A11Y-09", "A11Y-10", "A11Y-11", "A11Y-12",
        "BEQ-01", "BEQ-02", "BEQ-03", "BEQ-04", "BEQ-06", "BEQ-07", "BEQ-08", "BEQ-09",
        "BEQ-10", "BEQ-11", "BEQ-12", "BEQ-13", "BEQ-14", "BEQ-15", "BEQ-16", "BEQ-17",
        "BEQ-18", "BEQ-19", "BEQ-20", "BEQ-22", "BEQ-23",
        "TA-01", "TA-02", "TA-03", "TA-04", "TA-05", "TA-06",
        "PERF-01", "PERF-02", "PERF-03", "PERF-04", "PERF-05", "PERF-06", "CI-11", "SUP-09"
    ];

    public static void Run(string repositoryRoot, string pluginRoot)
    {
        var previous = Environment.GetEnvironmentVariable("READINESS_SKILL_ROOT");
        var skillRoot = Path.Combine(pluginRoot, "skills", "blazor-component-readiness");
        Environment.SetEnvironmentVariable("READINESS_SKILL_ROOT", skillRoot);
        var root = Path.Combine(
            Environment.GetEnvironmentVariable("READINESS_TEST_ARTIFACTS") ?? Path.Combine(repositoryRoot, "artifacts"),
            "readiness-scoped-component-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            TestFrozenSelection(skillRoot);
            var genericRoot = Path.Combine(root, "generic");
            Directory.CreateDirectory(genericRoot);
            var generic = AssessmentTests.CreateInputFixture(genericRoot);
            TestGenericIntake(generic, skillRoot);
            var fixture = CreateFixture(root, skillRoot, generic.Confirmed.Components.Single());
            TestRoundTrip(fixture);
            TestSelectionMutations(fixture);
            TestInputAndRoleMutations(fixture);
            TestContextClosure(fixture);
            TestContextFeedback(fixture);
            TestFeedbackChain(fixture);
            TestEvidence(fixture);
            TestDisclosure(fixture);
            TestPublicationRaces(fixture);
            TestPartialResult(fixture);
            TestCorrections(fixture);
            Console.WriteLine(Marker + ": exact-byte scoped component init/validate/report/reader, closure, " +
                "evidence, disclosure, correction and publication regressions passed; no product findings.");
        }
        finally
        {
            ReportCommand.BeforePublishForTests = null;
            ReaderCommand.BeforePublishForTests = null;
            Environment.SetEnvironmentVariable("READINESS_SKILL_ROOT", previous);
            Directory.Delete(root, recursive: true);
        }
    }

    private static void TestFrozenSelection(string skillRoot)
    {
        var rubric = RubricLoader.Load();
        var ordinary = RubricLoader.Select(rubric, "component", []);
        var selected = ordinary.Where(row => row.Basis is { RequiresPolicyApproval: false }).ToArray();
        Assert(selected.Select(row => row.Id).SequenceEqual(ExpectedIds), "complete ordered 51-check projection");
        Assert(ContractJson.RawDigest(JsonSerializer.SerializeToUtf8Bytes(ExpectedIds)).Value == SelectedDigest,
            "independently pinned ordered ID-array digest");
        Assert(ordinary.Where(row => row.Basis?.RequiresPolicyApproval == true).Select(row => row.Id)
            .ToHashSet(StringComparer.Ordinal).SetEquals(Excluded.Where(id => id != "TA-08")),
            "all ten removed component extensions, without silently losing another requirement");
        Assert(selected.All(row => row.Scope == "component-specific" && row.Basis is not null &&
            !string.IsNullOrWhiteSpace(row.Basis.Clause) && !string.IsNullOrWhiteSpace(row.Basis.Quotation)),
            "unchanged ownership and resolved primary mappings");
        Assert(selected.Count(row => row.Basis!.Classification == "direct obligation") == 13 &&
            selected.Count(row => row.Basis!.Classification == "decomposition evidence check") == 36 &&
            selected.Count(row => row.Basis!.Classification == "conditional obligation") == 2,
            "frozen mapping classifications");
        Assert(ContractJson.RawDigest(File.ReadAllBytes(Path.Combine(skillRoot, "references", "rubric.json"))).Value ==
            "260c646feb6b9c89ba46a15362ea0b6f5ed632891c330934915b916e7125cc0b",
            "all IDs, wording, ownership, areas, primary/additional mappings and order stay frozen");
        Assert(ContractJson.RawDigest(File.ReadAllBytes(Path.Combine(skillRoot, "references", ProfileName))).Value ==
            "b987b982163f2253a28d9d46e85073ceec8e48f4755bd45c35d96e4bc68a94ad", "frozen crosswalk bytes");
        Assert(RubricLoader.Select(rubric, "package", []).Count == 60 && ordinary.Count == 61 &&
            RubricLoader.Select(rubric, "unified", []).Count == 121, "ordinary current selection unchanged");
        var legacy = RubricLoader.Load("1.3.0");
        Assert(RubricLoader.Select(legacy, "package", []).Count == 46 &&
            RubricLoader.Select(legacy, "component", []).Count == 64 &&
            RubricLoader.Select(legacy, "unified", []).Count == 110, "legacy selection unchanged");
    }

    private static void TestGenericIntake(AssessmentTests.Fixture fixture, string skillRoot)
    {
        var input = fixture.Confirmed with { OwnerInputs = [] };
        var inputPath = Path.Combine(fixture.Root, "generic.input.json");
        File.WriteAllBytes(inputPath, InputManifestService.Serialize(input));
        foreach (var kind in new[] { "package", "unified" })
        {
            var output = Path.Combine(fixture.Root, kind + ".init.json");
            string[] component = kind == "unified" ? ["--component", "fancy-tree"] : [];
            Cli(["assessment", "init", "--kind", kind, "--root", fixture.Root, "--input", inputPath,
                "--output", output, .. component], 0);
            Assert(AssessmentService.Parse(File.ReadAllBytes(output)).Rows.Count == (kind == "package" ? 60 : 121),
                "no profile preserves ordinary CLI selection");
        }

        File.Copy(Path.Combine(skillRoot, "references", ProfileName), Path.Combine(fixture.Root, ProfileName));
        File.WriteAllText(Path.Combine(fixture.Root, ContextName), "{}");
        var profile = Artifact(fixture.Root, ProfileName, ProfileKind);
        var context = Artifact(fixture.Root, ContextName, ContextKind);
        var malformed = new Dictionary<string, InputEvidenceArtifact[]>
        {
            ["orphan profile"] = [profile],
            ["orphan context"] = [context],
            ["unsupported profile"] = [profile with { Kind = ProfileKind + "-unknown" }, context],
            ["unsupported context"] = [profile, context with { Kind = ContextKind + "-unknown" }],
            ["duplicate profile"] = [profile, profile, context],
            ["duplicate context"] = [profile, context, context],
            ["profile stripped by kind"] = [profile with { Kind = "untyped" }, context]
        };
        foreach (var (name, descriptors) in malformed)
        {
            var altered = input with { EvidenceInputs = descriptors.OrderBy(item => item.Basename, StringComparer.Ordinal).ToArray() };
            File.WriteAllBytes(inputPath, InputManifestService.Serialize(altered));
            var output = Path.Combine(fixture.Root, "invalid-" + Guid.NewGuid().ToString("N") + ".json");
            Cli(["assessment", "init", "--kind", "unified", "--component", "fancy-tree",
                "--root", fixture.Root, "--input", inputPath, "--output", output], 1, name);
            Assert(!File.Exists(output), name + " cannot produce an ordinary fallback");
        }
    }

    private static Fixture CreateFixture(
        string root, string skillRoot, InputComponent component)
    {
        var packageFixture = ScopedTestInputs.SelectScope(ScopedTestInputs.Create(root));
        Assert(ContractJson.RawDigest(File.ReadAllBytes(Path.Combine(root, "package.nupkg"))) ==
            packageFixture.Input.Package.NupkgDigest, "integration uses actual synthetic package bytes, not forged hashes");
        File.WriteAllText(Path.Combine(root, "context-observation.txt"), Marker + ": context dependency only, no product investigation.");
        var packageInput = InputManifestService.Confirm(packageFixture.Input with
        {
            State = "draft",
            EvidenceInputs = Sort(
                Artifact(root, AuthorizedPackageScope.Filename, AuthorizedPackageScope.Kind),
                Artifact(root, "context-observation.txt", "synthetic-analysis"))
        }, root);
        var packageInputPath = Path.Combine(root, "context.input.json");
        var packageAssessmentPath = Path.Combine(root, "context.assessment.json");
        var packageEvidencePath = Path.Combine(root, "context.evidence.json");
        File.WriteAllBytes(packageInputPath, InputManifestService.Serialize(packageInput));
        Cli(["assessment", "init", "--kind", "package", "--root", root, "--input", packageInputPath,
            "--output", packageAssessmentPath], 0);
        var packageAssessment = AssessmentService.Parse(File.ReadAllBytes(packageAssessmentPath));
        Assert(packageAssessment.Rows.Count == 48, "approved 48-row recovery remains intact");
        var draft = Draft(packageInput, "context-observation.txt", "context dependency", repositoryWide: true);
        var ledger = EvidenceLedgerBuilder.BuildRepositoryLedger(
            new("package", packageAssessment.Identity.Package, packageAssessment.Identity.InputManifestDigest, null), [draft]);
        var evidenceId = ledger.Records.Single().StableId;
        var packageEvidence = EvidenceLedgerBuilder.BuildBundle(packageAssessment.Identity, [ledger], [evidenceId]);
        packageAssessment = packageAssessment with
        {
            Rows = packageAssessment.Rows.Select((row, index) => row with
            {
                Status = "not tested", Observation = Marker + ": no package finding.",
                AssessmentFollowUp = Marker + ": product investigation is outside this fixture.",
                EvidenceIds = index == 0 ? [evidenceId] : []
            }).ToArray(),
            SummaryGroups = [new("not tested", Marker + ": all package rows are untested.",
                packageAssessment.SelectedIds, [evidenceId])],
            CompletionState = "complete"
        };
        File.WriteAllBytes(packageAssessmentPath, AssessmentService.Serialize(packageAssessment));
        File.WriteAllBytes(packageEvidencePath, CanonicalEvidenceJson.SerializeBundle(packageEvidence));
        var contextRevisions = Path.Combine(root, "context-revisions");
        Cli(["report", "render", "--root", root, "--input", packageInputPath, "--assessment", packageAssessmentPath,
            "--evidence", packageEvidencePath, "--output", contextRevisions], 0);
        var contextRevision = Path.Combine(contextRevisions, "0001");
        Cli(["report", "verify", "--root", root, "--revision", contextRevision], 0);
        Reject(() => RevisionService.LoadPackageBinding(root, contextRevision, null),
            "48-row scoped context remains ineligible for ordinary package binding");
        File.Copy(Path.Combine(contextRevision, "package.validation.json"), Path.Combine(root, ContextName));
        File.Copy(Path.Combine(skillRoot, "references", ProfileName), Path.Combine(root, ProfileName));
        File.WriteAllText(Path.Combine(root, "synthetic-observation.txt"),
            Marker + ": raw internal-only disclosure sentinel. BEQ-05 and TA-08 must never be exported.");
        var input = InputManifestService.Confirm(packageInput with
        {
            State = "draft",
            Components = [component with { DisplayName = Marker + " Fancy Tree" }],
            EvidenceInputs = Sort(
                Artifact(root, ProfileName, ProfileKind),
                Artifact(root, ContextName, ContextKind),
                Artifact(root, "synthetic-observation.txt", "synthetic-analysis"))
        }, root);
        var inputPath = Path.Combine(root, "component.input.json");
        var assessmentPath = Path.Combine(root, "component.assessment.json");
        var evidencePath = Path.Combine(root, "component.evidence.json");
        File.WriteAllBytes(inputPath, InputManifestService.Serialize(input));
        Cli(["assessment", "init", "--kind", "component", "--component", "fancy-tree", "--root", root,
            "--input", inputPath, "--output", assessmentPath, "--package-context-revision", contextRevision], 0);
        var initialized = AssessmentService.Parse(File.ReadAllBytes(assessmentPath));
        Assert(initialized.SchemaVersion == 2 && initialized.PackageReference is null &&
            initialized.CompletionState == "incomplete" && initialized.Rows.All(row => row.Status is null),
            "V1 keeps schema 2, null ordinary binding and fresh incomplete rows");
        var identityPath = Path.Combine(root, "synthetic.identity.json");
        Cli(["assessment", "export-identity", "--assessment", assessmentPath, "--output", identityPath], 0);
        Assert(File.ReadAllBytes(identityPath).SequenceEqual(CanonicalEvidenceJson.SerializeAssessment(initialized.Identity)),
            "identity export remains the exact existing identity format");
        var (assessment, evidence) = Complete(input, initialized);
        File.WriteAllBytes(assessmentPath, AssessmentService.Serialize(assessment));
        File.WriteAllBytes(evidencePath, CanonicalEvidenceJson.SerializeBundle(evidence));
        return new(root, input, inputPath, assessment, assessmentPath, evidence, evidencePath, contextRevision,
            Path.Combine(root, "component-revisions"), Path.Combine(root, "reader"));
    }

    private static (ReadinessAssessment Assessment, EvidenceBundle Evidence) Complete(InputManifest input, ReadinessAssessment initialized)
    {
        var ledger = EvidenceLedgerBuilder.BuildComponentLedger(initialized.Identity,
            [Draft(input, "synthetic-observation.txt", "first selected observation"),
             Draft(input, "synthetic-observation.txt", "second selected observation")]);
        var ids = ledger.Records.Select(record => record.StableId).Order(StringComparer.Ordinal).ToArray();
        var evidence = EvidenceLedgerBuilder.BuildBundle(initialized.Identity, [ledger], ids);
        var assessment = initialized with
        {
            Rows = initialized.Rows.Select((row, index) => row with
            {
                Status = index == 0 ? "verified" : index is 2 or 5 ? "owner evidence required" : "not tested",
                Observation = index == 0 ? Marker + ": synthetic verified field, not a product finding; runtime remains untested."
                    : Marker + ": no real product conclusion.",
                EvidenceIds = index == 0 ? ids : index == 1 ? [ids[0]] : [],
                OwnerAction = index is 2 or 5 ? Marker + ": synthetic owner review not supplied." : null,
                AssessmentFollowUp = index is 0 or 2 or 5 ? null : Marker + ": unrun check; evidence is explicitly missing."
            }).ToArray(),
            Findings = [new(Marker + ": qualified synthetic finding",
                Marker + ": preserve attribution and missing runtime qualification.", [ExpectedIds[0]], ids)],
            CompletionState = "complete"
        };
        return (assessment, evidence);
    }

    private static EvidenceRecordDraft Draft(InputManifest input, string basename, string claim, bool repositoryWide = false) =>
        new(Marker + ": " + claim,
            new(repositoryWide ? "repository-wide" : "component-specific", repositoryWide ? null : "fancy-tree"),
            new(EvidenceIdentity.ReviewerGeneratedAnalysis, basename, Marker + ": fixture construction, not a product probe.",
                "2026-09-09T03:38:08Z", input.EvidenceInputs.Single(item => item.Basename == basename).ContentDigest, "commitment-only"), []);

    private static void TestRoundTrip(Fixture fixture)
    {
        Assert(fixture.Assessment.SelectedIds.SequenceEqual(ExpectedIds), "CLI selects the exact 51 IDs");
        var expected = RubricLoader.Select(RubricLoader.Load(), "component", [])
            .Where(row => row.Basis is { RequiresPolicyApproval: false }).ToArray();
        Assert(fixture.Assessment.Rows.Zip(expected).All(pair => pair.First.Id == pair.Second.Id &&
            pair.First.Requirement == pair.Second.Requirement && pair.First.Area == pair.Second.Area &&
            pair.First.Scope == pair.Second.Scope), "CLI preserves canonical row fields");
        Cli(Validate(fixture), 0);
        Cli(Render(fixture), 0);
        Cli(Verify(fixture), 0);
        Cli(Reader(fixture, "render"), 0);
        Cli(Reader(fixture, "verify"), 0);
        var manifest = JsonNode.Parse(File.ReadAllBytes(Path.Combine(fixture.Revision, "component.validation.json")))!;
        Assert(manifest.AsObject().ContainsKey("package_reference") && manifest["package_reference"] is null,
            "validation receipt keeps the ordinary-reference field explicitly null");
        var readerReceipt = JsonNode.Parse(File.ReadAllBytes(Path.Combine(fixture.Reader, "reader.validation.json")))!;
        Assert(readerReceipt.AsObject().ContainsKey("package_validation_sha256") && readerReceipt["package_validation_sha256"] is null,
            "reader ordinary package binding remains explicitly null");
        var canonical = File.ReadAllText(Path.Combine(fixture.Revision, "component.report.md"));
        var table = canonical.Split('\n').Where(line => line.StartsWith("| `", StringComparison.Ordinal))
            .Select(line => line.Split('`')[1]).ToArray();
        Assert(table.SequenceEqual(ExpectedIds), "canonical report has exactly 51 rows, no context rows or placeholders");
        Assert(canonical.Contains(SelectedDigest, StringComparison.Ordinal), "canonical selected-set digest disclosed");
        Assert(canonical.Contains(ContractJson.RawDigest(File.ReadAllBytes(Path.Combine(fixture.Root, ContextName))).Value,
            StringComparison.Ordinal), "separate exact context receipt digest disclosed");
        CheckReader(fixture);
        CheckCompanionNotice(fixture.Reader, "included", equal: true);
        Assert(File.ReadAllBytes(Path.Combine(fixture.Reader, "technical", "selected.evidence.json"))
            .SequenceEqual(CanonicalEvidenceJson.SerializeBundle(fixture.Evidence)),
            "all-selected reconstruction preserves exact bundle bytes and therefore equal SHA-256 values");
        TestLegacyReaders(fixture);
    }

    private static void TestLegacyReaders(Fixture fixture)
    {
        var context = RevisionService.LoadScopedPackageContextBinding(fixture.Root, fixture.ContextRevision, null);
        var component = RevisionService.VerifyRevision(fixture.Root, fixture.Revision, null, null,
            validateChain: true, scopedPackageContext: context);
        var package = RevisionService.VerifyRevision(fixture.Root, fixture.ContextRevision, null, null, validateChain: true);
        // Pin every legacy reader file and its inventory for the synthetic subjects.
        var goldenMismatches = new List<string>();
        foreach (var (source, binding, golden) in new[]
        {
            (component, (ScopedPackageContextBinding?)context, "453ed511d9005141dabd29351bdc09caafb864702719d305b4adfda46e5b565e"),
            (package, (ScopedPackageContextBinding?)null, "957c2511c0b96bb30b77aa60ccf144f43e02c48a0665cd6fb78ba356f5de62e5")
        })
        {
            var reader = NewPath(fixture, "reader-v1-" + source.Kind);
            var files = ReaderService.Build(fixture.Root, source, null, null,
                scopedPackageContext: binding, readerVersion: "1.0.0");
            foreach (var (name, bytes) in files)
            {
                var path = Path.Combine(reader, name);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllBytes(path, bytes);
            }
            var beforeVerification = ReaderTreeDigest(reader);
            if (beforeVerification != golden)
                goldenMismatches.Add($"{source.Kind}: expected {golden}, actual {beforeVerification}");
            string[] args = ["reader", "verify", "--root", fixture.Root, "--revision", source.Directory, "--output", reader,
                .. (binding is null ? Array.Empty<string>() : ContextOptions(fixture))];
            Cli(args, 0, "legacy reader uses the same strict current canonical source verification");
            Assert(ReaderTreeDigest(reader) == beforeVerification, "verification does not rewrite legacy readers");
            var manifestPath = Path.Combine(reader, "reader.validation.json");
            var changedVersion = Encoding.UTF8.GetBytes(File.ReadAllText(manifestPath)
                .Replace("\"reader_version\":\"1.0.0\"", "\"reader_version\":\"1.0.1\"", StringComparison.Ordinal));
            MutateFile(manifestPath, changedVersion, () => Cli(args, 1, "changing a supported version cannot relabel legacy bytes"));
            var reportPath = Path.Combine(reader, "report.md");
            MutateFile(reportPath, [.. File.ReadAllBytes(reportPath), (byte)'!'],
                () => Cli(args, 1, "legacy reports remain byte-verified"));
            if (source.Kind == "component")
            {
                foreach (var name in new[] { "component.assessment.json", "component.report.md", "component.validation.json", "selected.evidence.json" })
                    Assert(files["technical/" + name].SequenceEqual(File.ReadAllBytes(Path.Combine(fixture.Reader, "technical", name))),
                        "corrected presentation preserves canonical technical and selected evidence bytes: " + name);
            }
        }
        Assert(goldenMismatches.Count == 0, "frozen 1.0.0 synthetic reader bytes: " + string.Join("; ", goldenMismatches));
    }

    private static void CheckCompanionNotice(string reader, string disposition, bool? equal = null)
    {
        foreach (var name in new[] { "report.md", "evidence.md" })
        {
            var text = File.ReadAllText(Path.Combine(reader, name));
            Assert(text.Contains("**Structured selected-only evidence companion: " + disposition + ".**", StringComparison.Ordinal),
                "explicit companion disposition in " + name);
            Assert(!text.Contains("not a relabeled historical ledger or an exact copy", StringComparison.Ordinal) &&
                !text.Contains("The original full ledgers remain internal", StringComparison.Ordinal),
                "included selected payloads are not mislabeled as absent or necessarily different");
            if (equal is not null)
                Assert(text.Contains(equal.Value
                        ? "For this revision, its bytes are identical to the current internal evidence bundle."
                        : "For this revision, its bytes differ from the current internal evidence bundle.", StringComparison.Ordinal) &&
                    text.Contains("preserves selected record identities and provenance", StringComparison.Ordinal) &&
                    text.Contains("Raw inputs and omitted historical dependencies are not delivered", StringComparison.Ordinal),
                    "accurate selected-payload and raw-dependency boundaries in " + name);
            else
                Assert(!text.Contains("For this revision, its bytes are identical", StringComparison.Ordinal) &&
                    !text.Contains("For this revision, its bytes differ", StringComparison.Ordinal),
                    "omission does not compare a nonexistent companion in " + name);
        }
    }

    private static string ReaderTreeDigest(string directory) =>
        ContractJson.RawDigest(StrictJson.SerializeCanonical(writer =>
        {
            writer.WriteStartObject();
            foreach (var path in Directory.GetFiles(directory, "*", SearchOption.AllDirectories)
                .OrderBy(path => Path.GetRelativePath(directory, path).Replace('\\', '/'), StringComparer.Ordinal))
                writer.WriteString(Path.GetRelativePath(directory, path).Replace('\\', '/'),
                    ContractJson.RawDigest(File.ReadAllBytes(path)).Value);
            writer.WriteEndObject();
        })).Value;

    private static void CheckReader(Fixture fixture)
    {
        using var mapping = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(fixture.Reader, "mapping.json")));
        using var receipt = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(fixture.Reader, "reader.validation.json")));
        Assert(mapping.RootElement.GetProperty("reader_version").GetString() == "1.0.1" &&
            receipt.RootElement.GetProperty("reader_version").GetString() == "1.0.1",
            "scoped mapping and receipt agree on corrected reader version");
        using var canonical = JsonDocument.Parse(AssessmentService.Serialize(fixture.Assessment));
        var originals = canonical.RootElement.GetProperty("rows").EnumerateArray()
            .ToDictionary(row => row.GetProperty("id").GetString()!);
        var groups = mapping.RootElement.GetProperty("groups").EnumerateArray().ToArray();
        var mapped = groups.SelectMany(group => group.GetProperty("checks").EnumerateArray()).ToArray();
        Assert(mapped.Length == 51 && mapped.Select(row => row.GetProperty("id").GetString()).Distinct().Count() == 51,
            "reader preserves every check exactly once, not 25 clause-level rows");
        foreach (var row in mapped)
        {
            var id = row.GetProperty("id").GetString()!;
            Assert(row.GetRawText() == originals[id].GetRawText(), "mapping preserves every per-check field: " + id);
        }
        Assert(groups.Any(group => group.GetProperty("checks").EnumerateArray()
            .Select(row => row.GetProperty("status").GetString()).Distinct().Count() > 1), "mixed grouped statuses retained");
        var report = File.ReadAllText(Path.Combine(fixture.Reader, "report.md"));
        Assert(report.Contains("**Check accounting:** complete.", StringComparison.Ordinal) &&
            report.Contains("It does not establish completed testing, complete evidence coverage, readiness or approval.", StringComparison.Ordinal) &&
            !report.Contains("**Completion:**", StringComparison.Ordinal),
            "completion header describes 51-check accounting, not behavior, evidence completeness or readiness");
        Assert(File.ReadAllText(Path.Combine(fixture.Reader, "evidence.md"))
            .Contains("complete current ledger payloads when all records are selected", StringComparison.Ordinal),
            "selected payloads and internally retained ledger artifacts remain distinct");
        Assert(report.Contains("Mixed results.", StringComparison.Ordinal) &&
            report.Contains("runtime remains untested", StringComparison.Ordinal) &&
            report.Contains("evidence is explicitly missing", StringComparison.Ordinal), "mixed results and qualifications visible");
        var text = string.Join("\n", Directory.GetFiles(fixture.Reader, "*", SearchOption.AllDirectories).Select(File.ReadAllText));
        Assert(text.Contains("not self-contained", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("not a self-contained", StringComparison.OrdinalIgnoreCase),
            "reader explicitly says it is not self-contained");
        Assert(text.Contains("omit", StringComparison.OrdinalIgnoreCase) &&
            text.Contains("internal", StringComparison.OrdinalIgnoreCase), "omissions and internal verification dependency disclosed");
        var files = Directory.GetFiles(fixture.Reader, "*", SearchOption.AllDirectories);
        Assert(files.All(path => !path.EndsWith(".bin", StringComparison.OrdinalIgnoreCase) &&
            !fixture.Input.EvidenceInputs.Any(input => Path.GetFileName(path) == input.Basename) &&
            !path.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase) &&
            Path.GetFileName(path) != ProfileName && Path.GetFileName(path) != ContextName &&
            Path.GetFileName(path) != "input-manifest.json" && Path.GetFileName(path) != "component.evidence.json" &&
            Path.GetFileName(path) != "synthetic-observation.txt" &&
            Path.GetFileName(path) != "context-observation.txt"), "raw inputs, full crosswalk and attachments omitted");
        foreach (var path in files)
        {
            AssertNoExcluded(File.ReadAllText(path), "reader surface " + Path.GetRelativePath(fixture.Reader, path));
        }
        AssertNoExcluded(File.ReadAllText(Path.Combine(fixture.Revision, "component.report.md")), "canonical report");
        Assert(!text.Contains("raw internal-only disclosure sentinel", StringComparison.Ordinal),
            "registered raw historical material stays internal");
    }

    private static void TestSelectionMutations(Fixture fixture)
    {
        void Invalid(string name, ReadinessAssessment changed) => MutateAssessment(fixture, changed, () => Cli(Validate(fixture), 1, name));
        var rubric = RubricLoader.Load();
        var definitions = rubric.CoreRequirements.Concat(rubric.Extensions ?? [])
            .GroupBy(row => row.Id).ToDictionary(group => group.Key, group => group.First());
        Invalid("50 rows", fixture.Assessment with
        {
            Rows = fixture.Assessment.Rows.Skip(1).ToArray(), SelectedIds = fixture.Assessment.SelectedIds.Skip(1).ToArray()
        });
        Invalid("duplicate", fixture.Assessment with { Rows = [.. fixture.Assessment.Rows, fixture.Assessment.Rows[0]] });
        Invalid("reordered", fixture.Assessment with
        {
            Rows = fixture.Assessment.Rows.Reverse().ToArray(), SelectedIds = fixture.Assessment.SelectedIds.Reverse().ToArray()
        });
        Invalid("arbitrary same-sized substitution", fixture.Assessment with
        {
            SelectedIds = ["LP-01", .. fixture.Assessment.SelectedIds.Skip(1)]
        });
        foreach (var id in Excluded)
        {
            var definition = definitions[id];
            var excludedRow = new AssessmentRow(definition.Id, definition.Requirement, definition.Scope, definition.Area,
                "not tested", Marker + ": forbidden selection test.", [], null, Marker + ": no product probe.", null);
            Invalid("excluded extra " + id, fixture.Assessment with
            {
                SelectedIds = [.. fixture.Assessment.SelectedIds, id],
                Rows = [.. fixture.Assessment.Rows, excludedRow]
            });
            Invalid("excluded replacement " + id, fixture.Assessment with
            {
                SelectedIds = [id, .. fixture.Assessment.SelectedIds.Skip(1)],
                Rows = [excludedRow, .. fixture.Assessment.Rows.Skip(1)]
            });
        }
        foreach (var row in new[]
        {
            fixture.Assessment.Rows[0] with { Requirement = Marker + ": changed wording" },
            fixture.Assessment.Rows[0] with { Scope = "repository-wide" },
            fixture.Assessment.Rows[0] with { Area = Marker + ": changed area" }
        })
        {
            Invalid("canonical definition drift", fixture.Assessment with { Rows = [row, .. fixture.Assessment.Rows.Skip(1)] });
        }
        Invalid("rubric identity", fixture.Assessment with { RubricDigest = ZeroDigest });
        Invalid("scope identity", fixture.Assessment with { ScopeMapDigest = ZeroDigest });
        Invalid("legacy schema escape", fixture.Assessment with { SchemaVersion = 1 });
        Invalid("legacy rubric escape", fixture.Assessment with { RubricVersion = "1.3.0" });
    }

    private static void TestInputAndRoleMutations(Fixture fixture)
    {
        foreach (var changed in new[]
        {
            fixture.Input with { Package = fixture.Input.Package with { PackageId = "other.package" } },
            fixture.Input with { Package = fixture.Input.Package with { Version = "9.9.9" } },
            fixture.Input with { Package = fixture.Input.Package with { NupkgDigest = ZeroDigest } },
            fixture.Input with { Source = fixture.Input.Source with { Commit = new string('a', 40) } },
            fixture.Input with { Source = fixture.Input.Source with { RepositoryUri = "https://github.com/example/other" } },
            fixture.Input with { Source = fixture.Input.Source with { Mapping = Marker + ": different source mapping." } },
            fixture.Input with { Source = fixture.Input.Source with { Confidence = "low" } },
            fixture.Input with { Source = new("unresolved", null, null, null, null) }
        })
        {
            MutateFile(fixture.InputPath, InputManifestService.Serialize(changed), () =>
                Cli(Init(fixture, NewPath(fixture, "bad-identity")), 1, "wrong exact input identity"));
        }
        var descriptors = fixture.Input.EvidenceInputs;
        var profile = descriptors.Single(item => item.Kind == ProfileKind);
        var context = descriptors.Single(item => item.Kind == ContextKind);
        foreach (var changed in new[]
        {
            descriptors.Where(item => item.Kind != ProfileKind).ToArray(),
            descriptors.Where(item => item.Kind != ContextKind).ToArray(),
            Sort([.. descriptors, profile]),
            Sort([.. descriptors, context]),
            descriptors.Select(item => item == profile ? item with { Kind = ProfileKind + "-unknown" } : item).ToArray(),
            descriptors.Select(item => item == context ? item with { Kind = ContextKind + "-unknown" } : item).ToArray(),
            descriptors.Select(item => item == profile ? item with { ContentDigest = ZeroDigest } : item).ToArray(),
            descriptors.Select(item => item == context ? item with { ContentDigest = ZeroDigest } : item).ToArray(),
            descriptors.Select(item => item == profile ? item with { Size = item.Size + 1 } : item).ToArray(),
            descriptors.Select(item => item == context ? item with { Size = item.Size + 1 } : item).ToArray(),
            descriptors.Select(item => item == profile ? item with { Kind = "untyped" } : item).ToArray(),
            Sort([.. descriptors, Artifact(fixture.Root, AuthorizedPackageScope.Filename, AuthorizedPackageScope.Kind)])
        })
        {
            MutateFile(fixture.InputPath, InputManifestService.Serialize(fixture.Input with { EvidenceInputs = changed }), () =>
            {
                Cli(Init(fixture, NewPath(fixture, "bad-descriptor")), 1, "descriptor cannot broaden scope");
                Cli(Validate(fixture), 1, "persisted descriptor identity mismatch");
            });
        }
        foreach (var item in new[] { profile, context })
        {
            var alias = "renamed-" + item.Basename;
            File.Copy(Path.Combine(fixture.Root, item.Basename), Path.Combine(fixture.Root, alias));
            var renamed = fixture.Input with
            {
                EvidenceInputs = Sort(descriptors.Select(original => original == item
                    ? item with { Basename = alias } : original).ToArray())
            };
            MutateFile(fixture.InputPath, InputManifestService.Serialize(renamed), () =>
                Cli(Init(fixture, NewPath(fixture, "renamed-descriptor")), 1, "descriptor basename is immutable"));
            var conflicting = fixture.Input with
            {
                EvidenceInputs = Sort([.. descriptors, item with { Basename = alias }])
            };
            MutateFile(fixture.InputPath, InputManifestService.Serialize(conflicting), () =>
                Cli(Init(fixture, NewPath(fixture, "conflicting-descriptor")), 1, "same-kind conflicting descriptor"));
        }
        foreach (var name in new[] { ProfileName, ContextName })
        {
            var bytes = File.ReadAllBytes(Path.Combine(fixture.Root, name));
            WithChangedArtifact(fixture, name, [.. bytes, (byte)'\n'], () =>
                Cli(Init(fixture, NewPath(fixture, "rehashed-policy")), 1,
                    "recomputed input descriptor cannot authorize changed frozen bytes: " + name));
        }
        Cli(RemoveOption(Init(fixture, NewPath(fixture, "missing-context")), "--package-context-revision"), 1);
        Cli([.. Init(fixture, NewPath(fixture, "both-roles")), "--package-revision", fixture.ContextRevision], 1);
        Cli([.. RemoveOption(Init(fixture, NewPath(fixture, "ordinary-role")), "--package-context-revision"),
            "--package-revision", fixture.ContextRevision], 1);
        foreach (var kind in new[] { "package", "unified" })
        {
            var args = Init(fixture, NewPath(fixture, "wrong-kind"));
            args[Array.IndexOf(args, "--kind") + 1] = kind;
            if (kind == "package") args = RemoveOption(args, "--component");
            Cli(args, 1, "profile cannot apply to " + kind);
        }
        Cli([.. Init(fixture, NewPath(fixture, "wrong-version")), "--rubric-version", "1.3.0"], 1);
        Cli([.. Init(fixture, NewPath(fixture, "overlays")), "--overlays", "scaffolder"], 1);
        var ordinaryInput = fixture.Input with
        {
            EvidenceInputs = descriptors.Where(item => item.Kind is not (ProfileKind or ContextKind)).ToArray()
        };
        MutateFile(fixture.InputPath, InputManifestService.Serialize(ordinaryInput), () =>
            Cli(Init(fixture, NewPath(fixture, "ordinary-context")), 1, "context option is not an ordinary prerequisite"));
        foreach (var args in new[] { Validate(fixture), Verify(fixture), Reader(fixture, "verify") })
        {
            Cli(RemoveOption(args, "--package-context-revision"), 1, "every consumer requires explicit scoped locator");
            Cli([.. args, "--package-revision", fixture.ContextRevision], 1, "every consumer rejects both binding roles");
        }
        var ordinaryRevision = CreateOrdinaryRevision(fixture, ordinaryInput);
        var wrongContext = Init(fixture, NewPath(fixture, "full-package-as-context"));
        wrongContext[^1] = ordinaryRevision;
        Cli(wrongContext, 1, "ordinary full-package receipt cannot masquerade as scoped context");
        MutateFile(fixture.InputPath, InputManifestService.Serialize(ordinaryInput), () =>
        {
            var output = NewPath(fixture, "ordinary-component");
            Cli([.. RemoveOption(Init(fixture, output), "--package-context-revision"),
                "--package-revision", ordinaryRevision], 0);
            var assessment = AssessmentService.Parse(File.ReadAllBytes(output));
            Assert(assessment.Rows.Count == 61 && assessment.PackageReference is not null,
                "ordinary component still requires full package and selects 61 rows");
        });
        var ordinaryBinding = RevisionService.LoadPackageBinding(fixture.Root, ordinaryRevision, null);
        MutateAssessment(fixture, fixture.Assessment with { PackageReference = ordinaryBinding.Reference },
            () => Cli(Validate(fixture), 1, "profile cannot populate the ordinary-reference field"));
    }

    private static string CreateOrdinaryRevision(Fixture fixture, InputManifest input)
    {
        var inputPath = NewPath(fixture, "ordinary.input.json");
        var assessmentPath = NewPath(fixture, "ordinary.assessment.json");
        var evidencePath = NewPath(fixture, "ordinary.evidence.json");
        File.WriteAllBytes(inputPath, InputManifestService.Serialize(input));
        Cli(["assessment", "init", "--kind", "package", "--root", fixture.Root, "--input", inputPath,
            "--output", assessmentPath], 0);
        var initialized = AssessmentService.Parse(File.ReadAllBytes(assessmentPath));
        var ledger = EvidenceLedgerBuilder.BuildRepositoryLedger(
            new("package", initialized.Identity.Package, initialized.Identity.InputManifestDigest, null),
            [Draft(input, "synthetic-observation.txt", "ordinary package fixture", repositoryWide: true)]);
        var id = ledger.Records.Single().StableId;
        var evidence = EvidenceLedgerBuilder.BuildBundle(initialized.Identity, [ledger], [id]);
        var assessment = initialized with
        {
            Rows = initialized.Rows.Select((row, index) => row with
            {
                Status = "not tested", Observation = Marker + ": ordinary compatibility fixture.",
                AssessmentFollowUp = Marker + ": product investigation not performed.", EvidenceIds = index == 0 ? [id] : []
            }).ToArray(),
            SummaryGroups = [new("not tested", Marker + ": no ordinary package findings.", initialized.SelectedIds, [id])],
            CompletionState = "complete"
        };
        File.WriteAllBytes(assessmentPath, AssessmentService.Serialize(assessment));
        File.WriteAllBytes(evidencePath, CanonicalEvidenceJson.SerializeBundle(evidence));
        var output = NewPath(fixture, "ordinary-revisions");
        Cli(["report", "render", "--root", fixture.Root, "--input", inputPath, "--assessment", assessmentPath,
            "--evidence", evidencePath, "--output", output], 0);
        return Path.Combine(output, "0001");
    }

    private static void TestContextClosure(Fixture fixture)
    {
        var dependencies = new[]
        {
            Path.Combine(fixture.Root, ProfileName),
            Path.Combine(fixture.Root, ContextName), Path.Combine(fixture.Root, "package.nupkg"),
            Path.Combine(fixture.Root, AuthorizedPackageScope.Filename),
            Path.Combine(fixture.Root, "context-observation.txt"), Path.Combine(fixture.Root, "synthetic-observation.txt"),
            Path.Combine(fixture.ContextRevision, "input-manifest.json"),
            Path.Combine(fixture.ContextRevision, "package.assessment.json"),
            Path.Combine(fixture.ContextRevision, "package.evidence.json"),
            Path.Combine(fixture.ContextRevision, "package.report.md"),
            Path.Combine(fixture.ContextRevision, "package.validation.json")
        };
        foreach (var dependency in dependencies)
        {
            MutateFile(dependency, Encoding.UTF8.GetBytes("{}"), () =>
            {
                Cli(Verify(fixture), 1, "altered internal dependency: " + Path.GetFileName(dependency));
                Cli(Reader(fixture, "verify"), 1, "reader must recheck internal dependency: " + Path.GetFileName(dependency));
            });
            MissingFile(dependency, () => CliMissing(Verify(fixture), "omitted export is still required internally"));
        }
        var path = Path.Combine(fixture.ContextRevision, "package.validation.json");
        foreach (var field in new[] { "input_manifest_sha256", "assessment_sha256", "evidence_sha256", "report_sha256", "rubric_sha256", "scope_map_sha256" })
        {
            var receipt = JsonNode.Parse(File.ReadAllBytes(path))!.AsObject();
            Assert(receipt[field] is JsonObject, "context receipt fixture field exists: " + field);
            receipt[field]!["value"] = ZeroDigest.Value;
            WithChangedContextReceipt(fixture, receipt, () =>
                Cli(Init(fixture, NewPath(fixture, "invalid-context-receipt")), 1,
                    "matching descriptor cannot bless stale context " + field));
        }
        var missingFeedback = JsonNode.Parse(File.ReadAllBytes(path))!.AsObject();
        missingFeedback["feedback_sha256"] = new JsonObject { ["algorithm"] = "sha256", ["value"] = ZeroDigest.Value };
        WithChangedContextReceipt(fixture, missingFeedback, () =>
        {
            Cli(Init(fixture, NewPath(fixture, "missing-context-feedback")), 1, "context cannot skip missing bound feedback");
        });
        Cli(Verify(fixture), 0);
        Cli(Reader(fixture, "verify"), 0);
    }

    private static void WithChangedContextReceipt(Fixture fixture, JsonObject receipt, Action action)
    {
        var bytes = StrictJson.SerializeCanonical(writer => receipt.WriteTo(writer));
        MutateFile(Path.Combine(fixture.ContextRevision, "package.validation.json"), bytes, () =>
            MutateFile(Path.Combine(fixture.Root, ContextName), bytes, () =>
            {
                var input = fixture.Input with
                {
                    EvidenceInputs = fixture.Input.EvidenceInputs.Select(item => item.Kind == ContextKind
                        ? Artifact(fixture.Root, ContextName, ContextKind) : item).ToArray()
                };
                MutateFile(fixture.InputPath, InputManifestService.Serialize(input), action);
            }));
    }

    private static void WithChangedArtifact(Fixture fixture, string basename, byte[] replacement, Action action)
    {
        MutateFile(Path.Combine(fixture.Root, basename), replacement, () =>
        {
            var input = fixture.Input with
            {
                EvidenceInputs = fixture.Input.EvidenceInputs.Select(item => item.Basename == basename
                    ? Artifact(fixture.Root, basename, item.Kind) : item).ToArray()
            };
            MutateFile(fixture.InputPath, InputManifestService.Serialize(input), action);
        });
    }

    private static void TestContextFeedback(Fixture fixture)
    {
        var feedbackPath = Path.Combine(fixture.Root, "context-feedback.md");
        var feedbackBytes = Encoding.UTF8.GetBytes("# Assessment feedback\n\n" +
            "| Requirement IDs | Feedback |\n|---|---|\n| `LP-01` | TOOLING TESTS / synthetic commentary, no status change. |\n");
        File.WriteAllBytes(feedbackPath, feedbackBytes);
        var predecessor = ContractJson.RawDigest(File.ReadAllBytes(Path.Combine(fixture.ContextRevision, "package.validation.json"))).Value;
        Cli(["report", "render", "--root", fixture.Root, "--input", Path.Combine(fixture.Root, "context.input.json"),
            "--assessment", Path.Combine(fixture.Root, "context.assessment.json"),
            "--evidence", Path.Combine(fixture.Root, "context.evidence.json"),
            "--output", Path.GetDirectoryName(fixture.ContextRevision)!, "--predecessor", predecessor,
            "--feedback", feedbackPath], 0);
        var revision = Path.Combine(Path.GetDirectoryName(fixture.ContextRevision)!, "0002");
        var context = fixture with { ContextRevision = revision, ContextFeedback = feedbackPath };
        MutateFile(Path.Combine(fixture.Root, ContextName), File.ReadAllBytes(Path.Combine(revision, "package.validation.json")), () =>
        {
            var input = fixture.Input with
            {
                EvidenceInputs = fixture.Input.EvidenceInputs.Select(item => item.Kind == ContextKind
                    ? Artifact(fixture.Root, ContextName, ContextKind) : item).ToArray()
            };
            WithFreshInput(context, input, (assessment, evidence) =>
            {
                Cli(Validate(context), 0, "context-specific feedback verifies the exact feedback-bound context");
                Cli(RemoveOption(Init(context, NewPath(context, "missing-feedback")), "--package-context-feedback"), 1);
                Cli([.. RemoveOption(Init(context, NewPath(context, "wrong-feedback-role")), "--package-context-feedback"),
                    "--package-feedback", feedbackPath], 1, "ordinary feedback option cannot stand in for context feedback");
                MutateFile(feedbackPath, Encoding.UTF8.GetBytes("TOOLING TESTS / synthetic wrong feedback."), () =>
                    Cli(Init(context, NewPath(context, "wrong-feedback")), 1, "exact context feedback bytes are bound"));
                var output = NewPath(context, "feedback-context-revisions");
                Cli(Render(context, output), 0);
                var completed = context with
                {
                    Revisions = output, Reader = NewPath(context, "feedback-context-reader"),
                    Input = input, Assessment = assessment, Evidence = evidence
                };
                Cli(Verify(completed), 0);
                Cli(Reader(completed, "render"), 0);
                Cli(Reader(completed, "verify"), 0);
                Assert(!Directory.GetFiles(completed.Reader, "*", SearchOption.AllDirectories)
                    .Any(path => File.ReadAllBytes(path).SequenceEqual(feedbackBytes)),
                    "context commentary and package findings are not cloned into the component reader");
                MissingFile(Path.Combine(fixture.ContextRevision, "package.assessment.json"), () =>
                    CliMissing(Verify(completed), "context revision requires its complete predecessor closure"));
                var raceOutput = NewPath(context, "context-feedback-race");
                try
                {
                    ReportCommand.BeforePublishForTests = () => File.WriteAllText(feedbackPath, Marker + ": race.");
                    Cli(Render(context, raceOutput), 1, "context feedback publication recheck");
                    Assert(!Directory.Exists(Path.Combine(raceOutput, "0001")), "feedback race publishes nothing");
                }
                finally
                {
                    ReportCommand.BeforePublishForTests = null;
                    File.WriteAllBytes(feedbackPath, feedbackBytes);
                }
            });
        });
        Cli(Verify(fixture), 0, "original context relationship survives a separately bound feedback revision");
    }

    private static void TestFeedbackChain(Fixture fixture)
    {
        var output = NewPath(fixture, "feedback-chain");
        Cli(Render(fixture, output), 0);
        var first = Path.Combine(output, "0001");
        var firstReceipt = File.ReadAllBytes(Path.Combine(first, "component.validation.json"));
        var feedbackPath = NewPath(fixture, "safe-feedback.md");
        var feedbackBytes = Encoding.UTF8.GetBytes("# Assessment feedback\n\n" +
            "| Requirement IDs | Feedback |\n|---|---|\n" +
            "| `SEC-10` | TOOLING TESTS / synthetic safe commentary; no status change. |\n");
        File.WriteAllBytes(feedbackPath, feedbackBytes);
        Cli([.. Render(fixture, output), "--feedback", feedbackPath,
            "--predecessor", ContractJson.RawDigest(firstReceipt).Value], 0,
            "feedback-only correction may follow an ancestor that never bound feedback");
        var second = Path.Combine(output, "0002");
        string[] verifySecond = ["report", "verify", "--root", fixture.Root, "--revision", second,
            "--feedback", feedbackPath, .. ContextOptions(fixture)];
        Cli(verifySecond, 0, "safe synthetic feedback remains supported");
        var context = RevisionService.LoadScopedPackageContextBinding(fixture.Root, fixture.ContextRevision, null);
        Reject(() => RevisionService.VerifyRevision(fixture.Root, second, null, null,
            validateChain: true, allowMissingFeedback: true, scopedPackageContext: context),
            "V1 missing bound feedback cannot activate the legacy reconstruction bypass");
        var secondArtifacts = RevisionService.VerifyRevision(fixture.Root, second, feedbackBytes, null,
            validateChain: true, scopedPackageContext: context);
        foreach (var name in new[] { "input-manifest.json", "component.assessment.json", "component.evidence.json" })
            Assert(File.ReadAllBytes(Path.Combine(first, name)).SequenceEqual(File.ReadAllBytes(Path.Combine(second, name))),
                "feedback-only revision preserves underlying identity and findings: " + name);
        var secondDigest = ContractJson.RawDigest(secondArtifacts.ManifestBytes);
        Cli([.. Render(fixture, output), "--feedback", feedbackPath, "--predecessor", secondDigest.Value], 1,
            "publication must not skip historical feedback its predecessor loader cannot supply");
        Assert(!Directory.Exists(Path.Combine(output, "0003")), "unavailable predecessor feedback publishes no revision");

        var thirdManifest = ReportService.CreateManifest(
            secondArtifacts.Assessment, secondArtifacts.AssessmentBytes, secondArtifacts.Input, secondArtifacts.InputBytes,
            secondArtifacts.Evidence, secondArtifacts.EvidenceBytes, secondArtifacts.ReportBytes,
            predecessorManifestDigest: secondDigest, feedbackDigest: ContractJson.RawDigest(feedbackBytes),
            root: fixture.Root, scopedPackageContext: context);
        var third = Path.Combine(output, "0003");
        Directory.CreateDirectory(third);
        foreach (var name in new[] { "input-manifest.json", "component.assessment.json", "component.evidence.json", "component.report.md" })
            File.Copy(Path.Combine(second, name), Path.Combine(third, name));
        File.WriteAllBytes(Path.Combine(third, "component.validation.json"), ReportService.SerializeManifest(thirdManifest));
        string[] verifyThird = ["report", "verify", "--root", fixture.Root, "--revision", third,
            "--feedback", feedbackPath, .. ContextOptions(fixture)];
        Cli(verifyThird, 0, "exact unchanged feedback safely propagates through a V1 chain");
        var reader = NewPath(fixture, "feedback-chain-reader");
        Cli(["reader", "render", "--root", fixture.Root, "--revision", third,
            "--feedback", feedbackPath, "--output", reader, .. ContextOptions(fixture)], 0);
        Cli(["reader", "verify", "--root", fixture.Root, "--revision", third,
            "--feedback", feedbackPath, "--output", reader, .. ContextOptions(fixture)], 0);
        Cli(RemoveOption(verifyThird, "--feedback"), 1, "unavailable feedback cannot verify a V1 chain");

        var differentFeedback = Encoding.UTF8.GetBytes("# Assessment feedback\n\n" +
            "| Requirement IDs | Feedback |\n|---|---|\n" +
            "| `SEC-10` | TOOLING TESTS / synthetic different historical commentary. |\n");
        MutateFile(feedbackPath, differentFeedback, () =>
            Cli(verifyThird, 1, "different current feedback cannot stand in for exact bound bytes"));
        var differentParsed = FeedbackService.Parse(differentFeedback, secondArtifacts.Assessment);
        var differentReport = ReportService.RenderMarkdown(secondArtifacts.Assessment, secondArtifacts.Input,
            secondArtifacts.Evidence, differentParsed, fixture.Root, context);
        var differentManifest = ReportService.CreateManifest(
            secondArtifacts.Assessment, secondArtifacts.AssessmentBytes, secondArtifacts.Input, secondArtifacts.InputBytes,
            secondArtifacts.Evidence, secondArtifacts.EvidenceBytes, differentReport,
            predecessorManifestDigest: thirdManifest.PredecessorManifestDigest,
            feedbackDigest: differentParsed.Digest, root: fixture.Root, scopedPackageContext: context);
        MutateFile(feedbackPath, differentFeedback, () =>
            MutateFile(Path.Combine(third, "component.report.md"), differentReport, () =>
                MutateFile(Path.Combine(third, "component.validation.json"), ReportService.SerializeManifest(differentManifest), () =>
                    Cli(verifyThird, 1, "valid current feedback cannot conceal unavailable different ancestor feedback"))));
        var changedReport = secondArtifacts.ReportBytes.Concat(Encoding.UTF8.GetBytes("\n" + Marker + ": appended report prose.")).ToArray();
        var rehashedManifest = secondArtifacts.Manifest with { ReportDigest = ContractJson.RawDigest(changedReport) };
        MutateFile(Path.Combine(second, "component.report.md"), changedReport, () =>
            MutateFile(Path.Combine(second, "component.validation.json"), ReportService.SerializeManifest(rehashedManifest), () =>
                Reject(() => RevisionService.VerifyRevision(fixture.Root, second, feedbackBytes, null,
                    validateChain: true, allowMissingFeedback: true, scopedPackageContext: context),
                    "even exact feedback and a rehashed receipt cannot skip report reconstruction")));
        Cli(verifyThird, 0, "failed mutations preserve the valid exact-feedback chain");
        Assert(File.ReadAllBytes(Path.Combine(first, "component.validation.json")).SequenceEqual(firstReceipt),
            "feedback corrections never rewrite the unbound ancestor");
    }

    private static void TestEvidence(Fixture fixture)
    {
        var selected = fixture.Evidence.Selection.Select(item => item.EvidenceId).ToArray();
        Assert(fixture.Assessment.Rows[0].EvidenceIds.Count == 2 && fixture.Assessment.Rows[1].EvidenceIds.Contains(selected[0]),
            "many-to-many edges retained by fixture and successful round trip");
        foreach (var row in new[]
        {
            fixture.Assessment.Rows[0] with { EvidenceIds = [] },
            fixture.Assessment.Rows[1] with { AssessmentFollowUp = null },
            fixture.Assessment.Rows[2] with { OwnerAction = null },
            fixture.Assessment.Rows[0] with { EvidenceIds = ["EV1-" + new string('a', 64)] },
            fixture.Assessment.Rows[0] with { EvidenceIds = [selected[0], selected[0]] }
        })
        {
            MutateAssessment(fixture, fixture.Assessment with
            {
                Rows = fixture.Assessment.Rows.Select(original => original.Id == row.Id ? row : original).ToArray()
            }, () => Cli(Validate(fixture), 1, "current row evidence and explicit missing-evidence rules"));
        }
        var ledger = fixture.Evidence.SourceLedgers.Single().Ledger;
        var drafts = ledger.Records.Select(record => new EvidenceRecordDraft(record.Claim, record.Applicability,
            record.Provenance, record.Supersedes)).ToArray();
        var invalidProtocol = EvidenceLedgerBuilder.BuildComponentLedger(fixture.Assessment.Identity,
            [drafts[0] with { Provenance = drafts[0].Provenance with { Method = EvidenceProtocolValidator.ToolchainMethod } }, drafts[1]]);
        WithEvidence(fixture, invalidProtocol, () => Cli(Validate(fixture), 1, "structured protocol validation remains enabled"));
        foreach (var policy in new[] { ProfileName, ContextName })
        {
            var policyLedger = EvidenceLedgerBuilder.BuildComponentLedger(fixture.Assessment.Identity,
                [Draft(fixture.Input, policy, "policy metadata is not product evidence"), drafts[1]]);
            WithEvidence(fixture, policyLedger, () =>
                Cli(Validate(fixture), 1, "bound profile/context/document metadata cannot substitute for product evidence"));
        }
        var toolchainInput = fixture.Input with
        {
            EvidenceInputs = fixture.Input.EvidenceInputs.Select(item => item.Basename == "synthetic-observation.txt"
                ? item with { Kind = "toolchain-log" } : item).ToArray()
        };
        WithFreshInput(fixture, toolchainInput, (assessment, evidence) =>
        {
            Cli(Validate(fixture), 0, "synthetic toolchain input remains explicitly untested");
            foreach (var id in new[] { "TA-02", "TA-04" })
            {
                MutateAssessment(fixture, assessment with
                {
                    Rows = assessment.Rows.Select(row => row.Id == id ? row with
                    {
                        Status = "gap", EvidenceIds = evidence.Selection.Select(item => item.EvidenceId).Order(StringComparer.Ordinal).ToArray(),
                        Observation = Marker + ": unsupported synthetic gap without a named-toolchain protocol."
                    } : row).ToArray()
                }, () => Cli(Validate(fixture), 1, "current toolchain outcome protocol: " + id));
            }
        });
        var lifecycleInput = fixture.Input with
        {
            Components = [fixture.Input.Components.Single() with
            {
                DynamicChildLifecycle = new("required", ["grouped-children"], null)
            }]
        };
        WithFreshInput(fixture, lifecycleInput, (_, _) =>
            Cli(Validate(fixture), 1, "required lifecycle cannot be waived by scoped selection"));
        var historical = EvidenceLedgerBuilder.BuildComponentLedger(fixture.Assessment.Identity,
            [.. drafts, drafts[0] with { Claim = Marker + ": BEQ-05 historical unselected observation." }]);
        var expanded = EvidenceLedgerBuilder.BuildBundle(fixture.Assessment.Identity, [historical], selected);
        var expandedBytes = CanonicalEvidenceJson.SerializeBundle(expanded);
        MutateFile(fixture.EvidencePath, expandedBytes, () =>
        {
            Cli(Validate(fixture), 0, "complete canonical historical ledger remains valid internally");
            var output = NewPath(fixture, "historical-revisions");
            Cli(Render(fixture, output), 0, "canonical revision preserves the complete source ledger");
            var source = Path.Combine(output, "0001");
            var reader = NewPath(fixture, "historical-reader");
            Cli(["reader", "render", "--root", fixture.Root, "--revision", source,
                "--output", reader, "--package-context-revision", fixture.ContextRevision], 0);
            Cli(["reader", "verify", "--root", fixture.Root, "--revision", source,
                "--output", reader, "--package-context-revision", fixture.ContextRevision], 0);
            Assert(File.ReadAllBytes(Path.Combine(source, "component.evidence.json")).SequenceEqual(expandedBytes),
                "canonical historical bundle is not cropped or rewritten");
            var companion = CanonicalEvidenceJson.ParseBundle(File.ReadAllBytes(Path.Combine(reader, "technical", "selected.evidence.json")));
            Assert(companion.SourceLedgers.SelectMany(item => item.Ledger.Records).Count() == selected.Length &&
                companion.Selection.Select(item => item.EvidenceId).SequenceEqual(selected),
                "selected-only companion preserves selected identities and every evidence edge");
            Assert(companion.SourceLedgers.Single().SourceLedgerSha256 != expanded.SourceLedgers.Single().SourceLedgerSha256,
                "constructed companion has its own digest, not a relabeled historical ledger");
            CheckCompanionNotice(reader, "included", equal: false);
            var originalRecords = historical.Records.ToDictionary(record => record.StableId);
            foreach (var record in companion.SourceLedgers.SelectMany(item => item.Ledger.Records))
            {
                var original = originalRecords[record.StableId];
                Assert(record.Claim == original.Claim && record.Provenance == original.Provenance &&
                    record.Applicability == original.Applicability && record.Supersedes.SequenceEqual(original.Supersedes),
                    "companion preserves claims, attribution, provenance and record identity");
            }
            foreach (var path in Directory.GetFiles(reader, "*", SearchOption.AllDirectories))
                AssertNoExcluded(File.ReadAllText(path), "historical reader " + Path.GetFileName(path));
            var unselected = historical.Records.Single(record => !selected.Contains(record.StableId)).StableId;
            MutateAssessment(fixture, fixture.Assessment with
            {
                Rows = [fixture.Assessment.Rows[0] with
                {
                    EvidenceIds = fixture.Assessment.Rows[0].EvidenceIds.Append(unselected).Order(StringComparer.Ordinal).ToArray()
                }, .. fixture.Assessment.Rows.Skip(1)]
            }, () => Cli(Validate(fixture), 1, "known but unselected historical evidence cannot be cited"));
        });
        var cropped = expanded with
        {
            SourceLedgers = [expanded.SourceLedgers.Single() with { Ledger = ledger }]
        };
        Reject(() => CanonicalEvidenceJson.SerializeBundle(cropped), "cropped ledger cannot retain historical digest");
        var ancestorDraft = drafts[0] with
        {
            Claim = Marker + ": BEQ-05 superseded historical payload sentinel."
        };
        var ancestorId = EvidenceLedgerBuilder.BuildComponentLedger(fixture.Assessment.Identity, [ancestorDraft])
            .Records.Single().StableId;
        var successorDraft = drafts[0] with
        {
            Claim = Marker + ": synthetic successor retaining a historical dependency.",
            Supersedes = [ancestorId]
        };
        var supersedingLedger = EvidenceLedgerBuilder.BuildComponentLedger(fixture.Assessment.Identity, [ancestorDraft, drafts[1], successorDraft]);
        var successorIds = supersedingLedger.Records.Where(record => record.StableId != ancestorId)
            .Select(record => record.StableId).Order(StringComparer.Ordinal).ToArray();
        var supersedingEvidence = EvidenceLedgerBuilder.BuildBundle(fixture.Assessment.Identity, [supersedingLedger], successorIds);
        var supersedingAssessment = fixture.Assessment with
        {
            Rows = fixture.Assessment.Rows.Select((row, index) => index == 0 ? row with { EvidenceIds = successorIds }
                : index == 1 ? row with { EvidenceIds = [ledger.Records[1].StableId] } : row).ToArray(),
            Findings = [fixture.Assessment.Findings[0] with { EvidenceIds = successorIds }]
        };
        MutateAssessment(fixture, supersedingAssessment, () =>
            MutateFile(fixture.EvidencePath, CanonicalEvidenceJson.SerializeBundle(supersedingEvidence), () =>
            {
                Cli(Validate(fixture), 0, "complete internal supersession closure remains valid");
                var output = NewPath(fixture, "superseding-revisions");
                Cli(Render(fixture, output), 0);
                var source = Path.Combine(output, "0001");
                Cli(["report", "verify", "--root", fixture.Root, "--revision", source, .. ContextOptions(fixture)], 0);
                var reader = NewPath(fixture, "superseding-reader");
                Cli(["reader", "render", "--root", fixture.Root, "--revision", source, "--output", reader,
                    .. ContextOptions(fixture)], 0, "reader honestly omits an incomplete selected-only companion");
                Cli(["reader", "verify", "--root", fixture.Root, "--revision", source, "--output", reader,
                    .. ContextOptions(fixture)], 0, "supersession omission remains deterministically verifiable");
                CheckSupersessionProjection(fixture, reader, source, supersedingAssessment, supersedingEvidence);
                Cli(["report", "verify", "--root", fixture.Root, "--revision", source, .. ContextOptions(fixture)], 0,
                    "reader projection preserves the complete internal canonical closure");
            }));
        Reject(() => EvidenceLedgerBuilder.BuildComponentLedger(fixture.Assessment.Identity, [successorDraft, drafts[1]]),
            "selected companion cannot simply drop a required historical ancestor");
        var sibling = fixture.Assessment.Identity with { ComponentId = "synthetic-sibling" };
        var siblingLedger = EvidenceLedgerBuilder.BuildComponentLedger(sibling,
            [drafts[0] with { Applicability = new("component-specific", "synthetic-sibling") }]);
        Reject(() => EvidenceLedgerBuilder.BuildBundle(fixture.Assessment.Identity, [siblingLedger],
            [siblingLedger.Records.Single().StableId]), "sibling evidence cannot establish this component");
        var wrongRecord = drafts[0] with { Provenance = drafts[0].Provenance with { ContentDigest = ZeroDigest } };
        var wrongLedger = EvidenceLedgerBuilder.BuildComponentLedger(fixture.Assessment.Identity, [wrongRecord, drafts[1]]);
        WithEvidence(fixture, wrongLedger, () => Cli(Validate(fixture), 1, "selected evidence retains exact raw input binding"));
    }

    private static void CheckSupersessionProjection(
        Fixture fixture, string reader, string source, ReadinessAssessment assessment, EvidenceBundle evidence)
    {
        CheckCompanionNotice(reader, "omitted");
        var expectedFiles = Directory.GetFiles(fixture.Reader, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(fixture.Reader, path)).ToHashSet(StringComparer.Ordinal);
        Assert(expectedFiles.Remove(Path.Combine("technical", "selected.evidence.json")),
            "independent selected records still emit the valid JSON companion");
        var actualFiles = Directory.GetFiles(reader, "*", SearchOption.AllDirectories);
        Assert(expectedFiles.SetEquals(actualFiles.Select(path => Path.GetRelativePath(reader, path))),
            "supersession omits only the JSON companion, not canonical assessment/report/receipt or reader fields");
        var allText = string.Join('\n', actualFiles.Select(File.ReadAllText));
        Assert(!System.Text.RegularExpressions.Regex.IsMatch(allText, @"\]\([^)]*selected\.evidence\.json[^)]*\)"),
            "omitted companion has no dead Markdown link");
        Assert(allText.Contains("companion", StringComparison.OrdinalIgnoreCase) &&
            (allText.Contains("omit", StringComparison.OrdinalIgnoreCase) || allText.Contains("not exported", StringComparison.OrdinalIgnoreCase)) &&
            (allText.Contains("supersession", StringComparison.OrdinalIgnoreCase) || allText.Contains("superseded", StringComparison.OrdinalIgnoreCase)) &&
            allText.Contains("internal", StringComparison.OrdinalIgnoreCase) &&
            (allText.Contains("not a self-contained", StringComparison.OrdinalIgnoreCase) ||
                allText.Contains("not self-contained", StringComparison.OrdinalIgnoreCase)),
            "omitted companion, historical dependencies and internal-only reverification are explicit");
        Assert(!allText.Contains("historical payload sentinel", StringComparison.Ordinal),
            "superseded historical claims remain internal rather than escaping through Markdown");
        foreach (var path in actualFiles)
            AssertNoExcluded(File.ReadAllText(path), "supersession reader " + Path.GetFileName(path));

        var evidenceText = File.ReadAllText(Path.Combine(reader, "evidence.md"));
        var records = evidence.SourceLedgers.Single().Ledger.Records.ToDictionary(record => record.StableId);
        foreach (var selection in evidence.Selection)
        {
            var heading = "## Evidence " + selection.DisplayOrder + "\n";
            var start = evidenceText.IndexOf(heading, StringComparison.Ordinal);
            Assert(start >= 0, "selected evidence retains its own reader section");
            var end = evidenceText.IndexOf("\n## Evidence ", start + heading.Length, StringComparison.Ordinal);
            var section = end < 0 ? evidenceText[start..] : evidenceText[start..end];
            var record = records[selection.EvidenceId];
            foreach (var field in new[]
            {
                record.StableId, selection.SourceLedgerSha256, record.Applicability.Scope, record.Applicability.ComponentId!,
                record.Provenance.ContentDigest.Value,
                record.Claim, record.Provenance.Kind, record.Provenance.Locator, record.Provenance.Method,
                record.Provenance.CapturedAtUtc, record.Provenance.Retention
            }.Concat(record.Supersedes))
            {
                Assert(section.Contains(ReaderService.Text(field), StringComparison.Ordinal),
                    "selected record metadata survives without a JSON companion: " + field);
            }
            Assert(section.Contains("SHA-256", StringComparison.OrdinalIgnoreCase) ||
                section.Contains("sha256", StringComparison.OrdinalIgnoreCase),
                "record digest algorithm remains explicit");
        }
        using var mapping = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(reader, "mapping.json")));
        using var expected = JsonDocument.Parse(AssessmentService.Serialize(assessment));
        var expectedRows = expected.RootElement.GetProperty("rows").EnumerateArray()
            .ToDictionary(row => row.GetProperty("id").GetString()!);
        var actualRows = mapping.RootElement.GetProperty("groups").EnumerateArray()
            .SelectMany(group => group.GetProperty("checks").EnumerateArray()).ToArray();
        Assert(actualRows.Length == 51 && actualRows.Select(row => row.GetProperty("id").GetString()).Distinct().Count() == 51,
            "companion omission never drops or combines assessment checks");
        foreach (var row in actualRows)
            Assert(row.GetRawText() == expectedRows[row.GetProperty("id").GetString()!].GetRawText(),
                "mapping preserves exact statuses, observations, qualifications and many-to-many evidence edges");
        using var receipt = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(reader, "reader.validation.json")));
        Assert(!receipt.RootElement.GetProperty("files").EnumerateObject()
            .Any(item => item.Name.EndsWith("selected.evidence.json", StringComparison.Ordinal)),
            "reader receipt does not claim the omitted companion was delivered");
        Assert(receipt.RootElement.GetProperty("source_validation_sha256").GetString() ==
            ContractJson.RawDigest(File.ReadAllBytes(Path.Combine(source, "component.validation.json"))).Value,
            "reader remains bound to the complete internal source receipt");
        Assert(File.ReadAllBytes(Path.Combine(source, "component.evidence.json"))
            .SequenceEqual(CanonicalEvidenceJson.SerializeBundle(evidence)),
            "original complete source bundle is not cropped, relabeled or rewritten");
        MissingFile(Path.Combine(source, "component.evidence.json"), () =>
            CliMissing(["reader", "verify", "--root", fixture.Root, "--revision", source,
                "--output", reader, .. ContextOptions(fixture)], "omitted companion never excuses missing internal evidence"));
    }

    private static void TestDisclosure(Fixture fixture)
    {
        foreach (var id in Excluded)
        foreach (var encoded in new[]
        {
            id, id.ToLowerInvariant(), id.Replace("-", "&#45;", StringComparison.Ordinal),
            id.Replace("-", "&amp;#45;", StringComparison.Ordinal), id.Replace("-", "\\u002d", StringComparison.Ordinal),
            id.Replace("-", "\u2011", StringComparison.Ordinal)
        })
        {
            MutateAssessment(fixture, fixture.Assessment with
            {
                Rows = [fixture.Assessment.Rows[0] with { Observation = Marker + ": " + encoded + " verified." },
                    .. fixture.Assessment.Rows.Skip(1)]
            }, () => Cli(Render(fixture, NewPath(fixture, "excluded-prose")), 1, "excluded prose " + encoded));
        }
        var definitions = RubricLoader.Load();
        foreach (var text in definitions.CoreRequirements.Concat(definitions.Extensions ?? [])
            .Where(row => Excluded.Contains(row.Id)).Select(row => row.Requirement)
            .Concat(["All 61 component checks are complete.", "All 121 checks passed.",
                "Versioned extension conclusion.", "LP-01: verified package finding copied into the component."]))
        {
            MutateAssessment(fixture, fixture.Assessment with
            {
                Rows = [fixture.Assessment.Rows[0] with { Observation = Marker + ": " + text },
                    .. fixture.Assessment.Rows.Skip(1)]
            }, () => Cli(Render(fixture, NewPath(fixture, "excluded-obligation")), 1,
                "excluded obligations and broader-coverage prose cannot be hidden by removing IDs"));
        }
        var disclosure = Marker + ": BEQ-05 verified.";
        MutateAssessment(fixture, fixture.Assessment with
        {
            Rows = [fixture.Assessment.Rows[0] with { Observation = Marker + ": BEQ\\-05 verified." },
                .. fixture.Assessment.Rows.Skip(1)]
        }, () => Cli(Render(fixture, NewPath(fixture, "markdown-escaped-field")), 1,
            "Markdown escapes cannot hide an excluded identifier in a structured field"));
        foreach (var changed in new[]
        {
            fixture.Assessment with { Findings = [fixture.Assessment.Findings[0] with { Title = disclosure }] },
            fixture.Assessment with { Findings = [fixture.Assessment.Findings[0] with { FactualSummary = disclosure }] },
            fixture.Assessment with { Rows = [fixture.Assessment.Rows[0] with { OwnerAction = disclosure }, .. fixture.Assessment.Rows.Skip(1)] },
            fixture.Assessment with { Rows = [fixture.Assessment.Rows[0] with { AssessmentFollowUp = disclosure }, .. fixture.Assessment.Rows.Skip(1)] },
            fixture.Assessment with { Rows = [fixture.Assessment.Rows[0] with { NotApplicableRationale = disclosure }, .. fixture.Assessment.Rows.Skip(1)] }
        })
        {
            MutateAssessment(fixture, changed, () =>
            {
                var output = NewPath(fixture, "disclosure-fallback");
                Cli(Render(fixture, output), 1, "render cannot bypass disclosure");
                Assert(!Directory.Exists(Path.Combine(output, "0001")), "no unscoped fallback publication");
            });
        }
        var ledger = fixture.Evidence.SourceLedgers.Single().Ledger;
        var drafts = ledger.Records.Select(record => new EvidenceRecordDraft(record.Claim, record.Applicability,
            record.Provenance, record.Supersedes)).ToArray();
        foreach (var changed in new[]
        {
            drafts[0] with { Claim = disclosure },
            drafts[0] with { Provenance = drafts[0].Provenance with { Method = disclosure } }
        })
        {
            var altered = EvidenceLedgerBuilder.BuildComponentLedger(fixture.Assessment.Identity, [changed, drafts[1]]);
            WithEvidence(fixture, altered, () =>
            {
                Cli(Validate(fixture), 0, "canonical evidence remains internal");
                var output = NewPath(fixture, "selected-disclosure-revisions");
                Cli(Render(fixture, output), 0);
                var reader = NewPath(fixture, "selected-disclosure-reader");
                Cli(["reader", "render", "--root", fixture.Root, "--revision", Path.Combine(output, "0001"),
                    "--output", reader, "--package-context-revision", fixture.ContextRevision], 1,
                    "selected evidence and provenance must not escape through the reader");
                Assert(!Directory.Exists(reader), "failed evidence disclosure leaves no reader or raw fallback");
            });
        }
        var feedback = Path.Combine(fixture.Root, "excluded-feedback.md");
        File.WriteAllText(feedback, "# Assessment feedback\n\n| Requirement IDs | Feedback |\n|---|---|\n" +
            "| `SEC-10` | " + disclosure + " |\n");
        var feedbackOutput = NewPath(fixture, "feedback-disclosure");
        Cli([.. Render(fixture, feedbackOutput), "--feedback", feedback], 1, "feedback payload disclosure");
        Assert(!Directory.Exists(Path.Combine(feedbackOutput, "0001")), "feedback failure cannot publish a fallback");
        File.WriteAllText(feedback, "# Assessment feedback\n\n| Requirement IDs | Feedback |\n|---|---|\n" +
            "| `SEC-10` | TOOLING TESTS / synthetic BEQ\\-05 verified. |\n");
        var escapedFeedbackOutput = NewPath(fixture, "escaped-feedback-disclosure");
        Cli([.. Render(fixture, escapedFeedbackOutput), "--feedback", feedback], 1,
            "valid feedback syntax cannot use Markdown escapes to hide excluded content");
        Assert(!Directory.Exists(Path.Combine(escapedFeedbackOutput, "0001")), "escaped feedback publishes no report");
        foreach (var relative in new[]
        {
            "report.md", "evidence.md", "mapping.json", Path.Combine("technical", "component.assessment.json"),
            Path.Combine("technical", "selected.evidence.json"), Path.Combine("technical", "component.report.md")
        })
        {
            var path = Path.Combine(fixture.Reader, relative);
            Assert(File.Exists(path), "expected reader surface for mutation: " + relative);
            var bytes = File.ReadAllBytes(path);
            MutateFile(path, [.. bytes, .. Encoding.UTF8.GetBytes("\n" + disclosure)],
                () => Cli(Reader(fixture, "verify"), 1, "tampered reader surface: " + relative));
        }
        Reject(() => ReportService.RenderMarkdown(fixture.Assessment, fixture.Input, fixture.Evidence),
            "direct renderer cannot omit root to evade profile checks");
    }

    private static void TestPublicationRaces(Fixture fixture)
    {
        foreach (var path in new[]
        {
            Path.Combine(fixture.Root, ProfileName), Path.Combine(fixture.Root, ContextName),
            Path.Combine(fixture.Root, "context-observation.txt"),
            Path.Combine(fixture.ContextRevision, "package.validation.json")
        })
        {
            var bytes = File.ReadAllBytes(path);
            var reportOutput = NewPath(fixture, "race-revisions");
            try
            {
                ReportCommand.BeforePublishForTests = () => File.WriteAllText(path, "{}");
                Cli(Render(fixture, reportOutput), 1, "report publication rechecks " + Path.GetFileName(path));
                Assert(!Directory.Exists(Path.Combine(reportOutput, "0001")), "mutation race publishes no revision");
            }
            finally
            {
                ReportCommand.BeforePublishForTests = null;
                File.WriteAllBytes(path, bytes);
            }
            var readerOutput = NewPath(fixture, "race-reader");
            try
            {
                ReaderCommand.BeforePublishForTests = () => File.WriteAllText(path, "{}");
                Cli(Reader(fixture, "render", readerOutput), 1, "reader publication rechecks " + Path.GetFileName(path));
                Assert(!Directory.Exists(readerOutput), "mutation race publishes no reader");
            }
            finally
            {
                ReaderCommand.BeforePublishForTests = null;
                File.WriteAllBytes(path, bytes);
            }
        }
        Cli(Verify(fixture), 0);
        Cli(Reader(fixture, "verify"), 0);
    }

    private static void TestCorrections(Fixture fixture)
    {
        var originalFiles = Directory.GetFiles(fixture.Revision).ToDictionary(path => path, File.ReadAllBytes);
        var predecessor = ContractJson.RawDigest(File.ReadAllBytes(Path.Combine(fixture.Revision, "component.validation.json"))).Value;
        var changed = fixture.Assessment with
        {
            Rows = [fixture.Assessment.Rows[0] with { Observation = Marker + ": changed conclusion without new evidence." },
                .. fixture.Assessment.Rows.Skip(1)]
        };
        MutateAssessment(fixture, changed, () =>
        {
            Cli([.. Render(fixture), "--predecessor", predecessor], 1, "feedback-only correction preserves findings");
            Cli([.. Render(fixture), "--predecessor", predecessor, "--changed-ids", "SEC-10"], 1,
                "changed finding cannot silently reuse unchanged evidence");
        });
        Assert(!Directory.Exists(Path.Combine(fixture.Revisions, "0002")), "failed correction preserves earlier scoped work");
        var freshInput = fixture.Input with { Exclusions = [new(Marker + ": fresh input", Marker + ": bounded identity change.")] };
        var freshBytes = InputManifestService.Serialize(freshInput);
        MutateFile(fixture.InputPath, freshBytes, () =>
        {
            Cli(Validate(fixture), 1, "changed confirmed input cannot retain old assessment/evidence identity");
            var initializedPath = NewPath(fixture, "fresh.init.json");
            Cli(Init(fixture, initializedPath), 0);
            var initialized = AssessmentService.Parse(File.ReadAllBytes(initializedPath));
            var (freshAssessment, freshEvidence) = Complete(freshInput, initialized);
            Assert(!freshEvidence.Selection.Select(item => item.EvidenceId).Intersect(
                fixture.Evidence.Selection.Select(item => item.EvidenceId), StringComparer.Ordinal).Any(),
                "fresh confirmed input produces fresh EV1 identities");
            MutateAssessment(fixture, freshAssessment, () =>
            {
                Cli(Validate(fixture), 1, "new assessment cannot reuse old evidence bundle");
                MutateFile(fixture.EvidencePath, CanonicalEvidenceJson.SerializeBundle(freshEvidence), () =>
                {
                    Cli(Validate(fixture), 0, "fresh compatible identity remains valid");
                    var output = NewPath(fixture, "fresh-lineage");
                    Cli(Render(fixture, output), 0, "new input starts a fresh lineage");
                    Cli(["report", "verify", "--root", fixture.Root, "--revision", Path.Combine(output, "0001"),
                        "--package-context-revision", fixture.ContextRevision], 0);
                    Cli([.. Render(fixture), "--predecessor", predecessor, "--changed-ids", "SEC-10"], 1,
                        "fresh input cannot be spliced into old revision lineage");
                });
            });
        });
        Cli(Verify(fixture), 0, "earlier valid result survives failed corrections");
        foreach (var (path, bytes) in originalFiles)
            Assert(File.ReadAllBytes(path).SequenceEqual(bytes), "failed corrections do not rewrite earlier canonical bytes");
        var ledger = fixture.Evidence.SourceLedgers.Single().Ledger;
        var drafts = ledger.Records.Select(record => new EvidenceRecordDraft(record.Claim, record.Applicability,
            record.Provenance, record.Supersedes)).ToArray();
        var correctedLedger = EvidenceLedgerBuilder.BuildComponentLedger(fixture.Assessment.Identity,
            [.. drafts, drafts[0] with { Claim = Marker + ": separately attributed correction observation." }]);
        var ids = correctedLedger.Records.Select(record => record.StableId).Order(StringComparer.Ordinal).ToArray();
        var correctionEvidence = EvidenceLedgerBuilder.BuildBundle(fixture.Assessment.Identity, [correctedLedger], ids);
        var correction = fixture.Assessment with
        {
            Rows = [fixture.Assessment.Rows[0] with
            {
                Observation = Marker + ": corrected qualified synthetic field; runtime remains untested.",
                EvidenceIds = ids
            }, .. fixture.Assessment.Rows.Skip(1)],
            Findings = [fixture.Assessment.Findings[0] with { EvidenceIds = ids }]
        };
        MutateAssessment(fixture, correction, () =>
            MutateFile(fixture.EvidencePath, CanonicalEvidenceJson.SerializeBundle(correctionEvidence), () =>
            {
                Cli(["assessment", "revise", .. Render(fixture).Skip(2), "--predecessor", predecessor,
                    "--changed-ids", "SEC-10"], 0, "evidence-backed correction uses existing revision mechanism");
                var correctedRevision = Path.Combine(fixture.Revisions, "0002");
                Cli(["report", "verify", "--root", fixture.Root, "--revision", correctedRevision,
                    .. ContextOptions(fixture)], 0);
                var reader = NewPath(fixture, "corrected-reader");
                Cli(["reader", "render", "--root", fixture.Root, "--revision", correctedRevision,
                    "--output", reader, .. ContextOptions(fixture)], 0);
                Cli(["reader", "verify", "--root", fixture.Root, "--revision", correctedRevision,
                    "--output", reader, .. ContextOptions(fixture)], 0);
                var retained = CanonicalEvidenceJson.ParseBundle(File.ReadAllBytes(Path.Combine(correctedRevision, "component.evidence.json")));
                Assert(fixture.Evidence.Selection.All(item => retained.Selection.Any(after => after.EvidenceId == item.EvidenceId)),
                    "valid correction preserves earlier selected evidence and attribution");
            }));
        foreach (var (path, bytes) in originalFiles)
            Assert(File.ReadAllBytes(path).SequenceEqual(bytes), "successful correction also preserves predecessor bytes");
    }

    private static void TestPartialResult(Fixture fixture)
    {
        var partial = fixture.Assessment with
        {
            Rows = fixture.Assessment.Rows.Select((row, index) => index == fixture.Assessment.Rows.Count - 1
                ? row with { Status = null, Observation = null, EvidenceIds = [], OwnerAction = null,
                    AssessmentFollowUp = null, NotApplicableRationale = null } : row).ToArray(),
            CompletionState = "targeted"
        };
        MutateAssessment(fixture, partial, () =>
        {
            Cli(Validate(fixture), 0, "partial work retains all 51 selected rows and earlier evidence");
            var output = NewPath(fixture, "partial-revisions");
            Cli(Render(fixture, output), 0);
            var result = fixture with { Revisions = output, Reader = NewPath(fixture, "partial-reader"), Assessment = partial };
            Cli(Verify(result), 0);
            Cli(Reader(result, "render"), 0);
            Cli(Reader(result, "verify"), 0);
            var text = File.ReadAllText(Path.Combine(result.Reader, "report.md"));
            Assert(text.Contains("targeted", StringComparison.Ordinal) && text.Contains("incomplete", StringComparison.Ordinal) &&
                text.Contains("runtime remains untested", StringComparison.Ordinal),
                "partial result exposes missing work without erasing earlier qualified evidence");
            MutateAssessment(fixture, partial with { CompletionState = "complete" },
                () => Cli(Validate(fixture), 1, "partial cannot be promoted to complete"));
        });
    }

    private static void WithEvidence(Fixture fixture, EvidenceSourceLedger ledger, Action action)
    {
        var ids = ledger.Records.Select(record => record.StableId).Order(StringComparer.Ordinal).ToArray();
        var evidence = EvidenceLedgerBuilder.BuildBundle(fixture.Assessment.Identity, [ledger], ids);
        var assessment = fixture.Assessment with
        {
            Rows = fixture.Assessment.Rows.Select((row, index) => index == 0 ? row with { EvidenceIds = ids }
                : index == 1 ? row with { EvidenceIds = [ids[0]] } : row).ToArray(),
            Findings = [fixture.Assessment.Findings[0] with { EvidenceIds = ids }]
        };
        MutateAssessment(fixture, assessment, () =>
            MutateFile(fixture.EvidencePath, CanonicalEvidenceJson.SerializeBundle(evidence), action));
    }

    private static void WithFreshInput(
        Fixture fixture, InputManifest input, Action<ReadinessAssessment, EvidenceBundle> action)
    {
        MutateFile(fixture.InputPath, InputManifestService.Serialize(input), () =>
        {
            var path = NewPath(fixture, "rebound.init.json");
            Cli(Init(fixture, path), 0);
            var (assessment, evidence) = Complete(input, AssessmentService.Parse(File.ReadAllBytes(path)));
            MutateAssessment(fixture, assessment, () =>
                MutateFile(fixture.EvidencePath, CanonicalEvidenceJson.SerializeBundle(evidence),
                    () => action(assessment, evidence)));
        });
    }

    private static string[] Init(Fixture f, string output) =>
        ["assessment", "init", "--kind", "component", "--component", "fancy-tree", "--root", f.Root,
            "--input", f.InputPath, "--output", output, .. ContextOptions(f)];
    private static string[] Validate(Fixture f) =>
        ["assessment", "validate", "--root", f.Root, "--input", f.InputPath, "--assessment", f.AssessmentPath,
            "--evidence", f.EvidencePath, .. ContextOptions(f)];
    private static string[] Render(Fixture f, string? output = null) =>
        ["report", "render", "--root", f.Root, "--input", f.InputPath, "--assessment", f.AssessmentPath,
            "--evidence", f.EvidencePath, "--output", output ?? f.Revisions, .. ContextOptions(f)];
    private static string[] Verify(Fixture f) =>
        ["report", "verify", "--root", f.Root, "--revision", f.Revision, .. ContextOptions(f)];
    private static string[] Reader(Fixture f, string operation, string? output = null) =>
        ["reader", operation, "--root", f.Root, "--revision", f.Revision, "--output", output ?? f.Reader,
            .. ContextOptions(f)];
    private static string[] ContextOptions(Fixture f) => f.ContextFeedback is null
        ? ["--package-context-revision", f.ContextRevision]
        : ["--package-context-revision", f.ContextRevision, "--package-context-feedback", f.ContextFeedback];
    private static string NewPath(Fixture fixture, string name) => Path.Combine(fixture.Root, name + "-" + Guid.NewGuid().ToString("N"));
    private static InputEvidenceArtifact[] Sort(params InputEvidenceArtifact[] values) =>
        values.OrderBy(item => item.Basename, StringComparer.Ordinal).ToArray();
    private static InputEvidenceArtifact Artifact(string root, string basename, string kind)
    {
        var bytes = File.ReadAllBytes(Path.Combine(root, basename));
        return new(basename, kind, ContractJson.RawDigest(bytes), bytes.LongLength);
    }
    private static string[] RemoveOption(string[] arguments, string name)
    {
        var index = Array.IndexOf(arguments, name);
        Assert(index >= 0, "test option exists: " + name);
        return arguments.Take(index).Concat(arguments.Skip(index + 2)).ToArray();
    }
    private static void MutateAssessment(Fixture fixture, ReadinessAssessment assessment, Action action) =>
        MutateFile(fixture.AssessmentPath, AssessmentService.Serialize(assessment), action);
    private static void MutateFile(string path, byte[] bytes, Action action)
    {
        var original = File.ReadAllBytes(path);
        try { File.WriteAllBytes(path, bytes); action(); }
        finally { File.WriteAllBytes(path, original); }
    }
    private static void MissingFile(string path, Action action)
    {
        var hidden = path + ".synthetic-withheld";
        File.Move(path, hidden);
        try { action(); }
        finally { File.Move(hidden, path); }
    }
    private static void AssertNoExcluded(string text, string surface)
    {
        var decoded = WebUtility.HtmlDecode(text).Replace('\u2010', '-').Replace('\u2011', '-').Replace('\u2013', '-');
        foreach (var id in Excluded)
            Assert(!decoded.Contains(id, StringComparison.OrdinalIgnoreCase), surface + " discloses excluded ID " + id);
    }
    private static void Cli(string[] arguments, int expected, string? name = null)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var result = CliApplication.Run(arguments, output, error);
        Assert(result == expected, $"{Marker}: {name ?? string.Join(' ', arguments.Take(2))}; expected {expected}, actual {result}: {error}");
        if (expected == 0) Assert(output.ToString() == "" && error.ToString() == "", "successful CLI remains quiet");
    }
    private static void CliMissing(string[] arguments, string name)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var result = CliApplication.Run(arguments, output, error);
        Assert(result is ExitCodes.ValidationFailure or ExitCodes.EnvironmentFailure &&
            !error.ToString().Contains("unexpected validator failure", StringComparison.Ordinal),
            Marker + ": missing dependency must fail closed without a crash: " + name + ": " + error);
    }
    private static void Reject(Action action, string message)
    {
        try { action(); }
        catch (DeterministicValidationException) { return; }
        throw new InvalidOperationException(Marker + ": expected rejection: " + message);
    }
    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(Marker + ": " + message);
    }
    private sealed record Fixture(
        string Root, InputManifest Input, string InputPath, ReadinessAssessment Assessment, string AssessmentPath,
        EvidenceBundle Evidence, string EvidencePath, string ContextRevision, string Revisions, string Reader,
        string? ContextFeedback = null)
    {
        public string Revision => Path.Combine(Revisions, "0001");
    }
}
