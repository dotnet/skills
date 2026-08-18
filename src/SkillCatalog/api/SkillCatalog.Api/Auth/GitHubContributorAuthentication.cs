using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SkillCatalog.Api.Models;
using SkillCatalog.Api.Options;
using SkillCatalog.Api.Persistence;

namespace SkillCatalog.Api.Auth;

public sealed record GitHubAuthorizationResult(Guid SessionId, string OpenerOrigin, DateTimeOffset ExpiresAt);

public sealed class GitHubContributorAuthentication(
    GitHubSubmissionDbContext db,
    IDataProtectionProvider protection,
    IHttpClientFactory clients,
    IOptions<GitHubSubmissionOptions> options,
    TimeProvider time)
{
    public const string CookieName = "__Host-skillcatalog-session";
    private readonly IDataProtector _tokens = protection.CreateProtector("SkillCatalog.GitHubTokens.v1");
    private readonly GitHubSubmissionOptions _options = options.Value;

    public static CookieOptions SessionCookieOptions(DateTimeOffset? expiresAt) => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Strict,
        Path = "/",
        Expires = expiresAt
    };

    public bool IsAllowedOrigin(string? origin) =>
        origin is not null && _options.AllowedOrigins.Contains(origin, StringComparer.OrdinalIgnoreCase);

    public async Task<AuthorizationStartResponse> StartAsync(
        string origin,
        string callbackBase,
        CancellationToken cancellationToken)
    {
        if (!IsAllowedOrigin(origin))
        {
            throw new InvalidOperationException("Untrusted origin.");
        }

        var state = Base64Url(RandomNumberGenerator.GetBytes(32));
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(48));
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var transaction = new AuthorizationTransaction
        {
            StateDigest = Hash(state),
            PkceVerifier = verifier,
            OpenerOrigin = origin,
            ExpiresAt = time.GetUtcNow().AddSeconds(_options.AuthorizationLifetimeSeconds)
        };
        db.Add(transaction);
        await db.SaveChangesAsync(cancellationToken);

        var callback = $"{callbackBase.TrimEnd('/')}{_options.CallbackPath}";
        var url = $"https://github.com/login/oauth/authorize?client_id={Uri.EscapeDataString(_options.ClientId)}&redirect_uri={Uri.EscapeDataString(callback)}&state={Uri.EscapeDataString(state)}&code_challenge={Uri.EscapeDataString(challenge)}&code_challenge_method=S256";
        return new(new Uri(url), transaction.Id, transaction.ExpiresAt);
    }

    public async Task<GitHubAuthorizationResult> CompleteAsync(
        string code,
        string state,
        string callbackBase,
        CancellationToken cancellationToken)
    {
        var transaction = await db.AuthorizationTransactions.SingleOrDefaultAsync(
            x => x.StateDigest == Hash(state), cancellationToken)
            ?? throw new InvalidOperationException("Invalid authorization state.");
        if (transaction.CompletedAt is not null || transaction.ExpiresAt <= time.GetUtcNow())
        {
            throw new InvalidOperationException("Expired authorization state.");
        }

        var token = await ExchangeTokenAsync(new
        {
            client_id = _options.ClientId,
            client_secret = _options.ClientSecret,
            code,
            redirect_uri = $"{callbackBase.TrimEnd('/')}{_options.CallbackPath}",
            code_verifier = transaction.PkceVerifier
        }, cancellationToken);
        var accessToken = RequiredString(token, "access_token");

        var http = clients.CreateClient("GitHubOAuth");
        using var userRequest = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/user");
        userRequest.Headers.Authorization = new("Bearer", accessToken);
        userRequest.Headers.UserAgent.ParseAdd("SkillCatalog/1.0");
        using var userResponse = await http.SendAsync(userRequest, cancellationToken);
        userResponse.EnsureSuccessStatusCode();
        var user = await userResponse.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
        var now = time.GetUtcNow();
        var session = new ContributorSession
        {
            GitHubUserId = user.GetProperty("id").GetInt64(),
            GitHubLogin = RequiredString(user, "login"),
            ProtectedAccessToken = _tokens.Protect(accessToken),
            ProtectedRefreshToken = OptionalString(token, "refresh_token") is { } refresh
                ? _tokens.Protect(refresh)
                : null,
            AccessExpiresAt = now.AddSeconds(OptionalInt(token, "expires_in") ?? 28_800),
            RefreshExpiresAt = OptionalInt(token, "refresh_token_expires_in") is { } refreshSeconds
                ? now.AddSeconds(refreshSeconds)
                : null
        };
        db.Add(session);
        transaction.CompletedAt = now;
        await db.SaveChangesAsync(cancellationToken);
        return new(session.Id, transaction.OpenerOrigin, session.AccessExpiresAt);
    }

    public async Task<ContributorSession?> GetSessionAsync(string? cookie, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(cookie, out var id))
        {
            return null;
        }

        var session = await db.ContributorSessions.SingleOrDefaultAsync(
            x => x.Id == id && x.RevokedAt == null,
            cancellationToken);
        if (session is null)
        {
            return null;
        }

        if (session.AccessExpiresAt > time.GetUtcNow())
        {
            session.LastUsedAt = time.GetUtcNow();
            await db.SaveChangesAsync(cancellationToken);
            return session;
        }

        if (session.ProtectedRefreshToken is null || session.RefreshExpiresAt <= time.GetUtcNow())
        {
            session.RevokedAt = time.GetUtcNow();
            await db.SaveChangesAsync(cancellationToken);
            return null;
        }

        await RefreshAsync(session, cancellationToken);
        return session;
    }

    public string UnprotectToken(ContributorSession session) => _tokens.Unprotect(session.ProtectedAccessToken);

    public async Task RevokeAsync(Guid id, CancellationToken cancellationToken)
    {
        var session = await db.ContributorSessions.FindAsync([id], cancellationToken);
        if (session is null)
        {
            return;
        }

        session.RevokedAt = time.GetUtcNow();
        session.ProtectedAccessToken = string.Empty;
        session.ProtectedRefreshToken = null;
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task RefreshAsync(ContributorSession session, CancellationToken cancellationToken)
    {
        var token = await ExchangeTokenAsync(new
        {
            client_id = _options.ClientId,
            client_secret = _options.ClientSecret,
            grant_type = "refresh_token",
            refresh_token = _tokens.Unprotect(session.ProtectedRefreshToken!)
        }, cancellationToken);
        var now = time.GetUtcNow();
        session.ProtectedAccessToken = _tokens.Protect(RequiredString(token, "access_token"));
        if (OptionalString(token, "refresh_token") is { } rotatedRefresh)
        {
            session.ProtectedRefreshToken = _tokens.Protect(rotatedRefresh);
        }
        session.AccessExpiresAt = now.AddSeconds(OptionalInt(token, "expires_in") ?? 28_800);
        if (OptionalInt(token, "refresh_token_expires_in") is { } refreshSeconds)
        {
            session.RefreshExpiresAt = now.AddSeconds(refreshSeconds);
        }
        session.LastUsedAt = now;
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<JsonElement> ExchangeTokenAsync(object body, CancellationToken cancellationToken)
    {
        var http = clients.CreateClient("GitHubOAuth");
        using var response = await http.PostAsync(
            "https://github.com/login/oauth/access_token",
            JsonContent.Create(body),
            cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
    }

    private static string RequiredString(JsonElement element, string name) =>
        OptionalString(element, name) ?? throw new InvalidOperationException($"GitHub did not return {name}.");

    private static string? OptionalString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int? OptionalInt(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.TryGetInt32(out var result) ? result : null;

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static string Base64Url(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
