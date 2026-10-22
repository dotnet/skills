using System.Globalization;
using BlazorComponentReadiness.SourceReader;
using BlazorComponentReadiness.Validator.Cli;
using BlazorComponentReadiness.Validator.IO;

const string help = """
Read a complete captured C# method or constructor, without compiling assessed source.

Usage:
  dotnet BlazorComponentReadiness.SourceReader.dll --root <approved-external-root>
    --receipt <root-relative-capture.json> --expected-sha256 <raw-receipt-sha256>
    --source-path <captured-repository-relative.cs> --line <1-based-line>
    --output <new-root-relative.json>

The entire member must fit a 12 KiB serialized packet; source is limited to 8 MiB.
Failure is an inspection limitation, never a product gap. Parent output directories must exist.
""";

try
{
    if (args is ["--help"] or ["help"])
    {
        Console.WriteLine(help);
        return ExitCodes.Success;
    }
    var allowed = new[] { "--root", "--receipt", "--expected-sha256", "--source-path", "--line", "--output" };
    var options = new Dictionary<string, string>(StringComparer.Ordinal);
    for (var i = 0; i < args.Length; i += 2)
    {
        if (i + 1 >= args.Length || !allowed.Contains(args[i], StringComparer.Ordinal) ||
            string.IsNullOrWhiteSpace(args[i + 1]) || !options.TryAdd(args[i], args[i + 1]))
            throw new ArgumentException("Provide each of the six named options exactly once.");
    }
    if (options.Count != allowed.Length ||
        !int.TryParse(options["--line"], NumberStyles.None, CultureInfo.InvariantCulture, out var line) || line < 1)
        throw new ArgumentException("All six options and a positive 1-based line are required.");
    var result = SourceMemberReader.Read(options["--root"], options["--receipt"], options["--expected-sha256"],
        options["--source-path"], line, options["--output"]);
    Console.WriteLine($"Complete member packet written: {result.Length} bytes.");
    return ExitCodes.Success;
}
catch (ArgumentException error)
{
    Console.Error.WriteLine($"usage error: {error.Message}\n{help}");
    return ExitCodes.InvalidUsage;
}
catch (DeterministicValidationException error)
{
    Console.Error.WriteLine($"inspection failure: {error.Message}");
    return ExitCodes.ValidationFailure;
}
catch (Exception error) when (error is IOException or UnauthorizedAccessException or TypeLoadException)
{
    Console.Error.WriteLine($"environment failure: {error.Message}");
    return ExitCodes.EnvironmentFailure;
}
