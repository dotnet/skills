using System.Formats.Tar;
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using BlazorComponentReadiness.Validator.Validation;

namespace BlazorComponentReadiness.Validator.IO;

/// <summary>
/// Verifies selected files against a retained source archive without invoking Git or extracting it.
/// </summary>
public static partial class SourceArchiveCaptureService
{
    /// <summary>
    /// Inventories actual archive paths without establishing source provenance or project completeness.
    /// </summary>
    public static byte[] InventoryArchive(
        string root,
        string archivePath,
        string sourceRoot,
        string? archivePrefix,
        string archiveFormat,
        string? expectedArchiveSha256,
        string outputPath)
    {
        var document = BuildInventory(
            root,
            archivePath,
            sourceRoot,
            archivePrefix,
            archiveFormat,
            expectedArchiveSha256,
            outputPath);
        var bytes = SerializeInventory(document);
        byte[] persistedBytes = [.. bytes, (byte)'\n'];
        BoundedIO.EnsureLength(persistedBytes.Length, ResourceLimits.SerializedArtifactBytes, "source inventory");
        var fullRoot = EnsureRoot(root);
        AtomicFile.WriteNew(
            fullRoot,
            ResolveOutput(fullRoot, outputPath),
            persistedBytes);
        return bytes;
    }

    /// <summary>
    /// Revalidates an inventory and resolves its selected IDs through the existing source capture verifier.
    /// </summary>
    public static byte[] CaptureInventory(
        string root,
        string inventoryPath,
        IReadOnlyList<string> selectedEntryIds,
        string repositoryUri,
        string sourceCommit,
        string sourceMapping,
        string sourceConfidence,
        string acquisitionLocator,
        string outputPath)
    {
        var fullRoot = EnsureRoot(root);
        var (inventoryRelativePath, inventoryFullPath) = ResolveFile(
            fullRoot,
            inventoryPath,
            "source inventory");
        var inventoryBytes = BoundedIO.ReadAllBytes(
            inventoryFullPath,
            ResourceLimits.SerializedArtifactBytes,
            "source inventory");
        var inventory = ParseInventory(inventoryBytes);
        var canonicalInventory = SerializeInventory(inventory);
        var inventoryWithoutFinalNewline = inventoryBytes.Length > 0 &&
            inventoryBytes[^1] == (byte)'\n'
                ? inventoryBytes[..^1]
                : inventoryBytes;
        if (!inventoryWithoutFinalNewline.AsSpan().SequenceEqual(canonicalInventory))
        {
            throw new DeterministicValidationException(
                "The source inventory is not canonical.");
        }

        var archivePrefix = inventory.ArchiveEntryPrefix.Length == 0
            ? null
            : inventory.ArchiveEntryPrefix;
        var recomputed = BuildInventory(
            fullRoot,
            inventory.ArchivePath,
            inventory.SourceRoot,
            archivePrefix,
            inventory.ArchiveFormat,
            inventory.ArchiveSha256,
            inventoryRelativePath);
        if (!SerializeInventory(recomputed).AsSpan().SequenceEqual(canonicalInventory))
        {
            throw new DeterministicValidationException(
                "The source inventory does not match the retained archive.");
        }

        var selected = ResolveInventoryEntryIds(inventory, selectedEntryIds);
        return Capture(
            fullRoot,
            inventory.ArchivePath,
            inventory.SourceRoot,
            archivePrefix,
            inventory.ArchiveFormat,
            repositoryUri,
            sourceCommit,
            sourceMapping,
            sourceConfidence,
            acquisitionLocator,
            selected,
            inventory.ArchiveSha256,
            outputPath);
    }

    /// <summary>
    /// Captures selected source files and writes a new deterministic receipt beneath <paramref name="root"/>.
    /// </summary>
    public static byte[] Capture(
        string root,
        string archivePath,
        string sourceRoot,
        string? archivePrefix,
        string archiveFormat,
        string repositoryUri,
        string sourceCommit,
        string sourceMapping,
        string sourceConfidence,
        string acquisitionLocator,
        IReadOnlyList<string> selectedSourcePaths,
        string? expectedArchiveSha256,
        string outputPath)
    {
        var fullRoot = EnsureRoot(root);
        var (archiveRelativePath, fullArchivePath) = ResolveFile(fullRoot, archivePath, "archive");
        var (sourceRootRelativePath, _) = ResolveDirectory(
            fullRoot,
            sourceRoot,
            "source root");
        var outputRelativePath = ResolveOutput(fullRoot, outputPath);
        if (outputRelativePath.StartsWith(sourceRootRelativePath + "/", StringComparison.OrdinalIgnoreCase))
        {
            throw new DeterministicValidationException(
                "The source capture receipt must be outside the extracted source directory.");
        }

        var format = NormalizeFormat(archiveFormat, fullArchivePath);
        var prefix = NormalizeArchivePrefix(archivePrefix);
        var selected = NormalizeSelectedSourcePaths(selectedSourcePaths);
        var source = NormalizeSource(
            repositoryUri,
            sourceCommit,
            sourceMapping,
            sourceConfidence,
            acquisitionLocator);

        var archiveBytes = BoundedIO.ReadAllBytes(
            fullArchivePath,
            ResourceLimits.SourceArchiveBytes,
            "source archive");
        var archiveSha256 = Convert.ToHexStringLower(SHA256.HashData(archiveBytes));
        if (expectedArchiveSha256 is not null &&
            !string.Equals(
                NormalizeSha256(expectedArchiveSha256, "expected archive SHA-256"),
                archiveSha256,
                StringComparison.Ordinal))
        {
            throw new DeterministicValidationException(
                "The retained source archive does not match the expected SHA-256.");
        }

        var selectedEntries = selected.ToDictionary(
            path => ArchivePath(prefix, path),
            path => path,
            StringComparer.Ordinal);
        var captured = format switch
        {
            "tar.gz" => ReadTarGzip(archiveBytes, selectedEntries),
            "zip" => ReadZip(archiveBytes, selectedEntries),
            _ => throw new DeterministicValidationException($"Unsupported source archive format '{format}'.")
        };

        var artifacts = selected
            .Select(sourcePath =>
            {
                var archiveEntry = ArchivePath(prefix, sourcePath);
                if (!captured.TryGetValue(archiveEntry, out var archiveContent))
                {
                    throw new DeterministicValidationException(
                        $"The source archive does not contain selected file '{archiveEntry}'.");
                }

                var contentPath = Canonicalization.RelativePath(
                    Path.Combine(sourceRootRelativePath, sourcePath).Replace('\\', '/'),
                    "source artifact content_path");
                var fullContentPath = SafePath.ResolveUnderRoot(
                    fullRoot,
                    contentPath,
                    requireExisting: true,
                    requireFile: true);
                var localContent = BoundedIO.ReadAllBytes(
                    fullContentPath,
                    ResourceLimits.SourceArchiveExpandedBytes,
                    "selected source file");
                if (!localContent.SequenceEqual(archiveContent.Bytes))
                {
                    throw new DeterministicValidationException(
                        $"Selected source file '{sourcePath}' does not match archive entry '{archiveEntry}'.");
                }

                var contentSha256 = Convert.ToHexStringLower(SHA256.HashData(localContent));
                if (!string.Equals(contentSha256, archiveContent.Sha256, StringComparison.Ordinal))
                {
                    throw new DeterministicValidationException(
                        $"Selected source file '{sourcePath}' has inconsistent archive and local digests.");
                }

                return new CapturedArtifact(sourcePath, contentPath, archiveEntry, contentSha256);
            })
            .ToArray();

        var receipt = StrictJson.SerializeCanonical(writer =>
        {
            writer.WriteStartObject();
            writer.WriteNumber("schema_version", 1);
            writer.WriteString("capture_kind", "source-archive");
            writer.WriteString("archive_format", format);
            writer.WriteString("archive_path", archiveRelativePath);
            writer.WriteString("source_root", sourceRootRelativePath);
            writer.WriteString("archive_entry_prefix", prefix);
            writer.WritePropertyName("declared_source");
            writer.WriteStartObject();
            writer.WriteString("availability", "source-available");
            writer.WriteString("repository_uri", source.RepositoryUri);
            writer.WriteString("commit", source.Commit);
            writer.WriteString("mapping", source.Mapping);
            writer.WriteString("confidence", source.Confidence);
            writer.WriteString("acquisition_locator", source.AcquisitionLocator);
            writer.WriteEndObject();
            writer.WritePropertyName("archive");
            writer.WriteStartObject();
            writer.WriteString("path", archiveRelativePath);
            writer.WriteString("format", format);
            writer.WriteString("sha256", archiveSha256);
            writer.WriteNumber("size", archiveBytes.LongLength);
            writer.WriteEndObject();
            writer.WritePropertyName("source_artifacts");
            writer.WriteStartArray();
            foreach (var artifact in artifacts)
            {
                writer.WriteStartObject();
                writer.WriteString("source_path", artifact.SourcePath);
                writer.WriteString("content_path", artifact.ContentPath);
                writer.WriteString("archive_entry", artifact.ArchiveEntry);
                writer.WriteString("archive_content_sha256", artifact.ContentSha256);
                writer.WriteString("content_sha256", artifact.ContentSha256);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        });

        AtomicFile.WriteNew(
            fullRoot,
            outputRelativePath,
            receipt.Concat(Encoding.UTF8.GetBytes("\n")).ToArray());
        return receipt;
    }

    private static Dictionary<string, CapturedArchiveContent> ReadZip(
        byte[] archiveBytes,
        IReadOnlyDictionary<string, string> selectedEntries,
        string? inventoryPrefix = null,
        ICollection<InventoryEntry>? inventoryEntries = null,
        FullArchiveScan? full = null)
    {
        try
        {
            using var stream = new MemoryStream(archiveBytes, writable: false);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
            var names = new HashSet<string>(StringComparer.Ordinal);
            var result = new Dictionary<string, CapturedArchiveContent>(StringComparer.Ordinal);
            long expandedBytes = 0;
            var entryCount = 0;
            foreach (var entry in archive.Entries)
            {
                if (++entryCount > ResourceLimits.SourceArchiveEntryCount)
                {
                    throw new ResourceLimitException(
                        "source archive entries",
                        ResourceLimits.SourceArchiveEntryCount,
                        entryCount);
                }

                SafePath.ValidateArchiveEntry(entry.FullName, IsZipSymbolicLink(entry));
                full?.ZipMember(entry);
                if (!names.Add(CanonicalArchiveName(entry.FullName)))
                {
                    throw new DeterministicValidationException(
                        $"The source archive contains duplicate entry '{entry.FullName}'.");
                }

                if (entry.FullName.EndsWith('/', StringComparison.Ordinal))
                {
                    continue;
                }

                AddInventoryEntry(inventoryEntries, inventoryPrefix, entry.FullName);
                BoundedIO.EnsureLength(
                    entry.Length,
                    ResourceLimits.SourceArchiveExpandedBytes,
                    "source archive entry");
                using var entryStream = entry.Open();
                var selected = selectedEntries.ContainsKey(entry.FullName);
                var previousBytes = expandedBytes;
                var content = ReadEntry(entryStream, selected, ref expandedBytes);
                full?.ReadSize(entry.Length, expandedBytes - previousBytes);
                if (selected)
                {
                    result.Add(
                        entry.FullName,
                        new CapturedArchiveContent(
                            content,
                            Convert.ToHexStringLower(SHA256.HashData(content))));
                }
            }

            return result;
        }
        catch (InvalidDataException exception)
        {
            throw new DeterministicValidationException(
                "The source archive is not a valid ZIP archive.",
                exception);
        }
    }

    private static Dictionary<string, CapturedArchiveContent> ReadTarGzip(
        byte[] archiveBytes,
        IReadOnlyDictionary<string, string> selectedEntries,
        string? inventoryPrefix = null,
        ICollection<InventoryEntry>? inventoryEntries = null,
        FullArchiveScan? full = null)
    {
        try
        {
            using var compressed = new MemoryStream(archiveBytes, writable: false);
            using var gzip = new GZipStream(compressed, CompressionMode.Decompress, leaveOpen: false);
            // Bound metadata and padding too, before the TAR reader consumes them.
            long decompressedBytes = 0;
            using var expanded = new MemoryStream(ReadEntry(gzip, retain: true, ref decompressedBytes), writable: false);
            if (full is not null) full.TarStreamBytes = decompressedBytes;
            using var reader = new TarReader(expanded, leaveOpen: false);
            var names = new HashSet<string>(StringComparer.Ordinal);
            var result = new Dictionary<string, CapturedArchiveContent>(StringComparer.Ordinal);
            long expandedBytes = 0;
            var entryCount = 0;
            TarEntry? entry;
            while ((entry = reader.GetNextEntry(false)) is not null)
            {
                if (++entryCount > ResourceLimits.SourceArchiveEntryCount)
                {
                    throw new ResourceLimitException(
                        "source archive entries",
                        ResourceLimits.SourceArchiveEntryCount,
                        entryCount);
                }

                if (entry.EntryType == TarEntryType.GlobalExtendedAttributes)
                {
                    if (full is not null) full.ReaderMetadataEntries++;
                    continue;
                }

                var isDirectory = entry.EntryType == TarEntryType.Directory;
                if (entry.EntryType is not (TarEntryType.Directory or TarEntryType.RegularFile or TarEntryType.V7RegularFile))
                {
                    throw new DeterministicValidationException(
                        $"The source archive entry '{entry.Name}' is not a regular file or directory.");
                }

                SafePath.ValidateArchiveEntry(entry.Name, isSymbolicLink: false);
                full?.Member(entry.Name, isDirectory, entry.Length);
                if (!names.Add(CanonicalArchiveName(entry.Name)))
                {
                    throw new DeterministicValidationException(
                        $"The source archive contains duplicate entry '{entry.Name}'.");
                }

                if (isDirectory)
                {
                    continue;
                }

                if (entry.DataStream is null && entry.Length != 0)
                {
                    throw new DeterministicValidationException(
                        $"The source archive file entry '{entry.Name}' has no data stream.");
                }

                AddInventoryEntry(inventoryEntries, inventoryPrefix, entry.Name);
                var selected = selectedEntries.ContainsKey(entry.Name);
                var previousBytes = expandedBytes;
                var content = ReadEntry(entry.DataStream ?? Stream.Null, selected, ref expandedBytes);
                full?.ReadSize(entry.Length, expandedBytes - previousBytes);
                if (selected)
                {
                    result.Add(
                        entry.Name,
                        new CapturedArchiveContent(
                            content,
                            Convert.ToHexStringLower(SHA256.HashData(content))));
                }
            }

            return result;
        }
        catch (InvalidDataException exception)
        {
            throw new DeterministicValidationException(
                "The source archive is not a valid gzip-compressed TAR archive.",
                exception);
        }
    }

    private static byte[] ReadEntry(Stream input, bool retain, ref long expandedBytes)
    {
        using var retained = retain ? new MemoryStream() : null;
        var buffer = new byte[64 * 1024];
        while (true)
        {
            var read = input.Read(buffer, 0, buffer.Length);
            if (read == 0)
            {
                break;
            }

            expandedBytes = checked(expandedBytes + read);
            BoundedIO.EnsureLength(
                expandedBytes,
                ResourceLimits.SourceArchiveExpandedBytes,
                "expanded source archive");
            retained?.Write(buffer, 0, read);
        }

        return retained?.ToArray() ?? [];
    }

    private static string EnsureRoot(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        var fullRoot = Path.GetFullPath(root);
        if (!Directory.Exists(fullRoot) ||
            (File.GetAttributes(fullRoot) & (FileAttributes.Directory | FileAttributes.ReparsePoint)) !=
            FileAttributes.Directory)
        {
            throw new DeterministicValidationException(
                "The source capture root must be an existing regular directory.");
        }

        return fullRoot;
    }

    private static (string RelativePath, string FullPath) ResolveFile(
        string root,
        string path,
        string resource)
    {
        var relative = RelativePath(root, path, resource);
        return (
            relative,
            SafePath.ResolveUnderRoot(root, relative, requireExisting: true, requireFile: true));
    }

    private static (string RelativePath, string FullPath) ResolveDirectory(
        string root,
        string path,
        string resource)
    {
        var relative = RelativePath(root, path, resource);
        var fullPath = SafePath.ResolveUnderRoot(root, relative, requireExisting: true);
        if (!Directory.Exists(fullPath) ||
            (File.GetAttributes(fullPath) & (FileAttributes.Directory | FileAttributes.ReparsePoint)) !=
            FileAttributes.Directory)
        {
            throw new DeterministicValidationException(
                $"The {resource} must be an existing regular directory.");
        }

        return (relative, fullPath);
    }

    private static string ResolveOutput(string root, string path)
    {
        var relative = RelativePath(root, path, "output");
        _ = SafePath.ResolveUnderRoot(root, relative, requireExisting: false);
        return relative;
    }

    private static string RelativePath(string root, string path, string resource)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.IsPathRooted(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(Path.Combine(root, path));
        var relative = Path.GetRelativePath(root, fullPath).Replace('\\', '/');
        return Canonicalization.RelativePath(relative, $"{resource} path");
    }

    private static SourceInventory BuildInventory(
        string root,
        string archivePath,
        string sourceRoot,
        string? archivePrefix,
        string archiveFormat,
        string? expectedArchiveSha256,
        string outputPath)
    {
        var fullRoot = EnsureRoot(root);
        var (archiveRelativePath, fullArchivePath) = ResolveFile(fullRoot, archivePath, "archive");
        var (sourceRootRelativePath, _) = ResolveDirectory(fullRoot, sourceRoot, "source root");
        var outputRelativePath = ResolveOutput(fullRoot, outputPath);
        if (outputRelativePath.StartsWith(sourceRootRelativePath + "/", StringComparison.OrdinalIgnoreCase))
        {
            throw new DeterministicValidationException(
                "The source inventory must be outside the extracted source directory.");
        }

        var format = NormalizeFormat(archiveFormat, fullArchivePath);
        var prefix = NormalizeArchivePrefix(archivePrefix);
        var archiveBytes = BoundedIO.ReadAllBytes(
            fullArchivePath,
            ResourceLimits.SourceArchiveBytes,
            "source archive");
        var archiveSha256 = Convert.ToHexStringLower(SHA256.HashData(archiveBytes));
        if (expectedArchiveSha256 is not null &&
            !string.Equals(
                NormalizeSha256(expectedArchiveSha256, "expected archive SHA-256"),
                archiveSha256,
                StringComparison.Ordinal))
        {
            throw new DeterministicValidationException(
                "The retained source archive does not match the expected SHA-256.");
        }

        var inventoryEntries = new List<InventoryEntry>();
        _ = format switch
        {
            "tar.gz" => ReadTarGzip(archiveBytes, new Dictionary<string, string>(StringComparer.Ordinal), prefix, inventoryEntries),
            "zip" => ReadZip(archiveBytes, new Dictionary<string, string>(StringComparer.Ordinal), prefix, inventoryEntries),
            _ => throw new DeterministicValidationException($"Unsupported source archive format '{format}'.")
        };
        var ordered = inventoryEntries
            .OrderBy(entry => entry.SourcePath, StringComparer.Ordinal)
            .ToArray();
        if (ordered.Length == 0)
        {
            throw new DeterministicValidationException(
                "The source archive prefix contains no regular files.");
        }

        return new SourceInventory(
            1,
            archiveRelativePath,
            sourceRootRelativePath,
            format,
            prefix,
            archiveSha256,
            archiveBytes.LongLength,
            ordered.Select((entry, index) => entry with { Id = index + 1 }).ToArray());
    }

    private static byte[] SerializeInventory(SourceInventory inventory)
    {
        var bytes = StrictJson.SerializeCanonical(writer =>
        {
            writer.WriteStartObject();
            writer.WriteNumber("schema_version", inventory.SchemaVersion);
            writer.WriteString("archive_path", inventory.ArchivePath);
            writer.WriteString("source_root", inventory.SourceRoot);
            writer.WriteString("archive_format", inventory.ArchiveFormat);
            writer.WriteString("archive_entry_prefix", inventory.ArchiveEntryPrefix);
            writer.WriteString("archive_sha256", inventory.ArchiveSha256);
            writer.WriteNumber("archive_size", inventory.ArchiveSize);
            writer.WritePropertyName("entries");
            writer.WriteStartArray();
            foreach (var entry in inventory.Entries)
            {
                writer.WriteStartObject();
                writer.WriteNumber("id", entry.Id);
                writer.WriteString("source_path", entry.SourcePath);
                writer.WriteString("archive_entry", entry.ArchiveEntry);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        });
        BoundedIO.EnsureLength(bytes.Length, ResourceLimits.SerializedArtifactBytes, "source inventory");
        return bytes;
    }

    private static SourceInventory ParseInventory(ReadOnlyMemory<byte> bytes)
    {
        using var document = StrictJson.Parse(bytes, ResourceLimits.SerializedArtifactBytes, "source inventory");
        var root = document.RootElement;
        ContractJson.RequireProperties(
            root,
            "schema_version",
            "archive_path",
            "source_root",
            "archive_format",
            "archive_entry_prefix",
            "archive_sha256",
            "archive_size",
            "entries");
        var entryArray = ContractJson.Array(root, "entries");
        if (entryArray.GetArrayLength() is 0 or > ResourceLimits.SourceArchiveEntryCount)
        {
            throw new DeterministicValidationException(
                $"The source inventory requires between 1 and {ResourceLimits.SourceArchiveEntryCount} entries.");
        }

        var entries = entryArray.EnumerateArray()
            .Select(element =>
            {
                ContractJson.RequireProperties(element, "id", "source_path", "archive_entry");
                return new InventoryEntry(
                    ContractJson.Int32(element, "id"),
                    ContractJson.String(element, "source_path"),
                    ContractJson.String(element, "archive_entry"));
            })
            .ToArray();
        var inventory = new SourceInventory(
            ContractJson.Int32(root, "schema_version"),
            ContractJson.String(root, "archive_path"),
            ContractJson.String(root, "source_root"),
            ContractJson.String(root, "archive_format"),
            ContractJson.String(root, "archive_entry_prefix"),
            ContractJson.String(root, "archive_sha256"),
            ContractJson.Int64(root, "archive_size"),
            entries);
        if (inventory.SchemaVersion != 1 ||
            !inventory.Entries.Select(entry => entry.Id).SequenceEqual(Enumerable.Range(1, inventory.Entries.Count)) ||
            inventory.Entries.Select(entry => entry.SourcePath).Distinct(StringComparer.Ordinal).Count() != inventory.Entries.Count)
        {
            throw new DeterministicValidationException(
                "The source inventory has invalid entry IDs.");
        }

        if (inventory.ArchiveSize is <= 0 or > ResourceLimits.SourceArchiveBytes)
        {
            throw new DeterministicValidationException(
                "The source inventory archive size is outside the source archive bounds.");
        }

        _ = NormalizeFormat(inventory.ArchiveFormat, inventory.ArchivePath);
        _ = NormalizeArchivePrefix(
            inventory.ArchiveEntryPrefix.Length == 0 ? null : inventory.ArchiveEntryPrefix);
        _ = NormalizeSha256(inventory.ArchiveSha256, "archive SHA-256");
        _ = Canonicalization.RelativePath(inventory.ArchivePath, "archive path");
        _ = Canonicalization.RelativePath(inventory.SourceRoot, "source root");
        foreach (var entry in inventory.Entries)
        {
            _ = Canonicalization.RelativePath(entry.SourcePath, "inventory source path");
            SafePath.ValidateArchiveEntry(entry.ArchiveEntry, isSymbolicLink: false);
            if (entry.SourcePath.Length == 0 ||
                entry.ArchiveEntry.Length == 0 ||
                !string.Equals(
                    entry.ArchiveEntry,
                    ArchivePath(inventory.ArchiveEntryPrefix, entry.SourcePath),
                    StringComparison.Ordinal))
            {
                throw new DeterministicValidationException(
                    "The source inventory contains an invalid entry mapping.");
            }
        }

        return inventory;
    }

    private static string[] ResolveInventoryEntryIds(
        SourceInventory inventory,
        IReadOnlyList<string> values)
    {
        if (values.Count is 0 or > ResourceLimits.SourceArchiveSelectionCount)
        {
            throw new DeterministicValidationException(
                $"source inventory IDs require between 1 and {ResourceLimits.SourceArchiveSelectionCount} entries.");
        }

        var ids = values.Select(value =>
        {
            if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var id) ||
                id <= 0)
            {
                throw new DeterministicValidationException(
                    $"Source inventory entry ID '{value}' is not a positive number.");
            }

            return id;
        }).ToArray();
        if (ids.Distinct().Count() != ids.Length)
        {
            throw new DeterministicValidationException(
                "Source inventory entry IDs must be unique.");
        }

        var byId = inventory.Entries.ToDictionary(entry => entry.Id);
        return ids.Select(id => byId.TryGetValue(id, out var entry)
                ? entry.SourcePath
                : throw new DeterministicValidationException(
                    $"Source inventory entry ID '{id}' is unknown."))
            .ToArray();
    }

    private static string NormalizeFormat(string value, string archivePath)
    {
        var format = ContractJson.NormalizeText(value, "archive format", 16).ToLowerInvariant();
        if (format is "tgz")
        {
            format = "tar.gz";
        }

        if (format is not ("tar.gz" or "zip"))
        {
            throw new DeterministicValidationException(
                "archive format must be exactly 'tar.gz' or 'zip'.");
        }

        if (format == "tar.gz" &&
            !archivePath.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase) &&
            !archivePath.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase))
        {
            throw new DeterministicValidationException(
                "A tar.gz source archive must use a .tar.gz or .tgz retained path.");
        }

        if (format == "zip" && !archivePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            throw new DeterministicValidationException(
                "A ZIP source archive must use a .zip retained path.");
        }

        return format;
    }

    private static string NormalizeArchivePrefix(string? value)
    {
        if (value is null)
        {
            return string.Empty;
        }

        var prefix = ContractJson.NormalizeText(value, "archive entry prefix", 1024);
        if (prefix.EndsWith('/', StringComparison.Ordinal))
        {
            prefix = prefix[..^1];
        }

        if (prefix.Length == 0)
        {
            throw new DeterministicValidationException(
                "archive entry prefix must be a nonempty safe relative path when supplied.");
        }

        _ = Canonicalization.RelativePath(prefix, "archive entry prefix");
        SafePath.ValidateArchiveEntry(prefix, isSymbolicLink: false);
        return prefix;
    }

    private static string[] NormalizeSelectedSourcePaths(IReadOnlyList<string> values)
    {
        if (values.Count is 0 or > ResourceLimits.SourceArchiveSelectionCount)
        {
            throw new DeterministicValidationException(
                $"source paths require between 1 and {ResourceLimits.SourceArchiveSelectionCount} entries.");
        }

        var result = values
            .Select(value => Canonicalization.RelativePath(value, "selected source path"))
            .ToArray();
        if (result.Distinct(StringComparer.Ordinal).Count() != result.Length)
        {
            throw new DeterministicValidationException(
                "Selected source paths must be unique.");
        }

        return result.Order(StringComparer.Ordinal).ToArray();
    }

    private static SourceDeclaration NormalizeSource(
        string repositoryUri,
        string sourceCommit,
        string sourceMapping,
        string sourceConfidence,
        string acquisitionLocator) =>
        new(
            Canonicalization.HttpsUri(repositoryUri, requirePath: true),
            Canonicalization.Commit(sourceCommit),
            ContractJson.NormalizeText(sourceMapping, "source mapping", 1024),
            NormalizeConfidence(sourceConfidence),
            Canonicalization.HttpsUri(acquisitionLocator, requirePath: true));

    private static string NormalizeConfidence(string value)
    {
        value = ContractJson.NormalizeText(value, "source confidence", 16).ToLowerInvariant();
        if (value is not ("low" or "medium" or "high"))
        {
            throw new DeterministicValidationException(
                "source confidence must be 'low', 'medium', or 'high'.");
        }

        return value;
    }

    private static string NormalizeSha256(string value, string name)
    {
        value = ContractJson.NormalizeText(value, name, 64).ToLowerInvariant();
        if (value.Length != 64 || value.Any(character =>
                character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
        {
            throw new DeterministicValidationException($"{name} must be canonical lowercase SHA-256.");
        }

        return value;
    }

    private static string ArchivePath(string prefix, string sourcePath) =>
        prefix.Length == 0 ? sourcePath : $"{prefix}/{sourcePath}";

    private static void AddInventoryEntry(
        ICollection<InventoryEntry>? inventoryEntries,
        string? prefix,
        string archiveEntry)
    {
        if (inventoryEntries is null || prefix is null)
        {
            return;
        }

        var sourcePath = prefix.Length == 0
            ? archiveEntry
            : archiveEntry.StartsWith(prefix + "/", StringComparison.Ordinal)
                ? archiveEntry[(prefix.Length + 1)..]
                : null;
        if (sourcePath is not null && sourcePath.Length != 0)
        {
            _ = Canonicalization.RelativePath(sourcePath, "inventory source path");
            inventoryEntries.Add(new InventoryEntry(0, sourcePath, archiveEntry));
        }
    }

    private static string CanonicalArchiveName(string name) =>
        name.EndsWith('/', StringComparison.Ordinal) ? name[..^1] : name;

    private static bool IsZipSymbolicLink(ZipArchiveEntry entry) =>
        ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000;

    private sealed record SourceDeclaration(
        string RepositoryUri,
        string Commit,
        string Mapping,
        string Confidence,
        string AcquisitionLocator);

    private sealed record CapturedArchiveContent(byte[] Bytes, string Sha256);

    private sealed record CapturedArtifact(
        string SourcePath,
        string ContentPath,
        string ArchiveEntry,
        string ContentSha256);

    private sealed record InventoryEntry(
        int Id,
        string SourcePath,
        string ArchiveEntry);

    private sealed record SourceInventory(
        int SchemaVersion,
        string ArchivePath,
        string SourceRoot,
        string ArchiveFormat,
        string ArchiveEntryPrefix,
        string ArchiveSha256,
        long ArchiveSize,
        IReadOnlyList<InventoryEntry> Entries);
}
