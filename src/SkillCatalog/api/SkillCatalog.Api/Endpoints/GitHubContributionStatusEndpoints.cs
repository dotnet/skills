using Microsoft.Extensions.Options;
using SkillCatalog.Api.Auth;
using SkillCatalog.Api.GitHub;
using SkillCatalog.Api.Models;
using SkillCatalog.Api.Options;
using SkillCatalog.Api.Services;

namespace SkillCatalog.Api.Endpoints;

public static class GitHubContributionStatusEndpoints
{
    public static IEndpointRouteBuilder MapGitHubContributionStatusEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/webhooks/github", async (
            HttpContext context,
            GitHubWebhookProcessor processor,
            IOptions<GitHubSubmissionOptions> options,
            CancellationToken cancellationToken) =>
        {
            var maximum = options.Value.MaxWebhookBytes;
            if (context.Request.ContentLength > maximum) return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
            await using var buffer = new MemoryStream();
            await context.Request.Body.CopyToAsync(buffer, cancellationToken);
            if (buffer.Length > maximum) return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
            var payload = buffer.ToArray();
            if (!processor.HasValidSignature(payload, context.Request.Headers["X-Hub-Signature-256"]))
            {
                return Results.Unauthorized();
            }
            var delivery = context.Request.Headers["X-GitHub-Delivery"].ToString();
            var eventName = context.Request.Headers["X-GitHub-Event"].ToString();
            if (string.IsNullOrWhiteSpace(delivery) || string.IsNullOrWhiteSpace(eventName))
            {
                return Results.BadRequest(new ApiFailure(
                    "validation", "Required GitHub delivery headers are missing.", null, context.TraceIdentifier));
            }
            var outcome = await processor.ProcessAsync(delivery, eventName, payload, cancellationToken);
            return Results.Accepted(value: new { outcome = outcome.ToString() });
        });

        app.MapGet("/api/contributions/{contributionId:guid}", async (
            Guid contributionId,
            bool? refresh,
            HttpContext context,
            GitHubContributorAuthentication auth,
            ContributionStatusService status,
            CancellationToken cancellationToken) =>
        {
            var session = await auth.GetSessionAsync(
                context.Request.Cookies[GitHubContributorAuthentication.CookieName], cancellationToken);
            if (session is null) return Results.Unauthorized();
            try
            {
                var result = await status.GetAsync(
                    contributionId,
                    session,
                    auth.UnprotectToken(session),
                    refresh == true,
                    cancellationToken);
                return result is null ? Results.NotFound() : Results.Ok(result);
            }
            catch (GitHubRateLimitException exception)
            {
                context.Response.Headers.RetryAfter = ((int)(exception.RetryAfter ?? TimeSpan.FromMinutes(1)).TotalSeconds).ToString();
                return Results.Problem(title: "GitHub rate limit", statusCode: 429);
            }
            catch (HttpRequestException)
            {
                return Results.Problem(
                    title: "GitHub status is temporarily unavailable",
                    detail: "The last known state remains available. Try refreshing later.",
                    statusCode: 503);
            }
        });

        return app;
    }
}
