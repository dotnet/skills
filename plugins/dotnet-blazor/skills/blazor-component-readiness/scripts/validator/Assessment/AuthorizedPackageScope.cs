using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using BlazorComponentReadiness.Validator.Contracts;
using BlazorComponentReadiness.Validator.Evidence;
using BlazorComponentReadiness.Validator.Inputs;
using BlazorComponentReadiness.Validator.IO;
using BlazorComponentReadiness.Validator.Validation;

namespace BlazorComponentReadiness.Validator.Assessment;

internal sealed class AuthorizedPackageScope
{
    internal const string Kind = "authorized-package-report-scope";
    internal const string Filename = "authorized-package-report-scope.json";
    private const string ScopeId = "bundled-package-48-v1";
    private const string SelectionSha256 =
        "942a4c28425cc4a34269f1c564422d5cc87c389717f3f881ada38c3fd7c1feb0";
    private readonly byte[] _bytes;
    private readonly Regex _excludedIds;

    private AuthorizedPackageScope(
        byte[] bytes,
        Sha256Digest definitionDigest,
        EvidencePackageIdentity package,
        InputSource source,
        IReadOnlyList<RubricRequirement> requirements,
        Sha256Digest selectedSetDigest,
        IEnumerable<string> excludedIds)
    {
        _bytes = bytes;
        DefinitionDigest = definitionDigest;
        Package = package;
        Source = source;
        Requirements = requirements;
        SelectedSetDigest = selectedSetDigest;
        _excludedIds = new Regex(
            @"(?<![A-Z0-9])(?:" + string.Join("|", excludedIds.Select(Regex.Escape)) + @")(?![A-Z0-9])",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    }

    internal Sha256Digest DefinitionDigest { get; }
    internal EvidencePackageIdentity Package { get; }
    internal InputSource Source { get; }
    internal IReadOnlyList<RubricRequirement> Requirements { get; }
    internal Sha256Digest SelectedSetDigest { get; }
    internal Sha256Digest ManifestDigest => ContractJson.RawDigest(_bytes);
    internal byte[] CopyBytes() => (byte[])_bytes.Clone();

    internal string Declaration =>
        $"**Requirement basis:** bundled `{RubricLoader.CurrentVersion}` (SHA-256 `{DefinitionDigest.Value}`).\n" +
        $"**Authorized package-report scope:** `{ScopeId}`; {Requirements.Count} bundled package requirements, not full coverage.\n" +
        $"**Scope manifest SHA-256:** `{ManifestDigest.Value}`.\n" +
        $"**Selected-set SHA-256:** `{SelectedSetDigest.Value}`.\n" +
        "Scope metadata establishes neither publisher approval nor permission to execute.";

    internal static bool IsRequested(InputManifest input) =>
        input.EvidenceInputs.Any(IsScopeInput);

    private static bool IsScopeInput(InputEvidenceArtifact item) =>
        item.Kind.StartsWith(Kind, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(item.Basename, Filename, StringComparison.OrdinalIgnoreCase);

    internal static AuthorizedPackageScope? Load(string? root, InputManifest input)
    {
        var entries = input.EvidenceInputs.Where(IsScopeInput).ToArray();
        if (entries.Length == 0)
        {
            return null;
        }

        if (entries is not [{ Kind: Kind, Basename: Filename }] || root is null)
        {
            throw new DeterministicValidationException(
                "Authorized package scope requires exactly one supported retained manifest and an input root; it cannot fall back to ordinary selection.");
        }

        var entry = entries[0];
        var path = SafePath.ResolveUnderRoot(root, entry.Basename, requireExisting: true, requireFile: true);
        var bytes = BoundedIO.ReadAllBytes(path, ResourceLimits.SerializedArtifactBytes, "authorized package scope");
        if (entry.Size != bytes.LongLength || entry.ContentDigest != ContractJson.RawDigest(bytes))
        {
            throw new DeterministicValidationException("Authorized package scope differs from its confirmed input binding.");
        }

        var scope = Parse(bytes);
        if (scope.Package != new EvidencePackageIdentity(
                input.Package.PackageId, input.Package.Version, input.Package.NupkgDigest) ||
            scope.Source != input.Source)
        {
            throw new DeterministicValidationException("Authorized package scope belongs to a different exact package or source.");
        }

        return scope;
    }

    internal static byte[] Create(string root, InputManifest input)
    {
        InputManifestService.Validate(input, root, requireConfirmed: true);
        if (input.SchemaVersion != InputManifestService.SchemaVersion ||
            IsRequested(input) || ScopedComponentProfile.IsRequested(input))
        {
            throw new DeterministicValidationException(
                "Create a package scope from unscoped confirmed inputs, then register and confirm the new descriptor.");
        }

        var rubric = RubricLoader.Load();
        var selected = RubricLoader.Select(rubric, "package", [])
            .Where(row => row.Basis is { RequiresPolicyApproval: false }).ToArray();
        var bytes = Serialize(
            new(input.Package.PackageId, input.Package.Version, input.Package.NupkgDigest),
            input.Source, rubric, selected);
        _ = Parse(bytes);
        return bytes;
    }

    internal static AuthorizedPackageScope Parse(ReadOnlyMemory<byte> bytes)
    {
        using var document = StrictJson.Parse(bytes, ResourceLimits.SerializedArtifactBytes, "authorized package scope");
        var value = document.RootElement;
        ContractJson.RequireProperties(value, "schema_version", "kind", "scope_id", "requirement_source",
            "package", "source", "rubric_version", "selected_ids", "row_count", "selected_set_sha256");
        var definition = ContractJson.Object(value, "requirement_source");
        ContractJson.RequireProperties(definition, "filename", "sha256");
        var package = ContractJson.Object(value, "package");
        ContractJson.RequireProperties(package, "package_id", "version", "nupkg_sha256");
        var identity = new EvidencePackageIdentity(
            Canonicalization.PackageId(ContractJson.String(package, "package_id")),
            NuGetVersionNormalizer.Normalize(ContractJson.String(package, "version")),
            ContractJson.Digest(ContractJson.Object(package, "nupkg_sha256")));
        var source = InputManifestService.ParseSource(ContractJson.Object(value, "source"));
        InputManifestService.ValidateSource(source);

        var rubric = RubricLoader.Load(ContractJson.String(value, "rubric_version"));
        var packageRows = RubricLoader.Select(rubric, "package", []);
        var selected = packageRows.Where(row => row.Basis is { RequiresPolicyApproval: false }).ToArray();
        var ids = ContractJson.StringArray(value, "selected_ids");
        var selectedDigest = ContractJson.Digest(ContractJson.Object(value, "selected_set_sha256"));
        var idBytes = StrictJson.SerializeCanonical(writer =>
        {
            writer.WriteStartArray();
            foreach (var id in ids)
            {
                writer.WriteStringValue(id);
            }

            writer.WriteEndArray();
        });
        var definitionDigest = ContractJson.Digest(ContractJson.Object(definition, "sha256"));
        if (ContractJson.Int32(value, "schema_version") != 2 ||
            ContractJson.String(value, "kind") != Kind ||
            ContractJson.String(value, "scope_id") != ScopeId ||
            ContractJson.String(definition, "filename") != RubricLoader.RequirementBasisFilename ||
            definitionDigest != rubric.CrosswalkDigest ||
            rubric.RubricVersion != RubricLoader.CurrentVersion ||
            selected.Length != 48 ||
            ContractJson.Int32(value, "row_count") != selected.Length ||
            !ids.SequenceEqual(selected.Select(row => row.Id), StringComparer.Ordinal) ||
            selectedDigest.Value != SelectionSha256 ||
            selectedDigest != ContractJson.RawDigest(idBytes))
        {
            throw new DeterministicValidationException(
                "Authorized package scope must contain exactly the approved bundled package IDs in canonical order, with matching count and selected-set digest.");
        }

        ContractJson.RequireCanonical(bytes.Span, Serialize(identity, source, rubric, selected), "authorized package scope");

        return new AuthorizedPackageScope(
            bytes.ToArray(),
            definitionDigest,
            identity,
            source,
            selected,
            selectedDigest,
            packageRows.Where(row => row.Basis?.RequiresPolicyApproval == true).Select(row => row.Id));
    }

    private static byte[] Serialize(
        EvidencePackageIdentity package, InputSource source, RubricContract rubric,
        IReadOnlyList<RubricRequirement> selected) =>
        StrictJson.SerializeCanonical(writer =>
        {
            writer.WriteStartObject();
            writer.WriteNumber("schema_version", 2);
            writer.WriteString("kind", Kind);
            writer.WriteString("scope_id", ScopeId);
            writer.WritePropertyName("requirement_source");
            writer.WriteStartObject();
            writer.WriteString("filename", RubricLoader.RequirementBasisFilename);
            ContractJson.WriteDigest(writer, "sha256", rubric.CrosswalkDigest ??
                throw new DeterministicValidationException("Scoped package selection requires the frozen bundled basis."));
            writer.WriteEndObject();
            writer.WritePropertyName("package");
            writer.WriteStartObject();
            writer.WriteString("package_id", package.PackageId);
            writer.WriteString("version", package.Version);
            ContractJson.WriteDigest(writer, "nupkg_sha256", package.NupkgDigest);
            writer.WriteEndObject();
            writer.WritePropertyName("source");
            InputManifestService.WriteSource(writer, source);
            writer.WriteString("rubric_version", rubric.RubricVersion);
            writer.WritePropertyName("selected_ids");
            writer.WriteStartArray();
            foreach (var row in selected) writer.WriteStringValue(row.Id);
            writer.WriteEndArray();
            writer.WriteNumber("row_count", selected.Count);
            ContractJson.WriteDigest(writer, "selected_set_sha256", new("sha256", SelectionSha256));
            writer.WriteEndObject();
        });

    internal IReadOnlyList<RubricRequirement> Select(RubricContract rubric, string assessmentKind)
    {
        if (assessmentKind != "package" || rubric.RubricVersion != RubricLoader.CurrentVersion)
        {
            throw new DeterministicValidationException(
                "Authorized package scope cannot be applied to a component, unified assessment, or another rubric.");
        }

        return Requirements;
    }

    internal void Validate(ReadinessAssessment assessment, InputManifest input, EvidenceBundle evidence)
    {
        var rubric = RubricLoader.Load();
        var expectedIds = Requirements.Select(row => row.Id).ToArray();
        if (assessment.AssessmentKind != "package" ||
            assessment.SchemaVersion != AssessmentService.SchemaVersion ||
            assessment.RubricVersion != RubricLoader.CurrentVersion ||
            assessment.ScopeSchemaVersion != rubric.ScopeSchemaVersion ||
            assessment.RubricDigest != rubric.RubricDigest ||
            assessment.ScopeMapDigest != rubric.ScopeMapDigest ||
            assessment.Overlays.Count != 0 ||
            assessment.Identity.AssessmentKind != "package" ||
            assessment.Identity.ComponentId is not null ||
            assessment.PackageReference is not null ||
            assessment.Identity.Package != Package ||
            input.Source != Source ||
            assessment.Identity.InputManifestDigest != InputManifestService.Digest(InputManifestService.Serialize(input)) ||
            evidence.Assessment != assessment.Identity ||
            !assessment.SelectedIds.SequenceEqual(expectedIds, StringComparer.Ordinal) ||
            !assessment.Rows.Select(row => row.Id).SequenceEqual(expectedIds, StringComparer.Ordinal))
        {
            throw new DeterministicValidationException("Assessment and evidence do not represent the exact authorized package scope.");
        }

        if (assessment.Rows.Zip(Requirements).Any(pair =>
                pair.First.Requirement != pair.Second.Requirement ||
                pair.First.Scope != pair.Second.Scope ||
                pair.First.Area != pair.Second.Area))
        {
            throw new DeterministicValidationException("Authorized assessment has canonical requirement, ownership or area drift.");
        }

        var records = evidence.SourceLedgers.SelectMany(item => item.Ledger.Records).ToArray();
        var selected = evidence.Selection.Select(item => item.EvidenceId).ToHashSet(StringComparer.Ordinal);
        var used = assessment.Rows.SelectMany(row => row.EvidenceIds).ToHashSet(StringComparer.Ordinal);
        if (records.Length != selected.Count ||
            !records.Select(record => record.StableId).ToHashSet(StringComparer.Ordinal).SetEquals(selected) ||
            !used.SetEquals(selected))
        {
            throw new DeterministicValidationException(
                "Authorized scope requires a selected-only evidence companion. Keep complete historical ledgers and unselected records internal.");
        }

        RejectDisclosure(Encoding.UTF8.GetString(AssessmentService.Serialize(assessment)));
        RejectDisclosure(Encoding.UTF8.GetString(InputManifestService.Serialize(input)));
        RejectDisclosure(Encoding.UTF8.GetString(CanonicalEvidenceJson.SerializeBundle(evidence)));
    }

    internal void RejectDisclosure(string text)
    {
        var decoded = WebUtility.HtmlDecode(text)
            .Replace('\u2010', '-').Replace('\u2011', '-').Replace('\u2013', '-');
        if (_excludedIds.IsMatch(decoded) ||
            decoded.Contains("versioned extension", StringComparison.OrdinalIgnoreCase) ||
            Regex.IsMatch(decoded,
                @"\b(?:60|121)\s*[- ]\s*(?:canonical\s+)?(?:package\s+)?(?:rows?|checks?|requirements?|findings?)\b|\b12\s*[- ]\s*(?:excluded\s+)?extensions?\b",
                RegexOptions.CultureInvariant | RegexOptions.IgnoreCase))
        {
            throw new DeterministicValidationException("Internal extension material cannot appear in authorized partner output.");
        }
    }
}
