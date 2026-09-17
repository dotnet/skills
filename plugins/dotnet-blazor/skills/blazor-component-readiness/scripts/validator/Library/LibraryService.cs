using System.Text;
using System.Text.Json;
using BlazorComponentReadiness.Validator.Assessment;
using BlazorComponentReadiness.Validator.Contracts;
using BlazorComponentReadiness.Validator.Evidence;
using BlazorComponentReadiness.Validator.Inventory;
using BlazorComponentReadiness.Validator.IO;
using BlazorComponentReadiness.Validator.Rendering;
using BlazorComponentReadiness.Validator.Validation;

namespace BlazorComponentReadiness.Validator.Library;

public sealed record LibraryIndexArtifacts(
    string GenerationId,
    byte[] Json,
    byte[] Markdown);

public static class LibraryService
{
    public const int SchemaVersion = 1;
    public static readonly IReadOnlyList<string> UnitStates =
        ["pending", "active", "completed", "blocked", "incomplete"];

    public static LibraryRunManifest Reconcile(
        string root,
        LibraryInventory inventory,
        ReadOnlySpan<byte> inventoryBytes,
        LibraryRunManifest? previous = null,
        IReadOnlyDictionary<string, LibraryStateReceiptSnapshot>? stateReceipts = null)
    {
        InventoryService.Validate(inventory, root, requireConfirmed: true);
        stateReceipts ??= LibraryStateReceiptService.Load(root, inventory, inventoryBytes);
        if (previous is not null)
        {
            ValidateShape(previous, inventory, inventoryBytes);
        }

        var previousUnits = previous?.Units.ToDictionary(item => item.UnitId, StringComparer.Ordinal)
            ?? new Dictionary<string, LibraryUnit>(StringComparer.Ordinal);
        var packageBindings = new Dictionary<string, (string UnitId, PackageRevisionBinding Binding)>(
            StringComparer.Ordinal);
        var manifestOwners = new Dictionary<string, string>(StringComparer.Ordinal);
        var identityOwners = new Dictionary<string, string>(StringComparer.Ordinal);
        var units = new List<LibraryUnit>();

        foreach (var package in inventory.Packages)
        {
            previousUnits.TryGetValue(package.UnitId, out var previousUnit);
            var scan = ScanPackage(
                root,
                package,
                packageBindings,
                manifestOwners,
                identityOwners);
            stateReceipts.TryGetValue(package.UnitId, out var stateReceipt);
            units.Add(CreateUnit(package, scan, previousUnit, stateReceipt));
        }

        foreach (var package in inventory.Packages)
        {
            foreach (var component in package.Components)
            {
                previousUnits.TryGetValue(component.UnitId, out var previousUnit);
                var scan = ScanComponent(
                    root,
                    package,
                    component,
                    packageBindings,
                    manifestOwners,
                    identityOwners);
                stateReceipts.TryGetValue(component.UnitId, out var stateReceipt);
                units.Add(CreateUnit(component, scan, previousUnit, stateReceipt));
            }
        }

        units.Sort((left, right) => StringComparer.Ordinal.Compare(left.UnitId, right.UnitId));
        var interrupted =
            previous?.Interrupted == true && units.Any(unit => unit.State != "completed") ||
            stateReceipts.Values.Any(receipt => receipt.InterruptedTransition) &&
            units.Any(unit => unit.State != "completed") ||
            units.Any(unit =>
                previousUnits.TryGetValue(unit.UnitId, out var before) &&
                before.State == "active" &&
                unit.State == "incomplete");
        var manifest = new LibraryRunManifest(
            SchemaVersion,
            inventory.RunId!,
            InventoryService.Digest(inventoryBytes),
            units.All(unit => unit.State == "completed") ? "complete" : "incomplete",
            interrupted,
            units);
        ValidateShape(manifest, inventory, inventoryBytes);
        return manifest;
    }

    public static LibraryRunManifest Validate(
        string root,
        LibraryInventory inventory,
        ReadOnlySpan<byte> inventoryBytes,
        LibraryRunManifest manifest)
    {
        ValidateShape(manifest, inventory, inventoryBytes);
        var reconstructed = Reconcile(root, inventory, inventoryBytes, manifest);
        if (!Serialize(manifest).AsSpan().SequenceEqual(Serialize(reconstructed)))
        {
            throw new DeterministicValidationException(
                "Run manifest is stale, interrupted, or differs from immutable confirmed inventory and validation manifests; run library reconcile.");
        }

        if (manifest.State == "complete" &&
            manifest.Units.Any(unit => unit.State is "pending" or "active" or "blocked" or "incomplete"))
        {
            throw new DeterministicValidationException(
                "A library cannot claim complete while any inventory unit is missing, active, blocked, or incomplete.");
        }

        return reconstructed;
    }

    public static void ValidateShape(
        LibraryRunManifest manifest,
        LibraryInventory inventory,
        ReadOnlySpan<byte> inventoryBytes)
    {
        if (manifest.SchemaVersion != SchemaVersion ||
            manifest.RunId != inventory.RunId ||
            manifest.InventoryDigest != InventoryService.Digest(inventoryBytes) ||
            manifest.State is not ("complete" or "incomplete"))
        {
            throw new DeterministicValidationException(
                "Run manifest schema, run identity, inventory digest, or state is invalid.");
        }

        var expected = inventory.Packages
            .Select(package => (
                package.UnitId,
                Kind: "package",
                PackageUnitId: (string?)null,
                ComponentId: (string?)null,
                Modes: (IReadOnlyList<string>)Array.Empty<string>(),
                package.InputManifestPath,
                package.InputManifestDigest,
                package.RevisionRoot))
            .Concat(inventory.Packages.SelectMany(package => package.Components.Select(component => (
                component.UnitId,
                Kind: "component",
                PackageUnitId: (string?)package.UnitId,
                ComponentId: (string?)component.ComponentId,
                Modes: component.RenderModes,
                component.InputManifestPath,
                component.InputManifestDigest,
                component.RevisionRoot))))
            .OrderBy(item => item.UnitId, StringComparer.Ordinal)
            .ToArray();
        if (manifest.Units.Count != expected.Length ||
            !manifest.Units.Select(item => item.UnitId)
                .SequenceEqual(expected.Select(item => item.UnitId), StringComparer.Ordinal))
        {
            throw new DeterministicValidationException(
                "Run manifest must account for every inventory package/component unit exactly once.");
        }

        for (var index = 0; index < expected.Length; index++)
        {
            var unit = manifest.Units[index];
            var contract = expected[index];
            if (!UnitStates.Contains(unit.State, StringComparer.Ordinal) ||
                unit.Kind != contract.Kind ||
                unit.PackageUnitId != contract.PackageUnitId ||
                unit.ComponentId != contract.ComponentId ||
                !unit.RenderModes.SequenceEqual(contract.Modes, StringComparer.Ordinal) ||
                unit.InputManifestPath != contract.InputManifestPath ||
                unit.InputManifestDigest != contract.InputManifestDigest ||
                unit.RevisionRoot != contract.RevisionRoot)
            {
                throw new DeterministicValidationException(
                    $"Run manifest unit '{unit.UnitId}' has cross-package/component identity, path, mode, or state drift.");
            }

            ValidateUnitState(unit);
        }

        var complete = manifest.Units.All(unit => unit.State == "completed");
        if ((manifest.State == "complete") != complete)
        {
            throw new DeterministicValidationException(
                "Run manifest overall state must be complete exactly when every unit is completed.");
        }
    }

    public static byte[] Serialize(LibraryRunManifest manifest)
    {
        var bytes = StrictJson.SerializeCanonical(writer =>
        {
            writer.WriteStartObject();
            writer.WriteNumber("schema_version", manifest.SchemaVersion);
            writer.WriteString("run_id", manifest.RunId);
            ContractJson.WriteDigest(writer, "inventory_sha256", manifest.InventoryDigest);
            writer.WriteString("state", manifest.State);
            writer.WriteBoolean("interrupted", manifest.Interrupted);
            writer.WritePropertyName("units");
            writer.WriteStartArray();
            foreach (var unit in manifest.Units)
            {
                writer.WriteStartObject();
                writer.WriteString("unit_id", unit.UnitId);
                writer.WriteString("kind", unit.Kind);
                writer.WriteString("state", unit.State);
                WriteNullable(writer, "package_unit_id", unit.PackageUnitId);
                WriteNullable(writer, "component_id", unit.ComponentId);
                writer.WritePropertyName("render_modes");
                WriteStrings(writer, unit.RenderModes);
                writer.WriteString("input_manifest_path", unit.InputManifestPath);
                ContractJson.WriteDigest(writer, "input_manifest_sha256", unit.InputManifestDigest);
                writer.WriteString("revision_root", unit.RevisionRoot);
                WriteNullable(writer, "output_revision", unit.OutputRevision);
                WriteNullable(writer, "report_path", unit.ReportPath);
                WriteNullable(writer, "validation_manifest_path", unit.ValidationManifestPath);
                WriteNullableDigest(writer, "validation_manifest_sha256", unit.ValidationManifestDigest);
                WriteNullableDigest(
                    writer,
                    "package_validation_manifest_sha256",
                    unit.PackageValidationManifestDigest);
                WriteNullable(writer, "state_receipt_path", unit.StateReceiptPath);
                WriteNullableDigest(writer, "state_receipt_sha256", unit.StateReceiptDigest);
                if (unit.StateReceiptSequence is null)
                {
                    writer.WriteNull("state_receipt_sequence");
                }
                else
                {
                    writer.WriteNumber("state_receipt_sequence", unit.StateReceiptSequence.Value);
                }

                WriteNullable(writer, "transitioned_at_utc", unit.TransitionedAtUtc);
                WriteNullable(writer, "transition_reason", unit.TransitionReason);
                writer.WritePropertyName("status_counts");
                writer.WriteStartArray();
                foreach (var count in unit.StatusCounts)
                {
                    writer.WriteStartObject();
                    writer.WriteString("status", count.Status);
                    writer.WriteNumber("count", count.Count);
                    writer.WriteEndObject();
                }

                writer.WriteEndArray();
                writer.WritePropertyName("missing_inputs");
                WriteStrings(writer, unit.MissingInputs);
                writer.WritePropertyName("blocked_probes");
                WriteStrings(writer, unit.BlockedProbes);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        });
        BoundedIO.EnsureLength(bytes.Length, ResourceLimits.SerializedArtifactBytes, "run manifest");
        return bytes;
    }

    public static LibraryRunManifest Parse(ReadOnlyMemory<byte> bytes)
    {
        using var document = StrictJson.Parse(bytes, ResourceLimits.SerializedArtifactBytes, "run manifest");
        var root = document.RootElement;
        ContractJson.RequireProperties(
            root,
            "schema_version",
            "run_id",
            "inventory_sha256",
            "state",
            "interrupted",
            "units");
        if (!root.GetProperty("interrupted").ValueKind.Equals(JsonValueKind.True) &&
            !root.GetProperty("interrupted").ValueKind.Equals(JsonValueKind.False))
        {
            throw new DeterministicValidationException("Run manifest interrupted must be a boolean.");
        }

        var manifest = new LibraryRunManifest(
            ContractJson.Int32(root, "schema_version"),
            ContractJson.String(root, "run_id"),
            ContractJson.Digest(ContractJson.Object(root, "inventory_sha256")),
            ContractJson.String(root, "state"),
            root.GetProperty("interrupted").GetBoolean(),
            ContractJson.Array(root, "units").EnumerateArray().Select(ParseUnit).ToArray());
        ContractJson.RequireCanonical(bytes.Span, Serialize(manifest), "run manifest");
        return manifest;
    }

    public static LibraryStatus GetStatus(LibraryRunManifest manifest) =>
        new(
            manifest.State,
            manifest.Units.Count,
            manifest.Units.Count(unit => unit.State == "pending"),
            manifest.Units.Count(unit => unit.State == "active"),
            manifest.Units.Count(unit => unit.State == "completed"),
            manifest.Units.Count(unit => unit.State == "blocked"),
            manifest.Units.Count(unit => unit.State == "incomplete"),
            null);

    public static byte[] SerializeStatus(LibraryStatus status) =>
        StrictJson.SerializeCanonical(writer =>
        {
            writer.WriteStartObject();
            writer.WriteString("state", status.State);
            writer.WriteNumber("total", status.Total);
            writer.WriteNumber("pending", status.Pending);
            writer.WriteNumber("active", status.Active);
            writer.WriteNumber("completed", status.Completed);
            writer.WriteNumber("blocked", status.Blocked);
            writer.WriteNumber("incomplete", status.Incomplete);
            WriteNullable(writer, "error", status.Error);
            writer.WriteEndObject();
        });

    public static LibraryIndexArtifacts CreateIndex(
        LibraryInventory inventory,
        ReadOnlySpan<byte> inventoryBytes,
        LibraryRunManifest manifest,
        ReadOnlySpan<byte> manifestBytes)
    {
        var status = GetStatus(manifest);
        var inventoryDigest = InventoryService.Digest(inventoryBytes);
        var manifestDigest = ContractJson.RawDigest(manifestBytes);
        var generationId = "GEN1-" + DomainSeparatedHash.Compute(
            "library.index-generation",
            Encoding.UTF8.GetBytes($"{inventoryDigest.Value}\0{manifestDigest.Value}"));
        var assessmentCounts = RubricLoader.Load().Statuses
            .Concat(["incomplete"])
            .Select(value => new LibraryStatusCount(
                value,
                manifest.Units.SelectMany(unit => unit.StatusCounts)
                    .Where(count => count.Status == value)
                    .Sum(count => count.Count)))
            .ToArray();
        var json = StrictJson.SerializeCanonical(writer =>
        {
            writer.WriteStartObject();
            writer.WriteNumber("schema_version", 1);
            writer.WriteString("run_id", manifest.RunId);
            writer.WriteString("generation_id", generationId);
            ContractJson.WriteDigest(writer, "inventory_sha256", inventoryDigest);
            ContractJson.WriteDigest(writer, "run_manifest_sha256", manifestDigest);
            writer.WriteString("state", manifest.State);
            writer.WritePropertyName("counts");
            writer.WriteStartObject();
            writer.WriteNumber("total", status.Total);
            writer.WriteNumber("completed", status.Completed);
            writer.WriteNumber("pending", status.Pending);
            writer.WriteNumber("blocked", status.Blocked);
            writer.WriteNumber("incomplete", status.Incomplete + status.Active);
            writer.WriteEndObject();
            writer.WritePropertyName("assessment_status_counts");
            WriteStatusCounts(writer, assessmentCounts);
            writer.WritePropertyName("packages");
            writer.WriteStartArray();
            foreach (var package in inventory.Packages)
            {
                var packageUnit = manifest.Units.Single(unit => unit.UnitId == package.UnitId);
                writer.WriteStartObject();
                writer.WriteString("unit_id", package.UnitId);
                writer.WriteString("package_id", package.Package.PackageId);
                writer.WriteString("version", package.Package.Version);
                writer.WriteString("state", packageUnit.State);
                WriteNullable(writer, "report", packageUnit.ReportPath);
                writer.WritePropertyName("status_counts");
                WriteStatusCounts(writer, packageUnit.StatusCounts);
                writer.WritePropertyName("missing_inputs");
                WriteStrings(writer, packageUnit.MissingInputs);
                writer.WritePropertyName("blocked_probes");
                WriteStrings(writer, packageUnit.BlockedProbes);
                writer.WritePropertyName("components");
                writer.WriteStartArray();
                foreach (var component in package.Components)
                {
                    var unit = manifest.Units.Single(item => item.UnitId == component.UnitId);
                    writer.WriteStartObject();
                    writer.WriteString("unit_id", component.UnitId);
                    writer.WriteString("component_id", component.ComponentId);
                    writer.WriteString("state", unit.State);
                    writer.WritePropertyName("render_modes");
                    WriteStrings(writer, component.RenderModes);
                    WriteNullable(writer, "report", unit.ReportPath);
                    writer.WritePropertyName("status_counts");
                    WriteStatusCounts(writer, unit.StatusCounts);
                    writer.WritePropertyName("missing_inputs");
                    WriteStrings(writer, unit.MissingInputs);
                    writer.WritePropertyName("blocked_probes");
                    WriteStrings(writer, unit.BlockedProbes);
                    writer.WriteEndObject();
                }

                writer.WriteEndArray();
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        });

        var lines = new List<string>
        {
            "# Blazor component readiness library index",
            "",
            $"**Run:** `{manifest.RunId}`",
            $"**Generation:** `{generationId}`",
            $"**Inventory SHA-256:** `{inventoryDigest.Value}`",
            $"**Run manifest SHA-256:** `{manifestDigest.Value}`",
            $"**Overall state:** `{manifest.State}`",
            "",
            "## Factual status",
            "",
            $" - Total work units: {status.Total}",
            $" - Completed: {status.Completed}",
            $" - Pending: {status.Pending}",
            $" - Blocked: {status.Blocked}",
            $" - Incomplete: {status.Incomplete + status.Active}",
            " - Assessment row status counts: " +
            string.Join(", ", assessmentCounts.Select(count => $"`{count.Status}` {count.Count}")),
            ""
        };
        foreach (var package in inventory.Packages)
        {
            var packageUnit = manifest.Units.Single(unit => unit.UnitId == package.UnitId);
            lines.Add($"## `{package.Package.PackageId}` `{package.Package.Version}`");
            lines.Add("");
            lines.Add($" - Unit: `{package.UnitId}`");
            lines.Add($" - State: `{packageUnit.State}`");
            lines.Add($" - Report: {Link(packageUnit.ReportPath)}");
            AddCounts(lines, packageUnit.StatusCounts);
            AddList(lines, "Missing inputs", packageUnit.MissingInputs);
            AddList(lines, "Blocked probes", packageUnit.BlockedProbes);
            lines.Add("");
            lines.Add("| Component | State | Claimed render modes | Report |");
            lines.Add("|---|---|---|---|");
            foreach (var component in package.Components)
            {
                var unit = manifest.Units.Single(item => item.UnitId == component.UnitId);
                lines.Add(
                    $"| `{component.ComponentId}` | `{unit.State}` | " +
                    $"{string.Join(", ", component.RenderModes.Select(mode => $"`{mode}`"))} | " +
                    $"{Link(unit.ReportPath)} |");
            }

            lines.Add("");
        }

        var markdown = Encoding.UTF8.GetBytes(string.Join('\n', lines));
        BoundedIO.EnsureLength(json.Length, ResourceLimits.SerializedArtifactBytes, "library index JSON");
        BoundedIO.EnsureLength(markdown.Length, ResourceLimits.SerializedArtifactBytes, "library index Markdown");
        return new LibraryIndexArtifacts(generationId, json, markdown);
    }

    private static UnitScan ScanPackage(
        string root,
        InventoryPackage package,
        Dictionary<string, (string UnitId, PackageRevisionBinding Binding)> bindings,
        Dictionary<string, string> manifestOwners,
        Dictionary<string, string> identityOwners)
    {
        UnitScan? latest = null;
        foreach (var revision in RevisionDirectories(root, package.RevisionRoot))
        {
            var artifacts = RevisionService.VerifyRevision(
                root,
                revision,
                feedbackBytes: null,
                packageBinding: null,
                validateChain: true,
                allowMissingFeedback: true);
            ValidateArtifacts(package, artifacts);
            RegisterIdentity(package.UnitId, artifacts, manifestOwners, identityOwners);
            var scan = FromArtifacts(root, artifacts, packageBindingDigest: null);
            if (artifacts.Manifest.CompletionState == "complete")
            {
                var binding = RevisionService.LoadPackageBinding(root, revision, feedbackBytes: null);
                var digest = ContractJson.RawDigest(artifacts.ManifestBytes).Value;
                if (!bindings.TryAdd(digest, (package.UnitId, binding)))
                {
                    throw new DeterministicValidationException(
                        "Conflicting duplicate package validation manifests were found.");
                }
            }

            latest = scan;
        }

        return latest ?? UnitScan.Missing;
    }

    private static UnitScan ScanComponent(
        string root,
        InventoryPackage package,
        InventoryComponent component,
        IReadOnlyDictionary<string, (string UnitId, PackageRevisionBinding Binding)> bindings,
        Dictionary<string, string> manifestOwners,
        Dictionary<string, string> identityOwners)
    {
        UnitScan? latest = null;
        foreach (var revision in RevisionDirectories(root, component.RevisionRoot))
        {
            var manifestPath = Directory.GetFiles(revision, "*.validation.json", SearchOption.TopDirectoryOnly);
            if (manifestPath.Length != 1)
            {
                throw new DeterministicValidationException(
                    "Component revision requires exactly one validation manifest.");
            }

            var safeManifestPath = SafePath.ResolveUnderRoot(
                revision,
                Path.GetFileName(manifestPath[0]),
                requireExisting: true,
                requireFile: true);
            var manifestBytes = BoundedIO.ReadAllBytes(
                safeManifestPath,
                ResourceLimits.SerializedArtifactBytes,
                "component validation manifest");
            var manifest = ReportService.ParseManifest(manifestBytes);
            var packageDigest = manifest.PackageReference?.ValidationDigest.Value
                ?? throw new DeterministicValidationException(
                    "Component validation manifest requires an exact package validation reference.");
            if (!bindings.TryGetValue(packageDigest, out var packageBinding) ||
                packageBinding.UnitId != package.UnitId)
            {
                throw new DeterministicValidationException(
                    $"Component unit '{component.UnitId}' references the wrong package unit or package revision.");
            }

            var artifacts = RevisionService.VerifyRevision(
                root,
                revision,
                feedbackBytes: null,
                packageBinding.Binding,
                validateChain: true,
                allowMissingFeedback: true);
            ValidateArtifacts(root, package, component, artifacts);
            RegisterIdentity(component.UnitId, artifacts, manifestOwners, identityOwners);
            latest = FromArtifacts(root, artifacts, manifest.PackageReference.ValidationDigest);
        }

        return latest ?? UnitScan.Missing;
    }

    private static void ValidateArtifacts(InventoryPackage package, RevisionArtifacts artifacts)
    {
        if (artifacts.Kind != "package" ||
            artifacts.Assessment.Identity.Package != package.Package ||
            artifacts.Assessment.Identity.InputManifestDigest != package.InputManifestDigest ||
            artifacts.Input.Components.Select(item => item.Id)
                .Order(StringComparer.Ordinal)
                .SequenceEqual(package.Components.Select(item => item.ComponentId), StringComparer.Ordinal) == false)
        {
            throw new DeterministicValidationException(
                $"Package revision under '{package.RevisionRoot}' does not belong to its declared inventory unit.");
        }
    }

    private static void ValidateArtifacts(
        string root,
        InventoryPackage package,
        InventoryComponent component,
        RevisionArtifacts artifacts)
    {
        var inputComponent = artifacts.Input.Components.SingleOrDefault();
        if (artifacts.Kind != "component" ||
            artifacts.Assessment.Identity.Package != package.Package ||
            artifacts.Assessment.Identity.ComponentId != component.ComponentId ||
            artifacts.Assessment.Identity.InputManifestDigest != component.InputManifestDigest ||
            inputComponent is null ||
            inputComponent.Id != component.ComponentId ||
            !inputComponent.RenderModes.SequenceEqual(component.RenderModes, StringComparer.Ordinal))
        {
            throw new DeterministicValidationException(
                $"Component revision under '{component.RevisionRoot}' has cross-package/component identity, sibling leakage, or skipped render modes.");
        }

        ValidateComponentEvidenceIsolation(root, component, artifacts);
    }

    private static void ValidateComponentEvidenceIsolation(
        string root,
        InventoryComponent component,
        RevisionArtifacts artifacts)
    {
        _ = component;
        EvidenceInputBindingValidator.Validate(
            root,
            artifacts.Assessment,
            artifacts.Input,
            artifacts.Evidence);
    }

    private static void RegisterIdentity(
        string unitId,
        RevisionArtifacts artifacts,
        Dictionary<string, string> manifestOwners,
        Dictionary<string, string> identityOwners)
    {
        var manifestDigest = ContractJson.RawDigest(artifacts.ManifestBytes).Value;
        if (manifestOwners.TryGetValue(manifestDigest, out var manifestOwner) &&
            manifestOwner != unitId)
        {
            throw new DeterministicValidationException(
                "A validation manifest is duplicated across conflicting library units.");
        }

        manifestOwners[manifestDigest] = unitId;
        var identity = $"{artifacts.Kind}\0{artifacts.Assessment.Identity.Package.PackageId}\0" +
            $"{artifacts.Assessment.Identity.Package.Version}\0{artifacts.Assessment.Identity.Package.NupkgDigest.Value}\0" +
            $"{artifacts.Assessment.Identity.InputManifestDigest.Value}\0{artifacts.Assessment.Identity.ComponentId}";
        if (identityOwners.TryGetValue(identity, out var identityOwner) && identityOwner != unitId)
        {
            throw new DeterministicValidationException(
                "Conflicting duplicate assessment identities were found in declared library revision roots.");
        }

        identityOwners[identity] = unitId;
    }

    private static UnitScan FromArtifacts(
        string root,
        RevisionArtifacts artifacts,
        Sha256Digest? packageBindingDigest)
    {
        if (artifacts.Manifest.CompletionState != "complete")
        {
            return new UnitScan("incomplete", null, null, null, null, null, [], [], []);
        }

        var kind = artifacts.Kind;
        var revision = Relative(root, artifacts.Directory);
        return new UnitScan(
            "completed",
            revision,
            $"{revision}/{kind}.report.md",
            $"{revision}/{kind}.validation.json",
            ContractJson.RawDigest(artifacts.ManifestBytes),
            packageBindingDigest,
            StatusCounts(artifacts.Assessment),
            [],
            []);
    }

    private static LibraryUnit CreateUnit(
        InventoryPackage package,
        UnitScan scan,
        LibraryUnit? previous,
        LibraryStateReceiptSnapshot? stateReceipt)
    {
        var state = State(scan, previous, stateReceipt);
        var receipt = state == "completed" ? null : stateReceipt;
        return new LibraryUnit(
            package.UnitId,
            "package",
            state,
            null,
            null,
            [],
            package.InputManifestPath,
            package.InputManifestDigest,
            package.RevisionRoot,
            scan.OutputRevision,
            scan.ReportPath,
            scan.ValidationManifestPath,
            scan.ValidationManifestDigest,
            null,
            receipt?.Path,
            receipt?.Digest,
            receipt?.Receipt.Sequence,
            receipt?.Receipt.TransitionedAtUtc,
            receipt?.Receipt.TransitionReason,
            scan.StatusCounts,
            state == "completed"
                ? []
                : CanonicalMessages(receipt?.Receipt.MissingInputs ?? previous?.MissingInputs),
            state == "blocked"
                ? CanonicalMessages(receipt?.Receipt.BlockedProbes ?? previous?.BlockedProbes)
                : []);
    }

    private static LibraryUnit CreateUnit(
        InventoryComponent component,
        UnitScan scan,
        LibraryUnit? previous,
        LibraryStateReceiptSnapshot? stateReceipt)
    {
        var state = State(scan, previous, stateReceipt);
        var receipt = state == "completed" ? null : stateReceipt;
        return new LibraryUnit(
            component.UnitId,
            "component",
            state,
            component.PackageUnitId,
            component.ComponentId,
            component.RenderModes,
            component.InputManifestPath,
            component.InputManifestDigest,
            component.RevisionRoot,
            scan.OutputRevision,
            scan.ReportPath,
            scan.ValidationManifestPath,
            scan.ValidationManifestDigest,
            scan.PackageValidationManifestDigest,
            receipt?.Path,
            receipt?.Digest,
            receipt?.Receipt.Sequence,
            receipt?.Receipt.TransitionedAtUtc,
            receipt?.Receipt.TransitionReason,
            scan.StatusCounts,
            state == "completed"
                ? []
                : CanonicalMessages(receipt?.Receipt.MissingInputs ?? previous?.MissingInputs),
            state == "blocked"
                ? CanonicalMessages(receipt?.Receipt.BlockedProbes ?? previous?.BlockedProbes)
                : []);
    }

    private static string State(
        UnitScan scan,
        LibraryUnit? previous,
        LibraryStateReceiptSnapshot? stateReceipt)
    {
        if (scan.State == "completed")
        {
            return "completed";
        }

        if (scan.State == "incomplete")
        {
            return "incomplete";
        }

        if (stateReceipt is not null)
        {
            return stateReceipt.Receipt.State;
        }

        return previous?.State switch
        {
            "active" => "incomplete",
            "blocked" => "blocked",
            "incomplete" => "incomplete",
            _ => "pending"
        };
    }

    private static IReadOnlyList<string> CanonicalMessages(IReadOnlyList<string>? values) =>
        (values ?? [])
        .Select(value => ContractJson.NormalizeText(value, "run state detail", 2048))
        .Distinct(StringComparer.Ordinal)
        .Order(StringComparer.Ordinal)
        .ToArray();

    private static IReadOnlyList<string> RevisionDirectories(string root, string relativeRoot)
    {
        var path = SafePath.ResolveUnderRoot(root, relativeRoot, requireExisting: false);
        if (!Directory.Exists(path))
        {
            return [];
        }

        var files = Directory.GetFiles(path, "*", SearchOption.TopDirectoryOnly);
        foreach (var file in files)
        {
            _ = SafePath.ResolveUnderRoot(
                path,
                Path.GetFileName(file),
                requireExisting: true,
                requireFile: true);
        }

        if (files.Length != 0)
        {
            throw new DeterministicValidationException(
                $"Revision root '{relativeRoot}' contains undeclared files.");
        }

        var directories = Directory.GetDirectories(path, "*", SearchOption.TopDirectoryOnly)
            .Select(directory => SafePath.ResolveExistingDirectoryUnderRoot(path, directory))
            .OrderBy(item => Path.GetFileName(item), StringComparer.Ordinal)
            .ToArray();
        for (var index = 0; index < directories.Length; index++)
        {
            if (Path.GetFileName(directories[index]) != (index + 1).ToString("D4"))
            {
                throw new DeterministicValidationException(
                    $"Revision root '{relativeRoot}' contains a conflicting, duplicated, or skipped revision directory.");
            }
        }

        return directories;
    }

    private static IReadOnlyList<LibraryStatusCount> StatusCounts(ReadinessAssessment assessment)
    {
        var statuses = RubricLoader.Load().Statuses
            .Select(status => new LibraryStatusCount(
                status,
                assessment.Rows.Count(row => row.Status == status)))
            .ToList();
        statuses.Add(new LibraryStatusCount(
            "incomplete",
            assessment.Rows.Count(row => row.Status is null)));
        return statuses;
    }

    private static void ValidateUnitState(LibraryUnit unit)
    {
        var canonicalMissing = CanonicalMessages(unit.MissingInputs);
        var canonicalBlocked = CanonicalMessages(unit.BlockedProbes);
        if (!canonicalMissing.SequenceEqual(unit.MissingInputs, StringComparer.Ordinal) ||
            !canonicalBlocked.SequenceEqual(unit.BlockedProbes, StringComparer.Ordinal))
        {
            throw new DeterministicValidationException(
                $"Run manifest unit '{unit.UnitId}' state details are not canonical.");
        }

        if (unit.State == "completed")
        {
            if (unit.OutputRevision is null ||
                unit.ReportPath is null ||
                unit.ValidationManifestPath is null ||
                unit.ValidationManifestDigest is null ||
                unit.Kind == "component" && unit.PackageValidationManifestDigest is null ||
                unit.Kind == "package" && unit.PackageValidationManifestDigest is not null ||
                unit.StateReceiptPath is not null ||
                unit.StateReceiptDigest is not null ||
                unit.StateReceiptSequence is not null ||
                unit.TransitionedAtUtc is not null ||
                unit.TransitionReason is not null ||
                unit.MissingInputs.Count != 0 ||
                unit.BlockedProbes.Count != 0)
            {
                throw new DeterministicValidationException(
                    $"Completed unit '{unit.UnitId}' requires exact output paths and validation digests only.");
            }

            _ = Canonicalization.RelativePath(unit.OutputRevision, "output revision");
            _ = Canonicalization.RelativePath(unit.ReportPath, "report path");
            _ = Canonicalization.RelativePath(unit.ValidationManifestPath, "validation manifest path");
        }
        else if (unit.OutputRevision is not null ||
                 unit.ReportPath is not null ||
                 unit.ValidationManifestPath is not null ||
                 unit.ValidationManifestDigest is not null ||
                 unit.PackageValidationManifestDigest is not null ||
                 unit.StatusCounts.Count != 0)
        {
            throw new DeterministicValidationException(
                $"Noncompleted unit '{unit.UnitId}' cannot claim validated completed outputs.");
        }

        if (unit.StateReceiptPath is null != (unit.StateReceiptDigest is null) ||
            unit.StateReceiptPath is null != (unit.StateReceiptSequence is null) ||
            unit.StateReceiptPath is null != (unit.TransitionedAtUtc is null) ||
            unit.StateReceiptPath is null != (unit.TransitionReason is null) ||
            (unit.State is "active" or "blocked") && unit.StateReceiptPath is null)
        {
            throw new DeterministicValidationException(
                $"Run manifest unit '{unit.UnitId}' has an incomplete or missing state receipt binding.");
        }

        if (unit.StateReceiptPath is not null)
        {
            _ = Canonicalization.RelativePath(unit.StateReceiptPath, "state receipt path");
            if (unit.StateReceiptSequence <= 0)
            {
                throw new DeterministicValidationException(
                    $"Run manifest unit '{unit.UnitId}' state receipt sequence is invalid.");
            }

            _ = ContractJson.NormalizeText(unit.TransitionReason!, "state transition reason", 2048);
        }
    }

    private static LibraryUnit ParseUnit(JsonElement value)
    {
        ContractJson.RequireProperties(
            value,
            "unit_id",
            "kind",
            "state",
            "package_unit_id",
            "component_id",
            "render_modes",
            "input_manifest_path",
            "input_manifest_sha256",
            "revision_root",
            "output_revision",
            "report_path",
            "validation_manifest_path",
            "validation_manifest_sha256",
            "package_validation_manifest_sha256",
            "state_receipt_path",
            "state_receipt_sha256",
            "state_receipt_sequence",
            "transitioned_at_utc",
            "transition_reason",
            "status_counts",
            "missing_inputs",
            "blocked_probes");
        return new LibraryUnit(
            ContractJson.String(value, "unit_id"),
            ContractJson.String(value, "kind"),
            ContractJson.String(value, "state"),
            ContractJson.NullableString(value, "package_unit_id"),
            ContractJson.NullableString(value, "component_id"),
            ContractJson.StringArray(value, "render_modes"),
            ContractJson.String(value, "input_manifest_path"),
            ContractJson.Digest(ContractJson.Object(value, "input_manifest_sha256")),
            ContractJson.String(value, "revision_root"),
            ContractJson.NullableString(value, "output_revision"),
            ContractJson.NullableString(value, "report_path"),
            ContractJson.NullableString(value, "validation_manifest_path"),
            ParseNullableDigest(value, "validation_manifest_sha256"),
            ParseNullableDigest(value, "package_validation_manifest_sha256"),
            ContractJson.NullableString(value, "state_receipt_path"),
            ParseNullableDigest(value, "state_receipt_sha256"),
            NullableInt32(value, "state_receipt_sequence"),
            ContractJson.NullableString(value, "transitioned_at_utc"),
            ContractJson.NullableString(value, "transition_reason"),
            ContractJson.Array(value, "status_counts").EnumerateArray().Select(ParseStatusCount).ToArray(),
            ContractJson.StringArray(value, "missing_inputs"),
            ContractJson.StringArray(value, "blocked_probes"));
    }

    private static LibraryStatusCount ParseStatusCount(JsonElement value)
    {
        ContractJson.RequireProperties(value, "status", "count");
        return new LibraryStatusCount(
            ContractJson.String(value, "status"),
            ContractJson.Int32(value, "count"));
    }

    private static Sha256Digest? ParseNullableDigest(JsonElement value, string name)
    {
        var digest = ContractJson.NullableObject(value, name);
        return digest is null ? null : ContractJson.Digest(digest.Value);
    }

    private static int? NullableInt32(JsonElement value, string name)
    {
        var property = value.GetProperty(name);
        return property.ValueKind == JsonValueKind.Null
            ? null
            : ContractJson.Int32(value, name);
    }

    private static string Relative(string root, string path)
    {
        var relative = Path.GetRelativePath(Path.GetFullPath(root), Path.GetFullPath(path))
            .Replace('\\', '/');
        return Canonicalization.RelativePath(relative, "library output path");
    }

    private static void WriteStatusCounts(
        Utf8JsonWriter writer,
        IEnumerable<LibraryStatusCount> counts)
    {
        writer.WriteStartArray();
        foreach (var count in counts)
        {
            writer.WriteStartObject();
            writer.WriteString("status", count.Status);
            writer.WriteNumber("count", count.Count);
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

    private static void WriteNullableDigest(
        Utf8JsonWriter writer,
        string name,
        Sha256Digest? digest)
    {
        if (digest is null)
        {
            writer.WriteNull(name);
        }
        else
        {
            ContractJson.WriteDigest(writer, name, digest);
        }
    }

    private static string Link(string? path) => path is null ? "Not available." : $"[{path}]({path})";

    private static void AddCounts(List<string> lines, IReadOnlyList<LibraryStatusCount> counts)
    {
        if (counts.Count != 0)
        {
            lines.Add(
                " - Status counts: " +
                string.Join(", ", counts.Select(count => $"`{count.Status}` {count.Count}")));
        }
    }

    private static void AddList(List<string> lines, string label, IReadOnlyList<string> values)
    {
        if (values.Count != 0)
        {
            lines.Add($" - {label}: {string.Join("; ", values)}");
        }
    }

    private sealed record UnitScan(
        string State,
        string? OutputRevision,
        string? ReportPath,
        string? ValidationManifestPath,
        Sha256Digest? ValidationManifestDigest,
        Sha256Digest? PackageValidationManifestDigest,
        IReadOnlyList<LibraryStatusCount> StatusCounts,
        IReadOnlyList<string> MissingInputs,
        IReadOnlyList<string> BlockedProbes)
    {
        public static UnitScan Missing { get; } =
            new("pending", null, null, null, null, null, [], [], []);
    }
}
