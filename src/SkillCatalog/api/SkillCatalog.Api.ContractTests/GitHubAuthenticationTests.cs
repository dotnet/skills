using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace SkillCatalog.Api.ContractTests;

public sealed class GitHubAuthenticationTests : IClassFixture<CatalogApiFactory>
{
    private readonly HttpClient _client;

    public GitHubAuthenticationTests(CatalogApiFactory factory) =>
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });

    [Fact]
    public async Task Session_is_anonymous_by_default()
    {
        var response = await _client.GetAsync("/api/auth/session");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(body.GetProperty("authenticated").GetBoolean());
        Assert.False(response.Headers.Contains("Authorization"));
    }

    [Fact]
    public async Task Authorization_start_rejects_untrusted_origin()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/github/start");
        request.Headers.Add("Origin", "https://evil.example");
        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Authorization_start_uses_state_pkce_and_exact_callback()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/github/start");
        request.Headers.Add("Origin", "http://localhost:5173");
        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var authorization = new Uri(body.GetProperty("authorizationUrl").GetString()!);
        Assert.Equal("github.com", authorization.Host);
        var query = Uri.UnescapeDataString(authorization.Query);
        Assert.Contains("state=", query);
        Assert.Contains("code_challenge=", query);
        Assert.Contains("code_challenge_method=S256", query);
        Assert.Contains("/api/auth/github/callback", query);
    }

    [Fact]
    public async Task Callback_rejects_missing_or_replayed_state_without_setting_cookie()
    {
        var missing = await _client.GetAsync("/api/auth/github/callback");
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.False(missing.Headers.Contains("Set-Cookie"));

        var invalid = await _client.GetAsync("/api/auth/github/callback?code=code&state=unknown");
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.False(invalid.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task Antiforgery_token_is_secure_and_logout_requires_it()
    {
        var tokenResponse = await _client.GetAsync("/api/auth/csrf");
        Assert.Equal(HttpStatusCode.OK, tokenResponse.StatusCode);
        var token = await tokenResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(string.IsNullOrWhiteSpace(token.GetProperty("token").GetString()));
        var cookie = Assert.Single(tokenResponse.Headers.GetValues("Set-Cookie"));
        Assert.Contains("__Host-skillcatalog-csrf", cookie);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);

        var logout = await _client.DeleteAsync("/api/auth/session");
        Assert.Equal(HttpStatusCode.BadRequest, logout.StatusCode);
    }

    [Fact]
    public async Task Shared_key_ring_allows_antiforgery_recovery_across_instances()
    {
        await using var secondFactory = new CatalogApiFactory();
        using var secondClient = secondFactory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });
        var tokenResponse = await _client.GetAsync("/api/auth/csrf");
        var tokenBody = await tokenResponse.Content.ReadFromJsonAsync<JsonElement>();
        var cookie = tokenResponse.Headers.GetValues("Set-Cookie").Single().Split(';')[0];
        using var logout = new HttpRequestMessage(HttpMethod.Delete, "/api/auth/session");
        logout.Headers.Add("Cookie", cookie);
        logout.Headers.Add("X-CSRF-TOKEN", tokenBody.GetProperty("token").GetString());
        var response = await secondClient.SendAsync(logout);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }}


