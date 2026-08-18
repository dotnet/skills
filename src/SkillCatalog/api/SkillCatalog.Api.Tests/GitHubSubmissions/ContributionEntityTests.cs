using SkillCatalog.Api.Persistence;

namespace SkillCatalog.Api.Tests.GitHubSubmissions;

public sealed class ContributionEntityTests
{
    [Fact]
    public void Contribution_only_allows_documented_transitions()
    {
        var contribution = Contribution.Create(Guid.NewGuid(), 42, "user", "skills", "skill/test");
        contribution.AdvanceTo(ContributionState.ForkReady, "fork verified");
        contribution.AdvanceTo(ContributionState.BranchReady, "branch created");
        Assert.Equal(ContributionState.BranchReady, contribution.State);
        Assert.Throws<InvalidOperationException>(() => contribution.AdvanceTo(ContributionState.Merged, "invalid jump"));
    }

    [Fact]
    public void Recovery_transition_records_actionable_reason()
    {
        var contribution = Contribution.Create(Guid.NewGuid(), 42, "user", "skills", "skill/test");
        contribution.AdvanceTo(ContributionState.RecoveryRequired, "Inspect the contributor fork before retrying.");
        Assert.Equal(ContributionState.RecoveryRequired, contribution.State);
        Assert.Equal("Inspect the contributor fork before retrying.", contribution.RecoveryMessage);
        Assert.Throws<InvalidOperationException>(() => contribution.AdvanceTo(ContributionState.ForkReady, "retry"));
    }

    [Fact]
    public void Intent_is_bound_to_one_package_and_destination()
    {
        var intent = SubmissionIntent.Create(Guid.NewGuid(), "abc", "NewSkill", "plugins/dotnet/skills/test", "base", "key");
        Assert.Equal("abc", intent.PackageSha256);
        Assert.Equal("plugins/dotnet/skills/test", intent.DestinationPath);
        Assert.Throws<InvalidOperationException>(() => intent.Confirm("different"));
        Assert.Null(intent.ConfirmedAt);
        intent.Confirm("abc");
        Assert.NotNull(intent.ConfirmedAt);
    }

    [Fact]
    public void Session_contains_only_protected_server_side_credentials()
    {
        var session = new ContributorSession
        {
            GitHubUserId = 42,
            GitHubLogin = "octocat",
            ProtectedAccessToken = "CfDJ8-protected-ciphertext",
            AccessExpiresAt = DateTimeOffset.UtcNow.AddHours(1)
        };
        Assert.StartsWith("CfDJ8", session.ProtectedAccessToken);
        Assert.Null(session.RevokedAt);
    }

    [Fact]
    public void Lease_delivery_and_audit_have_deduplication_and_evidence_identity()
    {
        var contributionId = Guid.NewGuid();
        var lease = new IdempotencyLease
        {
            ContributorGitHubUserId = 42,
            IdempotencyKey = "request-1",
            LeaseOwner = "worker-1",
            LeaseExpiresAt = DateTimeOffset.UtcNow.AddMinutes(2)
        };
        var delivery = new WebhookDelivery
        {
            DeliveryId = "delivery-1",
            EventName = "pull_request",
            Action = "opened",
            PayloadDigest = "sha256"
        };
        var audit = new AuditTransition
        {
            ContributionId = contributionId,
            ActorType = "webhook",
            FromState = ContributionState.PullRequestOpen,
            ToState = ContributionState.ChecksPending,
            ReasonCode = "checks-requested"
        };

        Assert.Equal((42, "request-1"), (lease.ContributorGitHubUserId, lease.IdempotencyKey));
        Assert.Equal("delivery-1", delivery.DeliveryId);
        Assert.Equal(contributionId, audit.ContributionId);
    }
}
