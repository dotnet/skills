using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Antiforgery;
using SkillCatalog.Api.Auth;
using SkillCatalog.Api.Models;

namespace SkillCatalog.Api.Endpoints;

public static class GitHubAuthenticationEndpoints
{
    public static IEndpointRouteBuilder MapGitHubAuthenticationEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/auth/csrf", (HttpContext context, IAntiforgery antiforgery) =>
        {
            var tokens = antiforgery.GetAndStoreTokens(context);
            return Results.Ok(new { token = tokens.RequestToken });
        });

        app.MapPost("/api/auth/github/start", async (
            HttpContext context,
            GitHubContributorAuthentication auth,
            CancellationToken cancellationToken) =>
        {
            var origin = context.Request.Headers.Origin.ToString();
            if (!auth.IsAllowedOrigin(origin))
            {
                return Results.BadRequest(new ApiFailure(
                    "authentication",
                    "Untrusted origin.",
                    "Open the catalog from an allowed address.",
                    context.TraceIdentifier));
            }

            var baseUrl = $"{context.Request.Scheme}://{context.Request.Host}";
            return Results.Ok(await auth.StartAsync(origin, baseUrl, cancellationToken));
        });

        app.MapGet("/api/auth/github/callback", async (
            string? code,
            string? state,
            HttpContext context,
            GitHubContributorAuthentication auth,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(state))
            {
                return Results.BadRequest("Missing authorization response.");
            }

            try
            {
                var result = await auth.CompleteAsync(
                    code,
                    state,
                    $"{context.Request.Scheme}://{context.Request.Host}",
                    cancellationToken);
                context.Response.Cookies.Append(
                    GitHubContributorAuthentication.CookieName,
                    result.SessionId.ToString(),
                    GitHubContributorAuthentication.SessionCookieOptions(result.ExpiresAt));
                var origin = JavaScriptEncoder.Default.Encode(result.OpenerOrigin);
                return Results.Content(
                    $"<!doctype html><meta charset=utf-8><script>if(window.opener){{window.opener.postMessage({{type:'skillcatalog:github-auth-complete'}},'{origin}');}}window.close();</script>",
                    "text/html");
            }
            catch (InvalidOperationException)
            {
                return Results.BadRequest("Authorization could not be completed.");
            }
        });

        app.MapGet("/api/auth/session", async (
            HttpContext context,
            GitHubContributorAuthentication auth,
            CancellationToken cancellationToken) =>
        {
            var session = await auth.GetSessionAsync(
                context.Request.Cookies[GitHubContributorAuthentication.CookieName],
                cancellationToken);
            return Results.Ok(session is null
                ? new GitHubSessionView(false, null, null, null)
                : new GitHubSessionView(true, session.GitHubUserId, session.GitHubLogin, session.AccessExpiresAt));
        });

        app.MapDelete("/api/auth/session", async (
            HttpContext context,
            GitHubContributorAuthentication auth,
            IAntiforgery antiforgery,
            CancellationToken cancellationToken) =>
        {
            try
            {
                await antiforgery.ValidateRequestAsync(context);
            }
            catch (AntiforgeryValidationException)
            {
                return Results.BadRequest(new ApiFailure(
                    "authentication",
                    "The security token is missing or expired.",
                    "Refresh the page and try again.",
                    context.TraceIdentifier));
            }

            if (Guid.TryParse(context.Request.Cookies[GitHubContributorAuthentication.CookieName], out var id))
            {
                await auth.RevokeAsync(id, cancellationToken);
            }

            context.Response.Cookies.Delete(
                GitHubContributorAuthentication.CookieName,
                GitHubContributorAuthentication.SessionCookieOptions(null));
            return Results.NoContent();
        });

        return app;
    }
}
