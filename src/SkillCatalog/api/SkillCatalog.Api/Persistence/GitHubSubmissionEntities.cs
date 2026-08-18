using System.Security.Cryptography;
using System.ComponentModel.DataAnnotations;

namespace SkillCatalog.Api.Persistence;

public enum ContributionState { Preparing, ForkReady, BranchReady, CommitReady, PullRequestOpen, ChecksPending, AwaitingReview, Merged, Closed, RecoveryRequired }

public sealed class AuthorizationTransaction
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [MaxLength(128)] public string StateDigest { get; set; } = "";
    [MaxLength(128)] public string PkceVerifier { get; set; } = "";
    [MaxLength(256)] public string OpenerOrigin { get; set; } = "";
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}
public sealed class ContributorSession
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public long GitHubUserId { get; set; }
    [MaxLength(128)] public string GitHubLogin { get; set; } = "";
    public string ProtectedAccessToken { get; set; } = "";
    public string? ProtectedRefreshToken { get; set; }
    public DateTimeOffset AccessExpiresAt { get; set; }
    public DateTimeOffset? RefreshExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastUsedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? RevokedAt { get; set; }
}
public sealed class SubmissionIntent
{
    public Guid Id { get; set; }
    public Guid ContributorSessionId { get; set; }
    [MaxLength(64)] public string PackageSha256 { get; set; } = "";
    [MaxLength(64)] public string ValidationRevision { get; set; } = "";
    [MaxLength(16)] public string ContributionType { get; set; } = "";
    [MaxLength(128)] public string TargetOwner { get; set; } = "";
    [MaxLength(128)] public string TargetRepository { get; set; } = "";
    [MaxLength(128)] public string BaseBranch { get; set; } = "main";
    [MaxLength(128)] public string PluginId { get; set; } = "";
    [MaxLength(128)] public string SkillId { get; set; } = "";
    [MaxLength(512)] public string DestinationPath { get; set; } = "";
    [MaxLength(64)] public string BaseCommitSha { get; set; } = "";
    [MaxLength(128)] public string IdempotencyKey { get; set; } = "";
    [MaxLength(256)] public string PullRequestTitle { get; set; } = "";
    [MaxLength(4000)] public string PullRequestBody { get; set; } = "";
    public string FileManifestJson { get; set; } = "[]";
    public DateTimeOffset? ConfirmedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; } = DateTimeOffset.UtcNow.AddMinutes(30);
    public static SubmissionIntent Create(Guid sessionId,string packageSha,string type,string destination,string baseSha,string key)=>new(){Id=Guid.NewGuid(),ContributorSessionId=sessionId,PackageSha256=packageSha,ValidationRevision=packageSha,ContributionType=type,DestinationPath=destination,BaseCommitSha=baseSha,IdempotencyKey=key};
    public void Confirm(string packageSha)
    {
        var expected = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(PackageSha256));
        var actual = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(packageSha));
        if (!CryptographicOperations.FixedTimeEquals(expected, actual))
        {
            throw new InvalidOperationException("Package mismatch.");
        }

        ConfirmedAt = DateTimeOffset.UtcNow;
    }
}
public sealed class Contribution
{
    public Guid Id { get; set; }
    public Guid SubmissionIntentId { get; set; }
    public long ContributorGitHubUserId { get; set; }
    public string ForkOwner { get; set; } = "";
    public string ForkRepository { get; set; } = "";
    public string BranchName { get; set; } = "";
    public string? CommitSha { get; set; }
    public int? PullRequestNumber { get; set; }
    public string? PullRequestUrl { get; set; }
    public ContributionState State { get; set; }
    [MaxLength(64)] public string LastCompletedStep { get; set; } = "Preparing";
    public string? FailureCategory { get; set; }
    public string? RecoveryMessage { get; set; }
    public DateTimeOffset CreatedAt { get; set; }=DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; }=DateTimeOffset.UtcNow;
    public DateTimeOffset? LastReconciledAt { get; set; }
    [Timestamp] public uint Version { get; set; }
    public static Contribution Create(Guid intentId,long userId,string owner,string repo,string branch)=>new(){Id=Guid.NewGuid(),SubmissionIntentId=intentId,ContributorGitHubUserId=userId,ForkOwner=owner,ForkRepository=repo,BranchName=branch,State=ContributionState.Preparing};
    public void AdvanceTo(ContributionState next,string reason){var allowed=State switch{ContributionState.Preparing=>next is ContributionState.ForkReady or ContributionState.RecoveryRequired,ContributionState.ForkReady=>next is ContributionState.BranchReady or ContributionState.RecoveryRequired,ContributionState.BranchReady=>next is ContributionState.CommitReady or ContributionState.RecoveryRequired,ContributionState.CommitReady=>next is ContributionState.PullRequestOpen or ContributionState.RecoveryRequired,ContributionState.PullRequestOpen=>next is ContributionState.ChecksPending or ContributionState.AwaitingReview or ContributionState.Merged or ContributionState.Closed or ContributionState.RecoveryRequired,ContributionState.ChecksPending=>next is ContributionState.AwaitingReview or ContributionState.Merged or ContributionState.Closed or ContributionState.RecoveryRequired,ContributionState.AwaitingReview=>next is ContributionState.Merged or ContributionState.Closed or ContributionState.ChecksPending or ContributionState.RecoveryRequired,_=>false};if(!allowed)throw new InvalidOperationException($"Invalid transition {State} -> {next}.");State=next;LastCompletedStep=next.ToString();UpdatedAt=DateTimeOffset.UtcNow;RecoveryMessage=next==ContributionState.RecoveryRequired?reason:null;}
}
public sealed class IdempotencyLease { public long ContributorGitHubUserId {get;set;} public string IdempotencyKey {get;set;}=""; public Guid? SubmissionIntentId{get;set;} public Guid? ContributionId{get;set;} public string LeaseOwner{get;set;}=""; public DateTimeOffset LeaseExpiresAt{get;set;} public DateTimeOffset? CompletedAt{get;set;} }
public sealed class WebhookDelivery { [Key] public string DeliveryId{get;set;}=""; public string EventName{get;set;}=""; public string Action{get;set;}=""; public DateTimeOffset ReceivedAt{get;set;}=DateTimeOffset.UtcNow; public DateTimeOffset? ProcessedAt{get;set;} public Guid? ContributionId{get;set;} public string PayloadDigest{get;set;}=""; public string? Outcome{get;set;} }
public sealed class AuditTransition { public long Id{get;set;} public Guid ContributionId{get;set;} public string ActorType{get;set;}=""; public string? ActorId{get;set;} public ContributionState FromState{get;set;} public ContributionState ToState{get;set;} public string ReasonCode{get;set;}=""; public DateTimeOffset OccurredAt{get;set;}=DateTimeOffset.UtcNow; public string? GitHubResourceId{get;set;} public string? CorrelationId{get;set;} }



