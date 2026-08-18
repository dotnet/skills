using System.Security.Cryptography;
using System.Text;
using SkillCatalog.Api.GitHub;
using SkillCatalog.Api.Models;

namespace SkillCatalog.Api.Services;

public sealed record RepositoryRevisionAnalysis(
    string ContributionType,
    string BaseCommitSha,
    IReadOnlyList<SubmissionFileView> Manifest,
    IReadOnlyList<GitHubFileChange> Changes);

public sealed class RepositoryRevisionService(IGitHubContributionClient github)
{
    public async Task<RepositoryRevisionAnalysis> AnalyzeAsync(
        string token,
        string destinationPath,
        IReadOnlyDictionary<string, byte[]> uploadedFiles,
        CancellationToken cancellationToken)
    {
        ValidateBoundary(destinationPath, uploadedFiles.Keys);
        var snapshot = await github.GetTargetSnapshotAsync(token, cancellationToken);
        var prefix = destinationPath.TrimEnd('/') + "/";
        var existing = snapshot.Entries
            .Where(entry => entry.Type == "blob" && (entry.Path.Equals(destinationPath, StringComparison.Ordinal) || entry.Path.StartsWith(prefix, StringComparison.Ordinal)))
            .ToDictionary(entry => entry.Path, StringComparer.Ordinal);
        RejectCaseCollisions(existing.Keys.Concat(uploadedFiles.Keys));

        var isUpdate = existing.Count > 0;
        var manifest = new List<SubmissionFileView>();
        var changes = new List<GitHubFileChange>();
        foreach (var file in uploadedFiles.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            var operation = existing.TryGetValue(file.Key, out var current)
                ? string.Equals(current.Sha, GitBlobSha(file.Value), StringComparison.OrdinalIgnoreCase) ? null : "change"
                : "add";
            if (operation is null) continue;
            manifest.Add(new SubmissionFileView(
                file.Key,
                operation,
                Convert.ToHexString(SHA256.HashData(file.Value)).ToLowerInvariant(),
                file.Value.LongLength));
            changes.Add(new GitHubFileChange(file.Key, file.Value));
        }

        foreach (var removed in existing.Values.Where(entry => !uploadedFiles.ContainsKey(entry.Path)).OrderBy(entry => entry.Path, StringComparer.Ordinal))
        {
            if (removed.Path.EndsWith("/SKILL.md", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("An update cannot remove the required SKILL.md file.");
            }
            manifest.Add(new SubmissionFileView(removed.Path, "delete", removed.Sha, removed.Size ?? 0));
            changes.Add(new GitHubFileChange(removed.Path, null));
        }

        return new RepositoryRevisionAnalysis(
            isUpdate ? "Update" : "NewSkill",
            snapshot.CommitSha,
            manifest,
            changes);
    }

    public async Task VerifyBaseAsync(string token, string expectedCommitSha, CancellationToken cancellationToken)
    {
        var current = await github.GetTargetSnapshotAsync(token, cancellationToken);
        if (!string.Equals(current.CommitSha, expectedCommitSha, StringComparison.Ordinal))
        {
            throw new RepositoryRevisionConflictException(expectedCommitSha, current.CommitSha);
        }
    }

    private static void ValidateBoundary(string destinationPath, IEnumerable<string> paths)
    {
        var normalizedDestination = destinationPath.Replace('\\', '/').TrimEnd('/');
        var prefix = normalizedDestination + "/";
        foreach (var path in paths)
        {
            var normalized = path.Replace('\\', '/');
            if (normalized.StartsWith('/') || normalized.Split('/').Any(segment => segment is ".." or ".") ||
                !(normalized.Equals(normalizedDestination, StringComparison.Ordinal) || normalized.StartsWith(prefix, StringComparison.Ordinal)))
            {
                throw new InvalidDataException($"File '{path}' crosses the approved skill boundary.");
            }
        }
    }

    private static void RejectCaseCollisions(IEnumerable<string> paths)
    {
        var collision = paths.GroupBy(path => path, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Distinct(StringComparer.Ordinal).Skip(1).Any());
        if (collision is not null)
        {
            throw new InvalidDataException($"Case-colliding repository paths are not allowed: {string.Join(", ", collision)}");
        }
    }

    private static string GitBlobSha(byte[] content)
    {
        var header = Encoding.UTF8.GetBytes($"blob {content.Length}\0");
        var combined = new byte[header.Length + content.Length];
        Buffer.BlockCopy(header, 0, combined, 0, header.Length);
        Buffer.BlockCopy(content, 0, combined, header.Length, content.Length);
        return Convert.ToHexString(SHA1.HashData(combined)).ToLowerInvariant();
    }
}

public sealed class RepositoryRevisionConflictException(string expected, string actual)
    : Exception("The target repository changed after review. Refresh and review the update again.")
{
    public string ExpectedCommitSha { get; } = expected;
    public string ActualCommitSha { get; } = actual;
}
