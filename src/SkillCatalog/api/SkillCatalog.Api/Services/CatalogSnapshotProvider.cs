using SkillCatalog.Api.Models;
using SkillCatalog.Api.Options;

namespace SkillCatalog.Api.Services;

public sealed class CatalogSnapshotProvider
{
    public CatalogSnapshot Snapshot { get; }
    public string RepositoryRoot { get; }

    public CatalogSnapshotProvider(SkillCatalogOptions options)
    {
        RepositoryRoot = FindRepositoryRoot(options.RepositoryRoot);
        Snapshot = new CatalogSnapshotBuilder(options).Build(RepositoryRoot);
    }

    private static string FindRepositoryRoot(string configured)
    {
        var configuredPath = Path.GetFullPath(configured, Directory.GetCurrentDirectory());
        if (Directory.Exists(Path.Combine(configuredPath, "plugins"))) return configuredPath;
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var directory = new DirectoryInfo(start);
            while (directory is not null)
            {
                if (Directory.Exists(Path.Combine(directory.FullName, "plugins")) && Directory.Exists(Path.Combine(directory.FullName, ".git"))) return directory.FullName;
                directory = directory.Parent;
            }
        }
        throw new DirectoryNotFoundException("Could not locate a repository root containing the plugins directory.");
    }
}
