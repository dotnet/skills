using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SkillCatalog.Api.GitHub;
using SkillCatalog.Api.Options;
using SkillCatalog.Api.Persistence;
using SkillCatalog.Api.Services;

namespace SkillCatalog.Api.Tests.GitHubSubmissions;

public sealed class NewSkillContributionServiceTests
{
    [Fact]
    public async Task Creates_one_branch_commit_and_pull_request_from_exact_files()
    {
        await using var db = CreateContext();
        var github = new RecordingGitHubClient();
        var service = CreateService(db, github);
        var intent = SubmissionIntent.Create(Guid.NewGuid(), "hash", "NewSkill", "plugins/dotnet/skills/sample", "base", "key");
        var session = Session();
        var files = new Dictionary<string, byte[]> { ["plugins/dotnet/skills/sample/SKILL.md"] = "content"u8.ToArray() };

        var result = await service.ExecuteAsync(intent, session, "token", files, CancellationToken.None);

        Assert.Equal(ContributionState.PullRequestOpen, result.State);
        Assert.Equal("https://github.com/upstream/skills/pull/7", result.PullRequestUrl);
        Assert.Equal(files.Keys, github.CommittedFiles);
        Assert.Equal(1, github.BranchCreates);
        Assert.Equal(1, github.PullRequestCreates);
    }

    [Fact]
    public async Task Missing_eligible_fork_causes_zero_writes_and_actionable_guidance()
    {
        await using var db = CreateContext();
        var github = new RecordingGitHubClient { EligibleFork = null };
        var service = CreateService(db, github);

        var exception = await Assert.ThrowsAsync<GitHubForkRequiredException>(() => service.ExecuteAsync(
            SubmissionIntent.Create(Guid.NewGuid(), "hash", "NewSkill", "plugins/dotnet/skills/sample", "base", "key"),
            Session(),
            "token",
            new Dictionary<string, byte[]>(),
            CancellationToken.None));

        Assert.Contains("/fork", exception.ForkUrl);
        Assert.Equal(0, github.BranchCreates);
        Assert.Equal(0, github.PullRequestCreates);
        Assert.Empty(db.Contributions);
    }

    [Fact]
    public async Task Partial_success_enters_recovery_without_retrying_writes()
    {
        await using var db = CreateContext();
        var github = new RecordingGitHubClient { FailPullRequest = true };
        var service = CreateService(db, github);

        await Assert.ThrowsAsync<GitHubContributionRecoveryException>(() => service.ExecuteAsync(
            SubmissionIntent.Create(Guid.NewGuid(), "hash", "NewSkill", "plugins/dotnet/skills/sample", "base", "key"),
            Session(),
            "token",
            new Dictionary<string, byte[]> { ["SKILL.md"] = "content"u8.ToArray() },
            CancellationToken.None));

        var contribution = await db.Contributions.SingleAsync();
        Assert.Equal(ContributionState.RecoveryRequired, contribution.State);
        Assert.Contains("partially", contribution.RecoveryMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, github.PullRequestCreates);
    }

    [Fact]
    public async Task Controlled_orchestration_finishes_within_fifteen_second_budget()
    {
        await using var db = CreateContext();
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        await CreateService(db, new RecordingGitHubClient()).ExecuteAsync(
            SubmissionIntent.Create(Guid.NewGuid(), "hash", "NewSkill", "plugins/dotnet/skills/sample", "base", "key"),
            Session(), "token", new Dictionary<string, byte[]> { ["plugins/dotnet/skills/sample/SKILL.md"] = "content"u8.ToArray() }, CancellationToken.None);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(15), $"Submission took {stopwatch.Elapsed}.");
    }

    [Fact]
    public async Task Completed_idempotency_lease_replays_existing_contribution()
    {
        await using var db = CreateContext();
        var idempotency = new ContributionIdempotencyService(db, TimeProvider.System);
        var intentId = Guid.NewGuid();
        var acquired = await idempotency.AcquireAsync(42, "retry-key", intentId, CancellationToken.None);
        var contribution = Contribution.Create(intentId, 42, "octocat", "skills", "branch");
        db.Add(contribution);
        await db.SaveChangesAsync();
        await idempotency.CompleteAsync(acquired.Lease, contribution.Id, CancellationToken.None);

        var replay = await idempotency.AcquireAsync(42, "retry-key", intentId, CancellationToken.None);
        Assert.Equal(contribution.Id, replay.Existing?.Id);
    }

    [Fact]
    public async Task Update_with_stale_fork_head_causes_zero_writes()
    {
        await using var db = CreateContext();
        var github = new RecordingGitHubClient { EligibleFork = new GitHubRepository("octocat", "skills", "main", "stale", true) };
        var intent = SubmissionIntent.Create(Guid.NewGuid(), "hash", "Update", "plugins/dotnet/skills/sample", "reviewed", "key");
        intent.Confirm("hash");
        await Assert.ThrowsAsync<RepositoryRevisionConflictException>(() => CreateService(db, github).ExecuteAsync(
            intent, Session(), "token", new Dictionary<string, byte[]> { ["plugins/dotnet/skills/sample/SKILL.md"] = "content"u8.ToArray() }, CancellationToken.None));
        Assert.Equal(0, github.BranchCreates);
        Assert.Empty(db.Contributions);
    }
    private static NewSkillContributionService CreateService(GitHubSubmissionDbContext db, IGitHubContributionClient github) =>
        new(db, github, Microsoft.Extensions.Options.Options.Create(new GitHubSubmissionOptions { TargetOwner = "upstream", TargetRepository = "skills" }));

    private static ContributorSession Session() => new()
    {
        GitHubUserId = 42,
        GitHubLogin = "octocat",
        ProtectedAccessToken = "protected",
        AccessExpiresAt = DateTimeOffset.UtcNow.AddHours(1)
    };

    private static GitHubSubmissionDbContext CreateContext() => new(
        new DbContextOptionsBuilder<GitHubSubmissionDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private sealed class RecordingGitHubClient : IGitHubContributionClient
    {
        public GitHubRepository? EligibleFork { get; set; } = new("octocat", "skills", "main", "base", true);
        public bool FailPullRequest { get; set; }
        public int BranchCreates { get; private set; }
        public int PullRequestCreates { get; private set; }
        public IReadOnlyCollection<string> CommittedFiles { get; private set; } = [];

        public Task<GitHubIdentity> GetIdentityAsync(string token, CancellationToken ct) => Task.FromResult(new GitHubIdentity(42, "octocat"));
        public Task<IReadOnlyList<GitHubInstallation>> GetInstallationsAsync(string token, CancellationToken ct) => Task.FromResult<IReadOnlyList<GitHubInstallation>>([]);
        public Task<GitHubRepositorySnapshot> GetTargetSnapshotAsync(string token, CancellationToken ct) => throw new NotSupportedException();
        public Task<GitHubRepository?> GetEligibleForkAsync(string token, string login, CancellationToken ct) => Task.FromResult(EligibleFork);
        public Task CreateBranchAsync(string token, string owner, string repository, string branch, string sha, CancellationToken ct) { BranchCreates++; return Task.CompletedTask; }
        public Task UpdateBranchAsync(string token, string owner, string repository, string branch, string sha, CancellationToken ct) => Task.CompletedTask;
        public Task<string> CreateCommitAsync(string token, string owner, string repository, string branch, string baseTree, IReadOnlyList<GitHubFileChange> changes, string message, CancellationToken ct) { CommittedFiles = changes.Select(change => change.Path).ToArray(); return Task.FromResult("commit"); }
        public Task<GitHubPullRequest> CreatePullRequestAsync(string token, string headOwner, string headBranch, string title, string body, CancellationToken ct) { PullRequestCreates++; return FailPullRequest ? Task.FromException<GitHubPullRequest>(new HttpRequestException("GitHub unavailable")) : Task.FromResult(new GitHubPullRequest(7, "https://github.com/upstream/skills/pull/7", "open", "commit")); }
        public Task<GitHubPullRequest> GetPullRequestAsync(string token, int number, CancellationToken ct) => Task.FromResult(new GitHubPullRequest(number, "url", "open", "commit"));
        public Task<IReadOnlyList<GitHubCheck>> GetChecksAsync(string token, string sha, CancellationToken ct) => Task.FromResult<IReadOnlyList<GitHubCheck>>([]);
        public Task<IReadOnlyList<GitHubReview>> GetReviewsAsync(string token, int pullRequestNumber, CancellationToken ct) => Task.FromResult<IReadOnlyList<GitHubReview>>([]);
    }
}




