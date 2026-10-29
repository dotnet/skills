using System.Text.Json;
using BlazorComponentReadiness.Validator.IO;

namespace BlazorComponentReadiness.Validator.Release;

public sealed record ReleaseSource(string Role, string Container, string Pointer);
public sealed record ReleaseFact(
    string Id, string Kind, string? Algorithm, string Value, ReleaseSource Source);
public sealed record ReleaseComparison(
    string Id, string Mode, ReleaseFact Left, ReleaseFact Right, string Outcome, string Reason);
public sealed record ReleaseOperation(string Id, string Outcome, string Detail);
public sealed record ReleaseRole(
    string Role, string ManifestPointer, string Path, long Size, string Sha256);
public sealed record ReleaseManifestReference(string Path, string Sha256);
public sealed record ReleaseAccounting(
    string LimitBytes, string SelectedRawBytes, string ExpandedBytes, string DecodedBytes,
    string ResultBytes, string DiagnosticsBytes, string TotalBytes);
public sealed record ReleaseExecution(
    string? StartedAtUtc, string? CompletedAtUtc, string? DurationMs, string? Root);

public sealed record ReleaseFactsResult(
    int SchemaVersion,
    string ProducerVersion,
    int NormalizationVersion,
    ReleaseManifestReference InputManifest,
    IReadOnlyList<ReleaseRole> Roles,
    IReadOnlyList<ReleaseFact> Facts,
    IReadOnlyList<ReleaseComparison> Comparisons,
    IReadOnlyList<ReleaseOperation> Operations,
    IReadOnlyList<string> Diagnostics,
    ReleaseAccounting Accounting,
    ReleaseExecution Execution)
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public byte[] Serialize() => StrictJson.SerializeCanonical(writer =>
        JsonSerializer.Serialize(writer, this, Options));

    // Only execution clocks and the physical root are excluded. Accounting and coverage remain.
    public byte[] NormalizedProjection() =>
        (this with { Execution = new(null, null, null, null) }).Serialize();
}
