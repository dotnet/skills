using Microsoft.AspNetCore.Antiforgery;
using SkillCatalog.Api.Auth;
using SkillCatalog.Api.GitHub;
using SkillCatalog.Api.Models;
using SkillCatalog.Api.Services;

namespace SkillCatalog.Api.Endpoints;

public static class GitHubSubmissionEndpoints
{
    public static IEndpointRouteBuilder MapGitHubSubmissionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/contributions").WithTags("GitHub contributions");

        group.MapPost("/intents", async (
            IFormFile file,
            HttpContext context,
            GitHubContributorAuthentication auth,
            IAntiforgery antiforgery,
            SubmissionIntentService intents,
            CancellationToken cancellationToken) =>
        {
            var session = await auth.GetSessionAsync(
                context.Request.Cookies[GitHubContributorAuthentication.CookieName], cancellationToken);
            if (session is null)
            {
                return Results.Unauthorized();
            }
            if (!await ValidateAntiforgeryAsync(context, antiforgery))
            {
                return SecurityTokenFailure(context);
            }

            try
            {
                var result = await intents.CreateAsync(session.Id, auth.UnprotectToken(session), file, cancellationToken);
                return Results.Created($"/api/contributions/intents/{result.Intent.Id}", result.View);
            }
            catch (SubmissionValidationException exception)
            {
                return Results.UnprocessableEntity(exception.Inspection);
            }
            catch (InvalidDataException exception)
            {
                return Results.BadRequest(new ApiFailure(
                    "validation", exception.Message, "Upload a repository-shaped package.", context.TraceIdentifier));
            }
        }).DisableAntiforgery();

        group.MapPost("/intents/{intentId:guid}/submit", async (
            Guid intentId,
            IFormFile file,
            HttpContext context,
            GitHubContributorAuthentication auth,
            IAntiforgery antiforgery,
            SubmissionIntentService intents,
            ContributionIdempotencyService idempotency,
            NewSkillContributionService contributions,
            SkillUpdateContributionService updates,
            CancellationToken cancellationToken) =>
        {
            var session = await auth.GetSessionAsync(
                context.Request.Cookies[GitHubContributorAuthentication.CookieName], cancellationToken);
            if (session is null)
            {
                return Results.Unauthorized();
            }
            if (!await ValidateAntiforgeryAsync(context, antiforgery))
            {
                return SecurityTokenFailure(context);
            }

            var key = context.Request.Headers["Idempotency-Key"].ToString();
            if (string.IsNullOrWhiteSpace(key) || key.Length > 128)
            {
                return Results.BadRequest(new ApiFailure(
                    "validation",
                    "A valid Idempotency-Key header is required.",
                    "Review and confirm the submission again.",
                    context.TraceIdentifier));
            }

            try
            {
                var parsed = await intents.RevalidateAsync(intentId, session.Id, file, string.Equals(context.Request.Headers["X-Confirm-Update"], "true", StringComparison.OrdinalIgnoreCase), cancellationToken);
                var reviewedChanges = await updates.ValidateAsync(parsed.Intent, session, auth.UnprotectToken(session), parsed.Files, cancellationToken);
                var acquired = await idempotency.AcquireAsync(
                    session.GitHubUserId, key, intentId, cancellationToken);
                if (acquired.Existing is not null)
                {
                    return Results.Ok(NewSkillContributionService.View(acquired.Existing));
                }

                var contribution = await contributions.ExecuteAsync(
                    parsed.Intent,
                    session,
                    auth.UnprotectToken(session),
                    reviewedChanges,
                    cancellationToken);
                await idempotency.CompleteAsync(acquired.Lease, contribution.Id, cancellationToken);
                return Results.Created(
                    $"/api/contributions/{contribution.Id}",
                    NewSkillContributionService.View(contribution));
            }
            catch (SubmissionValidationException exception)
            {
                return Results.UnprocessableEntity(exception.Inspection);
            }
            catch (RepositoryRevisionConflictException exception)
            {
                return Results.Conflict(new ApiFailure(
                    "conflict", exception.Message, "Refresh and review the updated repository revision.", context.TraceIdentifier));
            }
            catch (UnauthorizedAccessException exception)
            {
                return Results.Problem(title: "Update authorization failed", detail: exception.Message, statusCode: 403);
            }
            catch (GitHubForkRequiredException exception)
            {
                return Results.Problem(
                    title: "Contributor fork required",
                    detail: exception.Message,
                    statusCode: StatusCodes.Status403Forbidden,
                    extensions: new Dictionary<string, object?>
                    {
                        ["forkUrl"] = exception.ForkUrl,
                        ["category"] = "authorization"
                    });
            }
            catch (GitHubContributionRecoveryException exception)
            {
                return Results.Problem(
                    title: "GitHub contribution requires recovery",
                    detail: exception.Contribution.RecoveryMessage,
                    statusCode: StatusCodes.Status503ServiceUnavailable,
                    extensions: new Dictionary<string, object?>
                    {
                        ["category"] = "partial-success",
                        ["contribution"] = NewSkillContributionService.View(exception.Contribution)
                    });
            }
            catch (GitHubRateLimitException exception)
            {
                context.Response.Headers.RetryAfter = ((int)(exception.RetryAfter ?? TimeSpan.FromMinutes(1)).TotalSeconds).ToString();
                return Results.Problem(
                    title: "GitHub rate limit",
                    statusCode: StatusCodes.Status429TooManyRequests,
                    extensions: new Dictionary<string, object?> { ["category"] = "rate-limit" });
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
            catch (InvalidOperationException exception)
            {
                return Results.Conflict(new ApiFailure(
                    "conflict", exception.Message, "Refresh and review the submission.", context.TraceIdentifier));
            }
        }).DisableAntiforgery();

        return app;
    }

    private static async Task<bool> ValidateAntiforgeryAsync(HttpContext context, IAntiforgery antiforgery)
    {
        try
        {
            await antiforgery.ValidateRequestAsync(context);
            return true;
        }
        catch (AntiforgeryValidationException)
        {
            return false;
        }
    }

    private static IResult SecurityTokenFailure(HttpContext context) => Results.BadRequest(new ApiFailure(
        "authentication",
        "The security token is missing or expired.",
        "Refresh the page and try again.",
        context.TraceIdentifier));
}


