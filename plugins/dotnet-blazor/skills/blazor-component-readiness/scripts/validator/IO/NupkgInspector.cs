using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using BlazorComponentReadiness.Validator.Contracts;

namespace BlazorComponentReadiness.Validator.IO;

public static class NupkgInspector
{
    public static byte[] SerializeInspection(PackageIdentity identity) => StrictJson.SerializeCanonical(writer =>
    {
        writer.WriteStartObject();
        writer.WriteNumber("schema_version", 1);
        writer.WriteString("package_id", identity.Id);
        writer.WriteString("package_version", identity.Version);
        writer.WriteString("nupkg_sha256", identity.NupkgSha256);
        writer.WriteNumber("nupkg_size", identity.NupkgSize);
        writer.WriteString("nuspec_entry", identity.NuspecEntry);
        writer.WriteEndObject();
    });

    private const int UnixFileTypeMask = 0xF000;
    private const int UnixSymbolicLink = 0xA000;
    private static readonly Regex PackageIdPattern = new(
        @"^[A-Za-z0-9_]+(?:[.-][A-Za-z0-9_]+)*$",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    public const string WholePackageEvidenceLocator = "package:whole-nupkg";
    public const string PackageEntryEvidencePrefix = "package:entry/";

    public static PackageIdentity Inspect(string path)
    {
        SafePath.EnsureRegularFile(path);
        var bytes = BoundedIO.ReadAllBytes(path, ResourceLimits.NupkgBytes, "nupkg");
        var digest = Convert.ToHexStringLower(SHA256.HashData(bytes));

        try
        {
            using var packageStream = new MemoryStream(bytes, writable: false);
            using var archive = new ZipArchive(packageStream, ZipArchiveMode.Read, leaveOpen: false);
            ValidateArchiveEntries(archive);

            var nuspecEntries = archive.Entries
                .Where(entry =>
                    !entry.FullName.Contains('/', StringComparison.Ordinal) &&
                    entry.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (nuspecEntries.Length != 1)
            {
                throw new DeterministicValidationException(
                    $"A package must contain exactly one root-level nuspec; found {nuspecEntries.Length}.");
            }

            var nuspec = nuspecEntries[0];
            BoundedIO.EnsureLength(nuspec.Length, ResourceLimits.NuspecBytes, "nuspec");
            using var nuspecStream = nuspec.Open();
            var nuspecBytes = BoundedIO.ReadAllBytes(nuspecStream, ResourceLimits.NuspecBytes, "nuspec");
            var (id, version) = ReadIdentity(nuspecBytes);
            return new PackageIdentity(id, version, digest, bytes.LongLength, nuspec.FullName);
        }
        catch (InvalidDataException exception)
        {
            throw new DeterministicValidationException("The nupkg is not a valid ZIP package.", exception);
        }
    }

    public static string ComputeEvidenceContentSha256(string path, string locator)
    {
        SafePath.EnsureRegularFile(path);
        var bytes = BoundedIO.ReadAllBytes(path, ResourceLimits.NupkgBytes, "nupkg");
        if (string.Equals(locator, WholePackageEvidenceLocator, StringComparison.Ordinal))
        {
            return Convert.ToHexStringLower(SHA256.HashData(bytes));
        }

        if (!locator.StartsWith(PackageEntryEvidencePrefix, StringComparison.Ordinal))
        {
            throw new DeterministicValidationException(
                "A package evidence locator must identify the whole nupkg or one package entry.");
        }

        var entryName = locator[PackageEntryEvidencePrefix.Length..];
        SafePath.ValidateArchiveEntry(entryName, isSymbolicLink: false);
        try
        {
            using var packageStream = new MemoryStream(bytes, writable: false);
            using var archive = new ZipArchive(packageStream, ZipArchiveMode.Read, leaveOpen: false);
            ValidateArchiveEntries(archive);
            var matches = archive.Entries
                .Where(entry =>
                    !entry.FullName.EndsWith('/') &&
                    string.Equals(entry.FullName, entryName, StringComparison.Ordinal))
                .ToArray();
            if (matches.Length != 1)
            {
                throw new DeterministicValidationException(
                    $"Package evidence entry '{entryName}' resolves to {matches.Length} entries; expected exactly one.");
            }

            BoundedIO.EnsureLength(
                matches[0].Length,
                ResourceLimits.NupkgBytes,
                "expanded package evidence entry");
            using var entryStream = matches[0].Open();
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[64 * 1024];
            long total = 0;
            while (true)
            {
                var read = entryStream.Read(buffer, 0, buffer.Length);
                if (read == 0)
                {
                    break;
                }

                total = checked(total + read);
                BoundedIO.EnsureLength(
                    total,
                    ResourceLimits.NupkgBytes,
                    "expanded package evidence entry");
                hash.AppendData(buffer, 0, read);
            }

            return Convert.ToHexStringLower(hash.GetHashAndReset());
        }
        catch (InvalidDataException exception)
        {
            throw new DeterministicValidationException(
                "The nupkg is not a valid ZIP package.",
                exception);
        }
    }

    public static IReadOnlyDictionary<string, string> GetFileEntryDigests(
        string path,
        string extension)
    {
        SafePath.EnsureRegularFile(path);
        var bytes = BoundedIO.ReadAllBytes(path, ResourceLimits.NupkgBytes, "nupkg");
        try
        {
            using var packageStream = new MemoryStream(bytes, writable: false);
            using var archive = new ZipArchive(packageStream, ZipArchiveMode.Read, leaveOpen: false);
            ValidateArchiveEntries(archive);
            var result = new SortedDictionary<string, string>(StringComparer.Ordinal);
            foreach (var entry in archive.Entries.Where(entry =>
                         !entry.FullName.EndsWith('/') &&
                         entry.FullName.EndsWith(extension, StringComparison.OrdinalIgnoreCase)))
            {
                BoundedIO.EnsureLength(
                    entry.Length,
                    ResourceLimits.NupkgBytes,
                    "expanded package entry");
                using var stream = entry.Open();
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var buffer = new byte[64 * 1024];
                long total = 0;
                while (true)
                {
                    var read = stream.Read(buffer, 0, buffer.Length);
                    if (read == 0)
                    {
                        break;
                    }

                    total = checked(total + read);
                    BoundedIO.EnsureLength(total, ResourceLimits.NupkgBytes, "expanded package entry");
                    hash.AppendData(buffer, 0, read);
                }

                if (!result.TryAdd(
                        entry.FullName,
                        Convert.ToHexStringLower(hash.GetHashAndReset())))
                {
                    throw new DeterministicValidationException(
                        $"Package contains duplicate entry '{entry.FullName}'.");
                }
            }

            return result;
        }
        catch (InvalidDataException exception)
        {
            throw new DeterministicValidationException(
                "The nupkg is not a valid ZIP package.",
                exception);
        }
    }

    private static (string Id, string Version) ReadIdentity(byte[] nuspecBytes)
    {
        try
        {
            using var stream = new MemoryStream(nuspecBytes, writable: false);
            using var reader = XmlReader.Create(stream, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = ResourceLimits.NuspecBytes,
                IgnoreComments = false,
                IgnoreWhitespace = false
            });
            var document = XDocument.Load(reader, LoadOptions.None);
            var root = document.Root;
            if (root is null || root.Name.LocalName != "package")
            {
                throw new DeterministicValidationException("The nuspec root element must be 'package'.");
            }

            var metadata = SingleChild(root, "metadata");
            var id = SingleChild(metadata, "id").Value.Trim().ToLowerInvariant();
            var version = NuGetVersionNormalizer.Normalize(
                SingleChild(metadata, "version").Value.Trim());
            ValidateIdentityValue(id, "package ID", PackageIdPattern, maximumLength: 100);
            return (id, version);
        }
        catch (XmlException exception)
        {
            throw new DeterministicValidationException($"The nuspec is not safe, well-formed XML: {exception.Message}", exception);
        }
    }

    private static XElement SingleChild(XElement parent, string localName)
    {
        var matches = parent.Elements().Where(element => element.Name.LocalName == localName).ToArray();
        if (matches.Length != 1)
        {
            throw new DeterministicValidationException(
                $"The nuspec must contain exactly one '{localName}' element beneath '{parent.Name.LocalName}'.");
        }

        return matches[0];
    }

    private static void ValidateIdentityValue(string value, string name, Regex pattern, int maximumLength)
    {
        if (value.Length is 0 || value.Length > maximumLength || value.Any(char.IsControl) || !pattern.IsMatch(value))
        {
            throw new DeterministicValidationException($"The nuspec {name} is empty or invalid.");
        }
    }

    private static bool IsSymbolicLink(ZipArchiveEntry entry) =>
        ((entry.ExternalAttributes >> 16) & UnixFileTypeMask) == UnixSymbolicLink;

    private static void ValidateArchiveEntries(ZipArchive archive)
    {
        foreach (var entry in archive.Entries)
        {
            SafePath.ValidateArchiveEntry(entry.FullName, IsSymbolicLink(entry));
        }
    }
}
