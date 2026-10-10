using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using BlazorComponentReadiness.Validator.Contracts;
using BlazorComponentReadiness.Validator.Inputs;
using BlazorComponentReadiness.Validator.IO;
using BlazorComponentReadiness.Validator.Validation;

namespace BlazorComponentReadiness.Validator.Release;

public sealed partial class ReleaseFactsService
{
    internal const int JsonNodeLimit = 100_000;
    private const long DiagnosticReserve = 16 * 1024;
    private static readonly string[] OperationIds =
    [
        "validate-inputs", "bind-roles", "target", "release-package", "spdx22",
        "spdx30", "provenance", "sbom-statement", "compare", "publish"
    ];
    private readonly List<ReleaseFact> facts = [];
    private readonly List<ReleaseComparison> comparisons = [];
    private readonly List<ReleaseRole> roles = [];
    private readonly List<ReleaseOperation> operations =
        OperationIds.Select(id => new ReleaseOperation(id, "not-attempted", "Not reached.")).ToList();
    private readonly List<string> diagnostics =
    [
        "Offline literal facts only; no signature authentication, remote context resolution or publication freshness.",
        "Inline SPDX3 verification records are occurrence-local; repeated declared IDs are not merged.",
        "Internal comparisons are not readiness statuses or partner obligations."
    ];
    private long rawBytes;
    private long expandedBytes;
    private long decodedBytes;
    private long stagedResultBytes;
    private int currentOperation;

    private ReleaseFactsService() { }

    public static ReleaseFactsResult Collect(
        string root, string input, string releasePackage, string spdx22, string spdx22Entry,
        string spdx30, string spdx30Entry, string provenance, string sbomStatement, string output) =>
        new ReleaseFactsService().Run(root, input, releasePackage, spdx22, spdx22Entry,
            spdx30, spdx30Entry, provenance, sbomStatement, output);

    private ReleaseFactsResult Run(
        string root, string input, string releasePackage, string spdx22, string spdx22Entry,
        string spdx30, string spdx30Entry, string provenance, string sbomStatement, string output)
    {
        var started = DateTimeOffset.UtcNow;
        var timer = Stopwatch.StartNew();
        try
        {
            root = Path.GetFullPath(root);
            var inputPath = UnderRoot(root, input, existing: true);
            var outputPath = UnderRoot(root, output, existing: false);
            if (File.Exists(outputPath) || Directory.Exists(outputPath))
            {
                throw new DeterministicValidationException("Release facts output must be a new file.");
            }

            var manifestBytes = BoundedIO.ReadAllBytes(
                inputPath, ResourceLimits.SerializedArtifactBytes, "confirmed input manifest");
            var manifest = InputManifestService.Parse(manifestBytes);
            InputManifestService.Validate(manifest, root, requireConfirmed: true);
            Succeeded("All confirmed input identities, digests, sizes and paths validated.");
            var selections = new[]
            {
                ("release-package", releasePackage), ("spdx22", spdx22), ("spdx30", spdx30),
                ("provenance", provenance), ("sbom-statement", sbomStatement)
            };
            var inputs = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            var selectedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                manifest.Package.NupkgPath
            };
            foreach (var (role, basename) in selections)
            {
                _ = Canonicalization.Basename(basename, "release role basename");
                if (!selectedPaths.Add(basename))
                {
                    throw new DeterministicValidationException("Release roles must select distinct input paths.");
                }

                var matches = manifest.EvidenceInputs.Select((item, index) =>
                        new ReleaseRole(role, $"/evidence_inputs/{index}", item.Basename,
                            item.Size, item.ContentDigest.Value))
                    .Concat(manifest.OwnerInputs.Select((item, index) =>
                        new ReleaseRole(role, $"/owner_inputs/{index}", item.Basename,
                            item.Size, item.ContentDigest.Value)))
                    .Where(item => item.Path == basename).ToArray();
                if (matches.Length != 1)
                {
                    throw new DeterministicValidationException(
                        $"Release role '{role}' must select exactly one registered input.");
                }

                var binding = matches[0];
                var bytes = BoundedIO.ReadAllBytes(UnderRoot(root, basename, true), Remaining(), role);
                if (bytes.LongLength != binding.Size || Hash(bytes) != binding.Sha256)
                {
                    throw new DeterministicValidationException($"Registered bytes changed for role '{role}'.");
                }

                Charge(ref rawBytes, bytes.LongLength);
                inputs.Add(role, bytes);
                roles.Add(binding);
            }

            Succeeded("Five distinct supplemental roles resolved to exact registered bytes.");
            var targetPath = UnderRoot(root, manifest.Package.NupkgPath, true);
            var targetBytes = BoundedIO.ReadAllBytes(targetPath, ResourceLimits.NupkgBytes, "target nupkg");
            if (Hash(targetBytes) != manifest.Package.NupkgDigest.Value)
            {
                throw new DeterministicValidationException("Target nupkg bytes changed after input validation.");
            }

            roles.Insert(0, new("target", "/package", manifest.Package.NupkgPath,
                targetBytes.LongLength, Hash(targetBytes)));
            InspectPackage("target", targetPath, targetBytes, supplemental: false);
            Succeeded("Target nupkg inspected and archive bounds enforced.");
            InspectPackage("release-package", UnderRoot(root, releasePackage, true),
                inputs["release-package"], supplemental: true);
            Succeeded("Release nupkg inspected and expanded bytes charged.");
            var filename = Path.GetFileName(manifest.Package.NupkgPath);
            var spdx22Bytes = ReadArchive(inputs["spdx22"], spdx22Entry, supplemental: true);
            using var document22 = Parse(spdx22Bytes, "SPDX2.2");
            ReadSpdx22(document22.RootElement, filename, spdx22Entry);
            Add("spdx22.document", "document-checksum", "sha256", Hash(spdx22Bytes),
                "spdx22", spdx22Entry, "");
            Succeeded("Selected SPDX2.2 document, described package and exact package file resolved.");
            var spdx30Bytes = ReadArchive(inputs["spdx30"], spdx30Entry, supplemental: true);
            using var document30 = Parse(spdx30Bytes, "SPDX3");
            ReadSpdx30(document30.RootElement, filename, spdx30Entry);
            Succeeded("Selected SPDX3 literal graph and typed file verification resolved locally.");
            using var provenanceDocument = ReadStatement(inputs["provenance"], "provenance",
                "https://slsa.dev/provenance/v1", filename);
            ReadProvenance(provenanceDocument.RootElement);
            Succeeded("Provenance statement decoded; subject, source and workflow declarations captured.");
            using var sbomDocument = ReadStatement(inputs["sbom-statement"], "sbom-statement",
                "https://spdx.dev/Document/v2.2", filename);
            var predicate = Object(sbomDocument.RootElement, "predicate");
            ReadSpdx22(predicate, filename, "dsseEnvelope.payload", "sbom-statement", "/predicate");
            // The digest identifies the literal predicate encoding, not structural equality.
            var predicateBytes = Encoding.UTF8.GetBytes(predicate.GetRawText());
            Charge(ref decodedBytes, predicateBytes.LongLength);
            Add("sbom-statement.predicate", "document-checksum", "sha256", Hash(predicateBytes),
                "sbom-statement", "dsseEnvelope.payload", "/predicate");
            Succeeded("SBOM statement decoded; subject and embedded document captured.");
            Compare("target-release", "target.sha256", "release-package.sha256");
            foreach (var (prefix, fact) in new[]
                     {
                         ("spdx22", "spdx22.file-sha256"), ("spdx30", "spdx30.file-sha256"),
                         ("provenance", "provenance.subject-sha256"),
                         ("sbom", "sbom-statement.subject-sha256")
                     })
            {
                Compare($"{prefix}-target", fact, "target.sha256");
                Compare($"{prefix}-release", fact, "release-package.sha256");
                if (prefix == "spdx30")
                {
                    Compare("spdx30-release-literal", fact, "release-package.sha256", literal: true);
                }
            }

            comparisons.Add(new("sbom-document", "structural-json", Fact("spdx22.document"),
                Fact("sbom-statement.predicate"),
                JsonElement.DeepEquals(document22.RootElement, predicate) ? "match" : "mismatch",
                "Compared JSON structure only; RDF equivalence was not assessed."));
            Succeeded("All eleven comparisons executed; incompatible kinds are explicitly not-comparable.");
            var result = new ReleaseFactsResult(1, "release-facts/1.0.0", 1,
                new(Path.GetRelativePath(root, inputPath).Replace('\\', '/'), Hash(manifestBytes)),
                roles, facts, comparisons, operations, diagnostics, Accounting(0),
                new(Clock(started), Clock(DateTimeOffset.UtcNow),
                    timer.ElapsedMilliseconds.ToString("D12", CultureInfo.InvariantCulture), root));
            operations[currentOperation] = new("publish", "succeeded", "Complete result published atomically to a new file.");
            var bytesWithPlaceholder = result.Serialize();
            result = result with { Accounting = Accounting(bytesWithPlaceholder.LongLength) };
            var resultBytes = result.Serialize();
            Require(resultBytes.Length == bytesWithPlaceholder.Length, "Result byte accounting did not converge.");
            BoundedIO.EnsureLength(Used + resultBytes.LongLength + DiagnosticReserve,
                ResourceLimits.SupplementalInputAggregateBytes, "release producer collective allowance");
            stagedResultBytes = resultBytes.LongLength;
            AtomicFile.WriteNew(root, Path.GetRelativePath(root, outputPath), resultBytes);
            return result;
        }
        catch (Exception exception) when (exception is DeterministicValidationException or
                   InvalidDataException or FileNotFoundException)
        {
            throw new DeterministicValidationException(Failure(exception.Message, "validation error: "), exception);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new IOException(Failure(exception.Message, "environment error: "), exception);
        }
    }

    private void InspectPackage(string role, string path, byte[] bytes, bool supplemental)
    {
        _ = ReadArchive(bytes, selectedEntry: null, supplemental);
        var identity = NupkgInspector.Inspect(path);
        Require(identity.NupkgSha256 == Hash(bytes) && identity.NupkgSize == bytes.LongLength,
            $"Package '{role}' changed during inspection.");
        Add($"{role}.sha256", "package-file-checksum", "sha256", identity.NupkgSha256, role, "raw", "");
        Add($"{role}.package-id", "package-name", null, identity.Id, role,
            $"nuspec:{identity.NuspecEntry}", "/package/metadata/id");
        Add($"{role}.package-version", "package-version", null, identity.Version, role,
            $"nuspec:{identity.NuspecEntry}", "/package/metadata/version");
    }

    private byte[] ReadArchive(byte[] bytes, string? selectedEntry, bool supplemental)
    {
        if (selectedEntry is not null)
        {
            ValidateEntry(selectedEntry, 0);
            Require(!selectedEntry.EndsWith('/'), "Selected ZIP entry must be a file.");
        }

        using var stream = new MemoryStream(bytes, writable: false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        Require(archive.Entries.Count <= ResourceLimits.SourceArchiveEntryCount, "Archive entry count exceeds 100000.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var fileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in archive.Entries)
        {
            ValidateEntry(entry.FullName, entry.ExternalAttributes);
            Require(names.Add(entry.FullName.TrimEnd('/')), "Archive contains duplicate or conflicting entry names.");
            if (!entry.FullName.EndsWith('/')) fileNames.Add(entry.FullName);
        }

        foreach (var name in names)
        {
            var prefix = name;
            while (prefix.Contains('/'))
            {
                prefix = prefix[..prefix.LastIndexOf('/')];
                Require(!fileNames.Contains(prefix),
                    "Archive file/directory path collision.");
            }
        }

        byte[]? selected = null;
        long expanded = 0;
        var buffer = new byte[64 * 1024];
        foreach (var entry in archive.Entries)
        {
            var remaining = supplemental ? Remaining() : ResourceLimits.NupkgBytes - expanded;
            BoundedIO.EnsureLength(entry.Length, remaining, "expanded ZIP entry");
            using var contents = entry.Open();
            using var capture = entry.FullName == selectedEntry ? new MemoryStream() : null;
            long actual = 0;
            while (true)
            {
                var count = contents.Read(buffer, 0, buffer.Length);
                if (count == 0) break;
                actual = checked(actual + count);
                expanded = checked(expanded + count);
                if (supplemental) Charge(ref expandedBytes, count);
                else BoundedIO.EnsureLength(expanded, ResourceLimits.NupkgBytes, "expanded target nupkg");
                capture?.Write(buffer, 0, count);
            }

            Require(actual == entry.Length && (!entry.FullName.EndsWith('/') || actual == 0),
                "ZIP expanded length differs from its declared length.");
            if (capture is not null) selected = capture.ToArray();
        }

        Require(selectedEntry is null || selected is not null, "Selected ZIP entry is absent.");
        return selected ?? [];
    }

    private static void ValidateEntry(string name, int attributes)
    {
        Require(!string.IsNullOrWhiteSpace(name), "Archive entry name is empty.");
        var type = (attributes >> 16) & 0xF000;
        Require(type is 0 or 0x8000 or 0x4000, "Archive entry is not a regular file or directory.");
        Require(!name.Contains(':') && !name.Any(char.IsControl), "Archive path contains unsafe characters.");
        SafePath.ValidateArchiveEntry(name, isSymbolicLink: type == 0xA000);
    }

    private JsonDocument Parse(byte[] bytes, string resource)
    {
        // Count before creating the document and its duplicate-property traversal.
        try
        {
            var reader = new Utf8JsonReader(bytes, new JsonReaderOptions { MaxDepth = 64 });
            var count = 0;
            while (reader.Read())
            {
                Require(++count <= JsonNodeLimit, $"{resource} exceeds the 100000 JSON token bound.");
            }
        }
        catch (JsonException exception)
        {
            throw new DeterministicValidationException($"{resource} is malformed JSON.", exception);
        }

        return StrictJson.Parse(bytes, ResourceLimits.SupplementalInputAggregateBytes, resource);
    }

    private void Compare(string id, string leftId, string rightId, bool literal = false)
    {
        var left = Fact(leftId);
        var right = Fact(rightId);
        var compatible = left.Kind == right.Kind &&
            string.Equals(left.Algorithm, right.Algorithm, StringComparison.OrdinalIgnoreCase);
        var outcome = !literal && !compatible ? "not-comparable" :
            string.Equals(left.Value, right.Value, StringComparison.OrdinalIgnoreCase) ? "match" : "mismatch";
        comparisons.Add(new(id, literal ? "literal-value" : "identity", left, right, outcome,
            literal ? "Literal hash-value equality only; does not equate identity kinds or authenticate bytes." :
            !compatible ? $"Incompatible declared identity kinds/algorithms: {left.Kind}/{left.Algorithm} versus {right.Kind}/{right.Algorithm}." :
            "Compatible package-file SHA256 values compared; declarations are not authenticated."));
    }

    private ReleaseFact Fact(string id) => facts.Single(fact => fact.Id == id);

    private void Add(string id, string kind, string? algorithm, string value,
        string role, string container, string pointer) =>
        facts.Add(new(id, kind, algorithm, value, new(role, container, pointer)));

    private void Succeeded(string detail)
    {
        operations[currentOperation] = new(OperationIds[currentOperation], "succeeded", detail);
        currentOperation++;
    }

    private string Failure(string message, string prefix)
    {
        var safeMessage = new string(message.Take(2048).Select(c => char.IsControl(c) ? ' ' : c).ToArray());
        operations[currentOperation] = new(OperationIds[currentOperation], "failed", safeMessage);
        var trace = string.Join("; ", operations.Select(operation => $"{operation.Id}={operation.Outcome}"));
        string Format(long count) =>
            $"release facts: {safeMessage} Operations: {trace}. No result published. " +
            $"Accounting: selected_raw_bytes={Number(rawBytes)}, expanded_bytes={Number(expandedBytes)}, " +
            $"decoded_bytes={Number(decodedBytes)}, result_bytes={Number(stagedResultBytes)}, " +
            $"diagnostics_bytes={Number(count)}, total_bytes={Number(Used + stagedResultBytes + count)}.";
        var count = Encoding.UTF8.GetByteCount(prefix + Format(0) + Environment.NewLine);
        var diagnostic = Format(count);
        BoundedIO.EnsureLength(Used + stagedResultBytes + count,
            ResourceLimits.SupplementalInputAggregateBytes, "release producer diagnostics allowance");
        return diagnostic;
    }

    private long Used => checked(rawBytes + expandedBytes + decodedBytes);
    private long Remaining() => ResourceLimits.SupplementalInputAggregateBytes - DiagnosticReserve - Used;
    private void Charge(ref long category, long bytes)
    {
        BoundedIO.EnsureLength(bytes, Remaining(), "release producer collective allowance");
        category = checked(category + bytes);
    }

    private ReleaseAccounting Accounting(long resultBytes) => new(
        Number(ResourceLimits.SupplementalInputAggregateBytes), Number(rawBytes), Number(expandedBytes),
        Number(decodedBytes), Number(resultBytes), Number(0), Number(Used + resultBytes));
    private static string Number(long value) => value.ToString("D12", CultureInfo.InvariantCulture);
    private static string Clock(DateTimeOffset value) => value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture);
    private static string Hash(byte[] bytes) => ContractJson.RawDigest(bytes).Value;
    private static string UnderRoot(string root, string path, bool existing) =>
        SafePath.ResolveUnderRoot(root,
            Canonicalization.RelativePath(Path.IsPathRooted(path) ? Path.GetRelativePath(root, path) : path,
                "release path"), requireExisting: existing, requireFile: existing);
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new DeterministicValidationException(message);
    }

    private static JsonElement Object(JsonElement parent, string name)
    {
        Require(parent.ValueKind == JsonValueKind.Object, $"Expected object containing '{name}'.");
        return ContractJson.Object(parent, name);
    }

    private static JsonElement Array(JsonElement parent, string name)
    {
        Require(parent.ValueKind == JsonValueKind.Object, $"Expected object containing '{name}'.");
        return ContractJson.Array(parent, name);
    }

    private static string String(JsonElement parent, string name)
    {
        Require(parent.ValueKind == JsonValueKind.Object, $"Expected object containing '{name}'.");
        var value = ContractJson.String(parent, name);
        Require(value.Length > 0 && value.Length <= 4096 && !value.Any(char.IsControl),
            $"'{name}' must be nonempty bounded text without controls.");
        return value;
    }

    private static string Digest(JsonElement parent, string name, int length = 64)
    {
        var value = String(parent, name);
        Require(value.Length == length && value.All(Uri.IsHexDigit), $"'{name}' is not a {length}-hex digest.");
        return value;
    }
}
