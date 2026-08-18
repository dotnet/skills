using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SkillCatalog.Api.Options;
using SkillCatalog.Api.Persistence;

namespace SkillCatalog.Api.GitHub;

public enum WebhookProcessingOutcome { Accepted, Duplicate, Ignored }

public sealed class GitHubWebhookProcessor(
    GitHubSubmissionDbContext db,
    IOptions<GitHubSubmissionOptions> options,
    TimeProvider time)
{
    private readonly GitHubSubmissionOptions _options = options.Value;

    public bool HasValidSignature(ReadOnlySpan<byte> payload, string? signature)
    {
        if (string.IsNullOrWhiteSpace(_options.WebhookSecret) ||
            signature is null || !signature.StartsWith("sha256=", StringComparison.Ordinal))
        {
            return false;
        }
        byte[] supplied;
        try
        {
            supplied = Convert.FromHexString(signature[7..]);
        }
        catch (FormatException)
        {
            return false;
        }
        if (supplied.Length != 32) return false;
        var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(_options.WebhookSecret), payload);
        return CryptographicOperations.FixedTimeEquals(expected, supplied);
    }

    public async Task<WebhookProcessingOutcome> ProcessAsync(
        string deliveryId,
        string eventName,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken)
    {
        if (await db.WebhookDeliveries.AnyAsync(x => x.DeliveryId == deliveryId, cancellationToken))
        {
            return WebhookProcessingOutcome.Duplicate;
        }

        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;
        var action = root.TryGetProperty("action", out var actionElement) ? actionElement.GetString() ?? "" : "";
        var pullRequestNumber = FindPullRequestNumber(root);
        var delivery = new WebhookDelivery
        {
            DeliveryId = deliveryId,
            EventName = eventName,
            Action = action,
            PayloadDigest = Convert.ToHexString(SHA256.HashData(payload.Span)).ToLowerInvariant(),
            ReceivedAt = time.GetUtcNow()
        };
        db.Add(delivery);

        var contribution = pullRequestNumber is null
            ? null
            : await db.Contributions.SingleOrDefaultAsync(
                x => x.PullRequestNumber == pullRequestNumber, cancellationToken);
        if (contribution is null)
        {
            delivery.Outcome = "ignored";
            delivery.ProcessedAt = time.GetUtcNow();
            await db.SaveChangesAsync(cancellationToken);
            return WebhookProcessingOutcome.Ignored;
        }

        delivery.ContributionId = contribution.Id;
        var target = MapState(eventName, action, root);
        if (target is { } next && next != contribution.State)
        {
            var previous = contribution.State;
            try
            {
                contribution.AdvanceTo(next, $"github-webhook:{eventName}:{action}");
                db.Add(new AuditTransition
                {
                    ContributionId = contribution.Id,
                    ActorType = "github-webhook",
                    ActorId = deliveryId,
                    FromState = previous,
                    ToState = next,
                    ReasonCode = $"{eventName}:{action}",
                    GitHubResourceId = pullRequestNumber.ToString(),
                    OccurredAt = time.GetUtcNow()
                });
            }
            catch (InvalidOperationException)
            {
                delivery.Outcome = "stale-transition-ignored";
            }
        }
        delivery.Outcome ??= "processed";
        delivery.ProcessedAt = time.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        return WebhookProcessingOutcome.Accepted;
    }

    private static int? FindPullRequestNumber(JsonElement root)
    {
        if (root.TryGetProperty("pull_request", out var pullRequest) &&
            pullRequest.TryGetProperty("number", out var nestedNumber))
        {
            return nestedNumber.GetInt32();
        }
        if (root.TryGetProperty("number", out var number)) return number.GetInt32();
        if (root.TryGetProperty("check_run", out var checkRun) &&
            checkRun.TryGetProperty("pull_requests", out var pullRequests) &&
            pullRequests.GetArrayLength() > 0 &&
            pullRequests[0].TryGetProperty("number", out var checkNumber))
        {
            return checkNumber.GetInt32();
        }
        return null;
    }

    private static ContributionState? MapState(string eventName, string action, JsonElement root) => eventName switch
    {
        "pull_request" when action == "closed" &&
            root.GetProperty("pull_request").TryGetProperty("merged", out var merged) && merged.GetBoolean()
            => ContributionState.Merged,
        "pull_request" when action == "closed" => ContributionState.Closed,
        "pull_request" when action is "opened" or "reopened" => ContributionState.PullRequestOpen,
        "pull_request" when action == "synchronize" => ContributionState.ChecksPending,
        "check_run" when action is "created" or "rerequested" => ContributionState.ChecksPending,
        "check_run" when action == "completed" => ContributionState.AwaitingReview,
        "pull_request_review" when action == "submitted" => ContributionState.AwaitingReview,
        _ => null
    };
}

