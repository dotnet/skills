using System.Globalization;
using System.Text;
using System.Text.Json;
using BlazorComponentReadiness.Validator.Contracts;
using BlazorComponentReadiness.Validator.Inventory;
using BlazorComponentReadiness.Validator.IO;
using BlazorComponentReadiness.Validator.Validation;

namespace BlazorComponentReadiness.Validator.Library;

public sealed record LibraryStateReceiptSnapshot(
    string Path,
    Sha256Digest Digest,
    LibraryStateReceipt Receipt,
    bool InterruptedTransition);

public static class LibraryStateReceiptService
{
    public const int SchemaVersion = 1;

    public static LibraryStateUpdate ParseUpdate(ReadOnlyMemory<byte> bytes)
    {
        using var document = StrictJson.Parse(
            bytes,
            ResourceLimits.SerializedArtifactBytes,
            "library state update");
        var root = document.RootElement;
        ContractJson.RequireProperties(
            root,
            "schema_version",
            "unit_id",
            "state",
            "missing_inputs",
            "blocked_probes",
            "transitioned_at_utc",
            "transition_reason");
        var update = new LibraryStateUpdate(
            ContractJson.Int32(root, "schema_version"),
            ContractJson.String(root, "unit_id"),
            ContractJson.String(root, "state"),
            ContractJson.StringArray(root, "missing_inputs"),
            ContractJson.StringArray(root, "blocked_probes"),
            ContractJson.String(root, "transitioned_at_utc"),
            ContractJson.String(root, "transition_reason"));
        ValidateUpdate(update);
        ContractJson.RequireCanonical(bytes.Span, SerializeUpdate(update), "library state update");
        return update;
    }

    public static byte[] SerializeUpdate(LibraryStateUpdate update)
    {
        ValidateUpdate(update);
        return StrictJson.SerializeCanonical(writer =>
        {
            writer.WriteStartObject();
            writer.WriteNumber("schema_version", update.SchemaVersion);
            writer.WriteString("unit_id", update.UnitId);
            writer.WriteString("state", update.State);
            WriteStrings(writer, "missing_inputs", update.MissingInputs);
            WriteStrings(writer, "blocked_probes", update.BlockedProbes);
            writer.WriteString("transitioned_at_utc", update.TransitionedAtUtc);
            writer.WriteString("transition_reason", update.TransitionReason);
            writer.WriteEndObject();
        });
    }

    public static IReadOnlyDictionary<string, LibraryStateReceiptSnapshot> Load(
        string root,
        LibraryInventory inventory,
        ReadOnlySpan<byte> inventoryBytes)
    {
        var result = new Dictionary<string, LibraryStateReceiptSnapshot>(StringComparer.Ordinal);
        var runRootRelative = RunRoot(inventory.RunId!);
        var runRoot = SafePath.ResolveUnderRoot(root, runRootRelative, requireExisting: false);
        if (!Directory.Exists(runRoot))
        {
            return result;
        }

        if (Directory.GetFiles(runRoot, "*", SearchOption.TopDirectoryOnly).Length != 0)
        {
            throw new DeterministicValidationException(
                "Library state receipt run root contains undeclared files.");
        }

        var units = inventory.Packages
            .Select(package => package.UnitId)
            .Concat(inventory.Packages.SelectMany(package => package.Components.Select(component => component.UnitId)))
            .ToDictionary(UnitDirectoryName, unitId => unitId, StringComparer.Ordinal);
        foreach (var unitDirectory in Directory.GetDirectories(runRoot, "*", SearchOption.TopDirectoryOnly))
        {
            var safeUnitDirectory = SafePath.ResolveExistingDirectoryUnderRoot(runRoot, unitDirectory);
            var key = Path.GetFileName(safeUnitDirectory);
            if (!units.TryGetValue(key, out var unitId))
            {
                throw new DeterministicValidationException(
                    "Library state receipt run root contains a receipt directory for an unknown unit.");
            }

            if (Directory.GetDirectories(safeUnitDirectory, "*", SearchOption.TopDirectoryOnly).Length != 0)
            {
                throw new DeterministicValidationException(
                    $"Library state receipt directory for '{unitId}' contains undeclared subdirectories.");
            }

            Sha256Digest? priorDigest = null;
            string? priorState = null;
            var interruptedTransition = false;
            var files = Directory.GetFiles(safeUnitDirectory, "*", SearchOption.TopDirectoryOnly)
                .Select(file => SafePath.ResolveUnderRoot(
                    safeUnitDirectory,
                    Path.GetFileName(file),
                    requireExisting: true,
                    requireFile: true))
                .OrderBy(path => Path.GetFileName(path), StringComparer.Ordinal)
                .ToArray();
            LibraryStateReceiptSnapshot? latest = null;
            for (var index = 0; index < files.Length; index++)
            {
                var expectedName = $"{index + 1:D4}.state-receipt.json";
                if (Path.GetFileName(files[index]) != expectedName)
                {
                    throw new DeterministicValidationException(
                        $"Library state receipt sequence for '{unitId}' is duplicated, skipped, or contains undeclared files.");
                }

                var bytes = BoundedIO.ReadAllBytes(
                    files[index],
                    ResourceLimits.SerializedArtifactBytes,
                    "library state receipt");
                var receipt = ParseReceipt(bytes);
                if (receipt.RunId != inventory.RunId ||
                    receipt.InventoryDigest != InventoryService.Digest(inventoryBytes) ||
                    receipt.UnitId != unitId ||
                    receipt.Sequence != index + 1 ||
                    receipt.PriorReceiptDigest != priorDigest ||
                    receipt.PriorReceiptSequence != (index == 0 ? null : index))
                {
                    throw new DeterministicValidationException(
                        $"Library state receipt sequence for '{unitId}' has run, inventory, unit, sequence, or prior-digest drift.");
                }

                var digest = ContractJson.RawDigest(bytes);
                interruptedTransition |= priorState == "active" && receipt.State == "incomplete";
                latest = new LibraryStateReceiptSnapshot(
                    Relative(root, files[index]),
                    digest,
                    receipt,
                    interruptedTransition);
                priorDigest = digest;
                priorState = receipt.State;
            }

            if (latest is not null)
            {
                result.Add(unitId, latest);
            }
        }

        return result;
    }

    public static LibraryStateReceiptSnapshot Append(
        string root,
        LibraryInventory inventory,
        ReadOnlySpan<byte> inventoryBytes,
        LibraryStateUpdate update)
    {
        ValidateUpdate(update);
        var knownUnits = inventory.Packages
            .Select(package => package.UnitId)
            .Concat(inventory.Packages.SelectMany(package => package.Components.Select(component => component.UnitId)))
            .ToHashSet(StringComparer.Ordinal);
        if (!knownUnits.Contains(update.UnitId))
        {
            throw new DeterministicValidationException(
                $"Library state update names unknown unit '{update.UnitId}'.");
        }

        var latest = Load(root, inventory, inventoryBytes);
        latest.TryGetValue(update.UnitId, out var prior);
        var receipt = new LibraryStateReceipt(
            SchemaVersion,
            inventory.RunId!,
            InventoryService.Digest(inventoryBytes),
            update.UnitId,
            (prior?.Receipt.Sequence ?? 0) + 1,
            prior?.Digest,
            prior?.Receipt.Sequence,
            update.State,
            update.MissingInputs,
            update.BlockedProbes,
            update.TransitionedAtUtc,
            update.TransitionReason);
        var bytes = SerializeReceipt(receipt);
        var directoryRelative = $"{RunRoot(inventory.RunId!)}/{UnitDirectoryName(update.UnitId)}";
        Directory.CreateDirectory(SafePath.ResolveUnderRoot(root, directoryRelative, requireExisting: false));
        var relative = $"{directoryRelative}/{receipt.Sequence:D4}.state-receipt.json";
        AtomicFile.WriteNew(root, relative, bytes);
        return new LibraryStateReceiptSnapshot(
            relative,
            ContractJson.RawDigest(bytes),
            receipt,
            prior?.InterruptedTransition == true ||
            prior?.Receipt.State == "active" && receipt.State == "incomplete");
    }

    public static byte[] SerializeReceipt(LibraryStateReceipt receipt)
    {
        ValidateReceipt(receipt);
        return StrictJson.SerializeCanonical(writer =>
        {
            writer.WriteStartObject();
            writer.WriteNumber("schema_version", receipt.SchemaVersion);
            writer.WriteString("run_id", receipt.RunId);
            ContractJson.WriteDigest(writer, "inventory_sha256", receipt.InventoryDigest);
            writer.WriteString("unit_id", receipt.UnitId);
            writer.WriteNumber("sequence", receipt.Sequence);
            WriteNullableDigest(writer, "prior_receipt_sha256", receipt.PriorReceiptDigest);
            if (receipt.PriorReceiptSequence is null)
            {
                writer.WriteNull("prior_receipt_sequence");
            }
            else
            {
                writer.WriteNumber("prior_receipt_sequence", receipt.PriorReceiptSequence.Value);
            }

            writer.WriteString("state", receipt.State);
            WriteStrings(writer, "missing_inputs", receipt.MissingInputs);
            WriteStrings(writer, "blocked_probes", receipt.BlockedProbes);
            writer.WriteString("transitioned_at_utc", receipt.TransitionedAtUtc);
            writer.WriteString("transition_reason", receipt.TransitionReason);
            writer.WriteEndObject();
        });
    }

    private static LibraryStateReceipt ParseReceipt(ReadOnlyMemory<byte> bytes)
    {
        using var document = StrictJson.Parse(
            bytes,
            ResourceLimits.SerializedArtifactBytes,
            "library state receipt");
        var root = document.RootElement;
        ContractJson.RequireProperties(
            root,
            "schema_version",
            "run_id",
            "inventory_sha256",
            "unit_id",
            "sequence",
            "prior_receipt_sha256",
            "prior_receipt_sequence",
            "state",
            "missing_inputs",
            "blocked_probes",
            "transitioned_at_utc",
            "transition_reason");
        var prior = ContractJson.NullableObject(root, "prior_receipt_sha256");
        var receipt = new LibraryStateReceipt(
            ContractJson.Int32(root, "schema_version"),
            ContractJson.String(root, "run_id"),
            ContractJson.Digest(ContractJson.Object(root, "inventory_sha256")),
            ContractJson.String(root, "unit_id"),
            ContractJson.Int32(root, "sequence"),
            prior is null ? null : ContractJson.Digest(prior.Value),
            NullableInt32(root, "prior_receipt_sequence"),
            ContractJson.String(root, "state"),
            ContractJson.StringArray(root, "missing_inputs"),
            ContractJson.StringArray(root, "blocked_probes"),
            ContractJson.String(root, "transitioned_at_utc"),
            ContractJson.String(root, "transition_reason"));
        ValidateReceipt(receipt);
        ContractJson.RequireCanonical(bytes.Span, SerializeReceipt(receipt), "library state receipt");
        return receipt;
    }

    private static void ValidateUpdate(LibraryStateUpdate update)
    {
        if (update.SchemaVersion != SchemaVersion)
        {
            throw new DeterministicValidationException("Library state update schema_version must be 1.");
        }

        ValidateState(
            update.UnitId,
            update.State,
            update.MissingInputs,
            update.BlockedProbes,
            update.TransitionedAtUtc,
            update.TransitionReason);
    }

    private static void ValidateReceipt(LibraryStateReceipt receipt)
    {
        if (receipt.SchemaVersion != SchemaVersion ||
            receipt.Sequence <= 0 ||
            receipt.Sequence == 1 &&
            (receipt.PriorReceiptDigest is not null || receipt.PriorReceiptSequence is not null) ||
            receipt.Sequence > 1 &&
            (receipt.PriorReceiptDigest is null ||
             receipt.PriorReceiptSequence != receipt.Sequence - 1))
        {
            throw new DeterministicValidationException(
                "Library state receipt schema, sequence, or prior digest is invalid.");
        }

        _ = ContractJson.NormalizeText(receipt.RunId, "state receipt run ID", 128);
        ValidateState(
            receipt.UnitId,
            receipt.State,
            receipt.MissingInputs,
            receipt.BlockedProbes,
            receipt.TransitionedAtUtc,
            receipt.TransitionReason);
    }

    private static void ValidateState(
        string unitId,
        string state,
        IReadOnlyList<string> missingInputs,
        IReadOnlyList<string> blockedProbes,
        string transitionedAtUtc,
        string transitionReason)
    {
        _ = ContractJson.NormalizeText(unitId, "state receipt unit ID", 512);
        if (state is not ("active" or "blocked" or "incomplete"))
        {
            throw new DeterministicValidationException(
                "Library state updates may set only active, blocked, or incomplete.");
        }

        var canonicalMissing = CanonicalMessages(missingInputs);
        var canonicalBlocked = CanonicalMessages(blockedProbes);
        if (!canonicalMissing.SequenceEqual(missingInputs, StringComparer.Ordinal) ||
            !canonicalBlocked.SequenceEqual(blockedProbes, StringComparer.Ordinal) ||
            state != "blocked" && blockedProbes.Count != 0)
        {
            throw new DeterministicValidationException(
                "Library state update details must be canonical and blocked probes belong only to blocked state.");
        }

        _ = ContractJson.NormalizeText(transitionReason, "state transition reason", 2048);
        if (transitionReason.Length == 0 ||
            !DateTimeOffset.TryParseExact(
                transitionedAtUtc,
                "yyyy-MM-dd'T'HH:mm:ss'Z'",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed) ||
            parsed.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture) != transitionedAtUtc)
        {
            throw new DeterministicValidationException(
                "Library state update requires a nonempty reason and canonical UTC-second timestamp.");
        }
    }

    private static IReadOnlyList<string> CanonicalMessages(IEnumerable<string> values) =>
        values.Select(value => ContractJson.NormalizeText(value, "run state detail", 2048))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static string RunRoot(string runId) =>
        Canonicalization.RelativePath($"state-receipts/{runId}", "state receipt run root");

    private static string UnitDirectoryName(string unitId) =>
        DomainSeparatedHash.Compute("library.state-unit", Encoding.UTF8.GetBytes(unitId));

    private static string Relative(string root, string path) =>
        Canonicalization.RelativePath(
            Path.GetRelativePath(Path.GetFullPath(root), Path.GetFullPath(path)).Replace('\\', '/'),
            "state receipt path");

    private static void WriteStrings(
        Utf8JsonWriter writer,
        string name,
        IEnumerable<string> values)
    {
        writer.WritePropertyName(name);
        writer.WriteStartArray();
        foreach (var value in values)
        {
            writer.WriteStringValue(value);
        }

        writer.WriteEndArray();
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

    private static int? NullableInt32(JsonElement value, string name)
    {
        var property = value.GetProperty(name);
        return property.ValueKind == JsonValueKind.Null
            ? null
            : ContractJson.Int32(value, name);
    }
}
