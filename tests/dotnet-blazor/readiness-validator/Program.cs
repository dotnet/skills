using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

var root = FindRepositoryRoot();
var plugin = Path.Combine(root, "plugins", "dotnet-blazor");
var standalonePlugin = Path.Combine(root, "plugins", "dotnet-blazor-component-readiness");
var references = Path.Combine(plugin, "skills", "blazor-component-readiness", "references");

using var rubricDocument = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(references, "rubric.json")));
var rubric = rubricDocument.RootElement;
var core = rubric.GetProperty("core").GetProperty("requirements").EnumerateArray().ToArray();
var overlays = rubric.GetProperty("overlays").EnumerateArray().ToArray();

AssertEqual(2, rubric.GetProperty("schema_version").GetInt32(), "rubric schema version");
AssertEqual("2.0.1", rubric.GetProperty("rubric_version").GetString(), "rubric version");
AssertEqual(2, rubric.GetProperty("scope_schema_version").GetInt32(), "scope schema version");
AssertEqual(
    "A versioned operational crosswalk to the bundled partner quality bar. Baseline obligations and unapproved operational extensions are distinct. Structural validation is not certification or Microsoft approval.",
    rubric.GetProperty("positioning").GetString(),
    "non-certification positioning");

var expectedStatuses = new[]
{
    "verified",
    "gap",
    "owner evidence required",
    "not tested",
    "not applicable"
};
AssertSequence(expectedStatuses, Strings(rubric.GetProperty("statuses")), "status vocabulary");

var expectedCoreIds = Expand(
    ("LP", 10),
    ("PI", 12),
    ("SEC", 13),
    ("A11Y", 12),
    ("BEQ", 24),
    ("TA", 7),
    ("PERF", 10),
    ("CI", 11),
    ("SUP", 10),
    ("SCF", 6),
    ("AI", 6));
AssertSequence(expectedCoreIds, core.Select(Id), "121 core IDs and canonical order");
AssertEqual(121, core.Length, "core requirement count");

var expectedRepositoryWide = new HashSet<string>(StringComparer.Ordinal)
{
    "LP-01", "LP-02", "LP-03", "LP-04", "LP-05", "LP-06", "LP-07", "LP-08", "LP-09", "LP-10",
    "PI-01", "PI-02", "PI-03", "PI-04", "PI-05", "PI-06", "PI-07", "PI-08", "PI-09", "PI-10", "PI-11", "PI-12",
    "SEC-01", "SEC-02", "SEC-03", "SEC-04", "SEC-05", "SEC-06", "SEC-07", "SEC-08", "SEC-09",
    "BEQ-21", "BEQ-24",
    "TA-07",
    "CI-01", "CI-05", "CI-06", "CI-07", "CI-08",
    "SUP-01", "SUP-02", "SUP-03", "SUP-04", "SUP-05", "SUP-06", "SUP-07", "SUP-08", "SUP-10",
    "SCF-01", "SCF-02", "SCF-03", "SCF-04", "SCF-05", "SCF-06",
    "AI-01", "AI-02", "AI-03", "AI-04", "AI-05", "AI-06"
};
var actualRepositoryWide = core
    .Where(requirement => Scope(requirement) == "repository-wide")
    .Select(Id)
    .ToHashSet(StringComparer.Ordinal);
AssertSet(expectedRepositoryWide, actualRepositoryWide, "60 repository-wide IDs");
AssertEqual(60, actualRepositoryWide.Count, "repository-wide requirement count");

var expectedComponentSpecific = expectedCoreIds
    .Where(id => !expectedRepositoryWide.Contains(id))
    .ToHashSet(StringComparer.Ordinal);
var actualComponentSpecific = core
    .Where(requirement => Scope(requirement) == "component-specific")
    .Select(Id)
    .ToHashSet(StringComparer.Ordinal);
AssertSet(expectedComponentSpecific, actualComponentSpecific, "61 component-specific IDs");
AssertEqual(61, actualComponentSpecific.Count, "component-specific requirement count");
AssertEqual(
    "d48756ed60c90b510b215e8dcdcb28523c0aca6de2a8a1d01368e31dbd45022d",
    RequirementDigest(core),
    "core ID, wording, and scope digest");

AssertEqual(0, overlays.Length, "no optional overlays");
var checklist = File.ReadAllText(Path.Combine(references, "checklist.md"));
var checklistRows = Regex.Matches(checklist, @"(?m)^\| ([A-Z0-9]+-\d{2}) \| (Package|Component) \| ([^|]+) \| ([DECX]) \|");
AssertSequence(expectedCoreIds, checklistRows.Select(match => match.Groups[1].Value),
    "current rubric/checklist ID order parity");
var basisLabels = new Dictionary<string, string>(StringComparer.Ordinal)
{
    ["direct obligation"] = "D",
    ["decomposition evidence check"] = "E",
    ["conditional obligation"] = "C",
    ["versioned extension"] = "X"
};
foreach (var (requirement, row) in core.Zip(checklistRows))
{
    AssertEqual(Scope(requirement) == "repository-wide" ? "Package" : "Component",
        row.Groups[2].Value, "current checklist ownership");
    var clauses = new[] { requirement.GetProperty("clause").GetString()! }
        .Concat(requirement.TryGetProperty("additional_clauses", out var additional) ? Strings(additional) : []);
    AssertEqual(string.Join("; ", clauses), row.Groups[3].Value, "current checklist clauses");
    AssertEqual(basisLabels[requirement.GetProperty("classification").GetString()!],
        row.Groups[4].Value, "current checklist classification");
}

var manifestPaths = new[]
{
    Path.Combine(plugin, "plugin.json"),
    Path.Combine(plugin, ".claude-plugin", "plugin.json"),
    Path.Combine(plugin, ".codex-plugin", "plugin.json")
};
var manifestBytes = manifestPaths.Select(File.ReadAllBytes).ToArray();
Assert(manifestBytes[1].SequenceEqual(manifestBytes[0]), "primary and Claude manifests must be byte-identical");
using (var manifest = JsonDocument.Parse(manifestBytes[0]))
using (var codexManifest = JsonDocument.Parse(manifestBytes[2]))
{
    var expectedCodex = JsonNode.Parse(manifestBytes[0])!.AsObject();
    expectedCodex.Remove("agents");
    Assert(JsonNode.DeepEquals(expectedCodex, JsonNode.Parse(manifestBytes[2])),
        "Codex manifest must equal the primary manifest with only agents removed");
    Assert(!codexManifest.RootElement.TryGetProperty("agents", out _), "Codex manifest must not declare unsupported agents");
    AssertEqual("dotnet-blazor", manifest.RootElement.GetProperty("name").GetString(), "plugin name");
    var manifestVersion = manifest.RootElement.GetProperty("version").GetString()
        ?? throw new InvalidOperationException("Plugin version must be a string.");
    AssertEqual(manifestVersion,
        BlazorComponentReadiness.Validator.IO.NuGetVersionNormalizer.Normalize(manifestVersion),
        "canonical generated plugin version");
    var previousSkillRoot = Environment.GetEnvironmentVariable("READINESS_SKILL_ROOT");
    try
    {
        Environment.SetEnvironmentVariable("READINESS_SKILL_ROOT", Path.GetDirectoryName(references));
        AssertEqual(manifestVersion,
            BlazorComponentReadiness.Validator.Validation.ContractVersions.PluginVersion,
            "installed plugin version matches the manifests");
    }
    finally
    {
        Environment.SetEnvironmentVariable("READINESS_SKILL_ROOT", previousSkillRoot);
    }
    AssertSequence(["./skills/"], Strings(manifest.RootElement.GetProperty("skills")), "plugin skills");
    AssertSequence(
        ["./agents/blazor-component-readiness.agent.md", "./agents/blazor-component-readiness-worker.agent.md"],
        Strings(manifest.RootElement.GetProperty("agents")),
        "plugin agents");
}

Assert(!Directory.Exists(standalonePlugin), "standalone readiness plugin must not remain installable");
AssertSequence(
    [Path.Combine(plugin, "skills", "blazor-component-readiness")],
    Directory.GetDirectories(
        Path.Combine(root, "plugins"),
        "blazor-component-readiness",
        SearchOption.AllDirectories),
    "repository readiness skill registration must be unique");
AssertSequence(
    [
        Path.Combine(plugin, "agents", "blazor-component-readiness-worker.agent.md"),
        Path.Combine(plugin, "agents", "blazor-component-readiness.agent.md")
    ],
    Directory.GetFiles(
            Path.Combine(root, "plugins"),
            "blazor-component-readiness*.agent.md",
            SearchOption.AllDirectories)
        .Order(StringComparer.Ordinal),
    "repository readiness agent registrations must be unique");

using (var version = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(plugin, "version.json"))))
{
    AssertEqual("0.1", version.RootElement.GetProperty("version").GetString(), "version base");
    AssertSequence(
        [".", ":!plugin.json", ":!.codex-plugin/plugin.json", ":!.claude-plugin/plugin.json", ":!version.json"],
        Strings(version.RootElement.GetProperty("pathFilters")),
        "canonical version path filters");
}

const string marketplaceDescription =
    "Skills and agents for Blazor development, including component authoring, interactivity, web application patterns, and explicit released component-package readiness assessments.";
var marketplacePaths = new[]
{
    Path.Combine(root, ".github", "plugin", "marketplace.json"),
    Path.Combine(root, ".claude-plugin", "marketplace.json"),
    Path.Combine(root, ".cursor-plugin", "marketplace.json"),
    Path.Combine(root, ".agents", "plugins", "marketplace.json")
};
foreach (var marketplacePath in marketplacePaths)
{
    using var marketplace = JsonDocument.Parse(File.ReadAllBytes(marketplacePath));
    var entries = marketplace.RootElement.GetProperty("plugins").EnumerateArray()
        .Where(entry => entry.GetProperty("name").GetString() == "dotnet-blazor")
        .ToArray();
    AssertEqual(1, entries.Length, $"marketplace entry count in {marketplacePath}");
    AssertEqual("./plugins/dotnet-blazor", entries[0].GetProperty("source").GetString(), "marketplace source");
    AssertEqual(marketplaceDescription, entries[0].GetProperty("description").GetString(), "marketplace description");
    Assert(
        !marketplace.RootElement.GetProperty("plugins").EnumerateArray()
            .Any(entry => entry.GetProperty("name").GetString() == "dotnet-blazor-component-readiness"),
        $"standalone marketplace entry in {marketplacePath}");
}

var readme = File.ReadAllText(Path.Combine(root, "README.md"));
Assert(
    readme.Contains(
        $"| [dotnet-blazor](plugins/dotnet-blazor/) | {marketplaceDescription} |",
        StringComparison.Ordinal),
    "README Blazor plugin row");
Assert(
    !readme.Contains("plugins/dotnet-blazor-component-readiness/", StringComparison.Ordinal),
    "README standalone plugin row must be removed");
Assert(
    readme.Contains("remove it with your", StringComparison.Ordinal) &&
    readme.Contains(
        "client's plugin manager before installing or updating `dotnet-blazor`",
        StringComparison.Ordinal),
    "legacy standalone plugin migration instruction");
var codeowners = File.ReadAllText(Path.Combine(root, ".github", "CODEOWNERS"));
Assert(codeowners.Contains("/plugins/dotnet-blazor/ @dotnet/aspnet", StringComparison.Ordinal), "plugin CODEOWNERS entry");
Assert(
    !codeowners.Contains("/plugins/dotnet-blazor-component-readiness/", StringComparison.Ordinal),
    "standalone plugin CODEOWNERS entry must be removed");
Assert(codeowners.Contains("/tests/dotnet-blazor/ @dotnet/aspnet", StringComparison.Ordinal), "tests CODEOWNERS entry");

var worker = File.ReadAllText(Path.Combine(plugin, "agents", "blazor-component-readiness-worker.agent.md"));
Assert(
    Regex.IsMatch(worker, @"(?m)^tools:\s*\[(?!\s*\])"),
    "top-level writable worker tools frontmatter must be present and non-empty");

var testGroups = new Dictionary<string, Action>(StringComparer.Ordinal)
{
    ["foundation"] = () => FoundationTests.Run(root, plugin),
    ["preparation"] = () => PreparationTests.Run(root, plugin),
    ["release-facts"] = () => ReleaseFactsTests.Run(root, plugin),
    ["evidence"] = () => EvidenceTests.Run(root, plugin),
    ["assessment"] = () => AssessmentTests.Run(root, plugin),
    ["normative"] = () => NormativeContractTests.Run(root, plugin),
    ["authorized-scope"] = () => AuthorizedScopeTests.Run(root, plugin),
    ["scoped-component"] = () => ScopedComponentTests.Run(root, plugin),
    ["reader"] = () => ReaderTests.Run(root, plugin),
    ["library"] = () => LibraryTests.Run(root, plugin),
    ["workflow"] = () => WorkflowTests.Run(plugin),
    ["archive"] = () => ArchiveCaptureTests.Run(root, plugin),
    ["enforcement"] = () => EnforcementTests.Run(root, plugin)
};
var selectedGroups = args.Length == 0
    ? testGroups.Keys.ToArray()
    : args;
if (selectedGroups.Distinct(StringComparer.Ordinal).Count() != selectedGroups.Length ||
    selectedGroups.Any(group => !testGroups.ContainsKey(group)))
{
    throw new InvalidOperationException(
        $"Test groups must be unique values from: {string.Join(", ", testGroups.Keys)}.");
}

foreach (var group in selectedGroups)
{
    testGroups[group]();
}

Console.WriteLine("All Blazor component readiness Commit 1 through Commit 8 tests passed.");

static string FindRepositoryRoot()
{
    var configuredRoot = Environment.GetEnvironmentVariable("READINESS_REPOSITORY_ROOT");
    if (!string.IsNullOrWhiteSpace(configuredRoot) && IsRepositoryRoot(configuredRoot))
    {
        return Path.GetFullPath(configuredRoot);
    }

    foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
    {
        for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
        {
            if (IsRepositoryRoot(directory.FullName))
            {
                return directory.FullName;
            }
        }
    }

    throw new InvalidOperationException("Could not locate the repository root.");
}

static bool IsRepositoryRoot(string path) =>
    File.Exists(Path.Combine(path, "README.md")) &&
    Directory.Exists(Path.Combine(path, "plugins")) &&
    Directory.Exists(Path.Combine(path, "tests"));

static string[] Expand(params (string Prefix, int Count)[] groups) =>
    groups.SelectMany(group => Enumerable.Range(1, group.Count).Select(number => $"{group.Prefix}-{number:00}")).ToArray();

static string[] Strings(JsonElement array) =>
    array.EnumerateArray().Select(item => item.GetString() ?? throw new InvalidDataException("Expected a string.")).ToArray();

static string Id(JsonElement requirement) =>
    requirement.GetProperty("id").GetString() ?? throw new InvalidDataException("Requirement ID is missing.");

static string Requirement(JsonElement requirement) =>
    requirement.GetProperty("requirement").GetString() ?? throw new InvalidDataException("Requirement wording is missing.");

static string Scope(JsonElement requirement) =>
    requirement.GetProperty("scope").GetString() ?? throw new InvalidDataException("Requirement scope is missing.");

static string RequirementDigest(IEnumerable<JsonElement> requirements)
{
    var projection = string.Concat(requirements.Select(requirement =>
        $"{Id(requirement)}\t{Requirement(requirement)}\t{Scope(requirement)}\n"));
    return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(projection)));
}

static void AssertSet(IReadOnlySet<string> expected, IReadOnlySet<string> actual, string name)
{
    Assert(
        expected.SetEquals(actual),
        $"{name} mismatch. Expected [{string.Join(", ", expected.Order())}], actual [{string.Join(", ", actual.Order())}].");
}

static void AssertSequence<T>(IEnumerable<T> expected, IEnumerable<T> actual, string name)
{
    var expectedArray = expected.ToArray();
    var actualArray = actual.ToArray();
    Assert(
        expectedArray.SequenceEqual(actualArray),
        $"{name} mismatch. Expected [{string.Join(", ", expectedArray)}], actual [{string.Join(", ", actualArray)}].");
}

static void AssertEqual<T>(T expected, T actual, string name) =>
    Assert(EqualityComparer<T>.Default.Equals(expected, actual), $"{name}: expected '{expected}', actual '{actual}'.");

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}
