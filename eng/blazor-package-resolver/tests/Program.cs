using System.Text.Json;
using BlazorPackageResolver;

var data = Path.Combine(AppContext.BaseDirectory, "data");
var fixture = Path.Combine(AppContext.BaseDirectory, "fixtures", "cases.json");
var catalog = Catalog.Load(data);
var registry = Registry.Bootstrap(catalog);
var expectedIds = ("LP-01 LP-02 LP-03 LP-04 LP-05 LP-06 LP-07 LP-08 LP-09 LP-10 " +
    "PI-01 PI-02 PI-03 PI-04 PI-05 PI-06 PI-07 PI-08 PI-09 PI-10 PI-11 PI-12 " +
    "SEC-01 SEC-02 SEC-03 SEC-04 SEC-05 SEC-06 SEC-07 SEC-08 SEC-09 " +
    "BEQ-21 BEQ-24 TA-07 CI-01 CI-05 CI-06 CI-07 CI-08 " +
    "SUP-01 SUP-02 SUP-03 SUP-04 SUP-05 SUP-06 SUP-07 SUP-08 SUP-10 " +
    "SCF-01 SCF-02 SCF-03 SCF-04 SCF-05 SCF-06 AI-01 AI-02 AI-03 AI-04 AI-05 AI-06").Split(' ');
Check(catalog.RubricDigest == "0153ba6982fd1102df9193d3bc2b1957bac1e3478bd0d6cef5a78b0e6daa31cd",
    "Frozen rubric SHA-256");
Check(catalog.BasisDigest == "5f39232d19fce5c5ea40e3f6949b3b4f695fed616a75446e3a42e8015c05cd5e",
    "Frozen basis SHA-256");
Check(catalog.PackageRows.Select(row => row.Id).SequenceEqual(expectedIds), "Exact ordered 60-row projection");
Check(catalog.ExcludedIds.Count == 53, "52 component IDs plus one extension excluded");
var coverage = registry.Coverage(catalog);
Check(coverage.SequenceEqual(registry.Coverage(catalog)), "Canonical coverage bytes");
using (var doc = JsonDocument.Parse(coverage))
{
    var rows = doc.RootElement.GetProperty("rows");
    Check(rows.GetArrayLength() == 60 &&
        rows.EnumerateArray().All(row => row.GetProperty("scope").GetString() == "repository-wide" &&
            row.GetProperty("work_state").GetString() == "tbd" &&
            !row.TryGetProperty("resolver_id", out _)), "Every row is unresolved and unclaimed");
    NoClaims(doc.RootElement);
}

var registrations = expectedIds.Select(id => new RegistryEntry(id, "tbd")).ToArray();
void Reject(params RegistryEntry[] entries) => Throws(() => new Registry(catalog, entries, []));
Reject(registrations.Skip(1).ToArray());
Reject([.. registrations, registrations[0]]);
foreach (var id in new[] { "TA-08", catalog.ExcludedIds.First(id => id != "TA-08"), "ZZ-99" })
    Reject([.. registrations.Skip(1), new RegistryEntry(id, "tbd")]);
Reject([new RegistryEntry("LP-01", "implemented", "synthetic", "1.0.0"), .. registrations.Skip(1)]);
Throws(() => new Registry(catalog, registrations, [Fake("LP-01", _ => throw new Exception())]));
Throws(() => new Registry(catalog, registrations, [Fake("ZZ-99", _ => throw new Exception())]));
Throws(() => new Registry(catalog,
    [new RegistryEntry("LP-01", "implemented", "mismatched", "1.0.0"), .. registrations.Skip(1)],
    [Fake("LP-01", _ => throw new Exception())]));

var tests = new[]
{
    ("positive", "applicable", "explicitly present"),
    ("negative", "not-applicable", "explicitly absent")
};
foreach (var (branch, applicability, finding) in tests)
{
    var handler = Fake("LP-01", input => Result(input, branch, "deterministic", applicability,
        finding, null, null, null));
    var withHandler = Implemented(handler);
    var input = new ResolverCase(branch, "LP-01", []);
    var result = withHandler.Dispatch(input);
    Check(result.BranchId == branch && result.Applicability == applicability && result.Finding == finding,
        "Deterministic branch identity and applicability");
    NoClaims(JsonDocument.Parse(Canonical.Write(result.Write)).RootElement);
}
var unknownHandler = Fake("LP-01", input => Result(input, "missing-owner", "escalated",
    "unknown", null, "Owner identity is not recorded.", "owner-record", "owner"));
var unknown = Implemented(unknownHandler).Dispatch(new ResolverCase("unknown", "LP-01", []));
Check(unknown.RequiredInput == "owner-record" && unknown.NextCategory == "owner", "Unknown branch escalation");
var branches = Path.Combine(AppContext.BaseDirectory, "fixtures", "branch-cases.json");
var branchRegistry = Implemented(Fake("LP-01", input => input.CaseId switch
{
    "positive" => Result(input, "positive", "deterministic", "applicable",
        "explicitly present", null, null, null),
    "negative" => Result(input, "negative", "deterministic", "not-applicable",
        "explicitly absent", null, null, null),
    _ => Result(input, "missing-owner", "escalated", "unknown",
        null, "Owner identity is not recorded.", "owner-record", "owner")
}));
var branchOutput = Replay.Run(branches, branchRegistry);
Check(branchOutput.SequenceEqual(Replay.Run(branches, branchRegistry)) &&
    JsonDocument.Parse(branchOutput).RootElement.GetProperty("cases").GetArrayLength() == 3,
    "Pinned positive, negative and unknown branch replays");
NoClaims(JsonDocument.Parse(branchOutput).RootElement);
Throws(() => Implemented(Fake("LP-01", input =>
    Result(input, "bad", "deterministic", "unknown", "guessed", null, null, null)))
    .Dispatch(new ResolverCase("invalid", "LP-01", [])));
Throws(() => Implemented(Fake("LP-01", input =>
    Result(input with { Inputs = [new PinnedInput("package", "wrong", new string('a', 64))] },
        "bad", "deterministic", "applicable", "wrong input", null, null, null)))
    .Dispatch(new ResolverCase("invalid-input", "LP-01", [])));
Throws(() => registry.Dispatch(new ResolverCase("component", catalog.ExcludedIds.First(), [])));

var replay = Replay.Run(fixture, registry);
Check(replay.SequenceEqual(Replay.Run(fixture, registry)), "Canonical replay bytes");
using (var doc = JsonDocument.Parse(replay))
{
    Check(doc.RootElement.GetProperty("cases").GetArrayLength() == 2, "Fixture replay cases");
    NoClaims(doc.RootElement);
}

var temp = Path.Combine(Path.GetTempPath(), "resolver-checks-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(temp);
try
{
    var suite = File.ReadAllText(fixture);
    foreach (var name in new[] { "package.txt", "repository.txt", "owner.txt" })
        File.Copy(Path.Combine(Path.GetDirectoryName(fixture)!, name), Path.Combine(temp, name));
    var path = Path.Combine(temp, "cases.json");
    void Mutated(string text)
    {
        File.WriteAllText(path, text);
        Throws(() => Replay.Run(path, registry));
    }
    Mutated(suite.Replace("\"resolver_version\": \"1.0.0\"", "\"resolver_version\": \"2.0.0\""));
    Mutated(suite.Replace("\"sha256\": \"c51ea103", "\"sha256\": \"051ea103"));
    Mutated(suite.Replace("\"role\": \"repository-snapshot\"", "\"role\": \"package\""));
    Mutated(suite.Replace("\"path\": \"owner.txt\"", "\"path\": \"missing.txt\""));
    Mutated(suite.Replace("\"row_id\": \"SUP-01\"", "\"row_id\": \"TA-08\""));
    Mutated(suite.Replace("\"path\": \"owner.txt\"", "\"path\": \"../owner.txt\""));
    File.WriteAllText(path, suite.Replace("\"case_id\": \"synthetic-owner\"",
        "\"case_id\": \"synthetic-owner\", \"status\": \"verified\""));
    Throws(() => Replay.Run(path, registry));
    File.WriteAllText(path, suite);
    Check(Replay.Run(path, registry).SequenceEqual(replay), "Relocated fixture replay");
    File.WriteAllText(path, "{\"schema_version\":1,\"cases\":[]}");
    Check(JsonDocument.Parse(Replay.Run(path, registry)).RootElement.GetProperty("cases")
        .GetArrayLength() == 0, "Empty suite supported");
    var dataCopy = Path.Combine(temp, "data");
    Directory.CreateDirectory(dataCopy);
    foreach (var name in new[] { "rubric.json", "requirement-basis.json", "provenance.json" })
        File.Copy(Path.Combine(data, name), Path.Combine(dataCopy, name));
    File.AppendAllText(Path.Combine(dataCopy, "rubric.json"), " ");
    Throws(() => Catalog.Load(dataCopy));
    File.Copy(Path.Combine(data, "rubric.json"), Path.Combine(dataCopy, "rubric.json"), true);
    File.AppendAllText(Path.Combine(dataCopy, "requirement-basis.json"), " ");
    Throws(() => Catalog.Load(dataCopy));
}
finally
{
    Directory.Delete(temp, true);
}

var source = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
var allowed = new HashSet<string>(StringComparer.Ordinal)
    { "Catalog.cs", "Registry.cs", "Cases.cs", "Program.cs" };
var allowedFiles = allowed.Concat(["BlazorPackageResolver.csproj", "README.md",
    "data/provenance.json", "data/rubric.json", "data/requirement-basis.json"])
    .ToHashSet(StringComparer.Ordinal);
var production = Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories)
    .Select(path => Path.GetRelativePath(source, path).Replace('\\', '/'))
    .Where(path => !path.StartsWith("tests/", StringComparison.Ordinal) &&
        !path.Split('/').Any(part => part is "obj" or "bin"))
    .ToHashSet(StringComparer.Ordinal);
Check(production.SetEquals(allowedFiles), "Standalone production file allowlist");
foreach (var file in allowed)
{
    var text = File.ReadAllText(Path.Combine(source, file));
    Check(!System.Text.RegularExpressions.Regex.IsMatch(text,
        @"\bnamespace\s+(?!BlazorPackageResolver\s*;)[\w.]+", System.Text.RegularExpressions.RegexOptions.IgnoreCase),
        "No assessment, rendering, reader, library, comparison, component or AI namespace");
}
Console.WriteLine("Resolver bootstrap checks passed.");

Registry Implemented(IDeterministicResolver handler) => new(catalog,
    [new RegistryEntry("LP-01", "implemented", "synthetic", "1.0.0"), .. registrations.Skip(1)],
    [handler]);
static IDeterministicResolver Fake(string id, Func<ResolverCase, CaseResult> action) =>
    new SyntheticResolver(id, action);
static CaseResult Result(ResolverCase input, string branch, string decision,
    string applicability, string? finding, string? missing, string? required, string? next) =>
    new(input.RowId, "synthetic", "1.0.0", branch, decision, applicability, input.Inputs,
        decision == "deterministic" ? ["fixture:synthetic"] : [], null,
        finding, missing, required, next);
static void Check(bool success, string message)
{
    if (!success) throw new Exception(message);
}
static void Throws(Action action)
{
    try { action(); }
    catch (InvalidDataException) { return; }
    catch (JsonException) { return; }
    catch (FileNotFoundException) { return; }
    throw new Exception("Expected input or contract rejection.");
}
static void NoClaims(JsonElement node)
{
    if (node.ValueKind == JsonValueKind.Array)
        foreach (var item in node.EnumerateArray()) NoClaims(item);
    else if (node.ValueKind == JsonValueKind.Object)
        foreach (var property in node.EnumerateObject())
        {
            Check(property.Name is not ("status" or "assessment" or "verdict" or "score" or "overall_readiness"),
                "No assessment or readiness authority in output");
            NoClaims(property.Value);
        }
}

sealed class SyntheticResolver(string row, Func<ResolverCase, CaseResult> resolve) : IDeterministicResolver
{
    public string RowId => row;
    public string ResolverId => "synthetic";
    public string Version => "1.0.0";
    public CaseResult Resolve(ResolverCase input) => resolve(input);
}
