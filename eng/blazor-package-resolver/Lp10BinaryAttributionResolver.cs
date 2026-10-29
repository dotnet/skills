using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace BlazorPackageResolver;

public sealed class Lp10BinaryAttributionResolver : IDeterministicResolver
{
    private const int PackageLimit = 64 * 1024 * 1024;
    private const int DirectoryLimit = 8 * 1024 * 1024;
    private const int NuspecLimit = 1024 * 1024;
    private const int EntryLimit = 8 * 1024 * 1024;
    private const int TotalLimit = 32 * 1024 * 1024;
    private const int XmlLimit = 256 * 1024;
    private const int XmlDepthLimit = 32;
    private const int XmlNodeLimit = 4096;
    private const int SnippetLimit = 160;
    private const int ImportDepthLimit = 8;
    private const int ImportCountLimit = 32;
    private const string MsbuildNamespace = "http://schemas.microsoft.com/developer/msbuild/2003";
    private const string NoCodeFinding =
        "No executable/code payload candidates were found among the safely classified entries; LP-10 holds vacuously for this checked package only.";
    private const string NoCodeContract =
        "All checked non-manifest entries matched the bounded passive/declaration allowlist.";
    private const string CandidatePrefix =
        "Checked code candidates (exact bytes hashed; locator snippets are display-only): ";
    private const string CandidateSuffix =
        ". These checked bytes do not establish ownership, release-commit correspondence or upstream.";

    private static readonly HashSet<string> KnownNamespaces = new(StringComparer.Ordinal)
    {
        "",
        "http://schemas.microsoft.com/packaging/2010/07/nuspec.xsd",
        "http://schemas.microsoft.com/packaging/2011/08/nuspec.xsd",
        "http://schemas.microsoft.com/packaging/2011/10/nuspec.xsd",
        "http://schemas.microsoft.com/packaging/2012/06/nuspec.xsd",
        "http://schemas.microsoft.com/packaging/2013/01/nuspec.xsd",
        "http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd"
    };
    private static readonly string[] MsbuildSuffixes =
        [".props", ".targets", ".proj", ".tasks", ".overridetasks"];
    private static readonly string[] ScriptSuffixes = [".ps1", ".psm1", ".sh", ".cmd", ".bat"];
    private static readonly string[] SourceSuffixes =
        [".pp", ".razor", ".ts", ".mts", ".cts", ".cs", ".fs", ".vb", ".cpp",
         ".c", ".h", ".hpp", ".java", ".py"];
    private static readonly string[] CompiledSuffixes =
        [".dll", ".exe", ".so", ".dylib", ".a", ".wasm", ".class", ".pyd"];
    private static readonly string[] DeclarationSuffixes = [".d.ts", ".d.mts", ".d.cts"];
    private static readonly string[] PassiveSuffixes =
        [".md", ".txt", ".css", ".png", ".jpg", ".jpeg", ".gif",
         ".svg", ".pdb", ".p7s", ".map", ".psmdcp", ".d.ts", ".d.mts", ".d.cts"];
    private static readonly HashSet<string> SupportJson = new(StringComparer.Ordinal)
        { "package.json", "sbom.json", "provenance.json", "sourcelink.json" };
    private static readonly Regex UnsafeXmlToken = new(
        @"<!\s*(?:DOCTYPE|ENTITY)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex PropertyFunction = new(
        @"\$\(\s*(?:\[[^\]]+\]\s*::|[A-Za-z_]\w*(?:\.[A-Za-z_]\w*)+\s*\()",
        RegexOptions.CultureInvariant);
    private static readonly Regex DeclarativeFullPath = new(
        @"\A\$\(\[System\.IO\.Path\]::GetFullPath\('\$\(MSBuildThisFileDirectory\)([A-Za-z0-9_.\-/\\]+)'\)\)\z",
        RegexOptions.CultureInvariant);
    private static readonly JsonSerializerOptions PrettyJson = new()
    {
        WriteIndented = true,
        NewLine = "\n",
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public string RowId => "LP-10";
    public string ResolverId => "lp10-binary-attribution";
    public string Version => "1.0.0";

    public CaseResult Resolve(ResolverCase input)
    {
        if (input.Inputs.Count == 0)
            return Result(input, "missing-package", []);
        if (input.Inputs.Count != 1 || input.Inputs[0].Role != "package" ||
            !input.CheckedInputs.TryGetValue("package", out var package))
            throw new InvalidDataException("LP-10 requires exactly one checked package input.");
        var scanned = ScanArchive(package.Content);
        return Result(input, scanned.Branch, scanned.Records);
    }

    private static CaseResult Result(ResolverCase input, string branch, List<EntryRecord> records)
    {
        if (branch == "no-code-payload")
            return new("LP-10", "lp10-binary-attribution", "1.0.0", branch,
                "deterministic", "applicable", input.Inputs,
                ["package:archive", "package:nuspec/metadata"], NoCodeContract,
                NoCodeFinding, null, null, null);

        var (missing, required, category) = branch switch
        {
            "missing-package" => ("The package bytes required for LP-10 were not supplied.",
                "package", "input-owner"),
            "unreadable-package" => ("The supplied checked bytes are not a readable NuGet archive.",
                "valid-nupkg", "package-owner"),
            "unsafe-archive" => ("Unsafe, aliased or duplicate ZIP entry paths prevent an unambiguous safe inventory.",
                "safe-unambiguous-nupkg", "package-owner"),
            "ambiguous-nuspec" => ("The archive has multiple root nuspec entries.",
                "unique-root-nuspec", "package-owner"),
            "missing-nuspec" => ("The archive has no nuspec entry.",
                "root-nuspec", "package-owner"),
            "missing-root-nuspec" => ("The only nuspec entry is nested in a subfolder.",
                "root-nuspec", "package-owner"),
            "unsafe-nuspec" => ("The nuspec has a DTD/entity declaration or unsupported encoding and cannot be safely inspected.",
                "safe-nuspec", "package-owner"),
            "invalid-xml" => ("The nuspec XML is not well formed.",
                "valid-nuspec", "package-owner"),
            "foreign-root" => ("The package root is not in a recognized NuGet namespace.",
                "recognized-nuspec", "package-owner"),
            "invalid-metadata" => ("The nuspec requires exactly one recognized metadata element.",
                "valid-nuspec", "package-owner"),
            "ambiguous-metadata" => ("A foreign-namespace metadata sibling makes authoritative NuGet metadata ambiguous across readers.",
                "unambiguous-nuspec", "package-owner"),
            "incomplete-inventory" => ("A required entry or build XML exceeds inspection limits or cannot be safely parsed; the package inventory is incomplete.",
                "bounded-safely-inspectable-package", "package-owner"),
            "unsupported-executable-content" => ("An executable-looking payload or active build/markup construct cannot be safely classified for attribution.",
                "safely-inspectable-payload-and-per-entry-source-map", "package-owner"),
            "compiled-attribution-unverified" => ("Packaged compiled code lacks verified per-entry ownership and correspondence to public release-commit builds or a referenced NuGet/npm upstream.",
                "per-entry-owner-and-public-release-commit-build-or-upstream-byte-map", "source-owner"),
            "text-code-attribution-unverified" => ("Packaged executable source-form text lacks verified per-entry ownership and exact release-commit blob correspondence, a pinned reproducible build/transform, or referenced-upstream byte correspondence.",
                "per-entry-owner-and-public-release-commit-blob-or-verified-transform-or-upstream-byte-map", "source-owner"),
            "mixed-code-attribution-unverified" => ("Packaged compiled code and executable source-form text each lack verified per-entry source or referenced-upstream correspondence.",
                "per-entry-compiled-build-and-text-blob-or-verified-transform-or-upstream-byte-maps", "source-owner"),
            _ => throw new InvalidDataException($"Unknown LP-10 branch: {branch}")
        };
        var structural = branch is "missing-package" or "unreadable-package" or "unsafe-archive" or
            "ambiguous-nuspec" or "missing-nuspec" or "missing-root-nuspec" or "unsafe-nuspec" or
            "invalid-xml" or "foreign-root" or "invalid-metadata" or "ambiguous-metadata" or
            "incomplete-inventory";
        return new("LP-10", "lp10-binary-attribution", "1.0.0", branch, "escalated",
            structural ? "unknown" : "applicable", input.Inputs,
            branch == "missing-package" ? [] : ["package:archive"],
            structural || records.Count == 0 ? null : CandidateContract(records),
            null, missing, required, category);
    }

    private sealed record EntryRecord(string Path, long SizeBytes, string Sha256,
        string Classification, string Reason, List<Locator> Locators);
    private sealed record Locator(string Name, Snippet Snippet);
    private sealed record Snippet(string Text, bool Truncated);
    private sealed record Classified(string Kind, string Reason, List<Locator> Locators,
        List<string> Promotions, List<string> Imports);
    private sealed record Inventory(string Branch, List<EntryRecord> Records);
    private sealed class XmlProblem(string reason) : Exception(reason);

    private static string CandidateContract(List<EntryRecord> records)
    {
        var candidates = records.Where(item => item.Classification != "passive").ToArray();
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer,
            new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            writer.WriteStartArray();
            foreach (var item in candidates)
            {
                writer.WriteStartObject();
                writer.WriteString("classification", item.Classification);
                writer.WritePropertyName("first_locator");
                if (item.Locators.Count == 0)
                    writer.WriteNullValue();
                else
                    writer.WriteStringValue(item.Locators[0].Name);
                writer.WriteNumber("locator_count", item.Locators.Count);
                writer.WriteString("locators_sha256", LocatorDigest(item.Locators));
                writer.WriteString("path", item.Path);
                writer.WriteString("reason", item.Reason);
                writer.WriteString("sha256", item.Sha256);
                writer.WriteNumber("size_bytes", item.SizeBytes);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
        }
        return CandidatePrefix + AsciiJson(Encoding.UTF8.GetString(buffer.ToArray())) +
            CandidateSuffix;
    }

    private static string LocatorDigest(List<Locator> locators) =>
        Catalog.Digest(Encoding.UTF8.GetBytes(AsciiJson(JsonSerializer.Serialize(
            locators.Select(item => new
            {
                locator = item.Name,
                snippet = new { text = item.Snippet.Text, truncated = item.Snippet.Truncated }
            }).ToArray(), PrettyJson)) + "\n"));

    private static string AsciiJson(string json)
    {
        var result = new StringBuilder(json.Length);
        foreach (var character in json)
        {
            if (character > 127)
                result.Append("\\u").Append(((int)character).ToString("x4",
                    System.Globalization.CultureInfo.InvariantCulture));
            else
                result.Append(character);
        }
        return result.ToString();
    }

    private static Inventory Structural(string branch) => new(branch, []);

    private static Inventory ScanArchive(ReadOnlyMemory<byte> content)
    {
        if (content.Length > PackageLimit || !TryRawNames(content.Span, out var names))
            return Structural("unreadable-package");
        if (names.Any(name => !SafePath(name)) ||
            names.Select(AsciiLower).Distinct(StringComparer.Ordinal).Count() != names.Count)
            return Structural("unsafe-archive");
        try
        {
            using var stream = new MemoryStream(content.ToArray(), writable: false);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
            var entries = archive.Entries;
            if (entries.Count != names.Count || entries.Count > 4096 ||
                entries.Any(entry => !SafePath(entry.FullName)) ||
                entries.Select(entry => AsciiLower(entry.FullName))
                    .Distinct(StringComparer.Ordinal).Count() != entries.Count)
                return Structural("unsafe-archive");
            if (entries.Where((entry, index) => entry.FullName != names[index]).Any())
                return Structural("unreadable-package");
            var roots = entries.Where(entry =>
                entry.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase) &&
                !entry.FullName.Contains('/')).ToArray();
            if (roots.Length > 1)
                return Structural("ambiguous-nuspec");
            if (roots.Length == 0)
                return Structural(entries.Any(entry => entry.FullName.EndsWith(
                    ".nuspec", StringComparison.OrdinalIgnoreCase))
                    ? "missing-root-nuspec" : "missing-nuspec");
            if (roots[0].Length > NuspecLimit)
                return Structural("incomplete-inventory");
            var nuspec = ReadEntry(roots[0], NuspecLimit);
            if (nuspec is null)
                return Structural("unreadable-package");
            XElement root;
            try
            {
                root = ReadXml(nuspec, NuspecLimit, "nuspec");
            }
            catch (XmlProblem error)
            {
                return Structural(error.Message is "nuspec-encoding" or "nuspec-dtd"
                    ? "unsafe-nuspec" : "invalid-xml");
            }
            if (root.Name.LocalName != "package" ||
                !KnownNamespaces.Contains(root.Name.NamespaceName))
                return Structural("foreign-root");
            var metadata = root.Elements().Where(child => child.Name.LocalName == "metadata").ToArray();
            var recognized = metadata.Where(child => child.Name.Namespace == root.Name.Namespace).ToArray();
            if (recognized.Length > 0 && recognized.Length != metadata.Length)
                return Structural("ambiguous-metadata");
            if (recognized.Length != 1)
                return Structural("invalid-metadata");

            var others = entries.Where(entry => entry != roots[0]).ToArray();
            if (others.Any(entry => entry.Length > EntryLimit) ||
                others.Sum(entry => entry.Length) > TotalLimit)
                return Structural("incomplete-inventory");
            var contents = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            foreach (var entry in others)
            {
                var bytes = ReadEntry(entry, EntryLimit);
                if (bytes is null)
                    return Structural("incomplete-inventory");
                contents.Add(entry.FullName, bytes);
            }
            return InspectEntries(contents);
        }
        catch (InvalidDataException)
        {
            return Structural("unreadable-package");
        }
        catch (NotSupportedException)
        {
            return Structural("unreadable-package");
        }
        catch (IOException)
        {
            return Structural("unreadable-package");
        }
    }

    private static byte[]? ReadEntry(ZipArchiveEntry entry, int limit)
    {
        using var stream = entry.Open();
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int read;
        while ((read = stream.Read(chunk)) > 0)
        {
            if (buffer.Length + read > limit)
                return null;
            buffer.Write(chunk, 0, read);
        }
        if (buffer.Length != entry.Length)
            return null;
        var content = buffer.ToArray();
        if (Crc32(content) != entry.Crc32)
            throw new InvalidDataException("ZIP entry CRC mismatch.");
        return content;
    }

    private static uint Crc32(ReadOnlySpan<byte> content)
    {
        var crc = uint.MaxValue;
        foreach (var value in content)
        {
            crc ^= value;
            for (var index = 0; index < 8; index++)
                crc = (crc >> 1) ^ ((crc & 1) == 1 ? 0xedb88320u : 0u);
        }
        return ~crc;
    }

    private static bool TryRawNames(ReadOnlySpan<byte> content, out List<string> names)
    {
        names = [];
        for (var end = content.Length - 22; end >= Math.Max(0, content.Length - 65557); end--)
        {
            if (!content.Slice(end, 4).SequenceEqual("PK\x05\x06"u8) ||
                end + 22 + BinaryPrimitives.ReadUInt16LittleEndian(content[(end + 20)..]) != content.Length)
                continue;
            var count = BinaryPrimitives.ReadUInt16LittleEndian(content[(end + 10)..]);
            var directorySize = BinaryPrimitives.ReadUInt32LittleEndian(content[(end + 12)..]);
            var cursor = BinaryPrimitives.ReadUInt32LittleEndian(content[(end + 16)..]);
            if (count > 4096 || directorySize > DirectoryLimit ||
                (ulong)cursor + directorySize > (ulong)end)
                return false;
            var start = cursor;
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            for (var index = 0; index < count; index++)
            {
                if ((ulong)cursor + 46 > (ulong)content.Length ||
                    !content.Slice((int)cursor, 4).SequenceEqual("PK\x01\x02"u8))
                    return false;
                var flags = BinaryPrimitives.ReadUInt16LittleEndian(content[((int)cursor + 8)..]);
                var nameLength = BinaryPrimitives.ReadUInt16LittleEndian(content[((int)cursor + 28)..]);
                var extraLength = BinaryPrimitives.ReadUInt16LittleEndian(content[((int)cursor + 30)..]);
                var commentLength = BinaryPrimitives.ReadUInt16LittleEndian(content[((int)cursor + 32)..]);
                if ((ulong)cursor + 46u + nameLength + extraLength + commentLength > (ulong)content.Length)
                    return false;
                var bytes = content.Slice((int)cursor + 46, nameLength);
                try
                {
                    names.Add((flags & 0x800) != 0
                        ? StrictUtf8.GetString(bytes) : Encoding.GetEncoding(437).GetString(bytes));
                }
                catch (DecoderFallbackException)
                {
                    return false;
                }
                cursor += (uint)(46 + nameLength + extraLength + commentLength);
            }
            return cursor == start + directorySize;
        }
        return false;
    }

    private static bool SafePath(string name)
    {
        if (name.Length is 0 or > 512 || name.StartsWith('/') || name.Contains('\\') ||
            name.Contains('\0') || name.EndsWith('/') ||
            Regex.IsMatch(name, @"\A[A-Za-z]:", RegexOptions.CultureInvariant))
            return false;
        return name.Split('/').All(part => part is not ("" or "." or ".."));
    }

    private static string AsciiLower(string value) =>
        string.Create(value.Length, value, (output, source) =>
        {
            for (var index = 0; index < source.Length; index++)
                output[index] = source[index] is >= 'A' and <= 'Z'
                    ? (char)(source[index] + ('a' - 'A')) : source[index];
        });

    private static Inventory InspectEntries(Dictionary<string, byte[]> contents)
    {
        var available = contents.Keys.ToHashSet(StringComparer.Ordinal);
        var records = new Dictionary<string, EntryRecord>(StringComparer.Ordinal);
        var imports = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var promotions = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in contents.Keys.Order(StringComparer.Ordinal))
        {
            var raw = contents[name];
            var classified = Classify(name, raw, available);
            if (classified.Kind == "incomplete")
                return Structural("incomplete-inventory");
            promotions.UnionWith(classified.Promotions);
            imports[name] = classified.Imports;
            records[name] = new(name, raw.Length, Catalog.Digest(raw), classified.Kind,
                classified.Reason, classified.Locators);
        }
        var importCount = imports.Values.Sum(items => items.Count);
        if (importCount > ImportCountLimit)
            return Structural("incomplete-inventory");

        void Follow(string name, HashSet<string> stack)
        {
            if (stack.Contains(name) || stack.Count >= ImportDepthLimit)
                throw new XmlProblem("build-import-cycle-or-depth");
            foreach (var target in imports[name])
            {
                if (!stack.Contains(target) && !records[target].Reason.StartsWith(
                    "msbuild-", StringComparison.Ordinal))
                {
                    var classified = Classify(target, contents[target], available, forceMsbuild: true);
                    if (classified.Kind == "incomplete")
                        throw new XmlProblem("build-import-target-" + classified.Reason);
                    promotions.UnionWith(classified.Promotions);
                    importCount += classified.Imports.Count;
                    if (importCount > ImportCountLimit)
                        throw new XmlProblem("build-import-count");
                    imports[target] = classified.Imports;
                    records[target] = records[target] with
                    {
                        Classification = classified.Kind, Reason = classified.Reason,
                        Locators = classified.Locators
                    };
                }
                var next = new HashSet<string>(stack, StringComparer.Ordinal) { name };
                Follow(target, next);
            }
        }

        try
        {
            foreach (var name in contents.Keys.Order(StringComparer.Ordinal))
                if (imports[name].Count > 0)
                    Follow(name, new(StringComparer.Ordinal));
        }
        catch (XmlProblem)
        {
            return Structural("incomplete-inventory");
        }
        foreach (var name in promotions)
            if (records[name].Classification == "passive")
                records[name] = records[name] with
                {
                    Classification = "text", Reason = "promoted-in-package-code-source"
                };
        var ordered = records.Values.OrderBy(item => item.Path, StringComparer.Ordinal).ToList();
        var compiled = ordered.Any(item => item.Classification == "compiled");
        var text = ordered.Any(item => item.Classification == "text");
        var branch = ordered.Any(item => item.Classification == "unsupported")
            ? "unsupported-executable-content"
            : compiled && text ? "mixed-code-attribution-unverified"
            : compiled ? "compiled-attribution-unverified"
            : text ? "text-code-attribution-unverified"
            : "no-code-payload";
        return new(branch, ordered);
    }

    private static Classified ClassifiedAs(string kind, string reason) =>
        new(kind, reason, [], [], []);

    private static Classified Classify(string name, byte[] raw,
        HashSet<string> available, bool forceMsbuild = false)
    {
        var lower = AsciiLower(name);
        var content = raw.AsSpan();
        var peOffset = content.Length >= 0x40
            ? BinaryPrimitives.ReadUInt32LittleEndian(content[0x3c..]) : uint.MaxValue;
        if (content.StartsWith("MZ"u8) &&
            (ulong)peOffset + 4 <= (ulong)content.Length &&
            content.Slice((int)peOffset, 4).SequenceEqual("PE\0\0"u8))
            return ClassifiedAs("compiled", "pe-signature");
        if (content.StartsWith("MZ"u8))
            return ClassifiedAs("unsupported", "incomplete-pe-signature");
        if (content.StartsWith(new byte[] { 0x7f, (byte)'E', (byte)'L', (byte)'F' }))
            return ClassifiedAs("compiled", "elf-signature");
        if (content.StartsWith(new byte[] { 0xcf, 0xfa, 0xed, 0xfe }) ||
            content.StartsWith(new byte[] { 0xfe, 0xed, 0xfa, 0xcf }) ||
            content.StartsWith(new byte[] { 0xce, 0xfa, 0xed, 0xfe }) ||
            content.StartsWith(new byte[] { 0xfe, 0xed, 0xfa, 0xce }) ||
            content.StartsWith(new byte[] { 0xca, 0xfe, 0xba, 0xbe }) ||
            content.StartsWith(new byte[] { 0xbe, 0xba, 0xfe, 0xca }))
            return ClassifiedAs("compiled", "macho-signature");
        if (content.StartsWith("\0asm\x01\0\0\0"u8))
            return ClassifiedAs("compiled", "wasm-signature");
        (string Label, byte[] Bytes)[] magic =
        [
            ("zip", [0x50, 0x4b, 0x03, 0x04]), ("gzip", [0x1f, 0x8b]),
            ("ar", "!<arch>\n"u8.ToArray()), ("cfb", [0xd0, 0xcf, 0x11, 0xe0, 0xa1, 0xb1, 0x1a, 0xe1]),
            ("cab", "MSCF"u8.ToArray()), ("7z", [0x37, 0x7a, 0xbc, 0xaf, 0x27, 0x1c]),
            ("xz", [0xfd, 0x37, 0x7a, 0x58, 0x5a, 0x00]),
            ("zstd", [0x28, 0xb5, 0x2f, 0xfd]), ("bzip2", "BZh"u8.ToArray()),
            ("rar4", [0x52, 0x61, 0x72, 0x21, 0x1a, 0x07, 0x00]),
            ("rar5", [0x52, 0x61, 0x72, 0x21, 0x1a, 0x07, 0x01, 0x00])
        ];
        foreach (var (label, marker) in magic)
            if (content.StartsWith(marker))
                return ClassifiedAs("unsupported", label + "-container");
        if (content.Length >= 262 && content[257..].StartsWith("ustar"u8))
            return ClassifiedAs("unsupported", "tar-container");
        if (content.StartsWith("#!"u8) ||
            content.StartsWith(new byte[] { 0xef, 0xbb, 0xbf, 0x23, 0x21 }))
        {
            try
            {
                StrictUtf8.GetString(content);
                return ClassifiedAs("text", "shebang-at-start");
            }
            catch (DecoderFallbackException)
            {
                return ClassifiedAs("unsupported", "shebang-unsupported-encoding");
            }
        }
        if (EndsWithAny(lower, [".js.gz", ".js.br", ".js.zip", ".js.nupkg"]))
            return ClassifiedAs("unsupported", "precompressed-code");

        bool project;
        try
        {
            project = !forceMsbuild && !EndsWithAny(lower, MsbuildSuffixes) &&
                StartsProject(raw);
        }
        catch (XmlProblem error)
        {
            return ClassifiedAs("incomplete", error.Message);
        }
        if (forceMsbuild || EndsWithAny(lower, MsbuildSuffixes) || project)
        {
            try
            {
                var (kind, locators, promotions, imports) = InspectMsbuild(name, raw, available);
                return new(kind, "msbuild-" + (kind == "passive" ? "declaration-only" :
                    kind == "unsupported" ? "unsupported" : "active"), locators, promotions, imports);
            }
            catch (XmlProblem error)
            {
                return ClassifiedAs("incomplete", error.Message);
            }
        }
        if (EndsWithAny(lower, [".html", ".svg", ".xslt", ".xsl", ".ps1xml"]))
        {
            try
            {
                var (kind, locators) = InspectMarkup(raw);
                return new(kind, "markup-" + (kind == "text" ? "active" : "declaration-only"),
                    locators, [], []);
            }
            catch (XmlProblem error)
            {
                return ClassifiedAs("unsupported", "markup-" + error.Message);
            }
        }
        if (EndsWithAny(lower, [".js", ".mjs", ".cjs", .. ScriptSuffixes]))
        {
            try
            {
                StrictUtf8.GetString(content);
                return ClassifiedAs("text", "executable-source-text");
            }
            catch (DecoderFallbackException)
            {
                return ClassifiedAs("unsupported", "code-unsupported-encoding");
            }
        }
        if (EndsWithAny(lower, DeclarationSuffixes))
            return ClassifiedAs("passive", "declaration-types");
        if (EndsWithAny(lower, CompiledSuffixes))
            return ClassifiedAs("unsupported", "misleading-compiled-suffix");
        if (EndsWithAny(lower, SourceSuffixes))
            return ClassifiedAs("unsupported", "consumer-source-unsupported");
        if (lower == "[content_types].xml" || lower.EndsWith(".psmdcp", StringComparison.Ordinal) ||
            (lower.EndsWith(".xml", StringComparison.Ordinal) &&
             lower.StartsWith("lib/", StringComparison.Ordinal) &&
             content[..Math.Min(512, content.Length)].StartsWith("<?xml version=\"1.0\"?>"u8)))
            return ClassifiedAs("passive", "explicit-xml-metadata");
        if (lower.EndsWith(".json", StringComparison.Ordinal) &&
            (SupportJson.Contains(lower[(lower.LastIndexOf('/') + 1)..]) ||
             lower.Contains("attestations/", StringComparison.Ordinal) ||
             lower.Contains("sources/", StringComparison.Ordinal)))
            return ClassifiedAs("passive", "supporting-json-claim");
        if (lower is "_rels/.rels" or "license" or "notice" ||
            EndsWithAny(lower, PassiveSuffixes))
            return ClassifiedAs("passive", "explicit-passive-asset");
        return ClassifiedAs("unsupported", content.Length > 0
            ? "unknown-nonempty-payload" : "unknown-empty-entry");
    }

    private static bool EndsWithAny(string value, string[] endings) =>
        endings.Any(ending => value.EndsWith(ending, StringComparison.Ordinal));

    private static string XmlText(byte[] content, int limit, string label)
    {
        if (content.Length > limit)
            throw new XmlProblem(label + "-byte-limit");
        Encoding encoding = StrictUtf8;
        var start = 0;
        var allowed = new[] { "utf-8" };
        if (content.AsSpan().StartsWith(new byte[] { 0xff, 0xfe }))
        {
            encoding = new UnicodeEncoding(false, false, true);
            start = 2;
            allowed = ["utf-16", "utf-16le"];
        }
        else if (content.AsSpan().StartsWith(new byte[] { 0xfe, 0xff }))
        {
            encoding = new UnicodeEncoding(true, false, true);
            start = 2;
            allowed = ["utf-16", "utf-16be"];
        }
        else if (content.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf }))
            start = 3;
        else if (content.Contains((byte)0))
            throw new XmlProblem(label + "-encoding");
        string text;
        try
        {
            text = encoding.GetString(content.AsSpan(start));
        }
        catch (DecoderFallbackException)
        {
            throw new XmlProblem(label + "-encoding");
        }
        if (text.Contains('\0') || text.StartsWith('\ufeff'))
            throw new XmlProblem(label + "-encoding");
        var declaration = Regex.Match(text, @"\A\s*<\?xml(?=\s)([^?]*)\?>",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (declaration.Success)
        {
            var declared = Regex.Match(declaration.Groups[1].Value,
                "\\bencoding\\s*=\\s*[\"']([^\"']+)[\"']",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (declared.Success && !allowed.Contains(
                declared.Groups[1].Value.ToLowerInvariant(), StringComparer.Ordinal))
                throw new XmlProblem(label + "-encoding");
        }
        return text;
    }

    private static bool HasUnsafeXmlDeclaration(string text)
    {
        if (!UnsafeXmlToken.IsMatch(text))
            return false;
        var position = 0;
        while ((position = text.IndexOf('<', position)) >= 0)
        {
            var opening = text.AsSpan(position);
            var (prefix, terminator) =
                opening.StartsWith("<!--", StringComparison.Ordinal) ? ("<!--", "-->") :
                opening.StartsWith("<![CDATA[", StringComparison.Ordinal) ? ("<![CDATA[", "]]>") :
                opening.StartsWith("<?", StringComparison.Ordinal) ? ("<?", "?>") : ("", "");
            if (prefix.Length != 0)
            {
                var end = text.IndexOf(terminator, position + prefix.Length,
                    StringComparison.Ordinal);
                if (end < 0)
                    return false;
                position = end + terminator.Length;
                continue;
            }
            if (UnsafeXmlToken.IsMatch(text, position) &&
                UnsafeXmlToken.Match(text, position).Index == position)
                return true;
            char? quote = null;
            var foundEnd = false;
            for (var index = position + 1; index < text.Length; index++)
            {
                var symbol = text[index];
                if (symbol == quote)
                    quote = null;
                else if (quote is null)
                {
                    if (symbol is '\'' or '"')
                        quote = symbol;
                    else if (symbol == '>')
                    {
                        position = index + 1;
                        foundEnd = true;
                        break;
                    }
                }
            }
            if (!foundEnd)
                return false;
        }
        return false;
    }

    private static XElement ReadXml(byte[] content, int limit, string label)
    {
        var text = XmlText(content, limit, label);
        if (HasUnsafeXmlDeclaration(text))
            throw new XmlProblem(label + "-dtd");
        try
        {
            using var reader = XmlReader.Create(new StringReader(text), new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = limit * 2L,
                MaxCharactersFromEntities = 0
            });
            return XDocument.Load(reader, LoadOptions.PreserveWhitespace).Root
                ?? throw new XmlProblem(label + "-parse");
        }
        catch (XmlException)
        {
            throw new XmlProblem(label + "-parse");
        }
    }

    private static bool IsSpace(byte value) =>
        value is 9 or 10 or 11 or 12 or 13 or 32;

    private static bool IsProjectToken(string text, int offset)
    {
        var remaining = text.AsSpan(offset);
        if (!remaining.StartsWith("<", StringComparison.Ordinal))
            return false;
        var index = 1;
        if (index >= remaining.Length)
            return false;
        if (char.IsLetter(remaining[index]) || remaining[index] == '_')
        {
            var colon = index;
            while (colon < remaining.Length &&
                   (char.IsLetterOrDigit(remaining[colon]) || remaining[colon] is '_' or '.' or '-'))
                colon++;
            if (colon < remaining.Length && remaining[colon] == ':')
                index = colon + 1;
        }
        return remaining[index..].StartsWith("Project", StringComparison.Ordinal) &&
            index + 7 < remaining.Length &&
            (char.IsWhiteSpace(remaining[index + 7]) || remaining[index + 7] is '/' or '>');
    }

    private static bool StartsProject(byte[] content)
    {
        var prefix = content.AsSpan(0, Math.Min(content.Length, XmlLimit));
        var utf16 = prefix.StartsWith(new byte[] { 0xff, 0xfe }) ||
            prefix.StartsWith(new byte[] { 0xfe, 0xff });
        if (!utf16 && (prefix.StartsWith(new byte[] { 0x3c, 0 }) ||
                       prefix.StartsWith(new byte[] { 0, 0x3c })))
            throw new XmlProblem("build-xml-encoding");
        if (!utf16)
        {
            if (prefix.StartsWith(new byte[] { 0xef, 0xbb, 0xbf }))
                prefix = prefix[3..];
            var spaces = 0;
            while (spaces < prefix.Length && IsSpace(prefix[spaces]))
                spaces++;
            if (spaces == prefix.Length || prefix[spaces] != '<')
            {
                if (content.Length > XmlLimit && spaces == prefix.Length)
                    throw new XmlProblem("build-xml-root-limit");
                return false;
            }
        }
        var text = XmlText(content[..Math.Min(content.Length, XmlLimit)],
            XmlLimit, "build-xml");
        var cursor = 0;
        while (true)
        {
            while (cursor < text.Length && char.IsWhiteSpace(text[cursor]))
                cursor++;
            if (text.AsSpan(cursor).StartsWith("<!--", StringComparison.Ordinal))
            {
                var end = text.IndexOf("-->", cursor + 4, StringComparison.Ordinal);
                if (end < 0)
                    throw new XmlProblem("build-xml-root-limit");
                cursor = end + 3;
            }
            else if (Regex.Match(text[cursor..], @"\A<\?xml(?=\s)[^?]*\?>",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) is { Success: true } xml)
                cursor += xml.Length;
            else
                break;
        }
        if (IsProjectToken(text, cursor))
            return true;
        var unsupported = false;
        while (true)
        {
            while (cursor < text.Length && char.IsWhiteSpace(text[cursor]))
                cursor++;
            if (text.AsSpan(cursor).StartsWith("<!--", StringComparison.Ordinal))
            {
                var end = text.IndexOf("-->", cursor + 4, StringComparison.Ordinal);
                if (end < 0)
                    throw new XmlProblem("build-xml-root-limit");
                cursor = end + 3;
            }
            else if (text.AsSpan(cursor).StartsWith("<?", StringComparison.Ordinal))
            {
                unsupported = true;
                if (content.Length > XmlLimit)
                    throw new XmlProblem("build-xml-root-limit");
                var end = text.IndexOf("?>", cursor + 2, StringComparison.Ordinal);
                if (end < 0)
                    throw new XmlProblem("build-xml-root-limit");
                cursor = end + 2;
            }
            else if (UnsafeXmlToken.Match(text, cursor) is { Success: true } unsafeToken &&
                     unsafeToken.Index == cursor)
            {
                unsupported = true;
                if (content.Length > XmlLimit)
                    throw new XmlProblem("build-xml-root-limit");
                var depth = 0;
                char? quote = null;
                var index = cursor + 2;
                for (; index < text.Length; index++)
                {
                    var remaining = text.AsSpan(index);
                    if (quote is null &&
                        (remaining.StartsWith("<!--", StringComparison.Ordinal) ||
                         remaining.StartsWith("<?", StringComparison.Ordinal)))
                    {
                        var ending = remaining.StartsWith("<!--", StringComparison.Ordinal)
                            ? "-->" : "?>";
                        var end = text.IndexOf(ending, index + 2, StringComparison.Ordinal);
                        if (end < 0)
                            throw new XmlProblem("build-xml-root-limit");
                        index = end + ending.Length - 1;
                        continue;
                    }
                    var symbol = text[index];
                    if (symbol == quote)
                        quote = null;
                    else if (quote is null)
                    {
                        if (symbol is '\'' or '"')
                            quote = symbol;
                        else if (symbol == '[')
                            depth++;
                        else if (symbol == ']' && depth > 0)
                            depth--;
                        else if (symbol == '>' && depth == 0)
                        {
                            cursor = index + 1;
                            break;
                        }
                    }
                }
                if (index == text.Length)
                    throw new XmlProblem("build-xml-root-limit");
            }
            else
                break;
        }
        if (unsupported && IsProjectToken(text, cursor))
            throw new XmlProblem("build-xml-unsupported-prolog");
        if (content.Length > XmlLimit && (cursor == text.Length ||
            text.AsSpan(cursor).StartsWith("<!--", StringComparison.Ordinal) ||
            text.AsSpan(cursor).StartsWith("<?", StringComparison.Ordinal)))
            throw new XmlProblem("build-xml-root-limit");
        return false;
    }

    private static string? ResolveInPackage(string name, string reference,
        HashSet<string> available, bool allowLiteral)
    {
        const string prefix = "$(MSBuildThisFileDirectory)";
        string relative;
        if (reference.StartsWith(prefix, StringComparison.Ordinal))
            relative = reference[prefix.Length..];
        else if (allowLiteral && !reference.Contains("$(", StringComparison.Ordinal))
            relative = reference;
        else
            return null;
        relative = relative.Replace('\\', '/');
        if (relative.Length == 0 || relative.StartsWith('/') ||
            Regex.IsMatch(relative, @"\A[A-Za-z]:") ||
            relative.IndexOfAny(['$', '*', '?', ':']) >= 0)
            return null;
        var segments = new List<string>(name.Split('/')[..^1]);
        foreach (var part in relative.Split('/'))
        {
            if (part == ".")
                continue;
            if (part == "..")
            {
                if (segments.Count == 0)
                    return null;
                segments.RemoveAt(segments.Count - 1);
            }
            else if (part.Length > 0)
                segments.Add(part);
        }
        var target = string.Join('/', segments);
        return SafePath(target) && available.Contains(target) ? target : null;
    }

    private static bool IsDeclarativeFullPath(string value, string path,
        string? attribute, string name)
    {
        var match = DeclarativeFullPath.Match(value);
        if (!match.Success)
            return false;
        const string item = @"/ItemGroup\[\d+\]/[^/]+\[\d+\]";
        if (attribute is not null)
        {
            if (attribute.Equals("Condition", StringComparison.OrdinalIgnoreCase) ||
                !Regex.IsMatch(path, item + "$"))
                return false;
        }
        else if (!Regex.IsMatch(path, item + @"/[^/]+\[\d+\]$"))
            return false;
        var relative = match.Groups[1].Value.Replace('\\', '/');
        var segments = new List<string>(name.Split('/')[..^1]);
        foreach (var part in relative.Split('/'))
        {
            if (part == ".")
                continue;
            if (part == "..")
            {
                if (segments.Count == 0)
                    return false;
                segments.RemoveAt(segments.Count - 1);
            }
            else if (part.Length > 0)
                segments.Add(part);
        }
        return SafePath(string.Join('/', segments));
    }

    private static Dictionary<XElement, string> LocatorMap(XElement root)
    {
        var paths = new Dictionary<XElement, string>();
        void Visit(XElement node, string path, int depth)
        {
            if (depth > XmlDepthLimit)
                throw new XmlProblem("build-xml-depth-limit");
            if (paths.Count >= XmlNodeLimit)
                throw new XmlProblem("build-xml-node-limit");
            paths[node] = path;
            var siblings = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var child in node.Elements())
            {
                var local = child.Name.LocalName;
                siblings.TryGetValue(local, out var index);
                siblings[local] = ++index;
                Visit(child, $"{path}/{local}[{index}]", depth + 1);
            }
        }
        Visit(root, "Project[1]", 1);
        return paths;
    }

    private static Snippet DisplaySnippet(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        var length = Math.Min(bytes.Length, SnippetLimit);
        while (length > 0)
        {
            try
            {
                return new(StrictUtf8.GetString(bytes.AsSpan(0, length)),
                    bytes.Length > SnippetLimit);
            }
            catch (DecoderFallbackException)
            {
                length--;
            }
        }
        return new("", bytes.Length > SnippetLimit);
    }

    private static string NodeText(XElement node) =>
        node.FirstNode is XText text ? text.Value : "";

    private static string TailText(XElement node) =>
        node.NextNode is XText text ? text.Value : "";

    private static bool HasPropertyFunction(string value) =>
        PropertyFunction.IsMatch(value);

    private static (string Kind, List<Locator> Locators,
        List<string> Promotions, List<string> Imports) InspectMsbuild(
        string name, byte[] raw, HashSet<string> available)
    {
        var root = ReadXml(raw, XmlLimit, "build-xml");
        if (root.Name.LocalName != "Project" ||
            root.Name.NamespaceName is not ("" or MsbuildNamespace))
            throw new XmlProblem("build-xml-foreign-root");
        var paths = LocatorMap(root);
        var locators = new List<Locator>();
        var promotions = new HashSet<string>(StringComparer.Ordinal);
        var imports = new List<string>();
        var unresolved = false;
        void Record(XElement node, string marker, string value) =>
            locators.Add(new(paths[node] + marker, DisplaySnippet(value)));

        foreach (var (node, path) in paths)
        {
            var local = node.Name.LocalName;
            var lower = local.ToLowerInvariant();
            if (lower == "import")
            {
                var reference = node.Attribute("Project")?.Value ?? "";
                var target = ResolveInPackage(name, reference, available, allowLiteral: true)
                    ?? throw new XmlProblem("build-import-unresolved");
                imports.Add(target);
            }
            if (lower == "usingtask")
            {
                var factory = node.Attribute("TaskFactory")?.Value ?? "";
                if (node.HasElements && factory.Length > 0 &&
                    !factory.Equals("RoslynCodeTaskFactory", StringComparison.OrdinalIgnoreCase) &&
                    !factory.Equals("CodeTaskFactory", StringComparison.OrdinalIgnoreCase))
                {
                    Record(node, "@TaskFactory", factory);
                    unresolved = true;
                }
                if (node.Elements().Any(child =>
                    child.Name.LocalName.ToLowerInvariant() is not ("task" or "parametergroup")))
                {
                    Record(node, "#unsupported-inline", local);
                    unresolved = true;
                }
            }
            if (lower == "code" && path.Contains("/UsingTask[", StringComparison.Ordinal))
            {
                var source = node.Attribute("Source");
                if (source is not null)
                {
                    Record(node, "@Source", source.Value);
                    var target = ResolveInPackage(name, source.Value, available,
                        allowLiteral: false);
                    if (target is not null && target != name)
                        promotions.Add(target);
                    else
                        unresolved = true;
                    if (!string.IsNullOrWhiteSpace(NodeText(node)))
                        Record(node, "#text", NodeText(node));
                }
                else
                    Record(node, "#text", node.Value);
            }
            if (lower == "task" && path.Contains("/UsingTask[", StringComparison.Ordinal) &&
                (!string.IsNullOrWhiteSpace(NodeText(node)) ||
                 node.Elements().Any(child => child.Name.LocalName.ToLowerInvariant() != "code")))
            {
                Record(node, "#unsupported-inline", NodeText(node).Length > 0
                    ? NodeText(node) : local);
                unresolved = true;
            }
            if (lower == "exec" || lower.EndsWith(".exec", StringComparison.Ordinal))
            {
                if (Regex.IsMatch(path, @"/Target\[\d+\]/[^/]+\[\d+\]$"))
                    Record(node, "@Command", node.Attribute("Command")?.Value ?? "");
                else if (Regex.IsMatch(path, @"/Target\[\d+\]/"))
                {
                    Record(node, "#unsupported-nested-exec",
                        node.Attribute("Command")?.Value ?? "");
                    unresolved = true;
                }
            }
            if (lower is "prebuildevent" or "postbuildevent" or "runcommand" ||
                lower.EndsWith("toolexe", StringComparison.Ordinal) ||
                lower.EndsWith("toolpath", StringComparison.Ordinal) ||
                lower.StartsWith("custombeforemicrosoft", StringComparison.Ordinal) ||
                lower.StartsWith("customaftermicrosoft", StringComparison.Ordinal))
                Record(node, "#text", NodeText(node) +
                    string.Concat(node.Attributes().Select(attribute => attribute.Value)));
            foreach (var attribute in node.Attributes())
            {
                if (HasPropertyFunction(attribute.Value) &&
                    !IsDeclarativeFullPath(attribute.Value, path,
                        attribute.Name.ToString(), name))
                {
                    Record(node, "@" + attribute.Name, attribute.Value);
                    unresolved = true;
                }
            }
            var nodeText = NodeText(node);
            if (nodeText.Length > 0 && HasPropertyFunction(nodeText) &&
                !IsDeclarativeFullPath(nodeText, path, null, name))
            {
                Record(node, "#property-function", nodeText);
                unresolved = true;
            }
            var tail = TailText(node);
            if (tail.Length > 0 && HasPropertyFunction(tail))
            {
                Record(node, "#tail-function", tail);
                unresolved = true;
            }
        }
        locators.Sort((left, right) => StringComparer.Ordinal.Compare(left.Name, right.Name));
        return (unresolved ? "unsupported" : locators.Count > 0 ? "text" : "passive",
            locators, promotions.Order(StringComparer.Ordinal).ToList(),
            imports.Order(StringComparer.Ordinal).ToList());
    }

    private static (string Kind, List<Locator> Locators) InspectMarkup(byte[] raw)
    {
        var root = ReadXml(raw, XmlLimit, "build-xml");
        var locators = new List<Locator>();
        foreach (var (node, path) in LocatorMap(root))
        {
            if (node.Name.LocalName.ToLowerInvariant() is "script" or "scriptblock" or "template")
                locators.Add(new(path, DisplaySnippet(node.Value)));
            foreach (var attribute in node.Attributes())
            {
                if (attribute.Name.LocalName.StartsWith("on", StringComparison.OrdinalIgnoreCase) ||
                    attribute.Value.Contains("javascript:", StringComparison.OrdinalIgnoreCase))
                    locators.Add(new(path + "@" + attribute.Name, DisplaySnippet(attribute.Value)));
            }
        }
        locators.Sort((left, right) => StringComparer.Ordinal.Compare(left.Name, right.Name));
        return (locators.Count > 0 ? "text" : "passive", locators);
    }
}
