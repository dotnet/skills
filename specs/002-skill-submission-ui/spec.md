# Feature Specification: Guided Skill Submission Workspace

**Feature Branch**: `002-skill-submission-ui`

**Created**: 2026-07-26

**Status**: Draft

**Input**: User description: "Create a guided skill-submission workspace in the catalog UI that lets contributors choose a plugin, author skill metadata and instructions, generate current Vally evaluation scenarios, validate repository rules and safety, preview the catalog result, and download a ready-to-commit package. Direct GitHub pull-request creation is deferred to a later phase."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Author a Valid Skill Package (Priority: P1)

A contributor uses a guided workspace to select an existing collection, define a skill's identity and purpose, write actionable instructions, and produce a repository-ready package without manually learning the repository layout.

**Why this priority**: Producing a structurally correct skill package is the minimum useful outcome and removes the largest barrier for new contributors.

**Independent Test**: A contributor can complete the required fields for a sample skill and download a package containing the expected skill and ownership paths.

**Acceptance Scenarios**:

1. **Given** the repository exposes available collections, **When** a contributor starts a submission, **Then** the workspace presents those collections and explains the experimental collection option.
2. **Given** valid required inputs, **When** the contributor generates the package, **Then** it contains a skill definition at the correct collection path and an ownership update template.
3. **Given** an invalid or duplicate skill name, **When** the contributor attempts to continue, **Then** the workspace identifies the problem and prevents package generation.

---

### User Story 2 - Create Meaningful Evaluations (Priority: P2)

A contributor defines realistic positive and out-of-scope scenarios that demonstrate whether the skill improves agent outcomes, and the workspace generates evaluation content in the repository's current supported format.

**Why this priority**: Repository acceptance requires evidence that a skill adds value rather than merely restating general model knowledge.

**Independent Test**: A contributor can add positive and non-activation scenarios and receive a package containing a valid evaluation definition at the matching test path.

**Acceptance Scenarios**:

1. **Given** a skill with stated use and exclusion boundaries, **When** the contributor creates evaluation scenarios, **Then** each scenario captures a natural user request, observable checks, and outcome-focused judgment criteria.
2. **Given** a scenario that mentions the skill name or judges a prescribed technique, **When** validation runs, **Then** the workspace warns about evaluation bias and suggests an outcome-focused correction.
3. **Given** at least one valid positive scenario, **When** the package is generated, **Then** the matching evaluation file is included under the correct collection and skill test path.

---

### User Story 3 - Validate and Preview Before Download (Priority: P3)

A contributor validates the complete draft, reviews actionable errors and warnings, and previews how the skill will appear in the catalog before downloading it.

**Why this priority**: Early feedback reduces failed pull requests and lets contributors assess clarity and presentation before review.

**Independent Test**: A deliberately flawed draft produces specific validation feedback; correcting it produces a clean preview and enables download.

**Acceptance Scenarios**:

1. **Given** a draft with missing metadata, unsafe references, unresolved file references, or incomplete ownership information, **When** validation runs, **Then** each issue is associated with the relevant field and severity.
2. **Given** a valid draft, **When** the contributor opens preview, **Then** the displayed title, collection, description, instructions, and resources match the generated package.
3. **Given** validation errors remain, **When** the contributor requests a download, **Then** download is blocked and the errors are summarized.
4. **Given** only non-blocking warnings remain, **When** the contributor requests a download, **Then** the package is produced and the warnings remain visible.

### Edge Cases

- The selected collection is removed or renamed while a draft is in progress.
- A skill name differs from its directory name, exceeds repository limits, or conflicts case-insensitively with an existing skill.
- Instructions exceed the recommended size or reference missing, absolute, or escaping paths.
- Evaluation prompts leak the skill name, lack graders or criteria, duplicate one another, or contain only out-of-scope scenarios.
- Uploaded or generated resources are empty, too large, unsupported, unsafe, or have duplicate paths.
- A contributor refreshes or closes the page when the current revision has not been successfully packaged.
- Validation cannot complete because repository metadata is temporarily unavailable.
- The generated package would require a new collection rather than an existing one.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The workspace MUST be reachable from the catalog through a clearly labeled contribution action.
- **FR-002**: Contributors MUST be able to choose an existing collection, including the experimental collection when appropriate.
- **FR-003**: The workspace MUST explain that creation of a new collection is outside the first release and requires the repository's separate contribution process.
- **FR-004**: Contributors MUST be able to define the skill name, description, purpose, activation guidance, exclusion guidance, required inputs, workflow, validation criteria, and common pitfalls.
- **FR-005**: The workspace MUST validate skill identity rules, required metadata, description length, instruction completeness, content size guidance, duplicate identities, and relative resource references.
- **FR-006**: Contributors MUST be able to add optional resources grouped as scripts, references, or assets using repository-relative paths.
- **FR-007**: The workspace MUST reject paths that are rooted, escape an allowed package root, contain traversal or control characters, or duplicate another normalized path; resources outside the published type and size limits; private-key blocks and high-confidence credential/token assignments; insecure HTTP references; external script references without integrity metadata; pipe-to-shell commands; and URLs whose domains are absent from the repository allowlist. Findings MUST use stable rule identifiers so contributors can understand which policy blocked the draft.
- **FR-008**: Contributors MUST be able to identify the proposed owners required for the skill and its evaluation directory.
- **FR-009**: The workspace MUST generate an ownership update template for both the skill and evaluation paths without claiming that the owners have approved the submission.
- **FR-010**: Contributors MUST be able to create positive activation scenarios and out-of-scope non-activation scenarios.
- **FR-011**: Each evaluation scenario MUST support a natural user prompt, deterministic checks where applicable, and independently evaluable outcome criteria.
- **FR-012**: The workspace MUST issue non-blocking overfitting warnings when an evaluation prompt contains the draft skill name or when a rubric criterion requires a command, flag, tool, or distinctive phrase copied from the draft instead of describing an observable outcome. The warning MUST identify the affected scenario and criterion; it MUST NOT block packaging by itself.
- **FR-013**: Generated evaluations MUST use the repository's currently supported evaluation format.
- **FR-014**: The workspace MUST validate the entire draft and classify findings as blocking errors or non-blocking warnings.
- **FR-015**: Each validation finding MUST identify the affected section and provide a corrective explanation.
- **FR-016**: Contributors MUST be able to preview the catalog summary and rendered instructions before download.
- **FR-017**: The preview MUST use the same interpretation rules as the public catalog so that generated and published appearances are materially consistent.
- **FR-018**: The workspace MUST generate a downloadable package using the repository's expected collection, skill, test, and ownership paths.
- **FR-019**: Package generation MUST be blocked while validation errors remain.
- **FR-020**: The generated package MUST include a contribution summary describing the skill's motivation, scope, validation performed, and remaining reviewer actions.
- **FR-021**: Draft data MUST remain local to the contributor's session in the first release and MUST NOT be submitted to GitHub or another external service.
- **FR-022**: Contributors MUST be warned before leaving when the current draft revision has not been included in a successfully downloaded package. Automatic session recovery does not count as completing or packaging the draft.
- **FR-023**: The first release MUST NOT create branches, commits, issues, or pull requests.
- **FR-024**: The workspace MUST provide the repository contribution steps that follow package download.
- **FR-025**: Existing catalog browsing and downloading behavior MUST continue to work unchanged.

### Key Entities

- **Skill Draft**: The contributor's in-progress skill identity, purpose, activation boundaries, workflow, validation guidance, and ownership proposal.
- **Skill Resource**: An optional script, reference, or asset with a repository-relative path, content, type, and size.
- **Evaluation Scenario**: A natural task prompt plus activation expectation, deterministic checks, outcome criteria, fixture requirements, and time budget.
- **Validation Finding**: A severity, affected section, explanation, and corrective guidance produced for a draft.
- **Submission Package**: The generated repository-shaped collection of the skill definition, resources, evaluation definition, ownership template, and contribution summary.
- **Catalog Preview**: A read-only representation of how the draft summary and instructions will appear after publication.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A first-time contributor can generate a valid basic skill package in 10 minutes or less without consulting repository layout documentation.
- **SC-002**: 100% of generated packages with no blocking findings contain the required skill, evaluation, ownership-template, and contribution-summary paths.
- **SC-003**: 100% of invalid names, missing required metadata, unsafe paths, and missing positive evaluation scenarios are blocked before download.
- **SC-004**: At least 90% of repository-rule violations represented in the acceptance test set produce a finding that identifies both the problem and a corrective action.
- **SC-005**: At least 90% of representative contributors can complete the primary author-validate-preview-download journey on their first attempt.
- **SC-006**: Validation feedback appears within 2 seconds for drafts containing up to 20 resources and 20 evaluation scenarios.
- **SC-007**: The catalog preview and the published catalog rendering match for all metadata, headings, tables, lists, code blocks, and resource names in the acceptance test set.
- **SC-008**: Existing catalog browse, search, filter, detail, and download journeys continue to pass their acceptance checks.

## Assumptions

- The first release targets contributors preparing a pull request to this repository and assumes they can use Git after downloading the package.
- Only existing collections can be selected; creating and registering a new collection remains a manual repository-maintainer workflow.
- The experimental collection is available for proposals without a proven stable home but follows the same quality requirements.
- The repository's current contribution policy and current evaluation format are the authoritative rules when they differ from older bundled guidance.
- At least two individual owners or one team owner must be proposed, but actual identity and approval remain reviewer responsibilities.
- Drafts are ephemeral and retained only for the active local browser session; account-based storage and cross-device recovery are out of scope.
- GitHub authentication, issue creation, branch creation, commits, and pull-request submission are deferred to a later feature.
- Running full model-based evaluations is outside the first release; the workspace prepares and statically validates evaluation definitions and explains how contributors run or trigger evaluations.
