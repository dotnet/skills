using BlazorComponentReadiness.Validator.Comparison;
using BlazorComponentReadiness.Validator.IO;

namespace BlazorComponentReadiness.Validator.Cli;

public static class ComparisonCommand
{
    private const string Help =
        """
        Blinded comparison input gate

        Usage:
          readiness-validator comparison inputs-freeze --root <dir> --draft <json> --output <confirmed>
          readiness-validator comparison inputs-validate --root <dir> --manifest <confirmed>
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
            "inputs-freeze" => Freeze(args.Skip(1).ToArray()),
            "inputs-validate" => Validate(args.Skip(1).ToArray()),
            _ => throw new UsageException($"Unknown comparison command '{args[0]}'.")
        };
    }

    private static int Freeze(IReadOnlyList<string> args)
    {
        var options = CommandOptions.Parse(args, "--root", "--draft", "--output");
        var root = Path.GetFullPath(options.Single("--root"));
        var bytes = BoundedIO.ReadAllBytes(
            options.Single("--draft"),
            ResourceLimits.SerializedArtifactBytes,
            "comparison input draft");
        var manifest = ComparisonInputService.Freeze(root, bytes);
        WriteNew(options.Single("--output"), ComparisonInputService.Serialize(manifest));
        return ExitCodes.Success;
    }

    private static int Validate(IReadOnlyList<string> args)
    {
        var options = CommandOptions.Parse(args, "--root", "--manifest");
        var root = Path.GetFullPath(options.Single("--root"));
        var bytes = BoundedIO.ReadAllBytes(
            options.Single("--manifest"),
            ResourceLimits.SerializedArtifactBytes,
            "comparison input manifest");
        ComparisonInputService.Validate(ComparisonInputService.Parse(bytes), root);
        return ExitCodes.Success;
    }

    private static void WriteNew(string path, ReadOnlySpan<byte> bytes)
    {
        path = Path.GetFullPath(path);
        var parent = Path.GetDirectoryName(path)
            ?? throw new UsageException("Output path requires a parent directory.");
        Directory.CreateDirectory(parent);
        AtomicFile.WriteNew(parent, Path.GetFileName(path), bytes.ToArray());
    }

    private static bool IsHelp(string value) =>
        value is "--help" or "-h";
}
