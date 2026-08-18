using System.Net;
using System.Text;
using Microsoft.Extensions.Options;
using SkillCatalog.Api.GitHub;
using SkillCatalog.Api.Options;

namespace SkillCatalog.Api.Tests.GitHubSubmissions;

public sealed class GitHubContributionClientTests
{
    [Fact]
    public void Permission_contract_excludes_administration_and_workflows()
    {
        Assert.DoesNotContain("Administration", GitHubPermissionContract.Required);
        Assert.DoesNotContain("Workflows", GitHubPermissionContract.Required);
        Assert.Contains("Contents:write", GitHubPermissionContract.Required);
        Assert.Contains("PullRequests:write", GitHubPermissionContract.Required);
        Assert.Contains("Checks:read", GitHubPermissionContract.Required);
    }

    [Fact]
    public void Client_rejects_non_github_host()
    {
        var options = Microsoft.Extensions.Options.Options.Create(new GitHubSubmissionOptions { ApiBaseUrl = "https://example.com" });
        var exception = Record.Exception(
            () => new GitHubContributionClient(new HttpClient(), options, TimeProvider.System));
        Assert.IsType<InvalidOperationException>(exception);
    }

    [Fact]
    public async Task Rate_limit_is_classified_without_leaking_token()
    {
        const string secret = "github-secret-token";
        var handler = new StubHandler(request =>
        {
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.Equal(secret, request.Headers.Authorization?.Parameter);
            var response = new HttpResponseMessage(HttpStatusCode.Forbidden)
            {
                Content = new StringContent("sensitive upstream body")
            };
            response.Headers.Add("X-RateLimit-Remaining", "0");
            return response;
        });
        var client = CreateClient(handler);

        var exception = await Assert.ThrowsAsync<GitHubRateLimitException>(
            () => client.GetIdentityAsync(secret, CancellationToken.None));
        Assert.DoesNotContain(secret, exception.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("sensitive upstream body", exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Transient_errors_are_retried_with_the_same_allowlisted_request()
    {
        var attempts = 0;
        var handler = new StubHandler(_ =>
        {
            attempts++;
            return attempts < 3
                ? Json(HttpStatusCode.ServiceUnavailable, "{}")
                : Json(HttpStatusCode.OK, "{\"id\":42,\"login\":\"octocat\"}");
        });
        var identity = await CreateClient(handler).GetIdentityAsync("token", CancellationToken.None);
        Assert.Equal("octocat", identity.Login);
        Assert.Equal(3, attempts);
    }

    [Fact]
    public async Task Eligible_fork_must_point_to_the_configured_upstream()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, """
            {"fork":true,"default_branch":"main","parent":{"full_name":"attacker/skills"}}
            """));
        var client = CreateClient(handler);

        var result = await client.GetEligibleForkAsync("token", "octocat", CancellationToken.None);
        Assert.Null(result);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task Checks_request_is_bounded_to_one_hundred_results()
    {
        var handler = new StubHandler(request =>
        {
            Assert.Contains("per_page=100", request.RequestUri!.Query);
            return Json(HttpStatusCode.OK, "{\"check_runs\":[]}");
        });
        var client = CreateClient(handler);

        Assert.Empty(await client.GetChecksAsync("token", "sha", CancellationToken.None));
    }

    private static GitHubContributionClient CreateClient(HttpMessageHandler handler) => new(
        new HttpClient(handler),
        Microsoft.Extensions.Options.Options.Create(new GitHubSubmissionOptions()),
        TimeProvider.System);

    private static HttpResponseMessage Json(HttpStatusCode status, string json) => new(status)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(respond(request));
        }
    }
}


