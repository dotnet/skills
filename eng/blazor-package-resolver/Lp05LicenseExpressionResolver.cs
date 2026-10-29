using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace BlazorPackageResolver;

public sealed class Lp05LicenseExpressionResolver : IDeterministicResolver
{
    private const int MaxPackageBytes = 64 * 1024 * 1024;
    private const int MaxNuspecBytes = 1024 * 1024;
    private const int MaxDirectoryBytes = 8 * 1024 * 1024;
    private const int MaxEntries = 4096;
    private const int MaxEntryNameLength = 512;
    private const string MissingExpression = "PackageLicenseExpression is not populated; the nuspec has no license expression.";
    private static readonly Version CurrentLicenseVersion = new(1, 0, 0);
    private static readonly HashSet<string> NuGetNamespaces = new(StringComparer.Ordinal)
    {
        "",
        "http://schemas.microsoft.com/packaging/2010/07/nuspec.xsd",
        "http://schemas.microsoft.com/packaging/2011/08/nuspec.xsd",
        "http://schemas.microsoft.com/packaging/2011/10/nuspec.xsd",
        "http://schemas.microsoft.com/packaging/2012/06/nuspec.xsd",
        "http://schemas.microsoft.com/packaging/2013/01/nuspec.xsd",
        "http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd"
    };

    public string RowId => "LP-05";
    public string ResolverId => "lp05-license-expression";
    public string Version => "1.0.0";

    public CaseResult Resolve(ResolverCase input)
    {
        if (input.Inputs.Count == 0)
            return Escalate(input, "missing-package", "unknown",
                "The package bytes required for LP-05 were not supplied.", "package", "input-owner");
        if (input.Inputs.Count != 1 || input.Inputs[0].Role != "package" ||
            !input.CheckedInputs.TryGetValue("package", out var checkedPackage))
            throw new InvalidDataException("LP-05 requires exactly one checked package input.");

        var content = checkedPackage.Content;
        if (content.Length > MaxPackageBytes || !TryReadDirectory(content.Span, out var entryCount))
            return Unreadable(input);
        try
        {
            using var stream = new MemoryStream(content.ToArray(), writable: false);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
            if (archive.Entries.Count != entryCount ||
                archive.Entries.Any(entry => entry.FullName.Length > MaxEntryNameLength))
                return Unreadable(input);

            var nuspecs = archive.Entries
                .Where(entry => entry.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            var roots = nuspecs
                .Where(entry => entry.FullName.IndexOfAny(['/', '\\']) < 0)
                .ToArray();
            if (roots.Length > 1)
                return Escalate(input, "ambiguous-nuspec", "unknown",
                    "The archive has multiple root nuspec entries.", "unique-root-nuspec", "package-owner");
            if (roots.Length == 0)
                return nuspecs.Length == 0
                    ? Escalate(input, "missing-nuspec", "unknown",
                        "The archive has no nuspec entry.", "root-nuspec", "package-owner")
                    : Escalate(input, "missing-root-nuspec", "unknown",
                        "The only nuspec entry is nested in a subfolder.", "root-nuspec", "package-owner");

            if (roots[0].Length > MaxNuspecBytes)
                return OversizedNuspec(input);
            using var nuspecStream = roots[0].Open();
            using var buffer = new MemoryStream();
            var chunk = new byte[8192];
            int read;
            while ((read = nuspecStream.Read(chunk)) > 0)
            {
                if (buffer.Length + read > MaxNuspecBytes)
                    return OversizedNuspec(input);
                buffer.Write(chunk, 0, read);
            }
            return InspectNuspec(input, buffer.ToArray());
        }
        catch (InvalidDataException)
        {
            return Unreadable(input);
        }
        catch (NotSupportedException)
        {
            return Unreadable(input);
        }
        catch (IOException)
        {
            return Unreadable(input);
        }
    }

    private CaseResult InspectNuspec(ResolverCase input, byte[] nuspec)
    {
        if (ContainsDoctype(nuspec))
            return UnsafeNuspec(input);
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = MaxNuspecBytes
        };
        XDocument document;
        try
        {
            using var stream = new MemoryStream(nuspec, writable: false);
            using var reader = XmlReader.Create(stream, settings);
            document = XDocument.Load(reader);
        }
        catch (XmlException)
        {
            return Escalate(input, "invalid-xml", "unknown",
                "The nuspec XML is not well formed.", "valid-nuspec", "package-owner");
        }

        var root = document.Root;
        if (root is null || root.Name.LocalName != "package" ||
            !NuGetNamespaces.Contains(root.Name.NamespaceName))
            return Escalate(input, "foreign-root", "unknown",
                "The package root is not in a recognized NuGet namespace.",
                "recognized-nuspec", "package-owner");

        var metadata = root.Elements(root.Name.Namespace + "metadata").ToArray();
        if (metadata.Length != 1)
            return Escalate(input, "invalid-xml", "unknown",
                "The nuspec XML is not well formed.", "valid-nuspec", "package-owner");

        var licenses = metadata[0].Elements()
            .Where(element => element.Name.LocalName == "license").ToArray();
        if (licenses.Any(element => element.Name.Namespace != root.Name.Namespace))
            return Escalate(input, "foreign-license", "applicable",
                "A foreign-namespace license element cannot establish NuGet license metadata.",
                "recognized-license-metadata", "package-owner");
        if (licenses.Length > 1)
            return Escalate(input, "ambiguous", "applicable",
                "Duplicate license elements conflict.", "unambiguous-nuspec", "package-owner");
        if (licenses.Length == 0)
            return Deterministic(input, "missing-expression", MissingExpression, "package:nuspec/metadata");

        var license = licenses[0];
        var type = license.Attribute("type");
        if (type is null)
            return Escalate(input, "malformed", "applicable",
                "The license element has no type attribute.", "corrected-nuspec", "package-owner");
        if (license.HasElements || license.Attributes().Any(attribute =>
                !attribute.IsNamespaceDeclaration && attribute.Name != "type" && attribute.Name != "version"))
            return Malformed(input);
        var versionAttribute = license.Attribute("version");
        if (versionAttribute is not null)
        {
            if (!System.Version.TryParse(versionAttribute.Value.Trim(), out var parsedVersion))
                return Malformed(input);
            if (parsedVersion.CompareTo(CurrentLicenseVersion) > 0)
                return Escalate(input, "unsupported-version", "applicable",
                    "The license expression version is newer than supported 1.0.0.",
                    "supported-license-version", "package-owner");
        }
        var value = license.Value.Trim();
        if (string.Equals(type.Value.Trim(), "file", StringComparison.OrdinalIgnoreCase))
            return value.Length == 0
                ? Deterministic(input, "missing-expression", MissingExpression, "package:nuspec/metadata")
                : Deterministic(input, "file",
                    "PackageLicenseExpression is not populated; the nuspec declares a license file.",
                    "package:nuspec/license");
        if (!string.Equals(type.Value.Trim(), "expression", StringComparison.OrdinalIgnoreCase))
            return Escalate(input, "malformed", "applicable",
                "The license type is not a supported NuGet expression or file type.",
                "corrected-nuspec", "package-owner");
        if (value.Length == 0 || value.Length > 1024)
            return Malformed(input);
        return Deterministic(input, "expression",
            $"PackageLicenseExpression is populated: {value}.", "package:nuspec/license");
    }

    private static bool ContainsDoctype(byte[] content)
    {
        foreach (var encoding in new[] { Encoding.UTF8, Encoding.Unicode, Encoding.BigEndianUnicode })
        {
            var marker = encoding.GetBytes("<!DOCTYPE");
            if (content.AsSpan().IndexOf(marker) >= 0)
                return true;
        }
        return false;
    }

    private static bool TryReadDirectory(ReadOnlySpan<byte> content, out int entryCount)
    {
        entryCount = 0;
        for (var index = content.Length - 22; index >= Math.Max(0, content.Length - 65557); index--)
        {
            var tail = content[index..];
            if (BinaryPrimitives.ReadUInt32LittleEndian(tail) != 0x06054b50 ||
                index + 22 + BinaryPrimitives.ReadUInt16LittleEndian(tail[20..]) != content.Length)
                continue;
            var disk = BinaryPrimitives.ReadUInt16LittleEndian(tail[4..]);
            var directoryDisk = BinaryPrimitives.ReadUInt16LittleEndian(tail[6..]);
            var diskEntries = BinaryPrimitives.ReadUInt16LittleEndian(tail[8..]);
            var entries = BinaryPrimitives.ReadUInt16LittleEndian(tail[10..]);
            var directorySize = BinaryPrimitives.ReadUInt32LittleEndian(tail[12..]);
            var directoryOffset = BinaryPrimitives.ReadUInt32LittleEndian(tail[16..]);
            if (disk != 0 || directoryDisk != 0 || diskEntries != entries ||
                entries > MaxEntries || directorySize > MaxDirectoryBytes ||
                (ulong)directoryOffset + directorySize > (ulong)index)
                return false;
            entryCount = entries;
            return true;
        }
        return false;
    }

    private CaseResult Deterministic(ResolverCase input, string branch, string finding, string reference) =>
        new(RowId, ResolverId, Version, branch, "deterministic", "applicable",
            input.Inputs, [reference], null, finding, null, null, null);

    private CaseResult Escalate(ResolverCase input, string branch, string applicability,
        string missingFact, string requiredInput, string nextCategory) =>
        new(RowId, ResolverId, Version, branch, "escalated", applicability,
            input.Inputs, [], null, null, missingFact, requiredInput, nextCategory);

    private CaseResult Malformed(ResolverCase input) =>
        Escalate(input, "malformed", "applicable",
            "A nonblank license expression cannot be established from malformed metadata.",
            "corrected-nuspec", "package-owner");

    private CaseResult UnsafeNuspec(ResolverCase input) =>
        Escalate(input, "unsafe-nuspec", "unknown",
            "The nuspec contains a DTD/entity declaration and cannot be safely inspected.",
            "safe-nuspec", "package-owner");

    private CaseResult OversizedNuspec(ResolverCase input) =>
        Escalate(input, "unsafe-nuspec", "unknown",
            "The nuspec exceeds the safe inspection size limit.",
            "safe-nuspec", "package-owner");

    private CaseResult Unreadable(ResolverCase input) =>
        Escalate(input, "unreadable-package", "unknown",
            "The supplied checked bytes are not a readable NuGet archive.",
            "valid-nupkg", "package-owner");
}
