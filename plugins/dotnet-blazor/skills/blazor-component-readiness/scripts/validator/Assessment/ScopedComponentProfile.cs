using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using BlazorComponentReadiness.Validator.Contracts;
using BlazorComponentReadiness.Validator.Evidence;
using BlazorComponentReadiness.Validator.Inputs;
using BlazorComponentReadiness.Validator.IO;
using BlazorComponentReadiness.Validator.Validation;

namespace BlazorComponentReadiness.Validator.Assessment;

internal sealed class ScopedComponentProfile
{
    internal const string Kind = "scoped-component-profile-v1";
    internal const string Filename = RubricLoader.RequirementBasisFilename;
    internal const string ContextKind = "scoped-package-context-v1";
    internal const string ContextFilename = "scoped-package.validation.json";
    private const string SelectionSha256 = "2f0c862398f58aa0cef426cb8a2b4c46fa33e2d907a294bd46d7f1fa3b1f4122";
    private readonly InputEvidenceArtifact _context;
    private readonly Sha256Digest _definitionDigest;
    private readonly Regex _excludedIds;
    private readonly string[] _excludedRequirements;

    private ScopedComponentProfile(RubricContract rubric, InputEvidenceArtifact context)
    {
        _context = context;
        _definitionDigest = rubric.CrosswalkDigest ??
            throw new DeterministicValidationException("Scoped component selection requires the frozen bundled basis.");
        Requirements = rubric.CoreRequirements
            .Where(row => row.Scope == "component-specific" && row.Basis is { RequiresPolicyApproval: false })
            .ToArray();
        var ids = StrictJson.SerializeCanonical(writer =>
        {
            writer.WriteStartArray();
            foreach (var row in Requirements) writer.WriteStringValue(row.Id);
            writer.WriteEndArray();
        });
        if (Requirements.Count != 51 || ContractJson.RawDigest(ids).Value != SelectionSha256)
            throw new DeterministicValidationException("The closed Scoped-component profile selection has drifted.");
        var selected = Requirements.Select(row => row.Id).ToHashSet(StringComparer.Ordinal);
        var excluded = rubric.CoreRequirements.Concat(rubric.Extensions ?? [])
            .Where(row => !selected.Contains(row.Id)).ToArray();
        _excludedIds = new Regex(
            @"(?<![A-Z0-9])(?:" + string.Join("|", excluded.Select(row => Regex.Escape(row.Id))) + @")(?![A-Z0-9])",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        _excludedRequirements = excluded.Select(row => row.Requirement).ToArray();
    }

    internal IReadOnlyList<RubricRequirement> Requirements { get; }
    internal string Declaration =>
        $"**Requirement basis:** bundled `{RubricLoader.CurrentVersion}` (SHA-256 `{_definitionDigest.Value}`).\n" +
        $"**Scoped-component scope:** `{Kind}`; exactly 51 requirement-backed, non-extension component checks, not full coverage.\n" +
        $"**Selected-set SHA-256:** `{SelectionSha256}`.\n" +
        $"**Scoped package context:** validation SHA-256 `{_context.ContentDigest.Value}`; " +
        "context only, not an ordinary full-package prerequisite or evidence of component behavior.";

    internal const string ExportNotice =
        "This reader is NOT a self-contained validation bundle. Re-verification requires the retained internal workspace. " +
        "Raw inputs and attachments, policy documents, context-receipt payloads, package/source archives, raw captures, " +
        "and historical ledgers/reports are not exported. Their registration and digest references do not mean their bytes were delivered. " +
        "Exact canonical copies are labeled separately from any constructed selected-only evidence companion; " +
        "the reader explicitly declares whether that companion is included or omitted.";

    internal static bool IsRequested(InputManifest input) => input.EvidenceInputs.Any(IsMarker);

    private static bool IsMarker(InputEvidenceArtifact item) =>
        item.Kind.StartsWith("scoped-component-profile", StringComparison.OrdinalIgnoreCase) ||
        item.Kind.StartsWith("scoped-package-context", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(item.Basename, ContextFilename, StringComparison.OrdinalIgnoreCase);

    internal static ScopedComponentProfile? Load(string? root, InputManifest input)
    {
        var entries = input.EvidenceInputs.Where(IsMarker).ToArray();
        if (entries.Length == 0) return null;
        var profiles = entries.Where(item => item.Kind == Kind && item.Basename == Filename).ToArray();
        var contexts = entries.Where(item => item.Kind == ContextKind && item.Basename == ContextFilename).ToArray();
        if (root is null || entries.Length != 2 || profiles.Length != 1 || contexts.Length != 1 ||
            AuthorizedPackageScope.IsRequested(input))
            throw new DeterministicValidationException(
                "Scoped-component profile requires exactly the supported profile/context descriptor pair and input root; unknown, orphaned or conflicting profiles cannot fall back to ordinary selection.");
        var rubric = RubricLoader.Load();
        var profileBytes = ReadBound(root, profiles[0]);
        if (ContractJson.RawDigest(profileBytes) != rubric.CrosswalkDigest)
            throw new DeterministicValidationException("Scoped-component profile requires the byte-identical shipped requirement basis.");
        _ = ReadBound(root, contexts[0]);
        return new ScopedComponentProfile(rubric, contexts[0]);
    }

    private static byte[] ReadBound(string root, InputEvidenceArtifact input)
    {
        var bytes = BoundedIO.ReadAllBytes(
            SafePath.ResolveUnderRoot(root, input.Basename, requireExisting: true, requireFile: true),
            ResourceLimits.SupplementalInputAggregateBytes, "scoped profile input");
        if (bytes.LongLength != input.Size || ContractJson.RawDigest(bytes) != input.ContentDigest)
            throw new DeterministicValidationException("Scoped profile input differs from its confirmed size or digest.");
        return bytes;
    }

    internal IReadOnlyList<RubricRequirement> Select(RubricContract rubric, string kind)
    {
        if (kind != "component" || rubric.RubricVersion != RubricLoader.CurrentVersion)
            throw new DeterministicValidationException("Scoped-component profile supports only current-rubric component assessments.");
        return Requirements;
    }

    internal void ValidateBinding(
        ReadinessAssessment assessment, InputManifest input, ScopedPackageContextBinding? context,
        PackageRevisionBinding? ordinaryBinding = null)
    {
        var rubric = RubricLoader.Load();
        _ = Select(rubric, assessment.AssessmentKind);
        if (context is null || ordinaryBinding is not null || assessment.PackageReference is not null ||
            assessment.SchemaVersion != AssessmentService.SchemaVersion ||
            assessment.Identity.AssessmentKind != "component" ||
            assessment.Identity.ComponentId is null ||
            !input.Components.Any(item => item.Id == assessment.Identity.ComponentId) ||
            assessment.Identity.InputManifestDigest != InputManifestService.Digest(InputManifestService.Serialize(input)) ||
            assessment.Identity.Package != new EvidencePackageIdentity(input.Package.PackageId, input.Package.Version, input.Package.NupkgDigest) ||
            context.ValidationDigest != _context.ContentDigest ||
            context.ValidationSize != _context.Size ||
            assessment.Identity.Package != context.Assessment.Identity.Package ||
            input.Source != context.Input.Source ||
            assessment.RubricVersion != rubric.RubricVersion ||
            assessment.RubricDigest != rubric.RubricDigest ||
            assessment.ScopeSchemaVersion != rubric.ScopeSchemaVersion ||
            assessment.ScopeMapDigest != rubric.ScopeMapDigest ||
            assessment.Overlays.Count != 0 ||
            !assessment.SelectedIds.SequenceEqual(Requirements.Select(row => row.Id), StringComparer.Ordinal) ||
            assessment.Rows.Count != Requirements.Count ||
            assessment.Rows.Zip(Requirements).Any(pair =>
                pair.First.Id != pair.Second.Id || pair.First.Requirement != pair.Second.Requirement ||
                pair.First.Scope != pair.Second.Scope || pair.First.Area != pair.Second.Area))
            throw new DeterministicValidationException(
                "Scoped-component assessment requires its exact closed selection and separately verified scoped package context, matching receipt, package, source and rubric identities, with a null ordinary package reference.");
    }

    internal void ValidateEvidence(InputManifest input, EvidenceBundle evidence)
    {
        EvidenceLedgerValidator.ValidateBundle(evidence);
        var policyInputs = input.EvidenceInputs
            .Where(IsMarker)
            .ToArray();
        var policyDigests = policyInputs.Select(item => item.ContentDigest).ToHashSet();
        var policyLocators = policyInputs.Select(item => item.Basename).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var selected = evidence.Selection.Select(item => item.EvidenceId).ToHashSet(StringComparer.Ordinal);
        if (evidence.SourceLedgers.SelectMany(item => item.Ledger.Records)
            .Any(record => selected.Contains(record.StableId) &&
                (policyDigests.Contains(record.Provenance.ContentDigest) || policyLocators.Contains(record.Provenance.Locator))))
            throw new DeterministicValidationException("Profile and package-context inputs do not establish product evidence.");
    }

    internal static void RejectUnboundComponent(ReadinessAssessment assessment)
    {
        if (assessment.AssessmentKind == "component" && assessment.PackageReference is null)
            throw new DeterministicValidationException(
                "An ordinary component projection requires its package reference; a stripped scoped profile cannot fall back to ordinary reporting.");
    }

    internal EvidenceBundle? BuildSelectedCompanion(EvidenceBundle evidence)
    {
        EvidenceLedgerValidator.ValidateBundle(evidence);
        var selected = evidence.Selection.Select(item => item.EvidenceId).ToHashSet(StringComparer.Ordinal);
        if (evidence.SourceLedgers.SelectMany(source => source.Ledger.Records)
            .Any(record => selected.Contains(record.StableId) &&
                record.Supersedes.Any(predecessor => !selected.Contains(predecessor))))
            return null;
        var ledgers = new List<EvidenceSourceLedger>();
        foreach (var source in evidence.SourceLedgers)
        {
            var records = source.Ledger.Records.Where(record => selected.Contains(record.StableId)).ToArray();
            if (records.Length == 0) continue;
            var drafts = records.Select(record => new EvidenceRecordDraft(
                record.Claim, record.Applicability, record.Provenance, record.Supersedes));
            var ledger = source.Ledger.LedgerKind == "component"
                ? EvidenceLedgerBuilder.BuildComponentLedger(source.Ledger.ComponentSubject!, drafts)
                : EvidenceLedgerBuilder.BuildRepositoryLedger(source.Ledger.RepositorySubject!, drafts);
            if (!ledger.Records.Select(record => record.StableId).SequenceEqual(
                    records.Select(record => record.StableId), StringComparer.Ordinal))
                throw new DeterministicValidationException("Selected evidence companion changed a source record identity.");
            ledgers.Add(ledger);
        }
        return EvidenceLedgerBuilder.BuildBundle(evidence.Assessment, ledgers,
            evidence.Selection.OrderBy(item => item.DisplayOrder).Select(item => item.EvidenceId).ToArray());
    }

    internal void RejectDisclosure(byte[] bytes, bool json = false)
    {
        var text = Encoding.UTF8.GetString(bytes);
        RejectDisclosure(text);
        if (!json) return;
        using var document = StrictJson.Parse(bytes, ResourceLimits.SerializedArtifactBytes, "scoped export");
        Visit(document.RootElement);
        void Visit(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.String) RejectDisclosure(element.GetString()!);
            else if (element.ValueKind == JsonValueKind.Object)
                foreach (var property in element.EnumerateObject())
                {
                    RejectDisclosure(property.Name);
                    Visit(property.Value);
                }
            else if (element.ValueKind == JsonValueKind.Array)
                foreach (var item in element.EnumerateArray()) Visit(item);
        }
    }

    internal void RejectDisclosure(string text)
    {
        var decoded = text;
        for (var index = 0; index < 3; index++) decoded = WebUtility.HtmlDecode(decoded);
        decoded = Regex.Replace(decoded, @"\\u([0-9a-fA-F]{4})",
            match => ((char)Convert.ToInt32(match.Groups[1].Value, 16)).ToString());
        decoded = decoded.Replace('\u2010', '-').Replace('\u2011', '-').Replace('\u2013', '-');
        var markdownUnescaped = Regex.Replace(decoded, @"\\([!-~])", match =>
        {
            var escaped = match.Groups[1].Value[0];
            return escaped is >= '!' and <= '/' or >= ':' and <= '@' or >= '[' and <= '`' or >= '{' and <= '~'
                ? match.Groups[1].Value : match.Value;
        });
        if (ContainsExcludedMaterial(decoded) || ContainsExcludedMaterial(markdownUnescaped))
            throw new DeterministicValidationException(
                "Scoped component output contains excluded requirement identifiers or material. Preserve internal artifacts; do not fall back to an unscoped export.");

        bool ContainsExcludedMaterial(string value) =>
            _excludedIds.IsMatch(value) ||
            value.Contains("versioned extension", StringComparison.OrdinalIgnoreCase) ||
            _excludedRequirements.Any(requirement => value.Contains(requirement, StringComparison.OrdinalIgnoreCase)) ||
            Regex.IsMatch(value, @"\b(?:60|61|121)\s*[- ]\s*(?:canonical\s+)?(?:package\s+|component\s+)?(?:rows?|checks?|requirements?|findings?)\b",
                RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    }
}
