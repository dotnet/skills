using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace BlazorPackageResolver;

internal enum NuspecStatus
{
    Ready,
    UnreadablePackage,
    MissingNuspec,
    MissingRootNuspec,
    AmbiguousNuspec,
    OversizedNuspec,
    UnsafeNuspec,
    InvalidXml,
    InvalidMetadata,
    AmbiguousMetadata,
    ForeignRoot
}

internal readonly record struct NuspecRead(NuspecStatus Status, XElement? Metadata = null);

internal static class NuGetNuspecReader
{
    internal const string AmbiguousMetadataMissingFact =
        "A foreign-namespace metadata sibling makes authoritative NuGet metadata ambiguous across readers.";

    private const int MaxPackageBytes = 64 * 1024 * 1024;
    private const int MaxNuspecBytes = 1024 * 1024;
    private const int MaxDirectoryBytes = 8 * 1024 * 1024;
    private const int MaxEntries = 4096;
    private const int MaxEntryNameLength = 512;
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

    internal static NuspecRead Read(ReadOnlyMemory<byte> content)
    {
        if (content.Length > MaxPackageBytes || !TryReadDirectory(content.Span, out var entryCount))
            return new(NuspecStatus.UnreadablePackage);
        try
        {
            using var stream = new MemoryStream(content.ToArray(), writable: false);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
            if (archive.Entries.Count != entryCount ||
                archive.Entries.Any(entry => entry.FullName.Length > MaxEntryNameLength))
                return new(NuspecStatus.UnreadablePackage);

            var nuspecs = archive.Entries
                .Where(entry => entry.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            var roots = nuspecs
                .Where(entry => entry.FullName.IndexOfAny(['/', '\\']) < 0)
                .ToArray();
            if (roots.Length > 1)
                return new(NuspecStatus.AmbiguousNuspec);
            if (roots.Length == 0)
                return new(nuspecs.Length == 0 ? NuspecStatus.MissingNuspec : NuspecStatus.MissingRootNuspec);

            if (roots[0].Length > MaxNuspecBytes)
                return new(NuspecStatus.OversizedNuspec);
            using var nuspecStream = roots[0].Open();
            using var buffer = new MemoryStream();
            var chunk = new byte[8192];
            int read;
            while ((read = nuspecStream.Read(chunk)) > 0)
            {
                if (buffer.Length + read > MaxNuspecBytes)
                    return new(NuspecStatus.OversizedNuspec);
                buffer.Write(chunk, 0, read);
            }
            return InspectNuspec(buffer.ToArray());
        }
        catch (InvalidDataException)
        {
            return new(NuspecStatus.UnreadablePackage);
        }
        catch (NotSupportedException)
        {
            return new(NuspecStatus.UnreadablePackage);
        }
        catch (IOException)
        {
            return new(NuspecStatus.UnreadablePackage);
        }
    }

    private static NuspecRead InspectNuspec(byte[] nuspec)
    {
        if (ContainsDoctype(nuspec))
            return new(NuspecStatus.UnsafeNuspec);
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
            return new(NuspecStatus.InvalidXml);
        }

        var root = document.Root;
        if (root is null || root.Name.LocalName != "package" ||
            !NuGetNamespaces.Contains(root.Name.NamespaceName))
            return new(NuspecStatus.ForeignRoot);
        var metadata = root.Elements()
            .Where(element => string.Equals(element.Name.LocalName, "metadata", StringComparison.Ordinal))
            .ToArray();
        var recognized = metadata.Where(element => element.Name.Namespace == root.Name.Namespace).ToArray();
        if (recognized.Length == 0)
            return new(NuspecStatus.InvalidMetadata);
        if (recognized.Length != metadata.Length)
            return new(NuspecStatus.AmbiguousMetadata);
        return recognized.Length == 1
            ? new(NuspecStatus.Ready, recognized[0])
            : new(NuspecStatus.InvalidMetadata);
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
}
