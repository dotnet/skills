using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using BlazorComponentReadiness.Validator.Cli;
using BlazorComponentReadiness.Validator.Contracts;
using BlazorComponentReadiness.Validator.Evidence;
using BlazorComponentReadiness.Validator.IO;

internal static class EvidenceTests
{
    private const string KnownEvidenceId =
        "EV1-fdcf80cd04ab59269765ea79b7ccb8e9240596c5974f5221e90069fc015555cb";
    private const string KnownLedgerSha256 =
        "7fcada16a9a7cd2e2ac4de7fe73452d4edf98b7c1ac28e88e09c17094852965a";
    private const string KnownAssessmentJson =
        """{"assessment_kind":"unified","package":{"package_id":"sample.widgets","version":"1.2.3","nupkg_sha256":{"algorithm":"sha256","value":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"}},"input_manifest_sha256":{"algorithm":"sha256","value":"cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc"},"component_id":"Tree"}""";
    private const string KnownLedgerJson =
        """{"schema_version":1,"ledger_kind":"repository","repository_subject":{"assessment_kind":"unified","package":{"package_id":"sample.widgets","version":"1.2.3","nupkg_sha256":{"algorithm":"sha256","value":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"}},"input_manifest_sha256":{"algorithm":"sha256","value":"cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc"},"component_id":"Tree"},"component_subject":null,"records":[{"stable_id":"EV1-fdcf80cd04ab59269765ea79b7ccb8e9240596c5974f5221e90069fc015555cb","claim":"Vendor documentation states keyboard support.","applicability":{"scope":"repository-wide","component_id":null},"provenance":{"kind":"vendor-public-documentation","locator":"https://docs.example.com/widgets","method":"Captured official documentation.","captured_at_utc":"2026-09-02T20:00:00Z","content_sha256":{"algorithm":"sha256","value":"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"},"retention":"commitment-only"},"supersedes":[]}]}""";

    public static void Run(string repositoryRoot, string pluginRoot)
    {
        EvidenceHandoffTests.Run(repositoryRoot, pluginRoot);
        TestKnownAnswersAndCanonicalBytes();
        TestCliCommandSurface();
        TestEvidenceDraftProducer(repositoryRoot);
        TestPackageInspectionPersistence(repositoryRoot);
        TestAssessmentKindsAndManifestBinding();
        TestNuGetVersionNormalization(repositoryRoot);
        TestPublicProvenanceKinds();
        TestScopeAndExactIdentity();
        TestHostNeutralSourceMapping();
        TestBundleSelectionAndAmbiguity();
        TestSupersession();
        TestPackageArtifactVerification(repositoryRoot);
        TestResourceCeilings();
        TestCliSubprocesses(repositoryRoot, pluginRoot);
    }

    private static void TestAssessmentKindsAndManifestBinding()
    {
        var unified = KnownAssessment();
        var package = unified with
        {
            AssessmentKind = "package",
            ComponentId = null
        };
        var component = unified with
        {
            AssessmentKind = "component"
        };
        EvidenceIdentity.ValidateAssessment(unified);
        EvidenceIdentity.ValidateAssessment(package);
        EvidenceIdentity.ValidateAssessment(component);

        ExpectValidation(
            () => EvidenceIdentity.ValidateAssessment(
                package with { ComponentId = "Tree" }),
            "package assessment component ID");
        ExpectValidation(
            () => EvidenceIdentity.ValidateAssessment(
                unified with { ComponentId = null }),
            "unified assessment missing component ID");
        ExpectValidation(
            () => EvidenceIdentity.ValidateAssessment(
                component with { ComponentId = null }),
            "component assessment missing component ID");

        var unifiedSubject = KnownRepositorySubject();
        var packageSubject = new RepositoryLedgerSubject(
            "package",
            package.Package,
            package.InputManifestDigest,
            null);
        var draft = RepositoryDraft(
            EvidenceIdentity.VendorPublicDocumentation,
            "https://docs.example.com/identity",
            "Assessment identity is bound.");
        var unifiedId = EvidenceLedgerBuilder.BuildRepositoryLedger(
            unifiedSubject,
            [draft]).Records.Single().StableId;
        var packageId = EvidenceLedgerBuilder.BuildRepositoryLedger(
            packageSubject,
            [draft]).Records.Single().StableId;
        var changedManifestId = EvidenceLedgerBuilder.BuildRepositoryLedger(
            unifiedSubject with
            {
                InputManifestDigest = new Sha256Digest("sha256", new string('d', 64))
            },
            [draft]).Records.Single().StableId;
        Assert(unifiedId != packageId, "assessment_kind participates in stable ID preimage");
        Assert(unifiedId != changedManifestId, "input manifest digest participates in stable ID preimage");
        var packageLedger = EvidenceLedgerBuilder.BuildRepositoryLedger(
            packageSubject,
            [draft]);
        AssertEqual(
            packageId,
            EvidenceLedgerBuilder.BuildBundle(
                package,
                [packageLedger],
                [packageId]).Selection.Single().EvidenceId,
            "package assessment has no component ID");
        ExpectValidation(
            () => EvidenceLedgerBuilder.BuildComponentLedger(
                package,
                [ComponentDraft("Tree")]),
            "package assessment component ledger");
        ExpectValidation(
            () => EvidenceLedgerBuilder.BuildRepositoryLedger(
                new RepositoryLedgerSubject(
                    "component",
                    component.Package,
                    component.InputManifestDigest,
                    component.ComponentId),
                [draft]),
            "component assessment repository ledger");

        var repositoryLedger = EvidenceLedgerBuilder.BuildRepositoryLedger(
            unifiedSubject,
            [draft]);
        ExpectValidation(
            () => EvidenceLedgerBuilder.BuildBundle(
                unified with
                {
                    InputManifestDigest = new Sha256Digest("sha256", new string('d', 64))
                },
                [repositoryLedger],
                [repositoryLedger.Records[0].StableId]),
            "input manifest compatibility mismatch");
        ExpectValidation(
            () => EvidenceLedgerBuilder.BuildBundle(
                component,
                [repositoryLedger],
                [repositoryLedger.Records[0].StableId]),
            "assessment kind compatibility mismatch");

        var noncanonicalPackageBytes = Encoding.UTF8.GetBytes(
            KnownAssessmentJson.Replace(
                "\"version\":\"1.2.3\"",
                "\"version\":\"01.02.003\"",
                StringComparison.Ordinal));
        ExpectValidation(
            () => CanonicalEvidenceJson.ParseAssessment(noncanonicalPackageBytes),
            "noncanonical subject package version");
    }

    private static void TestCliCommandSurface()
    {
        var rootOutput = new StringWriter();
        AssertEqual(
            ExitCodes.Success,
            CliApplication.Run(["help"], rootOutput, new StringWriter()),
            "root command help exit");
        var helpExamples = rootOutput.ToString()
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.StartsWith("readiness-validator ", StringComparison.Ordinal) &&
                line.EndsWith(" --help", StringComparison.Ordinal))
            .ToArray();
        AssertEqual(2, helpExamples.Length, "root help publishes executable hierarchical examples");
        foreach (var example in helpExamples)
        {
            var words = example.Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(1).ToArray();
            AssertEqual(
                ExitCodes.Success,
                CliApplication.Run(words, new StringWriter(), new StringWriter()),
                $"published help example runs: {example}");
            AssertEqual(
                ExitCodes.InvalidUsage,
                CliApplication.Run(
                    [string.Join(' ', words[..^1]), "--help"],
                    new StringWriter(),
                    new StringWriter()),
                "multiword command remains invalid as one argument");
        }

        var output = new StringWriter();
        var error = new StringWriter();
        AssertEqual(
            ExitCodes.Success,
            CliApplication.Run(["package", "inspect", "--help"], output, error),
            "package inspection help exit");
        Assert(
            output.ToString().Contains("--output <new-file>", StringComparison.Ordinal),
            "package inspection help documents persisted output");
        output.GetStringBuilder().Clear();
        error.GetStringBuilder().Clear();
        AssertEqual(
            ExitCodes.Success,
            CliApplication.Run(["evidence", "--help"], output, error),
            "evidence help exit");
        foreach (var command in new[] { "ledger-build", "ledger-validate", "bundle" })
        {
            output.GetStringBuilder().Clear();
            error.GetStringBuilder().Clear();
            AssertEqual(
                ExitCodes.Success,
                CliApplication.Run(["evidence", command, "--help"], output, error),
                $"{command} help exit");
            Assert(
                output.ToString().Contains($"evidence {command}", StringComparison.Ordinal),
                $"{command} help contains exact grouped command");
        }

        Assert(
            output.ToString().Contains("draft-add", StringComparison.Ordinal) &&
            output.ToString().Contains(EvidenceIdentity.OwnerDeclaredUnavailable, StringComparison.Ordinal),
            "evidence help documents draft producer and existing provenance kinds");
    }

    private static void TestEvidenceDraftProducer(string repositoryRoot)
    {
        var root = Path.Combine(
            Environment.GetEnvironmentVariable("READINESS_TEST_ARTIFACTS") ?? Path.Combine(repositoryRoot, "artifacts"),
            "readiness-evidence-draft-tests");
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }

        Directory.CreateDirectory(root);
        try
        {
            var contentPath = Path.Combine(root, "source.txt");
            File.WriteAllBytes(contentPath, [3]);
            var outputPath = Path.Combine(root, "draft-01.json");
            var arguments = new[]
            {
                "evidence", "draft-add",
                "--output", outputPath,
                "--claim", "The retained source contains the observed bytes.",
                "--scope", "repository-wide",
                "--kind", EvidenceIdentity.VendorSourceRepository,
                "--locator", "source:src/Observed.cs",
                "--method", "Read retained source file.",
                "--captured-at", "2026-09-07T18:00:00Z",
                "--content", contentPath
            };
            AssertEqual(
                ExitCodes.Success,
                CliApplication.Run(arguments, new StringWriter(), new StringWriter()),
                "evidence draft producer exit");
            var firstBytes = File.ReadAllBytes(outputPath);
            AssertEqual(
                "2026-09-07T18:00:00Z",
                CanonicalEvidenceJson.ParseDraftDocument(firstBytes).Records.Single().Provenance.CapturedAtUtc,
                "explicit historical capture instant is preserved, not replaced by the current clock");
            var secondPath = Path.Combine(root, "draft-02.json");
            arguments[3] = secondPath;
            AssertEqual(
                ExitCodes.Success,
                CliApplication.Run(arguments, new StringWriter(), new StringWriter()),
                "repeat evidence draft producer exit");
            AssertBytes(firstBytes, File.ReadAllBytes(secondPath), "deterministic evidence draft bytes");

            var appendedPath = Path.Combine(root, "draft-03.json");
            var appendArguments = arguments
                .Append("--input")
                .Append(outputPath)
                .ToArray();
            appendArguments[3] = appendedPath;
            AssertEqual(
                ExitCodes.Success,
                CliApplication.Run(appendArguments, new StringWriter(), new StringWriter()),
                "append evidence draft producer exit");
            Assert(
                CanonicalEvidenceJson.ParseDraftDocument(File.ReadAllBytes(appendedPath)).Records.Count == 2,
                "evidence draft producer appends without repair");
            var manualInputPath = Path.Combine(root, "manual-draft.json");
            var manualBytes = Encoding.UTF8.GetBytes(
                " \n" + DraftJson(CanonicalEvidenceJson.ParseDraftDocument(firstBytes).Records.Single()) + "\n");
            File.WriteAllBytes(manualInputPath, manualBytes);
            var manualOriginal = File.ReadAllBytes(manualInputPath);
            var manualOutputPath = Path.Combine(root, "manual-draft-next.json");
            var manualArguments = arguments
                .Append("--input")
                .Append(manualInputPath)
                .ToArray();
            manualArguments[3] = manualOutputPath;
            AssertEqual(
                ExitCodes.Success,
                CliApplication.Run(manualArguments, new StringWriter(), new StringWriter()),
                "manual valid draft append exit");
            AssertBytes(manualOriginal, File.ReadAllBytes(manualInputPath), "manual draft remains unchanged");

            const string ExpectedContentDigest =
                "084fed08b978af4d7d196a7446a86b58009e636b611db16211b65a9aadff29c5";
            var contentKinds = new (string Kind, string Locator)[]
            {
                (EvidenceIdentity.VendorPublicDocumentation, "https://docs.example.com/record"),
                (EvidenceIdentity.VendorSourceRepository, "source:src/Observed.cs"),
                (EvidenceIdentity.ReproducedRuntimeObservation, "dotnet test --filter Evidence.Record"),
                (EvidenceIdentity.ReviewerGeneratedAnalysis, "review evidence --scope package"),
                (EvidenceIdentity.OwnerSuppliedInternalEvidence, "owner-internal.txt"),
                (EvidenceIdentity.OwnerSuppliedPublicEvidence, "owner-public.txt"),
                (EvidenceIdentity.OwnerDeclaredClosedSource, "source"),
                (EvidenceIdentity.OwnerDeclaredUnavailable, "artifact:unavailable")
            };
            foreach (var (kind, locator) in contentKinds)
            {
                var kindOutput = Path.Combine(
                    root,
                    $"kind-{kind}.json");
                var kindArguments = new[]
                {
                    "evidence", "draft-add",
                    "--output", kindOutput,
                    "--claim", $"The retained {kind} record is explicit.",
                    "--scope", "repository-wide",
                    "--kind", kind,
                    "--locator", locator,
                    "--method", "Read the retained evidence file.",
                    "--captured-at", "2026-09-07T18:00:00Z",
                    "--content", contentPath
                };
                AssertEqual(
                    ExitCodes.Success,
                    CliApplication.Run(kindArguments, new StringWriter(), new StringWriter()),
                    $"producer accepts existing kind {kind}");
                var kindRecord = CanonicalEvidenceJson.ParseDraftDocument(File.ReadAllBytes(kindOutput))
                    .Records.Single();
                AssertEqual(ExpectedContentDigest, kindRecord.Provenance.ContentDigest.Value,
                    $"producer computes exact digest for {kind}");
            }

            var packagePath = Path.Combine(root, "Sample.Widgets.1.2.3.nupkg");
            byte[] entryBytes = [3];
            CreatePackage(
                packagePath,
                "Sample.Widgets",
                "1.2.3",
                new Dictionary<string, byte[]> { ["lib/Observed.dll"] = entryBytes });
            var packageDraft = Path.Combine(root, "package-draft.json");
            var packageArguments = new[]
            {
                "evidence", "draft-add",
                "--output", packageDraft,
                "--claim", "The package contains the observed artifact.",
                "--scope", "repository-wide",
                "--kind", EvidenceIdentity.PackageArtifactMetadata,
                "--locator", NupkgInspector.WholePackageEvidenceLocator,
                "--method", "Inspected the retained package.",
                "--captured-at", "2026-09-07T18:00:00Z",
                "--nupkg", packagePath
            };
            AssertEqual(
                ExitCodes.Success,
                CliApplication.Run(packageArguments, new StringWriter(), new StringWriter()),
                "package whole evidence draft producer exit");
            var wholeRecord = CanonicalEvidenceJson.ParseDraftDocument(File.ReadAllBytes(packageDraft))
                .Records.Single();
            AssertEqual(
                NupkgInspector.ComputeEvidenceContentSha256(
                    packagePath,
                    NupkgInspector.WholePackageEvidenceLocator),
                wholeRecord.Provenance.ContentDigest.Value,
                "package whole digest preserves every hex digit");
            var entryDraft = Path.Combine(root, "package-entry-draft.json");
            packageArguments[3] = entryDraft;
            packageArguments[11] = NupkgInspector.PackageEntryEvidencePrefix + "lib/Observed.dll";
            packageArguments = packageArguments
                .Append("--input")
                .Append(packageDraft)
                .ToArray();
            AssertEqual(
                ExitCodes.Success,
                CliApplication.Run(packageArguments, new StringWriter(), new StringWriter()),
                "package entry evidence draft producer exit");
            var entryRecord = CanonicalEvidenceJson.ParseDraftDocument(File.ReadAllBytes(entryDraft))
                .Records.Last();
            AssertEqual(
                ExpectedContentDigest,
                entryRecord.Provenance.ContentDigest.Value,
                "package entry digest preserves every hex digit");

            var subjectPath = Path.Combine(root, "subject.json");
            var inspectedPackage = NupkgInspector.Inspect(packagePath);
            var packageSubject = KnownRepositorySubject() with
            {
                Package = new EvidencePackageIdentity(
                    inspectedPackage.Id,
                    inspectedPackage.Version,
                    new Sha256Digest("sha256", inspectedPackage.NupkgSha256))
            };
            File.WriteAllBytes(subjectPath, CanonicalEvidenceJson.SerializeRepositorySubject(packageSubject));
            var ledgerPath = Path.Combine(root, "ledger.json");
            AssertEqual(
                ExitCodes.Success,
                CliApplication.Run(
                    [
                        "evidence", "ledger-build",
                        "--kind", "repository",
                        "--subject", subjectPath,
                        "--draft", entryDraft,
                        "--nupkg", packagePath,
                        "--output", ledgerPath
                    ],
                    new StringWriter(),
                    new StringWriter()),
                "producer ledger integration");
            var changedPackagePath = Path.Combine(root, "Sample.Widgets.changed.nupkg");
            CreatePackage(
                changedPackagePath,
                "Sample.Widgets",
                "1.2.3",
                new Dictionary<string, byte[]> { ["lib/Observed.dll"] = Encoding.UTF8.GetBytes("changed") });
            var changedLedgerPath = Path.Combine(root, "changed-ledger.json");
            AssertEqual(
                ExitCodes.ValidationFailure,
                CliApplication.Run(
                    [
                        "evidence", "ledger-build",
                        "--kind", "repository",
                        "--subject", subjectPath,
                        "--draft", entryDraft,
                        "--nupkg", changedPackagePath,
                        "--output", changedLedgerPath
                    ],
                    new StringWriter(),
                    new StringWriter()),
                "changed package binding is rejected");
            Assert(!File.Exists(changedLedgerPath), "changed package binding has no output");

            var invalidOutput = Path.Combine(root, "invalid.json");
            var invalidArguments = arguments
                .Select(value => value == EvidenceIdentity.VendorSourceRepository
                    ? "source-artifact"
                    : value)
                .ToArray();
            invalidArguments[3] = invalidOutput;
            AssertEqual(
                ExitCodes.ValidationFailure,
                CliApplication.Run(invalidArguments, new StringWriter(), new StringWriter()),
                "evidence draft producer rejects invented kind");
            Assert(!File.Exists(invalidOutput), "invalid evidence draft has no output");

            var overrideOutput = Path.Combine(root, "hash-override.json");
            var overrideArguments = arguments
                .Append("--content-sha256")
                .Append(new string('a', 64))
                .ToArray();
            overrideArguments[3] = overrideOutput;
            AssertEqual(
                ExitCodes.InvalidUsage,
                CliApplication.Run(overrideArguments, new StringWriter(), new StringWriter()),
                "evidence draft producer rejects caller hash");
            Assert(!File.Exists(overrideOutput), "hash override has no output");

            var bothModesOutput = Path.Combine(root, "both-modes.json");
            var bothModesArguments = arguments
                .Append("--nupkg")
                .Append(contentPath)
                .ToArray();
            bothModesArguments[3] = bothModesOutput;
            AssertEqual(
                ExitCodes.InvalidUsage,
                CliApplication.Run(bothModesArguments, new StringWriter(), new StringWriter()),
                "evidence draft producer rejects mutually exclusive modes");
            Assert(!File.Exists(bothModesOutput), "both source modes have no output");

            var noModeOutput = Path.Combine(root, "no-source-mode.json");
            var noModeArguments = arguments[..16];
            noModeArguments[3] = noModeOutput;
            AssertEqual(
                ExitCodes.InvalidUsage,
                CliApplication.Run(noModeArguments, new StringWriter(), new StringWriter()),
                "evidence draft producer requires a retained content source");
            Assert(!File.Exists(noModeOutput), "missing source mode has no output");

            var missingOutput = Path.Combine(root, "missing-content.json");
            var missingArguments = arguments.ToArray();
            missingArguments[17] = Path.Combine(root, "missing.txt");
            missingArguments[3] = missingOutput;
            AssertEqual(
                ExitCodes.EnvironmentFailure,
                CliApplication.Run(missingArguments, new StringWriter(), new StringWriter()),
                "evidence draft producer rejects missing content");
            Assert(!File.Exists(missingOutput), "missing content has no output");

            var oversizedPath = Path.Combine(root, "oversized-content.bin");
            using (var oversized = new FileStream(oversizedPath, FileMode.CreateNew))
            {
                oversized.SetLength(ResourceLimits.SupplementalInputAggregateBytes + 1);
            }

            var oversizedOutput = Path.Combine(root, "oversized-content.json");
            var oversizedArguments = arguments.ToArray();
            oversizedArguments[17] = oversizedPath;
            oversizedArguments[3] = oversizedOutput;
            AssertEqual(
                ExitCodes.ValidationFailure,
                CliApplication.Run(oversizedArguments, new StringWriter(), new StringWriter()),
                "evidence draft producer enforces retained content bound");
            Assert(!File.Exists(oversizedOutput), "oversized content has no output");

            var largeRetainedPath = Path.Combine(root, "large-retained-content.bin");
            using (var largeRetained = new FileStream(largeRetainedPath, FileMode.CreateNew))
            {
                largeRetained.SetLength(4L * 1024 * 1024 + 1);
            }

            var largeRetainedOutput = Path.Combine(root, "large-retained-content.json");
            var largeRetainedArguments = arguments.ToArray();
            largeRetainedArguments[17] = largeRetainedPath;
            largeRetainedArguments[3] = largeRetainedOutput;
            AssertEqual(
                ExitCodes.Success,
                CliApplication.Run(largeRetainedArguments, new StringWriter(), new StringWriter()),
                "evidence producer accepts retained content above JSON cap");
            AssertEqual(
                CanonicalEvidenceJson.ComputeSha256(File.ReadAllBytes(largeRetainedPath)),
                CanonicalEvidenceJson.ParseDraftDocument(File.ReadAllBytes(largeRetainedOutput))
                    .Records.Single().Provenance.ContentDigest.Value,
                "large retained content digest");

            var metadataCases = new[]
            {
                ("bad-locator.json", 11, "source:../unsafe"),
                ("bad-scope.json", 7, "not-a-scope"),
                ("bad-time.json", 15, "not-a-timestamp"),
                ("offset-time.json", 15, "2026-09-07T18:00:00+00:00"),
                ("fractional-time.json", 15, "2026-09-07T18:00:00.000Z")
            };
            foreach (var (fileName, index, value) in metadataCases)
            {
                var metadataOutput = Path.Combine(root, fileName);
                var metadataArguments = arguments.ToArray();
                metadataArguments[index] = value;
                metadataArguments[3] = metadataOutput;
                AssertEqual(
                    ExitCodes.ValidationFailure,
                    CliApplication.Run(metadataArguments, new StringWriter(), new StringWriter()),
                    $"producer rejects {fileName}");
                Assert(!File.Exists(metadataOutput), $"{fileName} has no output");
            }

            var packageModeOutput = Path.Combine(root, "wrong-package-mode.json");
            var packageModeArguments = arguments.ToArray();
            packageModeArguments[9] = EvidenceIdentity.PackageArtifactMetadata;
            packageModeArguments[3] = packageModeOutput;
            AssertEqual(
                ExitCodes.InvalidUsage,
                CliApplication.Run(packageModeArguments, new StringWriter(), new StringWriter()),
                "package kind requires nupkg mode");
            Assert(!File.Exists(packageModeOutput), "wrong package mode has no output");

            var contentModeOutput = Path.Combine(root, "wrong-content-mode.json");
            var contentModeArguments = arguments.ToArray();
            contentModeArguments[16] = "--nupkg";
            contentModeArguments[3] = contentModeOutput;
            AssertEqual(
                ExitCodes.InvalidUsage,
                CliApplication.Run(contentModeArguments, new StringWriter(), new StringWriter()),
                "non-package kind requires content mode");
            Assert(!File.Exists(contentModeOutput), "wrong content mode has no output");

            var invalidInputPath = Path.Combine(root, "invalid-existing.json");
            var invalidInput = DraftJson(
                RepositoryDraft(
                    EvidenceIdentity.VendorSourceRepository,
                    "source:src/Observed.cs",
                    "Invalid existing draft.") with
                {
                    Provenance = RepositoryDraft(
                        EvidenceIdentity.VendorSourceRepository,
                        "source:src/Observed.cs",
                        "Invalid existing draft.").Provenance with
                    {
                        ContentDigest = new Sha256Digest("sha256", new string('a', 63))
                    }
                });
            File.WriteAllText(invalidInputPath, invalidInput);
            var invalidInputOriginal = File.ReadAllBytes(invalidInputPath);
            var invalidInputOutput = Path.Combine(root, "invalid-existing-output.json");
            var invalidInputArguments = arguments
                .Append("--input")
                .Append(invalidInputPath)
                .ToArray();
            invalidInputArguments[3] = invalidInputOutput;
            AssertEqual(
                ExitCodes.ValidationFailure,
                CliApplication.Run(invalidInputArguments, new StringWriter(), new StringWriter()),
                "invalid existing draft is rejected");
            Assert(!File.Exists(invalidInputOutput), "invalid existing draft has no output");
            AssertBytes(invalidInputOriginal, File.ReadAllBytes(invalidInputPath),
                "invalid existing draft is not repaired");

            var supersedesOutput = Path.Combine(root, "empty-supersedes.json");
            var supersedesArguments = arguments
                .Append("--supersedes")
                .Append(",")
                .ToArray();
            supersedesArguments[3] = supersedesOutput;
            AssertEqual(
                ExitCodes.ValidationFailure,
                CliApplication.Run(supersedesArguments, new StringWriter(), new StringWriter()),
                "empty supersedes identifiers are rejected");
            Assert(!File.Exists(supersedesOutput), "empty supersedes has no output");

            var existingOutput = Path.Combine(root, "existing-output.json");
            var existingOutputOriginal = Encoding.UTF8.GetBytes("existing");
            File.WriteAllBytes(existingOutput, existingOutputOriginal);
            var existingOutputArguments = arguments.ToArray();
            existingOutputArguments[3] = existingOutput;
            AssertEqual(
                ExitCodes.EnvironmentFailure,
                CliApplication.Run(existingOutputArguments, new StringWriter(), new StringWriter()),
                "existing output is not overwritten");
            AssertBytes(existingOutputOriginal, File.ReadAllBytes(existingOutput),
                "existing output remains unchanged");

            var directoryOutput = Path.Combine(root, "directory-content.json");
            var directoryArguments = arguments.ToArray();
            directoryArguments[17] = root;
            directoryArguments[3] = directoryOutput;
            AssertEqual(
                ExitCodes.EnvironmentFailure,
                CliApplication.Run(directoryArguments, new StringWriter(), new StringWriter()),
                "directory content is rejected");
            Assert(!File.Exists(directoryOutput), "directory content has no output");

            var symlink = Path.Combine(root, "content-link.txt");
            if (TryCreateFileSymlink(symlink, contentPath))
            {
                var symlinkOutput = Path.Combine(root, "symlink-content.json");
                var symlinkArguments = arguments.ToArray();
                symlinkArguments[17] = symlink;
                symlinkArguments[3] = symlinkOutput;
                AssertEqual(
                    ExitCodes.ValidationFailure,
                    CliApplication.Run(symlinkArguments, new StringWriter(), new StringWriter()),
                    "symlink content is rejected");
                Assert(!File.Exists(symlinkOutput), "symlink content has no output");
            }
            var packageFailureCases = new[]
            {
                ("wrong-case-entry.json", NupkgInspector.PackageEntryEvidencePrefix + "lib/observed.dll"),
                ("missing-entry.json", NupkgInspector.PackageEntryEvidencePrefix + "lib/Missing.dll"),
                ("unsafe-entry.json", NupkgInspector.PackageEntryEvidencePrefix + "../escape")
            };
            foreach (var (fileName, locator) in packageFailureCases)
            {
                var packageFailureOutput = Path.Combine(root, fileName);
                var packageFailureArguments = packageArguments.ToArray();
                packageFailureArguments[3] = packageFailureOutput;
                packageFailureArguments[11] = locator;
                AssertEqual(
                    ExitCodes.ValidationFailure,
                    CliApplication.Run(packageFailureArguments, new StringWriter(), new StringWriter()),
                    $"producer rejects {fileName}");
                Assert(!File.Exists(packageFailureOutput), $"{fileName} has no output");
            }
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static void TestNuGetVersionNormalization(string repositoryRoot)
    {
        var root = Path.Combine(
            Environment.GetEnvironmentVariable("READINESS_TEST_ARTIFACTS") ?? Path.Combine(repositoryRoot, "artifacts"),
            "readiness-version-tests");
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }

        Directory.CreateDirectory(root);
        try
        {
            var cases = new[]
            {
                ("01.002", "1.2.0"),
                ("1.2", "1.2.0"),
                ("1.2.3", "1.2.3"),
                ("1.2.3.0", "1.2.3"),
                ("1.2.3.4", "1.2.3.4"),
                ("1.02.003-BETA.0002+BUILD.0007", "1.2.3-beta.2+build.7")
            };
            for (var index = 0; index < cases.Length; index++)
            {
                var packagePath = Path.Combine(root, $"Version.{index}.nupkg");
                CreatePackage(packagePath, "Version.Test", cases[index].Item1);
                AssertEqual(
                    cases[index].Item2,
                    NupkgInspector.Inspect(packagePath).Version,
                    $"canonical nupkg version {cases[index].Item1}");
            }

            foreach (var invalid in new[] { "1..2", "1.2.2147483648", "1.2.3-", "1.2.3+" })
            {
                var packagePath = Path.Combine(
                    root,
                    $"Invalid.{Convert.ToHexString(Encoding.UTF8.GetBytes(invalid))}.nupkg");
                CreatePackage(packagePath, "Version.Invalid", invalid);
                ExpectValidation(
                    () => NupkgInspector.Inspect(packagePath),
                    $"invalid nupkg version {invalid}");
            }

            var cliPackage = Path.Combine(root, "Cli.Version.nupkg");
            CreatePackage(cliPackage, "Version.Cli", "02.004.000-BETA.0001+BUILD.0009");
            var output = new StringWriter();
            var error = new StringWriter();
            AssertEqual(
                ExitCodes.Success,
                CliApplication.Run(
                    ["package", "inspect", "--nupkg", cliPackage],
                    output,
                    error),
                "canonical package inspect CLI");
            using var result = StrictJson.Parse(
                Encoding.UTF8.GetBytes(output.ToString().TrimEnd()),
                4096,
                "canonical package inspect output");
            AssertEqual(
                "2.4.0-beta.1+build.9",
                result.RootElement.GetProperty("package_version").GetString(),
                "package inspect emits canonical version");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static void TestPackageInspectionPersistence(string repositoryRoot)
    {
        var root = Path.Combine(
            Environment.GetEnvironmentVariable("READINESS_TEST_ARTIFACTS") ?? Path.Combine(repositoryRoot, "artifacts"),
            "readiness-package-inspection-tests");
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }

        Directory.CreateDirectory(root);
        try
        {
            var packagePath = Path.Combine(root, "Inspection.Sample.1.2.3.nupkg");
            CreatePackage(packagePath, "Inspection.Sample", "1.2.3");
            var packageBytes = File.ReadAllBytes(packagePath);
            var stdout = new StringWriter { NewLine = Environment.NewLine };
            AssertEqual(
                ExitCodes.Success,
                CliApplication.Run(["package", "inspect", "--nupkg", packagePath], stdout, new StringWriter()),
                "package inspection stdout exit");
            var expectedBytes = Encoding.UTF8.GetBytes(stdout.ToString());
            var outputPath = Path.Combine(root, "package-inspection.json");
            AssertEqual(
                ExitCodes.Success,
                CliApplication.Run(
                    ["package", "inspect", "--nupkg", packagePath, "--output", outputPath],
                    new StringWriter(),
                    new StringWriter()),
                "package inspection persisted exit");
            var persistedOutput = new StringWriter();
            AssertEqual(
                ExitCodes.Success,
                CliApplication.Run(
                    ["package", "inspect", "--nupkg", packagePath, "--output", Path.Combine(root, "stdout-empty.json")],
                    persistedOutput,
                    new StringWriter()),
                "package inspection persisted stdout exit");
            AssertEqual(
                string.Empty,
                persistedOutput.ToString(),
                "persisted package inspection does not write stdout");
            AssertBytes(expectedBytes, File.ReadAllBytes(outputPath), "persisted package inspection bytes");
            AssertBytes(packageBytes, File.ReadAllBytes(packagePath), "package inspection leaves input unchanged");
            using var stdoutJson = StrictJson.Parse(
                expectedBytes.AsSpan(..^Environment.NewLine.Length).ToArray(),
                4096,
                "stdout package inspection");
            using var persistedJson = StrictJson.Parse(
                File.ReadAllBytes(outputPath).AsSpan(..^Environment.NewLine.Length).ToArray(),
                4096,
                "persisted package inspection");
            AssertEqual(
                stdoutJson.RootElement.GetRawText(),
                persistedJson.RootElement.GetRawText(),
                "persisted package inspection identity");
            var secondOutputPath = Path.Combine(root, "package-inspection-second.json");
            AssertEqual(
                ExitCodes.Success,
                CliApplication.Run(
                    ["package", "inspect", "--nupkg", packagePath, "--output", secondOutputPath],
                    new StringWriter(),
                    new StringWriter()),
                "repeat package inspection persisted exit");
            AssertBytes(
                File.ReadAllBytes(outputPath),
                File.ReadAllBytes(secondOutputPath),
                "deterministic persisted package inspection");

            foreach (var arguments in new IReadOnlyList<string>[]
            {
                ["package", "inspect", "--unknown", packagePath],
                ["package", "inspect", "--nupkg", packagePath, "--nupkg", packagePath],
                ["package", "inspect", "--nupkg", packagePath, "--output", outputPath, "--output", secondOutputPath],
                ["package", "inspect", "--nupkg", ""],
                ["package", "inspect", "--nupkg", packagePath, "--output", ""]
            })
            {
                AssertEqual(
                    ExitCodes.InvalidUsage,
                    CliApplication.Run(arguments, new StringWriter(), new StringWriter()),
                    $"package inspection invalid options '{string.Join(' ', arguments)}'");
            }

            var existingBytes = Encoding.UTF8.GetBytes("existing");
            var existingPath = Path.Combine(root, "existing.json");
            File.WriteAllBytes(existingPath, existingBytes);
            AssertPackageInspectionFailure(
                ["package", "inspect", "--nupkg", packagePath, "--output", existingPath],
                ExitCodes.EnvironmentFailure,
                existingPath,
                "existing package inspection output",
                requireAbsentOutput: false);
            AssertBytes(existingBytes, File.ReadAllBytes(existingPath), "existing output remains unchanged");

            var directoryPath = Path.Combine(root, "directory-output");
            Directory.CreateDirectory(directoryPath);
            AssertPackageInspectionFailure(
                ["package", "inspect", "--nupkg", packagePath, "--output", directoryPath],
                ExitCodes.ValidationFailure,
                directoryPath,
                "directory package inspection output");
            var missingOutput = Path.Combine(root, "missing", "result.json");
            AssertPackageInspectionFailure(
                ["package", "inspect", "--nupkg", packagePath, "--output", missingOutput],
                ExitCodes.EnvironmentFailure,
                missingOutput,
                "missing parent package inspection output");
            AssertPackageInspectionFailure(
                ["package", "inspect", "--nupkg", Path.Combine(root, "missing.nupkg"), "--output", Path.Combine(root, "missing-input.json")],
                ExitCodes.EnvironmentFailure,
                Path.Combine(root, "missing-input.json"),
                "missing package inspection input");

            var malformedPath = Path.Combine(root, "malformed.nupkg");
            File.WriteAllText(malformedPath, "not a package", new UTF8Encoding(false));
            AssertPackageInspectionFailure(
                ["package", "inspect", "--nupkg", malformedPath, "--output", Path.Combine(root, "malformed.json")],
                ExitCodes.ValidationFailure,
                Path.Combine(root, "malformed.json"),
                "malformed package inspection input");
            var linkedPath = Path.Combine(root, "linked-output.json");
            if (TryCreateFileSymlink(linkedPath, existingPath))
            {
                AssertPackageInspectionFailure(
                    ["package", "inspect", "--nupkg", packagePath, "--output", linkedPath],
                    ExitCodes.ValidationFailure,
                    linkedPath,
                    "symbolic-link package inspection output",
                    requireAbsentOutput: false);
                AssertBytes(existingBytes, File.ReadAllBytes(existingPath), "symlink target remains unchanged");
            }
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static void TestKnownAnswersAndCanonicalBytes()
    {
        var assessmentBytes = Encoding.UTF8.GetBytes(KnownAssessmentJson);
        var ledgerBytes = Encoding.UTF8.GetBytes(KnownLedgerJson);
        var assessment = CanonicalEvidenceJson.ParseAssessment(assessmentBytes);
        var ledger = CanonicalEvidenceJson.ParseSourceLedger(ledgerBytes);

        AssertBytes(
            assessmentBytes,
            CanonicalEvidenceJson.SerializeAssessment(assessment),
            "known assessment canonical bytes");
        AssertBytes(
            ledgerBytes,
            CanonicalEvidenceJson.SerializeSourceLedger(ledger),
            "known source-ledger canonical bytes");
        AssertEqual(KnownEvidenceId, ledger.Records.Single().StableId, "known evidence ID");
        AssertEqual(
            KnownLedgerSha256,
            CanonicalEvidenceJson.ComputeSourceLedgerSha256(ledger),
            "known source-ledger digest");

        var rebuilt = EvidenceLedgerBuilder.BuildRepositoryLedger(
            ledger.RepositorySubject!,
            ledger.Records.Select(ToDraft));
        AssertBytes(
            ledgerBytes,
            CanonicalEvidenceJson.SerializeSourceLedger(rebuilt),
            "known ledger rebuild");

        ExpectValidation(
            () => CanonicalEvidenceJson.ParseSourceLedger(
                Encoding.UTF8.GetBytes(KnownLedgerJson + "\n")),
            "noncanonical trailing newline");
        ExpectValidation(
            () => CanonicalEvidenceJson.ParseAssessment(
                Encoding.UTF8.GetBytes(
                    KnownAssessmentJson.Replace(
                        "\"package\":",
                        "\"component_id\":\"Tree\",\"package\":",
                        StringComparison.Ordinal))),
            "property order and duplication");

        var forgedId = ledger with
        {
            Records =
            [
                ledger.Records[0] with
                {
                    StableId = "EV1-" + new string('f', 64)
                }
            ]
        };
        ExpectValidation(
            () => EvidenceLedgerValidator.ValidateSourceLedger(forgedId),
            "forged evidence ID");
    }

    private static void TestPublicProvenanceKinds()
    {
        var cases = new[]
        {
            (EvidenceIdentity.VendorPublicDocumentation, "https://docs.example.com/widgets"),
            (EvidenceIdentity.PackageArtifactMetadata, "package:entry/Sample.Widgets.nuspec"),
            (EvidenceIdentity.VendorSourceRepository, "source:src/Tree.cs"),
            (EvidenceIdentity.ReproducedRuntimeObservation, "dotnet test Tree.Keyboard"),
            (EvidenceIdentity.OwnerSuppliedInternalEvidence, "accessibility-audit.pdf"),
            (EvidenceIdentity.OwnerSuppliedPublicEvidence, "public-conformance.json"),
            (EvidenceIdentity.OwnerDeclaredClosedSource, "source"),
            (EvidenceIdentity.OwnerDeclaredUnavailable, "artifact:private-sbom")
        };

        foreach (var (kind, locator) in cases)
        {
            var ledger = EvidenceLedgerBuilder.BuildRepositoryLedger(
                KnownRepositorySubject(),
                [RepositoryDraft(kind, locator, $"Evidence kind {kind} is explicit.")]);
            AssertEqual(kind, ledger.Records.Single().Provenance.Kind, $"provenance kind {kind}");
        }

        ExpectValidation(
            () => EvidenceLedgerBuilder.BuildRepositoryLedger(
                KnownRepositorySubject(),
                [RepositoryDraft("closed-source", "source", "Source was not available.")]),
            "closed-source cannot be inferred through an unknown kind");
        ExpectValidation(
            () => EvidenceLedgerBuilder.BuildRepositoryLedger(
                KnownRepositorySubject(),
                [RepositoryDraft(
                    EvidenceIdentity.VendorSourceRepository,
                    "https://missing.example.com/source",
                    "A failed lookup does not imply closed source.")]),
            "retrieval failure cannot become closed-source provenance");
        ExpectValidation(
            () => EvidenceLedgerBuilder.BuildRepositoryLedger(
                KnownRepositorySubject(),
                [RepositoryDraft(
                    EvidenceIdentity.OwnerDeclaredUnavailable,
                    "source",
                    "Generic unavailability is distinct from closed source.")]),
            "generic unavailable cannot declare closed source");
    }

    private static void TestScopeAndExactIdentity()
    {
        var assessment = KnownAssessment();
        var repositoryDraft = RepositoryDraft(
            EvidenceIdentity.PackageArtifactMetadata,
            "package:entry/Sample.Widgets.nuspec",
            "Package metadata declares the license.");
        var componentDraft = ComponentDraft("Tree");

        ExpectValidation(
            () => EvidenceLedgerBuilder.BuildRepositoryLedger(
                KnownRepositorySubject(),
                [componentDraft]),
            "component evidence in repository ledger");
        ExpectValidation(
            () => EvidenceLedgerBuilder.BuildComponentLedger(
                assessment,
                [repositoryDraft]),
            "repository evidence in component ledger");
        ExpectValidation(
            () => EvidenceLedgerBuilder.BuildComponentLedger(
                assessment,
                [ComponentDraft("Grid")]),
            "component scope mismatch");

        var repositoryLedger = EvidenceLedgerBuilder.BuildRepositoryLedger(
            KnownRepositorySubject(),
            [repositoryDraft]);
        var componentLedger = EvidenceLedgerBuilder.BuildComponentLedger(
            assessment,
            [componentDraft]);
        AssertEqual(
            2,
            EvidenceLedgerBuilder.BuildBundle(
                assessment,
                [repositoryLedger, componentLedger],
                [repositoryLedger.Records[0].StableId, componentLedger.Records[0].StableId])
                .Selection.Count,
            "compatible package/component bundle");

        foreach (var mismatch in new[]
        {
            assessment with
            {
                Package = assessment.Package with { PackageId = "other.widgets" }
            },
            assessment with
            {
                Package = assessment.Package with { Version = "1.2.4" }
            },
            assessment with
            {
                Package = assessment.Package with
                {
                    NupkgDigest = new Sha256Digest("sha256", new string('c', 64))
                }
            }
        })
        {
            ExpectValidation(
                () => EvidenceLedgerBuilder.BuildBundle(
                    mismatch,
                    [repositoryLedger],
                    [repositoryLedger.Records[0].StableId]),
                "exact package mismatch");
        }

        ExpectValidation(
            () => EvidenceLedgerBuilder.BuildBundle(
                assessment with { ComponentId = "Grid" },
                [componentLedger],
                [componentLedger.Records[0].StableId]),
            "exact component mismatch");
    }

    private static void TestHostNeutralSourceMapping()
    {
        var locators = new[]
        {
            "source:src/Tree.cs",
            "source:components/Grid.razor",
            "source:eng/release/provenance.json"
        };

        foreach (var locator in locators)
        {
            var ledger = EvidenceLedgerBuilder.BuildRepositoryLedger(
                KnownRepositorySubject(),
                [RepositoryDraft(
                    EvidenceIdentity.VendorSourceRepository,
                    locator,
                    "Captured vendor source at an exact revision.")]);
            var canonical = ledger.Records.Single().Provenance.Locator;
            Assert(
                canonical.StartsWith("source:", StringComparison.Ordinal),
                "source mapping is manifest-relative");
            Assert(
                !canonical.Contains("://", StringComparison.Ordinal),
                "source mapping does not duplicate a repository host");
        }

        ExpectValidation(
            () => EvidenceLedgerBuilder.BuildRepositoryLedger(
                KnownRepositorySubject(),
                [RepositoryDraft(
                    EvidenceIdentity.VendorSourceRepository,
                    "source:../Tree.cs",
                    "Unsafe source mapping.")]),
            "source mapping path traversal");
    }

    private static void TestBundleSelectionAndAmbiguity()
    {
        var first = RepositoryDraft(
            EvidenceIdentity.VendorPublicDocumentation,
            "https://docs.example.com/first",
            "First public claim.");
        var second = RepositoryDraft(
            EvidenceIdentity.VendorPublicDocumentation,
            "https://docs.example.com/second",
            "Second public claim.") with
        {
            Provenance = RepositoryDraft(
                EvidenceIdentity.VendorPublicDocumentation,
                "https://docs.example.com/second",
                "Second public claim.").Provenance with
            {
                ContentDigest = new Sha256Digest("sha256", new string('c', 64))
            }
        };
        var ledger = EvidenceLedgerBuilder.BuildRepositoryLedger(
            KnownRepositorySubject(),
            [first, first, second]);
        AssertEqual(2, ledger.Records.Count, "identical evidence deduplication");

        var selectedIds = ledger.Records
            .Select(record => record.StableId)
            .Reverse()
            .Take(1)
            .ToArray();
        var bundle = EvidenceLedgerBuilder.BuildBundle(
            KnownAssessment(),
            [ledger],
            selectedIds);
        AssertEqual(selectedIds[0], bundle.Selection[0].EvidenceId, "selection order");
        Assert(
            ledger.Records.Any(record => !selectedIds.Contains(record.StableId, StringComparer.Ordinal)),
            "bundle source can contain an unselected record");
        AssertEqual(1, bundle.Selection.Count, "only explicitly selected evidence is referenced");

        ExpectValidation(
            () => EvidenceLedgerBuilder.BuildBundle(
                KnownAssessment(),
                [ledger],
                [selectedIds[0], selectedIds[0]]),
            "duplicate selection");

        var overlapA = EvidenceLedgerBuilder.BuildRepositoryLedger(
            KnownRepositorySubject(),
            [first]);
        var overlapB = EvidenceLedgerBuilder.BuildRepositoryLedger(
            KnownRepositorySubject(),
            [first, second]);
        ExpectValidation(
            () => EvidenceLedgerBuilder.BuildBundle(
                KnownAssessment(),
                [overlapA, overlapB],
                [overlapA.Records[0].StableId]),
            "ambiguous source-ledger membership");

        var forgedDigest = bundle with
        {
            SourceLedgers =
            [
                bundle.SourceLedgers[0] with
                {
                    SourceLedgerSha256 = new string('0', 64)
                }
            ]
        };
        ExpectValidation(
            () => EvidenceLedgerValidator.ValidateBundle(forgedDigest),
            "forged source-ledger digest");
    }

    private static void TestSupersession()
    {
        var originalDraft = RepositoryDraft(
            EvidenceIdentity.PackageArtifactMetadata,
            "package:entry/Sample.Widgets.nuspec",
            "Original package declaration.");
        var original = EvidenceLedgerBuilder.BuildRepositoryLedger(
            KnownRepositorySubject(),
            [originalDraft]).Records.Single();
        var successorDraft = originalDraft with
        {
            Claim = "Replacement package declaration.",
            Provenance = originalDraft.Provenance with
            {
                CapturedAtUtc = "2026-09-02T21:00:00Z",
                ContentDigest = new Sha256Digest("sha256", new string('d', 64))
            },
            Supersedes = [original.StableId]
        };
        var ledger = EvidenceLedgerBuilder.BuildRepositoryLedger(
            KnownRepositorySubject(),
            [originalDraft, successorDraft]);
        var successor = ledger.Records.Single(record => record.Supersedes.Count == 1);

        ExpectValidation(
            () => EvidenceLedgerBuilder.BuildBundle(
                KnownAssessment(),
                [ledger],
                [original.StableId, successor.StableId]),
            "superseded ancestor selection");
        AssertEqual(
            successor.StableId,
            EvidenceLedgerBuilder.BuildBundle(
                KnownAssessment(),
                [ledger],
                [successor.StableId]).Selection.Single().EvidenceId,
            "successor-only selection");

        const string firstId =
            "EV1-1111111111111111111111111111111111111111111111111111111111111111";
        const string secondId =
            "EV1-2222222222222222222222222222222222222222222222222222222222222222";
        var cycle = new EvidenceSourceLedger(
            1,
            "repository",
            KnownRepositorySubject(),
            null,
            [
                new EvidenceRecord(
                    firstId,
                    originalDraft.Claim,
                    originalDraft.Applicability,
                    originalDraft.Provenance,
                    [secondId]),
                new EvidenceRecord(
                    secondId,
                    successorDraft.Claim,
                    successorDraft.Applicability,
                    successorDraft.Provenance,
                    [firstId])
            ]);
        Assert(
            CaptureValidation(
                () => EvidenceLedgerValidator.ValidateSourceLedger(cycle))
                .Message.Contains("cycle", StringComparison.Ordinal),
            "supersession cycle is diagnosed before forged IDs");
    }

    private static void TestPackageArtifactVerification(string repositoryRoot)
    {
        var root = Path.Combine(
            Environment.GetEnvironmentVariable("READINESS_TEST_ARTIFACTS") ?? Path.Combine(repositoryRoot, "artifacts"),
            "readiness-package-evidence-tests");
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }

        Directory.CreateDirectory(root);
        try
        {
            var packagePath = Path.Combine(root, "Artifact.Test.1.0.0.nupkg");
            CreatePackage(
                packagePath,
                "Artifact.Test",
                "1.0",
                new Dictionary<string, byte[]>
                {
                    ["README.md"] = Encoding.UTF8.GetBytes("# Artifact Test\n")
                });
            var inspected = NupkgInspector.Inspect(packagePath);
            var package = EvidenceIdentity.FromInspectedPackage(inspected);
            var manifestDigest = new Sha256Digest("sha256", new string('9', 64));
            var subject = new RepositoryLedgerSubject(
                "unified",
                package,
                manifestDigest,
                "Tree");
            var subjectPath = Path.Combine(root, "subject.json");
            File.WriteAllBytes(
                subjectPath,
                CanonicalEvidenceJson.SerializeRepositorySubject(subject));

            var cases = new[]
            {
                (
                    NupkgInspector.WholePackageEvidenceLocator,
                    inspected.NupkgSha256,
                    "whole-nupkg"),
                (
                    NupkgInspector.PackageEntryEvidencePrefix + inspected.NuspecEntry,
                    NupkgInspector.ComputeEvidenceContentSha256(
                        packagePath,
                        NupkgInspector.PackageEntryEvidencePrefix + inspected.NuspecEntry),
                    "nuspec"),
                (
                    NupkgInspector.PackageEntryEvidencePrefix + "README.md",
                    Convert.ToHexStringLower(
                        System.Security.Cryptography.SHA256.HashData(
                            Encoding.UTF8.GetBytes("# Artifact Test\n"))),
                    "readme")
            };
            foreach (var (locator, digest, name) in cases)
            {
                var draftPath = Path.Combine(root, $"{name}.draft.json");
                File.WriteAllText(
                    draftPath,
                    DraftJson(PackageArtifactDraft(locator, digest)),
                    new UTF8Encoding(false));
                var output = new StringWriter();
                var error = new StringWriter();
                AssertEqual(
                    ExitCodes.Success,
                    CliApplication.Run(
                        [
                            "evidence",
                            "ledger-build",
                            "--kind",
                            "repository",
                            "--subject",
                            subjectPath,
                            "--draft",
                            draftPath,
                            "--nupkg",
                            packagePath,
                            "--output",
                            Path.Combine(root, $"{name}.ledger.json")
                        ],
                        output,
                        error),
                    $"verified package evidence {name}");
            }

            var helpOutput = new StringWriter();
            AssertEqual(ExitCodes.Success,
                CliApplication.Run(["evidence", "ledger-build", "--help"], helpOutput, new StringWriter()),
                "ledger draft authoring help");
            var help = helpOutput.ToString();
            var marker = help.IndexOf("```json", StringComparison.Ordinal);
            Assert(marker >= 0, "help exposes a complete JSON draft template");
            var templateStart = marker + "```json".Length;
            var templateEnd = help.IndexOf("```", templateStart, StringComparison.Ordinal);
            Assert(templateEnd > templateStart, "help draft template is delimited");
            var template = help[templateStart..templateEnd].Trim()
                .Replace("<observed claim>", "Fixture package bytes were inspected.", StringComparison.Ordinal)
                .Replace("<actual capture method>", "Inspected retained fixture package bytes.", StringComparison.Ordinal)
                .Replace("<actual capture UTC time>", "2026-09-02T20:00:00Z", StringComparison.Ordinal)
                .Replace("<observed content SHA-256>", inspected.NupkgSha256, StringComparison.Ordinal);
            Assert(help.Contains(NupkgInspector.PackageEntryEvidencePrefix, StringComparison.Ordinal),
                "help exposes the existing package-entry locator grammar");
            var packageSubjectPath = Path.Combine(root, "help-package-subject.json");
            File.WriteAllBytes(packageSubjectPath, CanonicalEvidenceJson.SerializeRepositorySubject(
                subject with { AssessmentKind = "package", ComponentId = null }));
            var templatePath = Path.Combine(root, "help-template.draft.json");
            File.WriteAllText(templatePath, template, new UTF8Encoding(false));
            var templateLedger = Path.Combine(root, "help-template.ledger.json");
            var templateError = new StringWriter();
            AssertEqual(ExitCodes.Success,
                CliApplication.Run(
                    ["evidence", "ledger-build", "--kind", "repository", "--subject", packageSubjectPath,
                        "--draft", templatePath, "--nupkg", packagePath, "--output", templateLedger],
                    new StringWriter(), templateError),
                $"help template builds a package-only ledger with observed fixture facts: {templateError}");
            AssertEqual(ExitCodes.Success,
                CliApplication.Run(["evidence", "ledger-validate", "--ledger", templateLedger],
                    new StringWriter(), new StringWriter()),
                "help template produces a canonical ledger");

            var aliasIndex = 0;
            foreach (var alias in new[] { "nupkg", "package:nupkg", "Artifact.Test.1.0.0.nupkg" })
            {
                var aliasPath = Path.Combine(root, $"alias-{aliasIndex}.draft.json");
                var aliasLedger = Path.Combine(root, $"alias-{aliasIndex++}.ledger.json");
                File.WriteAllText(aliasPath,
                    template.Replace(NupkgInspector.WholePackageEvidenceLocator, alias, StringComparison.Ordinal),
                    new UTF8Encoding(false));
                var originalAliasBytes = File.ReadAllBytes(aliasPath);
                var aliasError = new StringWriter();
                AssertEqual(ExitCodes.ValidationFailure,
                    CliApplication.Run(
                        ["evidence", "ledger-build", "--kind", "repository", "--subject", packageSubjectPath,
                            "--draft", aliasPath, "--nupkg", packagePath, "--output", aliasLedger],
                        new StringWriter(), aliasError),
                    $"package locator alias {alias} remains invalid");
                Assert(!File.Exists(aliasLedger), "invalid locator does not create a ledger");
                Assert(originalAliasBytes.SequenceEqual(File.ReadAllBytes(aliasPath)),
                    "invalid draft is not repaired in place");
                Assert(aliasError.ToString().Contains(NupkgInspector.WholePackageEvidenceLocator, StringComparison.Ordinal),
                    "invalid-locator error exposes the whole-package grammar");
                Assert(aliasError.ToString().Contains(NupkgInspector.PackageEntryEvidencePrefix, StringComparison.Ordinal),
                    "invalid-locator error exposes the entry grammar");
            }

            var missingDraft = Path.Combine(root, "missing.draft.json");
            File.WriteAllText(
                missingDraft,
                DraftJson(PackageArtifactDraft(
                    NupkgInspector.PackageEntryEvidencePrefix + "missing.txt",
                    new string('0', 64))),
                new UTF8Encoding(false));
            AssertCliValidationFailure(
                [
                    "evidence",
                    "ledger-build",
                    "--kind",
                    "repository",
                    "--subject",
                    subjectPath,
                    "--draft",
                    missingDraft,
                    "--nupkg",
                    packagePath,
                    "--output",
                    Path.Combine(root, "missing.ledger.json")
                ],
                "resolves to 0 entries",
                "missing package evidence entry");

            var wrongDigestDraft = Path.Combine(root, "wrong-digest.draft.json");
            File.WriteAllText(
                wrongDigestDraft,
                DraftJson(PackageArtifactDraft(
                    NupkgInspector.PackageEntryEvidencePrefix + "README.md",
                    new string('0', 64))),
                new UTF8Encoding(false));
            AssertCliValidationFailure(
                [
                    "evidence",
                    "ledger-build",
                    "--kind",
                    "repository",
                    "--subject",
                    subjectPath,
                    "--draft",
                    wrongDigestDraft,
                    "--nupkg",
                    packagePath,
                    "--output",
                    Path.Combine(root, "wrong-digest.ledger.json")
                ],
                "differs from the exact nupkg content",
                "wrong package evidence digest");

            ExpectValidation(
                () => EvidenceLedgerBuilder.BuildRepositoryLedger(
                    subject,
                    [PackageArtifactDraft("package:entry/../escape", new string('0', 64))]),
                "unsafe package evidence entry locator");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static void TestResourceCeilings()
    {
        EvidenceCommand.ValidateSupplementalInputs(
            Enumerable.Repeat(0L, ResourceLimits.SupplementalInputCount).ToArray());
        ExpectValidation(
            () => EvidenceCommand.ValidateSupplementalInputs(
                Enumerable.Repeat(0L, ResourceLimits.SupplementalInputCount + 1).ToArray()),
            "33 supplemental inputs");
        EvidenceCommand.ValidateSupplementalInputs(
            [ResourceLimits.SupplementalInputAggregateBytes]);
        ExpectValidation(
            () => EvidenceCommand.ValidateSupplementalInputs(
                [ResourceLimits.SupplementalInputAggregateBytes, 1]),
            "supplemental aggregate one byte over");
        BoundedIO.EnsureLength(
            ResourceLimits.AuthoredLedgerBytes,
            ResourceLimits.AuthoredLedgerBytes,
            "authored source ledger");
        ExpectValidation(
            () => BoundedIO.EnsureLength(
                ResourceLimits.AuthoredLedgerBytes + 1,
                ResourceLimits.AuthoredLedgerBytes,
                "authored source ledger"),
            "authored source ledger one byte over");
        EvidenceCommand.EnsureSerializedOutput(ResourceLimits.SerializedArtifactBytes);
        ExpectValidation(
            () => EvidenceCommand.EnsureSerializedOutput(
                ResourceLimits.SerializedArtifactBytes + 1),
            "serialized evidence output one byte over");
    }

    private static void TestCliSubprocesses(string repositoryRoot, string pluginRoot)
    {
        var root = Path.Combine(
            Environment.GetEnvironmentVariable("READINESS_TEST_ARTIFACTS") ?? Path.Combine(repositoryRoot, "artifacts"),
            "readiness-evidence-cli-tests");
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }

        Directory.CreateDirectory(root);
        try
        {
            var packagePath = Path.Combine(root, "Sample.Widgets.1.2.3.nupkg");
            CreatePackage(packagePath, "Sample.Widgets", "1.2.3");
            var inspected = EvidenceIdentity.FromInspectedPackage(
                NupkgInspector.Inspect(packagePath));
            var inputManifestDigest = new Sha256Digest("sha256", new string('7', 64));
            var subject = new RepositoryLedgerSubject(
                "unified",
                inspected,
                inputManifestDigest,
                "Tree");
            var assessment = new ExactAssessmentIdentity(
                "unified",
                inspected,
                inputManifestDigest,
                "Tree");
            var subjectPath = Path.Combine(root, "repository-subject.json");
            var assessmentPath = Path.Combine(root, "assessment.json");
            var repositoryDraftPath = Path.Combine(root, "repository-draft.json");
            var componentDraftPath = Path.Combine(root, "component-draft.json");
            var repositoryLedgerPath = Path.Combine(root, "repository-ledger.json");
            var componentLedgerPath = Path.Combine(root, "component-ledger.json");
            var bundlePath = Path.Combine(root, "bundle.json");

            File.WriteAllBytes(
                subjectPath,
                CanonicalEvidenceJson.SerializeRepositorySubject(subject));
            File.WriteAllBytes(
                assessmentPath,
                CanonicalEvidenceJson.SerializeAssessment(assessment));
            File.WriteAllText(
                repositoryDraftPath,
                DraftJson(RepositoryDraft(
                    EvidenceIdentity.OwnerDeclaredClosedSource,
                    "source",
                    "The package owner declared the package closed source.")),
                new UTF8Encoding(false));
            File.WriteAllText(
                componentDraftPath,
                DraftJson(ComponentDraft("Tree")),
                new UTF8Encoding(false));

            var validatorDirectory = Path.Combine(
                pluginRoot,
                "skills",
                "blazor-component-readiness",
                "scripts",
                "validator");
            var buildRoot = Path.Combine(root, "validator-build");
            var environment = new Dictionary<string, string>
            {
                ["READINESS_TEMP"] = buildRoot
            };
            RunProcess(
                "bash",
                [
                    Path.Combine(validatorDirectory, "run-validator.sh"),
                    "evidence",
                    "ledger-build",
                    "--kind",
                    "repository",
                    "--subject",
                    subjectPath,
                    "--draft",
                    repositoryDraftPath,
                    "--nupkg",
                    packagePath,
                    "--output",
                    repositoryLedgerPath
                ],
                repositoryRoot,
                environment,
                expectedExitCode: 0);

            var validatorDll = Path.Combine(
                buildRoot,
                "bin",
                "Release",
                "net11.0",
                "BlazorComponentReadiness.Validator.dll");
            var inspectionPath = Path.Combine(root, "package-inspection.json");
            var launcherInspection = RunProcess(
                "bash",
                [
                    Path.Combine(validatorDirectory, "run-validator.sh"),
                    "package",
                    "inspect",
                    "--nupkg",
                    packagePath,
                    "--output",
                    inspectionPath
                ],
                repositoryRoot,
                environment,
                expectedExitCode: ExitCodes.Success);
            Assert(
                !launcherInspection.StandardOutput.Contains("\"package_id\"", StringComparison.Ordinal),
                "launcher package inspection does not duplicate JSON on mixed stdout");
            using (var launcherInspectionJson = StrictJson.Parse(
                       File.ReadAllBytes(inspectionPath).AsSpan(..^Environment.NewLine.Length).ToArray(),
                       4096,
                       "launcher persisted package inspection"))
            {
                AssertEqual(
                    "sample.widgets",
                    launcherInspectionJson.RootElement.GetProperty("package_id").GetString(),
                    "launcher persisted package inspection identity");
            }
            RunProcess(
                "dotnet",
                [
                    validatorDll,
                    "evidence",
                    "ledger-validate",
                    "--ledger",
                    repositoryLedgerPath
                ],
                repositoryRoot,
                environment,
                expectedExitCode: 0);
            RunProcess(
                "dotnet",
                [
                    validatorDll,
                    "evidence",
                    "ledger-build",
                    "--kind",
                    "component",
                    "--subject",
                    assessmentPath,
                    "--draft",
                    componentDraftPath,
                    "--nupkg",
                    packagePath,
                    "--output",
                    componentLedgerPath
                ],
                repositoryRoot,
                environment,
                expectedExitCode: 0);

            var repositoryLedger = CanonicalEvidenceJson.ParseSourceLedger(
                File.ReadAllBytes(repositoryLedgerPath));
            var componentLedger = CanonicalEvidenceJson.ParseSourceLedger(
                File.ReadAllBytes(componentLedgerPath));
            var selectionDraftPath = Path.Combine(root, "selection-draft.json");
            var selectionLedgerPath = Path.Combine(root, "selection-ledger.json");
            File.WriteAllText(
                selectionDraftPath,
                DraftJson(
                [
                    RepositoryDraft(
                        EvidenceIdentity.OwnerDeclaredClosedSource,
                        "source",
                        "The package owner declared the package closed source."),
                    RepositoryDraft(
                        EvidenceIdentity.VendorPublicDocumentation,
                        "https://docs.example.com/widgets/keyboard",
                        "Vendor documentation states keyboard support."),
                    RepositoryDraft(
                        EvidenceIdentity.ReproducedRuntimeObservation,
                        "probe://sample-widgets/keyboard",
                        "The bounded keyboard probe completed successfully.")
                ]),
                new UTF8Encoding(false));
            RunProcess(
                "dotnet",
                [
                    validatorDll,
                    "evidence",
                    "ledger-build",
                    "--kind",
                    "repository",
                    "--subject",
                    subjectPath,
                    "--draft",
                    selectionDraftPath,
                    "--nupkg",
                    packagePath,
                    "--output",
                    selectionLedgerPath
                ],
                repositoryRoot,
                environment,
                expectedExitCode: 0);
            var selectionLedgerBytes = File.ReadAllBytes(selectionLedgerPath);
            var selectionLedger = CanonicalEvidenceJson.ParseSourceLedger(selectionLedgerBytes);
            var listingPath = Path.Combine(root, "disposable ledger listing.tsv");
            var inputCandidates = File.ReadAllText(Path.Combine(
                    pluginRoot,
                    "skills",
                    "blazor-component-readiness",
                    "references",
                    "input-candidates.md"));
            var projectionScript = ExtractBashBlock(inputCandidates, "### Passing selected evidence IDs");
            var selectionScript = ExtractBashBlock(inputCandidates, "following consumes the assessor");
            if (OperatingSystem.IsWindows())
            {
                Console.WriteLine("Skipping optional Bash/jq ledger handoff on Windows.");
            }
            else
            {
                var jqAvailable = RunProcess(
                    "/bin/bash", ["-c", "command -v jq"], repositoryRoot, environment, expectedExitCode: null);
                Assert(jqAvailable.ExitCode is 0 or 1, $"jq discovery failed: {jqAvailable.StandardError}");
                if (jqAvailable.ExitCode == 1)
                {
                    Console.WriteLine("Skipping optional ledger handoff: jq is unavailable.");
                }
                else
                {
                    var documentedScript = $"""
                        VALIDATOR="$6"
                        readiness() ( dotnet "$VALIDATOR" "$@" )
                        {projectionScript
                            .Replace("LEDGER=\"<retained-ledger.json>\"", "LEDGER=\"$1\"", StringComparison.Ordinal)
                            .Replace("LISTING=\"<disposable-ledger-listing.tsv>\"", "LISTING=\"$2\"", StringComparison.Ordinal)}
                        {selectionScript
                            .Replace("\"<assessment-draft.json>\"", "\"$3\"", StringComparison.Ordinal)
                            .Replace("\"<assessment-with-selected-ids.json>\"", "\"$4\"", StringComparison.Ordinal)
                            .Replace("\"<assessment-identity.json>\"", "\"$7\"", StringComparison.Ordinal)
                            .Replace("\"<new-evidence.json>\"", "\"$8\"", StringComparison.Ordinal)
                            .Replace("\"<requirement-id>\"", "\"LP-02\"", StringComparison.Ordinal)}
                        printf '%s' "$IDS_CSV" > "$5"
                        """;
                    var rowDraftPath = Path.Combine(root, "assessment draft with selected IDs.json");
                    var rowInputPath = Path.Combine(root, "assessment draft.json");
                    var rowDraftBytes = Encoding.UTF8.GetBytes(
                        """{"rows":[{"id":"LP-01","observation":"Unchanged first row.","evidence_ids":[]},{"id":"LP-02","observation":"Unchanged selected row.","evidence_ids":[]}]}""");
                    File.WriteAllBytes(rowInputPath, rowDraftBytes);
                    var idsPath = Path.Combine(root, "selected IDs.csv");
                    var selectedBundlePath = Path.Combine(root, "selected.bundle.json");
                    var projection = RunProcess(
                        "bash",
                        [
                            "-c",
                            documentedScript,
                            "ledger-id-projection",
                            selectionLedgerPath,
                            listingPath,
                            rowInputPath,
                            rowDraftPath,
                            idsPath,
                            validatorDll,
                            assessmentPath,
                            selectedBundlePath
                        ],
                        repositoryRoot,
                        environment,
                        expectedExitCode: 0);
                    Assert(projection.StandardError.Length == 0, "ledger ID projection has no stderr");
                    AssertBytes(selectionLedgerBytes, File.ReadAllBytes(selectionLedgerPath), "ledger projection preserves source bytes");
                    AssertBytes(rowDraftBytes, File.ReadAllBytes(rowInputPath), "handoff preserves input draft");
                    var listingLines = File.ReadAllLines(listingPath);
                    AssertEqual(selectionLedger.Records.Count, listingLines.Length, "ledger projection record count");
                    Assert(
                        listingLines.Select(line => int.Parse(line.Split('\t')[0]))
                            .SequenceEqual(Enumerable.Range(0, selectionLedger.Records.Count)),
                        "ledger projection exposes zero-based indexes");
                    Assert(
                        listingLines.Select(line => line.Split('\t')[1])
                            .SequenceEqual(selectionLedger.Records.Select(record => record.StableId)),
                        "ledger projection preserves stable ID order");
                    Assert(
                        listingLines.Select(line => line.Split('\t')[2])
                            .SequenceEqual(selectionLedger.Records.Select(record => record.Claim)),
                        "ledger projection preserves stable ID claims");
                    Assert(
                        listingLines.Select(line => line.Split('\t')[4])
                            .SequenceEqual(selectionLedger.Records.Select(record => record.Provenance.Locator)),
                        "ledger projection exposes exact provenance locators");

                    var selectedIds = File.ReadAllText(idsPath).Split(',', StringSplitOptions.RemoveEmptyEntries);
                    AssertSequence(
                        [selectionLedger.Records[1].StableId, selectionLedger.Records[0].StableId],
                        selectedIds,
                        "documented selection preserves explicit reverse order");
                    using (var rowDraft = JsonDocument.Parse(File.ReadAllBytes(rowDraftPath)))
                    {
                        AssertSequence(
                            selectedIds,
                            rowDraft.RootElement.GetProperty("rows")[1].GetProperty("evidence_ids")
                                .EnumerateArray()
                                .Select(value => value.GetString()),
                            "documented selection reaches row references");
                        AssertEqual(0, rowDraft.RootElement.GetProperty("rows")[0].GetProperty("evidence_ids").GetArrayLength(),
                            "explicit requirement selection does not modify first row");
                        AssertEqual("Unchanged selected row.", rowDraft.RootElement.GetProperty("rows")[1].GetProperty("observation").GetString(),
                            "selection preserves the assessor's observation");
                    }
                    var selectedBundle = CanonicalEvidenceJson.ParseBundle(File.ReadAllBytes(selectedBundlePath));
                    AssertSequence(selectedIds, selectedBundle.Selection.Select(selection => selection.EvidenceId), "projected explicit ID selection order");
                    Assert(
                        !selectedBundle.Selection.Any(selection =>
                            selection.EvidenceId == selectionLedger.Records[2].StableId),
                        "projected selection leaves an unselected ledger record");
                    var truncatedBundlePath = Path.Combine(root, "truncated.bundle.json");
                    RunProcess(
                        "dotnet",
                        [
                            validatorDll,
                            "evidence",
                            "bundle",
                            "--assessment",
                            assessmentPath,
                            "--source-ledger",
                            selectionLedgerPath,
                            "--ids",
                            selectedIds[0][..^1],
                            "--output",
                            truncatedBundlePath
                        ],
                        repositoryRoot,
                        environment,
                        expectedExitCode: ExitCodes.ValidationFailure);
                    Assert(!File.Exists(truncatedBundlePath), "truncated projected ID writes no bundle");

                    var listingBytes = File.ReadAllBytes(listingPath);
                    var collision = RunProcess(
                        "bash",
                        [
                            "-c",
                            documentedScript,
                            "ledger-id-projection",
                            selectionLedgerPath,
                            listingPath,
                            rowInputPath,
                            Path.Combine(root, "collision row draft.json"),
                            Path.Combine(root, "collision IDs.csv"),
                            validatorDll,
                            assessmentPath,
                            Path.Combine(root, "collision.bundle.json")
                        ],
                        repositoryRoot,
                        environment,
                        expectedExitCode: 1);
                    Assert(
                        collision.StandardError.Contains("cannot overwrite", StringComparison.OrdinalIgnoreCase),
                        "documented projection protects existing listing");
                    AssertBytes(
                        listingBytes,
                        File.ReadAllBytes(listingPath),
                        "existing listing remains unchanged");

                    foreach (var indexes in new[] { "[]", "[1,1]", "[-1]", "[3]", "[0.5]", "[\"1\"]", "null" })
                    {
                        var prefix = Path.Combine(root, $"invalid-index-{Guid.NewGuid():N}");
                        var invalidScript = documentedScript.Replace("INDEXES_JSON='[1,0]'", $"INDEXES_JSON='{indexes}'", StringComparison.Ordinal);
                        var invalid = RunProcess(
                            "bash",
                            ["-c", invalidScript, "invalid-selection", selectionLedgerPath, prefix + ".tsv",
                                rowInputPath, prefix + ".row.json", prefix + ".csv", validatorDll,
                                assessmentPath, prefix + ".bundle.json"],
                            repositoryRoot, environment, expectedExitCode: 5);
                        Assert(invalid.StandardError.Contains("index", StringComparison.Ordinal),
                            "invalid selection fails in the documented selector");
                        Assert(!File.Exists(prefix + ".row.json") && !File.Exists(prefix + ".bundle.json"),
                            "invalid selection writes no row draft or bundle");
                    }

                    foreach (var rows in new[]
                    {
                        """{"rows":[{"id":"LP-01","evidence_ids":[]}]}""",
                        """{"rows":[{"id":"LP-02","evidence_ids":[]},{"id":"LP-02","evidence_ids":[]}]}"""
                    })
                    {
                        var prefix = Path.Combine(root, $"invalid-row-{Guid.NewGuid():N}");
                        File.WriteAllText(prefix + ".input.json", rows);
                        var invalid = RunProcess(
                            "bash",
                            ["-c", documentedScript, "invalid-row", selectionLedgerPath, prefix + ".tsv",
                                prefix + ".input.json", prefix + ".row.json", prefix + ".csv", validatorDll,
                                assessmentPath, prefix + ".bundle.json"],
                            repositoryRoot, environment, expectedExitCode: 5);
                        Assert(invalid.StandardError.Contains("requirement must resolve exactly once", StringComparison.Ordinal),
                            "missing or duplicate requirement fails explicitly");
                        Assert(!File.Exists(prefix + ".bundle.json"), "invalid requirement writes no bundle");
                    }
                    AssertBytes(selectionLedgerBytes, File.ReadAllBytes(selectionLedgerPath), "negative handoffs preserve ledger");
                }
            }
            var selected = string.Join(
                ',',
                repositoryLedger.Records[0].StableId,
                componentLedger.Records[0].StableId);
            RunProcess(
                "dotnet",
                [
                    validatorDll,
                    "evidence",
                    "bundle",
                    "--assessment",
                    assessmentPath,
                    "--source-ledger",
                    componentLedgerPath,
                    "--source-ledger",
                    repositoryLedgerPath,
                    "--ids",
                    selected,
                    "--output",
                    bundlePath
                ],
                repositoryRoot,
                environment,
                expectedExitCode: 0);
            var bundle = CanonicalEvidenceJson.ParseBundle(File.ReadAllBytes(bundlePath));
            AssertSequence(
                selected.Split(','),
                bundle.Selection.Select(selection => selection.EvidenceId),
                "CLI bundle selection order");
            AssertSequence(
                bundle.SourceLedgers
                    .Select(source => source.SourceLedgerSha256)
                    .Order(StringComparer.Ordinal),
                bundle.SourceLedgers.Select(source => source.SourceLedgerSha256),
                "CLI source-ledger digest ordering");

            var supplementalPaths = new List<string>();
            var supplementalIds = new List<string>();
            for (var index = 0; index < ResourceLimits.SupplementalInputCount + 1; index++)
            {
                var digest = Convert.ToHexStringLower(
                    System.Security.Cryptography.SHA256.HashData(
                        Encoding.UTF8.GetBytes($"supplemental-{index}")));
                var supplemental = EvidenceLedgerBuilder.BuildRepositoryLedger(
                    subject,
                    [
                        RepositoryDraft(
                            EvidenceIdentity.OwnerSuppliedInternalEvidence,
                            $"supplemental-{index}.txt",
                            $"Supplemental evidence record {index}.") with
                        {
                            Provenance = RepositoryDraft(
                                EvidenceIdentity.OwnerSuppliedInternalEvidence,
                                $"supplemental-{index}.txt",
                                $"Supplemental evidence record {index}.").Provenance with
                            {
                                ContentDigest = new Sha256Digest("sha256", digest)
                            }
                        }
                    ]);
                var path = Path.Combine(root, $"supplemental-{index}.ledger.json");
                File.WriteAllBytes(
                    path,
                    CanonicalEvidenceJson.SerializeSourceLedger(supplemental));
                supplementalPaths.Add(path);
                supplementalIds.Add(supplemental.Records[0].StableId);
            }

            var thirtyTwoBundle = BuildBundleArguments(
                validatorDll,
                assessmentPath,
                supplementalPaths.Take(ResourceLimits.SupplementalInputCount),
                supplementalIds[0],
                Path.Combine(root, "thirty-two.bundle.json"));
            RunProcess(
                "dotnet",
                thirtyTwoBundle,
                repositoryRoot,
                environment,
                expectedExitCode: ExitCodes.Success);

            var thirtyThree = RunProcess(
                "dotnet",
                BuildBundleArguments(
                    validatorDll,
                    assessmentPath,
                    supplementalPaths,
                    supplementalIds[0],
                    Path.Combine(root, "thirty-three.bundle.json")),
                repositoryRoot,
                environment,
                expectedExitCode: ExitCodes.ValidationFailure);
            Assert(
                thirtyThree.StandardError.Contains(
                    "at most 32 explicit supplemental inputs",
                    StringComparison.Ordinal),
                "33 source-ledger CLI limit");

            var aggregatePaths = new List<string>();
            for (var index = 0; index < ResourceLimits.SupplementalInputCount; index++)
            {
                var path = Path.Combine(root, $"aggregate-{index}.ledger.json");
                SetFileLength(path, 2L * 1024 * 1024);
                aggregatePaths.Add(path);
            }

            var aggregateExact = RunProcess(
                "dotnet",
                BuildBundleArguments(
                    validatorDll,
                    assessmentPath,
                    aggregatePaths,
                    supplementalIds[0],
                    Path.Combine(root, "aggregate-exact.bundle.json")),
                repositoryRoot,
                environment,
                expectedExitCode: ExitCodes.ValidationFailure);
            Assert(
                !aggregateExact.StandardError.Contains(
                    "supplemental evidence inputs exceeds",
                    StringComparison.Ordinal),
                "exact 64 MiB aggregate passes the aggregate resource gate");
            SetFileLength(aggregatePaths[0], (2L * 1024 * 1024) + 1);
            var aggregateOver = RunProcess(
                "dotnet",
                BuildBundleArguments(
                    validatorDll,
                    assessmentPath,
                    aggregatePaths,
                    supplementalIds[0],
                    Path.Combine(root, "aggregate-over.bundle.json")),
                repositoryRoot,
                environment,
                expectedExitCode: ExitCodes.ValidationFailure);
            Assert(
                aggregateOver.StandardError.Contains(
                    "supplemental evidence inputs exceeds",
                    StringComparison.Ordinal),
                "64 MiB aggregate plus one byte fails the CLI resource gate");

            var exactLedgerLimit = Path.Combine(root, "ledger-exact-limit.json");
            var overLedgerLimit = Path.Combine(root, "ledger-over-limit.json");
            SetFileLength(exactLedgerLimit, ResourceLimits.AuthoredLedgerBytes);
            SetFileLength(overLedgerLimit, ResourceLimits.AuthoredLedgerBytes + 1);
            var exactLedgerResult = RunProcess(
                "dotnet",
                [validatorDll, "evidence", "ledger-validate", "--ledger", exactLedgerLimit],
                repositoryRoot,
                environment,
                expectedExitCode: ExitCodes.ValidationFailure);
            Assert(
                !exactLedgerResult.StandardError.Contains(
                    "authored source ledger exceeds",
                    StringComparison.Ordinal),
                "exact 4 MiB ledger reaches canonical validation");
            var overLedgerResult = RunProcess(
                "dotnet",
                [validatorDll, "evidence", "ledger-validate", "--ledger", overLedgerLimit],
                repositoryRoot,
                environment,
                expectedExitCode: ExitCodes.ValidationFailure);
            Assert(
                overLedgerResult.StandardError.Contains(
                    "authored source ledger exceeds",
                    StringComparison.Ordinal),
                "4 MiB ledger plus one byte fails the CLI resource gate");

            var mismatchedSubjects = new[]
            {
                subject with
                {
                    Package = subject.Package with { PackageId = "other.widgets" }
                },
                subject with
                {
                    Package = subject.Package with { Version = "1.2.4" }
                },
                subject with
                {
                    Package = subject.Package with
                    {
                        NupkgDigest = new Sha256Digest("sha256", new string('f', 64))
                    }
                },
            };
            for (var index = 0; index < mismatchedSubjects.Length; index++)
            {
                var mismatchedSubjectPath = Path.Combine(root, $"mismatched-subject-{index}.json");
                File.WriteAllBytes(
                    mismatchedSubjectPath,
                    CanonicalEvidenceJson.SerializeRepositorySubject(mismatchedSubjects[index]));
                var mismatch = RunProcess(
                    "dotnet",
                    [
                        validatorDll,
                        "evidence",
                        "ledger-build",
                        "--kind",
                        "repository",
                        "--subject",
                        mismatchedSubjectPath,
                        "--draft",
                        repositoryDraftPath,
                        "--nupkg",
                        packagePath,
                        "--output",
                        Path.Combine(root, $"must-not-exist-{index}.json")
                    ],
                    repositoryRoot,
                    environment,
                    expectedExitCode: 1);
                Assert(
                    mismatch.StandardError.Contains(
                        "exact nupkg ID, version, or digest",
                        StringComparison.Ordinal),
                    $"CLI exact package mismatch {index}");
            }

            AssertEqual(
                2,
                RunProcess(
                    "dotnet",
                    [validatorDll, "evidence", "ledger-build"],
                    repositoryRoot,
                    environment,
                    expectedExitCode: 2).ExitCode,
                "CLI usage exit");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static ExactAssessmentIdentity KnownAssessment() =>
        CanonicalEvidenceJson.ParseAssessment(Encoding.UTF8.GetBytes(KnownAssessmentJson));

    private static RepositoryLedgerSubject KnownRepositorySubject() =>
        new(
            KnownAssessment().AssessmentKind,
            KnownAssessment().Package,
            KnownAssessment().InputManifestDigest,
            KnownAssessment().ComponentId);

    private static EvidenceRecordDraft RepositoryDraft(
        string kind,
        string locator,
        string claim) =>
        new(
            claim,
            new EvidenceApplicability("repository-wide", null),
            new EvidenceProvenance(
                kind,
                locator,
                "Captured evidence deterministically.",
                "2026-09-02T20:00:00Z",
                new Sha256Digest("sha256", new string('b', 64)),
                "commitment-only"),
            []);

    private static EvidenceRecordDraft ComponentDraft(string componentId) =>
        new(
            "The component keyboard probe completed successfully.",
            new EvidenceApplicability("component-specific", componentId),
            new EvidenceProvenance(
                EvidenceIdentity.ReproducedRuntimeObservation,
                "dotnet test Tree.Keyboard",
                "Ran the retained component probe.",
                "2026-09-02T20:00:00Z",
                new Sha256Digest("sha256", new string('e', 64)),
                "commitment-only"),
            []);

    private static EvidenceRecordDraft PackageArtifactDraft(
        string locator,
        string digest) =>
        new(
            "The exact package artifact contains the retained metadata.",
            new EvidenceApplicability("repository-wide", null),
            new EvidenceProvenance(
                EvidenceIdentity.PackageArtifactMetadata,
                locator,
                "Read the exact package archive entry.",
                "2026-09-02T20:00:00Z",
                new Sha256Digest("sha256", digest),
                "commitment-only"),
            []);

    private static EvidenceRecordDraft ToDraft(EvidenceRecord record) =>
        new(
            record.Claim,
            record.Applicability,
            record.Provenance,
            record.Supersedes);

    private static string DraftJson(EvidenceRecordDraft draft)
        => DraftJson([draft]);

    private static string DraftJson(IReadOnlyList<EvidenceRecordDraft> drafts)
    {
        static string Escape(string value) =>
            value.Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("\"", "\\\"", StringComparison.Ordinal);

        var records = drafts.Select(draft =>
        {
            var componentId = draft.Applicability.ComponentId is null
                ? "null"
                : $"\"{Escape(draft.Applicability.ComponentId)}\"";
            var supersedes = string.Join(
                ',',
                draft.Supersedes.Select(value => $"\"{Escape(value)}\""));
            return
                $$"""{"claim":"{{Escape(draft.Claim)}}","applicability":{"scope":"{{draft.Applicability.Scope}}","component_id":{{componentId}}},"provenance":{"kind":"{{draft.Provenance.Kind}}","locator":"{{Escape(draft.Provenance.Locator)}}","method":"{{Escape(draft.Provenance.Method)}}","captured_at_utc":"{{draft.Provenance.CapturedAtUtc}}","content_sha256":{"algorithm":"sha256","value":"{{draft.Provenance.ContentDigest.Value}}"},"retention":"commitment-only"},"supersedes":[{{supersedes}}]}""";
        });
        return $$"""{"schema_version":1,"records":[{{string.Join(',', records)}}]}""";
    }

    private static void CreatePackage(
        string path,
        string id,
        string version,
        IReadOnlyDictionary<string, byte[]>? additionalEntries = null)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
        var entry = archive.CreateEntry($"{id}.nuspec", CompressionLevel.NoCompression);
        using (var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false)))
        {
            writer.Write(
                $"<package><metadata><id>{id}</id><version>{version}</version></metadata></package>");
        }

        if (additionalEntries is not null)
        {
            foreach (var pair in additionalEntries)
            {
                var additional = archive.CreateEntry(pair.Key, CompressionLevel.NoCompression);
                using var output = additional.Open();
                output.Write(pair.Value);
            }
        }
    }

    private static void AssertPackageInspectionFailure(
        IReadOnlyList<string> arguments,
        int expectedExitCode,
        string outputPath,
        string name,
        bool requireAbsentOutput = true)
    {
        AssertEqual(
            expectedExitCode,
            CliApplication.Run(arguments, new StringWriter(), new StringWriter()),
            $"{name} exit");
        if (requireAbsentOutput)
        {
            Assert(!File.Exists(outputPath), $"{name} does not create an output file");
        }
        var parent = Path.GetDirectoryName(outputPath);
        if (parent is not null && Directory.Exists(parent))
        {
            Assert(
                !Directory.EnumerateFiles(parent, $".{Path.GetFileName(outputPath)}.*.tmp").Any(),
                $"{name} does not strand a temporary output");
        }
    }

    private static bool TryCreateFileSymlink(string linkPath, string targetPath)
    {
        try
        {
            File.CreateSymbolicLink(linkPath, targetPath);
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (PlatformNotSupportedException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static void AssertCliValidationFailure(
        IReadOnlyList<string> arguments,
        string expectedMessage,
        string name)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        AssertEqual(
            ExitCodes.ValidationFailure,
            CliApplication.Run(arguments, output, error),
            $"{name} exit");
        Assert(
            error.ToString().Contains(expectedMessage, StringComparison.Ordinal),
            $"{name} message: {error}");
    }

    private static string ExtractBashBlock(string markdown, string anchor)
    {
        var anchorIndex = markdown.IndexOf(anchor, StringComparison.Ordinal);
        Assert(anchorIndex >= 0, $"documented selection anchor exists: {anchor}");
        var blockStart = markdown.IndexOf("```bash", anchorIndex, StringComparison.Ordinal);
        Assert(blockStart >= 0, $"documented Bash block exists: {anchor}");
        blockStart += "```bash".Length;
        var blockEnd = markdown.IndexOf("```", blockStart, StringComparison.Ordinal);
        Assert(blockEnd >= 0, $"documented Bash block closes: {anchor}");
        return markdown[blockStart..blockEnd].Trim();
    }

    private static ProcessResult RunProcess(
        string executable,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        IReadOnlyDictionary<string, string> environment,
        int? expectedExitCode)
    {
        if (executable == "bash" &&
            Environment.GetEnvironmentVariable("READINESS_TEST_BASH") is { } bashExecutable)
        {
            Assert(Path.IsPathFullyQualified(bashExecutable) && File.Exists(bashExecutable),
                "READINESS_TEST_BASH must name an existing absolute Bash executable.");
            executable = bashExecutable;
        }

        var startInfo = new ProcessStartInfo(executable)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        foreach (var pair in environment)
        {
            startInfo.Environment[pair.Key] = pair.Value;
        }

        FoundationTests.ResolveWindowsBash(startInfo);
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Could not start {executable}.");
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(milliseconds: 180_000))
        {
            process.Kill(entireProcessTree: true);
            _ = process.WaitForExit(milliseconds: 5_000);
            _ = Task.WaitAll(new Task[] { standardOutput, standardError }, millisecondsTimeout: 5_000);
            throw new TimeoutException($"{executable} did not exit within three minutes.");
        }

        if (!Task.WaitAll(
                new Task[] { standardOutput, standardError },
                millisecondsTimeout: 5_000))
        {
            throw new TimeoutException(
                $"{executable} output streams did not drain within five seconds.");
        }
        var result = new ProcessResult(
            process.ExitCode,
            standardOutput.Result,
            standardError.Result);
        if (expectedExitCode is { } expected)
        {
            Assert(
                result.ExitCode == expected,
                $"{executable} exit code: expected {expected}, actual {result.ExitCode}.\n{result}");
        }
        return result;
    }

    private static string[] BuildBundleArguments(
        string validatorDll,
        string assessmentPath,
        IEnumerable<string> sourceLedgers,
        string evidenceId,
        string outputPath)
    {
        var arguments = new List<string>
        {
            validatorDll,
            "evidence",
            "bundle",
            "--assessment",
            assessmentPath
        };
        foreach (var sourceLedger in sourceLedgers)
        {
            arguments.Add("--source-ledger");
            arguments.Add(sourceLedger);
        }

        arguments.Add("--ids");
        arguments.Add(evidenceId);
        arguments.Add("--output");
        arguments.Add(outputPath);
        return arguments.ToArray();
    }

    private static void SetFileLength(string path, long length)
    {
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        stream.SetLength(length);
    }

    private static void ExpectValidation(Action action, string name)
    {
        _ = CaptureValidation(action, name);
    }

    private static DeterministicValidationException CaptureValidation(
        Action action,
        string name = "validation")
    {
        try
        {
            action();
        }
        catch (DeterministicValidationException exception)
        {
            return exception;
        }

        throw new InvalidOperationException($"{name}: expected deterministic validation failure.");
    }

    private static void AssertBytes(byte[] expected, byte[] actual, string name) =>
        Assert(expected.AsSpan().SequenceEqual(actual), $"{name}: byte mismatch.");

    private static void AssertSequence<T>(
        IEnumerable<T> expected,
        IEnumerable<T> actual,
        string name)
    {
        var expectedArray = expected.ToArray();
        var actualArray = actual.ToArray();
        Assert(
            expectedArray.SequenceEqual(actualArray),
            $"{name}: expected [{string.Join(", ", expectedArray)}], " +
            $"actual [{string.Join(", ", actualArray)}].");
    }

    private static void AssertEqual<T>(T expected, T actual, string name) =>
        Assert(
            EqualityComparer<T>.Default.Equals(expected, actual),
            $"{name}: expected '{expected}', actual '{actual}'.");

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed record ProcessResult(
        int ExitCode,
        string StandardOutput,
        string StandardError)
    {
        public override string ToString() =>
            $"stdout:\n{StandardOutput}\nstderr:\n{StandardError}";
    }
}
