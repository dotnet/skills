# Feature Specification: GitHub Skill Submissions

**Feature Branch**: `003-github-skill-submissions`

**Created**: 2026-07-29

**Status**: Draft

**Input**: User description: "Allow contributors to authenticate with GitHub, submit a validated normalized skill package through a fork branch and pull request, and track CI and review status without browser authoring, direct main commits, automatic merging, or LLM evaluation."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Submit a validated skill for review (Priority: P1)

A contributor with a valid uploaded skill signs in with GitHub, reviews the destination and affected files, and creates a pull request without arranging repository files or using Git commands.

**Why this priority**: A safe path from validated package to reviewable contribution is the core value.

**Independent Test**: Submit a valid new skill and verify that one pull request is created containing only normalized files at the approved destination.

**Acceptance Scenarios**:

1. **Given** a valid skill and an unauthenticated contributor, **When** submission starts, **Then** GitHub sign-in is required and the contributor returns to the current submission.
2. **Given** an authenticated contributor and valid new skill, **When** the review is confirmed, **Then** a fork branch and pull request are created.
3. **Given** a changed or invalid package, **When** submission is requested, **Then** revalidation fails with zero GitHub writes.
4. **Given** successful submission, **When** results appear, **Then** the pull-request link, status, and next steps are shown.

---

### User Story 2 - Safely propose an existing skill update (Priority: P2)

A contributor explicitly confirms an existing-skill update, reviews added, changed, and removed files, and submits without affecting unrelated content.

**Why this priority**: It supports maintenance after the independently useful new-skill path.

**Independent Test**: Submit a valid update and verify that only the approved existing skill directory changes.

**Acceptance Scenarios**:

1. **Given** a package matching an existing skill, **When** it is reviewed, **Then** it is labeled as an update with an affected-file summary.
2. **Given** an update crossing its skill boundary, **When** submitted, **Then** it is blocked before any GitHub write.
3. **Given** the source changed after preview, **When** submitted, **Then** the conflict requires a fresh review.

---

### User Story 3 - Track contribution progress (Priority: P3)

A contributor can return and see whether a contribution is awaiting checks, failing, awaiting review, merged, or closed.

**Why this priority**: Status visibility reduces confusion after submission.

**Independent Test**: Open known submissions in each lifecycle state and verify accurate state and next actions.

**Acceptance Scenarios**:

1. **Given** an open contribution, **When** checks or review change, **Then** the authoritative GitHub state is displayed.
2. **Given** a failed check, **When** status is viewed, **Then** the failed check and evidence link are shown without claiming acceptance.
3. **Given** a merged or closed contribution, **When** viewed, **Then** its terminal outcome is shown and duplicate submission is unavailable.

### Edge Cases

- Sign-in is denied, expired, or revoked; the user lacks a fork or required access.
- A same-name branch or pull request already exists, or a retry follows an uncertain response.
- A commit succeeds but pull-request creation fails.
- The destination collides by identity or casing, or the source changes concurrently.
- Content contains repository-control files, workflows, secrets, unsafe links, or path escapes.
- GitHub is rate-limited, degraded, delayed, or the pull request is edited outside the workspace.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST require authenticated GitHub identity before any contribution write.
- **FR-002**: The current validated submission MUST survive authentication without exposing content or credentials in URLs or persistent browser-managed storage.
- **FR-003**: The exact package MUST be revalidated immediately before any GitHub write.
- **FR-004**: The destination MUST derive from validated identity and policy; arbitrary repository paths MUST NOT be accepted.
- **FR-005**: Target repository, contribution type, destination, affected files, and pull-request summary MUST be shown before explicit confirmation.
- **FR-006**: Contributions MUST use a branch in the contributor's fork and a pull request against the configured catalog repository.
- **FR-007**: The system MUST NOT commit directly to the default branch, approve, or automatically merge.
- **FR-008**: Writes MUST be confined to normalized files in one approved skill boundary and reject workflows, repository-control files, secrets, unsafe links, and path escapes.
- **FR-009**: New skills and updates MUST be distinguished, and updates MUST require explicit confirmation.
- **FR-010**: Stale or conflicting updates MUST be blocked pending fresh review.
- **FR-011**: Retries MUST be idempotent and MUST NOT duplicate branches, commits, or pull requests.
- **FR-012**: Partial success MUST report actual state and a safe recovery path without hiding or automatically deleting contributor-owned work.
- **FR-013**: Users MUST be able to open the pull request and view check, review, merge, or closure state.
- **FR-014**: GitHub MUST remain authoritative and displayed state MUST show its refresh time.
- **FR-015**: Only minimal status and GitHub identifiers MAY persist; uploaded bytes MUST be discarded after processing.
- **FR-016**: Credentials MUST be least-privilege, revocable, excluded from logs and client-readable storage, and limited to the contribution flow.
- **FR-017**: Security and contribution transitions MUST be auditable without secrets or uploaded content.
- **FR-018**: All journeys MUST satisfy repository accessibility and responsive-design requirements.
- **FR-019**: The feature MUST NOT execute, author, edit, model-evaluate, or make merge judgments about uploaded skills.
- **FR-020**: Errors MUST distinguish validation, authentication, authorization, conflict, rate-limit, service, and partial-success conditions with actionable recovery.

### Key Entities

- **Contributor Session**: Authenticated identity, authorization state, expiry, and return context.
- **Submission Intent**: Immutable package revision, type, destination, file summary, and confirmation.
- **Contribution Record**: Minimal fork, branch, commit, pull request, status, timestamp, and recovery linkage.
- **Repository Revision**: Catalog state used for classification and conflict detection.
- **Contribution Status**: Preparing, submitted, checks pending or failed, awaiting review, merged, closed, or recovery required.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: At least 90% of eligible contributors with valid packages create a pull request on their first attempt without Git commands.
- **SC-002**: Successful submission returns a pull-request link within 15 seconds under normal conditions.
- **SC-003**: Every created pull request changes only normalized files inside one approved skill boundary.
- **SC-004**: Five repeats of the same confirmed submission produce no more than one open pull request.
- **SC-005**: Invalid, stale, unauthorized, or secret-bearing submissions cause zero GitHub writes.
- **SC-006**: Contributors identify current state and next action within 10 seconds of opening status.
- **SC-007**: Authentication and submission journeys pass WCAG 2.2 AA checks at supported sizes.
- **SC-008**: Audit evidence reconstructs every transition without reusable credentials or uploaded content.

## Assumptions

- The initial operator-configured target is `JonC613/skills`; contributors cannot change it.
- Contributors have GitHub accounts and may authorize a narrowly scoped fork workflow.
- Human maintainers retain review, approval, and merge responsibility.
- GitHub is authoritative for repository and pull-request state.
- Existing upload, validation, preview, and normalization are prerequisites.
- Minimal submission metadata persists for recovery; uploaded bytes do not.
- GitHub supplies external notifications; LLM evaluation is a separate future feature.
