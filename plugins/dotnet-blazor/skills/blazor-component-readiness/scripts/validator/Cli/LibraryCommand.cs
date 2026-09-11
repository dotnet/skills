using BlazorComponentReadiness.Validator.Inventory;
using BlazorComponentReadiness.Validator.IO;
using BlazorComponentReadiness.Validator.Library;
using BlazorComponentReadiness.Validator.Contracts;

namespace BlazorComponentReadiness.Validator.Cli;

public static class LibraryCommand
{
    private const string Help =
        """
        Full-library validation and factual index

        Usage:
          readiness-validator library reconcile --root <dir> --inventory <inventory.confirmed.json> --run-manifest <run-manifest.json> [--state-update <state-update.json>]
          readiness-validator library validate --root <dir> --inventory <inventory.confirmed.json> --run-manifest <run-manifest.json>
          readiness-validator library index --root <dir> --inventory <inventory.confirmed.json> --run-manifest <run-manifest.json> --json <library-index.json> --markdown <library-index.md>

        A canonical state-update.json supplies schema_version, unit_id, active/blocked/incomplete
        state, missing_inputs, blocked_probes, transitioned_at_utc, and transition_reason.
        """;

    public static int Run(IReadOnlyList<string> args, TextWriter output)
    {
        if (args.Count == 0 || IsHelp(args[0]))
        {
            output.WriteLine(Help);
            return ExitCodes.Success;
        }

        return args[0] switch
        {
            "reconcile" => Reconcile(args.Skip(1).ToArray()),
            "validate" => Validate(args.Skip(1).ToArray()),
            "index" => Index(args.Skip(1).ToArray()),
            _ => throw new UsageException($"Unknown library command '{args[0]}'.")
        };
    }

    private static int Reconcile(IReadOnlyList<string> args)
    {
        var options = CommandOptions.Parse(
            args,
            "--root",
            "--inventory",
            "--run-manifest",
            "--state-update");
        var initial = LoadInventory(options);
        using var runLock = LibraryRunLock.Acquire(initial.Root, initial.Inventory.RunId!);
        var (root, inventoryBytes, inventory) = LoadInventory(options);
        EnsureLockedInventory(initial, inventoryBytes, inventory);
        var manifestRelative = InventoryCommand.ExactPath(
            root,
            options.Single("--run-manifest"),
            "run-manifest.json");
        LibraryRunManifest? previous = null;
        var manifestPath = Path.Combine(root, manifestRelative);
        byte[]? previousFileBytes = null;
        if (File.Exists(manifestPath))
        {
            previousFileBytes = BoundedIO.ReadAllBytes(
                manifestPath,
                ResourceLimits.SerializedArtifactBytes,
                "run manifest");
            try
            {
                previous = LibraryService.Parse(previousFileBytes);
                LibraryService.ValidateShape(previous, inventory, inventoryBytes);
            }
            catch (DeterministicValidationException)
            {
                previous = null;
            }
        }

        var updatePath = options.Optional("--state-update");
        LibraryStateUpdate? suppliedUpdate = null;
        if (updatePath is not null)
        {
            suppliedUpdate = LibraryStateReceiptService.ParseUpdate(BoundedIO.ReadAllBytes(
                updatePath,
                ResourceLimits.SerializedArtifactBytes,
                "library state update"));
            _ = LibraryStateReceiptService.Append(
                root,
                inventory,
                inventoryBytes,
                suppliedUpdate);
        }

        var receipts = LibraryStateReceiptService.Load(root, inventory, inventoryBytes);
        if (suppliedUpdate is null && previous is not null)
        {
            foreach (var active in previous.Units.Where(unit => unit.State == "active"))
            {
                if (!receipts.TryGetValue(active.UnitId, out var latest) ||
                    active.StateReceiptDigest != latest.Digest)
                {
                    continue;
                }

                _ = LibraryStateReceiptService.Append(
                    root,
                    inventory,
                    inventoryBytes,
                    new LibraryStateUpdate(
                        LibraryStateReceiptService.SchemaVersion,
                        active.UnitId,
                        "incomplete",
                        latest.Receipt.MissingInputs,
                        [],
                        latest.Receipt.TransitionedAtUtc,
                        latest.Receipt.TransitionReason));
            }

            receipts = LibraryStateReceiptService.Load(root, inventory, inventoryBytes);
        }

        var reconciled = LibraryService.Reconcile(
            root,
            inventory,
            inventoryBytes,
            previous,
            receipts);
        EnsureUnchanged(
            options,
            inventoryBytes,
            manifestPath,
            previousFileBytes,
            "reconcile");
        AtomicFile.Replace(root, manifestRelative, LibraryService.Serialize(reconciled));
        return ExitCodes.Success;
    }

    private static int Validate(IReadOnlyList<string> args)
    {
        var options = CommandOptions.Parse(args, "--root", "--inventory", "--run-manifest");
        var initial = LoadInventory(options);
        using var runLock = LibraryRunLock.Acquire(initial.Root, initial.Inventory.RunId!);
        var (root, inventoryBytes, inventory) = LoadInventory(options);
        EnsureLockedInventory(initial, inventoryBytes, inventory);
        var manifestBytes = InventoryCommand.ReadExact(
            root,
            options.Single("--run-manifest"),
            "run-manifest.json",
            "run manifest");
        var manifest = LibraryService.Parse(manifestBytes);
        _ = LibraryService.Validate(root, inventory, inventoryBytes, manifest);
        LibraryIndexPublicationService.RecoverCurrent(root);
        return ExitCodes.Success;
    }

    private static int Index(IReadOnlyList<string> args)
    {
        var options = CommandOptions.Parse(
            args,
            "--root",
            "--inventory",
            "--run-manifest",
            "--json",
            "--markdown");
        var initial = LoadInventory(options);
        using var runLock = LibraryRunLock.Acquire(initial.Root, initial.Inventory.RunId!);
        var (root, inventoryBytes, inventory) = LoadInventory(options);
        EnsureLockedInventory(initial, inventoryBytes, inventory);
        var manifestBytes = InventoryCommand.ReadExact(
            root,
            options.Single("--run-manifest"),
            "run-manifest.json",
            "run manifest");
        var manifest = LibraryService.Parse(manifestBytes);
        var validated = LibraryService.Validate(root, inventory, inventoryBytes, manifest);
        var canonicalManifestBytes = LibraryService.Serialize(validated);
        var index = LibraryService.CreateIndex(
            inventory,
            inventoryBytes,
            validated,
            canonicalManifestBytes);
        var json = InventoryCommand.ExactPath(
            root,
            options.Single("--json"),
            "library-index.json");
        var markdown = InventoryCommand.ExactPath(
            root,
            options.Single("--markdown"),
            "library-index.md");
        EnsureUnchanged(
            options,
            inventoryBytes,
            Path.Combine(root, "run-manifest.json"),
            manifestBytes,
            "index");
        _ = LibraryIndexPublicationService.Publish(
            root,
            json,
            markdown,
            index);
        return ExitCodes.Success;
    }

    private static (string Root, byte[] InventoryBytes, LibraryInventory Inventory)
        LoadInventory(CommandOptions options)
    {
        var root = InventoryCommand.PrepareRoot(options.Single("--root"));
        var inventoryBytes = InventoryCommand.ReadExact(
            root,
            options.Single("--inventory"),
            "inventory.confirmed.json",
            "confirmed inventory");
        var inventory = InventoryService.Parse(inventoryBytes);
        InventoryService.Validate(inventory, root, requireConfirmed: true);
        return (root, inventoryBytes, inventory);
    }

    private static void EnsureUnchanged(
        CommandOptions options,
        byte[] inventoryBytes,
        string manifestPath,
        byte[]? manifestBytes,
        string operation)
    {
        var currentInventory = InventoryCommand.ReadExact(
            InventoryCommand.PrepareRoot(options.Single("--root")),
            options.Single("--inventory"),
            "inventory.confirmed.json",
            "confirmed inventory");
        if (!currentInventory.AsSpan().SequenceEqual(inventoryBytes))
        {
            throw new DeterministicValidationException(
                $"Confirmed inventory changed during library {operation}; no output was published.");
        }

        byte[]? currentManifest = File.Exists(manifestPath)
            ? BoundedIO.ReadAllBytes(
                manifestPath,
                ResourceLimits.SerializedArtifactBytes,
                "run manifest")
            : null;
        if (manifestBytes is null != (currentManifest is null) ||
            manifestBytes is not null &&
            !manifestBytes.AsSpan().SequenceEqual(currentManifest))
        {
            throw new DeterministicValidationException(
                $"Run manifest changed during library {operation}; no output was published.");
        }
    }

    private static void EnsureLockedInventory(
        (string Root, byte[] InventoryBytes, LibraryInventory Inventory) initial,
        byte[] lockedBytes,
        LibraryInventory lockedInventory)
    {
        if (initial.Inventory.RunId != lockedInventory.RunId ||
            !initial.InventoryBytes.AsSpan().SequenceEqual(lockedBytes))
        {
            throw new DeterministicValidationException(
                "Confirmed inventory changed before the library run lock was acquired.");
        }
    }

    private static bool IsHelp(string value) => value is "--help" or "-h";
}
