using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SkillCatalog.Api.GitHub;
using SkillCatalog.Api.Models;
using SkillCatalog.Api.Options;
using SkillCatalog.Api.Persistence;

namespace SkillCatalog.Api.Services;

public sealed class ContributionStatusService(
    GitHubSubmissionDbContext db,
    IGitHubContributionClient github,
    IOptions<GitHubSubmissionOptions> options,
    TimeProvider time)
{
    private readonly GitHubSubmissionOptions _options = options.Value;

    public async Task<ContributionView?> GetAsync(
        Guid contributionId,
        ContributorSession session,
        string token,
        bool forceRefresh,
        CancellationToken cancellationToken)
    {
        var contribution = await db.Contributions.SingleOrDefaultAsync(
            x => x.Id == contributionId && x.ContributorGitHubUserId == session.GitHubUserId,
            cancellationToken);
        if (contribution is null) return null;

        IReadOnlyList<ContributionEvidenceView>? evidence = null;
        var refreshDue = contribution.LastReconciledAt is null ||
            contribution.LastReconciledAt <= time.GetUtcNow().AddSeconds(-_options.StatusRefreshSeconds);
        if ((forceRefresh || refreshDue) && contribution.PullRequestNumber is { } number)
        {
            evidence = await ReconcileAsync(contribution, token, number, cancellationToken);
        }
        return NewSkillContributionService.View(contribution) with { Evidence = evidence };
    }

    private async Task<IReadOnlyList<ContributionEvidenceView>> ReconcileAsync(
        Contribution contribution,
        string token,
        int pullRequestNumber,
        CancellationToken cancellationToken)
    {
        var pullRequest = await github.GetPullRequestAsync(token, pullRequestNumber, cancellationToken);
        var checks = await github.GetChecksAsync(token, pullRequest.HeadSha, cancellationToken);
        var reviews = await github.GetReviewsAsync(token, pullRequestNumber, cancellationToken);
        var target = pullRequest.Merged
            ? ContributionState.Merged
            : string.Equals(pullRequest.State, "closed", StringComparison.OrdinalIgnoreCase)
                ? ContributionState.Closed
                : checks.Any(check => !string.Equals(check.Status, "completed", StringComparison.OrdinalIgnoreCase))
                    ? ContributionState.ChecksPending
                    : ContributionState.AwaitingReview;

        if (target != contribution.State && contribution.State is not (ContributionState.Merged or ContributionState.Closed or ContributionState.RecoveryRequired))
        {
            var previous = contribution.State;
            contribution.AdvanceTo(target, "github-reconciliation");
            db.Add(new AuditTransition
            {
                ContributionId = contribution.Id,
                ActorType = "reconciliation",
                FromState = previous,
                ToState = target,
                ReasonCode = "github-authoritative-state",
                GitHubResourceId = pullRequestNumber.ToString(),
                OccurredAt = time.GetUtcNow()
            });
        }
        contribution.PullRequestUrl = pullRequest.Url;
        contribution.CommitSha = pullRequest.HeadSha;
        contribution.LastReconciledAt = time.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);

        return new[]
        {
            new ContributionEvidenceView("pull-request", $"Pull request #{pullRequest.Number}", pullRequest.Merged ? "merged" : pullRequest.State, pullRequest.Url)
        }.Concat(checks.Select(check => new ContributionEvidenceView(
            "check", check.Name, check.Conclusion ?? check.Status, check.Url)))
          .Concat(reviews.Select(review => new ContributionEvidenceView(
            "review", $"Review {review.Id}", review.State, review.Url)))
          .ToArray();
    }
}
