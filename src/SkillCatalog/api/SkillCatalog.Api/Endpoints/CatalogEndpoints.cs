using SkillCatalog.Api.Models;
using SkillCatalog.Api.Services;

namespace SkillCatalog.Api.Endpoints;

public static class CatalogEndpoints
{
    public static IEndpointRouteBuilder MapCatalogEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/catalog", (CatalogSnapshotProvider provider) =>
        {
            var snapshot = provider.Snapshot;
            return Results.Ok(new CatalogSummary(snapshot.Skills.Select(x => x.Plugin).Distinct(StringComparer.OrdinalIgnoreCase).Count(), snapshot.Skills.Count, snapshot.Revision, snapshot.RefreshedAt, snapshot.Skills.Select(x => x.Plugin).Distinct(StringComparer.OrdinalIgnoreCase).Order().ToArray(), snapshot.Diagnostics));
        }).WithName("GetCatalog");
        app.MapGet("/api/skills", (string? q, string? plugin, int? page, int? pageSize, SkillSearchService search) =>
        {
            var p = Math.Max(1, page ?? 1); var size = Math.Clamp(pageSize ?? 24, 1, 100);
            return Results.Ok(search.Search(q, plugin, p, size));
        }).WithName("SearchSkills");
        return app;
    }
}
