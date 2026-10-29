using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using BlazorPackageResolver;

internal static class Pi01BoundaryChecks
{
    private static readonly JsonSerializerOptions FixtureJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };
    private static readonly byte[] Nuspec = Encoding.UTF8.GetBytes(
        "<package xmlns=\"http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd\">" +
        "<metadata><id>Sample.PI01</id><version>1.0.0</version></metadata></package>");

    internal static void Run(Registry registry)
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "fixtures", "pi01");
        var suite = JsonSerializer.Deserialize<CaseSuite>(
            File.ReadAllBytes(Path.Combine(directory, "cases.json")), FixtureJson)
            ?? throw new InvalidDataException("Missing PI-01 frozen cases.");
        var selected = new[]
        {
            "pi01-metadata-named-enum-supported-assembly",
            "pi01-metadata-named-enum-unsupported-assembly",
            "pi01-metadata-named-enum-invalid-framework-token",
            "pi01-metadata-named-custom-attribute-count-without-argument",
            "pi01-metadata-bad-method-signature-content",
            "pi01-metadata-null-custom-attribute-value",
            "pi01-signed-with-unsigned-pe-overlay",
            "pi01-signed-with-authenticode-certificate",
            "pi01-prefixed-nested-with-passive-name",
            "pi01-native-bundle-shaped",
            "pi01-verified-signed",
            "pi01-signed-content-changed"
        };
        var previousCulture = CultureInfo.CurrentCulture;
        var previousUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            foreach (var culture in new[] { "tr-TR", "ar-SA" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
                CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
                foreach (var id in selected)
                {
                    var item = suite.Cases.Single(item => item.CaseId == id);
                    var identity = item.Inputs.Single();
                    var bytes = File.ReadAllBytes(Path.Combine(directory, identity.Path));
                    Check(Catalog.Digest(bytes) == identity.Sha256, "PI-01 pinned boundary bytes");
                    var relocated = identity with { Path = "unavailable/but-pinned.nupkg" };
                    var input = new ResolverCase(id, "PI-01", [relocated],
                        new Dictionary<string, CheckedInput> { ["package"] = new(relocated, bytes) });
                    var actual = registry.Dispatch(input);
                    Check(Canonical.Write((actual with { Inputs = item.Inputs }).Write)
                        .SequenceEqual(Canonical.Write(item.Expected.Write)),
                        $"PI-01 {id} under {culture} uses only checked bytes");
                }
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
        }

        CheckBranch(registry, Pack([("data.txt", new byte[8 * 1024 * 1024])]),
            "no-assemblies", "Exact per-entry uncompressed byte limit");
        CheckBranch(registry, Pack([("data.txt", new byte[8 * 1024 * 1024 + 1])]),
            "unsafe-package", "Over-limit per-entry bytes");
        var max = new byte[8 * 1024 * 1024];
        CheckBranch(registry, Pack(Enumerable.Range(0, 4)
            .Select(index => ($"item{index}.txt", max))), "no-assemblies",
            "Exact total uncompressed byte limit");
        CheckBranch(registry, Pack(Enumerable.Range(0, 4)
            .Select(index => ($"item{index}.txt", max))
            .Append(("extra.txt", new byte[1]))), "unsafe-package",
            "Over-limit total uncompressed bytes");
        CheckBranch(registry, Pack([(new string('a', 1024), [])]), "no-assemblies",
            "Exact ZIP name length limit");
        CheckBranch(registry, Pack([(new string('a', 1025), [])]), "unsafe-package",
            "Over-limit ZIP name length");
        CheckBranch(registry, Pack(Enumerable.Range(0, 4095)
            .Select(index => ($"item{index}.txt", Array.Empty<byte>()))),
            "no-assemblies", "Exact ZIP entry count limit");
        CheckBranch(registry, Pack(Enumerable.Range(0, 4096)
            .Select(index => ($"item{index}.txt", Array.Empty<byte>()))),
            "unsafe-package", "Over-limit ZIP entry count");
        CheckBranch(registry, Pack([("lib/A.dll", []), ("LIB/a.dll", [])]),
            "unsafe-package", "Case-folded ZIP path alias");
        var damagedCrc = Pack([("data.txt", "safe"u8.ToArray())]);
        var central = damagedCrc.AsSpan().LastIndexOf("PK\x01\x02"u8);
        Check(central >= 0, "Generated ZIP central entry");
        damagedCrc[central + 16] ^= 1;
        CheckBranch(registry, damagedCrc, "unsafe-package", "ZIP payload CRC mismatch");
        CheckBranch(registry, new byte[64 * 1024 * 1024 + 1], "unsafe-package",
            "Over-limit package byte count");
        CheckRetained(registry, Path.Combine(directory, "acceptance.json"));
        Console.WriteLine("PI-01 supplemental culture, checked-byte, resource, crypto and metadata checks passed.");
    }

    private static byte[] Pack(IEnumerable<(string Name, byte[] Bytes)> entries)
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            Write(zip, "Sample.PI01.nuspec", Nuspec);
            foreach (var (name, bytes) in entries)
                Write(zip, name, bytes);
        }
        return buffer.ToArray();
    }

    private static void Write(ZipArchive zip, string name, byte[] bytes)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var output = entry.Open();
        output.Write(bytes);
    }

    private static void CheckBranch(Registry registry, byte[] bytes, string branch, string label)
    {
        var identity = new PinnedInput("package", "absent-but-checked.nupkg", Catalog.Digest(bytes));
        var input = new ResolverCase(label, "PI-01", [identity],
            new Dictionary<string, CheckedInput> { ["package"] = new(identity, bytes) });
        var actual = registry.Dispatch(input);
        Check(actual.BranchId == branch, $"{label}: {actual.BranchId} != {branch}");
    }

    private static void CheckRetained(Registry registry, string acceptance)
    {
        string? syncfusion = Environment.GetEnvironmentVariable("PI01_SYNCFUSION_NUPKG");
        string? infragistics = Environment.GetEnvironmentVariable("PI01_INFRAGISTICS_NUPKG");
        if (syncfusion is null && infragistics is null)
            return;
        if (string.IsNullOrWhiteSpace(syncfusion) || string.IsNullOrWhiteSpace(infragistics))
            throw new InvalidDataException("Both PI-01 retained archive bindings are required together.");
        var suite = JsonSerializer.Deserialize<CaseSuite>(File.ReadAllBytes(acceptance), FixtureJson)
            ?? throw new InvalidDataException("Missing PI-01 retained expectations.");
        var paths = new[] { syncfusion, infragistics };
        for (int index = 0; index < paths.Length; index++)
        {
            var item = suite.Cases[index];
            var identity = item.Inputs.Single();
            var bytes = File.ReadAllBytes(paths[index]);
            Check(Catalog.Digest(bytes) == identity.Sha256, "PI-01 retained archive SHA-256");
            var input = new ResolverCase(item.CaseId, "PI-01", [identity],
                new Dictionary<string, CheckedInput> { ["package"] = new(identity, bytes) });
            Check(Canonical.Write(registry.Dispatch(input).Write)
                .SequenceEqual(Canonical.Write(item.Expected.Write)),
                $"PI-01 {item.CaseId} retained result");
        }
        Console.WriteLine("PI-01 retained package checks: two exact archives passed.");
    }

    private static void Check(bool success, string message)
    {
        if (!success)
            throw new InvalidDataException(message);
    }
}
