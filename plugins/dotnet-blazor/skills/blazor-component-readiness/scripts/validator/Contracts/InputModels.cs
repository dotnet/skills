namespace BlazorComponentReadiness.Validator.Contracts;

public sealed record InputPackage(
    string PackageId,
    string Version,
    Sha256Digest NupkgDigest,
    string NupkgPath,
    string OriginLocator,
    string RetrievalMethod);

public sealed record InputSource(
    string Availability,
    string? RepositoryUri,
    string? Commit,
    string? Mapping,
    string? Confidence);

public sealed record InputDocument(
    string Url,
    string ContentPath,
    Sha256Digest ContentDigest);

public sealed record InputPackageSource(
    string Kind,
    string Locator,
    string ContentPath,
    Sha256Digest ContentDigest);

public sealed record InputSourceArtifact(
    string SourcePath,
    string ContentPath,
    Sha256Digest ContentDigest);

public sealed record OwnerInput(
    string Basename,
    string Provenance,
    Sha256Digest ContentDigest,
    long Size);

public sealed record InputEvidenceArtifact(
    string Basename,
    string Kind,
    Sha256Digest ContentDigest,
    long Size);

public sealed record DynamicChildLifecycle(
    string Applicability,
    IReadOnlyList<string> Triggers,
    string? Rationale);

public sealed record InputComponent(
    string Id,
    string DisplayName,
    IReadOnlyList<string> RenderModes,
    IReadOnlyList<string> AllowedSourcePaths,
    DynamicChildLifecycle DynamicChildLifecycle);

public sealed record InputExclusion(
    string Subject,
    string Rationale);

public sealed record InputRetrievalAttempt(
    string Subject,
    string Locator,
    string RetrievalMethod,
    string Result,
    string? Detail);

public sealed record InputManifest(
    int SchemaVersion,
    string State,
    string Acquisition,
    InputPackage Package,
    InputSource Source,
    IReadOnlyList<InputRetrievalAttempt> RetrievalAttempts,
    IReadOnlyList<InputDocument> Documentation,
    IReadOnlyList<InputPackageSource> PackageSources,
    IReadOnlyList<InputSourceArtifact> SourceArtifacts,
    IReadOnlyList<InputEvidenceArtifact> EvidenceInputs,
    IReadOnlyList<OwnerInput> OwnerInputs,
    IReadOnlyList<InputComponent> Components,
    IReadOnlyList<InputExclusion> Exclusions);
