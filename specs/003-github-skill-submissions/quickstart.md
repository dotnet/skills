# Quickstart: GitHub Skill Submissions

## Prerequisites

- .NET SDK pinned by `src/SkillCatalog/global.json`
- Node.js and `npm`
- PostgreSQL
- A development GitHub App installed on the target test repository and an existing contributor-owned fork
- HTTPS callback and webhook URLs

## Required configuration

Configure the shared durable data-protection key ring, GitHub App client ID, client secret, app identity, callback URL, webhook secret, target repository, API version, and database connection through environment-backed configuration. Never commit these values.

## Run

1. Start PostgreSQL and apply migrations.
2. Start the API from `src/SkillCatalog`.
3. Install web dependencies and start the SPA from `src/SkillCatalog/web`.
4. Upload and validate a test skill, sign in with the development GitHub App, review the intent, and submit to the test repository.

## Verification gates

- API unit and contract tests
- GitHub permission-contract tests proving Contents read/write, Pull requests read/write, and Checks read are sufficient while Administration and Workflows remain disabled
- GitHub client fault, rate-limit, permission, and partial-success tests
- persistence migration and concurrency tests
- authentication, CSRF, webhook-signature, idempotency, secret-redaction, and path-boundary tests
- component and production build tests
- Playwright new-skill, update, retry, failure-recovery, status, responsive, and accessibility journeys
- sandbox integration test proving the exact minimum GitHub App permissions

Production deployment requires durable data-protection keys, HTTPS-only cookies, database backups, secret rotation, webhook reachability, health checks, and no uploaded-package retention.
## Deployment environment and secrets

| Setting | Required | Source and handling |
|---|---:|---|
| `ConnectionStrings__GitHubSubmissions` | Yes | Deployment secret store; PostgreSQL with TLS, backups, and least-privilege schema access |
| `GitHubSubmission__ClientId` | Yes | GitHub App registration; environment configuration |
| `GitHubSubmission__ClientSecret` | Yes | Secret store only; rotate without committing or logging |
| `GitHubSubmission__WebhookSecret` | Yes | Secret store only; high-entropy value shared with GitHub webhook registration |
| `GitHubSubmission__DataProtectionKeyPath` | Yes | Durable shared encrypted volume accessible to every API instance |
| `GitHubSubmission__TargetOwner`, `TargetRepository`, `BaseBranch` | Yes | Non-secret environment configuration |
| `GitHubSubmission__AllowedOrigins__N` | Yes | Exact production HTTPS SPA origins; no wildcards |
| `GitHubSubmission__StatusRefreshSeconds`, `RetentionDays`, `MaxWebhookBytes` | Yes | Bounded operational configuration |

Apply `dotnet ef database update` as a deployment migration step before shifting traffic. Expose `/health` as liveness and `/health/ready` as readiness. The webhook endpoint must be publicly reachable over HTTPS, while PostgreSQL and the data-protection volume remain private. Use sticky sessions only if required by the platform; shared database and key-ring state are designed for multi-instance operation.

Do not place secrets in React build variables, repository files, container images, command-line arguments, telemetry, or CI artifacts. Production verification must exercise a dedicated test repository and contributor fork and must never target the default branch directly.
