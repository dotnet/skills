using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SkillCatalog.Api.Auth;
using SkillCatalog.Api.GitHub;
using SkillCatalog.Api.Persistence;

namespace SkillCatalog.Api.ContractTests;

public sealed class GitHubSubmissionEndpointsTests : IClassFixture<CatalogApiFactory>
{
    private readonly CatalogApiFactory _factory;
    private readonly HttpClient _client;

    public GitHubSubmissionEndpointsTests(CatalogApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
    }

    [Fact]
    public async Task Intent_requires_authentication()
    {
        using var form = Package("sample");
        var response = await _client.PostAsync("/api/contributions/intents", form);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Valid_package_creates_intent_and_idempotent_pull_request()
    {
        var auth = await AuthenticateAsync();
        var intent = await CreateIntentAsync(auth, Package("new-skill"));
        Assert.Equal("NewSkill", intent.GetProperty("contributionType").GetString());
        Assert.Equal("plugins/dotnet/skills/new-skill", intent.GetProperty("destinationPath").GetString());

        var first = await SubmitAsync(auth, intent.GetProperty("id").GetGuid(), Package("new-skill"), "same-key");
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Contains("pull/7", await first.Content.ReadAsStringAsync());
        var replay = await SubmitAsync(auth, intent.GetProperty("id").GetGuid(), Package("new-skill"), "same-key");
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
    }

    [Fact]
    public async Task Validation_and_package_mismatch_fail_before_GitHub_writes()
    {
        var auth = await AuthenticateAsync();
        using var invalid = new MultipartFormDataContent();
        invalid.Add(new ByteArrayContent("invalid"u8.ToArray()), "file", "SKILL.md");
        using var invalidRequest = Request(HttpMethod.Post, "/api/contributions/intents", auth, invalid);
        var validation = await _client.SendAsync(invalidRequest);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, validation.StatusCode);

        var intent = await CreateIntentAsync(auth, Package("original"));
        var conflict = await SubmitAsync(auth, intent.GetProperty("id").GetGuid(), Package("changed"), "mismatch");
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
    }

    [Fact]
    public async Task Rate_limit_and_partial_success_have_actionable_categories()
    {
        var auth = await AuthenticateAsync();
        var fake = _factory.Services.GetRequiredService<IGitHubContributionClient>() as ContractGitHubClient
            ?? throw new InvalidOperationException();
        try
        {
            var rateIntent = await CreateIntentAsync(auth, Package("rate-limit"));
            fake.FailWithRateLimit = true;
            var rate = await SubmitAsync(auth, rateIntent.GetProperty("id").GetGuid(), Package("rate-limit"), "rate");
            Assert.Equal(HttpStatusCode.TooManyRequests, rate.StatusCode);
            Assert.Contains("rate", await rate.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
            fake.FailWithRateLimit = false;

            var recoveryIntent = await CreateIntentAsync(auth, Package("recovery"));
            fake.FailWithPartialSuccess = true;
            var recovery = await SubmitAsync(auth, recoveryIntent.GetProperty("id").GetGuid(), Package("recovery"), "recovery");
            Assert.Equal(HttpStatusCode.ServiceUnavailable, recovery.StatusCode);
            Assert.Contains("partial-success", await recovery.Content.ReadAsStringAsync());
        }
        finally
        {
            fake.FailWithRateLimit = false;
            fake.FailWithPartialSuccess = false;
        }
    }

    private async Task<(string Cookies, string Csrf)> AuthenticateAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<GitHubSubmissionDbContext>();
        var protector = scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>().CreateProtector("SkillCatalog.GitHubTokens.v1");
        var session = new ContributorSession
        {
            GitHubUserId = Random.Shared.NextInt64(1000, long.MaxValue),
            GitHubLogin = "octocat",
            ProtectedAccessToken = protector.Protect("test-token"),
            AccessExpiresAt = DateTimeOffset.UtcNow.AddHours(1)
        };
        db.Add(session);
        await db.SaveChangesAsync();
        var csrfResponse = await _client.GetAsync("/api/auth/csrf");
        var csrf = (await csrfResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;
        var csrfCookie = csrfResponse.Headers.GetValues("Set-Cookie").Single().Split(';')[0];
        return ($"{csrfCookie}; {GitHubContributorAuthentication.CookieName}={session.Id}", csrf);
    }

    private async Task<JsonElement> CreateIntentAsync((string Cookies, string Csrf) auth, MultipartFormDataContent package)
    {
        using var request = Request(HttpMethod.Post, "/api/contributions/intents", auth, package);
        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private async Task<HttpResponseMessage> SubmitAsync((string Cookies, string Csrf) auth, Guid intentId, MultipartFormDataContent package, string key)
    {
        var request = Request(HttpMethod.Post, $"/api/contributions/intents/{intentId}/submit", auth, package);
        request.Headers.Add("Idempotency-Key", key);
        request.Headers.Add("X-Confirm-Update", "true");
        return await _client.SendAsync(request);
    }

    private static HttpRequestMessage Request(HttpMethod method, string path, (string Cookies, string Csrf) auth, HttpContent content)
    {
        var request = new HttpRequestMessage(method, path) { Content = content };
        request.Headers.Add("Cookie", auth.Cookies);
        request.Headers.Add("X-CSRF-TOKEN", auth.Csrf);
        return request;
    }

    private static MultipartFormDataContent Package(string name)
    {
        var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
        {
            var entry = zip.CreateEntry($"plugins/dotnet/skills/{name}/SKILL.md");
            using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
            writer.Write($"---\nname: {name}\ndescription: A representative contribution skill.\n---\n# {name}\n## Workflow\n1. Inspect input.\n2. Return result.\n## Validation\nConfirm result.");
        }
        var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(stream.ToArray()), "file", "skill.zip");
        return form;
    }
}


