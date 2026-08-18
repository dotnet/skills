using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SkillCatalog.Api.Persistence;

namespace SkillCatalog.Api.ContractTests;

public sealed class GitHubWebhookTests : IClassFixture<CatalogApiFactory>
{
    private const string Secret = "contract-test-webhook-secret";
    private readonly HttpClient _client;
    private readonly CatalogApiFactory _factory;

    public GitHubWebhookTests(CatalogApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Invalid_signature_is_rejected_before_persistence()
    {
        using var request = Request("delivery-invalid", "{}"u8.ToArray(), "sha256=00");
        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Oversized_payload_is_rejected()
    {
        var payload = new byte[4097];
        using var request = Request("delivery-large", payload, Signature(payload));
        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    [Fact]
    public async Task Signed_delivery_is_accepted_and_replay_is_deduplicated()
    {
        var payload = "{\"action\":\"opened\",\"number\":999}"u8.ToArray();
        using var first = Request("delivery-once", payload, Signature(payload));
        Assert.Equal(HttpStatusCode.Accepted, (await _client.SendAsync(first)).StatusCode);

        using var replay = Request("delivery-once", payload, Signature(payload));
        var replayResponse = await _client.SendAsync(replay);
        Assert.Equal(HttpStatusCode.Accepted, replayResponse.StatusCode);
        Assert.Contains("Duplicate", await replayResponse.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Pull_request_merge_maps_to_terminal_state_and_audit_evidence()
    {
        var contribution = Contribution.Create(Guid.NewGuid(), 42, "octocat", "skills", "branch");
        contribution.AdvanceTo(ContributionState.ForkReady, "fork");
        contribution.AdvanceTo(ContributionState.BranchReady, "branch");
        contribution.AdvanceTo(ContributionState.CommitReady, "commit");
        contribution.AdvanceTo(ContributionState.PullRequestOpen, "pull request");
        contribution.PullRequestNumber = 42;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<GitHubSubmissionDbContext>();
            db.Add(contribution);
            await db.SaveChangesAsync();
        }

        var payload = "{\"action\":\"closed\",\"number\":42,\"pull_request\":{\"number\":42,\"merged\":true}}"u8.ToArray();
        using var request = Request("delivery-merged", payload, Signature(payload));
        Assert.Equal(HttpStatusCode.Accepted, (await _client.SendAsync(request)).StatusCode);

        await using var verificationScope = _factory.Services.CreateAsyncScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<GitHubSubmissionDbContext>();
        Assert.Equal(ContributionState.Merged, (await verificationDb.Contributions.SingleAsync(x => x.Id == contribution.Id)).State);
        Assert.Contains(await verificationDb.AuditTransitions.ToListAsync(), audit => audit.ContributionId == contribution.Id && audit.ToState == ContributionState.Merged);
    }

    private static HttpRequestMessage Request(string delivery, byte[] payload, string signature)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/github")
        {
            Content = new ByteArrayContent(payload)
        };
        request.Headers.Add("X-Hub-Signature-256", signature);
        request.Headers.Add("X-GitHub-Delivery", delivery);
        request.Headers.Add("X-GitHub-Event", "pull_request");
        return request;
    }

    private static string Signature(byte[] payload) =>
        $"sha256={Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(Secret), payload)).ToLowerInvariant()}";
}

