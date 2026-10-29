using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;
using System.Reflection.PortableExecutable;
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
            "pi01-signed-content-changed",
            "pi01-module-alternate",
            "pi01-module-changed-mvid",
            "pi01-module-empty-mvid",
            "pi01-module-false-signed-flag",
            "pi01-utf8-non-ascii-assembly-name",
            "pi01-nonutf8-non-ascii-assembly-name",
            "pi01-utf8-local-flag-mismatch",
            "pi01-utf8-local-name-mismatch"
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
        CheckBranch(registry, Pack([("lib/\u0130.dll", []), ("lib/i\u0307.dll", [])]),
            "unsafe-package", "Expanded Unicode lower-case ZIP path alias");
        CheckBranch(registry, Pack([("lib/\u00c9.dll", []), ("lib/\u00e9.dll", [])]),
            "unsafe-package", "Accented Unicode lower-case ZIP path alias");
        CheckBranch(registry, Pack([("lib/\u00df.dll", [])]),
            "unsafe-package", "Unsupported Unicode casefold shape fails closed");
        CheckBranch(registry, Pack([("lib/\u0345.dll", [])]),
            "unsafe-package", "Unsupported combining Unicode casefold fails closed");
        var unsigned = File.ReadAllBytes(Path.Combine(directory, "templates", "unsigned.dll"));
        CheckBranch(registry, Pack([("lib/\u00c9.dll", unsigned)]),
            "unsigned-assembly", "Supported non-ASCII UTF-8 ZIP entry name");
        var ordered = Resolve(registry, Pack([
            ("lib/\U0001d400.dll", unsigned), ("lib/\uff41.dll", unsigned)
        ]), "Unicode scalar evidence ordering");
        var contract = ordered.EvidenceContract ?? "";
        int fullwidth = contract.IndexOf(@"lib/\uff41.dll", StringComparison.Ordinal);
        int astral = contract.IndexOf(@"lib/\ud835\udc00.dll", StringComparison.Ordinal);
        Check(ordered.BranchId == "unsigned-assembly" &&
            fullwidth >= 0 && astral > fullwidth,
            "PI-01 evidence sorts fullwidth before astral by Unicode scalar value");
        var utf8 = Pack([("lib/S\u00e9.dll", [])]);
        CheckBranch(registry, CorruptUtf8Name(utf8, 0xff), "unsafe-package",
            "Invalid flagged UTF-8 ZIP entry name");
        CheckBranch(registry, CorruptUtf8Name(utf8, 0xc0), "unsafe-package",
            "Overlong flagged UTF-8 ZIP entry name");
        var module = File.ReadAllBytes(Path.Combine(directory, "templates", "module.netmodule"));
        using (var pe = new PEReader(new MemoryStream(module)))
        {
            int cli = pe.PEHeaders.CorHeaderStartOffset;
            Check(cli >= 0 && BinaryPrimitives.ReadInt64LittleEndian(module.AsSpan(cli + 32)) == 0,
                "Pinned module has no signature reservation");
            BinaryPrimitives.WriteInt32LittleEndian(module.AsSpan(cli + 32), 1);
            BinaryPrimitives.WriteInt32LittleEndian(module.AsSpan(cli + 36), 1);
        }
        CheckBranch(registry, Pack([("lib/Signature.netmodule", module)]),
            "unsafe-assembly", "Module with a nonzero signature directory");
        var damagedCrc = Pack([("data.txt", "safe"u8.ToArray())]);
        var central = damagedCrc.AsSpan().LastIndexOf("PK\x01\x02"u8);
        Check(central >= 0, "Generated ZIP central entry");
        damagedCrc[central + 16] ^= 1;
        CheckBranch(registry, damagedCrc, "unsafe-package", "ZIP payload CRC mismatch");
        CheckBranch(registry, new byte[64 * 1024 * 1024 + 1], "unsafe-package",
            "Over-limit package byte count");
        CheckRetained(registry, Path.Combine(directory, "acceptance.json"));
        Console.WriteLine("PI-01 supplemental module, ZIP name, alias, ordering, culture, resource, crypto and metadata checks passed.");
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
        var actual = Resolve(registry, bytes, label);
        Check(actual.BranchId == branch, $"{label}: {actual.BranchId} != {branch}");
    }

    private static CaseResult Resolve(Registry registry, byte[] bytes, string label)
    {
        var identity = new PinnedInput("package", "absent-but-checked.nupkg", Catalog.Digest(bytes));
        var input = new ResolverCase(label, "PI-01", [identity],
            new Dictionary<string, CheckedInput> { ["package"] = new(identity, bytes) });
        return registry.Dispatch(input);
    }

    private static byte[] CorruptUtf8Name(byte[] package, byte replacement)
    {
        var result = (byte[])package.Clone();
        var encoded = Encoding.UTF8.GetBytes("lib/S\u00e9.dll");
        int first = result.AsSpan().IndexOf(encoded);
        int next = first < 0 ? -1 : result.AsSpan(first + encoded.Length).IndexOf(encoded);
        Check(first >= 0 && next >= 0, "Generated local and central UTF-8 names");
        int accent = encoded.AsSpan().IndexOf((byte)0xc3);
        Check(accent >= 0, "Expected two-byte UTF-8 accent");
        result[first + accent] = replacement;
        result[first + encoded.Length + next + accent] = replacement;
        return result;
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
