using System.Text.Json;
using BlazorComponentReadiness.Validator.Contracts;
using BlazorComponentReadiness.Validator.IO;

namespace BlazorComponentReadiness.Validator.Inputs;

internal sealed record CandidateRetrievalFact(
    string Subject,
    string Locator,
    string Method,
    string Result,
    string? Detail);

internal sealed record CandidateDocumentationFact(string Url, string Path);

internal sealed record CandidatePackageSourceFact(string Kind, string Locator, string Path);

internal sealed record CandidateSourceArtifactFact(string SourcePath, string Path);

internal sealed record CandidateSupplementalInputFact(string Path, string KindOrProvenance);

internal sealed record CandidateComponentFact(
    string Id,
    string Name,
    IReadOnlyList<string> Modes,
    IReadOnlyList<string> SourcePaths,
    string LifecycleApplicability,
    IReadOnlyList<string> LifecycleTriggers,
    string? LifecycleRationale);

internal sealed record CandidateExclusionFact(string Subject, string Rationale);

internal sealed record InputCandidateFacts(
    int SchemaVersion,
    string Acquisition,
    string PackageLocator,
    string PackageRetrievalMethod,
    string SourceAvailability,
    string? SourceRepositoryUri,
    string? SourceCommit,
    string? SourceMapping,
    string? SourceConfidence,
    IReadOnlyList<CandidateRetrievalFact> RetrievalAttempts,
    IReadOnlyList<CandidateDocumentationFact> Documentation,
    IReadOnlyList<CandidatePackageSourceFact> PackageSources,
    IReadOnlyList<CandidateSourceArtifactFact> SourceArtifacts,
    IReadOnlyList<CandidateSupplementalInputFact> EvidenceInputs,
    IReadOnlyList<CandidateSupplementalInputFact> OwnerInputs,
    IReadOnlyList<CandidateComponentFact> Components,
    IReadOnlyList<CandidateExclusionFact> Exclusions);

internal static class InputCandidateBuilder
{
    private static readonly string[] CandidateProperties =
    [
        "schema_version",
        "acquisition",
        "package_origin",
        "source",
        "retrieval_attempts",
        "documentation",
        "package_sources",
        "source_artifacts",
        "evidence_inputs",
        "owner_inputs",
        "components",
        "exclusions"
    ];

    public static InputCandidateFacts Read(ReadOnlyMemory<byte> candidateBytes)
    {
        using var document = StrictJson.Parse(
            candidateBytes,
            ResourceLimits.SerializedArtifactBytes,
            "input candidates");
        return ParseCandidate(document.RootElement);
    }

    public static byte[] Build(InputCandidateFacts facts)
    {
        if (facts.SchemaVersion != InputManifestService.SchemaVersion)
        {
            throw new DeterministicValidationException(
                $"Input candidate facts schema_version must be {InputManifestService.SchemaVersion}.");
        }

        _ = InputManifestService.CanonicalAcquisition(facts.Acquisition);
        _ = InputManifestService.CanonicalSourceAvailability(facts.SourceAvailability);
        _ = InputManifestService.CanonicalSourceConfidence(facts.SourceConfidence);
        InputManifestService.ValidatePackageOriginMethod(facts.PackageRetrievalMethod);
        foreach (var attempt in facts.RetrievalAttempts)
        {
            _ = InputManifestService.CanonicalRetrievalMethod(attempt.Method);
            _ = InputManifestService.CanonicalRetrievalSubject(attempt.Subject);
            _ = InputManifestService.CanonicalRetrievalResult(attempt.Result);
        }

        foreach (var source in facts.PackageSources)
        {
            _ = InputManifestService.CanonicalPackageSourceKind(source.Kind);
        }

        foreach (var owner in facts.OwnerInputs)
        {
            _ = InputManifestService.CanonicalOwnerInputProvenance(owner.KindOrProvenance);
        }

        foreach (var component in facts.Components)
        {
            foreach (var mode in component.Modes)
            {
                _ = InputManifestService.CanonicalRenderMode(mode);
            }

            _ = InputManifestService.CanonicalLifecycleApplicability(
                component.LifecycleApplicability);
            foreach (var trigger in component.LifecycleTriggers)
            {
                _ = InputManifestService.CanonicalLifecycleTrigger(trigger);
            }
        }

        InputManifestService.ValidateCandidateSyntax(facts);

        return StrictJson.SerializeCanonical(writer =>
        {
            writer.WriteStartObject();
            writer.WriteNumber("schema_version", facts.SchemaVersion);
            WriteString(writer, "acquisition", facts.Acquisition);
            writer.WritePropertyName("package_origin");
            writer.WriteStartObject();
            WriteString(writer, "locator", facts.PackageLocator);
            WriteString(writer, "retrieval_method", facts.PackageRetrievalMethod);
            writer.WriteEndObject();
            writer.WritePropertyName("source");
            writer.WriteStartObject();
            WriteString(writer, "availability", facts.SourceAvailability);
            WriteNullableString(writer, "repository_uri", facts.SourceRepositoryUri);
            WriteNullableString(writer, "commit", facts.SourceCommit);
            WriteNullableString(writer, "mapping", facts.SourceMapping);
            WriteNullableString(writer, "confidence", facts.SourceConfidence);
            writer.WriteEndObject();
            WriteRetrievalAttempts(writer, facts.RetrievalAttempts);
            WriteDocumentation(writer, facts.Documentation);
            WritePackageSources(writer, facts.PackageSources);
            WriteSourceArtifacts(writer, facts.SourceArtifacts);
            WriteSupplementalInputs(writer, "evidence_inputs", facts.EvidenceInputs, "kind");
            WriteSupplementalInputs(writer, "owner_inputs", facts.OwnerInputs, "provenance");
            WriteComponents(writer, facts.Components);
            WriteExclusions(writer, facts.Exclusions);
            writer.WriteEndObject();
        });
    }

    private static InputCandidateFacts ParseCandidate(JsonElement value)
    {
        ContractJson.RequirePropertiesUnordered(value, CandidateProperties);
        var origin = ContractJson.Object(value, "package_origin");
        ContractJson.RequirePropertiesUnordered(origin, "locator", "retrieval_method");
        var source = ContractJson.Object(value, "source");
        ContractJson.RequirePropertiesUnordered(
            source, "availability", "repository_uri", "commit", "mapping", "confidence");
        return new InputCandidateFacts(
            ContractJson.Int32(value, "schema_version"),
            ContractJson.String(value, "acquisition"),
            ContractJson.String(origin, "locator"),
            ContractJson.String(origin, "retrieval_method"),
            ContractJson.String(source, "availability"),
            ContractJson.NullableString(source, "repository_uri"),
            ContractJson.NullableString(source, "commit"),
            ContractJson.NullableString(source, "mapping"),
            ContractJson.NullableString(source, "confidence"),
            ParseRetrievalAttempts(ContractJson.Array(value, "retrieval_attempts")),
            ParseDocumentation(ContractJson.Array(value, "documentation")),
            ParsePackageSources(ContractJson.Array(value, "package_sources")),
            ParseSourceArtifacts(ContractJson.Array(value, "source_artifacts")),
            ParseSupplementalInputs(ContractJson.Array(value, "evidence_inputs"), "kind"),
            ParseSupplementalInputs(ContractJson.Array(value, "owner_inputs"), "provenance"),
            ParseComponents(ContractJson.Array(value, "components")),
            ParseExclusions(ContractJson.Array(value, "exclusions")));
    }

    private static IReadOnlyList<CandidateRetrievalFact> ParseRetrievalAttempts(JsonElement value) =>
        value.EnumerateArray().Select(item =>
        {
            ContractJson.RequirePropertiesUnordered(item, "subject", "locator", "retrieval_method", "result", "detail");
            return new CandidateRetrievalFact(
                ContractJson.String(item, "subject"),
                ContractJson.String(item, "locator"),
                ContractJson.String(item, "retrieval_method"),
                ContractJson.String(item, "result"),
                ContractJson.NullableString(item, "detail"));
        }).ToArray();

    private static IReadOnlyList<CandidateDocumentationFact> ParseDocumentation(JsonElement value) =>
        value.EnumerateArray().Select(item =>
        {
            ContractJson.RequirePropertiesUnordered(item, "url", "content_path");
            return new CandidateDocumentationFact(
                ContractJson.String(item, "url"),
                ContractJson.String(item, "content_path"));
        }).ToArray();

    private static IReadOnlyList<CandidatePackageSourceFact> ParsePackageSources(JsonElement value) =>
        value.EnumerateArray().Select(item =>
        {
            ContractJson.RequirePropertiesUnordered(item, "kind", "locator", "content_path");
            return new CandidatePackageSourceFact(
                ContractJson.String(item, "kind"),
                ContractJson.String(item, "locator"),
                ContractJson.String(item, "content_path"));
        }).ToArray();

    private static IReadOnlyList<CandidateSourceArtifactFact> ParseSourceArtifacts(JsonElement value) =>
        value.EnumerateArray().Select(item =>
        {
            ContractJson.RequirePropertiesUnordered(item, "source_path", "content_path");
            return new CandidateSourceArtifactFact(
                ContractJson.String(item, "source_path"),
                ContractJson.String(item, "content_path"));
        }).ToArray();

    private static IReadOnlyList<CandidateSupplementalInputFact> ParseSupplementalInputs(
        JsonElement value,
        string property)
    {
        return value.EnumerateArray().Select(item =>
        {
            ContractJson.RequirePropertiesUnordered(item, "path", property);
            return new CandidateSupplementalInputFact(
                ContractJson.String(item, "path"),
                ContractJson.String(item, property));
        }).ToArray();
    }

    private static IReadOnlyList<CandidateComponentFact> ParseComponents(JsonElement value) =>
        value.EnumerateArray().Select(item =>
        {
            ContractJson.RequirePropertiesUnordered(
                item,
                "id",
                "display_name",
                "render_modes",
                "allowed_source_paths",
                "dynamic_child_lifecycle");
            var lifecycle = ContractJson.Object(item, "dynamic_child_lifecycle");
            ContractJson.RequirePropertiesUnordered(lifecycle, "applicability", "triggers", "rationale");
            return new CandidateComponentFact(
                ContractJson.String(item, "id"),
                ContractJson.String(item, "display_name"),
                ParseStringArray(ContractJson.Array(item, "render_modes"), "render_modes"),
                ParseStringArray(ContractJson.Array(item, "allowed_source_paths"), "allowed_source_paths"),
                ContractJson.String(lifecycle, "applicability"),
                ParseStringArray(
                    ContractJson.Array(lifecycle, "triggers"),
                    "triggers"),
                ContractJson.NullableString(lifecycle, "rationale"));
        }).ToArray();

    private static IReadOnlyList<CandidateExclusionFact> ParseExclusions(JsonElement value) =>
        value.EnumerateArray().Select(item =>
        {
            ContractJson.RequirePropertiesUnordered(item, "subject", "rationale");
            return new CandidateExclusionFact(
                ContractJson.String(item, "subject"),
                ContractJson.String(item, "rationale"));
        }).ToArray();

    private static IReadOnlyList<string> ParseStringArray(JsonElement value, string property) =>
        value.EnumerateArray().Select(item =>
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                throw new DeterministicValidationException(
                    $"JSON property '{property}' must contain only strings.");
            }

            return item.GetString()!;
        }).ToArray();

    private static void WriteRetrievalAttempts(
        Utf8JsonWriter writer,
        IReadOnlyList<CandidateRetrievalFact> facts)
    {
        writer.WritePropertyName("retrieval_attempts");
        writer.WriteStartArray();
        foreach (var fact in facts)
        {
            writer.WriteStartObject();
            WriteString(writer, "subject", fact.Subject);
            WriteString(writer, "locator", fact.Locator);
            WriteString(writer, "retrieval_method", fact.Method);
            WriteString(writer, "result", fact.Result);
            WriteNullableString(writer, "detail", fact.Detail);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteDocumentation(
        Utf8JsonWriter writer,
        IReadOnlyList<CandidateDocumentationFact> facts)
    {
        writer.WritePropertyName("documentation");
        writer.WriteStartArray();
        foreach (var fact in facts)
        {
            writer.WriteStartObject();
            WriteString(writer, "url", fact.Url);
            WriteString(writer, "content_path", fact.Path);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WritePackageSources(
        Utf8JsonWriter writer,
        IReadOnlyList<CandidatePackageSourceFact> facts)
    {
        writer.WritePropertyName("package_sources");
        writer.WriteStartArray();
        foreach (var fact in facts)
        {
            writer.WriteStartObject();
            WriteString(writer, "kind", fact.Kind);
            WriteString(writer, "locator", fact.Locator);
            WriteString(writer, "content_path", fact.Path);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteSourceArtifacts(
        Utf8JsonWriter writer,
        IReadOnlyList<CandidateSourceArtifactFact> facts)
    {
        writer.WritePropertyName("source_artifacts");
        writer.WriteStartArray();
        foreach (var fact in facts)
        {
            writer.WriteStartObject();
            WriteString(writer, "source_path", fact.SourcePath);
            WriteString(writer, "content_path", fact.Path);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteSupplementalInputs(
        Utf8JsonWriter writer,
        string property,
        IReadOnlyList<CandidateSupplementalInputFact> facts,
        string valueProperty)
    {
        writer.WritePropertyName(property);
        writer.WriteStartArray();
        foreach (var fact in facts)
        {
            writer.WriteStartObject();
            WriteString(writer, "path", fact.Path);
            WriteString(writer, valueProperty, fact.KindOrProvenance);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteComponents(
        Utf8JsonWriter writer,
        IReadOnlyList<CandidateComponentFact> facts)
    {
        writer.WritePropertyName("components");
        writer.WriteStartArray();
        foreach (var fact in facts)
        {
            writer.WriteStartObject();
            WriteString(writer, "id", fact.Id);
            WriteString(writer, "display_name", fact.Name);
            WriteStringArray(writer, "render_modes", fact.Modes);
            WriteStringArray(writer, "allowed_source_paths", fact.SourcePaths);
            writer.WritePropertyName("dynamic_child_lifecycle");
            writer.WriteStartObject();
            WriteString(writer, "applicability", fact.LifecycleApplicability);
            WriteStringArray(writer, "triggers", fact.LifecycleTriggers);
            WriteNullableString(writer, "rationale", fact.LifecycleRationale);
            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteExclusions(
        Utf8JsonWriter writer,
        IReadOnlyList<CandidateExclusionFact> facts)
    {
        writer.WritePropertyName("exclusions");
        writer.WriteStartArray();
        foreach (var fact in facts)
        {
            writer.WriteStartObject();
            WriteString(writer, "subject", fact.Subject);
            WriteString(writer, "rationale", fact.Rationale);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteStringArray(
        Utf8JsonWriter writer,
        string property,
        IReadOnlyList<string> values)
    {
        writer.WritePropertyName(property);
        writer.WriteStartArray();
        foreach (var value in values)
        {
            writer.WriteStringValue(RequireText(value, property));
        }

        writer.WriteEndArray();
    }

    private static void WriteString(Utf8JsonWriter writer, string property, string value) =>
        writer.WriteString(property, RequireText(value, property));

    private static void WriteNullableString(Utf8JsonWriter writer, string property, string? value)
    {
        if (value is null)
        {
            writer.WriteNull(property);
        }
        else
        {
            writer.WriteString(property, RequireText(value, property));
        }
    }

    private static string RequireText(string value, string property)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DeterministicValidationException($"Candidate fact '{property}' cannot be empty.");
        }

        return value;
    }
}
