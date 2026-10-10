using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using BlazorComponentReadiness.Validator.Assessment;
using BlazorComponentReadiness.Validator.Cli;
using BlazorComponentReadiness.Validator.Evidence;
using BlazorComponentReadiness.Validator.Inputs;
using BlazorComponentReadiness.Validator.IO;
using BlazorComponentReadiness.Validator.Release;

internal static class ReleaseFactsTests
{
    private const string Filename = "Sample.Widgets.1.2.3.nupkg";
    private const string Entry22 = "spdx_2.2/manifest.spdx.json";
    private const string Entry30 = "spdx_3.0/manifest.spdx.json";
    private static int assertions;

    public static void Run(string repositoryRoot, string pluginRoot)
    {
        var parent = Environment.GetEnvironmentVariable("READINESS_TEST_ARTIFACTS") ??
            Path.Combine(repositoryRoot, "artifacts");
        var root = Path.Combine(parent, $"release-facts-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var priorSkill = Environment.GetEnvironmentVariable("READINESS_SKILL_ROOT");
        Environment.SetEnvironmentVariable("READINESS_SKILL_ROOT",
            Path.Combine(pluginRoot, "skills", "blazor-component-readiness"));
        try
        {
            TestReviewCorrections(root, pluginRoot);
            TestValidAndIntegration(new Fixture(Path.Combine(root, "valid")));
            TestCompatibleAndStructural(new Fixture(Path.Combine(root, "compatible")));
            TestFatalInputs(root);
            TestArchiveAndLimits(root);
            Console.WriteLine($"Release facts: {assertions} assertions passed.");
        }
        finally
        {
            Environment.SetEnvironmentVariable("READINESS_SKILL_ROOT", priorSkill);
            Directory.Delete(root, recursive: true);
        }
    }

    private static void TestReviewCorrections(string root, string pluginRoot)
    {
        var failures = new List<string>();
        foreach (var identity in new[] { "file", "document", "file-alias", "document-alias" })
        {
            var fixture = new Fixture(Path.Combine(root, "cross-level-" + identity));
            var graph = fixture.Spdx30["@graph"]!;
            graph[0]!["@id"] = "document-alias";
            graph[1]!["@id"] = "file-alias";
            graph[1]!["verifiedUsing"]![0]!["@id"] = identity;
            fixture.WriteSpdx();
            fixture.Confirm();
            var error = new StringWriter();
            var exit = CliApplication.Run(fixture.Arguments("collision.json"), new StringWriter(), error);
            Record(exit == 1 && !File.Exists(fixture.Path("collision.json")) &&
                   error.ToString().Contains("spdx30=failed", StringComparison.Ordinal),
                $"required cross-level identity {identity}: expected rejection without output, actual exit {exit}");
        }

        int noncanonicalDecodedLength;
        bool decoderRejected;
        try
        {
            noncanonicalDecodedLength = Convert.FromBase64String("Zh==").Length;
            decoderRejected = false;
        }
        catch (FormatException)
        {
            noncanonicalDecodedLength = 0;
            decoderRejected = true;
        }
        Console.WriteLine($"Release facts BCL probe: Zh== conversion rejected={decoderRejected}, returned bytes={noncanonicalDecodedLength}.");

        foreach (var signature in new[] { true, false })
        {
            var fixture = new Fixture(Path.Combine(root, signature ? "signature-accounting" : "payload-accounting"));
            fixture.ChangeBundle(bundle =>
            {
                var envelope = bundle["dsseEnvelope"]!;
                if (signature) envelope["signatures"]![0]!["sig"] = "Zh==";
                else envelope["payload"] = "Zh==";
            });
            fixture.Confirm();
            var error = new StringWriter();
            var exit = CliApplication.Run(fixture.Arguments("noncanonical.json"), new StringWriter(), error);
            var decoded = System.Text.RegularExpressions.Regex.Match(error.ToString(), @"decoded_bytes=(\d+)");
            var expectedBytes = noncanonicalDecodedLength + (signature ? 0 : 1);
            Record(exit == 1 && !File.Exists(fixture.Path("noncanonical.json")),
                "noncanonical base64 must remain fatal");
            Record(decoded.Success && long.Parse(decoded.Groups[1].Value) == expectedBytes,
                $"noncanonical {(signature ? "signature" : "payload")}: expected decoded_bytes={expectedBytes}, actual {decoded.Groups[1].Value}; {error}");
            Record(error.ToString().Contains(
                    decoderRejected ? "is malformed base64" : "is not canonical base64", StringComparison.Ordinal),
                "noncanonical base64 reports the actual rejection branch");
            var diagnostics = System.Text.RegularExpressions.Regex.Match(error.ToString(), @"diagnostics_bytes=(\d+)");
            Record(diagnostics.Success && long.Parse(diagnostics.Groups[1].Value) ==
                   Encoding.UTF8.GetByteCount(error.ToString()), "noncanonical base64 exact diagnostic accounting");
        }

        Assert(failures.Count == 0, string.Join("; ", failures));

        var invalidPayload = new Fixture(Path.Combine(root, "decoded-invalid-json-accounting"));
        invalidPayload.ChangeBundle(bundle => bundle["dsseEnvelope"]!["payload"] =
            Convert.ToBase64String(Encoding.UTF8.GetBytes("{")));
        invalidPayload.Confirm();
        var invalidError = new StringWriter();
        Assert(CliApplication.Run(invalidPayload.Arguments("invalid.json"), new StringWriter(), invalidError) == 1 &&
               !File.Exists(invalidPayload.Path("invalid.json")), "decoded malformed JSON is fatal without output");
        Assert(invalidError.ToString().Contains("decoded_bytes=000000000002", StringComparison.Ordinal),
            "both canonical signature and subsequently rejected decoded JSON bytes are charged");

        // Strict runtimes reject nonzero pad bits before returning, so also guard the later-check order.
        var parsingSource = File.ReadAllText(System.IO.Path.Combine(pluginRoot,
            "skills/blazor-component-readiness/scripts/validator/ReleaseFacts/ReleaseFactsParsing.cs"));
        var decodeStart = parsingSource.IndexOf("private byte[] Decode(", StringComparison.Ordinal);
        var decodeEnd = parsingSource.IndexOf("private static void ValidateChecksums(", decodeStart, StringComparison.Ordinal);
        var decodeSource = parsingSource[decodeStart..decodeEnd];
        var chargePosition = decodeSource.IndexOf("Charge(ref decodedBytes, bytes.LongLength);", StringComparison.Ordinal);
        Assert(chargePosition >= 0 && decodeSource.IndexOf("Require(Convert.ToBase64String(bytes) == value",
                   StringComparison.Ordinal) > chargePosition, "returned decoded bytes charged before canonicality rejection");

        var large = new Fixture(Path.Combine(root, "many-file-references"));
        var files = large.Spdx22["files"]!.AsArray();
        var references = large.Spdx22["packages"]![0]!["hasFiles"]!.AsArray();
        for (var index = 0; index < 4000; index++)
        {
            var id = $"extra-file-{index}";
            files.Add(new JsonObject { ["SPDXID"] = id, ["fileName"] = $"./extra-{index}.txt" });
            references.Add(id);
        }
        large.WriteSpdx();
        large.WriteSbom(large.Spdx22);
        large.Confirm();
        var result = large.Collect("many-files.json");
        ExpectComparison(result, "spdx22-release", "match");
        ExpectComparison(result, "sbom-document", "match");
        Assert(result.Facts.Single(fact => fact.Id == "spdx22.file-id").Value == "file",
            "large file-ID membership retains the selected identity");
        references.Add("extra-file-3999");
        large.WriteSpdx();
        large.Confirm();
        large.ExpectFailure("large duplicate hasFiles reference", output: "duplicate.json", failedOperation: "spdx22");
        references.RemoveAt(references.Count - 1);
        references[references.Count - 1] = "missing-file";
        large.WriteSpdx();
        large.Confirm();
        large.ExpectFailure("large unresolved hasFiles reference", output: "unresolved.json", failedOperation: "spdx22");

        void Record(bool condition, string description)
        {
            assertions++;
            if (!condition) failures.Add(description);
        }
    }

    private static void TestValidAndIntegration(Fixture fixture)
    {
        fixture.Spdx30["@graph"]![0]!["@id"] = "document";
        fixture.Spdx30["@graph"]![1]!["@id"] = "file-alias";
        var repeatedInline = fixture.Spdx30["@graph"]![1]!["verifiedUsing"]![0]!.DeepClone();
        repeatedInline["hashValue"] = new string('d', 64);
        fixture.Spdx30["@graph"]![0]!["unrelatedInline"] = repeatedInline;
        fixture.WriteSpdx();
        fixture.Confirm();
        var preserved = fixture.InputFiles.ToDictionary(path => path, File.ReadAllBytes);
        var first = fixture.Collect("first.json");
        var second = fixture.Collect("second.json");
        Assert(first.NormalizedProjection().SequenceEqual(second.NormalizedProjection()), "normalized repeatability");
        Assert(first.SchemaVersion == 1 && first.NormalizationVersion == 1, "versioned result");
        Assert(first.Operations.Count == 10 && first.Operations.All(item => item.Outcome == "succeeded"),
            "all required operations accounted for");
        Assert(first.Comparisons.Count == 11, "eleven comparisons");
        ExpectComparison(first, "target-release", "mismatch");
        ExpectComparison(first, "spdx22-target", "mismatch");
        ExpectComparison(first, "spdx22-release", "match");
        ExpectComparison(first, "spdx30-target", "not-comparable");
        ExpectComparison(first, "spdx30-release", "not-comparable");
        ExpectComparison(first, "spdx30-release-literal", "match");
        ExpectComparison(first, "provenance-target", "mismatch");
        ExpectComparison(first, "provenance-release", "match");
        ExpectComparison(first, "sbom-target", "mismatch");
        ExpectComparison(first, "sbom-release", "match");
        ExpectComparison(first, "sbom-document", "match");
        var verification = first.Facts.Single(item => item.Id == "spdx30.file-sha256");
        Assert(verification.Kind == "package-verification-code" && verification.Algorithm == "sha256" &&
               verification.Value == fixture.ReleaseHash &&
               verification.Source == new ReleaseSource("spdx30", Entry30, "/@graph/1/verifiedUsing/0/hashValue"),
            "source-located declared verification kind");
        var commit = first.Facts.Single(item => item.Id == "provenance.source-commit");
        Assert(commit.Value == new string('c', 40) && commit.Algorithm == "gitCommit" &&
               commit.Source.Pointer == "/predicate/buildDefinition/resolvedDependencies/0/digest/gitCommit",
            "exact source commit and pointer");
        var nonComparable = first.Comparisons.Single(item => item.Id == "spdx30-release");
        Assert(nonComparable.Left.Kind != nonComparable.Right.Kind &&
               nonComparable.Reason.Contains("Incompatible", StringComparison.Ordinal), "not-comparable retains operand types and reason");
        Assert(first.Roles.Count == 6 && first.Roles[0].ManifestPointer == "/package", "existing role references");
        Assert(long.Parse(first.Accounting.ResultBytes) == File.ReadAllBytes(fixture.Path("first.json")).Length,
            "exact result-byte accounting");
        Assert(long.Parse(first.Accounting.TotalBytes) ==
               long.Parse(first.Accounting.SelectedRawBytes) + long.Parse(first.Accounting.ExpandedBytes) +
               long.Parse(first.Accounting.DecodedBytes) + long.Parse(first.Accounting.ResultBytes),
            "collective accounting includes every expanded and decoded form and output");
        Assert(long.Parse(first.Accounting.SelectedRawBytes) ==
               fixture.InputFiles.Skip(1).Sum(path => new FileInfo(path).Length), "exact raw supplemental accounting");
        foreach (var pair in preserved) Assert(pair.Value.SequenceEqual(File.ReadAllBytes(pair.Key)), "raw bytes preserved");

        var originalOutput = File.ReadAllBytes(fixture.Path("first.json"));
        fixture.ExpectFailure("existing output", output: "first.json");
        Assert(originalOutput.SequenceEqual(File.ReadAllBytes(fixture.Path("first.json"))), "existing output preserved");
        var help = new StringWriter();
        Assert(CliApplication.Run(["release", "facts", "--help"], help, new StringWriter()) == 0 &&
               help.ToString().Contains("not-comparable", StringComparison.Ordinal), "command help");
        var usageError = new StringWriter();
        Assert(CliApplication.Run([.. fixture.Arguments("unused.json"), "--expected-sha256", new string('a', 64)],
            new StringWriter(), usageError) == 2, "no caller replacement hash");
        var environmentError = new StringWriter();
        Assert(CliApplication.Run(fixture.Arguments("missing-parent/output.json"), new StringWriter(),
            environmentError) == 3, "publication environment error retains exit 3");
        Assert(environmentError.ToString().Contains("publish=failed", StringComparison.Ordinal) &&
               !File.Exists(fixture.Path("missing-parent/output.json")), "failed publication has no result");
        var stagedCount = System.Text.RegularExpressions.Regex.Match(environmentError.ToString(), @"result_bytes=(\d+)");
        var diagnosticCount = System.Text.RegularExpressions.Regex.Match(environmentError.ToString(), @"diagnostics_bytes=(\d+)");
        Assert(stagedCount.Success && long.Parse(stagedCount.Groups[1].Value) > 0 &&
               diagnosticCount.Success && long.Parse(diagnosticCount.Groups[1].Value) ==
               Encoding.UTF8.GetByteCount(environmentError.ToString()), "staged result and fatal diagnostics both charged");

        var preInputPath = fixture.Manifest;
        var preInputBytes = File.ReadAllBytes(preInputPath);
        fixture.Candidate("add-evidence", "--path", "first.json", "--kind", "offline-release-facts");
        var postInput = fixture.Confirm();
        Assert(first.InputManifest.Sha256 != Hash(File.ReadAllBytes(postInput)), "pre/post output manifest separation");
        Assert(preInputBytes.SequenceEqual(File.ReadAllBytes(preInputPath)) &&
               originalOutput.SequenceEqual(File.ReadAllBytes(fixture.Path("first.json"))),
            "registration and explicit confirmation preserve original manifest and result");
        Cli("assessment", "init", "--kind", "package", "--root", fixture.Root, "--input", postInput,
            "--output", fixture.Path("skeleton.json"));
        Cli("assessment", "export-identity", "--assessment", fixture.Path("skeleton.json"),
            "--output", fixture.Path("identity.json"));
        var capturedAt = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'",
            System.Globalization.CultureInfo.InvariantCulture);
        Cli("evidence", "draft-add", "--output", fixture.Path("evidence-draft.json"),
            "--claim", "Mechanical facts target.sha256 and release-package.sha256 differ in comparisons target-release.",
            "--scope", "repository-wide", "--kind", "reviewer-generated-analysis",
            "--method", "Offline release facts CLI; specific fact IDs target.sha256 and release-package.sha256. No authentication.",
            "--captured-at", capturedAt,
            "--root", fixture.Root, "--manifest", postInput, "--evidence-input", "first.json");
        var draft = CanonicalEvidenceJson.ParseDraftDocument(File.ReadAllBytes(fixture.Path("evidence-draft.json")));
        Assert(draft.Records.Single().Provenance.CapturedAtUtc == capturedAt,
            "post-result capture argument preserved in typed draft");
        Cli("evidence", "ledger-build", "--kind", "repository", "--subject", fixture.Path("identity.json"),
            "--draft", fixture.Path("evidence-draft.json"), "--nupkg", fixture.Target, "--output", fixture.Path("ledger.json"));
        Cli("evidence", "ledger-validate", "--ledger", fixture.Path("ledger.json"));
        var ledger = CanonicalEvidenceJson.ParseSourceLedger(File.ReadAllBytes(fixture.Path("ledger.json")));
        Assert(ledger.Records.Single().Provenance.CapturedAtUtc == capturedAt,
            "post-result capture argument preserved in source ledger");
        using var ledgerJson = JsonDocument.Parse(File.ReadAllBytes(fixture.Path("ledger.json")));
        var id = ledgerJson.RootElement.GetProperty("records")[0].GetProperty("stable_id").GetString()!;
        Cli("evidence", "bundle", "--assessment", fixture.Path("identity.json"), "--source-ledger", fixture.Path("ledger.json"),
            "--ids", id, "--root", fixture.Root, "--manifest", postInput, "--output", fixture.Path("bundle.json"));
        var assessment = AssessmentService.Parse(File.ReadAllBytes(fixture.Path("skeleton.json")));
        var input = InputManifestService.Parse(File.ReadAllBytes(postInput));
        var bundle = CanonicalEvidenceJson.ParseBundle(File.ReadAllBytes(fixture.Path("bundle.json")));
        EvidenceInputBindingValidator.Validate(fixture.Root, assessment, input, bundle);
        Assert(bundle.Selection.Single().EvidenceId == id, "existing producer-generated EV1 consumed by bundle");
        Assert(bundle.SourceLedgers.SelectMany(source => source.Ledger.Records)
                   .Single(record => record.StableId == bundle.Selection.Single().EvidenceId)
                   .Provenance.CapturedAtUtc == capturedAt,
            "post-result capture argument preserved in selected bundle record");
        Console.WriteLine("Post-result capture argument preserved through draft, ledger and selected bundle; metadata truth is not assessed.");
        Assert(Directory.GetFiles(fixture.Root, "*report*", SearchOption.AllDirectories).Length == 0,
            "integration does not render a report");
    }

    private static void TestCompatibleAndStructural(Fixture fixture)
    {
        var graph = fixture.Spdx30["@graph"]!.AsArray();
        var verification = graph[1]!["verifiedUsing"]![0]!.DeepClone();
        verification["type"] = "Hash";
        graph[1]!["verifiedUsing"]![0] = "verification";
        verification["@id"] = "verification-alias";
        graph.Add(verification);
        fixture.WriteSpdx();
        fixture.Confirm();
        var result = fixture.Collect("compatible.json");
        ExpectComparison(result, "spdx30-release", "match");
        ExpectDocumentComparison(fixture, result, byteIdentical: true, "match");
        Assert(result.Facts.Single(item => item.Id == "spdx30.file-sha256").Source.Pointer ==
               "/@graph/2/hashValue", "unique local reference resolves with target pointer");
        Assert(result.Facts.Single(item => item.Id == "spdx30.verification-link").Source.Pointer ==
               "/@graph/1/verifiedUsing/0", "reference occurrence retained");
        File.Copy(fixture.Path(Filename), fixture.Target, overwrite: true);
        fixture.Confirm();
        ExpectComparison(fixture.Collect("same-package.json"), "target-release", "match");
        var reordered = new JsonObject(fixture.Spdx22.Reverse().Select(pair =>
            new KeyValuePair<string, JsonNode?>(pair.Key, pair.Value?.DeepClone())));
        fixture.WriteSbom(reordered);
        fixture.Confirm();
        ExpectDocumentComparison(fixture, fixture.Collect("object-order.json"), byteIdentical: false, "match");

        var altered = fixture.Spdx22.DeepClone().AsObject();
        altered["optional"] = null;
        fixture.WriteSbom(altered);
        fixture.Confirm();
        ExpectDocumentComparison(fixture, fixture.Collect("null.json"), byteIdentical: false, "mismatch");
        altered.Remove("optional");
        altered["creators"] = new JsonArray("second", "first");
        fixture.WriteSbom(altered);
        fixture.Confirm();
        ExpectDocumentComparison(fixture, fixture.Collect("order.json"), byteIdentical: false, "mismatch");
    }

    private static void ExpectDocumentComparison(
        Fixture fixture, ReleaseFactsResult result, bool byteIdentical, string outcome)
    {
        using var archive = ZipFile.OpenRead(fixture.Path("spdx22.zip"));
        using var entry = archive.GetEntry(Entry22)!.Open();
        using var document = new MemoryStream();
        entry.CopyTo(document);
        var documentBytes = document.ToArray();
        using var bundle = JsonDocument.Parse(File.ReadAllBytes(fixture.Path("sbom.json")));
        var payload = Convert.FromBase64String(bundle.RootElement.GetProperty("dsseEnvelope")
            .GetProperty("payload").GetString()!);
        using var statement = JsonDocument.Parse(payload);
        var predicateBytes = Encoding.UTF8.GetBytes(statement.RootElement.GetProperty("predicate").GetRawText());
        Assert(documentBytes.SequenceEqual(predicateBytes) == byteIdentical,
            $"actual document encodings byte-identical={byteIdentical}");
        var documentFact = result.Facts.Single(fact => fact.Id == "spdx22.document");
        var predicateFact = result.Facts.Single(fact => fact.Id == "sbom-statement.predicate");
        Assert(documentFact.Kind == "document-checksum" && predicateFact.Kind == "document-checksum" &&
               documentFact.Algorithm == "sha256" && predicateFact.Algorithm == "sha256",
            "document digest kinds and algorithms preserved");
        Assert(documentFact.Value == Hash(documentBytes) && predicateFact.Value == Hash(predicateBytes),
            "document digests identify actual selected ZIP entry and decoded predicate bytes");
        Assert((documentFact.Value == predicateFact.Value) == byteIdentical,
            $"document digest equality agrees with actual encoding equality={byteIdentical}");
        ExpectComparison(result, "sbom-document", outcome);
        var comparison = result.Comparisons.Single(item => item.Id == "sbom-document");
        Assert(comparison.Mode == "structural-json" &&
               comparison.Reason == "Compared JSON structure only; RDF equivalence was not assessed.",
            $"structural {outcome} must state exactly that RDF equivalence was not assessed; actual: {comparison.Reason}");
    }

    private static void TestFatalInputs(string root)
    {
        Fatal("json-malformed", f => File.WriteAllText(f.Path("provenance.json"), "{"), "provenance");
        Fatal("json-null", f => File.WriteAllText(f.Path("provenance.json"), "null"), "provenance");
        Fatal("json-duplicate", f => File.WriteAllText(f.Path("provenance.json"), "{\"x\":1,\"x\":2}"), "provenance");
        Fatal("json-comments", f => File.WriteAllText(f.Path("provenance.json"), "{/*comment*/}"), "provenance");
        Fatal("json-trailing", f => File.WriteAllText(f.Path("provenance.json"), "{}{}"), "provenance");
        Fatal("utf8", f => File.WriteAllBytes(f.Path("provenance.json"), [0x22, 0xc3, 0x28, 0x22]), "provenance");
        Fatal("bom", f => File.WriteAllBytes(f.Path("provenance.json"), [0xef, 0xbb, 0xbf, 0x7b, 0x7d]), "provenance");
        Fatal("base64", f => f.ChangeBundle(b => b["dsseEnvelope"]!["payload"] = "!!!!"), "provenance");
        Fatal("base64-padding", f => f.ChangeBundle(b => b["dsseEnvelope"]!["payload"] = "Zh=="), "provenance");
        Fatal("base64-whitespace", f => f.ChangeBundle(b => b["dsseEnvelope"]!["payload"] = " Zg=="), "provenance");
        Fatal("payload-utf8", f => f.ChangeBundle(b => b["dsseEnvelope"]!["payload"] =
            Convert.ToBase64String([0xff])), "provenance");
        Fatal("payload-json-duplicates", f => f.ChangeBundle(b => b["dsseEnvelope"]!["payload"] =
            Convert.ToBase64String(Encoding.UTF8.GetBytes("{\"x\":1,\"x\":2}"))), "provenance");
        Fatal("bundle-version", f => f.ChangeBundle(b => b["mediaType"] = "unknown"), "provenance");
        Fatal("payload-null", f => f.ChangeBundle(b => b["dsseEnvelope"]!["payload"] = null), "provenance");
        Fatal("payload-absent", f => f.ChangeBundle(b => b["dsseEnvelope"]!.AsObject().Remove("payload")), "provenance");
        Fatal("payload-type", f => f.ChangeBundle(b => b["dsseEnvelope"]!["payloadType"] = "text/plain"), "provenance");
        Fatal("statement-type", f => f.ChangeStatement(s => s["_type"] = "unknown"), "provenance");
        Fatal("predicate-type", f => f.ChangeStatement(s => s["predicateType"] = "unknown"), "provenance");
        Fatal("subjects-duplicate", f => f.ChangeStatement(s => s["subject"]!.AsArray().Add(s["subject"]![0]!.DeepClone())), "provenance");
        Fatal("subject-missing", f => f.ChangeStatement(s => s["subject"]![0]!["name"] = "Other.nupkg"), "provenance");
        Fatal("source-duplicate", f => f.ChangeStatement(s =>
        {
            var d = s["predicate"]!["buildDefinition"]!["resolvedDependencies"]!.AsArray();
            d.Add(d[0]!.DeepClone());
        }), "provenance");
        Fatal("digest-invalid", f => f.ChangeStatement(s => s["subject"]![0]!["digest"]!["sha256"] = "wrong"), "provenance");
        Fatal("spdx-version", f => { f.Spdx22["spdxVersion"] = "SPDX-2.3"; f.WriteSpdx(); }, "spdx22");
        Fatal("spdx-duplicate-id", f => { f.Spdx22["files"]!.AsArray().Add(f.Spdx22["files"]![0]!.DeepClone()); f.WriteSpdx(); }, "spdx22");
        Fatal("spdx-ambiguous-name", f =>
        {
            var file = f.Spdx22["files"]![0]!.DeepClone();
            file["SPDXID"] = "different";
            f.Spdx22["files"]!.AsArray().Add(file);
            f.WriteSpdx();
        }, "spdx22");
        Fatal("spdx-duplicate-digest", f =>
        {
            var c = f.Spdx22["files"]![0]!["checksums"]!.AsArray();
            c.Add(c[0]!.DeepClone()); f.WriteSpdx();
        }, "spdx22");
        Fatal("spdx-null-files", f => { f.Spdx22["files"] = null; f.WriteSpdx(); }, "spdx22");
        Fatal("spdx-missing-files", f => { f.Spdx22.Remove("files"); f.WriteSpdx(); }, "spdx22");
        Fatal("spdx-unresolved", f => { f.Spdx22["documentDescribes"]![0] = "missing"; f.WriteSpdx(); }, "spdx22");
        Fatal("spdx-duplicate-description", f =>
        {
            f.Spdx22["documentDescribes"]!.AsArray().Add("package"); f.WriteSpdx();
        }, "spdx22");
        Fatal("context-unsupported", f => { f.Spdx30["@context"] = new JsonArray("https://example.test/context"); f.WriteSpdx(); }, "spdx30");
        Fatal("graph-duplicate", f =>
        {
            var g = f.Spdx30["@graph"]!.AsArray(); g.Add(g[1]!.DeepClone()); f.WriteSpdx();
        }, "spdx30");
        Fatal("graph-alias-collision", f =>
        {
            f.Spdx30["@graph"]![1]!["@id"] = "document"; f.WriteSpdx();
        }, "spdx30");
        Fatal("graph-reference-missing", f => { f.Spdx30["@graph"]![1]!["verifiedUsing"]![0] = "missing"; f.WriteSpdx(); }, "spdx30");
        Fatal("graph-reference-ambiguous", f =>
        {
            var g = f.Spdx30["@graph"]!.AsArray();
            var v = g[1]!["verifiedUsing"]![0]!.DeepClone();
            g[1]!["verifiedUsing"]![0] = "verification";
            g.Add(v.DeepClone());
            g[0]!["unrelatedInline"] = v;
            f.WriteSpdx();
        }, "spdx30");
        Fatal("verification-type", f => { f.Spdx30["@graph"]![1]!["verifiedUsing"]![0]!["type"] = "Unknown"; f.WriteSpdx(); }, "spdx30");
        Fatal("verification-duplicate", f =>
        {
            var v = f.Spdx30["@graph"]![1]!["verifiedUsing"]!.AsArray();
            v.Add(v[0]!.DeepClone()); f.WriteSpdx();
        }, "spdx30");
        Fatal("verification-null", f => { f.Spdx30["@graph"]![1]!["verifiedUsing"] = null; f.WriteSpdx(); }, "spdx30");
        Fatal("sbom-predicate-ambiguity", f =>
        {
            var p = f.Spdx22.DeepClone();
            p["files"]!.AsArray().Add(p["files"]![0]!.DeepClone());
            f.WriteSbom(p);
        }, "sbom-statement");
        var changed = new Fixture(Path.Combine(root, "changed"));
        changed.Confirm();
        File.AppendAllText(changed.Path("provenance.json"), " ");
        changed.ExpectFailure("digest/size changed", failedOperation: "validate-inputs");
        var missing = new Fixture(Path.Combine(root, "missing"));
        missing.Confirm();
        File.Delete(missing.Path("spdx30.zip"));
        missing.ExpectFailure("missing registered input", failedOperation: "validate-inputs");
        var ambiguous = new Fixture(Path.Combine(root, "roles"));
        ambiguous.Candidate("add-owner-input", "--path", "spdx22.zip", "--provenance", "owner-supplied-public-evidence");
        // Existing input validation may reject the cross-collection duplicate even before role binding.
        var duplicateManifest = CliApplication.Run(["inputs", "discover", "--root", ambiguous.Root,
            "--nupkg", ambiguous.Target, "--candidates", ambiguous.Candidates, "--output", ambiguous.Path("duplicate-draft.json")],
            new StringWriter(), new StringWriter());
        if (duplicateManifest == 0)
        {
            ambiguous.Confirm();
            ambiguous.ExpectFailure("ambiguous registration", failedOperation: "bind-roles");
        }
        else Assert(duplicateManifest == 1, "ambiguous registry is validation failure");
        var roles = new Fixture(Path.Combine(root, "role-reuse"));
        roles.Confirm();
        roles.ExpectFailure("role reused", arguments: Replace(roles.Arguments("failed.json"), "--spdx30", "spdx22.zip"),
            failedOperation: "bind-roles");
        roles.ExpectFailure("unsafe role", arguments: Replace(roles.Arguments("unsafe.json"), "--spdx30", "../spdx30.zip"),
            failedOperation: "bind-roles", output: "unsafe.json");
        roles.ExpectFailure("outside output", arguments: Replace(roles.Arguments("outside.json"), "--output", "../outside.json"),
            output: "outside.json", failedOperation: "validate-inputs");
        roles.ExpectFailure("unregistered role", arguments: Replace(roles.Arguments("unregistered.json"), "--spdx30", "other.zip"),
            output: "unregistered.json", failedOperation: "bind-roles");
        if (!OperatingSystem.IsWindows())
        {
            File.Move(roles.Path("spdx30.zip"), roles.Path("actual-spdx30.zip"));
            File.CreateSymbolicLink(roles.Path("spdx30.zip"), roles.Path("actual-spdx30.zip"));
            roles.ExpectFailure("input symlink", output: "symlink.json", failedOperation: "validate-inputs");
        }

        void Fatal(string name, Action<Fixture> mutate, string operation)
        {
            var fixture = new Fixture(Path.Combine(root, name));
            mutate(fixture);
            fixture.Confirm();
            fixture.ExpectFailure(name, failedOperation: operation);
        }
    }

    private static void TestArchiveAndLimits(string root)
    {
        foreach (var name in new[] { "../escape", "/absolute", "C:/drive", "a\\b", "a//b", "a/./b" })
        {
            var fixture = new Fixture(Path.Combine(root, $"unsafe-zip-{Guid.NewGuid():N}"));
            fixture.Zip22((name, [1], 0));
            fixture.Confirm();
            fixture.ExpectFailure("unsafe ZIP path", failedOperation: "spdx22");
        }

        foreach (var extra in new[]
                 {
                     new[] { (Entry22, new byte[] { 1 }, 0) },
                     new[] { ("link", new byte[] { 1 }, 0xA000 << 16) },
                     new[] { ("device", new byte[] { 1 }, 0x2000 << 16) },
                     new[] { ("file", new byte[] { 1 }, 0), ("file/child", new byte[] { 1 }, 0) }
                 })
        {
            var fixture = new Fixture(Path.Combine(root, $"bad-zip-{Guid.NewGuid():N}"));
            fixture.Zip22(extra);
            fixture.Confirm();
            fixture.ExpectFailure("duplicate/link/collision", failedOperation: "spdx22");
        }

        var absent = new Fixture(Path.Combine(root, "absent-entry"));
        absent.Confirm();
        absent.ExpectFailure("exact entry required",
            arguments: Replace(absent.Arguments("failed.json"), "--spdx22-entry", "SPDX_2.2/manifest.spdx.json"),
            failedOperation: "spdx22");
        var malformedZip = new Fixture(Path.Combine(root, "malformed-zip"));
        File.WriteAllText(malformedZip.Path("spdx22.zip"), "not a ZIP");
        malformedZip.Confirm();
        malformedZip.ExpectFailure("malformed ZIP", failedOperation: "spdx22");
        var dishonestLength = new Fixture(Path.Combine(root, "zip-length"));
        var archiveBytes = File.ReadAllBytes(dishonestLength.Path("spdx30.zip"));
        var central = archiveBytes.AsSpan().IndexOf(new byte[] { 0x50, 0x4b, 0x01, 0x02 });
        Assert(central >= 0, "synthetic ZIP central entry exists");
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(archiveBytes.AsSpan(central + 24, 4), 0);
        File.WriteAllBytes(dishonestLength.Path("spdx30.zip"), archiveBytes);
        dishonestLength.Confirm();
        dishonestLength.ExpectFailure("declared versus actual ZIP length", failedOperation: "spdx30");
        var depth = new Fixture(Path.Combine(root, "depth"));
        depth.Zip22Raw(Encoding.UTF8.GetBytes(new string('[', 65) + "0" + new string(']', 65)));
        depth.Confirm();
        depth.ExpectFailure("JSON depth", failedOperation: "spdx22");
        var count = new Fixture(Path.Combine(root, "json-count"));
        count.Zip22Raw(Encoding.UTF8.GetBytes("[" + string.Join(',', Enumerable.Repeat("0", 100001)) + "]"));
        count.Confirm();
        count.ExpectFailure("JSON token count", failedOperation: "spdx22");
        var entries = new Fixture(Path.Combine(root, "entry-count"));
        using (var stream = File.Create(entries.Path("spdx30.zip")))
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            for (var i = 0; i <= ResourceLimits.SourceArchiveEntryCount; i++) zip.CreateEntry($"entry-{i}");
        }
        entries.Confirm();
        entries.ExpectFailure("ZIP entry count", failedOperation: "spdx30");
        var expanded = new Fixture(Path.Combine(root, "expanded-total"));
        expanded.Zip22(("padding", new byte[33 * 1024 * 1024], 0));
        using (var zip = ZipFile.Open(expanded.Path("spdx30.zip"), ZipArchiveMode.Update))
        using (var content = zip.CreateEntry("padding").Open())
        {
            content.Write(new byte[33 * 1024 * 1024]);
        }
        expanded.Confirm();
        expanded.ExpectFailure("collective expanded allowance across ZIPs", failedOperation: "spdx30");
        var targetLimit = new Fixture(Path.Combine(root, "target-limit"));
        targetLimit.Confirm();
        using (var stream = File.OpenWrite(targetLimit.Target)) stream.SetLength(ResourceLimits.NupkgBytes + 1);
        targetLimit.ExpectFailure("existing target byte limit", failedOperation: "validate-inputs");
        var rawLimit = new Fixture(Path.Combine(root, "raw-limit"));
        using (var stream = File.OpenWrite(rawLimit.Path("provenance.json")))
            stream.SetLength(ResourceLimits.SupplementalInputAggregateBytes);
        var error = new StringWriter();
        Assert(CliApplication.Run(["inputs", "discover", "--root", rawLimit.Root, "--nupkg", rawLimit.Target,
            "--candidates", rawLimit.Candidates, "--output", rawLimit.Path("over-limit.json")],
            new StringWriter(), error) == 1, "existing registered raw aggregate allowance");
    }

    private static void ExpectComparison(ReleaseFactsResult result, string id, string expected) =>
        Assert(result.Comparisons.Single(item => item.Id == id).Outcome == expected, id + " outcome");

    private static string[] Replace(string[] args, string option, string replacement)
    {
        var copy = args.ToArray();
        copy[System.Array.IndexOf(copy, option) + 1] = replacement;
        return copy;
    }

    private static void Cli(params string[] args)
    {
        var error = new StringWriter();
        var result = CliApplication.Run(args, new StringWriter(), error);
        Assert(result == 0, $"{string.Join(' ', args.Take(3))}: exit {result}: {error}");
    }

    private static string Hash(byte[] bytes) => ContractJson.RawDigest(bytes).Value;
    private static void Assert(bool condition, string description)
    {
        assertions++;
        if (!condition) throw new InvalidOperationException("Release facts: " + description);
    }

    private sealed class Fixture
    {
        private int sequence;
        public string Root { get; }
        public string Target => Path("target/" + Filename);
        public string Candidates { get; private set; } = "";
        public string Manifest { get; private set; } = "";
        public string ReleaseHash { get; }
        public JsonObject Spdx22 { get; }
        public JsonObject Spdx30 { get; }
        public string[] InputFiles => [Target, Path(Filename), Path("spdx22.zip"), Path("spdx30.zip"),
            Path("provenance.json"), Path("sbom.json")];

        public Fixture(string root)
        {
            Root = root;
            Directory.CreateDirectory(Path("target"));
            Package(Target, "target bytes");
            Package(Path(Filename), "release bytes");
            ReleaseHash = Hash(File.ReadAllBytes(Path(Filename)));
            Spdx22 = JsonNode.Parse($$$"""
                {"spdxVersion":"SPDX-2.2","SPDXID":"document","name":"Sample document",
                 "documentNamespace":"https://example.test/sbom","documentDescribes":["package"],
                 "creators":["first","second"],
                 "packages":[{"SPDXID":"package","name":"Sample.Widgets","versionInfo":"1.2.3",
                   "hasFiles":["file"],"packageVerificationCode":{"packageVerificationCodeValue":"{{{new string('a', 40)}}}"}}],
                 "files":[{"SPDXID":"file","fileName":"./{{{Filename}}}","checksums":[
                   {"algorithm":"SHA256","checksumValue":"{{{ReleaseHash}}}"}]}]}
                """)!.AsObject();
            Spdx30 = JsonNode.Parse($$$"""
                {"@context":["https://spdx.org/rdf/3.0.1/spdx-context.json"],"@graph":[
                 {"spdxId":"document","name":"Sample document","type":"SpdxDocument"},
                 {"spdxId":"file","name":"./{{{Filename}}}","type":"software_File","verifiedUsing":[
                   {"spdxId":"verification","type":"PackageVerificationCode","algorithm":"sha256","hashValue":"{{{ReleaseHash}}}"}]}]}
                """)!.AsObject();
            WriteSpdx();
            var predicate = JsonNode.Parse($$$"""
                {"buildDefinition":{"buildType":"https://actions.github.io/buildtypes/workflow/v1",
                 "externalParameters":{"workflow":{"repository":"https://example.test/repo","ref":"refs/tags/v1","path":".github/workflows/release.yml"}},
                 "resolvedDependencies":[{"uri":"git+https://example.test/repo@refs/tags/v1","digest":{"gitCommit":"{{{new string('c', 40)}}}"}}]}}
                """)!;
            WriteBundle(Path("provenance.json"), Statement("https://slsa.dev/provenance/v1", predicate));
            WriteSbom(Spdx22);
            Candidate("init", "--acquisition", "release-candidate", "--package-locator", "target/" + Filename,
                "--package-method", "local-file", "--source-availability", "unresolved");
            Candidate("add-retrieval", "--subject", "package", "--locator", "target/" + Filename,
                "--method", "local-file", "--result", "succeeded");
            foreach (var name in InputFiles.Skip(1))
            {
                Candidate("add-evidence", "--path", System.IO.Path.GetFileName(name), "--kind", "retained-release");
            }
        }

        public string Path(string relative) => System.IO.Path.Combine(Root, relative);
        public void Candidate(string command, params string[] args)
        {
            var next = Path($"candidate-{sequence++}.json");
            Cli(["inputs", "candidates", command,
                .. (command == "init" ? System.Array.Empty<string>() : new[] { "--input", Candidates }),
                .. args, "--output", next]);
            Candidates = next;
        }

        public string Confirm()
        {
            var draft = Path($"draft-{sequence++}.json");
            var confirmed = Path($"input-{sequence++}.json");
            Cli("inputs", "discover", "--root", Root, "--nupkg", Target, "--candidates", Candidates, "--output", draft);
            Cli("inputs", "confirm", "--root", Root, "--draft", draft, "--output", confirmed);
            Manifest = confirmed;
            return confirmed;
        }

        public string[] Arguments(string output) =>
        [
            "release", "facts", "--root", Root, "--input", Manifest, "--release-package", Filename,
            "--spdx22", "spdx22.zip", "--spdx22-entry", Entry22, "--spdx30", "spdx30.zip",
            "--spdx30-entry", Entry30, "--provenance", "provenance.json", "--sbom-statement", "sbom.json", "--output", output
        ];

        public ReleaseFactsResult Collect(string output)
        {
            Cli(Arguments(output));
            // Exercise the typed collector too, without manufacturing expected output from it.
            var result = ReleaseFactsService.Collect(Root, Manifest, Filename, "spdx22.zip", Entry22,
                "spdx30.zip", Entry30, "provenance.json", "sbom.json", "typed-" + output);
            var actual = JsonNode.Parse(File.ReadAllBytes(Path(output)))!;
            foreach (var field in new[] { "started_at_utc", "completed_at_utc", "duration_ms", "root" })
                actual["execution"]![field] = null;
            Assert(JsonNode.DeepEquals(actual, JsonNode.Parse(result.NormalizedProjection())),
                "CLI and typed normalized projection agree");
            return result;
        }

        public void ExpectFailure(string name, string output = "failed.json", string[]? arguments = null,
            string? failedOperation = null)
        {
            var existed = File.Exists(Path(output));
            var error = new StringWriter();
            Assert(CliApplication.Run(arguments ?? Arguments(output), new StringWriter(), error) == 1,
                $"{name} must be validation exit 1: {error}");
            Assert(existed || !File.Exists(Path(output)), name + " publishes no successful output");
            Assert(error.ToString().Contains("No result published.", StringComparison.Ordinal), name + " explicit failure");
            var diagnosticCount = System.Text.RegularExpressions.Regex.Match(error.ToString(), @"diagnostics_bytes=(\d+)");
            Assert(diagnosticCount.Success &&
                   long.Parse(diagnosticCount.Groups[1].Value) == Encoding.UTF8.GetByteCount(error.ToString()),
                name + " exact fatal diagnostic byte accounting");
            if (failedOperation is not null)
            {
                Assert(error.ToString().Contains(failedOperation + "=failed", StringComparison.Ordinal),
                    name + " failed operation");
                Assert(error.ToString().Contains("publish=not-attempted", StringComparison.Ordinal),
                    name + " publish not attempted");
            }
        }

        public void WriteSpdx()
        {
            Zip22();
            WriteZip(Path("spdx30.zip"), (Entry30, Encoding.UTF8.GetBytes(Spdx30.ToJsonString()), 0));
        }

        public void Zip22(params (string Name, byte[] Bytes, int Attributes)[] extra) =>
            WriteZip(Path("spdx22.zip"), [(Entry22, Encoding.UTF8.GetBytes(Spdx22.ToJsonString()), 0), .. extra]);
        public void Zip22Raw(byte[] bytes) => WriteZip(Path("spdx22.zip"), (Entry22, bytes, 0));
        public void WriteSbom(JsonNode predicate) => WriteBundle(Path("sbom.json"), Statement("https://spdx.dev/Document/v2.2", predicate));
        public void ChangeBundle(Action<JsonObject> mutate)
        {
            var value = JsonNode.Parse(File.ReadAllBytes(Path("provenance.json")))!.AsObject();
            mutate(value);
            File.WriteAllText(Path("provenance.json"), value.ToJsonString());
        }

        public void ChangeStatement(Action<JsonObject> mutate) => ChangeBundle(bundle =>
        {
            var decoded = Convert.FromBase64String(bundle["dsseEnvelope"]!["payload"]!.GetValue<string>());
            var statement = JsonNode.Parse(decoded)!.AsObject();
            mutate(statement);
            bundle["dsseEnvelope"]!["payload"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(statement.ToJsonString()));
        });

        private JsonObject Statement(string type, JsonNode predicate) => new()
        {
            ["_type"] = "https://in-toto.io/Statement/v1",
            ["subject"] = new JsonArray(new JsonObject
            {
                ["name"] = Filename, ["digest"] = new JsonObject { ["sha256"] = ReleaseHash }
            }),
            ["predicateType"] = type, ["predicate"] = predicate.DeepClone()
        };

        private static void WriteBundle(string path, JsonNode statement) => File.WriteAllText(path,
            new JsonObject
            {
                ["mediaType"] = "application/vnd.dev.sigstore.bundle.v0.3+json",
                ["verificationMaterial"] = new JsonObject(),
                ["dsseEnvelope"] = new JsonObject
                {
                    ["payloadType"] = "application/vnd.in-toto+json",
                    ["payload"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(statement.ToJsonString())),
                    ["signatures"] = new JsonArray(new JsonObject { ["sig"] = "AA==" })
                }
            }.ToJsonString());

        private static void Package(string path, string content) => WriteZip(path,
            ("Sample.Widgets.nuspec", Encoding.UTF8.GetBytes(
                "<package><metadata><id>Sample.Widgets</id><version>1.2.3</version></metadata></package>"), 0),
            ("content.txt", Encoding.UTF8.GetBytes(content), 0));

        private static void WriteZip(string path, params (string Name, byte[] Bytes, int Attributes)[] entries)
        {
            using var stream = File.Create(path);
            using var zip = new ZipArchive(stream, ZipArchiveMode.Create);
            foreach (var (name, bytes, attributes) in entries)
            {
                var entry = zip.CreateEntry(name);
                entry.LastWriteTime = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
                entry.ExternalAttributes = attributes;
                using var content = entry.Open();
                content.Write(bytes);
            }
        }
    }
}
