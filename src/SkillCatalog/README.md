# Skill Catalog

A repository-backed catalog for discovering, inspecting, and downloading the skills in this fork. The application uses an ASP.NET Core 10 Minimal API and a React 19 + Fluent UI frontend. Repository files remain the source of truth; there is no catalog database.

## Run locally

Prerequisites: .NET SDK 10.0.302+ and Node.js 22+.

```powershell
cd src/SkillCatalog
$env:DOTNET_CLI_HOME="$env:TEMP\skill-catalog-dotnet-home"
dotnet run --project api/SkillCatalog.Api
```

In a second terminal:

```powershell
cd src/SkillCatalog/web
npm ci
npm run dev
```

Open `http://localhost:5173`. Vite proxies `/api` to the API. If the API port changes, update `web/vite.config.ts`.

## Configuration

`SkillCatalog` in `api/SkillCatalog.Api/appsettings.json` controls the repository root, source URL, maximum preview size, and maximum file size permitted in an archive. At startup the API locates the nearest Git repository containing `plugins/`, parses each `SKILL.md`, and atomically exposes the resulting in-memory snapshot.

## Security boundaries

All preview and archive paths are canonicalized and must remain inside their skill directory. Reparse-point files and files above configured limits are excluded. Markdown is rendered as inert, sanitized content; repository content is never executed. Downloads contain only regular files from one skill plus a generated `skill-package.json` manifest.

## Verification

```powershell
cd src/SkillCatalog
dotnet test SkillCatalog.slnx
cd web
npm run build
npm test
npm audit --audit-level=high
```

OpenAPI is available at `/openapi/v1.json`; health is available at `/health`.

## Troubleshooting

- If SDK selection uses the repository's preview SDK, run commands from `src/SkillCatalog`, which has its own `global.json`.
- If Vite reports the `#` in the parent directory, the production build still succeeds; use a path without `#` if a future plugin cannot handle it.
- If the UI cannot reach the API, align the API launch port and the Vite proxy target.

## Validate a skill package

Open `/contribute/skill` and upload one repository-shaped ZIP or `SKILL.md`. The workspace is read-only: it validates untrusted content without executing it, displays file-specific findings, previews the parsed skill, and enables a normalized ZIP download only when no blocking errors remain.

Uploads are processed statelessly and are never retained. After explicit review and confirmation, normalized files are sent directly to GitHub repository APIs to create a contributor-fork branch and pull request. Correct source files in your editor and re-upload them. The workspace does not author skills, run evaluations, invoke an LLM, or create branches, commits, issues, or pull requests.

## GitHub contribution development

Feature 003 requires PostgreSQL and a development GitHub App installed on the target repository and an existing contributor fork. Configure secrets with environment variables or user-secrets; never commit client secrets, webhook secrets, database credentials, tokens, or data-protection keys. The app requests Contents read/write, Pull requests read/write, and Checks read; Administration and Workflows remain disabled.
## GitHub App registration and permissions

Register a GitHub App with the callback path `/api/auth/github/callback` and the webhook endpoint `/api/webhooks/github`. Use expiring user access tokens. Grant only repository **Contents: read and write**, **Pull requests: read and write**, and **Checks: read**. Do not grant Administration, Actions/Workflows write, default-branch bypass, merge, or approval permissions. Contributors must create and synchronize their own fork and grant the app access to it.

Configure these values through environment variables, user-secrets, or the deployment secret store:

- `ConnectionStrings__GitHubSubmissions`
- `GitHubSubmission__ClientId` and `GitHubSubmission__ClientSecret`
- `GitHubSubmission__WebhookSecret`
- `GitHubSubmission__DataProtectionKeyPath`
- `GitHubSubmission__TargetOwner`, `TargetRepository`, and `BaseBranch`
- `GitHubSubmission__AllowedOrigins__0` and subsequent exact HTTPS origins

Apply EF migrations before starting the API. The data-protection key directory must be durable, shared by every API instance, restricted to the application identity, and backed up separately from the database.

## Contributor and recovery workflow

Upload and validate a repository-shaped package, sign in through the popup, then review the immutable destination and grouped add/change/delete operations. Existing-skill updates require explicit confirmation and are rejected if the upstream base revision or contributor fork changes before submission. The application never writes the default branch and never merges or approves pull requests.

A recovery-required response means GitHub may have accepted an earlier branch, commit, or pull-request step. Inspect the linked contributor fork and pull request before retrying. Reusing the same idempotency key returns the existing contribution instead of creating a second pull request. Status pages reconcile with GitHub when webhooks are delayed or lost.

## Operations and secret rotation

Use `/health` for liveness and `/health/ready` for PostgreSQL, GitHub configuration, and durable-key readiness. Monitor 429 responses, recovery-required transitions, webhook signature failures, cleanup failures, and stale reconciliation timestamps without logging tokens, uploaded bytes, file contents, or webhook payloads.

Rotate the GitHub client secret and webhook secret in the deployment secret store, update every instance atomically, and retain the previous webhook secret only for the shortest supported overlap. Rotate data-protection keys through the shared key ring; do not delete keys while active sessions may still require them. Revoke compromised GitHub App tokens and sessions, inspect audit transitions, and reconcile affected pull requests directly with GitHub.
