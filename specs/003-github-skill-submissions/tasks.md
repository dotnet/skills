# Tasks: GitHub Skill Submissions

**Input**: Design documents from `/specs/003-github-skill-submissions/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md

**Tests**: Tests are required by the repository constitution and are written before corresponding implementation.

**Organization**: Tasks are grouped by independently testable user story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel in different files after its phase prerequisites
- **[Story]**: Maps work to US1, US2, or US3

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Add pinned dependencies and configuration boundaries without changing user behavior.

- [x] T001 Add pinned ASP.NET authentication, EF Core, PostgreSQL, and test-container dependencies in `src/SkillCatalog/api/SkillCatalog.Api/SkillCatalog.Api.csproj`, `src/SkillCatalog/api/SkillCatalog.Api.Tests/SkillCatalog.Api.Tests.csproj`, and `src/SkillCatalog/api/SkillCatalog.Api.ContractTests/SkillCatalog.Api.ContractTests.csproj`
- [x] T002 [P] Define GitHub App, target repository, session, webhook, retry, and retention options in `src/SkillCatalog/api/SkillCatalog.Api/Options/GitHubSubmissionOptions.cs`
- [x] T003 [P] Add secret-free development configuration keys in `src/SkillCatalog/api/SkillCatalog.Api/appsettings.json` and `src/SkillCatalog/api/SkillCatalog.Api/appsettings.Development.json`
- [x] T004 [P] Add GitHub submission API models matching `contracts/github-submission.openapi.yaml` in `src/SkillCatalog/api/SkillCatalog.Api/Models/GitHubSubmissionModels.cs`
- [x] T005 [P] Add React authentication, intent, contribution, and error models in `src/SkillCatalog/web/src/api/githubSubmissionModels.ts`
- [x] T006 Add database and GitHub App setup instructions to `src/SkillCatalog/README.md`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Build the security, persistence, and GitHub boundaries required by every story.

**⚠️ CRITICAL**: No user-story implementation begins until this phase passes its tests.

- [x] T007 [P] Add entity and state-transition tests for contributor sessions, intents, contributions, leases, deliveries, and audits in `src/SkillCatalog/api/SkillCatalog.Api.Tests/GitHubSubmissions/ContributionEntityTests.cs`
- [x] T008 Implement contributor session, submission intent, contribution, idempotency lease, webhook delivery, and audit entities in `src/SkillCatalog/api/SkillCatalog.Api/Persistence/GitHubSubmissionEntities.cs`
- [x] T009 Implement the EF Core context, constraints, optimistic concurrency, expiry indexes, and encrypted credential converters in `src/SkillCatalog/api/SkillCatalog.Api/Persistence/GitHubSubmissionDbContext.cs`
- [x] T010 Generate the initial PostgreSQL migration in `src/SkillCatalog/api/SkillCatalog.Api/Persistence/Migrations/`
- [x] T011 [P] Add migration, uniqueness, concurrency, expiry, and credential-at-rest integration tests in `src/SkillCatalog/api/SkillCatalog.Api.Tests/GitHubSubmissions/GitHubSubmissionPersistenceTests.cs`
- [x] T012 [P] Define the allowlisted, versioned GitHub transport interface and DTOs in `src/SkillCatalog/api/SkillCatalog.Api/GitHub/IGitHubContributionClient.cs`
- [x] T013 [P] Add a documentation-backed permission contract proving Contents read/write, Pull requests read/write, and Checks read cover every allowlisted endpoint while Administration and Workflows are rejected, plus host, pagination, rate-limit, retry, redaction, and error tests in `src/SkillCatalog/api/SkillCatalog.Api.Tests/GitHubSubmissions/GitHubContributionClientTests.cs`
- [x] T014 Implement the typed REST client for identity, installations, existing-fork verification, refs, trees, commits, pull requests, checks, and reviews in `src/SkillCatalog/api/SkillCatalog.Api/GitHub/GitHubContributionClient.cs`
- [x] T015 [P] Add authentication security tests for popup state, PKCE, exact-origin callback handshake, blocked/closed popup preservation, cookies, CSRF, expiry, refresh rotation, logout, token redaction, and shared key-ring restart/multi-instance recovery in `src/SkillCatalog/api/SkillCatalog.Api.ContractTests/GitHubAuthenticationTests.cs`
- [x] T016 Implement popup GitHub App authorization, exact-origin callback handshake, refresh, revocation, secure session cookies, durable isolated data-protection keys, and CSRF protection in `src/SkillCatalog/api/SkillCatalog.Api/Auth/GitHubContributorAuthentication.cs`
- [x] T017 Implement `/api/auth/github/start`, callback, session, and logout endpoints in `src/SkillCatalog/api/SkillCatalog.Api/Endpoints/GitHubAuthenticationEndpoints.cs`
- [x] T018 Register validated options, data protection, persistence, authentication, antiforgery, typed GitHub client, and endpoint mappings in `src/SkillCatalog/api/SkillCatalog.Api/Program.cs`

**Checkpoint**: Persistence, authentication, and GitHub transport are independently tested and ready.

---

## Phase 3: User Story 1 - Submit a validated skill for review (Priority: P1) 🎯 MVP

**Goal**: Authenticate, review, and submit one valid new skill as a fork-based pull request.

**Independent Test**: Submit a valid new skill with an eligible test identity and verify one pull request contains only normalized files at the approved destination.

### Tests for User Story 1

- [x] T019 [P] [US1] Add intent and submit endpoint contract tests for success, validation failure, authentication, authorization, conflict, rate limit, and recovery responses in `src/SkillCatalog/api/SkillCatalog.Api.ContractTests/GitHubSubmissionEndpointsTests.cs`
- [x] T020 [P] [US1] Add orchestration tests for exact-byte revalidation, derived destinations, forbidden files, idempotent retries, existing-fork readiness, a 15-second controlled-latency budget, and partial success in `src/SkillCatalog/api/SkillCatalog.Api.Tests/GitHubSubmissions/NewSkillContributionServiceTests.cs`
- [x] T021 [P] [US1] Add component tests for popup sign-in, exact-origin completion, blocked/closed popup with preserved upload, review, confirmation, loading, success, and actionable errors in `src/SkillCatalog/web/src/features/github-submission/GitHubSubmissionPage.test.tsx`
- [x] T022 [P] [US1] Add browser journeys for popup sign-in with upload preservation, new-skill review, confirmation, duplicate retry, PR link, keyboard use, and responsive layouts in `src/SkillCatalog/web/e2e/github-submission-new-skill.spec.ts`

### Implementation for User Story 1

- [x] T023 [P] [US1] Implement immutable intent creation, package hashing, manifest comparison, expiry, and destination derivation in `src/SkillCatalog/api/SkillCatalog.Api/Services/SubmissionIntentService.cs`
- [x] T024 [P] [US1] Implement idempotency lease acquisition, renewal, completion, and replay lookup in `src/SkillCatalog/api/SkillCatalog.Api/Services/ContributionIdempotencyService.cs`
- [x] T025 [US1] Implement recoverable existing-fork verification, ref, tree, commit, and pull-request orchestration in `src/SkillCatalog/api/SkillCatalog.Api/Services/NewSkillContributionService.cs`
- [x] T026 [US1] Implement intent and submit endpoints with multipart bounds, antiforgery, authorization, and correlation IDs in `src/SkillCatalog/api/SkillCatalog.Api/Endpoints/GitHubSubmissionEndpoints.cs`
- [x] T027 [P] [US1] Implement the browser API client without token or package persistence in `src/SkillCatalog/web/src/api/githubSubmissionClient.ts`
- [x] T028 [P] [US1] Implement popup GitHub sign-in, fork-creation guidance, app-installation guidance, and authenticated contributor controls in `src/SkillCatalog/web/src/features/github-submission/components/GitHubSignInPanel.tsx`
- [x] T029 [P] [US1] Implement immutable destination, affected-file, PR-summary, and confirmation review in `src/SkillCatalog/web/src/features/github-submission/components/SubmissionReview.tsx`
- [x] T030 [US1] Integrate popup authentication with in-memory upload preservation, intent creation, re-upload confirmation, submission, recovery, and success states in `src/SkillCatalog/web/src/features/github-submission/GitHubSubmissionPage.tsx`
- [x] T031 [US1] Route valid upload results into GitHub submission while preserving upload-only authoring boundaries in `src/SkillCatalog/web/src/features/skill-submission/SkillSubmissionPage.tsx` and `src/SkillCatalog/web/src/app/App.tsx`

**Checkpoint**: The new-skill MVP works end to end and creates at most one PR per confirmed intent.

---

## Phase 4: User Story 2 - Safely propose an existing skill update (Priority: P2)

**Goal**: Detect, review, and safely submit updates confined to one existing skill.

**Independent Test**: Submit a valid update and verify only the approved existing skill directory changes, while stale or cross-boundary updates produce zero writes.

### Tests for User Story 2

- [x] T032 [P] [US2] Add update classification, case-collision, manifest diff, deletion, ownership, base-revision, and stale-conflict tests in `src/SkillCatalog/api/SkillCatalog.Api.Tests/GitHubSubmissions/SkillUpdateContributionServiceTests.cs`
- [x] T033 [P] [US2] Add component tests for update labeling, added/changed/removed files, explicit confirmation, and conflict recovery in `src/SkillCatalog/web/src/features/github-submission/components/SubmissionReview.test.tsx`
- [x] T034 [P] [US2] Add browser journeys for valid update, stale base, concurrent change, and out-of-boundary rejection in `src/SkillCatalog/web/e2e/github-submission-update.spec.ts`

### Implementation for User Story 2

- [x] T035 [P] [US2] Implement repository revision snapshots and new-versus-update classification in `src/SkillCatalog/api/SkillCatalog.Api/Services/RepositoryRevisionService.cs`
- [x] T036 [US2] Implement boundary-safe update diffs, deletion policy, ownership checks, and compare-and-swap base verification in `src/SkillCatalog/api/SkillCatalog.Api/Services/SkillUpdateContributionService.cs`
- [x] T037 [US2] Extend intent and submission orchestration with explicit update confirmation and conflict responses in `src/SkillCatalog/api/SkillCatalog.Api/Services/SubmissionIntentService.cs` and `src/SkillCatalog/api/SkillCatalog.Api/Endpoints/GitHubSubmissionEndpoints.cs`
- [x] T038 [US2] Add update badges, file-operation grouping, confirmation, and refresh-required states in `src/SkillCatalog/web/src/features/github-submission/components/SubmissionReview.tsx`

**Checkpoint**: New-skill and update journeys remain independently testable.

---

## Phase 5: User Story 3 - Track contribution progress (Priority: P3)

**Goal**: Display authoritative checks, review, merge, closure, and recovery states.

**Independent Test**: Reconcile fixtures for every lifecycle state and verify the displayed state, evidence links, refresh time, and next action.

### Tests for User Story 3

- [x] T039 [P] [US3] Add webhook signature, replay, delivery-deduplication, payload-bound, and event-mapping contract tests in `src/SkillCatalog/api/SkillCatalog.Api.ContractTests/GitHubWebhookTests.cs`
- [x] T040 [P] [US3] Add reconciliation tests for checks, reviews, external PR edits, terminal states, rate limits, and webhook loss in `src/SkillCatalog/api/SkillCatalog.Api.Tests/GitHubSubmissions/ContributionStatusServiceTests.cs`
- [x] T041 [P] [US3] Add status component tests for all states, refresh age, evidence links, and recovery actions in `src/SkillCatalog/web/src/features/github-submission/ContributionStatusPage.test.tsx`
- [x] T042 [P] [US3] Add browser journeys for pending, failed, review, merged, closed, delayed webhook, polling fallback, and accessibility in `src/SkillCatalog/web/e2e/github-submission-status.spec.ts`

### Implementation for User Story 3

- [x] T043 [P] [US3] Implement constant-time webhook signature verification, delivery deduplication, and event mapping in `src/SkillCatalog/api/SkillCatalog.Api/GitHub/GitHubWebhookProcessor.cs`
- [x] T044 [US3] Implement authoritative contribution reconciliation, refresh throttling, transitions, and audit evidence in `src/SkillCatalog/api/SkillCatalog.Api/Services/ContributionStatusService.cs`
- [x] T045 [US3] Implement signed webhook and contributor-owned status endpoints in `src/SkillCatalog/api/SkillCatalog.Api/Endpoints/GitHubContributionStatusEndpoints.cs`
- [x] T046 [US3] Implement contribution timeline, check evidence, refresh age, terminal outcomes, and recovery actions in `src/SkillCatalog/web/src/features/github-submission/ContributionStatusPage.tsx`
- [x] T047 [US3] Add contribution status routing and post-submission navigation in `src/SkillCatalog/web/src/app/App.tsx` and `src/SkillCatalog/web/src/features/github-submission/GitHubSubmissionPage.tsx`

**Checkpoint**: All three stories are functional, secure, accessible, and independently demonstrable.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Validate operational safety, documentation, deployment, and the complete contract.

- [x] T048 [P] Add retention cleanup and revoked-session background processing tests in `src/SkillCatalog/api/SkillCatalog.Api.Tests/GitHubSubmissions/GitHubSubmissionCleanupTests.cs`
- [x] T049 Implement bounded cleanup for expired sessions, intents, leases, deliveries, and audit retention in `src/SkillCatalog/api/SkillCatalog.Api/Services/GitHubSubmissionCleanupService.cs`
- [x] T050 [P] Add telemetry assertions proving tokens, package bytes, file contents, and webhook payloads are redacted in `src/SkillCatalog/api/SkillCatalog.Api.ContractTests/GitHubSubmissionTelemetryTests.cs`
- [x] T051 Add health checks for database, data-protection keys, and GitHub configuration without external write probes in `src/SkillCatalog/api/SkillCatalog.Api/Program.cs`
- [x] T052 [P] Update contributor, GitHub App registration, permission, secret rotation, recovery, and operator guidance in `src/SkillCatalog/README.md`
- [x] T053 [P] Add deployment environment and secret requirements to `specs/003-github-skill-submissions/quickstart.md`
- [x] T054 Add submission performance and non-execution regression tests with fake GitHub latency, executable-adjacent files, and prompt-injection fixtures in `src/SkillCatalog/api/SkillCatalog.Api.Tests/GitHubSubmissions/GitHubSubmissionSecurityPerformanceTests.cs`
- [x] T055 Validate OpenAPI security-scheme parity, migrations, API tests, component tests, production build, Playwright, accessibility, security, performance, vulnerability, markdown, and actionlint gates from `specs/003-github-skill-submissions/quickstart.md`
- [x] T056 Review specification, plan, research, data model, contract, implementation, tests, and completed task status for SDD drift in `specs/003-github-skill-submissions/`

---

## Dependencies & Execution Order

### Phase Dependencies

- Phase 1 starts immediately.
- Phase 2 depends on Phase 1 and blocks every user story.
- US1, US2, and US3 depend on Phase 2. Prioritize US1; US2 and US3 can proceed independently once their required contribution records exist.
- Phase 6 follows the stories selected for release.

### User Story Dependencies

- **US1 (P1)**: No story dependency after Phase 2; this is the MVP.
- **US2 (P2)**: Reuses intent and orchestration boundaries from US1 but is independently verified with update fixtures.
- **US3 (P3)**: Reuses persisted contribution identifiers from US1 but is independently verified with seeded lifecycle fixtures.

### Within Each Story

- Write tests first and confirm they fail for the intended behavior.
- Complete models and boundaries before orchestration.
- Complete orchestration before endpoints and UI integration.
- Pass the independent checkpoint before moving to a later story.

### Parallel Opportunities

- T002-T005 can run in parallel after T001.
- T007, T011-T013, and T015 target separate foundational test and contract files.
- T019-T024 and T027-T029 can be divided between API, security, and web work before integration.
- US2 test tasks T032-T034 and US3 test tasks T039-T042 are parallelizable after Phase 2.
- T048, T050, T052, and T053 are independent cross-cutting files.

## Parallel Example: User Story 1

```text
API contracts: T019
Orchestration tests: T020
React component tests: T021
Browser journeys: T022
Intent service: T023
Idempotency service: T024
Browser client and components: T027-T029
```

## Parallel Example: User Stories 2 and 3

```text
Update slice: T032-T038
Status slice: T039-T047
```

## Implementation Strategy

### MVP First

1. Complete Setup and Foundational phases.
2. Complete US1 through T031.
3. Run the US1 independent test and all security gates.
4. Demonstrate sign-in through one reviewable new-skill PR before adding updates or tracking.

### Incremental Delivery

1. Foundation establishes authentication, persistence, and GitHub isolation.
2. US1 delivers new-skill PR creation.
3. US2 adds conflict-safe existing-skill updates.
4. US3 adds authoritative lifecycle tracking.
5. Polish adds cleanup, operations, documentation, and full regression gates.

## Notes

- Every task uses an exact repository path and maps to its phase or user story.
- `[P]` tasks operate in separate files but still respect phase prerequisites.
- No task introduces browser authoring, direct default-branch writes, automatic merging, instruction execution, uploaded-byte persistence, or LLM evaluation.
- Commit after each tested logical group and update SDD artifacts before accepting scope drift.
