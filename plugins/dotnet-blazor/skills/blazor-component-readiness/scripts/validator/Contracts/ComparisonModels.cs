namespace BlazorComponentReadiness.Validator.Contracts;

public sealed record ComparisonAllowedInput(
    string Id,
    string Kind,
    string Path,
    Sha256Digest ContentDigest,
    long Size);

public sealed record ComparisonPackageIdentity(
    string PackageId,
    string Version,
    string NupkgInputId,
    Sha256Digest NupkgDigest);

public sealed record ComparisonSourceIdentity(
    string Id,
    string Availability,
    string? RepositoryUri,
    string? Commit,
    string? ArchiveInputId);

public sealed record ComparisonExecutionIdentity(
    string Id,
    string Name,
    string Version,
    string Disposition,
    string? Blocker,
    IReadOnlyList<string> InputIds);

public sealed record ComparisonRawRecord(
    string Id,
    string Subject,
    string Locator,
    string Disposition,
    string? Blocker,
    string? ToolchainId,
    string? BrowserId,
    IReadOnlyList<string> InputIds);

public sealed record ComparisonCoverageBinding(
    string Role,
    string InputId,
    string OriginKind,
    string OriginId);

public sealed record ComparisonCoverageSurface(
    string Id,
    string Disposition,
    string? Blocker,
    IReadOnlyList<ComparisonCoverageBinding> Bindings);

public sealed record ComparisonInputManifest(
    int SchemaVersion,
    string State,
    IReadOnlyList<string> AssessmentKinds,
    ComparisonPackageIdentity Package,
    IReadOnlyList<ComparisonSourceIdentity> Sources,
    IReadOnlyList<ComparisonCoverageSurface> Coverage,
    IReadOnlyList<ComparisonAllowedInput> AllowedInputs,
    IReadOnlyList<ComparisonExecutionIdentity> Toolchains,
    IReadOnlyList<ComparisonExecutionIdentity> Browsers,
    IReadOnlyList<ComparisonRawRecord> Retrievals,
    IReadOnlyList<ComparisonRawRecord> Probes);
