using BlazorComponentReadiness.Validator.Contracts;
using BlazorComponentReadiness.Validator.Inventory;
using BlazorComponentReadiness.Validator.IO;
using BlazorComponentReadiness.Validator.Library;

namespace BlazorComponentReadiness.Validator.Cli;

public static class InventoryCommand
{
    private const string Help =
        """
        Canonical full-library inventory

        Usage:
          readiness-validator inventory discover --root <dir> --candidates <json> --output <inventory.draft.json>
          readiness-validator inventory confirm --root <dir> --draft <inventory.draft.json> --output <inventory.confirmed.json>
          readiness-validator inventory status --root <dir> --inventory <inventory.confirmed.json> --run-manifest <run-manifest.json>
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
            "discover" => Discover(args.Skip(1).ToArray()),
            "confirm" => Confirm(args.Skip(1).ToArray()),
            "status" => Status(args.Skip(1).ToArray(), output),
            _ => throw new UsageException($"Unknown inventory command '{args[0]}'.")
        };
    }

    private static int Discover(IReadOnlyList<string> args)
    {
        var options = CommandOptions.Parse(args, "--root", "--candidates", "--output");
        var root = PrepareRoot(options.Single("--root"));
        var output = ExactPath(root, options.Single("--output"), "inventory.draft.json");
        var candidates = BoundedIO.ReadAllBytes(
            options.Single("--candidates"),
            ResourceLimits.SerializedArtifactBytes,
            "inventory discovery candidates");
        var inventory = InventoryService.Discover(root, candidates);
        AtomicFile.WriteNew(root, output, InventoryService.Serialize(inventory));
        return ExitCodes.Success;
    }

    private static int Confirm(IReadOnlyList<string> args)
    {
        var options = CommandOptions.Parse(args, "--root", "--draft", "--output");
        var root = PrepareRoot(options.Single("--root"));
        var output = ExactPath(root, options.Single("--output"), "inventory.confirmed.json");
        var draftBytes = BoundedIO.ReadAllBytes(
            options.Single("--draft"),
            ResourceLimits.SerializedArtifactBytes,
            "draft inventory");
        var draft = InventoryService.ParseDraft(draftBytes, root);
        InventoryService.Validate(draft, root, requireConfirmed: false);
        var confirmed = InventoryService.Confirm(draft, root);
        AtomicFile.WriteNew(root, output, InventoryService.Serialize(confirmed));
        return ExitCodes.Success;
    }

    private static int Status(IReadOnlyList<string> args, TextWriter output)
    {
        var options = CommandOptions.Parse(args, "--root", "--inventory", "--run-manifest");
        var root = PrepareRoot(options.Single("--root"));
        var inventoryBytes = ReadExact(
            root,
            options.Single("--inventory"),
            "inventory.confirmed.json",
            "confirmed inventory");
        var inventory = InventoryService.Parse(inventoryBytes);
        InventoryService.Validate(inventory, root, requireConfirmed: true);
        var total = inventory.Packages.Count + inventory.Packages.Sum(item => item.Components.Count);
        try
        {
            var manifestBytes = ReadExact(
                root,
                options.Single("--run-manifest"),
                "run-manifest.json",
                "run manifest");
            var manifest = LibraryService.Parse(manifestBytes);
            var validated = LibraryService.Validate(root, inventory, inventoryBytes, manifest);
            output.Write(System.Text.Encoding.UTF8.GetString(
                LibraryService.SerializeStatus(LibraryService.GetStatus(validated))));
            output.WriteLine();
            return ExitCodes.Success;
        }
        catch (Exception exception) when (
            exception is DeterministicValidationException or
            FileNotFoundException or
            DirectoryNotFoundException)
        {
            var invalid = new LibraryStatus(
                "invalid",
                total,
                0,
                0,
                0,
                0,
                total,
                exception.Message);
            output.Write(System.Text.Encoding.UTF8.GetString(LibraryService.SerializeStatus(invalid)));
            output.WriteLine();
            return ExitCodes.ValidationFailure;
        }
    }

    internal static string PrepareRoot(string value)
    {
        var root = Path.GetFullPath(value);
        Directory.CreateDirectory(root);
        return root;
    }

    internal static string ExactPath(string root, string value, string expected)
    {
        var full = Path.GetFullPath(value);
        var relative = Path.GetRelativePath(root, full).Replace('\\', '/');
        if (relative != expected)
        {
            throw new UsageException($"Output must be exactly '{expected}' beneath --root.");
        }

        return relative;
    }

    internal static byte[] ReadExact(
        string root,
        string value,
        string expected,
        string resource)
    {
        var relative = ExactPath(root, value, expected);
        return BoundedIO.ReadAllBytes(
            SafePath.ResolveUnderRoot(root, relative, requireExisting: true, requireFile: true),
            ResourceLimits.SerializedArtifactBytes,
            resource);
    }

    private static bool IsHelp(string value) => value is "--help" or "-h";
}
