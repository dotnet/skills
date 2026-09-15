using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using BlazorComponentReadiness.Validator.Contracts;
using BlazorComponentReadiness.Validator.Evidence;
using BlazorComponentReadiness.Validator.IO;
using BlazorComponentReadiness.Validator.Validation;

namespace BlazorComponentReadiness.Validator.Comparison;

public static partial class ComparisonInputService
{
    public const int SchemaVersion = 2;
    private static readonly string[] ForbiddenPathTokens =
    [
        "assessment",
        "comparison",
        "feedback",
        "finding",
        "oracle",
        "recommendation",
        "report",
        "scorecard",
        "verdict"
    ];
    private static readonly string[] CommonCoverageSurfaces =
    [
        "exact-package",
        "official-public-documents",
        "owner-held-records"
    ];
    private static readonly string[] PackageCoverageSurfaces =
    [
        "dependency-and-notice-inventory",
        "release-source-and-workflows",
        "signing-sbom-provenance",
        "support-and-lifecycle"
    ];
    private static readonly string[] ComponentCoverageSurfaces =
    [
        "accessibility-and-localization",
        "browser-interop-and-style-assets",
        "claimed-mode-runtime",
        "component-api-and-base-source",
        "performance-measurements",
        "regression-and-release-mapping",
        "tests-and-samples",
        "trim-aot-toolchains"
    ];

    [GeneratedRegex(
        "\"(?:status|findings|recommendations|summary_groups)\"\\s*:|^#{1,6}\\s+.*(?:assessment|finding|recommendation|verdict)",
        RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.CultureInvariant |
        RegexOptions.NonBacktracking)]
    private static partial Regex ConclusionPattern();

    public static ComparisonInputManifest Freeze(
        string root,
        ReadOnlyMemory<byte> draftBytes)
    {
        using var document = StrictJson.Parse(
            draftBytes,
            ResourceLimits.SerializedArtifactBytes,
            "comparison input draft");
        var value = document.RootElement;
        ContractJson.RequirePropertiesUnordered(
            value,
            "schema_version",
            "state",
            "assessment_kinds",
            "package",
            "sources",
            "coverage",
            "allowed_inputs",
            "toolchains",
            "browsers",
            "retrievals",
            "probes");
        if (ContractJson.Int32(value, "schema_version") != SchemaVersion ||
            !string.Equals(
                ContractJson.String(value, "state").Trim(),
                "draft",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new DeterministicValidationException(
                "Comparison input draft schema or state is invalid.");
        }

        var assessmentKinds = CanonicalAssessmentKinds(
            ContractJson.StringArray(value, "assessment_kinds"));
        var allowed = ContractJson.Array(value, "allowed_inputs")
            .EnumerateArray()
            .Select(item => CreateAllowedInput(root, item))
            .OrderBy(item => item.Id, StringComparer.Ordinal)
            .ToArray();
        var package = ParsePackage(ContractJson.Object(value, "package"));
        var sources = ContractJson.Array(value, "sources")
            .EnumerateArray()
            .Select(ParseSource)
            .OrderBy(item => item.Id, StringComparer.Ordinal)
            .ToArray();
        var coverage = ContractJson.Array(value, "coverage")
            .EnumerateArray()
            .Select(ParseCoverageSurface)
            .OrderBy(item => item.Id, StringComparer.Ordinal)
            .ToArray();
        var toolchains = ContractJson.Array(value, "toolchains")
            .EnumerateArray()
            .Select(ParseExecutionIdentity)
            .OrderBy(item => item.Id, StringComparer.Ordinal)
            .ToArray();
        var browsers = ContractJson.Array(value, "browsers")
            .EnumerateArray()
            .Select(ParseExecutionIdentity)
            .OrderBy(item => item.Id, StringComparer.Ordinal)
            .ToArray();
        var retrievals = ContractJson.Array(value, "retrievals")
            .EnumerateArray()
            .Select(ParseRawRecord)
            .OrderBy(item => item.Id, StringComparer.Ordinal)
            .ToArray();
        var probes = ContractJson.Array(value, "probes")
            .EnumerateArray()
            .Select(ParseRawRecord)
            .OrderBy(item => item.Id, StringComparer.Ordinal)
            .ToArray();
        var manifest = new ComparisonInputManifest(
            SchemaVersion,
            "confirmed",
            assessmentKinds,
            package,
            sources,
            coverage,
            allowed,
            toolchains,
            browsers,
            retrievals,
            probes);
        Validate(manifest, root);
        return manifest;
    }

    public static void Validate(ComparisonInputManifest manifest, string root)
    {
        if (manifest.SchemaVersion is not (1 or SchemaVersion) ||
            manifest.State != "confirmed")
        {
            throw new DeterministicValidationException(
                "Comparison input manifest must be confirmed schema version 1 or 2.");
        }

        string[] assessmentKinds;
        if (manifest.SchemaVersion == 1)
        {
            if (manifest.AssessmentKinds.Count != 0 || manifest.Coverage.Count != 0)
            {
                throw new DeterministicValidationException(
                    "Legacy comparison schema version 1 cannot contain v2 assessment kinds or coverage.");
            }

            assessmentKinds = [];
        }
        else
        {
            assessmentKinds = CanonicalAssessmentKinds(manifest.AssessmentKinds);
            if (!manifest.AssessmentKinds.SequenceEqual(assessmentKinds, StringComparer.Ordinal))
            {
                throw new DeterministicValidationException(
                    "Comparison assessment kinds must be unique and canonically sorted.");
            }
        }

        RequireUniqueSorted(manifest.AllowedInputs, item => item.Id, "allowed inputs");
        if (manifest.AllowedInputs.Count == 0)
        {
            throw new DeterministicValidationException(
                "Comparison input manifest requires an allowed-input set.");
        }

        foreach (var input in manifest.AllowedInputs)
        {
            _ = CanonicalId(input.Id, "allowed input ID");
            _ = CanonicalId(input.Kind, "allowed input kind");
            var path = Canonicalization.RelativePath(input.Path, "allowed input path");
            if (path != input.Path ||
                ForbiddenPathTokens.Any(token =>
                    Path.GetFileName(path).Contains(token, StringComparison.OrdinalIgnoreCase)))
            {
                throw new DeterministicValidationException(
                    $"Allowed input '{input.Id}' has a revealing or non-canonical path.");
            }

            var fullPath = SafePath.ResolveUnderRoot(root, path, requireExisting: true, requireFile: true);
            BoundedIO.EnsureFileLength(
                fullPath,
                ResourceLimits.SupplementalInputAggregateBytes,
                "comparison allowed input");
            if (new FileInfo(fullPath).Length != input.Size ||
                ContractJson.RawDigest(BoundedIO.ReadAllBytes(
                    fullPath,
                    ResourceLimits.SupplementalInputAggregateBytes,
                    "comparison allowed input")) != input.ContentDigest)
            {
                throw new DeterministicValidationException(
                    $"Allowed input '{input.Id}' size or digest is stale.");
            }

            EnsureConclusionFree(fullPath);
        }

        if (manifest.SchemaVersion == SchemaVersion)
        {
            var repeatedPath = manifest.AllowedInputs
                .GroupBy(item => item.Path, StringComparer.Ordinal)
                .FirstOrDefault(group => group.Count() > 1);
            var repeatedDigest = manifest.AllowedInputs
                .GroupBy(item => item.ContentDigest)
                .FirstOrDefault(group => group.Count() > 1);
            if (repeatedPath is not null || repeatedDigest is not null)
            {
                throw new DeterministicValidationException(
                    "Schema-v2 allowed inputs cannot reuse a path or content digest without an explicit safe alias rule.");
            }
        }

        var byId = manifest.AllowedInputs.ToDictionary(item => item.Id, StringComparer.Ordinal);
        var packageInput = RequireInput(byId, manifest.Package.NupkgInputId, "nupkg");
        var inspected = NupkgInspector.Inspect(
            SafePath.ResolveUnderRoot(root, packageInput.Path, requireExisting: true, requireFile: true));
        var expectedPackage = new ComparisonPackageIdentity(
            Canonicalization.PackageId(inspected.Id),
            NuGetVersionNormalizer.Normalize(inspected.Version),
            packageInput.Id,
            new Sha256Digest("sha256", inspected.NupkgSha256));
        if (manifest.Package != expectedPackage)
        {
            throw new DeterministicValidationException(
                "Comparison package identity does not match the exact allowed nupkg.");
        }

        RequireUniqueSorted(manifest.Sources, item => item.Id, "source identities");
        foreach (var source in manifest.Sources)
        {
            ValidateSource(source, byId);
        }
        ValidateExecutionIdentities(manifest.Toolchains, byId, "toolchain");
        ValidateExecutionIdentities(manifest.Browsers, byId, "browser");
        ValidateRawRecords(
            manifest.Retrievals,
            byId,
            manifest.Toolchains,
            manifest.Browsers,
            "retrieval");
        ValidateRawRecords(
            manifest.Probes,
            byId,
            manifest.Toolchains,
            manifest.Browsers,
            "probe");
        if (manifest.SchemaVersion == SchemaVersion)
        {
            ValidateCoverage(
                manifest.Coverage,
                assessmentKinds,
                manifest.Package.NupkgInputId,
                byId,
                manifest.Sources,
                manifest.Toolchains,
                manifest.Browsers,
                manifest.Retrievals,
                manifest.Probes);
        }

        foreach (var probe in manifest.Probes.Where(probe =>
                     probe.Subject.Contains("trim", StringComparison.OrdinalIgnoreCase) ||
                     probe.Subject.Contains("aot", StringComparison.OrdinalIgnoreCase)))
        {
            if (probe.Disposition == "available" && probe.ToolchainId is null)
            {
                throw new DeterministicValidationException(
                    $"Trim/AOT probe '{probe.Id}' requires a named supported toolchain.");
            }
        }

        var referenced = new HashSet<string>(StringComparer.Ordinal)
        {
            manifest.Package.NupkgInputId
        };
        foreach (var source in manifest.Sources.Where(source =>
                     source.ArchiveInputId is not null))
        {
            referenced.Add(source.ArchiveInputId!);
        }

        foreach (var id in manifest.Toolchains.SelectMany(item => item.InputIds)
                     .Concat(manifest.Browsers.SelectMany(item => item.InputIds))
                     .Concat(manifest.Coverage.SelectMany(item =>
                         item.Bindings.Select(binding => binding.InputId)))
                     .Concat(manifest.Retrievals.SelectMany(item => item.InputIds))
                     .Concat(manifest.Probes.SelectMany(item => item.InputIds)))
        {
            referenced.Add(id);
        }

        if (!referenced.SetEquals(byId.Keys))
        {
            throw new DeterministicValidationException(
                "Comparison allowed-input set must exactly equal the package, source, retrieval, probe, toolchain, and browser inputs.");
        }
    }

    public static byte[] Serialize(ComparisonInputManifest manifest)
    {
        var bytes = StrictJson.SerializeCanonical(writer =>
        {
            writer.WriteStartObject();
            writer.WriteNumber("schema_version", manifest.SchemaVersion);
            writer.WriteString("state", manifest.State);
            if (manifest.SchemaVersion == SchemaVersion)
            {
                WriteStrings(writer, "assessment_kinds", manifest.AssessmentKinds);
            }

            writer.WritePropertyName("package");
            writer.WriteStartObject();
            writer.WriteString("package_id", manifest.Package.PackageId);
            writer.WriteString("version", manifest.Package.Version);
            writer.WriteString("nupkg_input_id", manifest.Package.NupkgInputId);
            ContractJson.WriteDigest(writer, "nupkg_sha256", manifest.Package.NupkgDigest);
            writer.WriteEndObject();
            writer.WritePropertyName("sources");
            writer.WriteStartArray();
            foreach (var source in manifest.Sources)
            {
                writer.WriteStartObject();
                writer.WriteString("id", source.Id);
                writer.WriteString("availability", source.Availability);
                WriteNullable(writer, "repository_uri", source.RepositoryUri);
                WriteNullable(writer, "commit", source.Commit);
                WriteNullable(writer, "archive_input_id", source.ArchiveInputId);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            if (manifest.SchemaVersion == SchemaVersion)
            {
                WriteCoverage(writer, manifest.Coverage);
            }

            WriteAllowedInputs(writer, manifest.AllowedInputs);
            WriteExecutionIdentities(writer, "toolchains", manifest.Toolchains);
            WriteExecutionIdentities(writer, "browsers", manifest.Browsers);
            WriteRawRecords(writer, "retrievals", manifest.Retrievals);
            WriteRawRecords(writer, "probes", manifest.Probes);
            writer.WriteEndObject();
        });
        BoundedIO.EnsureLength(
            bytes.Length,
            ResourceLimits.SerializedArtifactBytes,
            "comparison input manifest");
        return bytes;
    }

    public static ComparisonInputManifest Parse(ReadOnlyMemory<byte> bytes)
    {
        using var document = StrictJson.Parse(
            bytes,
            ResourceLimits.SerializedArtifactBytes,
            "comparison input manifest");
        var root = document.RootElement;
        var schemaVersion = ContractJson.Int32(root, "schema_version");
        if (schemaVersion == 1)
        {
            ContractJson.RequireProperties(
                root,
                "schema_version",
                "state",
                "package",
                "sources",
                "allowed_inputs",
                "toolchains",
                "browsers",
                "retrievals",
                "probes");
        }
        else if (schemaVersion == SchemaVersion)
        {
            ContractJson.RequireProperties(
                root,
                "schema_version",
                "state",
                "assessment_kinds",
                "package",
                "sources",
                "coverage",
                "allowed_inputs",
                "toolchains",
                "browsers",
                "retrievals",
                "probes");
        }
        else
        {
            throw new DeterministicValidationException(
                "Comparison input manifest schema version is unsupported.");
        }

        var manifest = new ComparisonInputManifest(
            schemaVersion,
            ContractJson.String(root, "state"),
            schemaVersion == SchemaVersion
                ? ContractJson.StringArray(root, "assessment_kinds")
                : [],
            ParsePackage(ContractJson.Object(root, "package")),
            ContractJson.Array(root, "sources")
                .EnumerateArray()
                .Select(ParseSource)
                .ToArray(),
            schemaVersion == SchemaVersion
                ? ContractJson.Array(root, "coverage")
                    .EnumerateArray()
                    .Select(ParseCoverageSurface)
                    .ToArray()
                : [],
            ContractJson.Array(root, "allowed_inputs")
                .EnumerateArray()
                .Select(ParseAllowedInput)
                .ToArray(),
            ContractJson.Array(root, "toolchains")
                .EnumerateArray()
                .Select(ParseExecutionIdentity)
                .ToArray(),
            ContractJson.Array(root, "browsers")
                .EnumerateArray()
                .Select(ParseExecutionIdentity)
                .ToArray(),
            ContractJson.Array(root, "retrievals")
                .EnumerateArray()
                .Select(ParseRawRecord)
                .ToArray(),
            ContractJson.Array(root, "probes")
                .EnumerateArray()
                .Select(ParseRawRecord)
                .ToArray());
        ContractJson.RequireCanonical(bytes.Span, Serialize(manifest), "comparison input manifest");
        return manifest;
    }

    private static ComparisonAllowedInput CreateAllowedInput(string root, JsonElement element)
    {
        ContractJson.RequirePropertiesUnordered(element, "id", "kind", "path");
        var id = CanonicalId(ContractJson.String(element, "id"), "allowed input ID");
        var kind = CanonicalId(ContractJson.String(element, "kind"), "allowed input kind");
        var path = Canonicalization.RelativePath(
            ContractJson.String(element, "path"),
            "allowed input path");
        if (!File.Exists(Path.GetFullPath(Path.Combine(root, path))))
        {
            throw new DeterministicValidationException(
                $"Allowed input '{id}' path '{path}' does not exist.");
        }

        var fullPath = SafePath.ResolveUnderRoot(root, path, requireExisting: true, requireFile: true);
        var bytes = BoundedIO.ReadAllBytes(
            fullPath,
            ResourceLimits.SupplementalInputAggregateBytes,
            "comparison allowed input");
        return new ComparisonAllowedInput(
            id,
            kind,
            path,
            ContractJson.RawDigest(bytes),
            bytes.LongLength);
    }

    private static ComparisonAllowedInput ParseAllowedInput(JsonElement element)
    {
        ContractJson.RequireProperties(element, "id", "kind", "path", "content_sha256", "size");
        return new ComparisonAllowedInput(
            ContractJson.String(element, "id"),
            ContractJson.String(element, "kind"),
            ContractJson.String(element, "path"),
            ContractJson.Digest(ContractJson.Object(element, "content_sha256")),
            ContractJson.Int64(element, "size"));
    }

    private static ComparisonPackageIdentity ParsePackage(JsonElement element)
    {
        ContractJson.RequirePropertiesUnordered(
            element,
            "package_id",
            "version",
            "nupkg_input_id",
            "nupkg_sha256");
        return new ComparisonPackageIdentity(
            Canonicalization.PackageId(ContractJson.String(element, "package_id")),
            NuGetVersionNormalizer.Normalize(ContractJson.String(element, "version")),
            CanonicalId(ContractJson.String(element, "nupkg_input_id"), "nupkg input ID"),
            ContractJson.Digest(ContractJson.Object(element, "nupkg_sha256")));
    }

    private static ComparisonSourceIdentity ParseSource(JsonElement element)
    {
        ContractJson.RequirePropertiesUnordered(
            element,
            "id",
            "availability",
            "repository_uri",
            "commit",
            "archive_input_id");
        return new ComparisonSourceIdentity(
            CanonicalId(ContractJson.String(element, "id"), "source identity ID"),
            ContractJson.String(element, "availability").Trim().ToLowerInvariant(),
            ContractJson.NullableString(element, "repository_uri"),
            ContractJson.NullableString(element, "commit"),
            ContractJson.NullableString(element, "archive_input_id"));
    }

    private static ComparisonExecutionIdentity ParseExecutionIdentity(JsonElement element)
    {
        ContractJson.RequirePropertiesUnordered(
            element,
            "id",
            "name",
            "version",
            "disposition",
            "blocker",
            "input_ids");
        return new ComparisonExecutionIdentity(
            CanonicalId(ContractJson.String(element, "id"), "execution identity ID"),
            ContractJson.NormalizeText(ContractJson.String(element, "name"), "execution name", 256),
            ContractJson.NormalizeText(ContractJson.String(element, "version"), "execution version", 256),
            ContractJson.String(element, "disposition").Trim().ToLowerInvariant(),
            ContractJson.NullableString(element, "blocker"),
            CanonicalIds(ContractJson.StringArray(element, "input_ids"), "execution input ID"));
    }

    private static ComparisonRawRecord ParseRawRecord(JsonElement element)
    {
        ContractJson.RequirePropertiesUnordered(
            element,
            "id",
            "subject",
            "locator",
            "disposition",
            "blocker",
            "toolchain_id",
            "browser_id",
            "input_ids");
        return new ComparisonRawRecord(
            CanonicalId(ContractJson.String(element, "id"), "raw record ID"),
            ContractJson.NormalizeText(ContractJson.String(element, "subject"), "raw record subject", 256),
            ContractJson.NormalizeText(ContractJson.String(element, "locator"), "raw record locator", 2048),
            ContractJson.String(element, "disposition").Trim().ToLowerInvariant(),
            ContractJson.NullableString(element, "blocker"),
            ContractJson.NullableString(element, "toolchain_id"),
            ContractJson.NullableString(element, "browser_id"),
            CanonicalIds(ContractJson.StringArray(element, "input_ids"), "raw record input ID"));
    }

    private static ComparisonCoverageSurface ParseCoverageSurface(JsonElement element)
    {
        ContractJson.RequirePropertiesUnordered(
            element,
            "id",
            "disposition",
            "blocker",
            "bindings");
        return new ComparisonCoverageSurface(
            CanonicalId(ContractJson.String(element, "id"), "coverage surface ID"),
            ContractJson.String(element, "disposition").Trim().ToLowerInvariant(),
            ContractJson.NullableString(element, "blocker"),
            ContractJson.Array(element, "bindings")
                .EnumerateArray()
                .Select(ParseCoverageBinding)
                .OrderBy(item => item.Role, StringComparer.Ordinal)
                .ToArray());
    }

    private static ComparisonCoverageBinding ParseCoverageBinding(JsonElement element)
    {
        ContractJson.RequirePropertiesUnordered(
            element,
            "role",
            "input_id",
            "origin_kind",
            "origin_id");
        return new ComparisonCoverageBinding(
            CanonicalId(ContractJson.String(element, "role"), "coverage role"),
            CanonicalId(ContractJson.String(element, "input_id"), "coverage input ID"),
            CanonicalId(ContractJson.String(element, "origin_kind"), "coverage origin kind"),
            CanonicalId(ContractJson.String(element, "origin_id"), "coverage origin ID"));
    }

    private static void ValidateSource(
        ComparisonSourceIdentity source,
        IReadOnlyDictionary<string, ComparisonAllowedInput> byId)
    {
        if (source.Availability == "source-available")
        {
            if (source.RepositoryUri is null ||
                source.Commit is null ||
                source.ArchiveInputId is null ||
                Canonicalization.HttpsUri(source.RepositoryUri, requirePath: true) != source.RepositoryUri ||
                Canonicalization.Commit(source.Commit) != source.Commit)
            {
                throw new DeterministicValidationException(
                    "Source-available comparison input requires exact repository, commit, and archive input.");
            }

            _ = RequireInput(byId, source.ArchiveInputId, "source-archive");
            return;
        }

        if (source.Availability == "closed-source" &&
            source.RepositoryUri is null &&
            source.Commit is null &&
            source.ArchiveInputId is null)
        {
            return;
        }

        throw new DeterministicValidationException(
            "Comparison source identity must be exact source-available or explicit closed-source.");
    }

    private static void ValidateExecutionIdentities(
        IReadOnlyList<ComparisonExecutionIdentity> values,
        IReadOnlyDictionary<string, ComparisonAllowedInput> byId,
        string kind)
    {
        RequireUniqueSorted(values, item => item.Id, $"{kind} identities");
        foreach (var value in values)
        {
            ValidateDisposition(value.Disposition, value.Blocker, value.InputIds, $"{kind} '{value.Id}'");
            foreach (var inputId in value.InputIds)
            {
                _ = RequireInput(byId, inputId, $"{kind}-identity");
            }
        }
    }

    private static void ValidateRawRecords(
        IReadOnlyList<ComparisonRawRecord> values,
        IReadOnlyDictionary<string, ComparisonAllowedInput> byId,
        IReadOnlyList<ComparisonExecutionIdentity> toolchains,
        IReadOnlyList<ComparisonExecutionIdentity> browsers,
        string kind)
    {
        RequireUniqueSorted(values, item => item.Id, $"{kind} records");
        foreach (var value in values)
        {
            ValidateDisposition(value.Disposition, value.Blocker, value.InputIds, $"{kind} '{value.Id}'");
            foreach (var inputId in value.InputIds)
            {
                if (!byId.ContainsKey(inputId))
                {
                    throw new DeterministicValidationException(
                        $"{kind} '{value.Id}' references missing allowed input '{inputId}'.");
                }
            }

            if (value.ToolchainId is not null &&
                !toolchains.Any(item =>
                    item.Id == value.ToolchainId &&
                    item.Disposition == "available"))
            {
                throw new DeterministicValidationException(
                    $"{kind} '{value.Id}' references an unavailable toolchain.");
            }

            if (value.BrowserId is not null &&
                !browsers.Any(item =>
                    item.Id == value.BrowserId &&
                    item.Disposition == "available"))
            {
                throw new DeterministicValidationException(
                    $"{kind} '{value.Id}' references an unavailable browser.");
            }
        }
    }

    private static void ValidateCoverage(
        IReadOnlyList<ComparisonCoverageSurface> coverage,
        IReadOnlyList<string> assessmentKinds,
        string nupkgInputId,
        IReadOnlyDictionary<string, ComparisonAllowedInput> byId,
        IReadOnlyList<ComparisonSourceIdentity> sources,
        IReadOnlyList<ComparisonExecutionIdentity> toolchains,
        IReadOnlyList<ComparisonExecutionIdentity> browsers,
        IReadOnlyList<ComparisonRawRecord> retrievals,
        IReadOnlyList<ComparisonRawRecord> probes)
    {
        RequireUniqueSorted(coverage, item => item.Id, "coverage surfaces");
        var expected = RequiredCoverageSurfaces(assessmentKinds);
        if (!coverage.Select(item => item.Id).SequenceEqual(expected, StringComparer.Ordinal))
        {
            var missing = expected.Except(
                coverage.Select(item => item.Id),
                StringComparer.Ordinal).FirstOrDefault();
            var unexpected = coverage.Select(item => item.Id).Except(
                expected,
                StringComparer.Ordinal).FirstOrDefault();
            var detail = missing is not null
                ? $"missing required surface '{missing}'"
                : $"unexpected surface '{unexpected}'";
            throw new DeterministicValidationException(
                $"Comparison coverage surfaces must exactly match the selected assessment kinds: {detail}.");
        }

        foreach (var surface in coverage)
        {
            RequireUniqueSorted(
                surface.Bindings,
                item => item.Role,
                $"coverage surface '{surface.Id}' bindings");
            var roleRequirements = CoverageRoleRequirements(surface.Id);
            var expectedRoles = roleRequirements.Select(item => item.Role).ToArray();
            foreach (var binding in surface.Bindings)
            {
                var role = roleRequirements.SingleOrDefault(item => item.Role == binding.Role);
                if (role is null)
                {
                    throw new DeterministicValidationException(
                        $"Coverage surface '{surface.Id}' contains unknown role '{binding.Role}'.");
                }

                if (!byId.TryGetValue(binding.InputId, out var input))
                {
                    throw new DeterministicValidationException(
                        $"Coverage surface '{surface.Id}' references missing allowed input '{binding.InputId}'.");
                }

                if (!role.AcceptedKinds.Contains(input.Kind, StringComparer.Ordinal))
                {
                    throw new DeterministicValidationException(
                        $"Coverage surface '{surface.Id}' role '{binding.Role}' requires input kind " +
                        $"'{string.Join("' or '", role.AcceptedKinds)}', not '{input.Kind}'.");
                }

                if (!role.AcceptedOrigins.Contains(binding.OriginKind, StringComparer.Ordinal) ||
                    !OriginBinds(
                        binding,
                        surface.Disposition == "available",
                        nupkgInputId,
                        sources,
                        toolchains,
                        browsers,
                        retrievals,
                        probes))
                {
                    throw new DeterministicValidationException(
                        $"Coverage surface '{surface.Id}' role '{binding.Role}' has an invalid or unavailable " +
                        $"'{binding.OriginKind}' origin '{binding.OriginId}'.");
                }
            }

            if (surface.Disposition == "available")
            {
                if (surface.Blocker is not null ||
                    !surface.Bindings.Select(item => item.Role)
                        .SequenceEqual(expectedRoles, StringComparer.Ordinal))
                {
                    throw new DeterministicValidationException(
                        $"Coverage surface '{surface.Id}' available disposition requires every typed role and no blocker.");
                }
            }
            else if (surface.Disposition == "blocked")
            {
                if (surface.Blocker is null ||
                    ContractJson.NormalizeText(
                        surface.Blocker,
                        $"coverage surface '{surface.Id}' blocker",
                        2048).Length < 8)
                {
                    throw new DeterministicValidationException(
                        $"Coverage surface '{surface.Id}' blocked disposition requires an exact blocker.");
                }
            }
            else
            {
                throw new DeterministicValidationException(
                    $"Coverage surface '{surface.Id}' disposition must resolve to available or blocked.");
            }
        }

        var coverageUses = coverage
            .SelectMany(surface => surface.Bindings.Select(binding => new
            {
                Surface = surface.Id,
                binding.Role,
                binding.InputId,
                Input = byId[binding.InputId]
            }))
            .ToArray();
        var reusedInput = coverageUses
            .GroupBy(item => item.InputId, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (reusedInput is not null)
        {
            throw new DeterministicValidationException(
                $"Coverage input '{reusedInput.Key}' cannot satisfy multiple semantic roles without an explicit safe alias rule.");
        }

        for (var left = 0; left < coverageUses.Length; left++)
        {
            for (var right = left + 1; right < coverageUses.Length; right++)
            {
                if (coverageUses[left].Input.Path == coverageUses[right].Input.Path ||
                    coverageUses[left].Input.ContentDigest == coverageUses[right].Input.ContentDigest)
                {
                    throw new DeterministicValidationException(
                        $"Coverage roles '{coverageUses[left].Surface}/{coverageUses[left].Role}' and " +
                        $"'{coverageUses[right].Surface}/{coverageUses[right].Role}' reuse the same path or bytes " +
                        "without an explicit safe alias rule.");
                }
            }
        }

        var exactPackage = coverage.Single(item => item.Id == "exact-package");
        if (exactPackage.Disposition != "available" ||
            exactPackage.Bindings.Count != 1 ||
            exactPackage.Bindings[0].InputId != nupkgInputId ||
            exactPackage.Bindings[0].OriginKind != "package")
        {
            throw new DeterministicValidationException(
                "The exact-package coverage surface must be available and bind the canonical nupkg input.");
        }
    }

    private static bool OriginBinds(
        ComparisonCoverageBinding binding,
        bool requireAvailable,
        string nupkgInputId,
        IReadOnlyList<ComparisonSourceIdentity> sources,
        IReadOnlyList<ComparisonExecutionIdentity> toolchains,
        IReadOnlyList<ComparisonExecutionIdentity> browsers,
        IReadOnlyList<ComparisonRawRecord> retrievals,
        IReadOnlyList<ComparisonRawRecord> probes) =>
        binding.OriginKind switch
        {
            "package" =>
                binding.OriginId == "package" &&
                binding.InputId == nupkgInputId,
            "source" => sources.Any(source =>
                source.Id == binding.OriginId &&
                source.ArchiveInputId == binding.InputId &&
                (!requireAvailable || source.Availability == "source-available")),
            "toolchain" => toolchains.Any(toolchain =>
                toolchain.Id == binding.OriginId &&
                toolchain.InputIds.Contains(binding.InputId, StringComparer.Ordinal) &&
                (!requireAvailable || toolchain.Disposition == "available")),
            "browser" => browsers.Any(browser =>
                browser.Id == binding.OriginId &&
                browser.InputIds.Contains(binding.InputId, StringComparer.Ordinal) &&
                (!requireAvailable || browser.Disposition == "available")),
            "retrieval" => retrievals.Any(retrieval =>
                retrieval.Id == binding.OriginId &&
                retrieval.InputIds.Contains(binding.InputId, StringComparer.Ordinal) &&
                (!requireAvailable || retrieval.Disposition == "available")),
            "probe" => probes.Any(probe =>
                probe.Id == binding.OriginId &&
                probe.InputIds.Contains(binding.InputId, StringComparer.Ordinal) &&
                (!requireAvailable || probe.Disposition == "available")),
            _ => false
        };

    private static void ValidateDisposition(
        string disposition,
        string? blocker,
        IReadOnlyList<string> inputIds,
        string name)
    {
        if (disposition == "available")
        {
            if (blocker is not null || inputIds.Count == 0)
            {
                throw new DeterministicValidationException(
                    $"{name} available disposition requires raw inputs and no blocker.");
            }

            return;
        }

        if (disposition == "blocked" &&
            blocker is not null &&
            ContractJson.NormalizeText(blocker, $"{name} blocker", 2048).Length >= 8)
        {
            return;
        }

        throw new DeterministicValidationException(
            $"{name} disposition must resolve to available or blocked with an exact blocker.");
    }

    private static ComparisonAllowedInput RequireInput(
        IReadOnlyDictionary<string, ComparisonAllowedInput> byId,
        string id,
        string expectedKind)
    {
        if (!byId.TryGetValue(id, out var input) || input.Kind != expectedKind)
        {
            throw new DeterministicValidationException(
                $"Comparison input '{id}' must exist with kind '{expectedKind}'.");
        }

        return input;
    }

    private static string CanonicalId(string value, string name)
    {
        var result = value.Trim().ToLowerInvariant();
        if (result.Length is 0 or > 128 ||
            result.Any(character =>
                character is not (>= 'a' and <= 'z') and
                not (>= '0' and <= '9') and
                not '-' and not '.'))
        {
            throw new DeterministicValidationException($"{name} is not canonical.");
        }

        return result;
    }

    private static string[] CanonicalIds(IReadOnlyList<string> values, string name)
    {
        var result = values.Select(value => CanonicalId(value, name))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (result.Length != values.Count)
        {
            throw new DeterministicValidationException($"{name} values must be unique.");
        }

        return result;
    }

    private static string[] CanonicalAssessmentKinds(IReadOnlyList<string> values)
    {
        var result = values
            .Select(value => value.Trim().ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (result.Length == 0 ||
            result.Length != values.Count ||
            result.Any(value => value is not ("unified" or "package" or "component")) ||
            (result.Contains("unified", StringComparer.Ordinal) && result.Length != 1))
        {
            throw new DeterministicValidationException(
                "Comparison assessment_kinds must be unified alone, package, component, or package plus component.");
        }

        return result;
    }

    private static string[] RequiredCoverageSurfaces(IReadOnlyList<string> assessmentKinds)
    {
        var includePackage = assessmentKinds.Contains("unified", StringComparer.Ordinal) ||
            assessmentKinds.Contains("package", StringComparer.Ordinal);
        var includeComponent = assessmentKinds.Contains("unified", StringComparer.Ordinal) ||
            assessmentKinds.Contains("component", StringComparer.Ordinal);
        return CommonCoverageSurfaces
            .Concat(includePackage ? PackageCoverageSurfaces : [])
            .Concat(includeComponent ? ComponentCoverageSurfaces : [])
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    private static CoverageRoleRequirement[] CoverageRoleRequirements(string surfaceId) =>
        surfaceId switch
        {
            "exact-package" =>
            [
                Role("package", "nupkg", "package")
            ],
            "official-public-documents" =>
            [
                Role("documentation-corpus", "public-document-corpus", "retrieval")
            ],
            "owner-held-records" =>
            [
                Role("owner-record-corpus", "owner-record-corpus", "retrieval")
            ],
            "dependency-and-notice-inventory" =>
            [
                Role("asset-inventory", "asset-inventory", "probe"),
                Role("dependency-inventory", "dependency-inventory", "probe"),
                Role("notice-mapping", "notice-mapping", "probe")
            ],
            "release-source-and-workflows" =>
            [
                Role("source-snapshot", "source-archive", "source"),
                Role("workflow-inventory", "workflow-inventory", "probe")
            ],
            "signing-sbom-provenance" =>
            [
                Role("assembly-signing", "assembly-signing", "probe"),
                Role("package-signing", "package-signing", "probe"),
                Role("sbom-provenance", "sbom-provenance", "probe")
            ],
            "support-and-lifecycle" =>
            [
                Role("public-support-corpus", "public-support-corpus", "probe"),
                Role("release-lifecycle-corpus", "release-lifecycle-corpus", "probe")
            ],
            "component-api-and-base-source" =>
            [
                Role("component-source-closure", "component-source-closure", "probe")
            ],
            "browser-interop-and-style-assets" =>
            [
                Role("browser-interop-source", "browser-interop-source", "probe"),
                Role("style-asset-inventory", "style-asset-inventory", "probe")
            ],
            "tests-and-samples" =>
            [
                Role("sample-inventory", "sample-inventory", "probe"),
                Role("test-inventory", "test-inventory", "probe")
            ],
            "claimed-mode-runtime" =>
            [
                Role("mode-claims", "target-manifest", "retrieval", "probe"),
                Role("runtime-observations", "runtime-observation", "probe")
            ],
            "accessibility-and-localization" =>
            [
                Role("accessibility-observations", "accessibility-observation", "probe"),
                Role("localization-claims", "localization-claim-corpus", "retrieval", "probe")
            ],
            "trim-aot-toolchains" =>
            [
                Role("toolchain-identities", "toolchain-identity", "toolchain"),
                Role("trim-aot-observations", "toolchain-probe", "probe")
            ],
            "performance-measurements" =>
            [
                Role("performance-results", "performance-result", "probe"),
                Role("performance-scenarios", "performance-scenario", "retrieval", "probe")
            ],
            "regression-and-release-mapping" =>
            [
                Role("defect-regression-map", "regression-map", "retrieval", "probe"),
                Role("release-revalidation", "release-revalidation", "retrieval", "probe")
            ],
            _ => throw new DeterministicValidationException(
                $"Unknown comparison coverage surface '{surfaceId}'.")
        };

    private static CoverageRoleRequirement Role(
        string role,
        string acceptedKind,
        params string[] acceptedOrigins) =>
        new(role, [acceptedKind], acceptedOrigins);

    private static void RequireUniqueSorted<T>(
        IReadOnlyList<T> values,
        Func<T, string> key,
        string name)
    {
        var keys = values.Select(key).ToArray();
        if (keys.Distinct(StringComparer.Ordinal).Count() != keys.Length ||
            !keys.SequenceEqual(keys.Order(StringComparer.Ordinal), StringComparer.Ordinal))
        {
            throw new DeterministicValidationException(
                $"{name} must be unique and canonically sorted.");
        }
    }

    private static void EnsureConclusionFree(string path)
    {
        var bytes = BoundedIO.ReadAllBytes(
            path,
            ResourceLimits.SupplementalInputAggregateBytes,
            "comparison allowed input");
        if (bytes.Contains((byte)0))
        {
            return;
        }

        string text;
        try
        {
            text = new UTF8Encoding(false, true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return;
        }

        if (ConclusionPattern().IsMatch(text))
        {
            throw new DeterministicValidationException(
                $"Allowed input '{Path.GetFileName(path)}' contains conclusion-bearing assessment material.");
        }
    }

    private static void WriteAllowedInputs(
        Utf8JsonWriter writer,
        IReadOnlyList<ComparisonAllowedInput> values)
    {
        writer.WritePropertyName("allowed_inputs");
        writer.WriteStartArray();
        foreach (var value in values)
        {
            writer.WriteStartObject();
            writer.WriteString("id", value.Id);
            writer.WriteString("kind", value.Kind);
            writer.WriteString("path", value.Path);
            ContractJson.WriteDigest(writer, "content_sha256", value.ContentDigest);
            writer.WriteNumber("size", value.Size);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteCoverage(
        Utf8JsonWriter writer,
        IReadOnlyList<ComparisonCoverageSurface> values)
    {
        writer.WritePropertyName("coverage");
        writer.WriteStartArray();
        foreach (var value in values)
        {
            writer.WriteStartObject();
            writer.WriteString("id", value.Id);
            writer.WriteString("disposition", value.Disposition);
            WriteNullable(writer, "blocker", value.Blocker);
            writer.WritePropertyName("bindings");
            writer.WriteStartArray();
            foreach (var binding in value.Bindings)
            {
                writer.WriteStartObject();
                writer.WriteString("role", binding.Role);
                writer.WriteString("input_id", binding.InputId);
                writer.WriteString("origin_kind", binding.OriginKind);
                writer.WriteString("origin_id", binding.OriginId);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteExecutionIdentities(
        Utf8JsonWriter writer,
        string property,
        IReadOnlyList<ComparisonExecutionIdentity> values)
    {
        writer.WritePropertyName(property);
        writer.WriteStartArray();
        foreach (var value in values)
        {
            writer.WriteStartObject();
            writer.WriteString("id", value.Id);
            writer.WriteString("name", value.Name);
            writer.WriteString("version", value.Version);
            writer.WriteString("disposition", value.Disposition);
            WriteNullable(writer, "blocker", value.Blocker);
            WriteStrings(writer, "input_ids", value.InputIds);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteRawRecords(
        Utf8JsonWriter writer,
        string property,
        IReadOnlyList<ComparisonRawRecord> values)
    {
        writer.WritePropertyName(property);
        writer.WriteStartArray();
        foreach (var value in values)
        {
            writer.WriteStartObject();
            writer.WriteString("id", value.Id);
            writer.WriteString("subject", value.Subject);
            writer.WriteString("locator", value.Locator);
            writer.WriteString("disposition", value.Disposition);
            WriteNullable(writer, "blocker", value.Blocker);
            WriteNullable(writer, "toolchain_id", value.ToolchainId);
            WriteNullable(writer, "browser_id", value.BrowserId);
            WriteStrings(writer, "input_ids", value.InputIds);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteStrings(
        Utf8JsonWriter writer,
        string property,
        IReadOnlyList<string> values)
    {
        writer.WritePropertyName(property);
        writer.WriteStartArray();
        foreach (var value in values)
        {
            writer.WriteStringValue(value);
        }

        writer.WriteEndArray();
    }

    private static void WriteNullable(
        Utf8JsonWriter writer,
        string property,
        string? value)
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

    private sealed record CoverageRoleRequirement(
        string Role,
        IReadOnlyList<string> AcceptedKinds,
        IReadOnlyList<string> AcceptedOrigins);
}
