namespace SkillCatalog.Api.Models;

public sealed record GitHubSessionView(bool Authenticated, long? GitHubUserId, string? Login, DateTimeOffset? ExpiresAt);
public sealed record AuthorizationStartResponse(Uri AuthorizationUrl, Guid TransactionId, DateTimeOffset ExpiresAt);
public sealed record SubmissionFileView(string Path, string Operation, string Sha256, long Size);
public sealed record SubmissionIntentView(Guid Id, string ContributionType, string TargetRepository, string DestinationPath, IReadOnlyList<SubmissionFileView> Files, string PullRequestTitle, DateTimeOffset ExpiresAt);
public sealed record ContributionEvidenceView(string Kind,string Label,string? Status,string? Url);
public sealed record ContributionView(Guid Id, string State, string? PullRequestUrl, int? PullRequestNumber, string? FailureCategory, string? RecoveryMessage, DateTimeOffset UpdatedAt, DateTimeOffset? LastReconciledAt, IReadOnlyList<ContributionEvidenceView>? Evidence=null);
public sealed record ApiFailure(string Category, string Message, string? NextAction, string TraceId);

