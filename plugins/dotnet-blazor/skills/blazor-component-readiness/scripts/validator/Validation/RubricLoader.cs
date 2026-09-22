using System.Text;
using System.Text.Json;
using BlazorComponentReadiness.Validator.Contracts;
using BlazorComponentReadiness.Validator.IO;

namespace BlazorComponentReadiness.Validator.Validation;

public static class RubricLoader
{
    public const string CurrentVersion = "2.1.0";
    internal const string RequirementBasisFilename = "requirement-basis.json";
    private const string CurrentRubricDigest =
        "0153ba6982fd1102df9193d3bc2b1957bac1e3478bd0d6cef5a78b0e6daa31cd";
    private const string CurrentCoreDigest =
        "2edf382ccaa972a3f15ce1122a51499ef06ef0d1a948e116447853e9eb7bae3e";
    private const string CurrentScopeDigest =
        "f324b3db1217d7aade766fad3ce983c96fdf88182f35c26ac314d4672c79e13e";
    private const string ExpectedCrosswalkDigest =
        "5f39232d19fce5c5ea40e3f6949b3b4f695fed616a75446e3a42e8015c05cd5e";
    private static readonly string[] ExpectedStatuses =
    [
        "verified",
        "gap",
        "owner evidence required",
        "not tested",
        "not applicable"
    ];

    public static RubricContract Load(string? rubricVersion = null)
    {
        rubricVersion ??= CurrentVersion;
        if (rubricVersion != CurrentVersion)
        {
            throw new DeterministicValidationException($"Unsupported rubric version '{rubricVersion}'.");
        }

        var rubricPath = ResolveReferencePath("rubric.json");
        var bytes = BoundedIO.ReadAllBytes(
            rubricPath,
            ResourceLimits.SerializedArtifactBytes,
            "rubric");
        using var document = StrictJson.Parse(bytes, ResourceLimits.SerializedArtifactBytes, "rubric");
        var root = document.RootElement;
        ContractJson.RequireProperties(
            root,
            "schema_version", "rubric_version", "scope_schema_version", "positioning",
            "statuses", "crosswalk", "core", "overlays", "extensions");
        if (ContractJson.Int32(root, "schema_version") != 2 ||
            ContractJson.String(root, "rubric_version") != rubricVersion ||
            ContractJson.Int32(root, "scope_schema_version") != 2)
        {
            throw new DeterministicValidationException("Rubric version and schema binding is invalid.");
        }

        var statuses = ContractJson.StringArray(root, "statuses");
        if (!statuses.SequenceEqual(ExpectedStatuses, StringComparer.Ordinal))
        {
            throw new DeterministicValidationException("Rubric status vocabulary has drifted.");
        }

        var clauses = LoadCrosswalk(root);
        var coreObject = ContractJson.Object(root, "core");
        ContractJson.RequireProperties(coreObject, "name", "requirements");
        _ = ContractJson.NormalizeText(ContractJson.String(coreObject, "name"), "rubric core name", 256);
        var core = ContractJson.Array(coreObject, "requirements")
            .EnumerateArray()
            .Select(element => ParseCoreRequirement(element, clauses))
            .ToArray();
        const int expectedCount = 112;
        if (core.Length != expectedCount ||
            core.Count(requirement => requirement.Scope == "repository-wide") != 60 ||
            core.Count(requirement => requirement.Scope == "component-specific") != 52 ||
            core.Select(requirement => requirement.Id).Distinct(StringComparer.Ordinal).Count() != expectedCount)
        {
            throw new DeterministicValidationException("Rubric canonical inventory or ledger ownership has drifted.");
        }

        if (ContractJson.Array(root, "overlays").GetArrayLength() != 0)
        {
            throw new DeterministicValidationException("The current rubric cannot contain selected overlays.");
        }

        var extensions = ContractJson.Array(root, "extensions")
            .EnumerateArray().Select(element => ParseCoreRequirement(element, clauses)).ToArray();
        if (extensions.Any(row => row.Basis?.RequiresPolicyApproval != true) ||
            extensions.Select(row => row.Id).Distinct(StringComparer.Ordinal).Count() != extensions.Length ||
            extensions.Any(row => core.Any(canonical => canonical.Id == row.Id)))
        {
            throw new DeterministicValidationException("Supplementary extensions cannot duplicate canonical IDs.");
        }

        var rubricDigest = ContractJson.RawDigest(bytes);
        var coreDigest = ProjectionDigest(core);
        var scopeDigest = ScopeDigest(core);
        if (rubricDigest.Value != CurrentRubricDigest ||
            coreDigest.Value != CurrentCoreDigest ||
            scopeDigest.Value != CurrentScopeDigest)
        {
            throw new DeterministicValidationException(
                "Rubric/checklist contract digest has drifted from its frozen version.");
        }

        return new RubricContract(
            ContractJson.String(root, "rubric_version"),
            ContractJson.Int32(root, "scope_schema_version"),
            ContractJson.String(root, "positioning"),
            statuses,
            rubricDigest,
            coreDigest,
            scopeDigest,
            core,
            [],
            new Sha256Digest("sha256", ExpectedCrosswalkDigest),
            extensions);
    }

    public static IReadOnlyList<RubricRequirement> Select(
        RubricContract rubric,
        string kind,
        IReadOnlyList<string> selectedOverlays)
    {
        if (selectedOverlays.Count != 0)
        {
            throw new DeterministicValidationException("Current assessments require an empty overlays array.");
        }

        var core = kind switch
        {
            "package" => rubric.CoreRequirements.Where(row => row.Scope == "repository-wide").ToArray(),
            "component" => rubric.CoreRequirements.Where(row => row.Scope == "component-specific").ToArray(),
            _ => throw new DeterministicValidationException($"Unknown assessment kind '{kind}'.")
        };
        return core.ToArray();
    }

    internal static void RequireSupportedKind(string kind)
    {
        if (kind is not ("package" or "component"))
        {
            throw new DeterministicValidationException(
                "Only separate package and component assessments are supported; unified assessments are retired.");
        }
    }

    private static string ResolveReferencePath(string filename)
    {
        var skillRoot = Environment.GetEnvironmentVariable("READINESS_SKILL_ROOT");
        if (string.IsNullOrWhiteSpace(skillRoot))
        {
            throw new DeterministicValidationException(
                "READINESS_SKILL_ROOT must identify the loaded blazor-component-readiness skill.");
        }

        var fullRoot = Path.GetFullPath(skillRoot);
        if (!File.Exists(Path.Combine(fullRoot, "SKILL.md")))
        {
            throw new DeterministicValidationException("READINESS_SKILL_ROOT does not contain SKILL.md.");
        }

        return SafePath.ResolveUnderRoot(
            fullRoot,
            $"references/{filename}",
            requireExisting: true,
            requireFile: true);
    }

    private static RubricRequirement ParseCoreRequirement(
        JsonElement element,
        IReadOnlyDictionary<string, string> clauses)
    {
        string[] properties = ["id", "requirement", "scope", "area", "clause", "classification",
            .. new[] { "semantic_scope", "shared_action_key", "conditional_family", "extension_basis", "additional_clauses" }
                .Where(name => element.TryGetProperty(name, out _))];

        ContractJson.RequireProperties(element, properties);
        var scope = ContractJson.String(element, "scope");
        if (scope is not ("repository-wide" or "component-specific"))
        {
            throw new DeterministicValidationException($"Invalid rubric scope '{scope}'.");
        }

        return new RubricRequirement(
            ContractJson.NormalizeText(ContractJson.String(element, "id"), "requirement id", 16),
            ContractJson.NormalizeText(ContractJson.String(element, "requirement"), "requirement wording", 1024),
            scope,
            ContractJson.NormalizeText(ContractJson.String(element, "area"), "requirement area", 256),
            null,
            ParseBasis(element, scope, clauses));
    }

    private static IReadOnlyDictionary<string, string> LoadCrosswalk(JsonElement rubric)
    {
        if (ContractJson.String(rubric, "crosswalk") != RequirementBasisFilename)
        {
            throw new DeterministicValidationException("The normative crosswalk reference is invalid.");
        }

        var bytes = BoundedIO.ReadAllBytes(
            ResolveReferencePath(RequirementBasisFilename),
            ResourceLimits.SerializedArtifactBytes, "bundled requirement basis");
        if (ContractJson.RawDigest(bytes).Value != ExpectedCrosswalkDigest)
        {
            throw new DeterministicValidationException("bundled requirement basis digest has drifted from its frozen version.");
        }

        using var document = StrictJson.Parse(bytes, ResourceLimits.SerializedArtifactBytes, "bundled requirement basis");
        var root = document.RootElement;
        ContractJson.RequireProperties(root, "schema_version", "version", "source", "clauses");
        if (ContractJson.Int32(root, "schema_version") != 2 ||
            ContractJson.String(root, "version") != CurrentVersion)
        {
            throw new DeterministicValidationException("bundled requirement basis version is invalid.");
        }

        var source = ContractJson.Object(root, "source");
        ContractJson.RequireProperties(source, "description");
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var clause in ContractJson.Array(root, "clauses").EnumerateArray())
        {
            ContractJson.RequireProperties(clause, "id", "quotation");
            if (!result.TryAdd(
                ContractJson.String(clause, "id"),
                ContractJson.String(clause, "quotation")))
            {
                throw new DeterministicValidationException("Duplicate normative clause.");
            }
        }

        return result;
    }

    private static RequirementBasis ParseBasis(
        JsonElement element, string ledgerScope, IReadOnlyDictionary<string, string> clauses)
    {
        var clause = ContractJson.String(element, "clause");
        var classification = ContractJson.String(element, "classification");
        if (!clauses.TryGetValue(clause, out var quotation) ||
            classification is not ("direct obligation" or "decomposition evidence check" or
                "conditional obligation" or "versioned extension"))
        {
            throw new DeterministicValidationException("Requirement has an invalid requirement clause/classification.");
        }

        string? Optional(string name) => element.TryGetProperty(name, out _)
            ? ContractJson.String(element, name) : null;
        var extension = Optional("extension_basis");
        var semanticScope = Optional("semantic_scope") ?? ledgerScope;
        if ((classification == "versioned extension") != (extension is not null) ||
            semanticScope is not ("repository-wide" or "component-specific"))
        {
            throw new DeterministicValidationException("Requirement extension basis or semantic scope is invalid.");
        }

        var additional = element.TryGetProperty("additional_clauses", out _)
            ? ContractJson.StringArray(element, "additional_clauses") : [];
        if (additional.Any(id => !clauses.ContainsKey(id)) ||
            additional.Distinct(StringComparer.Ordinal).Count() != additional.Count)
        {
            throw new DeterministicValidationException("Additional normative clauses are invalid.");
        }

        return new RequirementBasis(
            clause, quotation, classification, semanticScope, Optional("shared_action_key"),
            Optional("conditional_family"), extension,
            additional.Select(id => new NormativeClause(id, clauses[id])).ToArray());
    }

    private static Sha256Digest ProjectionDigest(
        IEnumerable<RubricRequirement> requirements)
    {
        var projection = string.Concat(requirements.Select(requirement =>
            $"{requirement.Id}\t{requirement.Requirement}\t{requirement.Scope}\n"));
        return ContractJson.RawDigest(Encoding.UTF8.GetBytes(projection));
    }

    private static Sha256Digest ScopeDigest(IEnumerable<RubricRequirement> requirements)
    {
        var projection = string.Concat(requirements.Select(requirement =>
            $"{requirement.Id}\t{requirement.Scope}\n"));
        return ContractJson.RawDigest(Encoding.UTF8.GetBytes(projection));
    }
}
