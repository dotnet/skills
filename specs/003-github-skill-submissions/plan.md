# Implementation Plan: GitHub Skill Submissions

**Branch**: `003-github-skill-submissions` | **Date**: 2026-07-29 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/003-github-skill-submissions/spec.md`

## Summary

Extend the validated upload workspace with GitHub App sign-in, an immutable submission review, fork-branch commit and pull-request creation, and contribution status tracking. The API revalidates the package before writes, uses idempotency and repository-revision guards, persists only encrypted authentication material plus minimal contribution metadata, and treats GitHub as authoritative.

## Technical Context

**Language/Version**: C# 14 / .NET 10.0.302; TypeScript 6 / React 19

**Primary Dependencies**: ASP.NET Core authentication, data protection, typed HttpClient, EF Core with PostgreSQL, React Router, Fluent UI

**Storage**: PostgreSQL for encrypted contributor sessions, idempotency records, and contribution metadata; uploaded bytes remain request-scoped

**Testing**: xUnit unit/contract/integration/security tests, Vitest, Playwright, axe, GitHub API fakes, production builds

**Target Platform**: Linux containers behind HTTPS; evergreen desktop and mobile browsers

**Project Type**: Web application with .NET API and React SPA

**Performance Goals**: Review loads within 2 seconds; normal submission returns a PR link within 15 seconds; status refresh within 5 seconds

**Constraints**: No direct default-branch writes, automatic merge, arbitrary paths, uploaded-content persistence, instruction execution, or LLM calls; WCAG 2.2 AA

**Scale/Scope**: One configured catalog repository, one skill boundary per submission, initial hundreds of contributors and thousands of contribution records

## Constitution Check

| Principle | Status | Evidence |
|---|---|---|
| Versioned products | PASS | Submission is bound to normalized skill identity, revision, ownership, and repository destination. |
| Deterministic validation first | PASS | Exact bytes are revalidated before every GitHub write. |
| Independent roles | PASS | The workflow creates reviewable PRs but cannot approve or merge them. |
| Reproducible evidence | PASS | Intent hash, repository revision, commit, PR, transitions, and failure evidence are recorded. |
| Secure repository content | PASS | Paths are derived, bytes are request-scoped, secrets fail closed, and content is never executed. |
| Open compatibility | PASS | Canonical repository skill packages remain unchanged. |
| Accessible incremental value | PASS | New submission, update, and tracking are independently testable journeys. |

Pre-research gate: PASS. Post-design gate: PASS. No constitution exceptions are required.

## Project Structure

### Documentation (this feature)

```text
specs/003-github-skill-submissions/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/github-submission.openapi.yaml
└── tasks.md
```

### Source Code (repository root)

```text
src/SkillCatalog/
├── api/
│   ├── SkillCatalog.Api/
│   │   ├── Auth/
│   │   ├── Endpoints/
│   │   ├── GitHub/
│   │   ├── Models/
│   │   ├── Persistence/
│   │   └── Services/
│   ├── SkillCatalog.Api.Tests/
│   └── SkillCatalog.Api.ContractTests/
└── web/
    ├── src/features/github-submission/
    ├── src/api/
    └── e2e/
```

**Structure Decision**: Extend the existing API and SPA. Keep GitHub transport, persistence, submission orchestration, and UI state separated behind explicit contracts so deterministic validation remains reusable and GitHub can be faked in tests.

## Security and Reliability Design

- GitHub App authorization-code flow in a popup with state, PKCE where supported, exact callback and opener-origin allowlisting, a postMessage completion handshake, secure HTTP-only SameSite cookies, expiring user tokens, refresh rotation, and server-side data protection. Popup failure leaves the in-memory upload intact.
- Request forgery protection on all mutation endpoints; no token or package bytes in URLs, logs, telemetry, or browser persistence.
- Typed GitHub REST client pins the API version, allowlists hosts and repository coordinates, bounds retries, honors rate limits, and never follows untrusted redirects.
- Submission intent includes package SHA-256, base revision, destination, file manifest, contributor identity, and idempotency key. The API locks the intent, revalidates bytes, verifies an existing eligible contributor fork and installation, then creates/reuses branch, commit, and PR. Fork creation remains in GitHub UI to avoid Administration-write permission.
- The GitHub App permission contract is Contents read/write (writes allowlisted to the contributor fork), Pull requests read/write, and Checks read; Workflows and Administration are not requested.
- Data-protection keys are persisted in a shared protected key ring with application isolation and rotation; restart and multi-instance recovery are tested.
- Signed webhook deliveries are deduplicated and update cached state; reads reconcile with GitHub so webhook loss cannot create false terminal states.
- Partial success records the last confirmed GitHub resource and exposes recovery. No automated deletion of contributor branches.

## Complexity Tracking

No constitution violations.
