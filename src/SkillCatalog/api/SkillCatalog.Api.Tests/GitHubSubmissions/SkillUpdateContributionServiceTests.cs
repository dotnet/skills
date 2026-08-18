using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SkillCatalog.Api.GitHub;
using SkillCatalog.Api.Persistence;
using SkillCatalog.Api.Services;

namespace SkillCatalog.Api.Tests.GitHubSubmissions;

public sealed class SkillUpdateContributionServiceTests
{
    private const string Destination = "plugins/dotnet/skills/sample";

    [Fact]
    public async Task Classifies_new_and_existing_skills_from_authoritative_tree()
    {
        var fresh = new SnapshotGitHubClient(new GitHubRepositorySnapshot("base", []));
        var newAnalysis = await new RepositoryRevisionService(fresh).AnalyzeAsync(
            "token", Destination, Files(("SKILL.md", "new")), CancellationToken.None);
        Assert.Equal("NewSkill", newAnalysis.ContributionType);
        Assert.All(newAnalysis.Manifest, file => Assert.Equal("add", file.Operation));

        var existing = new SnapshotGitHubClient(Snapshot(("SKILL.md", "old")));
        var update = await new RepositoryRevisionService(existing).AnalyzeAsync(
            "token", Destination, Files(("SKILL.md", "changed")), CancellationToken.None);
        Assert.Equal("Update", update.ContributionType);
        Assert.Equal("change", Assert.Single(update.Manifest).Operation);
    }

    [Fact]
    public async Task Manifest_groups_add_change_and_delete_operations()
    {
        var github = new SnapshotGitHubClient(Snapshot(
            ("SKILL.md", "old"), ("remove.txt", "remove"), ("same.txt", "same")));
        var analysis = await new RepositoryRevisionService(github).AnalyzeAsync(
            "token",
            Destination,
            Files(("SKILL.md", "changed"), ("same.txt", "same"), ("added.txt", "added")),
            CancellationToken.None);

        Assert.Contains(analysis.Manifest, file => file.Path.EndsWith("added.txt") && file.Operation == "add");
        Assert.Contains(analysis.Manifest, file => file.Path.EndsWith("SKILL.md") && file.Operation == "change");
        Assert.Contains(analysis.Manifest, file => file.Path.EndsWith("remove.txt") && file.Operation == "delete");
        Assert.DoesNotContain(analysis.Manifest, file => file.Path.EndsWith("same.txt"));
        Assert.Contains(analysis.Changes, change => change.Path.EndsWith("remove.txt") && change.Content is null);
    }

    [Fact]
    public async Task Rejects_case_collision_and_cross_boundary_file()
    {
        var github = new SnapshotGitHubClient(Snapshot(("SKILL.md", "old")));
        var revisions = new RepositoryRevisionService(github);
        await Assert.ThrowsAsync<InvalidDataException>(() => revisions.AnalyzeAsync(
            "token", Destination,
            Files(("SKILL.md", "new"), ("skill.md", "collision")), CancellationToken.None));

        var escaped = new Dictionary<string, byte[]>
        {
            ["plugins/dotnet/skills/other/escape.txt"] = "escape"u8.ToArray()
        };
        await Assert.ThrowsAsync<InvalidDataException>(() => revisions.AnalyzeAsync(
            "token", Destination, escaped, CancellationToken.None));
    }

    [Fact]
    public async Task Explicit_confirmation_session_ownership_and_base_revision_are_required()
    {
        var github = new SnapshotGitHubClient(Snapshot(("SKILL.md", "old")));
        var revisions = new RepositoryRevisionService(github);
        var files = Files(("SKILL.md", "new"));
        var analysis = await revisions.AnalyzeAsync("token", Destination, files, CancellationToken.None);
        var session = new ContributorSession { Id = Guid.NewGuid(), GitHubUserId = 42 };
        var intent = SubmissionIntent.Create(session.Id, "hash", "Update", Destination, "base", "key");
        intent.FileManifestJson = JsonSerializer.Serialize(analysis.Manifest);
        var service = new SkillUpdateContributionService(revisions);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ValidateAsync(
            intent, session, "token", files, CancellationToken.None));
        intent.Confirm("hash");
        var other = new ContributorSession { Id = Guid.NewGuid(), GitHubUserId = 42 };
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ValidateAsync(
            intent, other, "token", files, CancellationToken.None));

        github.Snapshot = github.Snapshot with { CommitSha = "new-base" };
        await Assert.ThrowsAsync<RepositoryRevisionConflictException>(() => service.ValidateAsync(
            intent, session, "token", files, CancellationToken.None));
    }

    private static IReadOnlyDictionary<string, byte[]> Files(params (string Path, string Content)[] files) =>
        files.ToDictionary(
            file => $"{Destination}/{file.Path}",
            file => Encoding.UTF8.GetBytes(file.Content),
            StringComparer.Ordinal);

    private static GitHubRepositorySnapshot Snapshot(params (string Path, string Content)[] files) => new(
        "base",
        files.Select(file => new GitHubTreeEntry(
            $"{Destination}/{file.Path}", "blob", GitBlobSha(Encoding.UTF8.GetBytes(file.Content)), file.Content.Length)).ToArray());

    private static string GitBlobSha(byte[] content)
    {
        var header = Encoding.UTF8.GetBytes($"blob {content.Length}\0");
        return Convert.ToHexString(SHA1.HashData(header.Concat(content).ToArray())).ToLowerInvariant();
    }

    private sealed class SnapshotGitHubClient(GitHubRepositorySnapshot snapshot) : IGitHubContributionClient
    {
        public GitHubRepositorySnapshot Snapshot { get; set; } = snapshot;
        public Task<GitHubRepositorySnapshot> GetTargetSnapshotAsync(string token, CancellationToken ct) => Task.FromResult(Snapshot);
        public Task<GitHubIdentity> GetIdentityAsync(string token, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<GitHubInstallation>> GetInstallationsAsync(string token, CancellationToken ct) => throw new NotSupportedException();
        public Task<GitHubRepository?> GetEligibleForkAsync(string token, string login, CancellationToken ct) => throw new NotSupportedException();
        public Task CreateBranchAsync(string token, string owner, string repository, string branch, string sha, CancellationToken ct) => throw new NotSupportedException();
        public Task UpdateBranchAsync(string token, string owner, string repository, string branch, string sha, CancellationToken ct) => throw new NotSupportedException();
        public Task<string> CreateCommitAsync(string token, string owner, string repository, string branch, string baseTree, IReadOnlyList<GitHubFileChange> changes, string message, CancellationToken ct) => throw new NotSupportedException();
        public Task<GitHubPullRequest> CreatePullRequestAsync(string token, string headOwner, string headBranch, string title, string body, CancellationToken ct) => throw new NotSupportedException();
        public Task<GitHubPullRequest> GetPullRequestAsync(string token, int number, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<GitHubCheck>> GetChecksAsync(string token, string sha, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<GitHubReview>> GetReviewsAsync(string token, int pullRequestNumber, CancellationToken ct) => throw new NotSupportedException();
    }
}
