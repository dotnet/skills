# Feature Specification: Skill Catalog UI

**Feature Branch**: `001-skill-catalog-ui`

**Created**: 2026-07-26

**Status**: Draft

**Input**: User description: "Create a polished web experience for the skill repository where users can view and download skills. The implementation is expected to use a C# API with a React front end and Fluent design system."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Browse the Skill Catalog (Priority: P1)

As a visitor, I can browse the repository's available skills in a polished catalog so that I can quickly understand what is available without navigating the repository's folder structure.

**Why this priority**: Discovery is the primary value of the experience and is required before users can inspect or download a skill.

**Independent Test**: Open the catalog with a representative set of skills and confirm that a visitor can identify skill names, summaries, plugin groupings, and maturity information without opening repository files.

**Acceptance Scenarios**:

1. **Given** the repository contains valid skills, **When** a visitor opens the catalog, **Then** the visitor sees a structured collection of skills with a name, concise description, plugin or category, and availability status.
2. **Given** the catalog contains many skills, **When** a visitor searches by a term found in a skill's name, description, or category, **Then** matching skills are shown and nonmatching skills are excluded.
3. **Given** the visitor selects one or more filters, **When** the catalog updates, **Then** the visible results and result count reflect all active filters.
4. **Given** no skills match the current search and filters, **When** results are displayed, **Then** the visitor sees a clear empty state and can reset the search and filters.

---

### User Story 2 - Inspect a Skill (Priority: P2)

As a visitor, I can open a skill detail view so that I can decide whether the skill is relevant, trustworthy, and compatible with my intended agent.

**Why this priority**: Visitors need enough context to make an informed download decision.

**Independent Test**: Select a skill from the catalog and confirm that its core instructions, supporting files, source location, compatibility information, and available metadata can be reviewed from one view.

**Acceptance Scenarios**:

1. **Given** a skill is listed in the catalog, **When** a visitor opens it, **Then** the detail view shows its purpose, description, instructions, plugin or category, source location, and supporting resources.
2. **Given** a skill declares compatibility or requirements, **When** its detail view is shown, **Then** those constraints are presented prominently before download.
3. **Given** a skill references local supporting files, **When** a visitor examines its resources, **Then** the visitor can distinguish instructions, references, scripts, and assets.
4. **Given** a skill cannot be fully parsed, **When** a visitor opens it, **Then** available content remains readable and the incomplete metadata is clearly identified.

---

### User Story 3 - Download a Skill (Priority: P3)

As a visitor, I can download an individual skill with its supporting resources so that I can install or inspect it locally without cloning the entire repository.

**Why this priority**: Download turns discovery into practical use while keeping the first release focused on distribution rather than automated installation.

**Independent Test**: Download a skill that contains instructions and supporting resources, extract the resulting package, and confirm that the original relative directory structure and files are preserved.

**Acceptance Scenarios**:

1. **Given** a valid skill detail view, **When** a visitor requests a download, **Then** the visitor receives a package containing the skill instructions and all files within that skill's directory.
2. **Given** a skill contains nested references, scripts, or assets, **When** it is downloaded, **Then** those files retain their relative paths.
3. **Given** a requested skill no longer exists, **When** a visitor requests its download, **Then** no incomplete package is produced and the visitor receives a useful recovery message.
4. **Given** a skill package is available, **When** the download is prepared, **Then** the package name identifies the skill and does not expose unrelated repository content.

---

### User Story 4 - Use the Catalog Across Devices and Abilities (Priority: P4)

As a visitor, I can use the catalog with a keyboard, assistive technology, or a smaller screen so that skill discovery is not limited by device or interaction method.

**Why this priority**: A polished public catalog must be inclusive and usable beyond a desktop pointer-based workflow.

**Independent Test**: Complete browsing, searching, opening details, and starting a download using keyboard navigation at desktop and mobile viewport sizes, with automated accessibility checks enabled.

**Acceptance Scenarios**:

1. **Given** a visitor uses only a keyboard, **When** the visitor browses and opens a skill, **Then** focus order, focus visibility, and control activation remain clear and complete.
2. **Given** a visitor uses a narrow screen, **When** the visitor browses and opens a skill, **Then** content remains readable without horizontal page scrolling.
3. **Given** a visitor uses assistive technology, **When** catalog content or filters change, **Then** meaningful labels and status updates communicate the change.

### Edge Cases

- The repository contains no discoverable skills.
- Two plugins contain skills with the same skill name.
- A skill has missing, malformed, or unusually long metadata.
- A skill directory contains unsupported file types, large assets, empty directories, or symbolic links.
- A referenced resource points outside the skill directory.
- Repository content changes while a visitor is viewing a detail page.
- Search includes punctuation, casing differences, partial words, or no query.
- A download is requested repeatedly or interrupted before completion.
- Repository access is temporarily unavailable.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST discover skills from the repository's supported plugin and skill directory conventions.
- **FR-002**: The system MUST assign each discovered skill a stable identifier that remains unique when different plugins use the same skill name.
- **FR-003**: The catalog MUST present each skill's name, concise description, plugin or category, and available maturity or compatibility metadata.
- **FR-004**: Visitors MUST be able to search skills by name, description, plugin, category, and declared keywords.
- **FR-005**: Visitors MUST be able to filter the catalog by available plugin, category, maturity, and compatibility metadata.
- **FR-006**: Visitors MUST be able to clear all search and filter criteria in one action.
- **FR-007**: The system MUST provide a stable, shareable detail location for each skill.
- **FR-008**: A skill detail view MUST display the skill's primary instructions in a readable, safely rendered form.
- **FR-009**: A skill detail view MUST identify the skill's supporting references, scripts, and assets without executing them.
- **FR-010**: A skill detail view MUST provide a link to the skill's authoritative repository source.
- **FR-011**: Visitors MUST be able to download one skill independently of the rest of the repository.
- **FR-012**: A downloaded skill package MUST include the complete contents of the selected skill directory while preserving relative paths.
- **FR-013**: Download generation MUST exclude files outside the selected skill directory, even when a skill contains links or references to external paths.
- **FR-014**: The system MUST reject invalid, missing, or ambiguous skill identifiers without returning unrelated content.
- **FR-015**: The catalog MUST distinguish incomplete or invalid skill metadata from fully valid skill records while retaining any content that can be safely presented.
- **FR-016**: The experience MUST provide meaningful loading, empty, unavailable, and error states with an actionable recovery option where one exists.
- **FR-017**: Core browse, search, detail, and download journeys MUST be operable with a keyboard.
- **FR-018**: Core content and controls MUST meet WCAG 2.2 AA accessibility expectations.
- **FR-019**: The experience MUST adapt to commonly used desktop, tablet, and mobile viewport sizes.
- **FR-020**: Skill instructions and repository-provided text MUST be treated as untrusted display content and MUST NOT be executed as part of browsing or packaging.
- **FR-021**: The first release MUST allow anonymous read and download access and MUST NOT require user accounts.
- **FR-022**: The first release MUST NOT modify, rate, publish, install, or evaluate skills.
- **FR-023**: The catalog MUST communicate when its displayed repository snapshot was last refreshed.

### Key Entities

- **Skill**: A discoverable instruction package identified by its plugin and skill name; includes metadata, primary instructions, validation state, source location, compatibility declarations, and supporting resources.
- **Plugin**: A repository grouping that owns one or more skills and provides category, description, and distribution context.
- **Skill Resource**: A file contained within a skill directory, classified as primary instructions, reference, script, asset, or other supporting content.
- **Skill Package**: A downloadable snapshot of one skill directory, with a deterministic name and preserved internal paths.
- **Catalog Snapshot**: The repository state used to produce the current catalog, including its source revision and refresh time.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: At least 90% of first-time test participants can find a specified skill and open its details in under 60 seconds.
- **SC-002**: At least 95% of searches over a catalog of 500 skills show the updated result set within one second under normal operating conditions.
- **SC-003**: At least 95% of skill detail views become usable within two seconds under normal broadband conditions.
- **SC-004**: Every valid skill in the configured repository snapshot appears exactly once in the catalog.
- **SC-005**: Every successful skill download contains all files from the selected skill directory and no files from another repository directory.
- **SC-006**: At least 95% of first-time test participants can download and identify the selected skill package without assistance.
- **SC-007**: All primary user journeys pass automated accessibility checks with no critical or serious violations and pass manual keyboard navigation review.
- **SC-008**: Browse, search, detail, and download journeys remain usable at viewport widths from 320 pixels upward without horizontal page scrolling.
- **SC-009**: Repository parsing or availability failures produce a clear user-facing state rather than a broken or misleading catalog in 100% of tested failure scenarios.
- **SC-010**: No displayed skill content is executed and no download contains content outside the selected skill directory in the security test suite.

## Assumptions

- The first release catalogs skills from this repository rather than accepting arbitrary repository URLs.
- The catalog is public and read-only; authentication, personalization, ratings, comments, and administration are outside the first release.
- A skill download is a compressed package of the complete skill directory, not an automated installation into an agent.
- Repository metadata and files are the authoritative source; the catalog does not maintain an independent authoring database.
- Skills follow the repository's existing `plugins/<plugin>/skills/<skill>/` convention, with graceful handling for invalid entries.
- The catalog may refresh from repository changes asynchronously; immediate reflection of every commit is not required.
- Evaluation scores and LLM-generated judgments will be introduced as a separate feature after the catalog foundation exists.
