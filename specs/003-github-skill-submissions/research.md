# Research: GitHub Skill Submissions

## GitHub integration identity

**Decision**: Register a GitHub App and use expiring user access tokens for contributor-attributed fork and pull-request operations.

**Rationale**: GitHub Apps provide fine-grained permissions, revocation, installation scoping, and user-to-server attribution. Effective access is limited by both app and user permissions.

**Alternatives considered**: Broad OAuth app scopes were rejected as excessive; personal access tokens were rejected as unsafe contributor UX; an installation-only bot was rejected because contributor-owned fork activity should remain attributable and authorized by the contributor.

## API integration

**Decision**: Use a typed, versioned REST client over `HttpClient` rather than adding a general GitHub SDK.

**Rationale**: The workflow needs a small auditable endpoint set: identity/installations, fork lookup or creation, refs, trees/blobs/commits, pull requests, checks, and reviews. Explicit contracts simplify permission testing, rate-limit behavior, and failure simulation.

**Alternatives considered**: Git CLI requires disk and credential handling; a large SDK obscures HTTP behavior and adds dependency surface; GraphQL does not eliminate required Git data writes.

## Permissions

**Decision**: Request Contents read/write, Pull requests read/write, and Checks read. Never request Workflows or Administration. If a contributor lacks a fork, direct them through GitHub's fork UI and require app installation on that fork.

**Rationale**: GitHub requires Administration write plus Contents read to create a fork programmatically, while refs, trees, and commits require Contents write, pull-request creation requires Pull requests write, and status requires Checks read. Removing programmatic fork creation preserves least privilege.

**Alternatives considered**: Programmatic fork creation and Workflow permission were rejected. A permission-contract test will assert this documented matrix before transport implementation.

## Authentication state preservation

**Decision**: Complete authorization in a popup and return success through an exact-origin postMessage handshake; keep package bytes only in opener memory.

**Rationale**: Full-page redirect destroys browser memory, while package persistence expands exposure. Popup failure leaves the validated upload intact.

**Alternatives considered**: Persistent browser storage and server-side package handoff were rejected.

## Authentication and credential handling

**Decision**: Keep authorization state and tokens server-side, encrypted at rest through application data protection, with secure HTTP-only cookies containing only an opaque session identifier. Enable expiring tokens and refresh rotation. Persist the data-protection key ring in shared protected storage with application isolation and rotation so restarts and multiple instances can decrypt active sessions.

**Rationale**: Browser storage and URLs must never contain reusable credentials. Server-side revocation and expiry support logout and recovery.

**Alternatives considered**: Stateless token cookies increase leakage and rotation risk; browser token storage violates the security boundary.

## Durable state

**Decision**: Use PostgreSQL through EF Core for sessions, immutable submission intents, idempotency leases, contribution records, webhook delivery IDs, and audit transitions.

**Rationale**: Multi-step external writes need transactional local state, concurrency control, expiry, and recovery across process restarts and multiple instances.

**Alternatives considered**: In-memory state cannot recover; SQLite complicates multi-instance deployment; storing uploaded packages is unnecessary and increases risk.

## Submission algorithm

**Decision**: Re-send the selected package, bind it to an intent hash and base revision, acquire an idempotency lease, revalidate, then create or reuse each GitHub resource in a recoverable state machine.

**Rationale**: GitHub does not provide one atomic fork-to-PR operation. Persisted checkpoints prevent duplicates after timeouts or retries.

**Alternatives considered**: Best-effort sequential calls without checkpoints cannot distinguish failure from partial success; automatic rollback could destroy contributor-owned work.

## Status synchronization

**Decision**: Accept signed, deduplicated GitHub App webhooks for low-latency updates and reconcile status through authenticated API reads on view and periodic refresh.

**Rationale**: Webhooks can be delayed or lost, while polling alone is slower and consumes rate limits. GitHub remains authoritative.

**Alternatives considered**: Webhook-only state can become stale; polling-only state is wasteful and less responsive.

## Sources

- GitHub Docs: About creating GitHub Apps
- GitHub Docs: Choosing permissions for a GitHub App
- GitHub Docs: Generating a user access token for a GitHub App
- GitHub Docs: REST API endpoints for forks
- GitHub Docs: Using webhooks with GitHub Apps
