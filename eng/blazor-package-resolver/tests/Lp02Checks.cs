using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using BlazorPackageResolver;

internal static class Lp02Checks
{
    internal static void Run(Registry registry, string directory)
    {
        var fixture = Path.Combine(directory, "cases.json");
        var output = Replay.Run(fixture, registry);
        Require(output.SequenceEqual(Replay.Run(fixture, registry)), "Frozen replay determinism");
        using var suite = JsonDocument.Parse(output);
        Require(suite.RootElement.GetProperty("cases").GetArrayLength() == 33, "33 frozen cases");
        var package = File.ReadAllBytes(Path.Combine(directory, "inputs", "public-source.nupkg"));
        var archive = File.ReadAllBytes(Path.Combine(directory, "inputs", "public-source.zip"));
        var receipt = JsonNode.Parse(File.ReadAllBytes(Path.Combine(directory, "inputs", "public-source.json")))!;
        var record = receipt.Deserialize<Lp02Observation>(Lp02PublicSourceResolver.JsonOptions)!;
        var project = record.SourceInventory[0].Path;
        var implementation = record.SourceInventory[1].Path;
        using var originalZip = new ZipArchive(new MemoryStream(archive));
        using var projectReader = new StreamReader(originalZip.GetEntry(project)!.Open());
        var projectXml = projectReader.ReadToEnd();
        using var sourceStream = originalZip.GetEntry(implementation)!.Open();
        using var sourceBuffer = new MemoryStream();
        sourceStream.CopyTo(sourceBuffer);
        CaseResult ProjectResult(byte[] projectBytes, string? extraPath = null)
        {
            var files = new Dictionary<string, byte[]>
            {
                [project] = projectBytes, [implementation] = sourceBuffer.ToArray()
            };
            if (extraPath is not null) files.Add(extraPath, []);
            var changed = Zip(files);
            var observation = receipt.DeepClone();
            Bind(observation, changed);
            observation["source_inventory"]![0]!["sha256"] = Catalog.Digest(projectBytes);
            return Resolve(registry, package, Encoding.UTF8.GetBytes(observation.ToJsonString()), changed);
        }
        var runtimeCases = 0;
        void Case(string name, Action action)
        {
            action();
            runtimeCases++;
            Console.WriteLine($"LP-02 runtime: {name} passed.");
        }
        Case("anonymous-generated-observation", () =>
        {
            using var handler = Lp02SourceAcquisition.CreateHandler();
            Require(!handler.AllowAutoRedirect && !handler.UseCookies && !handler.UseDefaultCredentials &&
                handler.Credentials is null && !handler.UseProxy &&
                handler.AutomaticDecompression == DecompressionMethods.None &&
                handler.CheckCertificateRevocationList && handler.MaxResponseHeadersLength == 16,
                "Production handler disables credentials, cookies, proxies, redirects and decompression");
            using var client = Client(request =>
            {
                Require(request.Method == HttpMethod.Get && request.RequestUri!.AbsoluteUri == record.RetrievalAttempt.Url &&
                    request.Headers.Authorization is null && !request.Headers.Contains("Cookie") &&
                    !request.Headers.Contains("Proxy-Authorization") &&
                    request.Headers.Count() == 1 && request.Headers.UserAgent.ToString() == Lp02PublicSourceResolver.Producer,
                    "Only an anonymous fixed codeload request and user agent are sent");
                return new(HttpStatusCode.OK) { Content = new ByteArrayContent(archive) };
            });
            var generated = Lp02SourceAcquisition.RetrieveAsync(client, package, project, implementation,
                record.MappingRationale).GetAwaiter().GetResult();
            Require(generated.Archive!.SequenceEqual(archive) &&
                generated.Observation.CaptureReceipt!.Sha256 == Catalog.Digest(archive) &&
                generated.Observation.SourceInventory.SequenceEqual(record.SourceInventory),
                "Acquisition retains exact response bytes and generates file digests");
            var result = Resolve(registry, package, JsonSerializer.SerializeToUtf8Bytes(
                generated.Observation, Lp02PublicSourceResolver.JsonOptions), generated.Archive);
            Require(result.BranchId == "public-source-observed" &&
                result.Finding!.Contains("It does not establish that the published binaries were built from that revision.",
                    StringComparison.Ordinal) &&
                result.Finding.Contains("It also does not establish membership in the declared repository's branches or tags, " +
                    "because codeload may serve fork-network commit objects.", StringComparison.Ordinal),
                "Generated receipt reaches only the qualified positive with binary and membership limitations");
        });
        Case("retrieval-result-states", () =>
        {
            foreach (var (status, state) in new[]
            {
                (404, "not-found"), (401, "auth-required"), (403, "auth-required"),
                (429, "blocked"), (451, "blocked"), (302, "unsupported"), (500, "unavailable")
            })
            {
                var requests = 0;
                using var client = Client(_ =>
                {
                    requests++;
                    return new((HttpStatusCode)status) { Content = new ByteArrayContent([]) };
                });
                var generated = Lp02SourceAcquisition.RetrieveAsync(client, package, project, implementation,
                    record.MappingRationale).GetAwaiter().GetResult();
                Require(requests == 1 && generated.Archive is null &&
                    generated.Observation.RetrievalAttempt.Result == state &&
                    generated.Observation.CaptureReceipt is null && generated.Observation.SourceInventory.Length == 0,
                    "No redirect, retry, authenticated fallback or success evidence on unresolved retrieval");
                var result = Resolve(registry, package, JsonSerializer.SerializeToUtf8Bytes(
                    generated.Observation, Lp02PublicSourceResolver.JsonOptions), null);
                Require(result.BranchId == "public-retrieval-unresolved" && result.Decision == "escalated" &&
                    result.Finding is null && result.MissingFact!.Contains("not tested", StringComparison.Ordinal),
                    $"HTTP {status} remains unresolved, never closed-source or a gap");
            }
        });
        Case("transport-failure", () =>
        {
            using var client = Client(_ => throw new HttpRequestException("Synthetic network failure"));
            var generated = Lp02SourceAcquisition.RetrieveAsync(client, package, project, implementation,
                record.MappingRationale).GetAwaiter().GetResult();
            Require(generated.Observation.RetrievalAttempt is { Result: "unavailable", HttpStatus: null } &&
                generated.Archive is null, "Network failure is explicitly retained as unavailable");
        });
        Case("response-header-boundaries", () =>
        {
            foreach (var encoded in new[] { false, true })
            {
                using var client = Client(_ =>
                {
                    var content = new ByteArrayContent([]);
                    if (encoded) content.Headers.ContentEncoding.Add("gzip");
                    else content.Headers.ContentLength = Lp02PublicSourceResolver.MaxArchiveBytes + 1L;
                    return new(HttpStatusCode.OK) { Content = content };
                });
                var generated = Lp02SourceAcquisition.RetrieveAsync(client, package, project, implementation,
                    record.MappingRationale).GetAwaiter().GetResult();
                Require(generated.Observation.RetrievalAttempt.Result == "unsupported" &&
                    generated.Archive is null, "Encoded and oversized responses cannot establish public source");
            }
        });
        Case("streaming-response-boundary", () =>
        {
            using var client = Client(_ => new(HttpStatusCode.OK)
            {
                Content = new StreamContent(new RepeatedStream(Lp02PublicSourceResolver.MaxArchiveBytes + 1L))
            });
            var generated = Lp02SourceAcquisition.RetrieveAsync(client, package, project, implementation,
                record.MappingRationale).GetAwaiter().GetResult();
            Require(generated.Observation.RetrievalAttempt.Result == "unsupported" &&
                generated.Archive is null, "Unknown-length responses stop at the compressed-byte cap");
        });
        Case("observation-byte-boundary", () =>
        {
            var bytes = Encoding.UTF8.GetBytes(receipt.ToJsonString());
            var padded = bytes.Concat(Enumerable.Repeat((byte)' ',
                Lp02PublicSourceResolver.MaxObservationBytes - bytes.Length)).ToArray();
            Require(Resolve(registry, package, padded, archive).BranchId == "public-source-observed",
                "Exactly 16 KiB observation accepted");
            Require(Resolve(registry, package, [.. padded, (byte)' '], archive).BranchId == "observation-invalid",
                "Observation above 16 KiB rejected");
        });
        Case("selected-file-byte-boundary", () =>
        {
            using var zip = new ZipArchive(new MemoryStream(archive));
            using var projectStream = zip.GetEntry(project)!.Open();
            using var memory = new MemoryStream();
            projectStream.CopyTo(memory);
            foreach (var length in new[] { Lp02PublicSourceResolver.MaxSelectedFileBytes,
                Lp02PublicSourceResolver.MaxSelectedFileBytes + 1 })
            {
                var source = Encoding.UTF8.GetBytes("class C {}").Concat(
                    Enumerable.Repeat((byte)' ', length - 10)).ToArray();
                var changed = Zip(new() { [project] = memory.ToArray(), [implementation] = source });
                var observation = receipt.DeepClone();
                Bind(observation, changed);
                observation["source_inventory"]![1]!["sha256"] = Catalog.Digest(source);
                Require(Resolve(registry, package, Encoding.UTF8.GetBytes(observation.ToJsonString()), changed).BranchId ==
                    (length == Lp02PublicSourceResolver.MaxSelectedFileBytes
                        ? "public-source-observed" : "source-relevance-unestablished"),
                    "Selected implementation file must fit the documented inclusive byte cap");
            }
        });
        Case("archive-resource-boundaries", () =>
        {
            Require(Resolve(registry, new byte[Lp02PublicSourceResolver.MaxArchiveBytes + 1],
                null, null).BranchId == "package-unreadable", "Compressed package cap");
            var oversizedManifest = Zip(new() { ["sample.nuspec"] =
                Enumerable.Repeat((byte)' ', Lp02PublicSourceResolver.MaxSelectedFileBytes + 1).ToArray() });
            Require(Resolve(registry, oversizedManifest, null, null).BranchId == "package-unreadable", "Nuspec cap");
            var excessiveEntries = Zip(Enumerable.Range(0, 4097).ToDictionary(
                i => $"file{i}.txt", _ => Array.Empty<byte>()));
            Require(Resolve(registry, excessiveEntries, null, null).BranchId == "package-unreadable", "Entry count cap");
            var inflated = Zip(new() { ["large.bin"] =
                new byte[Lp02PublicSourceResolver.MaxArchiveBytes + 1] }, CompressionLevel.SmallestSize);
            Require(Resolve(registry, inflated, null, null).BranchId == "package-unreadable", "Inflated-byte cap");
        });
        Case("archive-root-and-selected-paths", () =>
        {
            var wrongComment = archive.ToArray();
            wrongComment[^1] = wrongComment[^1] == (byte)'0' ? (byte)'1' : (byte)'0';
            var invalidComment = receipt.DeepClone();
            Bind(invalidComment, wrongComment);
            Require(Resolve(registry, package, Encoding.UTF8.GetBytes(invalidComment.ToJsonString()), wrongComment).BranchId ==
                "observation-invalid", "Codeload ZIP comment must match the exact declared commit");
            var invalid = receipt.DeepClone();
            var changed = Zip(new() { ["wrong-root/file.cs"] = "class C {}"u8.ToArray() });
            Bind(invalid, changed);
            Require(Resolve(registry, package, Encoding.UTF8.GetBytes(invalid.ToJsonString()), changed).BranchId ==
                "observation-invalid", "Source archive root must identify the exact requested commit");
            invalid = receipt.DeepClone();
            invalid["source_inventory"]![1]!["path"] = @"C:\escape.cs";
            Require(Resolve(registry, package, Encoding.UTF8.GetBytes(invalid.ToJsonString()), archive).BranchId ==
                "observation-invalid", "Absolute selected path is unsupported");
        });
        Case("project-and-implementation-mapping", () =>
        {
            foreach (var mode in new[] { "outside", "disabled-defaults", "missing-project" })
            {
                var invalid = receipt.DeepClone();
                using var zip = new ZipArchive(new MemoryStream(archive));
                using var reader = new StreamReader(zip.GetEntry(project)!.Open());
                var xml = reader.ReadToEnd();
                if (mode == "disabled-defaults")
                    xml = xml.Replace("</PropertyGroup>", "<EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup>",
                        StringComparison.Ordinal);
                var sourcePath = mode == "outside" ? project[..project.IndexOf("/src/", StringComparison.Ordinal)] + "/other/Widget.cs" :
                    implementation;
                var files = new Dictionary<string, byte[]> { [sourcePath] = "class C {}"u8.ToArray() };
                if (mode != "missing-project") files[project] = Encoding.UTF8.GetBytes(xml);
                else invalid["source_inventory"]!.AsArray().RemoveAt(0);
                var changed = Zip(files);
                Bind(invalid, changed);
                foreach (var item in invalid["source_inventory"]!.AsArray())
                {
                    if (item!["kind"]!.GetValue<string>() == "implementation") item["path"] = sourcePath;
                    item["sha256"] = Catalog.Digest(files[item["path"]!.GetValue<string>()]);
                }
                Require(Resolve(registry, package, Encoding.UTF8.GetBytes(invalid.ToJsonString()), changed).BranchId ==
                    "source-relevance-unestablished", $"Mapping cannot be established: {mode}");
            }
        });
        Case("duplicate-inventory-kinds", () =>
        {
            var invalid = receipt.DeepClone();
            invalid["source_inventory"]![1]!["kind"] = "project";
            Require(Resolve(registry, package, Encoding.UTF8.GetBytes(invalid.ToJsonString()), archive).BranchId ==
                "observation-invalid", "Duplicate inventory kinds reject without crashing");
        });
        Case("checked-buffer-and-offline-determinism", () =>
        {
            var original = CultureInfo.CurrentCulture;
            try
            {
                byte[]? first = null;
                foreach (var culture in new[] { "en-US", "tr-TR", "fr-FR" })
                {
                    CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
                    var result = Resolve(registry, package, Encoding.UTF8.GetBytes(receipt.ToJsonString()), archive);
                    var serialized = Canonical.Write(result.Write);
                    first ??= serialized;
                    Require(serialized.SequenceEqual(first), "Offline result independent of culture, filesystem and current time");
                }
            }
            finally { CultureInfo.CurrentCulture = original; }
            var identity = new PinnedInput("package", "does-not-exist.nupkg", Catalog.Digest(package));
            try
            {
                registry.Dispatch(new("tampered", "LP-02", [identity],
                    new Dictionary<string, CheckedInput> { ["package"] = new(identity, "changed"u8.ToArray()) }));
                throw new Exception("LP-02 accepted a changed checked buffer");
            }
            catch (InvalidDataException) { }
        });
        Case("unsupported-msbuild-source-mapping", () =>
        {
            var root = project[..project.IndexOf("/src/", StringComparison.Ordinal)];
            var variants = new (string Name, string Xml, string? ExtraPath)[]
            {
                ("lowercase-default-compile", projectXml.Replace("</PropertyGroup>",
                    "<enabledefaultcompileitems>false</enabledefaultcompileitems></PropertyGroup>", StringComparison.Ordinal), null),
                ("mixed-case-default-items", projectXml.Replace("</PropertyGroup>",
                    "<eNaBlEdEfAuLtItEmS>false</eNaBlEdEfAuLtItEmS></PropertyGroup>", StringComparison.Ordinal), null),
                ("lowercase-compile-remove", projectXml.Replace("</Project>",
                    "<ItemGroup><compile remove=\"**/*.cs\" /></ItemGroup></Project>", StringComparison.Ordinal), null),
                ("mixed-case-import", projectXml.Replace("</Project>",
                    "<iMpOrT pRoJeCt=\"other.props\" /></Project>", StringComparison.Ordinal), null),
                ("duplicate-sdk-attribute", projectXml.Replace("Sdk=\"Microsoft.NET.Sdk\"",
                    "Sdk=\"Microsoft.NET.Sdk\" sdk=\"Other.Sdk\"", StringComparison.Ordinal), null),
                ("true-choose-override", projectXml.Replace("</Project>",
                    "<Choose><When Condition=\"'1' == '1'\"><PropertyGroup><PackageId>Other.Library</PackageId>" +
                    "</PropertyGroup></When></Choose></Project>", StringComparison.Ordinal), null),
                ("mixed-case-target-override", projectXml.Replace("</Project>",
                    "<tArGeT nAmE=\"Override\"><PropertyGroup><packageid>Other.Library</packageid>" +
                    "</PropertyGroup></tArGeT></Project>", StringComparison.Ordinal), null),
                ("nested-package-id", projectXml.Replace("</Project>",
                    "<Wrapper><PropertyGroup><PackageId>Other.Library</PackageId></PropertyGroup></Wrapper></Project>",
                    StringComparison.Ordinal), null),
                ("competing-case-package-id", projectXml.Replace("</PropertyGroup>",
                    "<packageid>Other.Library</packageid></PropertyGroup>", StringComparison.Ordinal), null),
                ("mixed-case-condition", projectXml.Replace("<PropertyGroup>",
                    "<PropertyGroup cOnDiTiOn=\"false\">", StringComparison.Ordinal), null),
                ("default-item-excludes", projectXml.Replace("</PropertyGroup>",
                    "<dEfAuLtItEmExClUdEs>**/*.cs</dEfAuLtItEmExClUdEs></PropertyGroup>", StringComparison.Ordinal), null),
                ("root-directory-build-props", projectXml, root + "/Directory.Build.props"),
                ("ancestor-directory-build-targets", projectXml, root + "/src/directory.build.targets"),
                ("project-directory-build-props", projectXml, project[..(project.LastIndexOf('/') + 1)] + "DIRECTORY.BUILD.PROPS")
            };
            var outcomes = variants.Select(item => (item.Name,
                Result: ProjectResult(Encoding.UTF8.GetBytes(item.Xml), item.ExtraPath))).ToArray();
            foreach (var item in outcomes)
                Console.WriteLine($"LP-02 acceptance {item.Name}: {item.Result.BranchId}.");
            Require(outcomes.All(item => item.Result.BranchId == "source-relevance-unestablished" &&
                item.Result.Decision == "escalated" && item.Result.Finding is null),
                "Unsupported case variants, conditional/competing identities, exclusions and ancestor imports cannot establish relevance");
            Require(ProjectResult(Encoding.UTF8.GetBytes(projectXml), root + "/other/Directory.Build.props").BranchId ==
                "public-source-observed", "Only Directory.Build files in the selected project's ancestors block relevance");
        });
        Case("malformed-observation-unicode", () =>
        {
            var valid = Encoding.UTF8.GetBytes(receipt.ToJsonString());
            var variants = new[]
            {
                (Name: "invalid-utf8-property", Bytes: (byte[])[.. valid[..^1], .. ",\""u8.ToArray(), 0xff, .. "\":0}"u8.ToArray()]),
                (Name: "invalid-utf8-value", Bytes: (byte[])[.. valid[..^1], .. ",\"unknown\":\""u8.ToArray(), 0xff, .. "\"}"u8.ToArray()]),
                (Name: "unpaired-property-surrogate", Bytes: Encoding.UTF8.GetBytes(
                    Encoding.UTF8.GetString(valid)[..^1] + ",\"\\uD800\":0}")),
                (Name: "unpaired-known-value-surrogate", Bytes: Encoding.UTF8.GetBytes(
                    receipt.ToJsonString().Replace(record.MappingRationale, "\\uD800", StringComparison.Ordinal)))
            };
            foreach (var item in variants)
                Require(Resolve(registry, package, item.Bytes, archive).BranchId == "observation-invalid",
                    $"Malformed JSON Unicode must reject without an unhandled exception: {item.Name}");
        });
        Case("project-utf8-bom", () =>
        {
            var bytes = Encoding.UTF8.GetBytes(projectXml);
            var bom = new byte[] { 0xef, 0xbb, 0xbf };
            Require(ProjectResult([.. bom, .. bytes]).BranchId == "public-source-observed",
                "A single UTF-8 BOM on an otherwise supported project preserves relevance");
            Require(ProjectResult([.. bom, .. bom, .. bytes]).BranchId == "source-relevance-unestablished",
                "Repeated BOMs are not a supported project encoding");
        });
        Require(runtimeCases == 15, "15 focused runtime cases; 48 total named synthetic cases");
        Retained(registry, directory);
        Console.WriteLine("LP-02: 33 frozen oracle cases + 15 runtime cases passed (network-free).");
    }

    private static void Retained(Registry registry, string directory)
    {
        var syncfusion = Environment.GetEnvironmentVariable("LP02_SYNCFUSION_NUPKG");
        var infragistics = Environment.GetEnvironmentVariable("LP02_INFRAGISTICS_NUPKG");
        Require((syncfusion is null) == (infragistics is null), "Both LP-02 retained inputs must be supplied together");
        using var retained = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory, "..", "lp01", "retained-inputs.json")));
        if (syncfusion is null)
        {
            Console.WriteLine("LP-02 retained replay: not run; both exact vendor packages must be supplied.");
            return;
        }
        var staging = Path.Combine(Path.GetTempPath(), "lp02-retained-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(staging, "packages"));
        try
        {
            File.Copy(Path.Combine(directory, "acceptance.json"), Path.Combine(staging, "acceptance.json"));
            foreach (var (path, vendor) in new[] { (syncfusion, retained.RootElement.GetProperty("vendors")[0]),
                (infragistics!, retained.RootElement.GetProperty("vendors")[1]) })
            {
                var bytes = File.ReadAllBytes(path);
                Require(bytes.Length == vendor.GetProperty("size_bytes").GetInt32() &&
                    Catalog.Digest(bytes) == vendor.GetProperty("sha256").GetString(), "Exact retained package identity");
                File.WriteAllBytes(Path.Combine(staging, "packages",
                    $"{vendor.GetProperty("package_id").GetString()}.{vendor.GetProperty("version").GetString()}.nupkg"), bytes);
            }
            var replay = Replay.Run(Path.Combine(staging, "acceptance.json"), registry);
            Require(replay.SequenceEqual(Replay.Run(Path.Combine(staging, "acceptance.json"), registry)),
                "Retained replay determinism");
            using var results = JsonDocument.Parse(replay);
            Require(results.RootElement.GetProperty("cases").GetArrayLength() == 2, "Two actual retained results");
            foreach (var item in results.RootElement.GetProperty("cases").EnumerateArray())
            {
                var result = item.GetProperty("result");
                Require(result.GetProperty("branch_id").GetString() == "observation-missing" &&
                    result.GetProperty("decision").GetString() == "escalated" &&
                    result.GetProperty("finding").ValueKind == JsonValueKind.Null,
                    "Retained declared-source-unverified escalation");
                Console.WriteLine($"LP-02 retained {item.GetProperty("case_id").GetString()}: " +
                    "declared-source-unverified (observation-missing).");
            }
            Console.WriteLine($"LP-02 retained replay: 2 escalations, SHA-256 {Catalog.Digest(replay)}.");
        }
        finally { Directory.Delete(staging, true); }
    }

    private static CaseResult Resolve(Registry registry, byte[] package, byte[]? observation, byte[]? archive)
    {
        var inputs = new List<PinnedInput>();
        var checkedInputs = new Dictionary<string, CheckedInput>(StringComparer.Ordinal);
        foreach (var (role, data) in new[] { ("package", package), ("source-observation", observation), ("source-archive", archive) })
        {
            if (data is null) continue;
            var identity = new PinnedInput(role, "unopened-" + role + ".bin", Catalog.Digest(data));
            inputs.Add(identity);
            checkedInputs.Add(role, new(identity, data));
        }
        return registry.Dispatch(new("runtime", "LP-02", inputs, checkedInputs));
    }

    private static void Bind(JsonNode receipt, byte[] archive) =>
        receipt["capture_receipt"]!["sha256"] = Catalog.Digest(archive);

    private static byte[] Zip(Dictionary<string, byte[]> files, CompressionLevel compression = CompressionLevel.NoCompression)
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, true))
            foreach (var (name, data) in files)
            {
                using var stream = zip.CreateEntry(name, compression).Open();
                stream.Write(data);
            }
        return buffer.ToArray();
    }

    private static HttpClient Client(Func<HttpRequestMessage, HttpResponseMessage> respond) =>
        new(new StubHandler(respond));

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }

    private sealed class RepeatedStream(long length) : Stream
    {
        private long remaining = length;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count)
        {
            var size = (int)Math.Min(count, remaining);
            buffer.AsSpan(offset, size).Clear();
            remaining -= size;
            return size;
        }
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception("LP-02: " + message);
    }
}
