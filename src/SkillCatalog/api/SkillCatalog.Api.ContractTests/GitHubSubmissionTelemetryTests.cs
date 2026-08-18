using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace SkillCatalog.Api.ContractTests;

public sealed class GitHubSubmissionTelemetryTests : IClassFixture<CatalogApiFactory>
{
    private readonly HttpClient _client;
    public GitHubSubmissionTelemetryTests(CatalogApiFactory factory) => _client = factory.CreateClient();

    [Fact]
    public async Task Tokens_package_content_and_webhook_payload_are_not_logged_or_returned()
    {
        while (CapturingLoggerProvider.Messages.TryDequeue(out _)) { }
        const string token = "ghu_super_secret_access_token";
        const string packageContent = "PRIVATE_SKILL_FILE_CONTENT";
        const string webhookPayload = "WEBHOOK_PRIVATE_PAYLOAD";

        using var unauthorized = new HttpRequestMessage(HttpMethod.Post, "/api/contributions/intents");
        unauthorized.Headers.Authorization = new("Bearer", token);
        var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(Encoding.UTF8.GetBytes(packageContent)), "file", "skill.zip");
        unauthorized.Content = form;
        var unauthorizedResponse = await _client.SendAsync(unauthorized);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorizedResponse.StatusCode);
        Assert.DoesNotContain(token, await unauthorizedResponse.Content.ReadAsStringAsync());
        Assert.DoesNotContain(packageContent, await unauthorizedResponse.Content.ReadAsStringAsync());

        var malformed = Encoding.UTF8.GetBytes($"{{not-json:{webhookPayload}}}");
        using var webhook = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/github") { Content = new ByteArrayContent(malformed) };
        webhook.Headers.Add("X-GitHub-Delivery", "redaction-delivery");
        webhook.Headers.Add("X-GitHub-Event", "pull_request");
        webhook.Headers.Add("X-Hub-Signature-256", $"sha256={Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes("contract-test-webhook-secret"), malformed)).ToLowerInvariant()}");
        _ = await _client.SendAsync(webhook);

        var logs = string.Join("\n", CapturingLoggerProvider.Messages);
        Assert.DoesNotContain(token, logs, StringComparison.Ordinal);
        Assert.DoesNotContain(packageContent, logs, StringComparison.Ordinal);
        Assert.DoesNotContain(webhookPayload, logs, StringComparison.Ordinal);
    }
}

