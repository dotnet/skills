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
        _ => throw new ArgumentException("Usage: blazor-package-resolver [coverage | replay <cases.json>]")
    };
    using var stdout = Console.OpenStandardOutput();
    stdout.Write(output);
}
catch (Exception error) when (error is IOException or JsonException or ArgumentException or InvalidOperationException)
{
    Console.Error.WriteLine(error.Message);
    Environment.ExitCode = 1;
}
