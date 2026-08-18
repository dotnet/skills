namespace SkillCatalog.Api.GitHub;

public static class GitHubPermissionContract
{
    public static readonly string[] Required = ["Contents:write", "PullRequests:write", "Checks:read"];
}
public sealed record GitHubIdentity(long Id,string Login);
public sealed record GitHubRepository(string Owner,string Name,string DefaultBranch,string HeadSha,bool IsFork);
public sealed record GitHubInstallation(long Id,string AccountLogin,IReadOnlyDictionary<string,string> Permissions);
public sealed record GitHubPullRequest(int Number,string Url,string State,string HeadSha,bool Merged=false);
public sealed record GitHubCheck(string Name,string Status,string? Conclusion,string Url);
public sealed record GitHubReview(long Id,string State,string? Url);
public sealed record GitHubTreeEntry(string Path,string Type,string Sha,long? Size);
public sealed record GitHubRepositorySnapshot(string CommitSha,IReadOnlyList<GitHubTreeEntry> Entries);
public sealed record GitHubFileChange(string Path,byte[]? Content);
public interface IGitHubContributionClient
{
    Task<GitHubIdentity> GetIdentityAsync(string token,CancellationToken ct);
    Task<IReadOnlyList<GitHubInstallation>> GetInstallationsAsync(string token,CancellationToken ct);
    Task<GitHubRepository?> GetEligibleForkAsync(string token,string login,CancellationToken ct);
    Task CreateBranchAsync(string token,string owner,string repository,string branch,string sha,CancellationToken ct);
    Task UpdateBranchAsync(string token,string owner,string repository,string branch,string sha,CancellationToken ct);
    Task<string> CreateCommitAsync(string token,string owner,string repository,string branch,string baseTree,IReadOnlyList<GitHubFileChange> changes,string message,CancellationToken ct);
    Task<GitHubRepositorySnapshot> GetTargetSnapshotAsync(string token,CancellationToken ct);
    Task<GitHubPullRequest> CreatePullRequestAsync(string token,string headOwner,string headBranch,string title,string body,CancellationToken ct);
    Task<GitHubPullRequest> GetPullRequestAsync(string token,int number,CancellationToken ct);
    Task<IReadOnlyList<GitHubCheck>> GetChecksAsync(string token,string sha,CancellationToken ct);
    Task<IReadOnlyList<GitHubReview>> GetReviewsAsync(string token,int pullRequestNumber,CancellationToken ct);
}




