using System.Text.Json;
using System.Text.Json.Serialization;
using BlazorComponentReadiness.Validator.Contracts;
using BlazorComponentReadiness.Validator.IO;
using BlazorComponentReadiness.Validator.Validation;

namespace BlazorComponentReadiness.Validator.Preparation;

public sealed record PreparationRequest(
    string Input, string Output, string? SourceArchive, string? ArchiveFormat, string? SourceRootId,
    string? ReleasePackage, string? Spdx22, string? Spdx22Entry, string? Spdx30, string? Spdx30Entry,
    string? Provenance, string? SbomStatement);
public sealed record PreparationArtifact(string Path, long Size, string Sha256);
public sealed record PreparationRole(string Role, string ManifestPointer, PreparationArtifact Artifact);
public sealed record PreparationSubject(
    InputPackage Package, InputSource Source, string ScopeSha256, string SelectedSetSha256, int RequirementCount);
public sealed record PreparationArgument(string Name, string? Value);
public sealed record PreparationCall(string Api, IReadOnlyList<PreparationArgument> Arguments);
public sealed record PreparationInvocation(
    string GenerationId, string OperationId, string CollectorVersion, string Root,
    PreparationRequest WrapperRequest, PreparationCall? Call, string? OutputPath, string StartedAtUtc);
public sealed record PreparationDiagnostic(
    string GenerationId, string OperationId, string Outcome, string Cause, string Detail, string CompletedAtUtc);
public sealed record PreparationOperation(
    string Id, string Outcome, string Cause, IReadOnlyList<string> Dependencies,
    PreparationArtifact Invocation, PreparationArtifact Diagnostic, PreparationArtifact? Output);
public sealed record PreparationMapping(
    string GenerationId, string OperationId, string OutputId, string Kind, string Basename,
    PreparationArtifact Original, PreparationArtifact Retained);
public sealed record PreparationRegistrationMap(
    int SchemaVersion, string GenerationId, PreparationSubject Subject, string PreInputSha256,
    IReadOnlyList<PreparationMapping> Entries);
public sealed record PreparationReceipt(
    int SchemaVersion, string Profile, string ProducerVersion, string GenerationId,
    PreparationSubject Subject, PreparationRequest Request, PreparationArtifact PreInput,
    PreparationArtifact InputSnapshot, PreparationArtifact Roles, PreparationArtifact Reservation,
    IReadOnlyList<PreparationOperation> Operations, PreparationArtifact RegistrationMap,
    string StartedAtUtc, string CompletedAtUtc);
public sealed record SourceRootCandidate(string Id, string Prefix);
public sealed record SourceLayout(
    int SchemaVersion, string ArchiveSha256, string Outcome, string Cause, string? Prefix,
    string? SelectedId, string? RecognizedPrefix, IReadOnlyList<SourceRootCandidate> Candidates);

internal static class PreparationJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true,
        RespectNullableAnnotations = true
    };

    internal static byte[] Bytes<T>(T value)
    {
        var bytes = StrictJson.SerializeCanonical(writer => JsonSerializer.Serialize(writer, value, Options));
        BoundedIO.EnsureLength(bytes.Length, ResourceLimits.SerializedArtifactBytes, "preparation artifact");
        return bytes;
    }

    internal static T Parse<T>(byte[] bytes)
    {
        using var document = StrictJson.Parse(bytes, ResourceLimits.SerializedArtifactBytes, "preparation artifact");
        RejectNullElements(document.RootElement);
        try
        {
            var result = document.RootElement.Deserialize<T>(Options)
                ?? throw new DeterministicValidationException("Preparation artifact cannot be null.");
            ContractJson.RequireCanonical(bytes, Bytes(result), "preparation artifact");
            return result;
        }
        catch (JsonException exception)
        {
            throw new DeterministicValidationException("Invalid preparation artifact fields: " + exception.Message, exception);
        }
    }

    internal static bool Same<T>(T left, T right) => Bytes(left).AsSpan().SequenceEqual(Bytes(right));

    private static void RejectNullElements(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                PreparationService.Require(item.ValueKind != JsonValueKind.Null, "Preparation arrays cannot contain null elements.");
                RejectNullElements(item);
            }
        }
        else if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject()) RejectNullElements(property.Value);
        }
    }
}
