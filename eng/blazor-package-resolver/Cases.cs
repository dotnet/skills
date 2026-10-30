using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace BlazorPackageResolver;

public sealed record PinnedInput(string Role, string Path, string Sha256);
public sealed record CheckedInput(PinnedInput Identity, ReadOnlyMemory<byte> Content);
public sealed record ResolverCase(
    string CaseId, string RowId, IReadOnlyList<PinnedInput> Inputs,
    IReadOnlyDictionary<string, CheckedInput> CheckedInputs);

public sealed record CaseResult(
    string RowId, string ResolverId, string ResolverVersion, string BranchId,
    string Decision, string Applicability, IReadOnlyList<PinnedInput> Inputs,
    IReadOnlyList<string> EvidenceReferences, string? EvidenceContract, string? Finding,
    string? MissingFact, string? RequiredInput, string? NextCategory)
{
    public void Validate(ResolverCase input, IDeterministicResolver? handler = null)
    {
        if (RowId != input.RowId || Inputs is null || !Inputs.SequenceEqual(input.Inputs) ||
            Inputs.Any(item => item is null || string.IsNullOrWhiteSpace(item.Role) ||
                string.IsNullOrWhiteSpace(item.Path) ||
                !Regex.IsMatch(item.Sha256 ?? "", "^[a-f0-9]{64}$", RegexOptions.CultureInvariant)) ||
            !Slug(ResolverId) || !Slug(BranchId) ||
            string.IsNullOrWhiteSpace(ResolverVersion) ||
            !Regex.IsMatch(ResolverVersion, @"^[1-9][0-9]*\.[0-9]+\.[0-9]+$", RegexOptions.CultureInvariant) ||
            EvidenceReferences is null || EvidenceReferences.Any(string.IsNullOrWhiteSpace) ||
            EvidenceReferences.Distinct(StringComparer.Ordinal).Count() != EvidenceReferences.Count ||
            Applicability is not ("applicable" or "not-applicable" or "unknown"))
            throw new InvalidDataException("Invalid case result identity, inputs, branch or applicability.");
        if (handler is not null &&
            (handler.RowId != RowId || handler.ResolverId != ResolverId || handler.Version != ResolverVersion))
            throw new InvalidDataException("Result identity does not match registered handler.");
        if (Decision == "deterministic")
        {
            if (Applicability == "unknown" || string.IsNullOrWhiteSpace(Finding) ||
                EvidenceReferences.Count == 0 && string.IsNullOrWhiteSpace(EvidenceContract) ||
                MissingFact is not null || RequiredInput is not null || NextCategory is not null)
                throw new InvalidDataException("Deterministic branch requires a finding and cited evidence contract.");
        }
        else if (Decision == "escalated")
        {
            if (Applicability == "not-applicable" || Finding is not null ||
                string.IsNullOrWhiteSpace(MissingFact) || string.IsNullOrWhiteSpace(RequiredInput) ||
                !Slug(NextCategory))
                throw new InvalidDataException("Applicable or unknown escalation requires a missing fact, input and next category.");
        }
        else
            throw new InvalidDataException($"Unknown case decision: {Decision}");
    }

    private static bool Slug(string? value) => value is not null &&
        Regex.IsMatch(value, "^[a-z][a-z0-9-]*$", RegexOptions.CultureInvariant);

    public void Write(Utf8JsonWriter writer)
    {
        writer.WriteStartObject();
        writer.WriteString("row_id", RowId);
        writer.WriteString("resolver_id", ResolverId);
        writer.WriteString("resolver_version", ResolverVersion);
        writer.WriteString("branch_id", BranchId);
        writer.WriteString("decision", Decision);
        writer.WriteString("applicability", Applicability);
        writer.WritePropertyName("inputs");
        writer.WriteStartArray();
        foreach (var item in Inputs)
        {
            writer.WriteStartObject();
            writer.WriteString("role", item.Role);
            writer.WriteString("path", item.Path);
            writer.WriteString("sha256", item.Sha256);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        writer.WritePropertyName("evidence_references");
        writer.WriteStartArray();
        foreach (var reference in EvidenceReferences)
            writer.WriteStringValue(reference);
        writer.WriteEndArray();
        WriteNullable(writer, "evidence_contract", EvidenceContract);
        WriteNullable(writer, "finding", Finding);
        WriteNullable(writer, "missing_fact", MissingFact);
        WriteNullable(writer, "required_input", RequiredInput);
        WriteNullable(writer, "next_category", NextCategory);
        writer.WriteEndObject();
    }

    private static void WriteNullable(Utf8JsonWriter writer, string name, string? value)
    {
        if (value is null)
            writer.WriteNull(name);
        else
            writer.WriteString(name, value);
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ReplayCase(
    string CaseId, string RowId, List<PinnedInput> Inputs, CaseResult Expected,
    string Rationale, List<string> EvidencePointers);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CaseSuite(int SchemaVersion, List<ReplayCase> Cases);

public static class Replay
{
    private static readonly Regex RelativeInputPath = new(
        @"\A[A-Za-z0-9._-]+(?:/[A-Za-z0-9._-]+)*\z",
        RegexOptions.CultureInvariant);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static byte[] Run(string manifestPath, Registry registry)
    {
        if (string.IsNullOrWhiteSpace(manifestPath))
            throw new InvalidDataException("Case manifest path is required.");
        byte[] manifest;
        try
        {
            manifest = File.ReadAllBytes(manifestPath);
        }
        catch (ArgumentException error)
        {
            throw new InvalidDataException("Invalid case manifest path.", error);
        }
        var suite = JsonSerializer.Deserialize<CaseSuite>(manifest, JsonOptions)
            ?? throw new InvalidDataException("Empty replay suite.");
        if (suite.SchemaVersion != 1 || suite.Cases is null)
            throw new InvalidDataException("Unsupported replay suite.");
        var root = Path.GetDirectoryName(FullPath(manifestPath))!;
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var results = new List<(string Id, CaseResult Result)>();
        foreach (var item in suite.Cases)
        {
            if (item is null || string.IsNullOrWhiteSpace(item.CaseId) || !ids.Add(item.CaseId) ||
                string.IsNullOrWhiteSpace(item.Rationale) || item.Inputs is null ||
                item.Expected is null || item.EvidencePointers is null ||
                item.EvidencePointers.Any(string.IsNullOrWhiteSpace))
                throw new InvalidDataException("Case needs a unique ID, rationale, inputs, expected result and evidence pointers.");
            var roles = new HashSet<string>(StringComparer.Ordinal);
            var checkedInputs = new Dictionary<string, CheckedInput>(StringComparer.Ordinal);
            foreach (var artifact in item.Inputs)
            {
                if (artifact is null || string.IsNullOrWhiteSpace(artifact.Role) || !roles.Add(artifact.Role) ||
                    artifact.Path is not { Length: > 0 and <= 256 } ||
                    !RelativeInputPath.IsMatch(artifact.Path) ||
                    artifact.Path.Split('/').Any(part => part is "." or "..") ||
                    !Regex.IsMatch(artifact.Sha256 ?? "", "^[a-f0-9]{64}$", RegexOptions.CultureInvariant))
                    throw new InvalidDataException($"Conflicting or invalid pinned inputs in {item.CaseId}.");
                var path = FullPath(root, artifact.Path);
                var relative = Path.GetRelativePath(root, path);
                if (Path.IsPathRooted(artifact.Path) ||
                    relative == ".." ||
                    relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                    throw new InvalidDataException($"Missing, escaped or digest-mismatched input: {artifact.Path}");
                var bytes = File.ReadAllBytes(path);
                if (Catalog.Digest(bytes) != artifact.Sha256)
                    throw new InvalidDataException($"Missing, escaped or digest-mismatched input: {artifact.Path}");
                checkedInputs.Add(artifact.Role, new CheckedInput(artifact, bytes));
            }
            var input = new ResolverCase(item.CaseId, item.RowId, item.Inputs, checkedInputs);
            item.Expected.Validate(input);
            if (item.Expected.EvidenceReferences.Any(reference =>
                !item.EvidencePointers.Contains(reference, StringComparer.Ordinal)))
                throw new InvalidDataException($"Uncited expected evidence in {item.CaseId}.");
            var actual = registry.Dispatch(input);
            actual.Validate(input);
            if (actual.EvidenceReferences.Any(reference =>
                !item.EvidencePointers.Contains(reference, StringComparer.Ordinal)))
                throw new InvalidDataException($"Uncited resolver evidence in {item.CaseId}.");
            if (!Canonical.Write(actual.Write).SequenceEqual(Canonical.Write(item.Expected.Write)))
                throw new InvalidDataException($"Frozen expectation mismatch: {item.CaseId}");
            results.Add((item.CaseId, actual));
        }
        return Canonical.Write(writer =>
        {
            writer.WriteStartObject();
            writer.WriteNumber("schema_version", 1);
            writer.WritePropertyName("cases");
            writer.WriteStartArray();
            foreach (var (id, result) in results)
            {
                writer.WriteStartObject();
                writer.WriteString("case_id", id);
                writer.WritePropertyName("result");
                result.Write(writer);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        });
    }

    private static string FullPath(string path, string? relative = null)
    {
        try
        {
            return Path.GetFullPath(relative is null ? path : Path.Combine(path, relative));
        }
        catch (ArgumentException error)
        {
            throw new InvalidDataException("Invalid case input path.", error);
        }
    }
}

public static class Canonical
{
    public static byte[] Write(Action<Utf8JsonWriter> write)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
            write(writer);
        buffer.WriteByte((byte)'\n');
        return buffer.ToArray();
    }
}
