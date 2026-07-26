using SkillCatalog.Api.Services;

namespace SkillCatalog.Api.Endpoints;

public static class SubmissionEndpoints
{
    public static IEndpointRouteBuilder MapSubmissionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/submissions").WithTags("Skill submissions").DisableAntiforgery();
        group.MapGet("/options", (SubmissionRuleProvider rules) => Results.Ok(rules.GetOptions()));
        group.MapPost("/inspect", async (IFormFile file, SkillPackageParser parser, CatalogTelemetry telemetry, CancellationToken token) =>
        {
            var started = System.Diagnostics.Stopwatch.StartNew();
            try { var result = await parser.InspectAsync(file, token); telemetry.Submission("inspect", started.Elapsed.TotalMilliseconds, result.Inspection.Preview.Entries.Count, 0, result.Inspection.Findings.Select(x => x.Code)); return Results.Ok(result.Inspection); }
            catch (InvalidDataException ex) { return Results.Problem(ex.Message, statusCode: StatusCodes.Status413PayloadTooLarge); }
        });
        group.MapPost("/normalize", async (IFormFile file, SkillPackageParser parser, CancellationToken token) =>
        {
            try { var result = await parser.InspectAsync(file, token); if (!result.Inspection.Valid) return Results.UnprocessableEntity(result.Inspection); var name = result.Inspection.Preview.Name ?? "skill"; return Results.File(SkillPackageParser.Normalize(result.Files), "application/zip", $"{name}-normalized.zip"); }
            catch (InvalidDataException ex) { return Results.Problem(ex.Message, statusCode: StatusCodes.Status413PayloadTooLarge); }
        });
        return app;
    }
}