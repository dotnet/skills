# Tasks: Guided Skill Submission Workspace

**Input**: Design documents from `/specs/002-skill-submission-ui/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md

**Tests**: Tests are required by the project constitution and are listed before the corresponding implementation tasks.

**Organization**: Tasks are grouped by user story so each increment remains independently testable.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel because it changes different files without depending on incomplete tasks
- **[Story]**: Maps the task to a specification user story
- Every task includes an exact file path

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Establish submission feature folders, configuration, and shared client contracts.

- [ ] T001 Create submission feature directories and placeholder exports under `src/SkillCatalog/web/src/features/skill-submission/` and API submission test directories under `src/SkillCatalog/api/SkillCatalog.Api.Tests/Submissions/`
- [ ] T002 [P] Add bounded submission limits and validation-on-start configuration in `src/SkillCatalog/api/SkillCatalog.Api/Options/SkillSubmissionOptions.cs` and `src/SkillCatalog/api/SkillCatalog.Api/appsettings.json`
- [ ] T003 [P] Add client request, response, finding, preview, and options types in `src/SkillCatalog/web/src/api/submissionModels.ts`
- [ ] T004 [P] Add submission page styling primitives and responsive breakpoints in `src/SkillCatalog/web/src/styles/submission.css`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Create the shared models, authoritative rules, canonical rendering, and client draft state required by all stories.

**⚠️ CRITICAL**: No user story implementation begins until this phase passes.

- [ ] T005 [P] Add API records for drafts, resources, scenarios, graders, findings, previews, canonical submissions, and options in `src/SkillCatalog/api/SkillCatalog.Api/Models/SubmissionModels.cs`
- [ ] T006 [P] Add unit tests for repository plugin discovery, duplicate identity lookup, known-domain parsing, and configured limits in `src/SkillCatalog/api/SkillCatalog.Api.Tests/Submissions/SubmissionRuleProviderTests.cs`
- [ ] T007 Implement repository-backed rules using the catalog snapshot and `eng/known-domains.txt` in `src/SkillCatalog/api/SkillCatalog.Api/Services/SubmissionRuleProvider.cs`
- [ ] T008 [P] Add canonical SKILL.md, CODEOWNERS additions, contribution summary, and catalog preview rendering tests in `src/SkillCatalog/api/SkillCatalog.Api.Tests/Submissions/SkillSubmissionRendererTests.cs`
- [ ] T009 Implement deterministic canonical document generation without content execution in `src/SkillCatalog/api/SkillCatalog.Api/Services/SkillSubmissionRenderer.cs`
- [ ] T010 [P] Add creation, migration, session-storage, revision, and stale-validation tests in `src/SkillCatalog/web/src/features/skill-submission/submissionDraft.test.ts`
- [ ] T011 Implement versioned draft defaults, immutable updates, session recovery, and revision tracking in `src/SkillCatalog/web/src/features/skill-submission/submissionDraft.ts`
- [ ] T012 Add same-origin options, validate, and package client methods with problem-response handling in `src/SkillCatalog/web/src/api/submissionClient.ts`
- [ ] T013 Register submission options, services, request limits, and endpoint mapping in `src/SkillCatalog/api/SkillCatalog.Api/Program.cs`

**Checkpoint**: Shared contracts, rules, renderer, and draft state are ready; user stories can proceed.

---

## Phase 3: User Story 1 - Author a Valid Skill Package (Priority: P1) 🎯 MVP

**Goal**: A contributor selects an existing plugin, authors a structurally valid skill with resources and owners, and downloads a repository-shaped package.

**Independent Test**: Complete the required authoring fields for a unique sample skill, validate it, download a ZIP, and confirm the skill, resource, ownership, and contribution paths.

### Tests for User Story 1

- [ ] T014 [P] [US1] Add validator tests for names, duplicate identities, metadata limits, required sections, owners, resource paths, encodings, sizes, secrets, insecure links, pipe-to-shell, and unknown domains in `src/SkillCatalog/api/SkillCatalog.Api.Tests/Submissions/SkillDraftValidatorAuthoringTests.cs`
- [ ] T015 [P] [US1] Add package tests for fixed archive roots, normalized entries, expected documents, duplicate entries, size limits, and traversal rejection in `src/SkillCatalog/api/SkillCatalog.Api.Tests/Submissions/SkillSubmissionPackageServiceTests.cs`
- [ ] T016 [P] [US1] Add options, validation, package, 400, 413, and 422 contract tests for the P1 payload in `src/SkillCatalog/api/SkillCatalog.Api.ContractTests/SubmissionEndpointsTests.cs`
- [ ] T017 [P] [US1] Add component tests for plugin selection, author fields, owners, resource editing, duplicate-name feedback, navigation, and package enablement in `src/SkillCatalog/web/src/features/skill-submission/SkillSubmissionPage.test.tsx`
- [ ] T018 [P] [US1] Add the desktop/mobile P1 author-to-ZIP journey and archive assertions in `src/SkillCatalog/web/e2e/skill-submission.spec.ts`

### Implementation for User Story 1

- [ ] T019 [US1] Implement authoring and resource validation with stable finding codes and corrective guidance in `src/SkillCatalog/api/SkillCatalog.Api/Services/SkillDraftValidator.cs`
- [ ] T020 [US1] Implement safe, bounded, repository-shaped ZIP generation using canonical rendering in `src/SkillCatalog/api/SkillCatalog.Api/Services/SkillSubmissionPackageService.cs`
- [ ] T021 [US1] Implement stateless options, validate, and package handlers with 400/413/422 behavior in `src/SkillCatalog/api/SkillCatalog.Api/Endpoints/SubmissionEndpoints.cs`
- [ ] T022 [P] [US1] Implement plugin/name/description/purpose/boundary/input/workflow/validation/owner fields in `src/SkillCatalog/web/src/features/skill-submission/components/AuthorStep.tsx`
- [ ] T023 [P] [US1] Implement bounded text and asset resource creation, editing, removal, and local safety feedback in `src/SkillCatalog/web/src/features/skill-submission/components/ResourceEditor.tsx`
- [ ] T024 [US1] Compose the step shell, progress, session recovery, validation state, and package action in `src/SkillCatalog/web/src/features/skill-submission/SkillSubmissionPage.tsx`
- [ ] T025 [US1] Add the `/contribute/skill` route and clearly labeled catalog/header contribution entry points in `src/SkillCatalog/web/src/app/App.tsx` and `src/SkillCatalog/web/src/features/catalog/CatalogPage.tsx`
- [ ] T026 [US1] Verify P1 with API unit/contract tests, component tests, production build, and the focused P1 browser journey documented in `specs/002-skill-submission-ui/quickstart.md`

**Checkpoint**: The MVP produces a safe repository-shaped basic skill package without GitHub writes.

---

## Phase 4: User Story 2 - Create Meaningful Evaluations (Priority: P2)

**Goal**: A contributor authors positive and non-activation scenarios and receives current Vally `eval.yaml` with bias-resistant outcome criteria.

**Independent Test**: Add one positive and one out-of-scope scenario, validate them, and confirm the generated eval contains current stimuli, graders, rubric, and activation expectation.

### Tests for User Story 2

- [ ] T027 [P] [US2] Add tests for positive-scenario requirements, supported graders, conditional grader fields, fixtures, timeout limits, duplicate scenarios, skill-name leakage, and technique/vocabulary bias warnings in `src/SkillCatalog/api/SkillCatalog.Api.Tests/Submissions/SkillDraftValidatorEvaluationTests.cs`
- [ ] T028 [P] [US2] Add exact current-format Vally YAML generation and escaping tests in `src/SkillCatalog/api/SkillCatalog.Api.Tests/Submissions/SkillSubmissionEvaluationRendererTests.cs`
- [ ] T029 [P] [US2] Add component tests for scenario creation, activation toggle, grader/rubric editing, fixtures, removal, and accessible warnings in `src/SkillCatalog/web/src/features/skill-submission/components/EvaluationStep.test.tsx`
- [ ] T030 [P] [US2] Add positive/non-activation evaluation authoring and generated-YAML assertions in `src/SkillCatalog/web/e2e/skill-submission-evaluations.spec.ts`

### Implementation for User Story 2

- [ ] T031 [US2] Extend evaluation validation and stable overfitting findings in `src/SkillCatalog/api/SkillCatalog.Api/Services/SkillDraftValidator.cs`
- [ ] T032 [US2] Generate current repository Vally metadata, stimuli, graders, rubrics, fixtures, and `expect_activation` in `src/SkillCatalog/api/SkillCatalog.Api/Services/SkillSubmissionRenderer.cs`
- [ ] T033 [US2] Implement evaluation scenario, grader, rubric, timeout, fixture, and activation-boundary editing in `src/SkillCatalog/web/src/features/skill-submission/components/EvaluationStep.tsx`
- [ ] T034 [US2] Integrate the evaluation step and stale-validation behavior into `src/SkillCatalog/web/src/features/skill-submission/SkillSubmissionPage.tsx`
- [ ] T035 [US2] Verify the P2 journey and compare generated YAML with the contract and representative repository evals using `specs/002-skill-submission-ui/quickstart.md`

**Checkpoint**: Contributors can independently create reviewable current-format evaluations.

---

## Phase 5: User Story 3 - Validate and Preview Before Download (Priority: P3)

**Goal**: A contributor receives actionable errors/warnings, previews canonical catalog output, and downloads only after current validation succeeds.

**Independent Test**: Validate a deliberately flawed draft, fix its field-linked findings, preview matching catalog output, change the draft to invalidate the result, revalidate, and download.

### Tests for User Story 3

- [ ] T036 [P] [US3] Add validation ordering, field-path, severity, guidance, canonical preview, stale revision, and 20-resource/20-scenario performance tests in `src/SkillCatalog/api/SkillCatalog.Api.Tests/Submissions/SkillSubmissionValidationTests.cs`
- [ ] T037 [P] [US3] Add component tests for error summary focus, field links, warning behavior, stale results, preview parity, blocked download, unsaved-leave warning, and recovery in `src/SkillCatalog/web/src/features/skill-submission/SubmissionReview.test.tsx`
- [ ] T038 [P] [US3] Add keyboard, screen-reader semantics, contrast, responsive, reduced-motion, and no-horizontal-overflow checks in `src/SkillCatalog/web/e2e/skill-submission-accessibility.spec.ts`
- [ ] T039 [P] [US3] Add unsafe input, no-content-execution, request limit, path traversal, telemetry redaction, and server non-persistence journeys in `src/SkillCatalog/web/e2e/skill-submission-security.spec.ts`

### Implementation for User Story 3

- [ ] T040 [P] [US3] Implement grouped, focusable error/warning summaries with field navigation in `src/SkillCatalog/web/src/features/skill-submission/components/ValidationSummary.tsx`
- [ ] T041 [P] [US3] Implement card, Markdown, resource, ownership, eval, and manifest preview using the shared Markdown component in `src/SkillCatalog/web/src/features/skill-submission/components/SubmissionPreview.tsx`
- [ ] T042 [P] [US3] Implement validation status, stale-state explanation, ZIP manifest, download behavior, and contribution next steps in `src/SkillCatalog/web/src/features/skill-submission/components/PackageStep.tsx`
- [ ] T043 [US3] Integrate review/preview/package steps, before-unload protection, focus management, live announcements, and responsive navigation in `src/SkillCatalog/web/src/features/skill-submission/SkillSubmissionPage.tsx`
- [ ] T044 [US3] Add duration/count/finding-code telemetry with explicit draft-content redaction in `src/SkillCatalog/api/SkillCatalog.Api/Services/CatalogTelemetry.cs` and `src/SkillCatalog/api/SkillCatalog.Api/Endpoints/SubmissionEndpoints.cs`
- [ ] T045 [US3] Verify P3 independently with focused accessibility, security, performance, recovery, and preview-parity journeys from `specs/002-skill-submission-ui/quickstart.md`

**Checkpoint**: All three stories are functional, accessible, safe, and independently demonstrable.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Complete repository-wide quality gates and contributor documentation.

- [ ] T046 [P] Add endpoint schemas and representative submission examples to generated OpenAPI verification in `src/SkillCatalog/api/SkillCatalog.Api.ContractTests/OpenApiContractTests.cs`
- [ ] T047 [P] Add contributor-facing workspace usage, privacy boundary, static validation, eval execution, and PR handoff documentation in `src/SkillCatalog/README.md`
- [ ] T048 [P] Add catalog browse/search/filter/detail/download regression coverage after submission routing and styles in `src/SkillCatalog/web/e2e/catalog.spec.ts` and `src/SkillCatalog/web/e2e/skill-detail.spec.ts`
- [ ] T049 Run API unit, performance, contract, security, component, production build, full Playwright, accessibility, and dependency vulnerability checks listed in `specs/002-skill-submission-ui/quickstart.md`
- [ ] T050 Perform a final contract/data-model/implementation consistency review and update any drift in `specs/002-skill-submission-ui/contracts/skill-submission.openapi.yaml`, `specs/002-skill-submission-ui/data-model.md`, and `specs/002-skill-submission-ui/quickstart.md`

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 Setup**: starts immediately.
- **Phase 2 Foundation**: depends on Phase 1 and blocks all stories.
- **US1 (P1)**: starts after Foundation and is the MVP.
- **US2 (P2)**: starts after Foundation; renderer integration tasks T032/T034 merge with shared files after US1 equivalents.
- **US3 (P3)**: starts after Foundation; can build review components in parallel but final integration depends on canonical validation and rendering from US1/US2.
- **Polish**: follows all stories selected for release.

### User Story Completion Order

```text
Setup -> Foundation -> US1 (MVP)
                    ├-> US2
                    └-> US3 components
US1 + US2 -> US3 integration -> Polish
```

### Parallel Opportunities

- T002-T004 can proceed in parallel.
- T005, T006, T008, and T010 can proceed in parallel before their paired implementations.
- US1 test tasks T014-T018 can proceed in parallel.
- US1 author and resource components T022-T023 can proceed in parallel with API work.
- US2 test tasks T027-T030 and US3 test tasks T036-T039 can proceed in parallel.
- US3 components T040-T042 can proceed in parallel.
- Polish documentation, OpenAPI, and regression tasks T046-T048 can proceed in parallel.

## Parallel Examples

### User Story 1

```text
Task T014: Authoring validator tests
Task T015: Package boundary tests
Task T016: Endpoint contract tests
Task T017: Authoring component tests
Task T018: Browser MVP journey
```

### User Story 2

```text
Task T027: Evaluation validator tests
Task T028: Vally rendering tests
Task T029: Evaluation editor component tests
Task T030: Evaluation browser journey
```

### User Story 3

```text
Task T036: Validation/performance tests
Task T037: Review component tests
Task T038: Accessibility journeys
Task T039: Security journeys
```

## Implementation Strategy

### MVP First

1. Complete Setup and Foundation.
2. Complete US1 with tests written first.
3. Stop and validate the author-to-ZIP journey independently.
4. Demo or deploy the MVP before adding evaluation and preview depth.

### Incremental Delivery

1. **US1**: repository-ready basic skill ZIP.
2. **US2**: current-format evaluation authoring.
3. **US3**: authoritative review, preview, and hardened download.
4. **Polish**: complete all quality gates and documentation.

## Notes

- Tests precede implementation and should initially fail for the intended behavior.
- `[P]` means the task can safely change a different file in parallel.
- Server endpoints remain stateless and must never log draft content.
- No task may add authentication, GitHub writes, model execution, a database, or a new plugin workflow.
