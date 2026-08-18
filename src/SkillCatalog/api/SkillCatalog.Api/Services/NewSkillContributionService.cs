using Microsoft.Extensions.Options;
using SkillCatalog.Api.GitHub;
using SkillCatalog.Api.Models;
using SkillCatalog.Api.Options;
using SkillCatalog.Api.Persistence;

namespace SkillCatalog.Api.Services;

public sealed class NewSkillContributionService(
    GitHubSubmissionDbContext db,
    IGitHubContributionClient github,
    IOptions<GitHubSubmissionOptions> options)
{
    private readonly GitHubSubmissionOptions _options = options.Value;

    public Task<Contribution> ExecuteAsync(
        SubmissionIntent intent,
        ContributorSession session,
        string token,
        IReadOnlyDictionary<string, byte[]> files,
        CancellationToken cancellationToken) =>
        ExecuteAsync(intent, session, token, files.Select(file => new GitHubFileChange(file.Key, file.Value)).ToArray(), cancellationToken);

    public async Task<Contribution> ExecuteAsync(
        SubmissionIntent intent,
        ContributorSession session,
        string token,
        IReadOnlyList<GitHubFileChange> changes,
        CancellationToken cancellationToken)
    {
        var fork = await github.GetEligibleForkAsync(token, session.GitHubLogin, cancellationToken)
            ?? throw new GitHubForkRequiredException(
                $"https://github.com/{_options.TargetOwner}/{_options.TargetRepository}/fork");
        if (string.Equals(intent.ContributionType, "Update", StringComparison.Ordinal) &&
            !string.Equals(fork.HeadSha, intent.BaseCommitSha, StringComparison.Ordinal))
        {
            throw new RepositoryRevisionConflictException(intent.BaseCommitSha, fork.HeadSha);
        }
        var branch = $"skillcatalog/{intent.Id:N}";
        var contribution = Contribution.Create(
            intent.Id, session.GitHubUserId, fork.Owner, fork.Name, branch);
        db.Add(contribution);
        await db.SaveChangesAsync(cancellationToken);

        try
        {
            contribution.AdvanceTo(ContributionState.ForkReady, "fork verified");
            await github.CreateBranchAsync(
                token, fork.Owner, fork.Name, branch, fork.HeadSha, cancellationToken);
            contribution.AdvanceTo(ContributionState.BranchReady, "branch created");
            var commit = await github.CreateCommitAsync(
                token,
                fork.Owner,
                fork.Name,
                branch,
                fork.HeadSha,
                changes,
                $"Contribute {intent.DestinationPath}",
                cancellationToken);
            await github.UpdateBranchAsync(
                token, fork.Owner, fork.Name, branch, commit, cancellationToken);
            contribution.CommitSha = commit;
            contribution.AdvanceTo(ContributionState.CommitReady, "commit created");
            var pullRequest = await github.CreatePullRequestAsync(
                token,
                fork.Owner,
                branch,
                $"Contribute {intent.DestinationPath}",
                "Created through the Skill Catalog contributor workspace.",
                cancellationToken);
            contribution.PullRequestNumber = pullRequest.Number;
            contribution.PullRequestUrl = pullRequest.Url;
            contribution.AdvanceTo(ContributionState.PullRequestOpen, "pull request created");
            await db.SaveChangesAsync(cancellationToken);
            return contribution;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            contribution.FailureCategory = exception is GitHubRateLimitException ? "rate-limit" : "service";
            contribution.AdvanceTo(
                ContributionState.RecoveryRequired,
                "GitHub work may have partially completed. Review the contributor fork before retrying.");
            await db.SaveChangesAsync(cancellationToken);
            throw new GitHubContributionRecoveryException(contribution, exception);
        }
    }

    public static ContributionView View(Contribution contribution) => new(
        contribution.Id,
        contribution.State.ToString(),
        contribution.PullRequestUrl,
        contribution.PullRequestNumber,
        contribution.FailureCategory,
        contribution.RecoveryMessage,
        contribution.UpdatedAt,
        contribution.LastReconciledAt);
}

public sealed class GitHubForkRequiredException(string url) : Exception("An eligible contributor fork is required.")
{
    public string ForkUrl { get; } = url;
}

public sealed class GitHubContributionRecoveryException(Contribution contribution, Exception innerException)
    : Exception("GitHub work may have partially completed.", innerException)
{
    public Contribution Contribution { get; } = contribution;
}





