using BlazorPackageResolver;
using System.Text.Json;

try
{
    var catalog = Catalog.Load(Path.Combine(AppContext.BaseDirectory, "data"));
    var registry = Registry.Bootstrap(catalog);
    byte[] output = args switch
    {
        [] or ["coverage"] => registry.Coverage(catalog),
        ["replay", var manifest] => Replay.Run(manifest, registry),
        ["acquire-lp02", var package, var destination, var project, var implementation, var rationale] =>
            await Lp02SourceAcquisition.CaptureAsync(package, destination, project, implementation, rationale),
        _ => throw new CliUsageException()
    };
    using var stdout = Console.OpenStandardOutput();
    stdout.Write(output);
}
catch (InvalidDataException)
{
    Console.Error.WriteLine("Invalid resolver catalog or replay contract.");
    Environment.ExitCode = 2;
}
catch (JsonException)
{
    Console.Error.WriteLine("Invalid JSON contract.");
    Environment.ExitCode = 2;
}
catch (IOException)
{
    Console.Error.WriteLine("Unable to read required catalog or case input.");
    Environment.ExitCode = 2;
}
catch (UnauthorizedAccessException)
{
    Console.Error.WriteLine("Unable to read required catalog or case input.");
    Environment.ExitCode = 2;
}
catch (CliUsageException)
{
    Console.Error.WriteLine("Usage: blazor-package-resolver [coverage | replay <cases.json> | acquire-lp02 <nupkg> <new-directory> <archive-project-path> <archive-implementation-path> <rationale>]");
    Environment.ExitCode = 2;
}

sealed class CliUsageException : Exception;
