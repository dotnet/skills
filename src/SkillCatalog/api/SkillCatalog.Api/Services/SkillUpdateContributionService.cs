using SkillCatalog.Api.GitHub;
using SkillCatalog.Api.Persistence;

namespace SkillCatalog.Api.Services;

public sealed class SkillUpdateContributionService(RepositoryRevisionService revisions)
{
    public async Task<IReadOnlyList<GitHubFileChange>> ValidateAsync(
        SubmissionIntent intent,
        ContributorSession session,
        string token,
        IReadOnlyDictionary<string, byte[]> files,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(intent.ContributionType, "Update", StringComparison.Ordinal))
        {
            return files.Select(file => new GitHubFileChange(file.Key, file.Value)).ToArray();
        }
        if (intent.ContributorSessionId != session.Id)
        {
            throw new UnauthorizedAccessException("The reviewed update belongs to another contributor session.");
        }
        if (intent.ConfirmedAt is null)
        {
            throw new InvalidOperationException("Explicit update confirmation is required.");
        }

        await revisions.VerifyBaseAsync(token, intent.BaseCommitSha, cancellationToken);
        var analysis = await revisions.AnalyzeAsync(token, intent.DestinationPath, files, cancellationToken);
        if (!string.Equals(analysis.ContributionType, "Update", StringComparison.Ordinal))
        {
            throw new RepositoryRevisionConflictException(intent.BaseCommitSha, analysis.BaseCommitSha);
        }
        if (!EquivalentManifest(intent.FileManifestJson, analysis.Manifest))
        {
            throw new RepositoryRevisionConflictException(intent.BaseCommitSha, analysis.BaseCommitSha);
        }
        return analysis.Changes;
    }

    private static bool EquivalentManifest(string reviewedJson, IReadOnlyList<Models.SubmissionFileView> current)
    {
        var reviewed = System.Text.Json.JsonSerializer.Deserialize<Models.SubmissionFileView[]>(reviewedJson) ?? [];
        return reviewed.OrderBy(item => item.Path, StringComparer.Ordinal)
            .SequenceEqual(current.OrderBy(item => item.Path, StringComparer.Ordinal));
    }
}
