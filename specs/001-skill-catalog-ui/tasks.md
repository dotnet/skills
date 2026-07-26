# Tasks: Skill Catalog UI

**Input**: Design documents from `specs/001-skill-catalog-ui/`

**Prerequisites**: `plan.md`, `spec.md`, `research.md`, `data-model.md`, `contracts/`

**Tests**: Required by the constitution and specification. Write behavioral tests before implementation and confirm they fail for the expected reason.

## Format

Every item follows `- [ ] T### [P?] [US?] Description with exact file path`.

## Phase 1: Setup

**Purpose**: Establish isolated API and web workspaces without changing existing plugin tooling.

- [ ] T001 Create `src/SkillCatalog/global.json` pinning the .NET 10 LTS SDK independently of the repository preview SDK
- [ ] T002 [P] Scaffold the ASP.NET Core project in `src/SkillCatalog/api/SkillCatalog.Api/SkillCatalog.Api.csproj` with nullable analysis and first-party OpenAPI
- [ ] T003 [P] Scaffold xUnit Microsoft.Testing.Platform projects in `src/SkillCatalog/api/SkillCatalog.Api.Tests/SkillCatalog.Api.Tests.csproj` and `src/SkillCatalog/api/SkillCatalog.Api.ContractTests/SkillCatalog.Api.ContractTests.csproj`
- [ ] T004 [P] Scaffold the React TypeScript Vite workspace and pinned dependencies in `src/SkillCatalog/web/package.json`
- [ ] T005 [P] Configure TypeScript, Vite, ESLint, Vitest, and test setup in `src/SkillCatalog/web/tsconfig.json`, `src/SkillCatalog/web/vite.config.ts`, `src/SkillCatalog/web/eslint.config.js`, and `src/SkillCatalog/web/src/test/setup.ts`
- [ ] T006 [P] Configure Playwright projects and local API/web startup in `src/SkillCatalog/web/playwright.config.ts`
- [ ] T007 Assemble the API and test projects in `src/SkillCatalog/SkillCatalog.slnx` after T002 and T003 create them

## Phase 2: Foundational

**Purpose**: Build the secure repository, snapshot, contract, and application foundations required by every story.

- [ ] T008 Add catalog configuration and startup validation in `src/SkillCatalog/api/SkillCatalog.Api/Options/SkillCatalogOptions.cs`
- [ ] T009 [P] Define catalog, plugin, skill, resource, diagnostic, paging, and problem response records in `src/SkillCatalog/api/SkillCatalog.Api/Models/`
- [ ] T010 [P] Add repository path containment, regular-file, reparse-point, and size-limit tests in `src/SkillCatalog/api/SkillCatalog.Api.Tests/Services/SafeRepositoryPathTests.cs`
- [ ] T011 Implement canonical path resolution and containment in `src/SkillCatalog/api/SkillCatalog.Api/Services/SafeRepositoryPath.cs`
- [ ] T012 [P] Add malformed frontmatter, duplicate name, missing SKILL.md, and unsafe resource fixtures under `src/SkillCatalog/api/SkillCatalog.Api.Tests/Fixtures/Repository/`
- [ ] T013 [P] Add snapshot builder tests for valid, warning, invalid, duplicate, and empty catalogs in `src/SkillCatalog/api/SkillCatalog.Api.Tests/Services/CatalogSnapshotBuilderTests.cs`
- [ ] T014 Implement defensive plugin manifest and SKILL.md frontmatter parsing in `src/SkillCatalog/api/SkillCatalog.Api/Services/RepositorySkillReader.cs`
- [ ] T015 Implement atomic immutable snapshot construction and diagnostics in `src/SkillCatalog/api/SkillCatalog.Api/Services/CatalogSnapshotBuilder.cs`
- [ ] T016 Implement catalog snapshot lifetime, startup loading, refresh timestamp, and unavailable state in `src/SkillCatalog/api/SkillCatalog.Api/Services/CatalogSnapshotProvider.cs`
- [ ] T017 Add shared RFC 9457 problem responses, correlation IDs, logging, compression, caching, and CORS policy in `src/SkillCatalog/api/SkillCatalog.Api/Program.cs`
- [ ] T018 Add generated OpenAPI comparison against `specs/001-skill-catalog-ui/contracts/skill-catalog.openapi.yaml` in `src/SkillCatalog/api/SkillCatalog.Api.ContractTests/OpenApiContractTests.cs`
- [ ] T019 [P] Create the typed API client and response types in `src/SkillCatalog/web/src/api/skillCatalogClient.ts` and `src/SkillCatalog/web/src/api/models.ts`
- [ ] T020 [P] Configure FluentProvider, light/dark tokens, router, error boundary, and application shell in `src/SkillCatalog/web/src/app/App.tsx` and `src/SkillCatalog/web/src/app/theme.ts`

**Checkpoint**: A ready or unavailable catalog snapshot can be exposed safely, and API/UI contracts compile.

## Phase 3: User Story 1 - Browse the Skill Catalog (Priority: P1) MVP

**Goal**: Visitors can browse, search, filter, reset, and understand catalog state.

**Independent Test**: With a 500-skill fixture, find a named skill using search and plugin filters, observe result counts and empty/reset states, and verify p95 search response under 250ms.

- [ ] T021 [P] [US1] Add `/api/catalog` and `/api/skills` contract and integration tests for facets, paging, combined filters, empty state, invalid parameters, and 500-skill performance in `src/SkillCatalog/api/SkillCatalog.Api.ContractTests/CatalogEndpointsTests.cs`
- [ ] T022 [P] [US1] Add catalog search ranking, case, punctuation, partial query, filter intersection, and pagination tests in `src/SkillCatalog/api/SkillCatalog.Api.Tests/Services/SkillSearchServiceTests.cs`
- [ ] T023 [US1] Implement normalized search, filtering, stable sorting, facets, and paging in `src/SkillCatalog/api/SkillCatalog.Api/Services/SkillSearchService.cs`
- [ ] T024 [US1] Map `/api/catalog` and `/api/skills` with typed results and OpenAPI metadata in `src/SkillCatalog/api/SkillCatalog.Api/Endpoints/CatalogEndpoints.cs`
- [ ] T025 [P] [US1] Add component tests for loading, results, filters, counts, empty state, errors, and reset behavior in `src/SkillCatalog/web/src/features/catalog/CatalogPage.test.tsx`
- [ ] T026 [P] [US1] Build reusable search, filter, skill card, result count, snapshot freshness, skeleton, empty, and unavailable components in `src/SkillCatalog/web/src/features/catalog/components/`
- [ ] T027 [US1] Implement URL-synchronized catalog query state and data loading in `src/SkillCatalog/web/src/features/catalog/useCatalogQuery.ts`
- [ ] T028 [US1] Implement the responsive Fluent catalog page, including visible snapshot refresh time, in `src/SkillCatalog/web/src/features/catalog/CatalogPage.tsx`
- [ ] T029 [US1] Add Playwright browsing and search journey coverage in `src/SkillCatalog/web/e2e/catalog.spec.ts`

**Checkpoint**: User Story 1 is a deployable catalog MVP independent of detail and download features.

## Phase 4: User Story 2 - Inspect a Skill (Priority: P2)

**Goal**: Visitors can open a stable detail URL and safely inspect instructions, metadata, diagnostics, and resources.

**Independent Test**: Open a valid and malformed skill from direct URLs, verify readable sanitized content, source and compatibility information, and resource classifications without executing embedded content.

- [ ] T030 [P] [US2] Add skill detail and resource preview contract tests for valid, malformed, missing, oversized, binary, and unsafe resources in `src/SkillCatalog/api/SkillCatalog.Api.ContractTests/SkillDetailEndpointsTests.cs`
- [ ] T031 [US2] Implement allowlisted bounded text preview and media classification in `src/SkillCatalog/api/SkillCatalog.Api/Services/SkillResourceService.cs`
- [ ] T032 [US2] Map detail and resource preview endpoints with typed results in `src/SkillCatalog/api/SkillCatalog.Api/Endpoints/SkillDetailEndpoints.cs`
- [ ] T033 [P] [US2] Add tests for sanitized Markdown, metadata, diagnostics, resources, missing skills, and error recovery in `src/SkillCatalog/web/src/features/skill-detail/SkillDetailPage.test.tsx`
- [ ] T034 [P] [US2] Implement sanitized Markdown and resource list components in `src/SkillCatalog/web/src/features/skill-detail/components/SkillMarkdown.tsx` and `src/SkillCatalog/web/src/features/skill-detail/components/ResourceList.tsx`
- [ ] T035 [US2] Implement the stable routed detail page and source navigation in `src/SkillCatalog/web/src/features/skill-detail/SkillDetailPage.tsx`
- [ ] T036 [US2] Add direct-link, malformed-skill, and malicious-markup Playwright coverage in `src/SkillCatalog/web/e2e/skill-detail.spec.ts`

**Checkpoint**: User Story 2 is independently demonstrable from a direct skill URL.

## Phase 5: User Story 3 - Download a Skill (Priority: P3)

**Goal**: Visitors can download a complete, isolated skill package with preserved relative paths.

**Independent Test**: Download a nested skill, inspect the archive manifest and entries, and verify no sibling, linked, traversed, or unrelated files are present.

- [ ] T037 [P] [US3] Add archive service tests for nested files, manifest metadata, repeated requests, links, traversal, invalid names, large files, and missing skills in `src/SkillCatalog/api/SkillCatalog.Api.Tests/Services/SkillPackageServiceTests.cs`
- [ ] T038 [P] [US3] Add download endpoint contract tests for headers, streaming, not found, and unsafe-package conflict responses in `src/SkillCatalog/api/SkillCatalog.Api.ContractTests/SkillDownloadEndpointTests.cs`
- [ ] T039 [US3] Implement confined streaming ZIP creation and generated source manifest in `src/SkillCatalog/api/SkillCatalog.Api/Services/SkillPackageService.cs`
- [ ] T040 [US3] Map the skill download endpoint with safe content-disposition handling in `src/SkillCatalog/api/SkillCatalog.Api/Endpoints/SkillDownloadEndpoints.cs`
- [ ] T041 [P] [US3] Add download action state and error tests in `src/SkillCatalog/web/src/features/skill-detail/components/DownloadSkillButton.test.tsx`
- [ ] T042 [US3] Implement the accessible download action in `src/SkillCatalog/web/src/features/skill-detail/components/DownloadSkillButton.tsx`
- [ ] T043 [US3] Add Playwright download archive-name and failure-state coverage in `src/SkillCatalog/web/e2e/download.spec.ts`

**Checkpoint**: User Story 3 safely distributes one skill without cloning the repository.

## Phase 6: User Story 4 - Accessible Responsive Experience (Priority: P4)

**Goal**: Core journeys work with keyboard, assistive technology, and viewports from 320px upward.

**Independent Test**: Complete catalog, detail, and download journeys using only the keyboard at desktop and mobile sizes with no serious axe violations or horizontal page scrolling.

- [ ] T044 [P] [US4] Add automated axe checks for catalog, detail, empty, error, and download states in `src/SkillCatalog/web/e2e/accessibility.spec.ts`
- [ ] T045 [P] [US4] Add keyboard focus order, announcements, filter labels, and reduced-motion component tests in `src/SkillCatalog/web/src/app/accessibility.test.tsx`
- [ ] T046 [US4] Implement skip navigation, route focus management, live result announcements, and accessible page landmarks in `src/SkillCatalog/web/src/app/Accessibility.tsx`
- [ ] T047 [US4] Implement responsive grid, filter drawer, typography, overflow, forced-colors, and reduced-motion styles in `src/SkillCatalog/web/src/styles/responsive.css`
- [ ] T048 [US4] Add Playwright keyboard and 320px/768px/1440px responsive journey coverage in `src/SkillCatalog/web/e2e/responsive.spec.ts`

**Checkpoint**: User Story 4 satisfies the accessibility and responsive acceptance gate.

## Phase 7: Polish and Cross-Cutting Verification

- [ ] T049 [P] Add structured snapshot build, request, download, and failure telemetry without repository absolute paths in `src/SkillCatalog/api/SkillCatalog.Api/Services/CatalogTelemetry.cs`
- [ ] T050 [P] Add API security headers, content type, traversal, malformed encoding, and archive bomb regression tests in `src/SkillCatalog/api/SkillCatalog.Api.ContractTests/SecurityTests.cs`
- [ ] T051 [P] Add web bundle budgets and production build verification in `src/SkillCatalog/web/package.json` and `src/SkillCatalog/web/vite.config.ts`
- [ ] T052 Add 500-skill search and detail performance fixtures and repeatable API benchmarks in `src/SkillCatalog/api/SkillCatalog.Api.Tests/Performance/CatalogPerformanceTests.cs`
- [ ] T053 Add browser timing assertions for updated search results within one second and usable detail views within two seconds in `src/SkillCatalog/web/e2e/performance.spec.ts`
- [ ] T054 Document configuration, security boundaries, deployment, and troubleshooting in `src/SkillCatalog/README.md`
- [ ] T055 Run every command and manual check in `specs/001-skill-catalog-ui/quickstart.md` and record corrections in that file

## Dependencies and Execution Order

- Phase 1 -> Phase 2 -> user story phases -> Phase 7.
- US1 is the MVP and has no dependency on another user story after Phase 2.
- US2 can begin after Phase 2; its catalog navigation integration follows US1, but direct URLs remain independently testable.
- US3 can begin after Phase 2 and uses the detail view only for its UI entry point.
- US4 can begin after shared shell components exist and must pass before release.
- Tasks marked `[P]` touch different files and can run concurrently after their phase prerequisites.

## Parallel Examples

- **Setup**: T002-T006 can run concurrently after T001 establishes the SDK boundary; T007 follows T002-T003.
- **Foundation**: T009, T010, T012, T013, T019, and T020 can run concurrently.
- **US1**: T021, T022, T025, and T026 can begin together; T023-T024 and T027-T029 follow their tests.
- **US2**: T030, T033, and T034 can begin together.
- **US3**: T037, T038, and T041 can begin together.
- **US4**: T044 and T045 can begin together before implementation corrections.

## Implementation Strategy

### MVP First

1. Complete Setup and Foundation.
2. Complete T021-T029 for US1.
3. Run API, component, Playwright, performance, and accessibility checks relevant to US1.
4. Demonstrate the searchable public catalog before adding detail and download flows.

### Incremental Delivery

1. US1: searchable catalog.
2. US2: safe, shareable skill details.
3. US3: confined individual downloads.
4. US4: release-level accessibility and responsiveness.
5. Phase 7: full cross-cutting release verification.
