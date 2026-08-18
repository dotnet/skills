export type GitHubSession = { authenticated:boolean; githubUserId?:number; login?:string; expiresAt?:string }
export type SubmissionFile = { path:string; operation:'add'|'change'|'delete'; sha256:string; size:number }
export type SubmissionIntent = { id:string; contributionType:'NewSkill'|'Update'; targetRepository:string; destinationPath:string; files:SubmissionFile[]; pullRequestTitle:string; expiresAt:string }
export type ContributionState = 'Preparing'|'ForkReady'|'BranchReady'|'CommitReady'|'PullRequestOpen'|'ChecksPending'|'AwaitingReview'|'Merged'|'Closed'|'RecoveryRequired'
export type ContributionEvidence = { kind:'pull-request'|'check'|'review'; label:string; status?:string; url?:string }
export type Contribution = { id:string; state:ContributionState; pullRequestUrl?:string; pullRequestNumber?:number; failureCategory?:string; recoveryMessage?:string; updatedAt:string; lastReconciledAt?:string; evidence?:ContributionEvidence[] }
export type ApiFailure = { category:string; message:string; nextAction?:string; traceId:string }

