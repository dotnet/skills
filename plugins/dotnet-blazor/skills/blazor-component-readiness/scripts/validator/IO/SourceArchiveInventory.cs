using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using BlazorComponentReadiness.Validator.Validation;

namespace BlazorComponentReadiness.Validator.IO;

public sealed record ArchiveMember(string Path, string Kind, long Size);
public sealed record FullSourceInventory(
    int SchemaVersion, string ArchivePath, string ArchiveFormat, string ArchiveSha256, long ArchiveSize,
    int RegularFiles, int Directories, int LogicalMembers, long ExpandedFileBytes,
    int ReaderMetadataEntries, long? TarStreamBytes, long? TarNonFileBytes,
    string MetadataAccounting, IReadOnlyList<ArchiveMember> Members);

public static partial class SourceArchiveCaptureService
{
    // The full logical view uses the same bounded readers, without the old extracted-tree contract.
    public static FullSourceInventory InventoryFullArchive(
        string root, string archivePath, string archiveFormat, string expectedSha256)
    {
        var fullRoot = EnsureRoot(root);
        var (relative, path) = ResolveFile(fullRoot, archivePath, "archive");
        var format = NormalizeFormat(archiveFormat, path);
        var bytes = BoundedIO.ReadAllBytes(path, ResourceLimits.SourceArchiveBytes, "source archive");
        var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        if (hash != NormalizeSha256(expectedSha256, "confirmed archive digest"))
            throw new DeterministicValidationException("Source archive changed from its confirmed binding.");
        var scan = new FullArchiveScan();
        var selections = new Dictionary<string, string>(StringComparer.Ordinal);
        _ = format == "zip" ? ReadZip(bytes, selections, full: scan) : ReadTarGzip(bytes, selections, full: scan);
        return new(1, relative, format, hash, bytes.LongLength,
            scan.Members.Count(member => member.Kind == "file"), scan.Members.Count(member => member.Kind == "directory"),
            scan.Members.Count, scan.ExpandedFileBytes, scan.ReaderMetadataEntries, scan.TarStreamBytes,
            scan.TarStreamBytes - scan.ExpandedFileBytes,
            "Logical files/directories only. Reader-visible global TAR metadata counted separately; TAR non-file bytes include headers, local metadata, padding and terminators. ZIP framing is not expanded content.",
            scan.Members.OrderBy(member => member.Path, StringComparer.Ordinal).ToArray());
    }

    private sealed class FullArchiveScan
    {
        internal List<ArchiveMember> Members { get; } = [];
        internal long ExpandedFileBytes { get; private set; }
        internal long? TarStreamBytes { get; set; }
        internal int ReaderMetadataEntries { get; set; }
        private readonly Dictionary<string, (string Path, bool File)> paths = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> explicitPaths = new(StringComparer.OrdinalIgnoreCase);

        internal void ZipMember(ZipArchiveEntry entry)
        {
            var directory = entry.FullName.EndsWith('/');
            var kind = (entry.ExternalAttributes >> 16) & 0xF000;
            if (kind != 0 && kind != (directory ? 0x4000 : 0x8000))
                throw new DeterministicValidationException($"Unsupported ZIP member kind: {entry.FullName}");
            Member(entry.FullName, directory, entry.Length);
        }

        internal void Member(string name, bool directory, long size)
        {
            var path = name.EndsWith('/') ? name[..^1] : name;
            if (Canonicalization.RelativePath(path, "full inventory path") != path ||
                path != path.Normalize(NormalizationForm.FormC) ||
                path.Any(char.IsControl) || size < 0 || directory && size != 0)
                throw new DeterministicValidationException($"Unsafe or noncanonical full inventory member: {name}");
            if (!explicitPaths.Add(path))
                throw new DeterministicValidationException($"Case/Unicode/duplicate archive collision: {name}");
            var segments = path.Split('/');
            BoundedIO.EnsureLength(segments.Length, 64, "full inventory path depth");
            if (segments.Any(segment => segment.Trim() != segment || segment.EndsWith('.')))
                throw new DeterministicValidationException($"Nonportable archive path segment: {name}");
            var prefix = "";
            for (var index = 0; index < segments.Length; index++)
            {
                prefix = index == 0 ? segments[index] : prefix + "/" + segments[index];
                var file = index == segments.Length - 1 && !directory;
                if (paths.TryGetValue(prefix, out var previous) &&
                    (previous.Path != prefix || previous.File || file))
                    throw new DeterministicValidationException($"File/directory or path alias collision: {name}");
                paths[prefix] = (prefix, file);
                BoundedIO.EnsureLength(paths.Count, ResourceLimits.SourceArchiveEntryCount, "full inventory explicit/implicit paths");
            }
            Members.Add(new(name, directory ? "directory" : "file", size));
        }

        internal void ReadSize(long declared, long actual)
        {
            if (actual != declared)
                throw new DeterministicValidationException("Archive member expanded length differs from declared size.");
            ExpandedFileBytes = checked(ExpandedFileBytes + actual);
        }
    }
}
