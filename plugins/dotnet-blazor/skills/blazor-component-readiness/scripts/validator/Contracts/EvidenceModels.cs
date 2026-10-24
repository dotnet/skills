namespace BlazorComponentReadiness.Validator.Contracts;

public sealed record Sha256Digest(
    string Algorithm,
    string Value);

public sealed record EvidencePackageIdentity(
    string PackageId,
    string Version,
    Sha256Digest NupkgDigest);

public sealed record ExactAssessmentIdentity(
    string AssessmentKind,
    EvidencePackageIdentity Package,
    Sha256Digest InputManifestDigest,
    string? ComponentId);

public sealed record RepositoryLedgerSubject(
    string AssessmentKind,
    EvidencePackageIdentity Package,
    Sha256Digest InputManifestDigest,
    string? ComponentId);

public sealed record EvidenceApplicability(
    string Scope,
    string? ComponentId);

public sealed record EvidenceProvenance(
    string Kind,
    string Locator,
    string Method,
    string CapturedAtUtc,
    Sha256Digest ContentDigest,
    string Retention);

public sealed record EvidenceRecordDraft(
    string Claim,
    EvidenceApplicability Applicability,
    EvidenceProvenance Provenance,
    IReadOnlyList<string> Supersedes);

public sealed record EvidenceDraftDocument(
    int SchemaVersion,
    IReadOnlyList<EvidenceRecordDraft> Records);

public sealed record EvidenceRecord(
    string StableId,
    string Claim,
    EvidenceApplicability Applicability,
    EvidenceProvenance Provenance,
    IReadOnlyList<string> Supersedes);

public sealed record EvidenceSourceLedger(
    int SchemaVersion,
    string LedgerKind,
    RepositoryLedgerSubject? RepositorySubject,
    ExactAssessmentIdentity? ComponentSubject,
    IReadOnlyList<EvidenceRecord> Records);

public sealed record EmbeddedSourceLedger(
    string SourceLedgerSha256,
    EvidenceSourceLedger Ledger);

public sealed record EvidenceSelection(
    int DisplayOrder,
    string SourceLedgerSha256,
    string EvidenceId);

public sealed record EvidenceBundle(
    int SchemaVersion,
    ExactAssessmentIdentity Assessment,
    IReadOnlyList<EmbeddedSourceLedger> SourceLedgers,
    IReadOnlyList<EvidenceSelection> Selection);

public interface IEvidenceHasher
{
    byte[] Hash(ReadOnlySpan<byte> content);
}
