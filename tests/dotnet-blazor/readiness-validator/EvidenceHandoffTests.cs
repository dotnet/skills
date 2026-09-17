using System.IO.Compression;
using System.Text;
using BlazorComponentReadiness.Validator.Assessment;
using BlazorComponentReadiness.Validator.Cli;
using BlazorComponentReadiness.Validator.Contracts;
using BlazorComponentReadiness.Validator.Evidence;
using BlazorComponentReadiness.Validator.Inputs;
using BlazorComponentReadiness.Validator.IO;

internal static class EvidenceHandoffTests
{
    private const string ResultName = "derived-facts.json";
    private const string Capture = "2026-09-09T19:00:00Z";
    private static int assertions;

    public static void Run(string repositoryRoot, string pluginRoot)
    {
        var parent = Environment.GetEnvironmentVariable("READINESS_TEST_ARTIFACTS") ??
            Path.Combine(repositoryRoot, "artifacts");
        var root = Path.Combine(parent, $"evidence-handoff-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var previousSkill = Environment.GetEnvironmentVariable("READINESS_SKILL_ROOT");
        Environment.SetEnvironmentVariable("READINESS_SKILL_ROOT",
            Path.Combine(pluginRoot, "skills", "blazor-component-readiness"));
        try
        {
            var fixture = new Fixture(root);
            TestConstructionAndAcceptance(fixture);
            TestOptionsAndInputs(fixture);
            TestBindingCompatibility(fixture);
            fixture.AssertPreserved();
            Console.WriteLine($"Evidence handoff: {assertions} assertions passed.");
        }
        finally
        {
            Environment.SetEnvironmentVariable("READINESS_SKILL_ROOT", previousSkill);
            Directory.Delete(root, recursive: true);
        }
    }

    private static void TestConstructionAndAcceptance(Fixture f)
    {
        var wrongLocator = f.Record(EvidenceIdentity.ReviewerGeneratedAnalysis, "release-facts/1.0.0");
        var legacy = f.BundleArguments(f.Identity, [wrongLocator]);
        var structural = Invoke(legacy);
        Assert(structural.Exit == 0, "legacy structural-only bundle remains supported");
        var unbound = CanonicalEvidenceJson.ParseBundle(File.ReadAllBytes(Value(legacy, "--output")));
        try
        {
            EvidenceInputBindingValidator.Validate(f.Root, f.Assessment, f.Manifest, unbound);
            throw new InvalidOperationException("Wrong locator unexpectedly bound.");
        }
        catch (DeterministicValidationException error)
        {
            Console.WriteLine($"Pre-existing structural/binding distinction: {structural.Exit}; {error.Message}");
        }

        var draftArgs = f.DraftArguments();
        var draft = Invoke(draftArgs);
        var boundArgs = f.BundleArguments(f.Identity, [f.Record(EvidenceIdentity.ReviewerGeneratedAnalysis, ResultName)]);
        var bound = Invoke([.. boundArgs, "--root", f.Root, "--manifest", f.PostInput]);
        Console.WriteLine($"Registered draft regression: exit={draft.Exit}; {draft.Error.Trim()}");
        Console.WriteLine($"Input-bound bundle regression: exit={bound.Exit}; {bound.Error.Trim()}");
        Assert(draft.Exit == 0 && bound.Exit == 0, "registered construction and bound acceptance are available");
        Assert(structural.Output.Contains("structural", StringComparison.OrdinalIgnoreCase) &&
               structural.Output.Contains("not", StringComparison.OrdinalIgnoreCase),
            "structural-only success discloses unaccepted input linkage");
        Assert(bound.Output.Contains("input-bound", StringComparison.OrdinalIgnoreCase), "bound success is explicit");

        var firstBytes = File.ReadAllBytes(Value(draftArgs, "--output"));
        var first = CanonicalEvidenceJson.ParseDraftDocument(firstBytes).Records.Single();
        Assert(first.Provenance.Locator == ResultName && first.Provenance.ContentDigest == f.ResultDigest,
            "registered basename and actual digest derived together");
        var repeat = f.DraftArguments();
        Success(Replace(repeat, "--manifest", System.IO.Path.GetFileName(f.PostInput)));
        Assert(firstBytes.SequenceEqual(File.ReadAllBytes(Value(repeat, "--output"))), "registered drafts repeat exactly");
        var append = f.DraftArguments();
        Success([.. Replace(append, "--claim", "A second specific observed fact."),
            "--input", Value(draftArgs, "--output")]);
        Assert(firstBytes.SequenceEqual(File.ReadAllBytes(Value(draftArgs, "--output"))), "previous draft untouched");
        Assert(CanonicalEvidenceJson.ParseDraftDocument(File.ReadAllBytes(Value(append, "--output"))).Records.Count == 2,
            "registered draft immutable append");

        var ledgerPath = f.NewPath("produced-ledger");
        Success("evidence", "ledger-build", "--kind", "repository", "--subject", f.IdentityPath,
            "--draft", Value(append, "--output"), "--nupkg", f.PackagePath, "--output", ledgerPath);
        Success("evidence", "ledger-validate", "--ledger", ledgerPath);
        var ledger = CanonicalEvidenceJson.ParseSourceLedger(File.ReadAllBytes(ledgerPath));
        var final = f.NewPath("produced-bundle");
        Success("evidence", "bundle", "--assessment", f.IdentityPath, "--source-ledger", ledgerPath,
            "--ids", string.Join(",", ledger.Records.Select(record => record.StableId)),
            "--root", f.Root, "--manifest", f.PostInput, "--output", final);
        var bundle = CanonicalEvidenceJson.ParseBundle(File.ReadAllBytes(final));
        Assert(bundle.Assessment == f.Identity && bundle.Selection.Count == 2, "producer-generated final identity and IDs");
        Assert(f.Identity.InputManifestDigest == InputManifestService.Digest(File.ReadAllBytes(f.PostInput)) &&
               f.Identity.InputManifestDigest != f.PreIdentity.InputManifestDigest, "post-confirmed identity");
        Assert(f.Manifest.EvidenceInputs.Count == f.PreManifest.EvidenceInputs.Count + 1 &&
               f.PreManifest.EvidenceInputs.All(item => f.Manifest.EvidenceInputs.Contains(item)),
            "post manifest retains prior registrations plus only the result");
        Assert(f.Assessment.CompletionState == "incomplete" &&
               f.Assessment.Rows.All(row => row.Status is null && row.EvidenceIds.Count == 0),
            "identity skeleton remains unscored");
        Assert(!Directory.EnumerateFiles(f.Root, "*report*", SearchOption.AllDirectories).Any(), "no report generated");

        foreach (var record in new[]
                 {
                     wrongLocator,
                     f.Record(EvidenceIdentity.ReviewerGeneratedAnalysis, ResultName) with
                     {
                         Provenance = first.Provenance with { ContentDigest = new("sha256", new string('0', 64)) }
                     },
                     f.Record(EvidenceIdentity.ReviewerGeneratedAnalysis, "absent.json")
                 })
        {
            var args = f.BundleArguments(f.Identity, [record]);
            Failure([.. args, "--root", f.Root, "--manifest", f.PostInput], 1,
                "Selected evidence", record.Provenance.Locator, f.PostInput);
        }
        Failure([.. f.BundleArguments(f.PreIdentity, [f.Record(EvidenceIdentity.ReviewerGeneratedAnalysis, ResultName)]),
            "--root", f.Root, "--manifest", f.PostInput], 1, "identity", f.PostInput);
        Failure([.. f.BundleArguments(f.Identity with { Package = f.Identity.Package with { Version = "9.9.9" } },
            [f.Record(EvidenceIdentity.ReviewerGeneratedAnalysis, ResultName)]),
            "--root", f.Root, "--manifest", f.PostInput], 1, "identity");
        var nonexistentComponent = f.Identity with { AssessmentKind = "unified", ComponentId = "Absent" };
        Failure([.. f.BundleArguments(nonexistentComponent, [f.Record(EvidenceIdentity.ReviewerGeneratedAnalysis, ResultName)]),
            "--root", f.Root, "--manifest", f.PostInput], 1, "component");
    }

    private static void TestOptionsAndInputs(Fixture f)
    {
        foreach (var option in new[] { "--root", "--manifest", "--evidence-input" })
            Failure(Remove(f.DraftArguments(), option), 2, option);
        foreach (var (option, value) in new[]
                 {
                     ("--locator", ResultName), ("--content", f.ResultPath),
                     ("--nupkg", f.PackagePath), ("--content-sha256", f.ResultDigest.Value)
                 })
            Failure([.. f.DraftArguments(), option, value], 2, option);
        Failure(Replace(f.DraftArguments(), "--kind", EvidenceIdentity.OwnerSuppliedPublicEvidence), 2,
            EvidenceIdentity.ReviewerGeneratedAnalysis);
        foreach (var name in new[] { "absent.json", "owner.txt", "../derived-facts.json", f.ResultPath,
                     "nested/file.json", " derived-facts.json " })
            Failure(Replace(f.DraftArguments(), "--evidence-input", name), 1);
        Failure(Replace(f.DraftArguments(), "--manifest", f.PreInput), 1, "register");

        var drafts = f.WriteManifest(f.Manifest with { State = "draft" });
        Failure(Replace(f.DraftArguments(), "--manifest", drafts), 1);
        var duplicate = f.WriteManifest(f.Manifest with { EvidenceInputs = [.. f.Manifest.EvidenceInputs, f.Manifest.EvidenceInputs.Last()] });
        Failure(Replace(f.DraftArguments(), "--manifest", duplicate), 1);
        var limited = f.WriteManifest(f.Manifest with
        {
            EvidenceInputs = f.Manifest.EvidenceInputs.Select(item =>
                item.Basename == ResultName ? item with { Size = ResourceLimits.SupplementalInputAggregateBytes + 1 } : item).ToArray()
        });
        Failure(Replace(f.DraftArguments(), "--manifest", limited), 1);

        var validRecords = new[] { f.Record(EvidenceIdentity.ReviewerGeneratedAnalysis, ResultName) };
        Failure([.. f.BundleArguments(f.Identity, validRecords), "--root", f.Root], 2, "--manifest");
        Failure([.. f.BundleArguments(f.Identity, validRecords), "--manifest", f.PostInput], 2, "--root");
        Failure([.. f.BundleArguments(f.Identity, validRecords), "--root", f.Root, "--manifest", drafts], 1);

        var original = File.ReadAllBytes(f.ResultPath);
        foreach (var bytes in new[] { Encoding.UTF8.GetBytes("different length"), original.Select(b => (byte)(b ^ 1)).ToArray() })
        {
            File.WriteAllBytes(f.ResultPath, bytes);
            Failure(f.DraftArguments(), 1);
            Failure([.. f.BundleArguments(f.Identity, validRecords), "--root", f.Root, "--manifest", f.PostInput], 1);
        }
        File.WriteAllBytes(f.ResultPath, original);

        using (var stream = File.OpenWrite(f.ResultPath))
            stream.SetLength(ResourceLimits.SupplementalInputAggregateBytes + 1);
        Failure(f.DraftArguments(), 1, "limit");
        Failure([.. f.BundleArguments(f.Identity, validRecords), "--root", f.Root, "--manifest", f.PostInput], 1, "limit");
        File.WriteAllBytes(f.ResultPath, original);
        var oversizedDraft = f.NewPath("oversized-draft");
        using (var stream = File.Create(oversizedDraft))
            stream.SetLength(ResourceLimits.AuthoredLedgerBytes + 1);
        Failure([.. f.DraftArguments(), "--input", oversizedDraft], 1, "limit");
        File.Delete(oversizedDraft);

        var documentationPath = f.Path("docs.txt");
        var documentationBytes = File.ReadAllBytes(documentationPath);
        File.WriteAllBytes(documentationPath, documentationBytes.Select(b => (byte)(b ^ 1)).ToArray());
        Failure(f.DraftArguments(), 1, "documentation");
        Failure([.. f.BundleArguments(f.Identity, validRecords), "--root", f.Root, "--manifest", f.PostInput], 1,
            "documentation");
        File.WriteAllBytes(documentationPath, documentationBytes);
        var packageBytes = File.ReadAllBytes(f.PackagePath);
        using (var stream = new FileStream(f.PackagePath, FileMode.Append))
            stream.WriteByte(0);
        Failure(f.DraftArguments(), 1, "package");
        Failure([.. f.BundleArguments(f.Identity, validRecords), "--root", f.Root, "--manifest", f.PostInput], 1, "package");
        File.WriteAllBytes(f.PackagePath, packageBytes);

        File.Delete(f.ResultPath);
        Failure(f.DraftArguments(), 3);
        Directory.CreateDirectory(f.ResultPath);
        Failure(f.DraftArguments(), 1);
        Directory.Delete(f.ResultPath);
        File.WriteAllBytes(f.ResultPath, original);

        if (!OperatingSystem.IsWindows())
        {
            var link = f.NewPath("manifest-link");
            File.CreateSymbolicLink(link, f.PostInput);
            Failure(Replace(f.DraftArguments(), "--manifest", link), 1, "Symbolic");
            Failure([.. f.BundleArguments(f.Identity, validRecords), "--root", f.Root, "--manifest", link], 1, "Symbolic");
            File.Delete(link);
            File.Delete(f.ResultPath);
            File.CreateSymbolicLink(f.ResultPath, f.Path("raw.bin"));
            Failure(f.DraftArguments(), 1, "Symbolic");
            Failure([.. f.BundleArguments(f.Identity, validRecords), "--root", f.Root, "--manifest", f.PostInput], 1, "Symbolic");
            File.Delete(f.ResultPath);
            File.WriteAllBytes(f.ResultPath, original);
        }

        var large = f.NewPath("large-manifest");
        using (var stream = File.Create(large))
            stream.SetLength(ResourceLimits.SerializedArtifactBytes + 1);
        Failure(Replace(f.DraftArguments(), "--manifest", large), 1);
        File.Delete(large);
        var outside = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(f.Root)!, $"outside-{Guid.NewGuid():N}.json");
        File.WriteAllBytes(outside, File.ReadAllBytes(f.PostInput));
        try
        {
            Failure(Replace(f.DraftArguments(), "--manifest", outside), 1);
        }
        finally { File.Delete(outside); }

        var output = f.DraftArguments();
        Success(output);
        var outputBytes = File.ReadAllBytes(Value(output, "--output"));
        Assert(Invoke(output).Exit != 0 && outputBytes.SequenceEqual(File.ReadAllBytes(Value(output, "--output"))),
            "draft existing output not overwritten");
        var bundle = f.BundleArguments(f.Identity, validRecords);
        string[] bound = [.. bundle, "--root", f.Root, "--manifest", System.IO.Path.GetFileName(f.PostInput)];
        Success(bound);
        var bundleBytes = File.ReadAllBytes(Value(bundle, "--output"));
        Assert(Invoke(bound).Exit != 0 && bundleBytes.SequenceEqual(File.ReadAllBytes(Value(bundle, "--output"))),
            "bundle existing output not overwritten");
    }

    private static void TestBindingCompatibility(Fixture f)
    {
        var records = new[]
        {
            f.Record(EvidenceIdentity.ReviewerGeneratedAnalysis, ResultName),
            f.Record(EvidenceIdentity.ReproducedRuntimeObservation, ResultName, "protocol:unknown"),
            f.Record(EvidenceIdentity.ReproducedRuntimeObservation, "legacy command probe"),
            f.Record(EvidenceIdentity.OwnerDeclaredUnavailable, "artifact:unavailable"),
            f.Record(EvidenceIdentity.VendorPublicDocumentation, "https://example.test/docs"),
            f.Record(EvidenceIdentity.VendorSourceRepository, "source:src/Observed.cs"),
            f.Record(EvidenceIdentity.OwnerSuppliedPublicEvidence, "owner.txt"),
            f.Record(EvidenceIdentity.OwnerSuppliedInternalEvidence, "internal.txt"),
            f.Record(EvidenceIdentity.PackageArtifactMetadata, NupkgInspector.WholePackageEvidenceLocator) with
            {
                Provenance = f.Record(EvidenceIdentity.PackageArtifactMetadata, NupkgInspector.WholePackageEvidenceLocator)
                    .Provenance with { ContentDigest = f.Manifest.Package.NupkgDigest }
            },
            f.Record(EvidenceIdentity.PackageArtifactMetadata, NupkgInspector.PackageEntryEvidencePrefix + "payload.txt")
        };
        Success([.. f.BundleArguments(f.Identity, records), "--root", f.Root, "--manifest", f.PostInput]);
        var unselected = f.Record(EvidenceIdentity.ReviewerGeneratedAnalysis, "not-registered");
        Success([.. f.BundleArguments(f.Identity, [records[0], unselected], selectedCount: 1),
            "--root", f.Root, "--manifest", f.PostInput]);
        Failure([.. f.BundleArguments(f.Identity, [records[0], unselected]),
            "--root", f.Root, "--manifest", f.PostInput], 1, "not-registered");
        Failure([.. f.BundleArguments(f.Identity,
            [f.Record(EvidenceIdentity.ReproducedRuntimeObservation, "unregistered", "protocol:unknown")]),
            "--root", f.Root, "--manifest", f.PostInput], 1);
        Failure([.. f.BundleArguments(f.Identity,
            [f.Record(EvidenceIdentity.OwnerSuppliedInternalEvidence, "owner.txt")]),
            "--root", f.Root, "--manifest", f.PostInput], 1);
        foreach (var record in records.Where(record => record.Provenance.Kind is
                     EvidenceIdentity.VendorPublicDocumentation or EvidenceIdentity.VendorSourceRepository or
                     EvidenceIdentity.PackageArtifactMetadata or EvidenceIdentity.OwnerSuppliedPublicEvidence))
        {
            Failure([.. f.BundleArguments(f.Identity, [record with
            {
                Provenance = record.Provenance with { ContentDigest = new("sha256", new string('0', 64)) }
            }]), "--root", f.Root, "--manifest", f.PostInput], 1);
        }

        var componentIdentity = f.Identity with
        {
            AssessmentKind = "component",
            ComponentId = f.Manifest.Components.Single().Id
        };
        Success([.. f.BundleArguments(componentIdentity,
            [f.Record(EvidenceIdentity.VendorSourceRepository, "source:src/Observed.cs")]),
            "--root", f.Root, "--manifest", f.PostInput]);
        var closed = f.Manifest with { Source = new("closed-source", null, null, null, null), SourceArtifacts = [] };
        var closedPath = f.WriteManifest(closed);
        var closedIdentity = f.Identity with { InputManifestDigest = InputManifestService.Digest(File.ReadAllBytes(closedPath)) };
        Success([.. f.BundleArguments(closedIdentity, [f.Record(EvidenceIdentity.OwnerDeclaredClosedSource, "source")]),
            "--root", f.Root, "--manifest", closedPath]);
        Failure([.. f.BundleArguments(f.Identity, [f.Record(EvidenceIdentity.OwnerDeclaredClosedSource, "source")]),
            "--root", f.Root, "--manifest", f.PostInput], 1);
    }

    private static (int Exit, string Output, string Error) Invoke(string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var exit = CliApplication.Run(args, output, error);
        return (exit, output.ToString(), error.ToString());
    }

    private static void Success(params string[] args)
    {
        var result = Invoke(args);
        Assert(result.Exit == 0, $"CLI success: {string.Join(" ", args.Take(2))}: {result.Error}");
    }

    private static void Failure(string[] args, int exit, params string[] diagnostics)
    {
        var result = Invoke(args);
        Assert(result.Exit == exit, $"expected exit {exit}, observed {result.Exit}: {result.Error}");
        Assert(!File.Exists(Value(args, "--output")), "failure publishes no output");
        foreach (var text in diagnostics)
            Assert(result.Error.Contains(text, StringComparison.OrdinalIgnoreCase), $"diagnostic includes {text}: {result.Error}");
    }

    private static string Value(string[] args, string option) => args[Array.IndexOf(args, option) + 1];
    private static string[] Replace(string[] args, string option, string value)
    {
        var copy = args.ToArray();
        copy[Array.IndexOf(copy, option) + 1] = value;
        return copy;
    }
    private static string[] Remove(string[] args, string option)
    {
        var index = Array.IndexOf(args, option);
        return [.. args.Take(index), .. args.Skip(index + 2)];
    }
    private static void Assert(bool condition, string message)
    {
        assertions++;
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class Fixture
    {
        private int sequence;
        private string candidates = "";
        private readonly Dictionary<string, byte[]> preserved = [];
        public string Root { get; }
        public string PackagePath => Path("Sample.Widgets.1.2.3.nupkg");
        public string ResultPath => Path(ResultName);
        public Sha256Digest ResultDigest { get; }
        public string PreInput { get; }
        public string PostInput { get; }
        public InputManifest PreManifest { get; }
        public InputManifest Manifest { get; }
        public ExactAssessmentIdentity PreIdentity { get; }
        public ExactAssessmentIdentity Identity => Assessment.Identity;
        public ReadinessAssessment Assessment { get; }
        public string IdentityPath { get; }

        public Fixture(string root)
        {
            Root = root;
            byte[] content = Encoding.UTF8.GetBytes("synthetic observed bytes");
            ResultDigest = ContractJson.RawDigest(content);
            using (var archive = ZipFile.Open(PackagePath, ZipArchiveMode.Create))
            {
                using (var writer = new StreamWriter(archive.CreateEntry("Sample.Widgets.nuspec").Open()))
                    writer.Write("<package><metadata><id>Sample.Widgets</id><version>1.2.3</version></metadata></package>");
                using var stream = archive.CreateEntry("payload.txt").Open();
                stream.Write(content);
            }
            foreach (var name in new[] { "raw.bin", "docs.txt", "source.txt", "owner.txt", "internal.txt" })
                File.WriteAllBytes(Path(name), content);
            Candidate("init", "--acquisition", "release-candidate", "--package-locator", System.IO.Path.GetFileName(PackagePath),
                "--package-method", "local-file", "--source-availability", "source-available",
                "--repository-uri", "https://example.test/repo", "--source-commit", new string('c', 40),
                "--source-mapping", "Synthetic test mapping.", "--source-confidence", "high");
            Candidate("add-retrieval", "--subject", "package", "--locator", System.IO.Path.GetFileName(PackagePath),
                "--method", "local-file", "--result", "succeeded");
            Candidate("add-document", "--url", "https://example.test/docs", "--path", "docs.txt");
            Candidate("add-source-artifact", "--source-path", "src/Observed.cs", "--path", "source.txt");
            Candidate("add-owner-input", "--path", "owner.txt", "--provenance", EvidenceIdentity.OwnerSuppliedPublicEvidence);
            Candidate("add-owner-input", "--path", "internal.txt", "--provenance", EvidenceIdentity.OwnerSuppliedInternalEvidence);
            Candidate("add-evidence", "--path", "raw.bin", "--kind", "raw");
            Candidate("add-component", "--id", "Observed", "--name", "Observed", "--mode", "static-ssr",
                "--source-path", "src/Observed.cs", "--lifecycle-applicability", "not-applicable",
                "--lifecycle-rationale", "Synthetic component without children.");
            PreInput = Confirm();
            PreManifest = InputManifestService.Parse(File.ReadAllBytes(PreInput));
            PreIdentity = Initialize(PreInput).Assessment.Identity;
            foreach (var file in Directory.EnumerateFiles(Root))
                preserved.Add(file, File.ReadAllBytes(file));
            File.WriteAllBytes(ResultPath, content);
            preserved.Add(ResultPath, content);
            Candidate("add-evidence", "--path", ResultName, "--kind", "offline-release-facts");
            PostInput = Confirm();
            Manifest = InputManifestService.Parse(File.ReadAllBytes(PostInput));
            (Assessment, IdentityPath) = Initialize(PostInput);
            preserved.Add(PostInput, File.ReadAllBytes(PostInput));
        }

        public string Path(string name) => System.IO.Path.Combine(Root, name);
        public string NewPath(string kind) => Path($"{kind}-{sequence++}.json");
        public string WriteManifest(InputManifest input)
        {
            var path = NewPath("input");
            File.WriteAllBytes(path, InputManifestService.Serialize(input));
            return path;
        }
        private void Candidate(string command, params string[] args)
        {
            var next = NewPath("candidates");
            Success(["inputs", "candidates", command, .. (command == "init" ? Array.Empty<string>() : new[] { "--input", candidates }),
                .. args, "--output", next]);
            candidates = next;
        }
        private string Confirm()
        {
            var draft = NewPath("input-draft");
            var confirmed = NewPath("input-confirmed");
            Success("inputs", "discover", "--root", Root, "--nupkg", PackagePath, "--candidates", candidates, "--output", draft);
            Success("inputs", "confirm", "--root", Root, "--draft", draft, "--output", confirmed);
            Success("inputs", "validate", "--root", Root, "--manifest", confirmed);
            return confirmed;
        }
        private (ReadinessAssessment Assessment, string IdentityPath) Initialize(string input)
        {
            var skeleton = NewPath("skeleton");
            var identity = NewPath("identity");
            Success("assessment", "init", "--kind", "package", "--root", Root, "--input", input, "--output", skeleton);
            Success("assessment", "export-identity", "--assessment", skeleton, "--output", identity);
            return (AssessmentService.Parse(File.ReadAllBytes(skeleton)), identity);
        }
        public string[] DraftArguments() =>
            ["evidence", "draft-add", "--output", NewPath("draft"), "--claim", "A specific observed fact.",
             "--scope", "repository-wide", "--kind", EvidenceIdentity.ReviewerGeneratedAnalysis,
             "--method", "Offline mechanical collection; no authentication.", "--captured-at", Capture,
             "--root", Root, "--manifest", PostInput, "--evidence-input", ResultName];
        public EvidenceRecordDraft Record(string kind, string locator, string method = "Observed retained test bytes.") =>
            new($"Synthetic {kind} observation at {locator}.", new("repository-wide", null),
                new(kind, locator, method, Capture, ResultDigest, "commitment-only"), []);
        public string[] BundleArguments(ExactAssessmentIdentity identity, EvidenceRecordDraft[] records, int? selectedCount = null)
        {
            var subject = NewPath("subject");
            var source = NewPath("ledger");
            File.WriteAllBytes(subject, CanonicalEvidenceJson.SerializeAssessment(identity));
            var ledger = identity.AssessmentKind == "component"
                ? EvidenceLedgerBuilder.BuildComponentLedger(identity, records.Select(record => record with
                {
                    Applicability = new("component-specific", identity.ComponentId)
                }))
                : EvidenceLedgerBuilder.BuildRepositoryLedger(
                    new(identity.AssessmentKind, identity.Package, identity.InputManifestDigest, identity.ComponentId), records);
            File.WriteAllBytes(source, CanonicalEvidenceJson.SerializeSourceLedger(ledger));
            var selected = selectedCount is null ? ledger.Records : ledger.Records.Where(record =>
                records.Take(selectedCount.Value).Any(draft => draft.Claim == record.Claim)).ToArray();
            return ["evidence", "bundle", "--assessment", subject, "--source-ledger", source,
                "--ids", string.Join(",", selected.Select(record => record.StableId)), "--output", NewPath("bundle")];
        }
        public void AssertPreserved()
        {
            foreach (var (path, bytes) in preserved)
                Assert(bytes.SequenceEqual(File.ReadAllBytes(path)), $"preserved {System.IO.Path.GetFileName(path)}");
        }
    }
}
