using SkillCatalog.Api.Models;
using SkillCatalog.Api.Options;
using SkillCatalog.Api.Services;

namespace SkillCatalog.Api.Endpoints;

public static class SkillEndpoints
{
    public static IEndpointRouteBuilder MapSkillEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/skills/{plugin}/{skill}", (string plugin, string skill, CatalogSnapshotProvider provider) =>
            Find(provider, plugin, skill) is { } item ? Results.Ok(item) : Results.NotFound()).WithName("GetSkill");
        app.MapGet("/api/skills/{plugin}/{skill}/resources", (string plugin, string skill, string path, CatalogSnapshotProvider provider, SkillCatalogOptions options) =>
        {
            var item = Find(provider, plugin, skill); if (item is null) return Results.NotFound();
            var resource = item.Resources.FirstOrDefault(x => x.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
            if (resource is null || !resource.Previewable) return Results.NotFound();
            var directory = SafeRepositoryPath.Resolve(provider.RepositoryRoot, "plugins", plugin, "skills", skill);
            var file = SafeRepositoryPath.Resolve(directory, path);
            if (!SafeRepositoryPath.IsSafeRegularFile(directory, file, options.MaxPreviewBytes)) return Results.BadRequest();
            return Results.Text(File.ReadAllText(file), "text/plain; charset=utf-8");
        }).WithName("PreviewSkillResource");
        app.MapGet("/api/skills/{plugin}/{skill}/download", (string plugin, string skill, CatalogSnapshotProvider provider, SkillPackageService packages) =>
        {
            var item = Find(provider, plugin, skill); if (item is null) return Results.NotFound();
            return Results.File(packages.Create(item), "application/zip", $"{SafeName(plugin)}-{SafeName(skill)}.zip");
        }).WithName("DownloadSkill");
        return app;
    }
    private static SkillDetail? Find(CatalogSnapshotProvider provider, string plugin, string skill) => provider.Snapshot.Skills.FirstOrDefault(x => x.Plugin.Equals(plugin, StringComparison.OrdinalIgnoreCase) && x.Name.Equals(skill, StringComparison.OrdinalIgnoreCase));
    private static string SafeName(string value) => new(value.Where(x => char.IsLetterOrDigit(x) || x is '-' or '_').ToArray());
}
