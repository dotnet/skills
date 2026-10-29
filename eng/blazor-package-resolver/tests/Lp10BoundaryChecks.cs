using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using BlazorPackageResolver;

internal static class Lp10BoundaryChecks
{
    private static readonly JsonSerializerOptions FixtureJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    internal static void Run(Registry registry, string fixture)
    {
        var suite = JsonSerializer.Deserialize<CaseSuite>(File.ReadAllBytes(fixture), FixtureJson)
            ?? throw new InvalidDataException("Missing LP-10 cases.");
        var directory = Path.GetDirectoryName(fixture)!;
        var ids = new[]
        {
            "lp10-upper-js", "lp10-inline-task", "lp10-compiled-plus-js",
            "lp10-nuspec-comment-decoy", "lp10-build-comment-decoy",
            "lp10-per-entry-at-limit", "lp10-per-entry-over-limit",
            "lp10-total-at-limit", "lp10-total-over-limit",
            "lp10-msbuild-xml-at-limit", "lp10-msbuild-xml-over-limit",
            "lp10-import-count-at-limit", "lp10-import-count-over-limit"
        };
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            foreach (var culture in new[] { "tr-TR", "ar-SA" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
                CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
                foreach (var id in ids)
                {
                    var item = suite.Cases.Single(item => item.CaseId == id);
                    var identity = item.Inputs.Single();
                    var content = File.ReadAllBytes(Path.Combine(directory, identity.Path));
                    Check(Catalog.Digest(content) == identity.Sha256, "Pinned boundary input bytes");
                    var relocated = identity with { Path = "unavailable/but-pinned.nupkg" };
                    var input = new ResolverCase(id, "LP-10", [relocated],
                        new Dictionary<string, CheckedInput>
                            { ["package"] = new(relocated, content) });
                    var actual = registry.Dispatch(input);
                    Check(Canonical.Write((actual with { Inputs = item.Inputs }).Write)
                        .SequenceEqual(Canonical.Write(item.Expected.Write)),
                        $"LP-10 {id} under {culture} consumes only checked bytes");
                    if (id.StartsWith("lp10-per-entry-", StringComparison.Ordinal) ||
                        id.StartsWith("lp10-total-", StringComparison.Ordinal))
                    {
                        using var archive = new ZipArchive(new MemoryStream(content), ZipArchiveMode.Read);
                        var lengths = archive.Entries.Where(entry => !entry.FullName.EndsWith(
                            ".nuspec", StringComparison.OrdinalIgnoreCase)).Select(entry => entry.Length);
                        var expected = id.StartsWith("lp10-per-entry-", StringComparison.Ordinal)
                            ? 8L * 1024 * 1024 : 32L * 1024 * 1024;
                        var observed = id.StartsWith("lp10-per-entry-", StringComparison.Ordinal)
                            ? lengths.Max() : lengths.Sum();
                        Check(observed == expected + (id.EndsWith("over-limit", StringComparison.Ordinal) ? 1 : 0),
                            $"LP-10 {id} tests the measured uncompressed ceiling");
                    }
                }
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
        Console.WriteLine($"LP-10 supplemental checked-byte, culture and resource checks: {ids.Length * 2} cases passed.");
        CheckCrcCorruption(registry);
        CheckRetained(registry, Path.Combine(directory, "acceptance.json"));
    }

    private static void CheckCrcCorruption(Registry registry)
    {
        const string nuspec = "<package><metadata><id>Sample</id><version>1.0</version></metadata></package>";
        const string script = "console.log(1)";
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, value) in new[]
            {
                ("Sample.nuspec", nuspec), ("wwwroot/code.js", script)
            })
            {
                using var output = zip.CreateEntry(name, CompressionLevel.NoCompression).Open();
                output.Write(Encoding.UTF8.GetBytes(value));
            }
        }
        var archive = buffer.ToArray();
        foreach (var payload in new[] { nuspec, script })
        {
            var bytes = (byte[])archive.Clone();
            var marker = Encoding.UTF8.GetBytes(payload);
            var position = bytes.AsSpan().IndexOf(marker);
            Check(position > 0, "CRC probe must locate uncompressed entry bytes.");
            bytes[position + marker.Length - 1] ^= 1;
            using (var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read))
                Check(zip.Entries.Count == 2, "CRC probe must retain a readable central directory.");
            var pinned = new PinnedInput("package", "pinned-but-absent.nupkg", Catalog.Digest(bytes));
            var input = new ResolverCase("crc-mismatch", "LP-10", [pinned],
                new Dictionary<string, CheckedInput> { ["package"] = new(pinned, bytes) });
            var result = registry.Dispatch(input);
            Check(result.BranchId == "unreadable-package" && result.Applicability == "unknown" &&
                result.RequiredInput == "valid-nupkg" && result.EvidenceContract is null,
                "A damaged nuspec or code-entry CRC cannot yield an inventory.");
        }
    }

    private static void CheckRetained(Registry registry, string fixture)
    {
        var syncfusion = Environment.GetEnvironmentVariable("LP10_SYNCFUSION_NUPKG");
        var infragistics = Environment.GetEnvironmentVariable("LP10_INFRAGISTICS_NUPKG");
        if (syncfusion is null && infragistics is null)
            return;
        if (string.IsNullOrWhiteSpace(syncfusion) || string.IsNullOrWhiteSpace(infragistics))
            throw new InvalidDataException("Both LP-10 retained package bindings are required.");
        var suite = JsonSerializer.Deserialize<CaseSuite>(File.ReadAllBytes(fixture), FixtureJson)
            ?? throw new InvalidDataException("Missing LP-10 retained expectations.");
        Check(suite.Cases.Count == 2, "Two frozen retained LP-10 results");
        foreach (var (item, path, candidateCount) in new[]
        {
            (suite.Cases[0], syncfusion, 24), (suite.Cases[1], infragistics, 10)
        })
        {
            var identity = item.Inputs.Single();
            var bytes = File.ReadAllBytes(path);
            Check(Catalog.Digest(bytes) == identity.Sha256, "Exact retained LP-10 package hash");
            var input = new ResolverCase(item.CaseId, "LP-10", item.Inputs,
                new Dictionary<string, CheckedInput> { ["package"] = new(identity, bytes) });
            var result = registry.Dispatch(input);
            Check(Canonical.Write(result.Write).SequenceEqual(Canonical.Write(item.Expected.Write)) &&
                Canonical.Write(result.Write).SequenceEqual(Canonical.Write(registry.Dispatch(input).Write)),
                $"Frozen retained LP-10 replay: {item.CaseId}");
            var contract = result.EvidenceContract!;
            var candidates = contract["Checked code candidates (exact bytes hashed; locator snippets are display-only): ".Length..];
            candidates = candidates[..candidates.IndexOf(". These checked bytes", StringComparison.Ordinal)];
            using var json = JsonDocument.Parse(candidates);
            Check(json.RootElement.GetArrayLength() == candidateCount &&
                json.RootElement.EnumerateArray().Count(record =>
                    record.GetProperty("classification").GetString() == "compiled") == 3 &&
                json.RootElement.EnumerateArray().Count(record =>
                    record.GetProperty("classification").GetString() == "text") == candidateCount - 3,
                $"Exact retained LP-10 candidate inventory: {item.CaseId}");
            Console.WriteLine($"LP-10 {item.CaseId}: {candidateCount} pinned candidates; mixed escalation matched.");
        }
    }

    private static void Check(bool success, string reason)
    {
        if (!success)
            throw new InvalidDataException(reason);
    }
}
