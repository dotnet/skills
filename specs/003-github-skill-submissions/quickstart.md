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
