using System.IO.Compression;
using System.Text.Json;
using SkillCatalog.Api.Models;
using SkillCatalog.Api.Options;

namespace SkillCatalog.Api.Services;

public sealed class SkillPackageService(CatalogSnapshotProvider provider, SkillCatalogOptions options)
{
    public byte[] Create(SkillDetail skill)
    {
        var directory = SafeRepositoryPath.Resolve(provider.RepositoryRoot, "plugins", skill.Plugin, "skills", Path.GetFileName(new Uri(skill.SourceUrl).AbsolutePath));
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, true))
        {
            foreach (var path in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            {
                if (!SafeRepositoryPath.IsSafeRegularFile(directory, path, options.MaxArchiveFileBytes)) continue;
                archive.CreateEntryFromFile(path, Path.GetRelativePath(directory, path).Replace('\\','/'), CompressionLevel.Fastest);
            }
            var manifest = archive.CreateEntry("skill-package.json");
            using var writer = new StreamWriter(manifest.Open());
            writer.Write(JsonSerializer.Serialize(new { skill.Plugin, skill.Name, skill.SourceUrl, Revision = provider.Snapshot.Revision, CreatedAt = DateTimeOffset.UtcNow }));
        }
        return output.ToArray();
    }
}
