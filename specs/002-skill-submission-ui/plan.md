# Implementation Plan: Guided Skill Submission Workspace

**Branch**: `002-skill-submission-ui` | **Date**: 2026-07-26 | **Spec**: [spec.md](./spec.md)

## Summary

Extend the existing catalog with a guided workspace that authors repository-shaped skills, generates current Vally evaluations, performs deterministic repository and safety validation, previews canonical output, and downloads a ZIP. Drafts remain session-local, API processing is stateless, and GitHub writes and model execution remain out of scope.

## Technical Context

**Language/Version**: C# 14 / .NET 10; TypeScript 6 / React 19

**Primary Dependencies**: ASP.NET Core, YamlDotNet, System.IO.Compression, React Router, Fluent UI, React Markdown/GFM

**Storage**: Browser session storage; repository files as authoritative rule data; no database or server draft persistence

**Testing**: xUnit unit/contract tests, Vitest, Playwright, axe accessibility, production builds and vulnerability checks

**Target Platform**: Modern desktop/mobile browsers and cross-platform .NET host

**Project Type**: Existing C# API plus React web application

**Performance Goals**: Full validation within 2 seconds for 20 resources and 20 scenarios; package generation within 3 seconds at configured limits

**Constraints**: Anonymous/stateless; no GitHub writes; no content execution; fail-closed paths and archives; WCAG 2.2 AA; preserve catalog behavior and upstream layout

**Scale/Scope**: One active draft per browser tab, existing plugins only, up to 20 resources and 20 evaluation scenarios

## Constitution Check

*GATE: Passed before research and re-checked after design.*

| Principle or constraint | Status | Evidence |
|---|---|---|
| Skills are versioned products | PASS | Package includes purpose, boundaries, inputs, workflow, validation, owners, evals, and contribution evidence. |
| Deterministic validation first | PASS | Static validation blocks packaging; LLM execution is out of scope. |
| Independent author/executor/judge | PASS | UI authors scenarios but neither executes nor judges them. |
| Reproducible evidence | PASS | Generated evals, graders, rubric, paths, and contribution summary ship together. |
| Secure repository content | PASS | Untrusted content is never executed; paths, references, secrets, and archive roots fail closed. |
| Open agent compatibility | PASS | Canonical Agent Skills and repository Vally formats are generated. |
| Accessible incremental value | PASS | P1 author/package, P2 evaluations, and P3 validate/preview are separately testable. |
| Existing layout and stack | PASS | Extends current SkillCatalog C# API, React/Fluent UI, and repository paths. |
| Repository authoritative | PASS | Plugins, identities, domains, and contribution rules come from the checkout. |
| Dependencies minimized | PASS | Platform ZIP support and existing UI/YAML packages are reused. |

Post-design re-check: PASS. Contracts are stateless and bounded, no mutable catalog copy or identity system is added, preview and packaging share canonical rendering, and all required quality gates are represented in quickstart.

## Project Structure

### Documentation

```text
specs/002-skill-submission-ui/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/skill-submission.openapi.yaml
└── tasks.md
```

### Source Code

```text
src/SkillCatalog/
├── api/
│   ├── SkillCatalog.Api/
│   │   ├── Endpoints/SubmissionEndpoints.cs
│   │   ├── Models/SubmissionModels.cs
│   │   ├── Options/SkillSubmissionOptions.cs
│   │   └── Services/{SubmissionRuleProvider,SkillDraftValidator,SkillSubmissionRenderer,SkillSubmissionPackageService}.cs
│   ├── SkillCatalog.Api.Tests/Submissions/
│   └── SkillCatalog.Api.ContractTests/SubmissionEndpointsTests.cs
└── web/
    ├── src/api/submissionClient.ts
    ├── src/features/skill-submission/
    │   ├── SkillSubmissionPage.tsx
    │   ├── submissionDraft.ts
    │   └── components/{AuthorStep,EvaluationStep,ResourceEditor,ValidationSummary,SubmissionPreview,PackageStep}.tsx
    └── e2e/skill-submission*.spec.ts
```

**Structure Decision**: Extend the existing SkillCatalog projects and isolate submission code as a feature module while sharing catalog snapshots, Markdown rendering, theme, telemetry, and test infrastructure.

## Design Decisions

1. Client owns editable state and keeps only the active draft in session storage.
2. API operations for options, validation, and packaging are stateless and never retain content.
3. Validation and ZIP generation use one canonical renderer so preview and output cannot drift.
4. Repository snapshot, known-domains list, contribution policy, and configured limits drive rules.
5. Evaluations use current Vally `stimuli`, `graders`, `rubric`, and `expect_activation` structures.
6. Packaging revalidates and permits entries only under fixed repository roots.
7. Telemetry records counts, durations, finding codes, and status—never draft content.

## Complexity Tracking

No constitution violations require justification.