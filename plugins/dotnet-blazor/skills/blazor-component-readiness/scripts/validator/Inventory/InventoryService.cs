using System.Text.Json;
using BlazorComponentReadiness.Validator.Contracts;
using BlazorComponentReadiness.Validator.Inputs;
using BlazorComponentReadiness.Validator.IO;
using BlazorComponentReadiness.Validator.Validation;

namespace BlazorComponentReadiness.Validator.Inventory;

public static class InventoryService
{
    public const int SchemaVersion = 1;

    public static LibraryInventory Discover(string root, ReadOnlyMemory<byte> candidateBytes)
    {
        using var document = StrictJson.Parse(
            candidateBytes,
            ResourceLimits.SerializedArtifactBytes,
            "inventory discovery candidates");
        var value = document.RootElement;
        ContractJson.RequirePropertiesUnordered(value, "schema_version", "packages");
        if (ContractJson.Int32(value, "schema_version") != SchemaVersion)
        {
            throw new DeterministicValidationException(
                "Inventory discovery candidate schema_version must be 1.");
        }

        var packages = ContractJson.Array(value, "packages")
            .EnumerateArray()
            .Select(item => ParseCandidatePackage(root, item))
            .OrderBy(item => item.UnitId, StringComparer.Ordinal)
            .ToArray();
        var inventory = new LibraryInventory(SchemaVersion, "draft", null, null, packages);
        Validate(inventory, root, requireConfirmed: false);
        return inventory;
    }

    public static LibraryInventory Confirm(LibraryInventory draft, string root)
    {
        Validate(draft, root, requireConfirmed: false);
        if (draft.State != "draft" || draft.InventoryId is not null || draft.RunId is not null)
        {
            throw new DeterministicValidationException("Only an unidentified draft inventory can be confirmed.");
        }

        var content = draft with { State = "confirmed" };
        var identity = "LIB1-" + DomainSeparatedHash.Compute(
            "library.inventory",
            Serialize(content));
        var confirmed = content with { InventoryId = identity, RunId = identity };
        Validate(confirmed, root, requireConfirmed: true);
        return confirmed;
    }

    public static void Validate(LibraryInventory inventory, string root, bool requireConfirmed)
    {
        if (inventory.SchemaVersion != SchemaVersion ||
            inventory.State is not ("draft" or "confirmed") ||
            requireConfirmed && inventory.State != "confirmed")
        {
            throw new DeterministicValidationException("Inventory schema or state is invalid.");
        }

        if (inventory.Packages.Count == 0)
        {
            throw new DeterministicValidationException("Inventory requires at least one exact package.");
        }

        if (inventory.State == "draft")
        {
            if (inventory.InventoryId is not null || inventory.RunId is not null)
            {
                throw new DeterministicValidationException("Draft inventory cannot contain an inventory or run ID.");
            }
        }
        else
        {
            var content = inventory with { InventoryId = null, RunId = null };
            var expected = "LIB1-" + DomainSeparatedHash.Compute(
                "library.inventory",
                Serialize(content));
            if (inventory.InventoryId != expected || inventory.RunId != expected)
            {
                throw new DeterministicValidationException(
                    "Confirmed inventory identity and run ID do not match its canonical confirmed content.");
            }
        }

        ValidateUniqueSorted(inventory.Packages, item => item.UnitId, "package units");
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var componentUnits = new HashSet<string>(StringComparer.Ordinal);
        foreach (var package in inventory.Packages)
        {
            var packageInput = LoadInput(root, package.InputManifestPath);
            var expectedPackage = PackageIdentity(packageInput);
            var expectedUnit = PackageUnitId(expectedPackage);
            var expectedRevisionRoot = PackageRevisionRoot(expectedPackage, packageInput.Acquisition);
            if (package.UnitId != expectedUnit ||
                package.Package != expectedPackage ||
                package.Acquisition != packageInput.Acquisition ||
                package.InputManifestDigest != InputManifestService.Digest(
                    ReadInputBytes(root, package.InputManifestPath)) ||
                package.RevisionRoot != expectedRevisionRoot)
            {
                throw new DeterministicValidationException(
                    $"Package inventory unit '{package.UnitId}' does not match its exact confirmed input manifest or canonical revision root.");
            }

            AddPath(paths, package.InputManifestPath, "inventory input manifest");
            AddPath(paths, package.RevisionRoot, "inventory revision root");
            _ = SafePath.ResolveUnderRoot(root, package.RevisionRoot, requireExisting: false);
            ValidateExclusions(package.Exclusions);
            if (package.Components.Count == 0)
            {
                throw new DeterministicValidationException(
                    $"Package inventory unit '{package.UnitId}' requires at least one confirmed component.");
            }

            ValidateUniqueSorted(package.Components, item => item.UnitId, "component units");
            foreach (var component in package.Components)
            {
                if (!componentUnits.Add(component.UnitId))
                {
                    throw new DeterministicValidationException(
                        $"Duplicate component ownership for unit '{component.UnitId}'.");
                }

                var componentBytes = ReadInputBytes(root, component.InputManifestPath);
                var componentInput = InputManifestService.Parse(componentBytes);
                InputManifestService.Validate(componentInput, root, requireConfirmed: true);
                var inputComponent = componentInput.Components.SingleOrDefault();
                var expectedComponentId = ComponentUnitId(package.UnitId, component.ComponentId);
                var expectedComponentRoot = ComponentRevisionRoot(
                    expectedPackage,
                    packageInput.Acquisition,
                    component.ComponentId);
                if (component.PackageUnitId != package.UnitId ||
                    component.UnitId != expectedComponentId ||
                    component.ComponentId != Canonicalization.ComponentId(component.ComponentId) ||
                    component.RevisionRoot != expectedComponentRoot ||
                    component.InputManifestDigest != InputManifestService.Digest(componentBytes) ||
                    PackageIdentity(componentInput) != expectedPackage ||
                    inputComponent is null ||
                    inputComponent.Id != component.ComponentId ||
                    inputComponent.DisplayName != component.DisplayName ||
                    !inputComponent.RenderModes.SequenceEqual(component.RenderModes, StringComparer.Ordinal) ||
                    !inputComponent.AllowedSourcePaths.SequenceEqual(
                        component.AllowedSourcePaths,
                        StringComparer.Ordinal))
                {
                    throw new DeterministicValidationException(
                        $"Component inventory unit '{component.UnitId}' has a missing component, skipped render mode, duplicate/cross-package identity, or incorrect input/revision binding.");
                }

                var canonicalModes = component.RenderModes
                    .Select(Canonicalization.RenderMode)
                    .Distinct(StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal)
                    .ToArray();
                if (canonicalModes.Length == 0 ||
                    !canonicalModes.SequenceEqual(component.RenderModes, StringComparer.Ordinal))
                {
                    throw new DeterministicValidationException(
                        $"Component inventory unit '{component.UnitId}' render modes must be nonempty, canonical, unique, and sorted.");
                }

                AddPath(paths, component.InputManifestPath, "inventory input manifest");
                AddPath(paths, component.RevisionRoot, "inventory revision root");
                _ = SafePath.ResolveUnderRoot(root, component.RevisionRoot, requireExisting: false);
                ValidateExclusions(component.Exclusions);
            }

            var packageComponents = packageInput.Components
                .Select(item => (
                    item.Id,
                    Modes: string.Join('\0', item.RenderModes),
                    Sources: string.Join('\0', item.AllowedSourcePaths)))
                .OrderBy(item => item.Id, StringComparer.Ordinal)
                .ToArray();
            var inventoryComponents = package.Components
                .Select(item => (
                    Id: item.ComponentId,
                    Modes: string.Join('\0', item.RenderModes),
                    Sources: string.Join('\0', item.AllowedSourcePaths)))
                .OrderBy(item => item.Id, StringComparer.Ordinal)
                .ToArray();
            if (!packageComponents.SequenceEqual(inventoryComponents))
            {
                throw new DeterministicValidationException(
                    $"Package inventory unit '{package.UnitId}' does not account for every confirmed component and claimed render mode in its package input manifest.");
            }
        }
    }

    public static byte[] Serialize(LibraryInventory inventory)
    {
        var bytes = StrictJson.SerializeCanonical(writer =>
        {
            writer.WriteStartObject();
            writer.WriteNumber("schema_version", inventory.SchemaVersion);
            writer.WriteString("state", inventory.State);
            WriteNullable(writer, "inventory_id", inventory.InventoryId);
            WriteNullable(writer, "run_id", inventory.RunId);
            writer.WritePropertyName("packages");
            writer.WriteStartArray();
            foreach (var package in inventory.Packages)
            {
                writer.WriteStartObject();
                writer.WriteString("unit_id", package.UnitId);
                WritePackageIdentity(writer, package.Package);
                writer.WriteString("acquisition", package.Acquisition);
                writer.WriteString("input_manifest_path", package.InputManifestPath);
                ContractJson.WriteDigest(writer, "input_manifest_sha256", package.InputManifestDigest);
                writer.WriteString("revision_root", package.RevisionRoot);
                writer.WritePropertyName("components");
                writer.WriteStartArray();
                foreach (var component in package.Components)
                {
                    writer.WriteStartObject();
                    writer.WriteString("unit_id", component.UnitId);
                    writer.WriteString("component_id", component.ComponentId);
                    writer.WriteString("display_name", component.DisplayName);
                    writer.WriteString("package_unit_id", component.PackageUnitId);
                    writer.WriteString("input_manifest_path", component.InputManifestPath);
                    ContractJson.WriteDigest(writer, "input_manifest_sha256", component.InputManifestDigest);
                    writer.WriteString("revision_root", component.RevisionRoot);
                    writer.WritePropertyName("render_modes");
                    WriteStrings(writer, component.RenderModes);
                    writer.WritePropertyName("allowed_source_paths");
                    WriteStrings(writer, component.AllowedSourcePaths);
                    writer.WritePropertyName("exclusions");
                    WriteExclusions(writer, component.Exclusions);
                    writer.WriteEndObject();
                }

                writer.WriteEndArray();
                writer.WritePropertyName("exclusions");
                WriteExclusions(writer, package.Exclusions);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        });
        BoundedIO.EnsureLength(bytes.Length, ResourceLimits.SerializedArtifactBytes, "inventory");
        return bytes;
    }

    public static LibraryInventory Parse(ReadOnlyMemory<byte> bytes)
    {
        using var document = StrictJson.Parse(bytes, ResourceLimits.SerializedArtifactBytes, "inventory");
        var root = document.RootElement;
        ContractJson.RequireProperties(
            root,
            "schema_version",
            "state",
            "inventory_id",
            "run_id",
            "packages");
        var inventory = new LibraryInventory(
            ContractJson.Int32(root, "schema_version"),
            ContractJson.String(root, "state"),
            ContractJson.NullableString(root, "inventory_id"),
            ContractJson.NullableString(root, "run_id"),
            ContractJson.Array(root, "packages").EnumerateArray().Select(ParsePackage).ToArray());
        ContractJson.RequireCanonical(bytes.Span, Serialize(inventory), "inventory");
        return inventory;
    }

    public static LibraryInventory ParseDraft(ReadOnlyMemory<byte> bytes, string root)
    {
        using var document = StrictJson.Parse(bytes, ResourceLimits.SerializedArtifactBytes, "draft inventory");
        var value = document.RootElement;
        ContractJson.RequirePropertiesUnordered(
            value,
            "schema_version",
            "state",
            "inventory_id",
            "run_id",
            "packages");
        var editable = new LibraryInventory(
            ContractJson.Int32(value, "schema_version"),
            ContractJson.String(value, "state").Trim().ToLowerInvariant(),
            ContractJson.NullableString(value, "inventory_id"),
            ContractJson.NullableString(value, "run_id"),
            ContractJson.Array(value, "packages").EnumerateArray().Select(ParseEditablePackage).ToArray());
        var packages = editable.Packages.Select(package =>
        {
            var packageBytes = ReadInputBytes(root, package.InputManifestPath);
            var packageInput = InputManifestService.Parse(packageBytes);
            InputManifestService.Validate(packageInput, root, requireConfirmed: true);
            var identity = PackageIdentity(packageInput);
            var packageUnitId = PackageUnitId(identity);
            var components = package.Components.Select(component =>
            {
                var componentBytes = ReadInputBytes(root, component.InputManifestPath);
                var componentInput = InputManifestService.Parse(componentBytes);
                InputManifestService.Validate(componentInput, root, requireConfirmed: true);
                var inputComponent = componentInput.Components.SingleOrDefault(item =>
                    item.Id == component.ComponentId);
                if (componentInput.Components.Count != 1 ||
                    inputComponent is null ||
                    PackageIdentity(componentInput) != identity)
                {
                    throw new DeterministicValidationException(
                        $"Component '{component.ComponentId}' requires its own confirmed input manifest for the exact package.");
                }

                return component with
                {
                    UnitId = ComponentUnitId(packageUnitId, component.ComponentId),
                    DisplayName = inputComponent.DisplayName,
                    PackageUnitId = packageUnitId,
                    InputManifestDigest = InputManifestService.Digest(componentBytes),
                    AllowedSourcePaths = inputComponent.AllowedSourcePaths
                };
            }).OrderBy(item => item.UnitId, StringComparer.Ordinal).ToArray();
            return package with
            {
                UnitId = packageUnitId,
                Package = identity,
                Acquisition = packageInput.Acquisition,
                InputManifestDigest = InputManifestService.Digest(packageBytes),
                Components = components
            };
        }).OrderBy(item => item.UnitId, StringComparer.Ordinal).ToArray();
        return editable with
        {
            Packages = packages
        };
    }

    public static Sha256Digest Digest(ReadOnlySpan<byte> bytes) => ContractJson.RawDigest(bytes);

    public static string PackageUnitId(EvidencePackageIdentity package) =>
        $"package:{package.PackageId}@{package.Version}#{package.NupkgDigest.Value}";

    public static string ComponentUnitId(string packageUnitId, string componentId) =>
        $"component:{packageUnitId}/{componentId}";

    private static InventoryPackage ParseCandidatePackage(string root, JsonElement value)
    {
        ContractJson.RequirePropertiesUnordered(
            value,
            "input_manifest_path",
            "revision_root",
            "components",
            "exclusions");
        var inputPath = Canonicalization.RelativePath(
            ContractJson.String(value, "input_manifest_path"),
            "package input_manifest_path");
        var inputBytes = ReadInputBytes(root, inputPath);
        var input = InputManifestService.Parse(inputBytes);
        InputManifestService.Validate(input, root, requireConfirmed: true);
        var package = PackageIdentity(input);
        var unitId = PackageUnitId(package);
        var components = ContractJson.Array(value, "components")
            .EnumerateArray()
            .Select(item => ParseCandidateComponent(root, unitId, package, item))
            .OrderBy(item => item.UnitId, StringComparer.Ordinal)
            .ToArray();
        return new InventoryPackage(
            unitId,
            package,
            input.Acquisition,
            inputPath,
            InputManifestService.Digest(inputBytes),
            Canonicalization.RelativePath(ContractJson.String(value, "revision_root"), "package revision_root"),
            components,
            ParseCandidateExclusions(value));
    }

    private static InventoryComponent ParseCandidateComponent(
        string root,
        string packageUnitId,
        EvidencePackageIdentity package,
        JsonElement value)
    {
        ContractJson.RequirePropertiesUnordered(
            value,
            "component_id",
            "input_manifest_path",
            "revision_root",
            "render_modes",
            "exclusions");
        var componentId = Canonicalization.ComponentId(ContractJson.String(value, "component_id"));
        var inputPath = Canonicalization.RelativePath(
            ContractJson.String(value, "input_manifest_path"),
            "component input_manifest_path");
        var inputBytes = ReadInputBytes(root, inputPath);
        var input = InputManifestService.Parse(inputBytes);
        InputManifestService.Validate(input, root, requireConfirmed: true);
        var inputComponent = input.Components.SingleOrDefault(item => item.Id == componentId);
        if (input.Components.Count != 1 || inputComponent is null || PackageIdentity(input) != package)
        {
            throw new DeterministicValidationException(
                $"Component '{componentId}' requires its own confirmed input manifest for the exact package.");
        }

        var modes = ContractJson.StringArray(value, "render_modes")
            .Select(Canonicalization.RenderMode)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        return new InventoryComponent(
            ComponentUnitId(packageUnitId, componentId),
            componentId,
            inputComponent.DisplayName,
            packageUnitId,
            inputPath,
            InputManifestService.Digest(inputBytes),
            Canonicalization.RelativePath(ContractJson.String(value, "revision_root"), "component revision_root"),
            modes,
            inputComponent.AllowedSourcePaths,
            ParseCandidateExclusions(value));
    }

    private static IReadOnlyList<InventoryExclusion> ParseCandidateExclusions(JsonElement value) =>
        ContractJson.Array(value, "exclusions")
            .EnumerateArray()
            .Select(item =>
            {
                ContractJson.RequirePropertiesUnordered(item, "subject", "rationale");
                return new InventoryExclusion(
                    ContractJson.NormalizeText(ContractJson.String(item, "subject"), "exclusion subject", 256),
                    ContractJson.NormalizeText(ContractJson.String(item, "rationale"), "exclusion rationale", 2048));
            })
            .OrderBy(item => item.Subject, StringComparer.Ordinal)
            .ThenBy(item => item.Rationale, StringComparer.Ordinal)
            .ToArray();

    private static InventoryPackage ParsePackage(JsonElement value)
    {
        ContractJson.RequireProperties(
            value,
            "unit_id",
            "package",
            "acquisition",
            "input_manifest_path",
            "input_manifest_sha256",
            "revision_root",
            "components",
            "exclusions");
        return new InventoryPackage(
            ContractJson.String(value, "unit_id"),
            ParsePackageIdentity(ContractJson.Object(value, "package")),
            ContractJson.String(value, "acquisition"),
            ContractJson.String(value, "input_manifest_path"),
            ContractJson.Digest(ContractJson.Object(value, "input_manifest_sha256")),
            ContractJson.String(value, "revision_root"),
            ContractJson.Array(value, "components").EnumerateArray().Select(ParseComponent).ToArray(),
            ParseExclusions(ContractJson.Array(value, "exclusions")));
    }

    private static InventoryPackage ParseEditablePackage(JsonElement value)
    {
        ContractJson.RequirePropertiesUnordered(
            value,
            "unit_id",
            "package",
            "acquisition",
            "input_manifest_path",
            "input_manifest_sha256",
            "revision_root",
            "components",
            "exclusions");
        var package = ParsePackageIdentityUnordered(ContractJson.Object(value, "package"));
        return new InventoryPackage(
            PackageUnitId(package),
            package,
            ContractJson.String(value, "acquisition").Trim().ToLowerInvariant(),
            Canonicalization.RelativePath(ContractJson.String(value, "input_manifest_path"), "package input_manifest_path"),
            ContractJson.Digest(ContractJson.Object(value, "input_manifest_sha256")),
            Canonicalization.RelativePath(ContractJson.String(value, "revision_root"), "package revision_root"),
            ContractJson.Array(value, "components").EnumerateArray()
                .Select(item => ParseEditableComponent(PackageUnitId(package), item))
                .OrderBy(item => item.UnitId, StringComparer.Ordinal)
                .ToArray(),
            ParseEditableExclusions(ContractJson.Array(value, "exclusions")));
    }

    private static InventoryComponent ParseComponent(JsonElement value)
    {
        ContractJson.RequireProperties(
            value,
            "unit_id",
            "component_id",
            "display_name",
            "package_unit_id",
            "input_manifest_path",
            "input_manifest_sha256",
            "revision_root",
            "render_modes",
            "allowed_source_paths",
            "exclusions");
        return new InventoryComponent(
            ContractJson.String(value, "unit_id"),
            ContractJson.String(value, "component_id"),
            ContractJson.String(value, "display_name"),
            ContractJson.String(value, "package_unit_id"),
            ContractJson.String(value, "input_manifest_path"),
            ContractJson.Digest(ContractJson.Object(value, "input_manifest_sha256")),
            ContractJson.String(value, "revision_root"),
            ContractJson.StringArray(value, "render_modes"),
            ContractJson.StringArray(value, "allowed_source_paths"),
            ParseExclusions(ContractJson.Array(value, "exclusions")));
    }

    private static InventoryComponent ParseEditableComponent(string packageUnitId, JsonElement value)
    {
        ContractJson.RequirePropertiesUnordered(
            value,
            "unit_id",
            "component_id",
            "display_name",
            "package_unit_id",
            "input_manifest_path",
            "input_manifest_sha256",
            "revision_root",
            "render_modes",
            "allowed_source_paths",
            "exclusions");
        var componentId = Canonicalization.ComponentId(ContractJson.String(value, "component_id"));
        return new InventoryComponent(
            ComponentUnitId(packageUnitId, componentId),
            componentId,
            ContractJson.NormalizeText(ContractJson.String(value, "display_name"), "component display name", 256),
            packageUnitId,
            Canonicalization.RelativePath(ContractJson.String(value, "input_manifest_path"), "component input_manifest_path"),
            ContractJson.Digest(ContractJson.Object(value, "input_manifest_sha256")),
            Canonicalization.RelativePath(ContractJson.String(value, "revision_root"), "component revision_root"),
            ContractJson.StringArray(value, "render_modes")
                .Select(Canonicalization.RenderMode)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray(),
            ContractJson.StringArray(value, "allowed_source_paths")
                .Select(path => Canonicalization.RelativePath(path, "component allowed source path"))
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray(),
            ParseEditableExclusions(ContractJson.Array(value, "exclusions")));
    }

    private static IReadOnlyList<InventoryExclusion> ParseExclusions(JsonElement value) =>
        value.EnumerateArray().Select(item =>
        {
            ContractJson.RequireProperties(item, "subject", "rationale");
            return new InventoryExclusion(
                ContractJson.String(item, "subject"),
                ContractJson.String(item, "rationale"));
        }).ToArray();

    private static IReadOnlyList<InventoryExclusion> ParseEditableExclusions(JsonElement value) =>
        value.EnumerateArray().Select(item =>
        {
            ContractJson.RequirePropertiesUnordered(item, "subject", "rationale");
            return new InventoryExclusion(
                ContractJson.NormalizeText(ContractJson.String(item, "subject"), "exclusion subject", 256),
                ContractJson.NormalizeText(ContractJson.String(item, "rationale"), "exclusion rationale", 2048));
        })
        .OrderBy(item => item.Subject, StringComparer.Ordinal)
        .ThenBy(item => item.Rationale, StringComparer.Ordinal)
        .ToArray();

    private static void ValidateExclusions(IReadOnlyList<InventoryExclusion> exclusions)
    {
        var canonical = exclusions
            .OrderBy(item => item.Subject, StringComparer.Ordinal)
            .ThenBy(item => item.Rationale, StringComparer.Ordinal)
            .ToArray();
        if (!canonical.SequenceEqual(exclusions))
        {
            throw new DeterministicValidationException("Inventory exclusions are not in canonical order.");
        }

        foreach (var exclusion in exclusions)
        {
            _ = ContractJson.NormalizeText(exclusion.Subject, "exclusion subject", 256);
            _ = ContractJson.NormalizeText(exclusion.Rationale, "exclusion rationale", 2048);
        }
    }

    private static InputManifest LoadInput(string root, string relativePath)
    {
        var bytes = ReadInputBytes(root, relativePath);
        var input = InputManifestService.Parse(bytes);
        InputManifestService.Validate(input, root, requireConfirmed: true);
        return input;
    }

    private static byte[] ReadInputBytes(string root, string relativePath) =>
        BoundedIO.ReadAllBytes(
            SafePath.ResolveUnderRoot(root, relativePath, requireExisting: true, requireFile: true),
            ResourceLimits.SerializedArtifactBytes,
            "inventory input manifest");

    private static EvidencePackageIdentity PackageIdentity(InputManifest input) =>
        new(input.Package.PackageId, input.Package.Version, input.Package.NupkgDigest);

    private static string PackageRevisionRoot(
        EvidencePackageIdentity package,
        string acquisition) =>
        acquisition == "release-candidate"
            ? $"packages/{package.PackageId}/{package.Version}/candidates/{package.NupkgDigest.Value}/revisions"
            : $"packages/{package.PackageId}/{package.Version}/revisions";

    private static string ComponentRevisionRoot(
        EvidencePackageIdentity package,
        string acquisition,
        string componentId) =>
        acquisition == "release-candidate"
            ? $"packages/{package.PackageId}/{package.Version}/candidates/{package.NupkgDigest.Value}/components/{componentId}/revisions"
            : $"packages/{package.PackageId}/{package.Version}/components/{componentId}/revisions";

    private static void ValidateUniqueSorted<T>(
        IReadOnlyList<T> values,
        Func<T, string> key,
        string resource)
    {
        var keys = values.Select(key).ToArray();
        if (keys.Distinct(StringComparer.Ordinal).Count() != keys.Length ||
            !keys.SequenceEqual(keys.Order(StringComparer.Ordinal), StringComparer.Ordinal))
        {
            throw new DeterministicValidationException(
                $"{resource} must be unique and canonically sorted.");
        }
    }

    private static void AddPath(HashSet<string> paths, string path, string resource)
    {
        if (Canonicalization.RelativePath(path, resource) != path || !paths.Add(path))
        {
            throw new DeterministicValidationException(
                "Inventory contains duplicate or noncanonical manifest/revision paths.");
        }
    }

    private static void WritePackageIdentity(Utf8JsonWriter writer, EvidencePackageIdentity package)
    {
        writer.WritePropertyName("package");
        writer.WriteStartObject();
        writer.WriteString("package_id", package.PackageId);
        writer.WriteString("version", package.Version);
        ContractJson.WriteDigest(writer, "nupkg_sha256", package.NupkgDigest);
        writer.WriteEndObject();
    }

    private static EvidencePackageIdentity ParsePackageIdentity(JsonElement value)
    {
        ContractJson.RequireProperties(value, "package_id", "version", "nupkg_sha256");
        return new EvidencePackageIdentity(
            ContractJson.String(value, "package_id"),
            ContractJson.String(value, "version"),
            ContractJson.Digest(ContractJson.Object(value, "nupkg_sha256")));
    }

    private static EvidencePackageIdentity ParsePackageIdentityUnordered(JsonElement value)
    {
        ContractJson.RequirePropertiesUnordered(value, "package_id", "version", "nupkg_sha256");
        return new EvidencePackageIdentity(
            Canonicalization.PackageId(ContractJson.String(value, "package_id")),
            NuGetVersionNormalizer.Normalize(ContractJson.String(value, "version")),
            ContractJson.Digest(ContractJson.Object(value, "nupkg_sha256")));
    }

    private static void WriteExclusions(Utf8JsonWriter writer, IEnumerable<InventoryExclusion> exclusions)
    {
        writer.WriteStartArray();
        foreach (var exclusion in exclusions)
        {
            writer.WriteStartObject();
            writer.WriteString("subject", exclusion.Subject);
            writer.WriteString("rationale", exclusion.Rationale);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteStrings(Utf8JsonWriter writer, IEnumerable<string> values)
    {
        writer.WriteStartArray();
        foreach (var value in values)
        {
            writer.WriteStringValue(value);
        }

        writer.WriteEndArray();
    }

    private static void WriteNullable(Utf8JsonWriter writer, string name, string? value)
    {
        if (value is null)
        {
            writer.WriteNull(name);
        }
        else
        {
            writer.WriteString(name, value);
        }
    }
}
