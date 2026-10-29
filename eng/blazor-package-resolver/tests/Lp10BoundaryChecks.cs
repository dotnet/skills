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
        CheckOracleProbes(registry, suite);
        CheckRetained(registry, Path.Combine(directory, "acceptance.json"));
    }

    private sealed record OracleProbe(string Name, string Path, string Text, string Branch,
        string? Kind = null, string? Reason = null, string? FirstLocator = null,
        int LocatorCount = 0, string? LocatorsSha256 = null, string? Source = null);

    private static void CheckOracleProbes(Registry registry, CaseSuite suite)
    {
        const string property = "$([System.IO.File]::ReadAllText('a'))";
        const string props = "build/Task.props";
        const string emptyLocators = "37517e5f3dc66819f61f5a7bb8ace1921282415f10551d2defa5c3eb0985b570";
        const string unsupported = "unsupported-executable-content";
        const string text = "text-code-attribution-unverified";
        const string incomplete = "incomplete-inventory";
        const string noCode = "no-code-payload";
        const string msbuildUnsupported = "msbuild-unsupported";
        const string msbuildActive = "msbuild-active";
        var probes = new[]
        {
            new OracleProbe("node-comment", props,
                $"<Project><PropertyGroup><Foo><!--c-->{property}</Foo></PropertyGroup></Project>",
                unsupported, "unsupported", msbuildUnsupported,
                "Project[1]/PropertyGroup[1]/Foo[1]#property-function", 1,
                "af360726ff2a071c3d41459bf850ddc2e127fbf75e4864f8eddf9e266c9ba46c"),
            new OracleProbe("node-pi", props,
                $"<Project><PropertyGroup><Foo><?pi x?>{property}</Foo></PropertyGroup></Project>",
                unsupported, "unsupported", msbuildUnsupported,
                "Project[1]/PropertyGroup[1]/Foo[1]#property-function", 1,
                "af360726ff2a071c3d41459bf850ddc2e127fbf75e4864f8eddf9e266c9ba46c"),
            new OracleProbe("node-cdata-whitespace", props,
                $"<Project><PropertyGroup><Foo> \n<!--c--><![CDATA[{property}]]></Foo></PropertyGroup></Project>",
                unsupported, "unsupported", msbuildUnsupported,
                "Project[1]/PropertyGroup[1]/Foo[1]#property-function", 1,
                "d22213db33b8c20c84990ee406c61babbd133dc8ca4805e599eedbb4083e17cd"),
            new OracleProbe("metadata-comment", props,
                $"<Project><ItemGroup><I Include=\"x\"><M><!--c-->{property}</M></I></ItemGroup></Project>",
                unsupported, "unsupported", msbuildUnsupported,
                "Project[1]/ItemGroup[1]/I[1]/M[1]#property-function", 1,
                "099e1d8713b7e96aa639071a0e0acdecf5e6b8ae6624ecf6b84e4b096d4a58e7"),
            new OracleProbe("tail-comment-pi-cdata", props,
                $"<Project><PropertyGroup><A/><!--c--><?pi x?><![CDATA[{property}]]><B/></PropertyGroup></Project>",
                unsupported, "unsupported", msbuildUnsupported,
                "Project[1]/PropertyGroup[1]/A[1]#tail-function", 1,
                "0b3b2ac8e82335c6da7f0ecaa98e01a36db4dfa7dd3da524794777a1e582fa6e"),
            new OracleProbe("tail-concatenated-text", props,
                $"<Project><PropertyGroup><A/>prefix<!--c-->{property}<?pi x?>suffix<B/></PropertyGroup></Project>",
                unsupported, "unsupported", msbuildUnsupported,
                "Project[1]/PropertyGroup[1]/A[1]#tail-function", 1,
                "f7950ecd8ec64318a38fc8fee1c2dff41dc75811a9e6470e10c69a41b8069923"),
            new OracleProbe("command-comment", props,
                "<Project><PropertyGroup><PreBuildEvent><!--c-->echo sample</PreBuildEvent></PropertyGroup></Project>",
                text, "text", msbuildActive,
                "Project[1]/PropertyGroup[1]/PreBuildEvent[1]#text", 1,
                "768f2e4bbe0dc916efc4cf7ac5624536bb3e37f558cd131112e347642c363030"),
            new OracleProbe("command-concatenated-text", props,
                "<Project><PropertyGroup><PreBuildEvent>echo <!--c-->sample</PreBuildEvent></PropertyGroup></Project>",
                text, "text", msbuildActive,
                "Project[1]/PropertyGroup[1]/PreBuildEvent[1]#text", 1,
                "768f2e4bbe0dc916efc4cf7ac5624536bb3e37f558cd131112e347642c363030"),
            new OracleProbe("command-cdata", props,
                "<Project><PropertyGroup><PreBuildEvent><![CDATA[echo sample]]></PreBuildEvent></PropertyGroup></Project>",
                text, "text", msbuildActive,
                "Project[1]/PropertyGroup[1]/PreBuildEvent[1]#text", 1,
                "768f2e4bbe0dc916efc4cf7ac5624536bb3e37f558cd131112e347642c363030"),
            new OracleProbe("inline-task-comment-cdata", props,
                $"<Project><UsingTask TaskName=\"T\" TaskFactory=\"RoslynCodeTaskFactory\"><Task><!--c--><![CDATA[{property}]]></Task></UsingTask></Project>",
                unsupported, "unsupported", msbuildUnsupported,
                "Project[1]/UsingTask[1]/Task[1]#property-function", 2,
                "be5cc76fb30f45686684a61f8a99406f01ca2278750d816d656b1fdb511d89a4"),
            new OracleProbe("inline-task-concatenated-text", props,
                $"<Project><UsingTask TaskName=\"T\" TaskFactory=\"RoslynCodeTaskFactory\"><Task>prefix<!--c-->{property}</Task></UsingTask></Project>",
                unsupported, "unsupported", msbuildUnsupported,
                "Project[1]/UsingTask[1]/Task[1]#property-function", 2,
                "8d505d73b88a3d0c0999b276381e24baacf6a27bf3e1d3f6846308184b75760c"),
            new OracleProbe("inline-code-source-comment", props,
                "<Project><UsingTask TaskName=\"T\" TaskFactory=\"RoslynCodeTaskFactory\"><Task><Code Source=\"$(MSBuildThisFileDirectory)src.txt\"><!--c-->return 1;</Code></Task></UsingTask></Project>",
                text, "text", msbuildActive,
                "Project[1]/UsingTask[1]/Task[1]/Code[1]#text", 2,
                "99f0db0aaeae1526c69e37f40d9d659c70e6fc5fcb193592794342ae143da637",
                "passive text"),
            new OracleProbe("uppercase-xml-pi", "build/run.txt",
                "<?XML version=\"1.0\"?>\n<Project><Target Name=\"Go\"><Exec Command=\"echo sample\"/></Target></Project>",
                incomplete),
            new OracleProbe("uppercase-xml-pi-over-limit", "build/run.txt",
                "<?XML version=\"1.0\"?>\n<!--" + new string('x', 262144) +
                "--><Project><Target Name=\"Go\"><Exec Command=\"echo sample\"/></Target></Project>",
                incomplete),
            new OracleProbe("unicode-control-prolog", "build/run.txt",
                "<?xml version=\"1.0\"?>\u001c<Project><Target Name=\"Go\"><Exec Command=\"echo sample\"/></Target></Project>",
                incomplete),
            new OracleProbe("unicode-control-1d-prolog", "build/run.txt",
                "<?xml version=\"1.0\"?>\u001d<Project><Target Name=\"Go\"><Exec Command=\"echo sample\"/></Target></Project>",
                incomplete),
            new OracleProbe("unicode-control-1e-prolog", "build/run.txt",
                "<?xml version=\"1.0\"?>\u001e<Project><Target Name=\"Go\"><Exec Command=\"echo sample\"/></Target></Project>",
                incomplete),
            new OracleProbe("unicode-control-1f-prolog", "build/run.txt",
                "<?xml version=\"1.0\"?>\u001f<Project><Target Name=\"Go\"><Exec Command=\"echo sample\"/></Target></Project>",
                incomplete),
            new OracleProbe("unicode-control-root-boundary", "build/run.txt",
                "<Project\u001c><Target Name=\"Go\"><Exec Command=\"echo sample\"/></Target></Project>",
                incomplete),
            new OracleProbe("unicode-control-1c-doctype", "build/run.txt",
                "<!\u001cDOCTYPE Project><Project><Target Name=\"Go\"><Exec Command=\"echo sample\"/></Target></Project>",
                incomplete),
            new OracleProbe("unicode-control-1d-doctype", "build/run.txt",
                "<!\u001dDOCTYPE Project><Project><Target Name=\"Go\"><Exec Command=\"echo sample\"/></Target></Project>",
                incomplete),
            new OracleProbe("unicode-control-1e-doctype", "build/run.txt",
                "<!\u001eDOCTYPE Project><Project><Target Name=\"Go\"><Exec Command=\"echo sample\"/></Target></Project>",
                incomplete),
            new OracleProbe("unicode-control-1f-doctype", "build/run.txt",
                "<!\u001fDOCTYPE Project><Project><Target Name=\"Go\"><Exec Command=\"echo sample\"/></Target></Project>",
                incomplete),
            new OracleProbe("non-ascii-first-prefix", "build/run.txt",
                "<\u0394:Project xmlns:\u0394=\"http://schemas.microsoft.com/developer/msbuild/2003\"><Target Name=\"Go\"><Exec Command=\"echo sample\"/></Target></\u0394:Project>",
                noCode),
            new OracleProbe("ascii-first-unicode-prefix", "build/run.txt",
                "<A\u00e9:Project xmlns:A\u00e9=\"http://schemas.microsoft.com/developer/msbuild/2003\"><A\u00e9:Target Name=\"Go\"><A\u00e9:Exec Command=\"echo sample\"/></A\u00e9:Target></A\u00e9:Project>",
                text, "text", msbuildActive,
                "Project[1]/Target[1]/Exec[1]@Command", 1,
                "7fb8fe33ee4ace317fd79d4b33f5b36583b5261ea9d257197e51941b7d64c434"),
            new OracleProbe("python-word-numeric-prefix", "build/run.txt",
                "<A\u2160:Project xmlns:A\u2160=\"http://schemas.microsoft.com/developer/msbuild/2003\"><Target Name=\"Go\"><Exec Command=\"echo sample\"/></Target></A\u2160:Project>",
                incomplete),
            new OracleProbe("property-function-comment-pi-decoy", props,
                $"<Project><PropertyGroup><Foo><!--{property}--><?pi {property}?></Foo></PropertyGroup></Project>",
                noCode),
            new OracleProbe("bom-lib-xml-doc", "lib/net8.0/Sample.xml",
                "\ufeff<?xml version=\"1.0\"?><doc/>", noCode)
        };
        foreach (var probe in probes)
        {
            var raw = Encoding.UTF8.GetBytes(probe.Text);
            var bytes = OraclePackage(probe.Path, raw, probe.Source);
            var identity = new PinnedInput("package", "unavailable/but-pinned.nupkg",
                Catalog.Digest(bytes));
            var input = new ResolverCase(probe.Name, "LP-10", [identity],
                new Dictionary<string, CheckedInput> { ["package"] = new(identity, bytes) });
            var actual = registry.Dispatch(input);
            var expected = suite.Cases.First(item => item.Expected.BranchId == probe.Branch).Expected;
            Check(actual.BranchId == probe.Branch &&
                actual.Decision == expected.Decision &&
                actual.Applicability == expected.Applicability &&
                actual.EvidenceReferences.SequenceEqual(expected.EvidenceReferences) &&
                actual.Finding == expected.Finding &&
                actual.MissingFact == expected.MissingFact &&
                actual.RequiredInput == expected.RequiredInput &&
                actual.NextCategory == expected.NextCategory,
                $"Oracle-derived branch/result contract: {probe.Name}");
            if (probe.Kind is null)
            {
                Check(actual.EvidenceContract == expected.EvidenceContract,
                    $"Oracle-derived no-code/structural evidence: {probe.Name}");
                continue;
            }
            const string prefix = "Checked code candidates (exact bytes hashed; locator snippets are display-only): ";
            const string suffix = ". These checked bytes do not establish ownership, release-commit correspondence or upstream.";
            var contract = actual.EvidenceContract ??
                throw new InvalidDataException($"Missing LP-10 candidate evidence: {probe.Name}");
            Check(contract.StartsWith(prefix, StringComparison.Ordinal) &&
                contract.EndsWith(suffix, StringComparison.Ordinal),
                $"Oracle-derived candidate contract shape: {probe.Name}");
            using var json = JsonDocument.Parse(contract[prefix.Length..^suffix.Length]);
            var candidates = json.RootElement;
            Check(candidates.GetArrayLength() == (probe.Source is null ? 1 : 2),
                $"Oracle-derived candidate count: {probe.Name}");
            CheckCandidate(candidates[0], probe.Path, raw, probe.Kind, probe.Reason!,
                probe.FirstLocator, probe.LocatorCount, probe.LocatorsSha256!, probe.Name);
            if (probe.Source is not null)
                CheckCandidate(candidates[1], "build/src.txt", Encoding.UTF8.GetBytes(probe.Source),
                    "text", "promoted-in-package-code-source", null, 0, emptyLocators, probe.Name);
        }
        Console.WriteLine($"LP-10 oracle-derived XML/text boundary checks: {probes.Length} cases passed.");
    }

    private static byte[] OraclePackage(string path, byte[] payload, string? source)
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            void Add(string name, byte[] content)
            {
                using var output = zip.CreateEntry(name, CompressionLevel.NoCompression).Open();
                output.Write(content);
            }
            Add("Sample.nuspec", "<package><metadata><id>X</id><version>1.0</version></metadata></package>"u8.ToArray());
            Add(path, payload);
            if (source is not null)
                Add("build/src.txt", Encoding.UTF8.GetBytes(source));
        }
        return buffer.ToArray();
    }

    private static void CheckCandidate(JsonElement actual, string path, byte[] raw,
        string kind, string reason, string? first, int count, string locatorsSha, string probe)
    {
        Check(actual.EnumerateObject().Count() == 8 &&
            actual.GetProperty("path").GetString() == path &&
            actual.GetProperty("sha256").GetString() == Catalog.Digest(raw) &&
            actual.GetProperty("size_bytes").GetInt32() == raw.Length &&
            actual.GetProperty("classification").GetString() == kind &&
            actual.GetProperty("reason").GetString() == reason &&
            actual.GetProperty("first_locator").GetString() == first &&
            actual.GetProperty("locator_count").GetInt32() == count &&
            actual.GetProperty("locators_sha256").GetString() == locatorsSha,
            $"Oracle-derived exact entry/locator inventory: {probe}/{path}");
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
