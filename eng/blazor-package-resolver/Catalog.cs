using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace BlazorPackageResolver;

public sealed record PackageRow(string Id, string Scope, string Requirement, string Area, string Clause);

public sealed class Catalog
{
    private const string SourceCommit = "fefdaccbfdc0f34cec3d3ca5b8f7ea3fce4a68e3";
    private const string SourceDirectory = "plugins/dotnet-blazor/skills/blazor-component-readiness/references/";
    public IReadOnlyList<PackageRow> PackageRows { get; }
    public IReadOnlySet<string> ExcludedIds { get; }
    public string RubricDigest { get; }
    public string BasisDigest { get; }
    public string RubricVersion { get; }

    private Catalog(List<PackageRow> rows, HashSet<string> excluded, string rubricDigest,
        string basisDigest, string version)
    {
        PackageRows = rows.AsReadOnly();
        ExcludedIds = excluded;
        RubricDigest = rubricDigest;
        BasisDigest = basisDigest;
        RubricVersion = version;
    }

    public static Catalog Load(string dataDirectory)
    {
        using var provenance = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(dataDirectory, "provenance.json")));
        var root = provenance.RootElement;
        Require(root.GetProperty("source_commit").GetString() == SourceCommit, "Unexpected catalog source commit.");
        var files = root.GetProperty("files");
        Require(files.GetArrayLength() == 2, "Provenance must identify exactly two source files.");
        var rubric = ReadPinned(dataDirectory, files[0], "rubric.json");
        var basis = ReadPinned(dataDirectory, files[1], "requirement-basis.json");
        using var rubricJson = JsonDocument.Parse(rubric);
        using var basisJson = JsonDocument.Parse(basis);
        var doc = rubricJson.RootElement;
        var crosswalk = basisJson.RootElement;
        Require(doc.GetProperty("schema_version").GetInt32() == 2 &&
            doc.GetProperty("scope_schema_version").GetInt32() == 2 &&
            crosswalk.GetProperty("schema_version").GetInt32() == 2 &&
            doc.GetProperty("crosswalk").GetString() == "requirement-basis.json",
            "Unsupported normative catalog schema or crosswalk.");
        var version = RequiredText(doc, "rubric_version");
        Require(version == RequiredText(crosswalk, "version"), "Rubric and basis versions differ.");
        var clauseIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var clause in crosswalk.GetProperty("clauses").EnumerateArray())
        {
            Require(clauseIds.Add(RequiredText(clause, "id")) &&
                !string.IsNullOrWhiteSpace(RequiredText(clause, "quotation")), "Invalid basis clause.");
        }

        var rows = new List<PackageRow>();
        var excluded = new HashSet<string>(StringComparer.Ordinal);
        var allIds = new HashSet<string>(StringComparer.Ordinal);
        var core = doc.GetProperty("core").GetProperty("requirements");
        foreach (var item in core.EnumerateArray())
        {
            var id = RequiredText(item, "id");
            Require(allIds.Add(id), $"Duplicate normative row: {id}");
            var clause = RequiredText(item, "clause");
            Require(clauseIds.Contains(clause), $"Missing basis clause {clause} for {id}.");
            if (item.TryGetProperty("additional_clauses", out var additional))
                foreach (var extra in additional.EnumerateArray())
                    Require(clauseIds.Contains(extra.GetString() ?? ""), $"Missing additional basis clause for {id}.");
            var scope = RequiredText(item, "scope");
            var row = new PackageRow(id, scope, RequiredText(item, "requirement"),
                RequiredText(item, "area"), clause);
            if (scope == "repository-wide")
                rows.Add(row);
            else if (scope == "component-specific")
                excluded.Add(id);
            else
                throw new InvalidDataException($"Unknown normative scope: {scope}");
        }
        Require(core.GetArrayLength() == 112 && rows.Count == 60 && excluded.Count == 52,
            "Expected 112 core rows: 60 repository-wide and 52 component-specific.");
        foreach (var extension in doc.GetProperty("extensions").EnumerateArray())
            Require(excluded.Add(RequiredText(extension, "id")) &&
                allIds.Add(RequiredText(extension, "id")), "Duplicate extension row.");
        Require(doc.GetProperty("overlays").GetArrayLength() == 0, "Unexpected normative overlays.");
        return new Catalog(rows, excluded, Digest(rubric), Digest(basis), version);
    }

    private static byte[] ReadPinned(string directory, JsonElement provenance, string name)
    {
        Require(RequiredText(provenance, "path") == SourceDirectory + name &&
            RequiredText(provenance, "git_blob").Length == 40, $"Unexpected provenance for {name}.");
        var bytes = File.ReadAllBytes(Path.Combine(directory, name));
        var prefix = Encoding.ASCII.GetBytes($"blob {bytes.Length}\0");
        var blob = new byte[prefix.Length + bytes.Length];
        prefix.CopyTo(blob, 0);
        bytes.CopyTo(blob, prefix.Length);
        Require(Digest(bytes) == RequiredText(provenance, "sha256") &&
            Convert.ToHexStringLower(SHA1.HashData(blob)) == RequiredText(provenance, "git_blob"),
            $"Digest mismatch for {name}.");
        return bytes;
    }

    private static string RequiredText(JsonElement element, string property)
    {
        var text = element.GetProperty(property).GetString();
        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidDataException($"Missing or empty {property}.");
        return text;
    }

    private static void Require(bool valid, string message)
    {
        if (!valid)
            throw new InvalidDataException(message);
    }

    public static string Digest(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
}
