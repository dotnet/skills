# Tasks: Skill Package Upload and Validation

## Phase 1: Setup and Contracts

- [X] T001 Replace authoring requirements with upload-only scope in `specs/002-skill-submission-ui/spec.md`
- [X] T002 Update upload architecture, data model, research, and quickstart in `specs/002-skill-submission-ui/`
- [X] T003 Add upload inspection client models in `src/SkillCatalog/web/src/api/submissionModels.ts`
- [X] T004 Add multipart inspect and normalize client methods in `src/SkillCatalog/web/src/api/submissionClient.ts`

## Phase 2: Foundational Archive Safety

- [X] T005 Add upload inspection records in `src/SkillCatalog/api/SkillCatalog.Api/Models/UploadModels.cs`
- [X] T006 Implement bounded in-memory ZIP and Markdown parsing in `src/SkillCatalog/api/SkillCatalog.Api/Services/SkillPackageParser.cs`
- [X] T007 Implement normalized deterministic ZIP output in `src/SkillCatalog/api/SkillCatalog.Api/Services/SkillPackageParser.cs`
- [X] T008 Register the upload parser and request limits in `src/SkillCatalog/api/SkillCatalog.Api/Program.cs`
- [X] T009 [P] Add ZIP bomb, traversal, duplicate, encoding, entry-count, root, and size tests in `src/SkillCatalog/api/SkillCatalog.Api.Tests/Submissions/SkillPackageParserTests.cs`

## Phase 3: User Story 1 - Upload and Validate (P1)

- [X] T010 [US1] Add stateless multipart inspect and normalize endpoints in `src/SkillCatalog/api/SkillCatalog.Api/Endpoints/SubmissionEndpoints.cs`
- [X] T011 [US1] Implement the accessible ZIP/SKILL.md dropzone in `src/SkillCatalog/web/src/features/skill-submission/components/PackageDropzone.tsx`
- [X] T012 [US1] Replace all authoring controls with upload-only state in `src/SkillCatalog/web/src/features/skill-submission/SkillSubmissionPage.tsx`
- [X] T013 [US1] Add responsive upload and inspection styling in `src/SkillCatalog/web/src/styles/submission.css`
- [X] T014 [P] [US1] Add multipart endpoint contract tests in `src/SkillCatalog/api/SkillCatalog.Api.ContractTests/SubmissionEndpointsTests.cs`
- [X] T015 [P] [US1] Add drop, select, replace, error, and loading component tests in `src/SkillCatalog/web/src/features/skill-submission/SkillSubmissionPage.test.tsx`
- [X] T016 [US1] Add valid ZIP and SKILL.md browser journeys in `src/SkillCatalog/web/e2e/skill-submission.spec.ts`

## Phase 4: User Story 2 - Inspect Repository Fit (P2)

- [X] T017 [US2] Parse frontmatter and referenced resources using repository-compatible rules in `src/SkillCatalog/api/SkillCatalog.Api/Services/SkillPackageParser.cs`
- [X] T018 [US2] Parse and validate current evaluation YAML, ownership coverage, and safety policies in `src/SkillCatalog/api/SkillCatalog.Api/Services/UploadedSkillValidator.cs`
- [X] T019 [P] [US2] Add representative skill, evaluation, ownership, and update-disposition tests in `src/SkillCatalog/api/SkillCatalog.Api.Tests/Submissions/UploadedSkillValidatorTests.cs`
- [X] T020 [US2] Add evaluation and ownership summaries to `src/SkillCatalog/web/src/features/skill-submission/components/SubmissionPreview.tsx`
- [X] T021 [US2] Add field/file navigation and grouped findings in `src/SkillCatalog/web/src/features/skill-submission/components/ValidationSummary.tsx`

## Phase 5: User Story 3 - Normalize and Download (P3)

- [X] T022 [US3] Gate normalized download on current upload validity in `src/SkillCatalog/web/src/features/skill-submission/SkillSubmissionPage.tsx`
- [X] T023 [US3] Revalidate bytes during normalized download in `src/SkillCatalog/api/SkillCatalog.Api/Endpoints/SubmissionEndpoints.cs`
- [X] T024 [P] [US3] Add preview/ZIP parity, stable ordering, timestamps, and stale-upload tests in `src/SkillCatalog/api/SkillCatalog.Api.Tests/Submissions/NormalizedPackageTests.cs`
- [X] T025 [P] [US3] Add keyboard, screen-reader, responsive, and reduced-motion journeys in `src/SkillCatalog/web/e2e/skill-submission-accessibility.spec.ts`
- [X] T026 [P] [US3] Add non-execution, secret, link, command, non-persistence, and telemetry-redaction journeys in `src/SkillCatalog/web/e2e/skill-submission-security.spec.ts`

## Phase 6: Polish

- [X] T027 Update multipart OpenAPI schemas and examples in `specs/002-skill-submission-ui/contracts/skill-submission.openapi.yaml`
- [X] T028 Update upload-only contributor documentation in `src/SkillCatalog/README.md`
- [X] T029 Run API, contract, component, build, Playwright, accessibility, performance, and vulnerability checks from `specs/002-skill-submission-ui/quickstart.md`
- [X] T030 Review spec, contract, data model, implementation, and task status for drift in `specs/002-skill-submission-ui/`

## Dependencies

Setup -> Archive safety -> US1 upload -> US2 deep inspection -> US3 normalization -> Polish

## MVP

Tasks T001-T016 provide the upload-only MVP. Tests precede further validation depth. The workspace never edits or executes uploaded content.
