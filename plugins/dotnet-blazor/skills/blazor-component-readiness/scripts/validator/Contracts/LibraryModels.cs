namespace BlazorComponentReadiness.Validator.Contracts;

public sealed record InventoryExclusion(
    string Subject,
    string Rationale);

public sealed record InventoryComponent(
    string UnitId,
    string ComponentId,
    string DisplayName,
    string PackageUnitId,
    string InputManifestPath,
    Sha256Digest InputManifestDigest,
    string RevisionRoot,
    IReadOnlyList<string> RenderModes,
    IReadOnlyList<string> AllowedSourcePaths,
    IReadOnlyList<InventoryExclusion> Exclusions);

public sealed record InventoryPackage(
    string UnitId,
    EvidencePackageIdentity Package,
    string Acquisition,
    string InputManifestPath,
    Sha256Digest InputManifestDigest,
    string RevisionRoot,
    IReadOnlyList<InventoryComponent> Components,
    IReadOnlyList<InventoryExclusion> Exclusions);

public sealed record LibraryInventory(
    int SchemaVersion,
    string State,
    string? InventoryId,
    string? RunId,
    IReadOnlyList<InventoryPackage> Packages);

public sealed record LibraryStatusCount(
    string Status,
    int Count);

public sealed record LibraryUnit(
    string UnitId,
    string Kind,
    string State,
    string? PackageUnitId,
    string? ComponentId,
    IReadOnlyList<string> RenderModes,
    string InputManifestPath,
    Sha256Digest InputManifestDigest,
    string RevisionRoot,
    string? OutputRevision,
    string? ReportPath,
    string? ValidationManifestPath,
    Sha256Digest? ValidationManifestDigest,
    Sha256Digest? PackageValidationManifestDigest,
    string? StateReceiptPath,
    Sha256Digest? StateReceiptDigest,
    int? StateReceiptSequence,
    string? TransitionedAtUtc,
    string? TransitionReason,
    IReadOnlyList<LibraryStatusCount> StatusCounts,
    IReadOnlyList<string> MissingInputs,
    IReadOnlyList<string> BlockedProbes);

public sealed record LibraryRunManifest(
    int SchemaVersion,
    string RunId,
    Sha256Digest InventoryDigest,
    string State,
    bool Interrupted,
    IReadOnlyList<LibraryUnit> Units);

public sealed record LibraryStateUpdate(
    int SchemaVersion,
    string UnitId,
    string State,
    IReadOnlyList<string> MissingInputs,
    IReadOnlyList<string> BlockedProbes,
    string TransitionedAtUtc,
    string TransitionReason);

public sealed record LibraryStateReceipt(
    int SchemaVersion,
    string RunId,
    Sha256Digest InventoryDigest,
    string UnitId,
    int Sequence,
    Sha256Digest? PriorReceiptDigest,
    int? PriorReceiptSequence,
    string State,
    IReadOnlyList<string> MissingInputs,
    IReadOnlyList<string> BlockedProbes,
    string TransitionedAtUtc,
    string TransitionReason);

public sealed record LibraryStatus(
    string State,
    int Total,
    int Pending,
    int Active,
    int Completed,
    int Blocked,
    int Incomplete,
    string? Error);
