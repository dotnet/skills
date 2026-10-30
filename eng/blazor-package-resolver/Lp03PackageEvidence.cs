using System.IO.Compression;
using System.Text;

namespace BlazorPackageResolver;

internal sealed record Lp03PackageEvidence(string Id, string Version, string? Expression, string? GrantSha256)
{
    internal static Lp03PackageEvidence? Read(ReadOnlyMemory<byte> content, string? grantEntry)
    {
        if (content.Length is 0 or > 8 * 1024 * 1024 ||
            !NuGetNuspecReader.HasBoundedDirectory(content.Span, out var count))
            return null;
        try
        {
            using var stream = new MemoryStream(content.ToArray(), writable: false);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, false, Lp01ArchiveIntegrity.Cp437);
            var entries = archive.Entries;
            if (entries.Count != count || entries.Any(entry =>
                !(Lp02PublicSourceResolver.SafePath(entry.FullName.EndsWith('/') ? entry.FullName[..^1] : entry.FullName) ||
                  entry.FullName == "[Content_Types].xml") ||
                entry.FullName.EndsWith('/') && entry.Length != 0 ||
                ((entry.ExternalAttributes >> 16) & 0xf000) == 0xa000) ||
                !Lp01ArchiveIntegrity.Validate(content.Span, entries))
                return null;
            var nuspecs = entries.Where(entry => !entry.FullName.Contains('/') &&
                entry.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (nuspecs.Length != 1 || nuspecs[0].Length > 1024 * 1024)
                return null;
            using var xmlStream = nuspecs[0].Open();
            using var xml = new MemoryStream();
            xmlStream.CopyTo(xml);
            if (!Lp01LicensePolicyResolver.SupportedXmlEncoding(xml.ToArray()))
                return null;
            var read = NuGetNuspecReader.Read(content);
            if (read.Metadata is not { } metadata)
                return null;
            string? Single(string name)
            {
                var fields = metadata.Elements().Where(e => e.Name.LocalName == name).ToArray();
                return fields.Length == 1 && fields[0].Name.Namespace == metadata.Name.Namespace &&
                    !fields[0].HasElements ? fields[0].Value : null;
            }
            var id = Single("id");
            var version = Single("version");
            if (id is null || id.Any(ch => ch > 127) || version is null)
                return null;
            id = string.Concat(id.Select(ch => ch is >= 'A' and <= 'Z' ? (char)(ch + 32) : ch));
            if (!Lp03DependencyLicensesResolver.ValidId(id) ||
                !Lp03DependencyLicensesResolver.ValidVersion(version))
                return null;
            var licenses = metadata.Elements().Where(e => e.Name.LocalName == "license").ToArray();
            if (licenses.Length > 1 || licenses.Any(e => e.HasElements || e.Name.Namespace != metadata.Name.Namespace))
                return null;
            string? expression = licenses.Length == 1 &&
                (string?)licenses[0].Attribute("type") == "expression" ? licenses[0].Value : null;
            string? grantDigest = null;
            if (grantEntry is not null && archive.GetEntry(grantEntry) is { Length: <= 16384 } grant)
            {
                using var grantStream = grant.Open();
                grantDigest = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(grantStream));
            }
            return new(id, version, expression, grantDigest);
        }
        catch (Exception error) when (error is InvalidDataException or IOException or
            NotSupportedException or DecoderFallbackException or System.Xml.XmlException or ArgumentException)
        {
            return null;
        }
    }
}
