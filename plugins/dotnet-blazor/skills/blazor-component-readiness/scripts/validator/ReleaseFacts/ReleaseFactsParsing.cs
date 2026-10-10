using System.Text.Json;
using BlazorComponentReadiness.Validator.IO;

namespace BlazorComponentReadiness.Validator.Release;

public sealed partial class ReleaseFactsService
{
    private void ReadSpdx22(JsonElement document, string filename, string entry,
        string role = "spdx22", string pointerPrefix = "")
    {
        Require(String(document, "spdxVersion") == "SPDX-2.2", "Unsupported SPDX2 document version.");
        var ids = new HashSet<string>(StringComparer.Ordinal) { String(document, "SPDXID") };
        var packages = Array(document, "packages").EnumerateArray().ToArray();
        var files = Array(document, "files").EnumerateArray().ToArray();
        foreach (var element in packages.Concat(files))
        {
            Require(ids.Add(String(element, "SPDXID")), "Duplicate SPDX2 element identity.");
        }

        var described = Array(document, "documentDescribes").EnumerateArray().ToArray();
        Require(described.Length == 1 && described[0].ValueKind == JsonValueKind.String,
            "SPDX2 documentDescribes must resolve one local package.");
        var packageIndex = UniqueIndex(packages, package =>
            String(package, "SPDXID") == described[0].GetString(), "SPDX2 described package");
        var package = packages[packageIndex];
        var fileIndex = UniqueIndex(files, file => FileNameMatches(String(file, "fileName"), filename),
            "SPDX2 package file");
        var file = files[fileIndex];
        var fileId = String(file, "SPDXID");
        var hasFiles = Array(package, "hasFiles").EnumerateArray().ToArray();
        Require(hasFiles.All(item => item.ValueKind == JsonValueKind.String),
            "SPDX2 package hasFiles must contain local identity strings.");
        var fileIds = files.Select(item => String(item, "SPDXID")).ToHashSet(StringComparer.Ordinal);
        var fileRefs = hasFiles.Select(item => item.GetString()!).ToArray();
        Require(fileRefs.Distinct(StringComparer.Ordinal).Count() == fileRefs.Length &&
                fileRefs.All(fileIds.Contains) &&
                fileRefs.Contains(fileId, StringComparer.Ordinal),
            "SPDX2 package hasFiles has duplicate, unresolved or missing package-file references.");
        var checksums = Array(file, "checksums").EnumerateArray().ToArray();
        ValidateChecksums(checksums, "algorithm", "checksumValue", spdx22: true);
        var checksumIndex = UniqueIndex(checksums, checksum => String(checksum, "algorithm") == "SHA256",
            "SPDX2 package-file SHA256");
        var packagePath = $"/packages/{packageIndex}";
        var filePath = $"/files/{fileIndex}";
        Capture("document-id", "document-id", document, "SPDXID", "");
        Capture("document-name", "document-name", document, "name", "");
        Capture("document-namespace", "document-namespace", document, "documentNamespace", "");
        Capture("version", "format-version", document, "spdxVersion", "");
        Capture("package-id", "declared-id", package, "SPDXID", packagePath);
        Capture("package-name", "package-name", package, "name", packagePath);
        Capture("package-version", "package-version", package, "versionInfo", packagePath);
        Capture("file-id", "declared-id", file, "SPDXID", filePath);
        Capture("file-name", "file-name", file, "fileName", filePath);
        var verification = Object(package, "packageVerificationCode");
        Add($"{role}.package-verification", "package-verification-code", "sha1",
            Digest(verification, "packageVerificationCodeValue", 40), role, entry,
            $"{pointerPrefix}{packagePath}/packageVerificationCode/packageVerificationCodeValue");
        Add($"{role}.file-sha256", "package-file-checksum", String(checksums[checksumIndex], "algorithm"),
            Digest(checksums[checksumIndex], "checksumValue"), role, entry,
            $"{pointerPrefix}{filePath}/checksums/{checksumIndex}/checksumValue");

        void Capture(string id, string kind, JsonElement element, string property, string path) =>
            Add($"{role}.{id}", kind, null, String(element, property), role, entry, $"{pointerPrefix}{path}/{property}");
    }

    private void ReadSpdx30(JsonElement document, string filename, string entry)
    {
        var context = Array(document, "@context").EnumerateArray().ToArray();
        Require(context.Length == 1 && context[0].ValueKind == JsonValueKind.String &&
                context[0].GetString() == "https://spdx.org/rdf/3.0.1/spdx-context.json",
            "Only the retained SPDX3 3.0.1 context reference is supported; contexts are never fetched.");
        var graph = Array(document, "@graph").EnumerateArray().ToArray();
        var topLevel = new Dictionary<string, int>(StringComparer.Ordinal);
        var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
        CountIds(document);
        for (var i = 0; i < graph.Length; i++)
        {
            var node = graph[i];
            Require(node.ValueKind == JsonValueKind.Object, "SPDX3 graph nodes must be objects.");
            var ids = NodeIds(node);
            Require(ids.Length > 0, "Missing SPDX3 top-level identity.");
            foreach (var id in ids)
            {
                Require(topLevel.TryAdd(id, i), "Duplicate SPDX3 top-level identity or alias.");
            }
        }

        var documentIndex = UniqueIndex(graph, node => String(node, "type") == "SpdxDocument",
            "SPDX3 document");
        var fileIndex = UniqueIndex(graph, node =>
            String(node, "type") == "software_File" && FileNameMatches(String(node, "name"), filename),
            "SPDX3 package file");
        foreach (var index in new[] { documentIndex, fileIndex })
        {
            foreach (var id in NodeIds(graph[index]))
            {
                Require(occurrences.GetValueOrDefault(id) == 1,
                    "SPDX3 required document/file identity or alias is ambiguous.");
            }
        }

        var file = graph[fileIndex];
        var filePath = $"/@graph/{fileIndex}";
        var verifications = Array(file, "verifiedUsing").EnumerateArray().ToArray();
        var resolved = verifications.Select((value, index) =>
            Resolve(value, $"{filePath}/verifiedUsing/{index}")).ToArray();
        ValidateChecksums(resolved.Select(item => item.Value).ToArray(), "algorithm", "hashValue", spdx22: false);
        foreach (var (value, _) in resolved)
        {
            Require(String(value, "type") is "Hash" or "PackageVerificationCode",
                "Unsupported SPDX3 verification type.");
            _ = String(value, "spdxId");
        }

        var checksumIndex = UniqueIndex(resolved.Select(item => item.Value).ToArray(),
            value => String(value, "algorithm") == "sha256", "SPDX3 file-associated SHA256");
        var (verification, path) = resolved[checksumIndex];
        var type = String(verification, "type");
        Add("spdx30.context", "format-context", null, context[0].GetString()!, "spdx30", entry, "/@context/0");
        Add("spdx30.document-id", "document-id", null, String(graph[documentIndex], "spdxId"),
            "spdx30", entry, $"/@graph/{documentIndex}/spdxId");
        Add("spdx30.document-name", "document-name", null, String(graph[documentIndex], "name"),
            "spdx30", entry, $"/@graph/{documentIndex}/name");
        Add("spdx30.file-id", "declared-id", null, String(file, "spdxId"), "spdx30", entry, $"{filePath}/spdxId");
        Add("spdx30.file-name", "file-name", null, String(file, "name"), "spdx30", entry, $"{filePath}/name");
        Add("spdx30.file-sha256", type == "Hash" ? "package-file-checksum" : "package-verification-code",
            String(verification, "algorithm"), Digest(verification, "hashValue"), "spdx30", entry, $"{path}/hashValue");
        Add("spdx30.verification-type", "declared-type", null, type, "spdx30", entry, $"{path}/type");
        Add("spdx30.verification-id", "declared-id", null, String(verification, "spdxId"),
            "spdx30", entry, $"{path}/spdxId");
        Add("spdx30.verification-link", "local-verification-reference", null,
            verifications[checksumIndex].ValueKind == JsonValueKind.String
                ? verifications[checksumIndex].GetString()! : "inline-occurrence",
            "spdx30", entry, $"{filePath}/verifiedUsing/{checksumIndex}");

        (JsonElement Value, string Path) Resolve(JsonElement value, string pointer)
        {
            // Embedded records are literal occurrences, never globally merged by their declared IDs.
            if (value.ValueKind == JsonValueKind.Object) return (value, pointer);
            Require(value.ValueKind == JsonValueKind.String, "SPDX3 verification must be an object or local ID reference.");
            var id = value.GetString()!;
            Require(topLevel.ContainsKey(id) && occurrences.TryGetValue(id, out var count) && count == 1,
                "SPDX3 required verification reference is unresolved or ambiguous.");
            return (graph[topLevel[id]], $"/@graph/{topLevel[id]}");
        }

        void CountIds(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (var id in NodeIds(element)) occurrences[id] = occurrences.GetValueOrDefault(id) + 1;
                foreach (var property in element.EnumerateObject()) CountIds(property.Value);
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in element.EnumerateArray()) CountIds(item);
            }
        }
    }

    private JsonDocument ReadStatement(byte[] bytes, string role, string predicateType, string filename)
    {
        using var bundle = Parse(bytes, role);
        var root = bundle.RootElement;
        Require(String(root, "mediaType") == "application/vnd.dev.sigstore.bundle.v0.3+json",
            "Unsupported Sigstore bundle media type.");
        _ = Object(root, "verificationMaterial");
        var envelope = Object(root, "dsseEnvelope");
        Require(String(envelope, "payloadType") == "application/vnd.in-toto+json", "Unsupported DSSE payload type.");
        var signatures = Array(envelope, "signatures").EnumerateArray().ToArray();
        Require(signatures.Length > 0, "DSSE signatures array is empty (signatures are not authenticated).");
        foreach (var signature in signatures)
        {
            _ = Decode(String(signature, "sig"), "DSSE signature encoding");
        }

        Require(envelope.TryGetProperty("payload", out var payload) && payload.ValueKind == JsonValueKind.String,
            "DSSE payload must be a non-null base64 string.");
        var decoded = Decode(payload.GetString()!, "DSSE payload");
        var statement = Parse(decoded, $"{role} statement");
        var transferred = false;
        try
        {
            var value = statement.RootElement;
            Require(String(value, "_type") == "https://in-toto.io/Statement/v1", "Unsupported in-toto statement type.");
            Require(String(value, "predicateType") == predicateType, "Unsupported statement predicate type.");
            _ = Object(value, "predicate");
            var subjects = Array(value, "subject").EnumerateArray().ToArray();
            var names = subjects.Select(subject => String(subject, "name")).ToArray();
            Require(names.Distinct(StringComparer.Ordinal).Count() == names.Length, "Duplicate statement subject identities.");
            var subjectIndex = UniqueIndex(subjects, subject => String(subject, "name") == filename,
                $"{role} exact package subject");
            var digest = Object(subjects[subjectIndex], "digest");
            Require(digest.EnumerateObject().Count() == 1, "Only one unambiguous subject SHA256 is supported.");
            var hash = Digest(digest, "sha256");
            Add($"{role}.subject-name", "file-name", null, names[subjectIndex], role,
                "dsseEnvelope.payload", $"/subject/{subjectIndex}/name");
            Add($"{role}.subject-sha256", "package-file-checksum", "sha256", hash, role,
                "dsseEnvelope.payload", $"/subject/{subjectIndex}/digest/sha256");
            Add($"{role}.predicate-type", "predicate-type", null, predicateType, role,
                "dsseEnvelope.payload", "/predicateType");
            transferred = true;
            return statement;
        }
        finally
        {
            if (!transferred) statement.Dispose();
        }
    }

    private void ReadProvenance(JsonElement statement)
    {
        var definition = Object(Object(statement, "predicate"), "buildDefinition");
        Require(String(definition, "buildType") == "https://actions.github.io/buildtypes/workflow/v1",
            "Unsupported SLSA build type.");
        var workflow = Object(Object(definition, "externalParameters"), "workflow");
        var repository = String(workflow, "repository");
        var reference = String(workflow, "ref");
        var dependencies = Array(definition, "resolvedDependencies").EnumerateArray().ToArray();
        var uris = dependencies.Select(dependency => String(dependency, "uri")).ToArray();
        Require(uris.Distinct(StringComparer.Ordinal).Count() == uris.Length, "Duplicate SLSA dependency identities.");
        var index = UniqueIndex(dependencies, dependency =>
            String(dependency, "uri") == $"git+{repository}@{reference}", "SLSA workflow source dependency");
        var digest = Object(dependencies[index], "digest");
        Require(digest.EnumerateObject().Count() == 1, "SLSA source digest is ambiguous or unsupported.");
        Add("provenance.source-commit", "source-commit", "gitCommit", Digest(digest, "gitCommit", 40),
            "provenance", "dsseEnvelope.payload", $"/predicate/buildDefinition/resolvedDependencies/{index}/digest/gitCommit");
        Add("provenance.source-uri", "source-uri", null, uris[index], "provenance",
            "dsseEnvelope.payload", $"/predicate/buildDefinition/resolvedDependencies/{index}/uri");
        foreach (var (id, kind, property) in new[]
                 {
                     ("workflow", "workflow-path", "path"), ("repository", "repository-uri", "repository"),
                     ("workflow-ref", "source-ref", "ref")
                 })
        {
            Add($"provenance.{id}", kind, null, String(workflow, property), "provenance",
                "dsseEnvelope.payload", $"/predicate/buildDefinition/externalParameters/workflow/{property}");
        }
    }

    private byte[] Decode(string value, string resource)
    {
        Require(value.Length > 0 && value.Length % 4 == 0 && !value.Any(char.IsWhiteSpace),
            $"{resource} is not canonical base64.");
        var padding = value.EndsWith("==", StringComparison.Ordinal) ? 2 : value.EndsWith('=') ? 1 : 0;
        BoundedIO.EnsureLength((long)value.Length / 4 * 3 - padding, Remaining(), resource);
        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(value);
        }
        catch (FormatException exception)
        {
            throw new DeterministicValidationException($"{resource} is malformed base64.", exception);
        }

        Charge(ref decodedBytes, bytes.LongLength);
        Require(Convert.ToBase64String(bytes) == value, $"{resource} is not canonical base64.");
        return bytes;
    }

    private static void ValidateChecksums(JsonElement[] values, string algorithmProperty, string valueProperty, bool spdx22)
    {
        var algorithms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in values)
        {
            var algorithm = String(value, algorithmProperty);
            Require(algorithms.Add(algorithm), "Duplicate verification algorithms are ambiguous.");
            Require(spdx22 ? algorithm is "SHA256" or "SHA1" : algorithm is "sha256" or "sha1",
                "Only SHA256 and SHA1 verification declarations are supported.");
            _ = Digest(value, valueProperty, algorithm.Equals("sha1", StringComparison.OrdinalIgnoreCase) ? 40 : 64);
        }
    }

    private static string[] NodeIds(JsonElement node)
    {
        var hasSpdxId = node.TryGetProperty("spdxId", out _);
        var hasId = node.TryGetProperty("@id", out _);
        var ids = new List<string>();
        if (hasSpdxId) ids.Add(String(node, "spdxId"));
        if (hasId) ids.Add(String(node, "@id"));
        return ids.Distinct(StringComparer.Ordinal).ToArray();
    }

    private static int UniqueIndex(JsonElement[] values, Func<JsonElement, bool> predicate, string resource)
    {
        var matches = Enumerable.Range(0, values.Length).Where(index => predicate(values[index])).ToArray();
        Require(matches.Length == 1, $"{resource} must resolve exactly once; found {matches.Length}.");
        return matches[0];
    }

    private static bool FileNameMatches(string declared, string filename) =>
        declared == filename || declared == $"./{filename}";
}
