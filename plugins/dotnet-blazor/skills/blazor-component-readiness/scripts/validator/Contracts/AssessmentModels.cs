namespace BlazorComponentReadiness.Validator.Contracts;

public sealed record RubricRequirement(
    string Id,
    string Requirement,
    string Scope,
    string Area,
    string? OverlayId,
    RequirementBasis? Basis = null);

public sealed record NormativeClause(string Clause, string Quotation);

public sealed record RequirementBasis(
    string Clause,
    string Quotation,
    string Classification,
    string SemanticScope,
    string? SharedActionKey,
    string? ConditionalFamily,
    string? ExtensionBasis,
    IReadOnlyList<NormativeClause> AdditionalClauses)
{
    public bool RequiresPolicyApproval => Classification == "versioned extension";
}

public sealed record RubricOverlay(
    string Id,
    string Name,
    string Version,
    Sha256Digest Digest,
    IReadOnlyList<RubricRequirement> Requirements);

public sealed record RubricContract(
    string RubricVersion,
    int ScopeSchemaVersion,
    string Positioning,
    IReadOnlyList<string> Statuses,
    Sha256Digest RubricDigest,
    Sha256Digest CoreDigest,
    Sha256Digest ScopeMapDigest,
    IReadOnlyList<RubricRequirement> CoreRequirements,
    IReadOnlyList<RubricOverlay> Overlays,
    Sha256Digest? CrosswalkDigest = null,
    IReadOnlyList<RubricRequirement>? Extensions = null);

public sealed record AssessmentOverlay(
    string Id,
    string Version,
    Sha256Digest Digest);

public sealed record PackageAssessmentReference(
    EvidencePackageIdentity Package,
    Sha256Digest InputManifestDigest,
    Sha256Digest? AssessmentDigest,
    Sha256Digest? ReportDigest,
    Sha256Digest ValidationDigest);

public sealed record AssessmentFeedbackEntry(
    IReadOnlyList<string> RequirementIds,
    string RawPayload);

public sealed record AssessmentFeedback(
    IReadOnlyList<AssessmentFeedbackEntry> Entries,
    Sha256Digest Digest);

public sealed record AssessmentRow(
    string Id,
    string Requirement,
    string Scope,
    string Area,
    string? Status,
    string? Observation,
    IReadOnlyList<string> EvidenceIds,
    string? OwnerAction,
    string? AssessmentFollowUp,
    string? NotApplicableRationale);

public sealed record AssessmentFinding(
    string Title,
    string FactualSummary,
    IReadOnlyList<string> RequirementIds,
    IReadOnlyList<string> EvidenceIds);

public sealed record AssessmentSummaryGroup(
    string Name,
    string FactualSummary,
    IReadOnlyList<string> RequirementIds,
    IReadOnlyList<string> EvidenceIds);

public sealed record ReadinessAssessment(
    int SchemaVersion,
    string AssessmentKind,
    ExactAssessmentIdentity Identity,
    string RubricVersion,
    int ScopeSchemaVersion,
    Sha256Digest RubricDigest,
    Sha256Digest ScopeMapDigest,
    IReadOnlyList<AssessmentOverlay> Overlays,
    IReadOnlyList<string> SelectedIds,
    PackageAssessmentReference? PackageReference,
    IReadOnlyList<AssessmentRow> Rows,
    IReadOnlyList<AssessmentFinding> Findings,
    IReadOnlyList<AssessmentSummaryGroup> SummaryGroups,
    string CompletionState);

public sealed record ValidationManifest(
    int SchemaVersion,
    string PluginVersion,
    string ValidatorVersion,
    string RendererVersion,
    string RubricVersion,
    int ScopeSchemaVersion,
    Sha256Digest RubricDigest,
    Sha256Digest ScopeMapDigest,
    IReadOnlyList<AssessmentOverlay> Overlays,
    Sha256Digest InputManifestDigest,
    Sha256Digest AssessmentDigest,
    Sha256Digest EvidenceDigest,
    IReadOnlyList<string> SelectedEvidenceIds,
    PackageAssessmentReference? PackageReference,
    Sha256Digest ReportDigest,
    string AssessmentKind,
    string CompletionState,
    Sha256Digest? PredecessorManifestDigest,
    Sha256Digest? FeedbackDigest,
    IReadOnlyList<string> DeclaredChangedIds,
    IReadOnlyList<string> Limitations);
