# Data Model: GitHub Skill Submissions

## AuthorizationTransaction

- `Id`, state digest, PKCE verifier digest, opener origin, expiry, and one-time completion timestamp

Rules: short-lived and single-use; contains no package bytes; callback completion is communicated only to the exact allowed opener origin.

## ContributorSession

- `Id`: opaque identifier
- `GitHubUserId`, `GitHubLogin`: stable identity and display name
- `EncryptedAccessToken`, `EncryptedRefreshToken`: server-readable only
- `AccessExpiresAt`, `RefreshExpiresAt`, `RevokedAt`
- `CreatedAt`, `LastUsedAt`

Rules: one active browser session maps to one GitHub identity; expired or revoked credentials cannot authorize writes; credentials never enter API responses or logs.

## SubmissionIntent

- `Id`, `ContributorSessionId`
- `PackageSha256`, `ValidationRevision`
- `ContributionType`: `NewSkill` or `Update`
- `TargetOwner`, `TargetRepository`, `BaseBranch`, `BaseCommitSha`
- `PluginId`, `SkillId`, `DestinationPath`
- `FileManifest`: normalized path, operation, content hash, size
- `PullRequestTitle`, `PullRequestBody`
- `IdempotencyKey`, `ConfirmedAt`, `ExpiresAt`

Rules: immutable after confirmation; one skill boundary; destination is derived; expired or mismatched package hashes require a new intent.

## Contribution

- `Id`, `SubmissionIntentId`, `ContributorGitHubUserId`
- `ForkOwner`, `ForkRepository`
- `BranchName`, `CommitSha`, `PullRequestNumber`, `PullRequestUrl`
- `State`, `LastCompletedStep`, `FailureCategory`, `RecoveryMessage`
- `CreatedAt`, `UpdatedAt`, `LastReconciledAt`
- concurrency token

States: `Preparing -> ForkReady -> BranchReady -> CommitReady -> PullRequestOpen -> ChecksPending -> AwaitingReview -> Merged|Closed`; any nonterminal state may enter `RecoveryRequired`. Reconciliation may move among open-status states but terminal states do not reopen locally without authoritative evidence.

## IdempotencyLease

- `ContributorGitHubUserId`, `IdempotencyKey` composite identity
- `SubmissionIntentId`, `ContributionId`
- `LeaseOwner`, `LeaseExpiresAt`, `CompletedAt`

Rules: one active executor; retry returns the existing contribution; abandoned leases expire safely.

## WebhookDelivery

- `DeliveryId`, `EventName`, `Action`
- `ReceivedAt`, `ProcessedAt`, `ContributionId`
- payload digest and processing outcome, not raw payload

Rules: delivery IDs are unique; signatures are verified before persistence; duplicates are acknowledged without reprocessing.

## AuditTransition

- `Id`, `ContributionId`, `ActorType`, `ActorId`
- `FromState`, `ToState`, `ReasonCode`, `OccurredAt`
- GitHub resource identifiers and correlation ID

Rules: append-only; excludes tokens, package bytes, file contents, and webhook payloads.
