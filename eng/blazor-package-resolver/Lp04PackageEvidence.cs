using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace BlazorPackageResolver;

internal sealed record Lp04EntryEvidence(string Sha256, byte[]? Content);
internal sealed record Lp04PackageEvidence(string Id, string Version,
    Dictionary<string, Lp04EntryEvidence> Entries, bool Duplicates, bool Aliases, bool Safe)
{
    internal static Lp04PackageEvidence? Read(ReadOnlyMemory<byte> content, HashSet<string> selected, string? weakened)
    {
        if (content.Length == 0 || weakened != "archive-byte-limit" && content.Length > 8 * 1024 * 1024 ||
            !NuGetNuspecReader.HasBoundedDirectory(content.Span, out var count)) return null;
        try
        {
            using var stream = new MemoryStream(content.ToArray(), writable: false);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, false, Lp01ArchiveIntegrity.Cp437);
            var entries = archive.Entries;
            var names = entries.Select(entry => entry.FullName).ToArray();
            var duplicates = names.Distinct(StringComparer.Ordinal).Count() != names.Length;
            var aliases = names.Select(Lp01CaseFold.Fold).Distinct(StringComparer.Ordinal).Count() !=
                names.Distinct(StringComparer.Ordinal).Count();
            var allowNames = weakened == "dependency-safety" ||
                weakened == "unique-package-paths" && !aliases || weakened == "no-path-aliases" && !duplicates;
            if (entries.Count != count || entries.Any(entry =>
                entry.FullName.EndsWith('/') && entry.Length != 0 ||
                ((entry.ExternalAttributes >> 16) & 0xf000) == 0xa000) ||
                !Lp01ArchiveIntegrity.Validate(content.Span, entries, allowNameAliases: allowNames)) return null;
            var safe = names.All(Lp04NoticeCoverageResolver.SafePath);
            var nuspecs = entries.Where(entry => !entry.FullName.Contains('/') &&
                entry.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (nuspecs.Length != 1 || nuspecs[0].Length > 1024 * 1024) return null;
            using (var xmlSource = nuspecs[0].Open())
            using (var xml = new MemoryStream())
            {
                xmlSource.CopyTo(xml);
                if (!Lp01LicensePolicyResolver.SupportedXmlEncoding(xml.ToArray())) return null;
            }
            var read = NuGetNuspecReader.Read(content);
            if (read.Metadata is not { } metadata) return null;
            string? Single(string name)
            {
                var fields = metadata.Elements().Where(element => element.Name.LocalName == name).ToArray();
                return fields.Length == 1 && fields[0].Name.Namespace == metadata.Name.Namespace &&
                    !fields[0].HasElements ? fields[0].Value : null;
            }
            var id = Single("id");
            var version = Single("version");
            if (id is null || id.Any(ch => ch > 127) || version is null) return null;
            id = id.ToLowerInvariant();
            if (!Lp03DependencyLicensesResolver.ValidId(id) || !Lp03DependencyLicensesResolver.ValidVersion(version)) return null;
            var observed = new Dictionary<string, Lp04EntryEvidence>(StringComparer.Ordinal);
            foreach (var entry in entries)
            {
                using var source = entry.Open();
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                using var retained = selected.Contains(entry.FullName) ? new MemoryStream() : null;
                var chunk = new byte[8192];
                int length;
                while ((length = source.Read(chunk)) > 0)
                {
                    hash.AppendData(chunk.AsSpan(0, length));
                    retained?.Write(chunk.AsSpan(0, length));
                }
                observed[entry.FullName] = new(Convert.ToHexStringLower(hash.GetHashAndReset()), retained?.ToArray());
            }
            return new(id, version, observed, duplicates, aliases, safe);
        }
        catch (Exception error) when (error is InvalidDataException or IOException or
            NotSupportedException or DecoderFallbackException or System.Xml.XmlException or ArgumentException)
        {
            return null;
        }
    }
}
