using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BlazorComponentReadiness.Validator.Contracts;
using BlazorComponentReadiness.Validator.IO;

namespace BlazorComponentReadiness.Validator.Validation;

public static class RubricLoader
{
    public const string CurrentVersion = "2.0.1";
    public const string LegacyVersion = "1.3.0";
    internal const string RequirementBasisFilename = "requirement-basis.json";
    private const string CurrentRubricDigest =
        "260c646feb6b9c89ba46a15362ea0b6f5ed632891c330934915b916e7125cc0b";
    private const string CurrentCoreDigest =
        "d48756ed60c90b510b215e8dcdcb28523c0aca6de2a8a1d01368e31dbd45022d";
    private const string CurrentScopeDigest =
        "6e949e7018070880972909990f685bc048cc5f4f391024fc0ff4e224aeba4c0a";
    private const string ExpectedCrosswalkDigest =
        "b987b982163f2253a28d9d46e85073ceec8e48f4755bd45c35d96e4bc68a94ad";
    private const string ExpectedRubricDigest =
        "6a36fc581af3a2710fec3a70a484c7dc751cdf2c248dcd98e49ce4bb9cf8c49f";
    private const string ExpectedCoreDigest =
        "bca63be737c7a02d56bc40387ca1e56b1e07ce6dd7146e5b6bfa9fa165045382";
    private const string ExpectedScopeDigest =
        "023a6204a20be9a6558cdba8b284a1729ffe0439781c1c73b94d577fe66279d5";
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
        if (rubricVersion is not (CurrentVersion or LegacyVersion))
        {
            throw new DeterministicValidationException($"Unsupported rubric version '{rubricVersion}'.");
        }

        var legacy = rubricVersion == LegacyVersion;
        var rubricPath = ResolveReferencePath(legacy ? "rubric.v1.3.0.json" : "rubric.json");
        var bytes = BoundedIO.ReadAllBytes(
            rubricPath,
            ResourceLimits.SerializedArtifactBytes,
            "rubric");
        using var document = StrictJson.Parse(bytes, ResourceLimits.SerializedArtifactBytes, "rubric");
        var root = document.RootElement;
        var properties = new[]
        {
            "schema_version", "rubric_version", "scope_schema_version", "positioning",
            "statuses", "core", "overlays"
        };
        ContractJson.RequireProperties(
            root,
            legacy ? properties :
                ["schema_version", "rubric_version", "scope_schema_version", "positioning",
                    "statuses", "crosswalk", "core", "overlays", "extensions"]);
        if (ContractJson.Int32(root, "schema_version") != (legacy ? 1 : 2) ||
            ContractJson.String(root, "rubric_version") != rubricVersion ||
            ContractJson.Int32(root, "scope_schema_version") != (legacy ? 1 : 2))
        {
            throw new DeterministicValidationException("Rubric version and schema binding is invalid.");
        }

        var statuses = ContractJson.StringArray(root, "statuses");
        if (!statuses.SequenceEqual(ExpectedStatuses, StringComparer.Ordinal))
        {
            throw new DeterministicValidationException("Rubric status vocabulary has drifted.");
        }

        var clauses = legacy ? null : LoadCrosswalk(root);
        var coreObject = ContractJson.Object(root, "core");
        ContractJson.RequireProperties(coreObject, "name", "requirements");
        _ = ContractJson.NormalizeText(ContractJson.String(coreObject, "name"), "rubric core name", 256);
        var core = ContractJson.Array(coreObject, "requirements")
            .EnumerateArray()
            .Select(element => ParseCoreRequirement(element, clauses))
            .ToArray();
        var expectedCount = legacy ? 110 : 121;
        if (core.Length != expectedCount ||
            core.Count(requirement => requirement.Scope == "repository-wide") != (legacy ? 46 : 60) ||
            core.Count(requirement => requirement.Scope == "component-specific") != (legacy ? 64 : 61) ||
            core.Select(requirement => requirement.Id).Distinct(StringComparer.Ordinal).Count() != expectedCount)
        {
            throw new DeterministicValidationException("Rubric canonical inventory or ledger ownership has drifted.");
        }

        var overlays = ContractJson.Array(root, "overlays")
            .EnumerateArray()
            .Select(ParseOverlay)
            .ToArray();
        if (!overlays.Select(overlay => overlay.Id)
                .SequenceEqual(legacy ? new[] { "scaffolder", "ai-skill" } : [], StringComparer.Ordinal))
        {
            throw new DeterministicValidationException("Only the frozen legacy rubric permits selected overlays.");
        }

        var extensions = legacy ? [] : ContractJson.Array(root, "extensions")
            .EnumerateArray().Select(element => ParseCoreRequirement(element, clauses)).ToArray();
        if (extensions.Any(row => row.Basis?.RequiresPolicyApproval != true) ||
            extensions.Select(row => row.Id).Distinct(StringComparer.Ordinal).Count() != extensions.Length ||
            extensions.Any(row => core.Any(canonical => canonical.Id == row.Id)))
        {
            throw new DeterministicValidationException("Supplementary extensions cannot duplicate canonical IDs.");
        }

        var rubricDigest = ContractJson.RawDigest(bytes);
        var coreDigest = ProjectionDigest(core, includeScope: true);
        var scopeDigest = ScopeDigest(core);
        if (rubricDigest.Value != (legacy ? ExpectedRubricDigest : CurrentRubricDigest) ||
            coreDigest.Value != (legacy ? ExpectedCoreDigest : CurrentCoreDigest) ||
            scopeDigest.Value != (legacy ? ExpectedScopeDigest : CurrentScopeDigest))
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
            overlays,
            legacy ? null : new Sha256Digest("sha256", ExpectedCrosswalkDigest),
            extensions);
    }

    public static IReadOnlyList<RubricRequirement> Select(
        RubricContract rubric,
        string kind,
        IReadOnlyList<string> selectedOverlays)
    {
        var core = kind switch
        {
            "unified" => rubric.CoreRequirements,
            "package" => rubric.CoreRequirements.Where(row => row.Scope == "repository-wide").ToArray(),
            "component" => rubric.CoreRequirements.Where(row => row.Scope == "component-specific").ToArray(),
            _ => throw new DeterministicValidationException($"Unknown assessment kind '{kind}'.")
        };
        var overlaySet = selectedOverlays.ToHashSet(StringComparer.Ordinal);
        if (overlaySet.Count != selectedOverlays.Count)
        {
            throw new DeterministicValidationException("Selected overlays cannot contain duplicates.");
        }

        var unknown = overlaySet.Except(rubric.Overlays.Select(overlay => overlay.Id), StringComparer.Ordinal).ToArray();
        if (unknown.Length > 0)
        {
            throw new DeterministicValidationException($"Unknown overlay '{unknown[0]}'.");
        }

        return core.Concat(rubric.Overlays
                .Where(overlay => overlaySet.Contains(overlay.Id))
                .SelectMany(overlay => overlay.Requirements))
            .ToArray();
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
        IReadOnlyDictionary<string, string>? clauses)
    {
        var properties = new[] { "id", "requirement", "scope", "area" };
        if (clauses is not null)
        {
            properties = [.. properties, "clause", "classification",
                .. new[] { "semantic_scope", "shared_action_key", "conditional_family", "extension_basis", "additional_clauses" }
                    .Where(name => element.TryGetProperty(name, out _))];
        }

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
            clauses is null ? null : ParseBasis(element, scope, clauses));
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

    private static RubricOverlay ParseOverlay(JsonElement element)
    {
        ContractJson.RequireProperties(element, "id", "name", "version", "selection", "requirements");
        var id = ContractJson.String(element, "id");
        if (ContractJson.String(element, "selection") != "explicit")
        {
            throw new DeterministicValidationException("Overlay selection must be explicit.");
        }

        var name = ContractJson.String(element, "name");
        var requirements = ContractJson.Array(element, "requirements").EnumerateArray().Select(row =>
        {
            ContractJson.RequireProperties(row, "id", "requirement");
            return new RubricRequirement(
                ContractJson.String(row, "id"),
                ContractJson.String(row, "requirement"),
                "overlay",
                name,
                id);
        }).ToArray();
        if (requirements.Length != 6)
        {
            throw new DeterministicValidationException($"Overlay '{id}' must contain six requirements.");
        }

        return new RubricOverlay(
            id,
            name,
            ContractJson.String(element, "version"),
            ProjectionDigest(requirements, includeScope: false),
            requirements);
    }

    private static Sha256Digest ProjectionDigest(
        IEnumerable<RubricRequirement> requirements,
        bool includeScope)
    {
        var projection = string.Concat(requirements.Select(requirement =>
            includeScope
                ? $"{requirement.Id}\t{requirement.Requirement}\t{requirement.Scope}\n"
                : $"{requirement.Id}\t{requirement.Requirement}\n"));
        return ContractJson.RawDigest(Encoding.UTF8.GetBytes(projection));
    }

    private static Sha256Digest ScopeDigest(IEnumerable<RubricRequirement> requirements)
    {
        var projection = string.Concat(requirements.Select(requirement =>
            $"{requirement.Id}\t{requirement.Scope}\n"));
        return ContractJson.RawDigest(Encoding.UTF8.GetBytes(projection));
    }
}
