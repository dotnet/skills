using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;

namespace BlazorPackageResolver;

public sealed class Pi01StrongNameResolver : IDeterministicResolver
{
    private const int PackageLimit = 64 * 1024 * 1024;
    private const int NuspecLimit = 1024 * 1024;
    private const int EntryLimit = 8 * 1024 * 1024;
    private const int TotalLimit = 32 * 1024 * 1024;
    private const int NameLimit = 1024;
    private const int CountLimit = 4096;
    private const string NuspecNamespace = "http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd";
    private const string ContractPrefix =
        "Inspected PE/CLI candidates (sorted by ordinal path; SHA-256 covers exact ZIP entry bytes): ";
    private static readonly uint[] CrcTable = Enumerable.Range(0, 256).Select(index =>
    {
        uint crc = (uint)index;
        for (int bit = 0; bit < 8; bit++)
            crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0u : 0xedb88320u);
        return crc;
    }).ToArray();
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly UnicodeEncoding StrictUtf16Le = new(false, true, true);
    private static readonly UnicodeEncoding StrictUtf16Be = new(true, true, true);
    private static readonly string[] OpaqueSuffixes =
        [".zip", ".nupkg", ".snupkg", ".jar", ".7z", ".tar", ".gz", ".br", ".cab",
         ".so", ".dylib", ".a", ".wasm"];
    private static readonly string[] PassiveSuffixes =
        [".rels", ".txt", ".md", ".png", ".jpg", ".jpeg", ".gif", ".svg", ".css",
         ".js", ".d.ts", ".xml", ".psmdcp", ".p7s", ".props", ".json"];
    private static readonly byte[][] ContainerMagic =
    [
        [0x1f, 0x8b], [0x37, 0x7a, 0xbc, 0xaf, 0x27, 0x1c], "MSCF"u8.ToArray(),
        "!<arch>\n"u8.ToArray(), [0x7f, (byte)'E', (byte)'L', (byte)'F'],
        [0, (byte)'a', (byte)'s', (byte)'m'], [0xfd, (byte)'7', (byte)'z', (byte)'X', (byte)'Z', 0],
        "BZh"u8.ToArray(), "Rar!\x1a\x07"u8.ToArray(),
        [0xd0, 0xcf, 0x11, 0xe0], [0x28, 0xb5, 0x2f, 0xfd],
        [0xcf, 0xfa, 0xed, 0xfe], [0xfe, 0xed, 0xfa, 0xcf]
    ];

    public string RowId => "PI-01";
    public string ResolverId => "pi01-strong-name";
    public string Version => "1.0.0";

    public CaseResult Resolve(ResolverCase input)
    {
        if (input.Inputs.Count == 0)
            return Result(input, "missing-package", []);
        if (input.Inputs.Count != 1 || input.Inputs[0].Role != "package" ||
            !input.CheckedInputs.TryGetValue("package", out var package))
            throw new InvalidDataException("PI-01 requires exactly one checked package input.");
        var (branch, records) = InspectPackage(package.Content);
        return Result(input, branch, records);
    }

    private sealed record EntryRecord(string Path, string Sha256, int SizeBytes,
        string State, string? CryptographicEvidence = null, string? InventoryEvidence = null);
    private readonly record struct CentralEntry(string Name, uint Crc32);

    private static CaseResult Result(ResolverCase input, string branch, List<EntryRecord> records)
    {
        string? contract = records.Count == 0 ? null : ContractPrefix + WriteRecords(records);
        var finding = branch switch
        {
            "no-assemblies" =>
                "No .NET assemblies found in the safely inspected package; PI-01 holds vacuously for this package only.",
            "unsigned-assembly" =>
                "At least one shipped .NET assembly has no strong-name signature reservation; PI-01 is not met for this package.",
            "delay-signed" =>
                "At least one shipped .NET assembly has a zero-filled strong-name reservation with the CLR signed flag clear (delay signing); PI-01 is not met for this package.",
            "public-signed" =>
                "At least one shipped .NET assembly has a zero-filled strong-name reservation with the CLR signed flag set (public signing); PI-01 is not met for this package.",
            "invalid-signature" =>
                "At least one shipped .NET assembly has a cryptographically invalid strong-name signature; PI-01 is not met for this package.",
            "verified-signed" =>
                "Every inventoried shipped .NET assembly has a cryptographically verified strong-name signature; PI-01 is met for this package.",
            _ => null
        };
        var (missing, required, category) = branch switch
        {
            "missing-package" => ("No checked package was supplied.", "pinned-package", "input-owner"),
            "unsafe-package" => ("Archive or manifest cannot be safely and unambiguously inspected.",
                "safe-unambiguous-package", "package-owner"),
            "unsafe-assembly" => ("Potential .NET assembly cannot be safely classified.",
                "inspectable-complete-pe-cli-assembly", "package-owner"),
            "opaque-container" => ("An opaque entry, nested container or native PE could contain assemblies; packaged assembly inventory is incomplete.",
                "complete-safe-bundle-and-nested-container-inventory", "package-owner"),
            "signature-unverified" => ("The strong-name algorithm or key shape is unsupported; cryptographic validity is undetermined.",
                "supported-strong-name-algorithm-and-key-evidence", "package-owner"),
            _ => ((string?)null, null, null)
        };
        var structural = branch is "missing-package" or "unsafe-package" or "unsafe-assembly"
            or "opaque-container";
        var decisive = finding is not null;
        if (!decisive && missing is null)
            throw new InvalidDataException($"Unknown PI-01 branch: {branch}");
        return new("PI-01", "pi01-strong-name", "1.0.0", branch,
            decisive ? "deterministic" : "escalated",
            structural ? "unknown" : "applicable", input.Inputs,
            branch == "missing-package" ? [] : ["package:archive"],
            contract, finding, missing, required, category);
    }

    private static string WriteRecords(List<EntryRecord> records)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartArray();
            foreach (var item in records)
            {
                writer.WriteStartObject();
                if (item.CryptographicEvidence is not null)
                    writer.WriteString("cryptographic_evidence", item.CryptographicEvidence);
                if (item.InventoryEvidence is not null)
                    writer.WriteString("inventory_evidence", item.InventoryEvidence);
                writer.WriteString("path", item.Path);
                writer.WriteString("sha256", item.Sha256);
                writer.WriteNumber("size_bytes", item.SizeBytes);
                writer.WriteString("state", item.State);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
        }
        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static (string Branch, List<EntryRecord> Records) InspectPackage(ReadOnlyMemory<byte> content)
    {
        try
        {
            if (content.Length > PackageLimit || !TryCentralNames(content.Span, out var rawNames))
                return ("unsafe-package", []);
            using var stream = new MemoryStream(content.ToArray(), writable: false);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
            var entries = archive.Entries;
            if (entries.Count != rawNames.Count || entries.Count > CountLimit ||
                entries.Where((entry, index) => entry.FullName != rawNames[index].Name).Any() ||
                entries.Any(entry => !SafePath(entry.FullName)) ||
                entries.Select(entry => entry.FullName.ToLowerInvariant())
                    .Distinct(StringComparer.Ordinal).Count() != entries.Count)
                return ("unsafe-package", []);
            var roots = entries.Where(entry => !entry.FullName.Contains('/') &&
                entry.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (roots.Length != 1 || roots[0].Length > NuspecLimit)
                return ("unsafe-package", []);
            var manifest = ReadEntry(roots[0], NuspecLimit);
            if (Crc32(manifest) != rawNames.Single(item => item.Name == roots[0].FullName).Crc32)
                return ("unsafe-package", []);
            if (!ValidNuspec(manifest))
                return ("unsafe-package", []);
            var records = new List<EntryRecord>();
            long total = 0;
            for (int index = 0; index < entries.Count; index++)
            {
                var entry = entries[index];
                var name = entry.FullName;
                if (entry == roots[0])
                    continue;
                if (name.EndsWith('/'))
                {
                    if (entry.Length != 0)
                        return ("unsafe-package", []);
                    continue;
                }
                if (entry.Length > EntryLimit || (total += entry.Length) > TotalLimit)
                    return ("unsafe-package", []);
                var bytes = ReadEntry(entry, EntryLimit);
                if (Crc32(bytes) != rawNames[index].Crc32)
                    return ("unsafe-package", []);
                var raw = bytes.AsSpan();
                var lower = name.ToLowerInvariant();
                string? state = null;
                string? crypto = null;
                string? inventory = null;
                if (HasEmbeddedZip(raw) || HasContainerMagic(raw) ||
                    (raw.Length >= 262 && raw.Slice(257, 5).SequenceEqual("ustar"u8)) ||
                    OpaqueSuffixes.Any(lower.EndsWith))
                    state = "opaque-container";
                else if (raw.StartsWith("MZ"u8) || lower.EndsWith(".dll", StringComparison.Ordinal) ||
                    lower.EndsWith(".exe", StringComparison.Ordinal) ||
                    lower.EndsWith(".winmd", StringComparison.Ordinal))
                {
                    var inspected = Pi01PeInspector.Inspect(bytes);
                    state = inspected.State;
                    if (state is "verified-signed" or "invalid-signature" or "signature-unverified")
                        crypto = inspected.Detail;
                    else if (state == "opaque-container")
                        inventory = inspected.Detail;
                }
                else if (raw.Length > 0 && !PassiveSuffixes.Any(lower.EndsWith) &&
                    lower[(lower.LastIndexOf('/') + 1)..] != "license")
                    state = "opaque-container";
                if (state is not null)
                    records.Add(new(name, Catalog.Digest(bytes), bytes.Length, state, crypto, inventory));
            }
            records.Sort((left, right) => StringComparer.Ordinal.Compare(left.Path, right.Path));
            foreach (var (state, branch) in new (string, string)[]
            {
                ("unsafe", "unsafe-assembly"), ("opaque-container", "opaque-container"),
                ("native-pe", "opaque-container"), ("delay-signed", "delay-signed"),
                ("public-signed", "public-signed"), ("unsigned", "unsigned-assembly"),
                ("invalid-signature", "invalid-signature"),
                ("signature-unverified", "signature-unverified"),
                ("verified-signed", "verified-signed")
            })
                if (records.Any(record => record.State == state))
                    return (branch, records);
            return ("no-assemblies", records);
        }
        catch (Exception error) when (error is InvalidDataException or IOException or
            NotSupportedException or DecoderFallbackException or XmlException or ArgumentException)
        {
            return ("unsafe-package", []);
        }
    }

    private static bool ValidNuspec(byte[] raw)
    {
        string text = raw.Length >= 2 && raw[0] == 0xff && raw[1] == 0xfe
            ? StrictUtf16Le.GetString(raw.AsSpan(2))
            : raw.Length >= 2 && raw[0] == 0xfe && raw[1] == 0xff
                ? StrictUtf16Be.GetString(raw.AsSpan(2))
                : StrictUtf8.GetString(raw.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf })
                    ? raw.AsSpan(3) : raw);
        if (text.Contains('\0'))
            return false;
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = NuspecLimit
        };
        using var reader = XmlReader.Create(new StringReader(text), settings);
        var document = XDocument.Load(reader);
        return document.Root?.Name == XName.Get("package", NuspecNamespace) &&
            document.Root.Elements().Select(item => item.Name)
                .SequenceEqual([XName.Get("metadata", NuspecNamespace)]);
    }

    private static byte[] ReadEntry(ZipArchiveEntry entry, int limit)
    {
        if (entry.Length > limit)
            throw new InvalidDataException("ZIP entry declares excessive bytes.");
        using var input = entry.Open();
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int read;
        while ((read = input.Read(chunk)) > 0)
        {
            if (buffer.Length + read > limit)
                throw new InvalidDataException("ZIP entry exceeds inspection limit.");
            buffer.Write(chunk, 0, read);
        }
        if (buffer.Length != entry.Length)
            throw new InvalidDataException("ZIP entry declared and actual lengths differ.");
        return buffer.ToArray();
    }

    private static bool HasEmbeddedZip(ReadOnlySpan<byte> raw) =>
        raw.IndexOf("PK\x03\x04"u8) >= 0 ||
        raw.IndexOf("PK\x01\x02"u8) >= 0 ||
        raw.IndexOf("PK\x05\x06"u8) >= 0;

    private static uint Crc32(ReadOnlySpan<byte> bytes)
    {
        uint crc = uint.MaxValue;
        foreach (byte value in bytes)
            crc = CrcTable[(int)((crc ^ value) & 0xff)] ^ (crc >> 8);
        return ~crc;
    }

    private static bool HasContainerMagic(ReadOnlySpan<byte> raw)
    {
        foreach (var magic in ContainerMagic)
            if (raw.StartsWith(magic))
                return true;
        return false;
    }

    private static bool SafePath(string path)
    {
        if (string.IsNullOrEmpty(path) || path.EnumerateRunes().Count() > NameLimit ||
            path[0] == '/' || path.Contains('\\') || path.Contains(':') ||
            path.Contains('\0'))
            return false;
        var parts = path.TrimEnd('/').Split('/');
        return parts.All(part => part is not ("" or "." or ".."));
    }

    private static bool TryCentralNames(ReadOnlySpan<byte> content, out List<CentralEntry> names)
    {
        names = [];
        int end = -1;
        for (int i = content.Length - 4; i >= Math.Max(0, content.Length - 65557); i--)
            if (content.Slice(i, 4).SequenceEqual("PK\x05\x06"u8))
            {
                end = i;
                break;
            }
        if (end < 0 || end > content.Length - 22)
            return false;
        int count = BinaryPrimitives.ReadUInt16LittleEndian(content[(end + 10)..]);
        uint size = BinaryPrimitives.ReadUInt32LittleEndian(content[(end + 12)..]);
        uint start = BinaryPrimitives.ReadUInt32LittleEndian(content[(end + 16)..]);
        if (count > CountLimit || (ulong)start + size > (ulong)end)
            return false;
        int cursor = (int)start;
        for (int index = 0; index < count; index++)
        {
            if (cursor > end - 46 || !content.Slice(cursor, 4).SequenceEqual("PK\x01\x02"u8))
                return false;
            int nameSize = BinaryPrimitives.ReadUInt16LittleEndian(content[(cursor + 28)..]);
            int extraSize = BinaryPrimitives.ReadUInt16LittleEndian(content[(cursor + 30)..]);
            int commentSize = BinaryPrimitives.ReadUInt16LittleEndian(content[(cursor + 32)..]);
            uint crc = BinaryPrimitives.ReadUInt32LittleEndian(content[(cursor + 16)..]);
            int next = cursor + 46 + nameSize + extraSize + commentSize;
            if ((long)next > (long)start + size)
                return false;
            names.Add(new(StrictUtf8.GetString(content.Slice(cursor + 46, nameSize)), crc));
            cursor = next;
        }
        return cursor == (long)start + size;
    }
}
