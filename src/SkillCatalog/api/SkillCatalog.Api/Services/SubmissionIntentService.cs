using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SkillCatalog.Api.Models;
using SkillCatalog.Api.Options;
using SkillCatalog.Api.Persistence;

namespace SkillCatalog.Api.Services;

public sealed class SubmissionIntentService(
    GitHubSubmissionDbContext db,
    SkillPackageParser parser,
    RepositoryRevisionService revisions,
    IOptions<GitHubSubmissionOptions> options,
    TimeProvider time)
{
    private readonly GitHubSubmissionOptions _options = options.Value;

    public async Task<(SubmissionIntent Intent, SubmissionIntentView View)> CreateAsync(
        Guid sessionId,
        string token,
        IFormFile file,
        CancellationToken cancellationToken)
    {
        var parsed = await parser.InspectAsync(file, cancellationToken);
        if (!parsed.Inspection.Valid) throw new SubmissionValidationException(parsed.Inspection);
        var preview = parsed.Inspection.Preview;
        if (string.IsNullOrWhiteSpace(preview.Plugin) || string.IsNullOrWhiteSpace(preview.Name))
        {
            throw new InvalidDataException("A repository-shaped package with plugin and skill identity is required for GitHub submission.");
        }

        var destination = $"plugins/{preview.Plugin}/skills/{preview.Name}";
        var analysis = await revisions.AnalyzeAsync(token, destination, parsed.Files, cancellationToken);
        if (analysis.Manifest.Count == 0)
        {
            throw new InvalidDataException("The uploaded package contains no changes from the target repository.");
        }
        var intent = SubmissionIntent.Create(
            sessionId,
            parsed.Inspection.UploadRevision,
            analysis.ContributionType,
            destination,
            analysis.BaseCommitSha,
            Guid.NewGuid().ToString("N"));
        intent.TargetOwner = _options.TargetOwner;
        intent.TargetRepository = _options.TargetRepository;
        intent.BaseBranch = _options.BaseBranch;
        intent.PluginId = preview.Plugin;
        intent.SkillId = preview.Name;
        intent.PullRequestTitle = $"{(analysis.ContributionType == "Update" ? "Update" : "Contribute")} {preview.Name} skill";
        intent.PullRequestBody = "Created through the Skill Catalog contributor workspace.";
        intent.FileManifestJson = JsonSerializer.Serialize(analysis.Manifest);
        intent.ExpiresAt = time.GetUtcNow().AddSeconds(_options.IntentLifetimeSeconds);
        db.Add(intent);
        await db.SaveChangesAsync(cancellationToken);
        return (intent, new SubmissionIntentView(
            intent.Id,
            intent.ContributionType,
            $"{_options.TargetOwner}/{_options.TargetRepository}",
            destination,
            analysis.Manifest,
            intent.PullRequestTitle,
            intent.ExpiresAt));
    }

    public async Task<(SubmissionIntent Intent, IReadOnlyDictionary<string, byte[]> Files)> RevalidateAsync(
        Guid intentId,
        Guid sessionId,
        IFormFile file,
        bool explicitUpdateConfirmation,
        CancellationToken cancellationToken)
    {
        var intent = await db.SubmissionIntents.SingleOrDefaultAsync(
            x => x.Id == intentId && x.ContributorSessionId == sessionId,
            cancellationToken) ?? throw new KeyNotFoundException();
        if (intent.ExpiresAt <= time.GetUtcNow()) throw new InvalidOperationException("Submission intent expired.");
        var parsed = await parser.InspectAsync(file, cancellationToken);
        if (!parsed.Inspection.Valid) throw new SubmissionValidationException(parsed.Inspection);
        if (!string.Equals(intent.PackageSha256, parsed.Inspection.UploadRevision, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Uploaded package no longer matches the reviewed submission.");
        }
        if (string.Equals(intent.ContributionType, "Update", StringComparison.Ordinal) && !explicitUpdateConfirmation)
        {
            throw new InvalidOperationException("Explicit update confirmation is required.");
        }
        intent.Confirm(parsed.Inspection.UploadRevision);
        await db.SaveChangesAsync(cancellationToken);
        return (intent, parsed.Files);
    }
}

public sealed class SubmissionValidationException(UploadInspection inspection) : Exception("Package validation failed.")
{
    public UploadInspection Inspection { get; } = inspection;
}
