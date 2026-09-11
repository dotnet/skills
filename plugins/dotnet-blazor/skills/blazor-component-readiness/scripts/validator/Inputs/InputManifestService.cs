using System.Text.Json;
using BlazorComponentReadiness.Validator.Contracts;
using BlazorComponentReadiness.Validator.Evidence;
using BlazorComponentReadiness.Validator.IO;
using BlazorComponentReadiness.Validator.Validation;

namespace BlazorComponentReadiness.Validator.Inputs;

public static class InputManifestService
{
    public const int SchemaVersion = 2;
    internal static IReadOnlyList<string> RetrievalMethods { get; } =
    [
        "package-feed",
        "direct-download",
        "repository-fetch",
        "local-file",
        "owner-supplied"
    ];

    private static readonly string[] DynamicChildLifecycleTriggers =
    [
        "composite-children",
        "grouped-children",
        "registered-children",
        "selected-children"
    ];

    internal static IReadOnlyList<string> AcquisitionKinds { get; } =
    [
        "published",
        "release-candidate"
    ];

    internal static IReadOnlyList<string> SourceAvailabilityKinds { get; } =
    [
        "source-available",
        "closed-source",
        "unresolved"
    ];

    internal static IReadOnlyList<string> SourceConfidenceKinds { get; } =
    [
        "low",
        "medium",
        "high"
    ];

    internal static IReadOnlyList<string> RetrievalSubjects { get; } =
    [
        "package",
        "documentation",
        "source"
    ];

    internal static IReadOnlyList<string> RetrievalResults { get; } =
    [
        "succeeded",
        "not-found",
        "access-denied",
        "unavailable",
        "invalid-content",
        "network-failure"
    ];

    internal static IReadOnlyList<string> PackageSourceKinds { get; } =
    [
        "package-readme",
        "package-metadata"
    ];

    internal static IReadOnlyList<string> OwnerInputProvenances { get; } =
    [
        "owner-supplied-internal-evidence",
        "owner-supplied-public-evidence"
    ];

    internal static IReadOnlyList<string> DynamicChildLifecycleApplicabilities { get; } =
    [
        "required",
        "not-applicable"
    ];

    internal static IReadOnlyList<string> PackageOriginMethods { get; } =
        RetrievalMethods.Where(method => method != "repository-fetch").ToArray();

    internal static IReadOnlyList<string> DynamicChildLifecycleTriggerKinds => DynamicChildLifecycleTriggers;

    public static InputManifest Discover(string root, string nupkgPath, ReadOnlyMemory<byte> candidateBytes)
    {
        var packageRelativePath = GetRelativePath(root, nupkgPath, "nupkg");
        var inspected = NupkgInspector.Inspect(
            SafePath.ResolveUnderRoot(root, packageRelativePath, requireExisting: true, requireFile: true));
        using var document = StrictJson.Parse(
            candidateBytes,
            ResourceLimits.SerializedArtifactBytes,
            "input discovery candidates");
        var candidate = document.RootElement;
        ContractJson.RequireProperties(
            candidate,
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
            "exclusions");
        if (ContractJson.Int32(candidate, "schema_version") != SchemaVersion)
        {
            throw new DeterministicValidationException(
                $"Input discovery candidate schema_version must be {SchemaVersion}.");
        }

        var acquisition = ContractJson.String(candidate, "acquisition");
        _ = CanonicalAcquisition(acquisition);

        var packageOrigin = ParseCandidatePackageOrigin(ContractJson.Object(candidate, "package_origin"));
        var source = ParseCandidateSource(ContractJson.Object(candidate, "source"));
        var retrievalAttempts = ContractJson.Array(candidate, "retrieval_attempts")
            .EnumerateArray()
            .Select(ParseCandidateRetrievalAttempt)
            .OrderBy(AttemptKey, StringComparer.Ordinal)
            .ToArray();
        var documentation = ContractJson.Array(candidate, "documentation")
            .EnumerateArray()
            .Select(item => CreateDocument(root, item))
            .OrderBy(item => item.Url, StringComparer.Ordinal)
            .ToArray();
        var packageSources = ContractJson.Array(candidate, "package_sources")
            .EnumerateArray()
            .Select(item => CreatePackageSource(root, item))
            .OrderBy(item => item.Kind, StringComparer.Ordinal)
            .ThenBy(item => item.Locator, StringComparer.Ordinal)
            .ToArray();
        var sourceArtifacts = ContractJson.Array(candidate, "source_artifacts")
            .EnumerateArray()
            .Select(item => CreateSourceArtifact(root, item))
            .OrderBy(item => item.SourcePath, StringComparer.Ordinal)
            .ToArray();
        var evidenceInputs = ContractJson.Array(candidate, "evidence_inputs")
            .EnumerateArray()
            .Select(item => CreateEvidenceInput(root, item))
            .OrderBy(item => item.Basename, StringComparer.Ordinal)
            .ToArray();
        var ownerInputs = ContractJson.Array(candidate, "owner_inputs")
            .EnumerateArray()
            .Select(item => CreateOwnerInput(root, item))
            .OrderBy(item => item.Basename, StringComparer.Ordinal)
            .ToArray();
        ValidateSupplementalInputCeilings(evidenceInputs, ownerInputs);
        var components = ContractJson.Array(candidate, "components")
            .EnumerateArray()
            .Select(ParseCandidateComponent)
            .OrderBy(item => item.Id, StringComparer.Ordinal)
            .ToArray();
        var exclusions = ContractJson.Array(candidate, "exclusions")
            .EnumerateArray()
            .Select(ParseExclusion)
            .OrderBy(item => item.Subject, StringComparer.Ordinal)
            .ThenBy(item => item.Rationale, StringComparer.Ordinal)
            .ToArray();

        var manifest = new InputManifest(
            SchemaVersion,
            "draft",
            acquisition,
            new InputPackage(
                Canonicalization.PackageId(inspected.Id),
                NuGetVersionNormalizer.Normalize(inspected.Version),
                new Sha256Digest("sha256", inspected.NupkgSha256),
                packageRelativePath,
                packageOrigin.Locator,
                packageOrigin.RetrievalMethod),
            source,
            RequireUnique(retrievalAttempts, AttemptKey, "retrieval attempt"),
            RequireUnique(documentation, item => item.Url, "documentation URL"),
            RequireUnique(packageSources, item => $"{item.Kind}\0{item.Locator}", "package source"),
            RequireUnique(sourceArtifacts, item => item.SourcePath, "source artifact path"),
            RequireUnique(evidenceInputs, item => item.Basename, "evidence input basename"),
            RequireUnique(ownerInputs, item => item.Basename, "owner input basename"),
            RequireUnique(components, item => item.Id, "component ID"),
            exclusions);
        Validate(manifest, root, requireConfirmed: false);
        return manifest;
    }

    public static InputManifest Confirm(InputManifest draft, string root)
    {
        Validate(draft, root, requireConfirmed: false);
        if (draft.State != "draft")
        {
            throw new DeterministicValidationException("Only a draft input manifest can be confirmed.");
        }

        var confirmed = draft with { State = "confirmed" };
        Validate(confirmed, root, requireConfirmed: true);
        return confirmed;
    }

    public static void Validate(InputManifest manifest, string root, bool requireConfirmed)
    {
        if (manifest.SchemaVersion != SchemaVersion ||
            manifest.State is not ("draft" or "confirmed") ||
            requireConfirmed && manifest.State != "confirmed" ||
            !AcquisitionKinds.Contains(manifest.Acquisition, StringComparer.Ordinal))
        {
            throw new DeterministicValidationException("Input manifest schema, state, or acquisition is invalid.");
        }

        var packagePath = SafePath.ResolveUnderRoot(
            root,
            manifest.Package.NupkgPath,
            requireExisting: true,
            requireFile: true);
        var inspected = NupkgInspector.Inspect(packagePath);
        var expectedPackage = new InputPackage(
            Canonicalization.PackageId(inspected.Id),
            NuGetVersionNormalizer.Normalize(inspected.Version),
            new Sha256Digest("sha256", inspected.NupkgSha256),
            Canonicalization.RelativePath(manifest.Package.NupkgPath, "nupkg_path"),
            CanonicalLocator(manifest.Package.OriginLocator, manifest.Package.RetrievalMethod),
            CanonicalRetrievalMethod(manifest.Package.RetrievalMethod));
        if (manifest.Package != expectedPackage ||
            !PackageOriginMethods.Contains(manifest.Package.RetrievalMethod, StringComparer.Ordinal))
        {
            throw new DeterministicValidationException(
                "Input manifest package identity or digest does not match the exact inspected nupkg.");
        }

        ValidateSource(manifest.Source);
        ValidateRetrievalAttempts(manifest);
        ValidateUniqueAndSorted(manifest.Documentation, item => item.Url, "documentation");
        foreach (var document in manifest.Documentation)
        {
            var canonical = new InputDocument(
                Canonicalization.HttpsUri(document.Url, requirePath: false),
                Canonicalization.RelativePath(document.ContentPath, "documentation content_path"),
                document.ContentDigest);
            if (canonical != document)
            {
                throw new DeterministicValidationException("Documentation input is not canonical.");
            }

            ValidateLocalDigest(root, document.ContentPath, document.ContentDigest, "documentation content");
        }

        ValidateUniqueAndSorted(
            manifest.PackageSources,
            item => $"{item.Kind}\0{item.Locator}",
            "package sources");
        foreach (var source in manifest.PackageSources)
        {
            if (!PackageSourceKinds.Contains(source.Kind, StringComparer.Ordinal) ||
                Canonicalization.RelativePath(source.ContentPath, "package source content_path") != source.ContentPath ||
                ContractJson.NormalizeText(source.Locator, "package source locator", 512) != source.Locator)
            {
                throw new DeterministicValidationException("Package source is not canonical.");
            }

            ValidateLocalDigest(root, source.ContentPath, source.ContentDigest, "package source content");
        }

        ValidateUniqueAndSorted(manifest.SourceArtifacts, item => item.SourcePath, "source artifacts");
        foreach (var artifact in manifest.SourceArtifacts)
        {
            var canonical = new InputSourceArtifact(
                Canonicalization.RelativePath(artifact.SourcePath, "source artifact source_path"),
                Canonicalization.RelativePath(artifact.ContentPath, "source artifact content_path"),
                artifact.ContentDigest);
            if (canonical != artifact)
            {
                throw new DeterministicValidationException("Source artifact is not canonical.");
            }

            ValidateLocalDigest(root, artifact.ContentPath, artifact.ContentDigest, "source artifact content");
        }

        if (manifest.Source.Availability != "source-available" && manifest.SourceArtifacts.Count != 0)
        {
            throw new DeterministicValidationException(
                "Only source-available inputs may confirm captured source artifacts.");
        }

        ValidateUniqueAndSorted(manifest.EvidenceInputs, item => item.Basename, "evidence inputs");
        foreach (var evidenceInput in manifest.EvidenceInputs)
        {
            if (Canonicalization.Basename(evidenceInput.Basename, "evidence input basename") !=
                    evidenceInput.Basename ||
                ContractJson.NormalizeText(evidenceInput.Kind, "evidence input kind", 128) !=
                    evidenceInput.Kind)
            {
                throw new DeterministicValidationException(
                    "Evidence input basename or kind is invalid.");
            }

            ValidateSupplementalInput(
                root,
                evidenceInput.Basename,
                evidenceInput.ContentDigest,
                evidenceInput.Size,
                "evidence input");
        }

        ValidateUniqueAndSorted(manifest.OwnerInputs, item => item.Basename, "owner inputs");
        ValidateSupplementalInputCeilings(manifest.EvidenceInputs, manifest.OwnerInputs);
        foreach (var owner in manifest.OwnerInputs)
        {
            if (Canonicalization.Basename(owner.Basename, "owner input basename") != owner.Basename ||
                !OwnerInputProvenances.Contains(owner.Provenance, StringComparer.Ordinal))
            {
                throw new DeterministicValidationException("Owner input basename or provenance is invalid.");
            }

            ValidateSupplementalInput(
                root,
                owner.Basename,
                owner.ContentDigest,
                owner.Size,
                "owner input");
        }

        ValidateUniqueAndSorted(manifest.Components, item => item.Id, "components");
        foreach (var component in manifest.Components)
        {
            if (Canonicalization.ComponentId(component.Id) != component.Id ||
                ContractJson.NormalizeText(component.DisplayName, "component display name", 256) != component.DisplayName ||
                component.RenderModes.Count == 0)
            {
                throw new DeterministicValidationException("Component inventory entry is invalid.");
            }

            var normalizedModes = component.RenderModes.Select(Canonicalization.RenderMode)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray();
            if (!normalizedModes.SequenceEqual(component.RenderModes, StringComparer.Ordinal))
            {
                throw new DeterministicValidationException("Component render modes are not canonical, unique, and sorted.");
            }

            var allowedSourcePaths = component.AllowedSourcePaths
                .Select(path => Canonicalization.RelativePath(path, "component allowed source path"))
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray();
            if (!allowedSourcePaths.SequenceEqual(component.AllowedSourcePaths, StringComparer.Ordinal))
            {
                throw new DeterministicValidationException(
                    "Component allowed source paths are not canonical, unique, and sorted.");
            }

            ValidateDynamicChildLifecycle(component.DynamicChildLifecycle);
        }

        var exclusions = manifest.Exclusions
            .OrderBy(item => item.Subject, StringComparer.Ordinal)
            .ThenBy(item => item.Rationale, StringComparer.Ordinal)
            .ToArray();
        if (!exclusions.SequenceEqual(manifest.Exclusions))
        {
            throw new DeterministicValidationException("Exclusions are not in canonical order.");
        }

        foreach (var exclusion in manifest.Exclusions)
        {
            _ = ContractJson.NormalizeText(exclusion.Subject, "exclusion subject", 256);
            _ = ContractJson.NormalizeText(exclusion.Rationale, "exclusion rationale", 2048);
        }
    }

    public static byte[] Serialize(InputManifest manifest)
    {
        var bytes = StrictJson.SerializeCanonical(writer =>
        {
            writer.WriteStartObject();
            writer.WriteNumber("schema_version", manifest.SchemaVersion);
            writer.WriteString("state", manifest.State);
            writer.WriteString("acquisition", manifest.Acquisition);
            writer.WritePropertyName("package");
            writer.WriteStartObject();
            writer.WriteString("package_id", manifest.Package.PackageId);
            writer.WriteString("version", manifest.Package.Version);
            ContractJson.WriteDigest(writer, "nupkg_sha256", manifest.Package.NupkgDigest);
            writer.WriteString("nupkg_path", manifest.Package.NupkgPath);
            writer.WriteString("origin_locator", manifest.Package.OriginLocator);
            writer.WriteString("retrieval_method", manifest.Package.RetrievalMethod);
            writer.WriteEndObject();
            writer.WritePropertyName("source");
            WriteSource(writer, manifest.Source);
            writer.WritePropertyName("retrieval_attempts");
            writer.WriteStartArray();
            foreach (var attempt in manifest.RetrievalAttempts)
            {
                writer.WriteStartObject();
                writer.WriteString("subject", attempt.Subject);
                writer.WriteString("locator", attempt.Locator);
                writer.WriteString("retrieval_method", attempt.RetrievalMethod);
                writer.WriteString("result", attempt.Result);
                WriteNullable(writer, "detail", attempt.Detail);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WritePropertyName("documentation");
            writer.WriteStartArray();
            foreach (var document in manifest.Documentation)
            {
                writer.WriteStartObject();
                writer.WriteString("url", document.Url);
                writer.WriteString("content_path", document.ContentPath);
                ContractJson.WriteDigest(writer, "content_sha256", document.ContentDigest);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WritePropertyName("package_sources");
            writer.WriteStartArray();
            foreach (var source in manifest.PackageSources)
            {
                writer.WriteStartObject();
                writer.WriteString("kind", source.Kind);
                writer.WriteString("locator", source.Locator);
                writer.WriteString("content_path", source.ContentPath);
                ContractJson.WriteDigest(writer, "content_sha256", source.ContentDigest);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WritePropertyName("source_artifacts");
            writer.WriteStartArray();
            foreach (var artifact in manifest.SourceArtifacts)
            {
                writer.WriteStartObject();
                writer.WriteString("source_path", artifact.SourcePath);
                writer.WriteString("content_path", artifact.ContentPath);
                ContractJson.WriteDigest(writer, "content_sha256", artifact.ContentDigest);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WritePropertyName("evidence_inputs");
            writer.WriteStartArray();
            foreach (var evidenceInput in manifest.EvidenceInputs)
            {
                writer.WriteStartObject();
                writer.WriteString("basename", evidenceInput.Basename);
                writer.WriteString("kind", evidenceInput.Kind);
                ContractJson.WriteDigest(writer, "content_sha256", evidenceInput.ContentDigest);
                writer.WriteNumber("size", evidenceInput.Size);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WritePropertyName("owner_inputs");
            writer.WriteStartArray();
            foreach (var owner in manifest.OwnerInputs)
            {
                writer.WriteStartObject();
                writer.WriteString("basename", owner.Basename);
                writer.WriteString("provenance", owner.Provenance);
                ContractJson.WriteDigest(writer, "content_sha256", owner.ContentDigest);
                writer.WriteNumber("size", owner.Size);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WritePropertyName("components");
            writer.WriteStartArray();
            foreach (var component in manifest.Components)
            {
                writer.WriteStartObject();
                writer.WriteString("id", component.Id);
                writer.WriteString("display_name", component.DisplayName);
                writer.WritePropertyName("render_modes");
                writer.WriteStartArray();
                foreach (var mode in component.RenderModes)
                {
                    writer.WriteStringValue(mode);
                }

                writer.WriteEndArray();
                writer.WritePropertyName("allowed_source_paths");
                writer.WriteStartArray();
                foreach (var path in component.AllowedSourcePaths)
                {
                    writer.WriteStringValue(path);
                }

                writer.WriteEndArray();
                writer.WritePropertyName("dynamic_child_lifecycle");
                writer.WriteStartObject();
                writer.WriteString(
                    "applicability",
                    component.DynamicChildLifecycle.Applicability);
                writer.WritePropertyName("triggers");
                writer.WriteStartArray();
                foreach (var trigger in component.DynamicChildLifecycle.Triggers)
                {
                    writer.WriteStringValue(trigger);
                }

                writer.WriteEndArray();
                WriteNullable(writer, "rationale", component.DynamicChildLifecycle.Rationale);
                writer.WriteEndObject();
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WritePropertyName("exclusions");
            writer.WriteStartArray();
            foreach (var exclusion in manifest.Exclusions)
            {
                writer.WriteStartObject();
                writer.WriteString("subject", exclusion.Subject);
                writer.WriteString("rationale", exclusion.Rationale);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        });
        BoundedIO.EnsureLength(bytes.Length, ResourceLimits.SerializedArtifactBytes, "input manifest");
        return bytes;
    }

    public static InputManifest Parse(ReadOnlyMemory<byte> bytes)
    {
        using var document = StrictJson.Parse(bytes, ResourceLimits.SerializedArtifactBytes, "input manifest");
        var root = document.RootElement;
        ContractJson.RequireProperties(
            root,
            "schema_version",
            "state",
            "acquisition",
            "package",
            "source",
            "retrieval_attempts",
            "documentation",
            "package_sources",
            "source_artifacts",
            "evidence_inputs",
            "owner_inputs",
            "components",
            "exclusions");
        var manifest = new InputManifest(
            ContractJson.Int32(root, "schema_version"),
            ContractJson.String(root, "state"),
            ContractJson.String(root, "acquisition"),
            ParsePackage(ContractJson.Object(root, "package")),
            ParseSource(ContractJson.Object(root, "source")),
            ContractJson.Array(root, "retrieval_attempts").EnumerateArray().Select(ParseRetrievalAttempt).ToArray(),
            ContractJson.Array(root, "documentation").EnumerateArray().Select(ParseDocument).ToArray(),
            ContractJson.Array(root, "package_sources").EnumerateArray().Select(ParsePackageSource).ToArray(),
            ContractJson.Array(root, "source_artifacts").EnumerateArray().Select(ParseSourceArtifact).ToArray(),
            ContractJson.Array(root, "evidence_inputs").EnumerateArray().Select(ParseEvidenceInput).ToArray(),
            ContractJson.Array(root, "owner_inputs").EnumerateArray().Select(ParseOwnerInput).ToArray(),
            ContractJson.Array(root, "components").EnumerateArray().Select(ParseComponent).ToArray(),
            ContractJson.Array(root, "exclusions").EnumerateArray().Select(ParseExclusion).ToArray());
        ContractJson.RequireCanonical(bytes.Span, Serialize(manifest), "input manifest");
        return manifest;
    }

    public static InputManifest ParseDraft(ReadOnlyMemory<byte> bytes, string root)
    {
        using var document = StrictJson.Parse(bytes, ResourceLimits.SerializedArtifactBytes, "draft input manifest");
        var value = document.RootElement;
        ContractJson.RequirePropertiesUnordered(
            value,
            "schema_version",
            "state",
            "acquisition",
            "package",
            "source",
            "retrieval_attempts",
            "documentation",
            "package_sources",
            "source_artifacts",
            "evidence_inputs",
            "owner_inputs",
            "components",
            "exclusions");
        if (ContractJson.Int32(value, "schema_version") != SchemaVersion ||
            !string.Equals(ContractJson.String(value, "state").Trim(), "draft", StringComparison.OrdinalIgnoreCase))
        {
            throw new DeterministicValidationException("Draft input manifest schema or state is invalid.");
        }

        var packageElement = ContractJson.Object(value, "package");
        ContractJson.RequirePropertiesUnordered(
            packageElement,
            "package_id",
            "version",
            "nupkg_sha256",
            "nupkg_path",
            "origin_locator",
            "retrieval_method");
        _ = ContractJson.String(packageElement, "package_id");
        _ = ContractJson.String(packageElement, "version");
        _ = ContractJson.Digest(ContractJson.Object(packageElement, "nupkg_sha256"));
        var nupkgPath = Canonicalization.RelativePath(
            EditableText(ContractJson.String(packageElement, "nupkg_path")),
            "nupkg_path");
        var inspected = NupkgInspector.Inspect(
            SafePath.ResolveUnderRoot(root, nupkgPath, requireExisting: true, requireFile: true));
        var retrievalMethod = CanonicalRetrievalMethod(
            EditableText(ContractJson.String(packageElement, "retrieval_method")).ToLowerInvariant());
        ValidatePackageOriginMethod(retrievalMethod);
        var package = new InputPackage(
            Canonicalization.PackageId(inspected.Id),
            NuGetVersionNormalizer.Normalize(inspected.Version),
            new Sha256Digest("sha256", inspected.NupkgSha256),
            nupkgPath,
            CanonicalLocator(ContractJson.String(packageElement, "origin_locator"), retrievalMethod),
            retrievalMethod);
        var source = ParseDraftSource(ContractJson.Object(value, "source"));
        var attempts = ContractJson.Array(value, "retrieval_attempts")
            .EnumerateArray()
            .Select(ParseDraftRetrievalAttempt)
            .OrderBy(AttemptKey, StringComparer.Ordinal)
            .ToArray();
        var documentation = ContractJson.Array(value, "documentation")
            .EnumerateArray()
            .Select(item => CreateDraftDocument(root, item))
            .OrderBy(item => item.Url, StringComparer.Ordinal)
            .ToArray();
        var packageSources = ContractJson.Array(value, "package_sources")
            .EnumerateArray()
            .Select(item => CreateDraftPackageSource(root, item))
            .OrderBy(item => item.Kind, StringComparer.Ordinal)
            .ThenBy(item => item.Locator, StringComparer.Ordinal)
            .ToArray();
        var sourceArtifacts = ContractJson.Array(value, "source_artifacts")
            .EnumerateArray()
            .Select(item => CreateDraftSourceArtifact(root, item))
            .OrderBy(item => item.SourcePath, StringComparer.Ordinal)
            .ToArray();
        var evidenceInputs = ContractJson.Array(value, "evidence_inputs")
            .EnumerateArray()
            .Select(item => CreateDraftEvidenceInput(root, item))
            .OrderBy(item => item.Basename, StringComparer.Ordinal)
            .ToArray();
        var ownerInputs = ContractJson.Array(value, "owner_inputs")
            .EnumerateArray()
            .Select(item => CreateDraftOwnerInput(root, item))
            .OrderBy(item => item.Basename, StringComparer.Ordinal)
            .ToArray();
        var components = ContractJson.Array(value, "components")
            .EnumerateArray()
            .Select(ParseDraftComponent)
            .OrderBy(item => item.Id, StringComparer.Ordinal)
            .ToArray();
        var exclusions = ContractJson.Array(value, "exclusions")
            .EnumerateArray()
            .Select(ParseDraftExclusion)
            .OrderBy(item => item.Subject, StringComparer.Ordinal)
            .ThenBy(item => item.Rationale, StringComparer.Ordinal)
            .ToArray();
        var manifest = new InputManifest(
            SchemaVersion,
            "draft",
            EditableText(ContractJson.String(value, "acquisition")).ToLowerInvariant(),
            package,
            source,
            RequireUnique(attempts, AttemptKey, "retrieval attempt"),
            RequireUnique(documentation, item => item.Url, "documentation URL"),
            RequireUnique(packageSources, item => $"{item.Kind}\0{item.Locator}", "package source"),
            RequireUnique(sourceArtifacts, item => item.SourcePath, "source artifact path"),
            RequireUnique(evidenceInputs, item => item.Basename, "evidence input basename"),
            RequireUnique(ownerInputs, item => item.Basename, "owner input basename"),
            RequireUnique(components, item => item.Id, "component ID"),
            exclusions);
        Validate(manifest, root, requireConfirmed: false);
        return manifest;
    }

    public static Sha256Digest Digest(ReadOnlySpan<byte> confirmedBytes) =>
        ContractJson.RawDigest(confirmedBytes);

    private static InputSource ParseCandidateSource(JsonElement element)
    {
        ContractJson.RequireProperties(
            element,
            "availability",
            "repository_uri",
            "commit",
            "mapping",
            "confidence");
        var availability = CanonicalSourceAvailability(
            ContractJson.String(element, "availability"));
        var repositoryUri = ContractJson.NullableString(element, "repository_uri");
        var commit = ContractJson.NullableString(element, "commit");
        var mapping = ContractJson.NullableString(element, "mapping");
        var confidence = ContractJson.NullableString(element, "confidence");
        var source = new InputSource(
            availability,
            repositoryUri is null ? null : Canonicalization.HttpsUri(EditableText(repositoryUri), requirePath: true),
            commit is null ? null : Canonicalization.Commit(EditableText(commit)),
            mapping is null ? null : CanonicalSourceMapping(mapping),
            CanonicalSourceConfidence(confidence));
        ValidateSource(source);
        return source;
    }

    private static (string Locator, string RetrievalMethod) ParseCandidatePackageOrigin(JsonElement element)
    {
        ContractJson.RequireProperties(element, "locator", "retrieval_method");
        var method = CanonicalRetrievalMethod(
            EditableText(ContractJson.String(element, "retrieval_method")).ToLowerInvariant());
        ValidatePackageOriginMethod(method);

        return (CanonicalLocator(ContractJson.String(element, "locator"), method), method);
    }

    private static InputRetrievalAttempt ParseCandidateRetrievalAttempt(JsonElement element)
    {
        ContractJson.RequireProperties(element, "subject", "locator", "retrieval_method", "result", "detail");
        return CanonicalRetrievalAttempt(element);
    }

    private static InputRetrievalAttempt CanonicalRetrievalAttempt(JsonElement element)
    {
        var method = CanonicalRetrievalMethod(
            EditableText(ContractJson.String(element, "retrieval_method")).ToLowerInvariant());
        var subject = CanonicalRetrievalSubject(ContractJson.String(element, "subject"));
        var result = CanonicalRetrievalResult(ContractJson.String(element, "result"));

        var detail = ContractJson.NullableString(element, "detail");
        return new InputRetrievalAttempt(
            subject,
            CanonicalLocator(ContractJson.String(element, "locator"), method),
            method,
            result,
            detail is null ? null : CanonicalRetrievalDetail(detail));
    }

    private static void ValidateRetrievalAttempts(InputManifest manifest)
    {
        if (manifest.RetrievalAttempts.Count == 0 ||
            manifest.RetrievalAttempts.Count > ResourceLimits.RetrievalAttemptCount)
        {
            throw new DeterministicValidationException(
                $"Retrieval attempts require between 1 and {ResourceLimits.RetrievalAttemptCount} entries.");
        }

        ValidateUniqueAndSorted(manifest.RetrievalAttempts, AttemptKey, "retrieval attempts");
        foreach (var attempt in manifest.RetrievalAttempts)
        {
            var canonical = new InputRetrievalAttempt(
                attempt.Subject,
                CanonicalLocator(attempt.Locator, attempt.RetrievalMethod),
                CanonicalRetrievalMethod(attempt.RetrievalMethod),
                attempt.Result,
                attempt.Detail is null ? null : ContractJson.NormalizeText(
                    attempt.Detail,
                    "retrieval attempt detail",
                    1024));
            if (canonical != attempt ||
                !RetrievalSubjects.Contains(attempt.Subject, StringComparer.Ordinal) ||
                !RetrievalResults.Contains(attempt.Result, StringComparer.Ordinal))
            {
                throw new DeterministicValidationException("Retrieval attempt is not canonical.");
            }
        }

        if (!manifest.RetrievalAttempts.Any(attempt =>
                attempt.Subject == "package" &&
                attempt.Locator == manifest.Package.OriginLocator &&
                attempt.RetrievalMethod == manifest.Package.RetrievalMethod &&
                attempt.Result == "succeeded"))
        {
            throw new DeterministicValidationException(
                "Retrieval attempts must record successful acquisition of the exact package origin.");
        }

        if (manifest.Source.Availability == "closed-source" &&
            manifest.RetrievalAttempts.Any(attempt => attempt.Subject == "source"))
        {
            throw new DeterministicValidationException(
                "Closed-source is an explicit owner boundary and cannot be inferred from source retrieval attempts.");
        }
    }

    internal static void ValidateSource(InputSource source)
    {
        switch (source.Availability)
        {
            case "source-available":
                if (source.RepositoryUri is null || source.Commit is null ||
                    source.Mapping is null || source.Confidence is null ||
                    !SourceConfidenceKinds.Contains(source.Confidence, StringComparer.Ordinal) ||
                    Canonicalization.HttpsUri(source.RepositoryUri, requirePath: true) != source.RepositoryUri ||
                    Canonicalization.Commit(source.Commit) != source.Commit)
                {
                    throw new DeterministicValidationException(
                        "source-available requires canonical repository URI, exact commit, mapping, and confidence.");
                }

                _ = ContractJson.NormalizeText(source.Mapping, "source mapping", 1024);
                break;
            case "closed-source":
                if (source.RepositoryUri is not null || source.Commit is not null ||
                    source.Mapping is not null || source.Confidence is not null)
                {
                    throw new DeterministicValidationException(
                        "closed-source must not fabricate repository mapping fields.");
                }

                break;
            case "unresolved":
                if (source.Commit is not null || source.Mapping is not null || source.Confidence is not null ||
                    source.RepositoryUri is not null &&
                    Canonicalization.HttpsUri(source.RepositoryUri, requirePath: true) != source.RepositoryUri)
                {
                    throw new DeterministicValidationException(
                        "unresolved source may contain only a canonical attempted repository URI.");
                }

                break;
            default:
                throw new DeterministicValidationException($"Unknown source availability '{source.Availability}'.");
        }
    }

    private static InputDocument CreateDocument(string root, JsonElement element)
    {
        ContractJson.RequireProperties(element, "url", "content_path");
        var path = Canonicalization.RelativePath(
            EditableText(ContractJson.String(element, "content_path")),
            "documentation content_path");
        return new InputDocument(
            Canonicalization.HttpsUri(EditableText(ContractJson.String(element, "url")), requirePath: false),
            path,
            LocalDigest(root, path, "documentation content"));
    }

    private static InputPackageSource CreatePackageSource(string root, JsonElement element)
    {
        ContractJson.RequireProperties(element, "kind", "locator", "content_path");
        var kind = CanonicalPackageSourceKind(ContractJson.String(element, "kind"));

        var path = Canonicalization.RelativePath(
            EditableText(ContractJson.String(element, "content_path")),
            "package source content_path");
        return new InputPackageSource(
            kind,
            CanonicalPackageSourceLocator(ContractJson.String(element, "locator")),
            path,
            LocalDigest(root, path, "package source content"));
    }

    private static OwnerInput CreateOwnerInput(string root, JsonElement element)
    {
        ContractJson.RequireProperties(element, "path", "provenance");
        var basename = Canonicalization.Basename(
            EditableText(ContractJson.String(element, "path")),
            "owner input path");
        var provenance = CanonicalOwnerInputProvenance(ContractJson.String(element, "provenance"));

        var path = SafePath.ResolveUnderRoot(root, basename, requireExisting: true, requireFile: true);
        BoundedIO.EnsureFileLength(path, ResourceLimits.SupplementalInputAggregateBytes, "owner input");
        return new OwnerInput(
            basename,
            provenance,
            ContractJson.RawDigest(BoundedIO.ReadAllBytes(
                path,
                ResourceLimits.SupplementalInputAggregateBytes,
                "owner input")),
            new FileInfo(path).Length);
    }

    private static InputEvidenceArtifact CreateEvidenceInput(string root, JsonElement element)
    {
        ContractJson.RequireProperties(element, "path", "kind");
        var basename = Canonicalization.Basename(
            EditableText(ContractJson.String(element, "path")),
            "evidence input path");
        var kind = CanonicalEvidenceKind(ContractJson.String(element, "kind"));
        var path = SafePath.ResolveUnderRoot(root, basename, requireExisting: true, requireFile: true);
        BoundedIO.EnsureFileLength(
            path,
            ResourceLimits.SupplementalInputAggregateBytes,
            "evidence input");
        var content = BoundedIO.ReadAllBytes(
            path,
            ResourceLimits.SupplementalInputAggregateBytes,
            "evidence input");
        return new InputEvidenceArtifact(
            basename,
            kind,
            ContractJson.RawDigest(content),
            content.LongLength);
    }

    private static InputSourceArtifact CreateSourceArtifact(string root, JsonElement element)
    {
        ContractJson.RequireProperties(element, "source_path", "content_path");
        var sourcePath = Canonicalization.RelativePath(
            EditableText(ContractJson.String(element, "source_path")),
            "source artifact source_path");
        var contentPath = Canonicalization.RelativePath(
            EditableText(ContractJson.String(element, "content_path")),
            "source artifact content_path");
        return new InputSourceArtifact(
            sourcePath,
            contentPath,
            LocalDigest(root, contentPath, "source artifact content"));
    }

    private static InputComponent ParseCandidateComponent(JsonElement element)
    {
        ContractJson.RequireProperties(
            element,
            "id",
            "display_name",
            "render_modes",
            "allowed_source_paths",
            "dynamic_child_lifecycle");
        var modes = ContractJson.StringArray(element, "render_modes")
            .Select(CanonicalRenderMode)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (modes.Length == 0)
        {
            throw new DeterministicValidationException("A component requires at least one claimed render mode.");
        }

        return new InputComponent(
            Canonicalization.ComponentId(ContractJson.String(element, "id")),
            CanonicalComponentName(ContractJson.String(element, "display_name")),
            modes,
            ParseAllowedSourcePaths(element),
            ParseDynamicChildLifecycle(ContractJson.Object(element, "dynamic_child_lifecycle")));
    }

    private static InputExclusion ParseExclusion(JsonElement element)
    {
        ContractJson.RequireProperties(element, "subject", "rationale");
        return new InputExclusion(
            CanonicalExclusionSubject(ContractJson.String(element, "subject")),
            CanonicalExclusionRationale(ContractJson.String(element, "rationale")));
    }

    private static InputPackage ParsePackage(JsonElement element)
    {
        ContractJson.RequireProperties(
            element,
            "package_id",
            "version",
            "nupkg_sha256",
            "nupkg_path",
            "origin_locator",
            "retrieval_method");
        return new InputPackage(
            ContractJson.String(element, "package_id"),
            ContractJson.String(element, "version"),
            ContractJson.Digest(ContractJson.Object(element, "nupkg_sha256")),
            ContractJson.String(element, "nupkg_path"),
            ContractJson.String(element, "origin_locator"),
            ContractJson.String(element, "retrieval_method"));
    }

    internal static void WriteSource(Utf8JsonWriter writer, InputSource source)
    {
        writer.WriteStartObject();
        writer.WriteString("availability", source.Availability);
        WriteNullable(writer, "repository_uri", source.RepositoryUri);
        WriteNullable(writer, "commit", source.Commit);
        WriteNullable(writer, "mapping", source.Mapping);
        WriteNullable(writer, "confidence", source.Confidence);
        writer.WriteEndObject();
    }

    internal static InputSource ParseSource(JsonElement element)
    {
        ContractJson.RequireProperties(
            element,
            "availability",
            "repository_uri",
            "commit",
            "mapping",
            "confidence");
        return new InputSource(
            ContractJson.String(element, "availability"),
            ContractJson.NullableString(element, "repository_uri"),
            ContractJson.NullableString(element, "commit"),
            ContractJson.NullableString(element, "mapping"),
            ContractJson.NullableString(element, "confidence"));
    }

    private static InputDocument ParseDocument(JsonElement element)
    {
        ContractJson.RequireProperties(element, "url", "content_path", "content_sha256");
        return new InputDocument(
            ContractJson.String(element, "url"),
            ContractJson.String(element, "content_path"),
            ContractJson.Digest(ContractJson.Object(element, "content_sha256")));
    }

    private static InputRetrievalAttempt ParseRetrievalAttempt(JsonElement element)
    {
        ContractJson.RequireProperties(element, "subject", "locator", "retrieval_method", "result", "detail");
        return new InputRetrievalAttempt(
            ContractJson.String(element, "subject"),
            ContractJson.String(element, "locator"),
            ContractJson.String(element, "retrieval_method"),
            ContractJson.String(element, "result"),
            ContractJson.NullableString(element, "detail"));
    }

    private static InputPackageSource ParsePackageSource(JsonElement element)
    {
        ContractJson.RequireProperties(element, "kind", "locator", "content_path", "content_sha256");
        return new InputPackageSource(
            ContractJson.String(element, "kind"),
            ContractJson.String(element, "locator"),
            ContractJson.String(element, "content_path"),
            ContractJson.Digest(ContractJson.Object(element, "content_sha256")));
    }

    private static OwnerInput ParseOwnerInput(JsonElement element)
    {
        ContractJson.RequireProperties(element, "basename", "provenance", "content_sha256", "size");
        return new OwnerInput(
            ContractJson.String(element, "basename"),
            ContractJson.String(element, "provenance"),
            ContractJson.Digest(ContractJson.Object(element, "content_sha256")),
            ContractJson.Int64(element, "size"));
    }

    private static InputEvidenceArtifact ParseEvidenceInput(JsonElement element)
    {
        ContractJson.RequireProperties(element, "basename", "kind", "content_sha256", "size");
        return new InputEvidenceArtifact(
            ContractJson.String(element, "basename"),
            ContractJson.String(element, "kind"),
            ContractJson.Digest(ContractJson.Object(element, "content_sha256")),
            ContractJson.Int64(element, "size"));
    }

    private static InputSourceArtifact ParseSourceArtifact(JsonElement element)
    {
        ContractJson.RequireProperties(element, "source_path", "content_path", "content_sha256");
        return new InputSourceArtifact(
            ContractJson.String(element, "source_path"),
            ContractJson.String(element, "content_path"),
            ContractJson.Digest(ContractJson.Object(element, "content_sha256")));
    }

    private static InputComponent ParseComponent(JsonElement element)
    {
        ContractJson.RequireProperties(
            element,
            "id",
            "display_name",
            "render_modes",
            "allowed_source_paths",
            "dynamic_child_lifecycle");
        return new InputComponent(
            ContractJson.String(element, "id"),
            ContractJson.String(element, "display_name"),
            ContractJson.StringArray(element, "render_modes"),
            ContractJson.StringArray(element, "allowed_source_paths"),
            ParseDynamicChildLifecycle(ContractJson.Object(element, "dynamic_child_lifecycle")));
    }

    private static InputSource ParseDraftSource(JsonElement element)
    {
        ContractJson.RequirePropertiesUnordered(
            element,
            "availability",
            "repository_uri",
            "commit",
            "mapping",
            "confidence");
        return ParseCandidateSourceInAnyOrder(element);
    }

    private static InputSource ParseCandidateSourceInAnyOrder(JsonElement element)
    {
        var availability = EditableText(ContractJson.String(element, "availability")).ToLowerInvariant();
        var repositoryUri = ContractJson.NullableString(element, "repository_uri");
        var commit = ContractJson.NullableString(element, "commit");
        var mapping = ContractJson.NullableString(element, "mapping");
        var confidence = ContractJson.NullableString(element, "confidence");
        var source = new InputSource(
            availability,
            repositoryUri is null ? null : Canonicalization.HttpsUri(EditableText(repositoryUri), requirePath: true),
            commit is null ? null : Canonicalization.Commit(EditableText(commit)),
            mapping is null ? null : ContractJson.NormalizeText(EditableText(mapping), "source mapping", 1024),
            confidence is null ? null : EditableText(confidence).ToLowerInvariant());
        ValidateSource(source);
        return source;
    }

    private static InputRetrievalAttempt ParseDraftRetrievalAttempt(JsonElement element)
    {
        ContractJson.RequirePropertiesUnordered(
            element,
            "subject",
            "locator",
            "retrieval_method",
            "result",
            "detail");
        return CanonicalRetrievalAttempt(element);
    }

    private static InputDocument CreateDraftDocument(string root, JsonElement element)
    {
        ContractJson.RequirePropertiesUnordered(element, "url", "content_path", "content_sha256");
        _ = ContractJson.Digest(ContractJson.Object(element, "content_sha256"));
        return CreateDocumentFromConfirmedShape(root, element);
    }

    private static InputDocument CreateDocumentFromConfirmedShape(string root, JsonElement element)
    {
        var path = Canonicalization.RelativePath(
            EditableText(ContractJson.String(element, "content_path")),
            "documentation content_path");
        return new InputDocument(
            Canonicalization.HttpsUri(EditableText(ContractJson.String(element, "url")), requirePath: false),
            path,
            LocalDigest(root, path, "documentation content"));
    }

    private static InputPackageSource CreateDraftPackageSource(string root, JsonElement element)
    {
        ContractJson.RequirePropertiesUnordered(element, "kind", "locator", "content_path", "content_sha256");
        _ = ContractJson.Digest(ContractJson.Object(element, "content_sha256"));
        var kind = CanonicalPackageSourceKind(EditableText(ContractJson.String(element, "kind")).ToLowerInvariant());

        var path = Canonicalization.RelativePath(
            EditableText(ContractJson.String(element, "content_path")),
            "package source content_path");
        return new InputPackageSource(
            kind,
            ContractJson.NormalizeText(
                EditableText(ContractJson.String(element, "locator")),
                "package source locator",
                512),
            path,
            LocalDigest(root, path, "package source content"));
    }

    private static OwnerInput CreateDraftOwnerInput(string root, JsonElement element)
    {
        ContractJson.RequirePropertiesUnordered(element, "basename", "provenance", "content_sha256", "size");
        _ = ContractJson.Digest(ContractJson.Object(element, "content_sha256"));
        _ = ContractJson.Int64(element, "size");
        var basename = Canonicalization.Basename(
            EditableText(ContractJson.String(element, "basename")),
            "owner input basename");
        var provenance = CanonicalOwnerInputProvenance(ContractJson.String(element, "provenance"));

        var path = SafePath.ResolveUnderRoot(root, basename, requireExisting: true, requireFile: true);
        BoundedIO.EnsureFileLength(path, ResourceLimits.SupplementalInputAggregateBytes, "owner input");
        var content = BoundedIO.ReadAllBytes(
            path,
            ResourceLimits.SupplementalInputAggregateBytes,
            "owner input");
        return new OwnerInput(
            basename,
            provenance,
            ContractJson.RawDigest(content),
            content.LongLength);
    }

    private static InputEvidenceArtifact CreateDraftEvidenceInput(string root, JsonElement element)
    {
        ContractJson.RequirePropertiesUnordered(
            element,
            "basename",
            "kind",
            "content_sha256",
            "size");
        _ = ContractJson.Digest(ContractJson.Object(element, "content_sha256"));
        _ = ContractJson.Int64(element, "size");
        var basename = Canonicalization.Basename(
            EditableText(ContractJson.String(element, "basename")),
            "evidence input basename");
        var kind = ContractJson.NormalizeText(
            EditableText(ContractJson.String(element, "kind")).ToLowerInvariant(),
            "evidence input kind",
            128);
        var path = SafePath.ResolveUnderRoot(root, basename, requireExisting: true, requireFile: true);
        BoundedIO.EnsureFileLength(
            path,
            ResourceLimits.SupplementalInputAggregateBytes,
            "evidence input");
        var content = BoundedIO.ReadAllBytes(
            path,
            ResourceLimits.SupplementalInputAggregateBytes,
            "evidence input");
        return new InputEvidenceArtifact(
            basename,
            kind,
            ContractJson.RawDigest(content),
            content.LongLength);
    }

    private static InputSourceArtifact CreateDraftSourceArtifact(string root, JsonElement element)
    {
        ContractJson.RequirePropertiesUnordered(
            element,
            "source_path",
            "content_path",
            "content_sha256");
        _ = ContractJson.Digest(ContractJson.Object(element, "content_sha256"));
        return CreateSourceArtifactFromConfirmedShape(root, element);
    }

    private static InputSourceArtifact CreateSourceArtifactFromConfirmedShape(
        string root,
        JsonElement element)
    {
        var sourcePath = Canonicalization.RelativePath(
            EditableText(ContractJson.String(element, "source_path")),
            "source artifact source_path");
        var contentPath = Canonicalization.RelativePath(
            EditableText(ContractJson.String(element, "content_path")),
            "source artifact content_path");
        return new InputSourceArtifact(
            sourcePath,
            contentPath,
            LocalDigest(root, contentPath, "source artifact content"));
    }

    private static InputComponent ParseDraftComponent(JsonElement element)
    {
        ContractJson.RequirePropertiesUnordered(
            element,
            "id",
            "display_name",
            "render_modes",
            "allowed_source_paths",
            "dynamic_child_lifecycle");
        var modes = ContractJson.StringArray(element, "render_modes")
            .Select(CanonicalRenderMode)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (modes.Length == 0)
        {
            throw new DeterministicValidationException("A component requires at least one claimed render mode.");
        }

        return new InputComponent(
            Canonicalization.ComponentId(ContractJson.String(element, "id")),
            CanonicalComponentName(ContractJson.String(element, "display_name")),
            modes,
            ParseAllowedSourcePaths(element),
            ParseDynamicChildLifecycle(ContractJson.Object(element, "dynamic_child_lifecycle")));
    }

    private static DynamicChildLifecycle ParseDynamicChildLifecycle(JsonElement element)
    {
        ContractJson.RequirePropertiesUnordered(
            element,
            "applicability",
            "triggers",
            "rationale");
        var rationale = ContractJson.NullableString(element, "rationale");
        var value = new DynamicChildLifecycle(
            CanonicalLifecycleApplicability(ContractJson.String(element, "applicability")),
            ContractJson.StringArray(element, "triggers")
                .Select(CanonicalLifecycleTrigger)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray(),
            rationale is null
                ? null
                : CanonicalLifecycleRationale(rationale));
        ValidateDynamicChildLifecycle(value);
        return value;
    }

    private static IReadOnlyList<string> ParseAllowedSourcePaths(JsonElement element) =>
        ContractJson.StringArray(element, "allowed_source_paths")
            .Select(value => Canonicalization.RelativePath(
                EditableText(value),
                "component allowed source path"))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static InputExclusion ParseDraftExclusion(JsonElement element)
    {
        ContractJson.RequirePropertiesUnordered(element, "subject", "rationale");
        return new InputExclusion(
            CanonicalExclusionSubject(ContractJson.String(element, "subject")),
            CanonicalExclusionRationale(ContractJson.String(element, "rationale")));
    }

    internal static void ValidateCandidateSyntax(InputCandidateFacts facts)
    {
        _ = CanonicalLocator(facts.PackageLocator, facts.PackageRetrievalMethod);
        if (facts.SourceRepositoryUri is not null)
        {
            _ = Canonicalization.HttpsUri(EditableText(facts.SourceRepositoryUri), requirePath: true);
        }

        if (facts.SourceCommit is not null)
        {
            _ = Canonicalization.Commit(EditableText(facts.SourceCommit));
        }

        if (facts.SourceMapping is not null)
        {
            _ = CanonicalSourceMapping(facts.SourceMapping);
        }

        foreach (var attempt in facts.RetrievalAttempts)
        {
            _ = CanonicalLocator(attempt.Locator, attempt.Method);
            if (attempt.Detail is not null)
            {
                _ = CanonicalRetrievalDetail(attempt.Detail);
            }
        }

        foreach (var document in facts.Documentation)
        {
            _ = Canonicalization.HttpsUri(EditableText(document.Url), requirePath: false);
            _ = Canonicalization.RelativePath(EditableText(document.Path), "documentation content_path");
        }

        foreach (var source in facts.PackageSources)
        {
            _ = CanonicalPackageSourceLocator(source.Locator);
            _ = Canonicalization.RelativePath(EditableText(source.Path), "package source content_path");
        }

        foreach (var source in facts.SourceArtifacts)
        {
            _ = Canonicalization.RelativePath(EditableText(source.SourcePath), "source artifact source_path");
            _ = Canonicalization.RelativePath(EditableText(source.Path), "source artifact content_path");
        }

        foreach (var evidence in facts.EvidenceInputs)
        {
            _ = Canonicalization.Basename(EditableText(evidence.Path), "evidence input path");
            _ = CanonicalEvidenceKind(evidence.KindOrProvenance);
        }

        foreach (var owner in facts.OwnerInputs)
        {
            _ = Canonicalization.Basename(EditableText(owner.Path), "owner input path");
        }

        foreach (var component in facts.Components)
        {
            _ = Canonicalization.ComponentId(component.Id);
            _ = CanonicalComponentName(component.Name);
            foreach (var path in component.SourcePaths)
            {
                _ = Canonicalization.RelativePath(EditableText(path), "component allowed source path");
            }

            if (component.LifecycleRationale is not null)
            {
                _ = CanonicalLifecycleRationale(component.LifecycleRationale);
            }
        }

        foreach (var exclusion in facts.Exclusions)
        {
            _ = CanonicalExclusionSubject(exclusion.Subject);
            _ = CanonicalExclusionRationale(exclusion.Rationale);
        }
    }

    private static string CanonicalSourceMapping(string value) =>
        ContractJson.NormalizeText(EditableText(value), "source mapping", 1024);

    private static string CanonicalRetrievalDetail(string value) =>
        ContractJson.NormalizeText(EditableText(value), "retrieval attempt detail", 1024);

    private static string CanonicalPackageSourceLocator(string value) =>
        ContractJson.NormalizeText(EditableText(value), "package source locator", 512);

    private static string CanonicalEvidenceKind(string value) =>
        ContractJson.NormalizeText(EditableText(value).ToLowerInvariant(), "evidence input kind", 128);

    private static string CanonicalComponentName(string value) =>
        ContractJson.NormalizeText(EditableText(value), "component display name", 256);

    private static string CanonicalLifecycleRationale(string value) =>
        ContractJson.NormalizeText(EditableText(value), "dynamic child lifecycle rationale", 1024);

    private static string CanonicalExclusionSubject(string value) =>
        ContractJson.NormalizeText(EditableText(value), "exclusion subject", 256);

    private static string CanonicalExclusionRationale(string value) =>
        ContractJson.NormalizeText(EditableText(value), "exclusion rationale", 2048);

    internal static string CanonicalRetrievalMethod(string value)
    {
        value = EditableText(value).ToLowerInvariant();
        if (!RetrievalMethods.Contains(value, StringComparer.Ordinal))
        {
            throw new DeterministicValidationException(
                $"Unknown retrieval method '{value}'. Accepted methods: {string.Join(", ", RetrievalMethods)}.");
        }

        return value;
    }

    internal static string CanonicalAcquisition(string value)
    {
        if (!AcquisitionKinds.Contains(value, StringComparer.Ordinal))
        {
            throw new DeterministicValidationException(
                $"Unknown acquisition '{value}'. Accepted values: {string.Join(", ", AcquisitionKinds)}.");
        }

        return value;
    }

    internal static string CanonicalRenderMode(string value) =>
        Canonicalization.RenderMode(EditableText(value));

    internal static string CanonicalSourceAvailability(string value)
    {
        value = EditableText(value).ToLowerInvariant();
        if (!SourceAvailabilityKinds.Contains(value, StringComparer.Ordinal))
        {
            throw new DeterministicValidationException(
                $"Unknown source availability '{value}'. Accepted values: {string.Join(", ", SourceAvailabilityKinds)}.");
        }

        return value;
    }

    internal static string? CanonicalSourceConfidence(string? value)
    {
        if (value is null)
        {
            return null;
        }

        value = EditableText(value).ToLowerInvariant();
        if (!SourceConfidenceKinds.Contains(value, StringComparer.Ordinal))
        {
            throw new DeterministicValidationException(
                $"Unknown source confidence '{value}'. Accepted values: {string.Join(", ", SourceConfidenceKinds)}.");
        }

        return value;
    }

    internal static string CanonicalRetrievalSubject(string value)
    {
        value = EditableText(value).ToLowerInvariant();
        if (!RetrievalSubjects.Contains(value, StringComparer.Ordinal))
        {
            throw new DeterministicValidationException(
                $"Unknown retrieval subject '{value}'. Accepted values: {string.Join(", ", RetrievalSubjects)}.");
        }

        return value;
    }

    internal static string CanonicalRetrievalResult(string value)
    {
        value = EditableText(value).ToLowerInvariant();
        if (!RetrievalResults.Contains(value, StringComparer.Ordinal))
        {
            throw new DeterministicValidationException(
                $"Unknown retrieval result '{value}'. Accepted values: {string.Join(", ", RetrievalResults)}.");
        }

        return value;
    }

    internal static string CanonicalPackageSourceKind(string value)
    {
        if (!PackageSourceKinds.Contains(value, StringComparer.Ordinal))
        {
            throw new DeterministicValidationException(
                $"Unknown package source kind '{value}'. Accepted values: {string.Join(", ", PackageSourceKinds)}.");
        }

        return value;
    }

    internal static string CanonicalOwnerInputProvenance(string value)
    {
        value = EditableText(value).ToLowerInvariant();
        if (!OwnerInputProvenances.Contains(value, StringComparer.Ordinal))
        {
            throw new DeterministicValidationException(
                $"Unknown owner input provenance '{value}'. Accepted values: {string.Join(", ", OwnerInputProvenances)}.");
        }

        return value;
    }

    internal static string CanonicalLifecycleApplicability(string value)
    {
        value = EditableText(value).ToLowerInvariant();
        if (!DynamicChildLifecycleApplicabilities.Contains(value, StringComparer.Ordinal))
        {
            throw new DeterministicValidationException(
                $"Dynamic child lifecycle applicability must be one of: {string.Join(", ", DynamicChildLifecycleApplicabilities)}.");
        }

        return value;
    }

    internal static string CanonicalLifecycleTrigger(string value)
    {
        value = EditableText(value).ToLowerInvariant();
        if (!DynamicChildLifecycleTriggers.Contains(value, StringComparer.Ordinal))
        {
            throw new DeterministicValidationException(
                $"Unknown dynamic child lifecycle trigger '{value}'. Accepted values: {string.Join(", ", DynamicChildLifecycleTriggerKinds)}.");
        }

        return value;
    }

    internal static void ValidatePackageOriginMethod(string value)
    {
        if (!PackageOriginMethods.Contains(CanonicalRetrievalMethod(value), StringComparer.Ordinal))
        {
            throw new DeterministicValidationException(
                $"Package origin retrieval_method cannot be repository-fetch. Accepted methods: {string.Join(", ", PackageOriginMethods)}.");
        }
    }

    private static string CanonicalLocator(string value, string retrievalMethod)
    {
        retrievalMethod = CanonicalRetrievalMethod(retrievalMethod);
        return retrievalMethod is "local-file" or "owner-supplied"
            ? Canonicalization.RelativePath(EditableText(value), "retrieval locator")
            : Canonicalization.HttpsUri(EditableText(value), requirePath: false);
    }

    private static string AttemptKey(InputRetrievalAttempt attempt) =>
        $"{attempt.Subject}\0{attempt.Locator}\0{attempt.RetrievalMethod}\0{attempt.Result}\0{attempt.Detail}";

    private static string EditableText(string value) =>
        value.Trim().Normalize(System.Text.NormalizationForm.FormC);

    private static string GetRelativePath(string root, string path, string resource)
    {
        var fullRoot = Path.GetFullPath(root);
        var fullPath = Path.GetFullPath(path);
        var relative = Path.GetRelativePath(fullRoot, fullPath);
        var canonical = Canonicalization.RelativePath(relative, $"{resource} path");
        _ = SafePath.ResolveUnderRoot(fullRoot, canonical, requireExisting: true, requireFile: true);
        return canonical;
    }

    private static Sha256Digest LocalDigest(string root, string relativePath, string resource)
    {
        var path = SafePath.ResolveUnderRoot(root, relativePath, requireExisting: true, requireFile: true);
        return ContractJson.RawDigest(BoundedIO.ReadAllBytes(
            path,
            ResourceLimits.SupplementalInputAggregateBytes,
            resource));
    }

    private static void ValidateLocalDigest(
        string root,
        string relativePath,
        Sha256Digest expected,
        string resource)
    {
        EvidenceIdentity.ValidateDigest(expected, $"{resource}_sha256");
        if (LocalDigest(root, relativePath, resource) != expected)
        {
            throw new DeterministicValidationException($"{resource} has a stale local digest.");
        }
    }

    private static void ValidateDynamicChildLifecycle(DynamicChildLifecycle lifecycle)
    {
        if (lifecycle.Applicability == "required")
        {
            if (lifecycle.Triggers.Count == 0 || lifecycle.Rationale is not null)
            {
                throw new DeterministicValidationException(
                    "Required dynamic child lifecycle coverage needs at least one trigger and no not-applicable rationale.");
            }
        }
        else if (lifecycle.Applicability == "not-applicable")
        {
            if (lifecycle.Triggers.Count != 0 ||
                string.IsNullOrWhiteSpace(lifecycle.Rationale))
            {
                throw new DeterministicValidationException(
                    "A not-applicable dynamic child lifecycle declaration requires a rationale and no triggers.");
            }
        }
        else
        {
            throw new DeterministicValidationException(
                "Dynamic child lifecycle applicability must be 'required' or 'not-applicable'.");
        }

        if (!lifecycle.Triggers.SequenceEqual(
                lifecycle.Triggers.Order(StringComparer.Ordinal),
                StringComparer.Ordinal) ||
            lifecycle.Triggers.Any(trigger =>
                !DynamicChildLifecycleTriggers.Contains(trigger, StringComparer.Ordinal)))
        {
            throw new DeterministicValidationException(
                "Dynamic child lifecycle triggers are not canonical, unique, and supported.");
        }
    }

    private static void ValidateSupplementalInput(
        string root,
        string basename,
        Sha256Digest digest,
        long size,
        string name)
    {
        var path = SafePath.ResolveUnderRoot(root, basename, requireExisting: true, requireFile: true);
        BoundedIO.EnsureFileLength(path, ResourceLimits.SupplementalInputAggregateBytes, name);
        if (new FileInfo(path).Length != size)
        {
            throw new DeterministicValidationException(
                $"{name} '{basename}' has a stale byte size.");
        }

        ValidateLocalDigest(root, basename, digest, name);
    }

    private static void ValidateSupplementalInputCeilings(
        IReadOnlyList<InputEvidenceArtifact> evidenceInputs,
        IReadOnlyList<OwnerInput> ownerInputs)
    {
        if (evidenceInputs.Count + ownerInputs.Count > ResourceLimits.SupplementalInputCount)
        {
            throw new DeterministicValidationException(
                $"Supplemental inputs exceed the {ResourceLimits.SupplementalInputCount}-file ceiling.");
        }

        long total = 0;
        foreach (var size in evidenceInputs.Select(input => input.Size)
                     .Concat(ownerInputs.Select(input => input.Size)))
        {
            total = checked(total + size);
            BoundedIO.EnsureLength(
                total,
                ResourceLimits.SupplementalInputAggregateBytes,
                "supplemental input aggregate");
        }
    }

    private static T[] RequireUnique<T>(T[] values, Func<T, string> key, string name)
    {
        if (values.Select(key).Distinct(StringComparer.Ordinal).Count() != values.Length)
        {
            throw new DeterministicValidationException($"{name} values must be unique.");
        }

        return values;
    }

    private static void ValidateUniqueAndSorted<T>(
        IReadOnlyList<T> values,
        Func<T, string> key,
        string name)
    {
        var keys = values.Select(key).ToArray();
        if (keys.Distinct(StringComparer.Ordinal).Count() != keys.Length ||
            !keys.SequenceEqual(keys.Order(StringComparer.Ordinal), StringComparer.Ordinal))
        {
            throw new DeterministicValidationException($"{name} must be unique and canonically sorted.");
        }
    }

    private static void WriteNullable(Utf8JsonWriter writer, string property, string? value)
    {
        if (value is null)
        {
            writer.WriteNull(property);
        }
        else
        {
            writer.WriteString(property, value);
        }
    }
}
