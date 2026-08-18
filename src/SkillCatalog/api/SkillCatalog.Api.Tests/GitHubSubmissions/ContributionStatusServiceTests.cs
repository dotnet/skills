using Microsoft.EntityFrameworkCore;
using SkillCatalog.Api.GitHub;
using SkillCatalog.Api.Options;
using SkillCatalog.Api.Persistence;
using SkillCatalog.Api.Services;

namespace SkillCatalog.Api.Tests.GitHubSubmissions;

public sealed class ContributionStatusServiceTests
{
    [Fact]
    public async Task Reconciliation_maps_pending_checks_and_records_refresh_time()
    {
        await using var db = CreateContext();
        var contribution = SeedContribution(db);
        await db.SaveChangesAsync();
        var github = new StatusGitHubClient
        {
            Checks = [new GitHubCheck("build", "in_progress", null, "https://checks/1")]
        };
        var service = CreateService(db, github);

        var result = await service.GetAsync(contribution.Id, Session(), "token", true, CancellationToken.None);

        Assert.Equal("ChecksPending", result!.State);
        Assert.NotNull(result.LastReconciledAt);
        Assert.Single(db.AuditTransitions);
    }

    [Fact]
    public async Task Authoritative_merge_is_terminal()
    {
        await using var db = CreateContext();
        var contribution = SeedContribution(db);
        await db.SaveChangesAsync();
        var github = new StatusGitHubClient
        {
            PullRequest = new GitHubPullRequest(7, "https://pull/7", "closed", "sha", true)
        };

        var result = await CreateService(db, github).GetAsync(
            contribution.Id, Session(), "token", true, CancellationToken.None);

        Assert.Equal("Merged", result!.State);
        Assert.Equal(ContributionState.Merged, contribution.State);
    }

    [Fact]
    public async Task Another_contributor_cannot_read_or_trigger_GitHub_calls()
    {
        await using var db = CreateContext();
        var contribution = SeedContribution(db);
        await db.SaveChangesAsync();
        var github = new StatusGitHubClient();
        var other = Session();
        other.GitHubUserId = 999;

        var result = await CreateService(db, github).GetAsync(
            contribution.Id, other, "token", true, CancellationToken.None);

        Assert.Null(result);
        Assert.Equal(0, github.PullRequestReads);
    }

    [Fact]
    public async Task Recent_reconciliation_is_throttled()
    {
        await using var db = CreateContext();
        var contribution = SeedContribution(db);
        contribution.LastReconciledAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
        var github = new StatusGitHubClient();

        _ = await CreateService(db, github).GetAsync(
            contribution.Id, Session(), "token", false, CancellationToken.None);

        Assert.Equal(0, github.PullRequestReads);
    }

    private static Contribution SeedContribution(GitHubSubmissionDbContext db)
    {
        var contribution = Contribution.Create(Guid.NewGuid(), 42, "octocat", "skills", "branch");
        contribution.AdvanceTo(ContributionState.ForkReady, "fork");
        contribution.AdvanceTo(ContributionState.BranchReady, "branch");
        contribution.AdvanceTo(ContributionState.CommitReady, "commit");
        contribution.AdvanceTo(ContributionState.PullRequestOpen, "pr");
        contribution.PullRequestNumber = 7;
        contribution.PullRequestUrl = "https://pull/7";
        contribution.CommitSha = "sha";
        db.Add(contribution);
        return contribution;
    }

    private static ContributorSession Session() => new()
    {
        GitHubUserId = 42,
        GitHubLogin = "octocat",
        ProtectedAccessToken = "protected",
        AccessExpiresAt = DateTimeOffset.UtcNow.AddHours(1)
    };

    private static ContributionStatusService CreateService(GitHubSubmissionDbContext db, IGitHubContributionClient github) =>
        new(db, github, Microsoft.Extensions.Options.Options.Create(new GitHubSubmissionOptions()), TimeProvider.System);

    private static GitHubSubmissionDbContext CreateContext() => new(
        new DbContextOptionsBuilder<GitHubSubmissionDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed class StatusGitHubClient : IGitHubContributionClient
    {
        public GitHubPullRequest PullRequest { get; set; } = new(7, "https://pull/7", "open", "sha");
        public IReadOnlyList<GitHubCheck> Checks { get; set; } = [];
        public int PullRequestReads { get; private set; }
        public Task<GitHubPullRequest> GetPullRequestAsync(string token, int number, CancellationToken ct) { PullRequestReads++; return Task.FromResult(PullRequest); }
        public Task<IReadOnlyList<GitHubCheck>> GetChecksAsync(string token, string sha, CancellationToken ct) => Task.FromResult(Checks);
        public Task<IReadOnlyList<GitHubReview>> GetReviewsAsync(string token, int pullRequestNumber, CancellationToken ct) => Task.FromResult<IReadOnlyList<GitHubReview>>([]);
        public Task<GitHubIdentity> GetIdentityAsync(string token, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<GitHubInstallation>> GetInstallationsAsync(string token, CancellationToken ct) => throw new NotSupportedException();
        public Task<GitHubRepositorySnapshot> GetTargetSnapshotAsync(string token, CancellationToken ct) => throw new NotSupportedException();
        public Task<GitHubRepository?> GetEligibleForkAsync(string token, string login, CancellationToken ct) => throw new NotSupportedException();
        public Task CreateBranchAsync(string token, string owner, string repository, string branch, string sha, CancellationToken ct) => throw new NotSupportedException();
        public Task UpdateBranchAsync(string token, string owner, string repository, string branch, string sha, CancellationToken ct) => throw new NotSupportedException();
        public Task<string> CreateCommitAsync(string token, string owner, string repository, string branch, string baseTree, IReadOnlyList<GitHubFileChange> changes, string message, CancellationToken ct) => throw new NotSupportedException();
        public Task<GitHubPullRequest> CreatePullRequestAsync(string token, string headOwner, string headBranch, string title, string body, CancellationToken ct) => throw new NotSupportedException();
    }
}

