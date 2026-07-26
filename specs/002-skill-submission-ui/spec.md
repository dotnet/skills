# Feature Specification: Skill Package Upload and Validation

**Feature Branch**: `002-skill-submission-ui`
**Created**: 2026-07-26
**Updated**: 2026-07-26
**Status**: Approved

## User Scenarios & Testing

### User Story 1 - Upload and Validate a Skill Package (Priority: P1)

A contributor uploads an existing skill ZIP in repository format and immediately sees whether it is structurally valid, safe, and eligible for packaging.

**Independent Test**: Upload a valid package containing `plugins/<plugin>/skills/<skill>/SKILL.md` and confirm its identity, files, validation result, and package readiness are displayed.

**Acceptance Scenarios**:

1. **Given** a valid repository-shaped ZIP, **When** it is uploaded, **Then** the workspace identifies the skill, validates every entry, and reports that it is ready.
2. **Given** an invalid or unsafe archive, **When** it is uploaded, **Then** the workspace rejects or flags it with stable, actionable findings and never executes its contents.
3. **Given** a loose `SKILL.md`, **When** it is uploaded, **Then** the workspace validates it as a single-file skill package and reports missing optional or required companion files.

---

### User Story 2 - Inspect Evaluations and Repository Fit (Priority: P2)

A contributor reviews the uploaded skill, resources, ownership coverage, and evaluations without editing them in the workspace.

**Independent Test**: Upload a skill with `eval.yaml`, resources, and ownership metadata; confirm each is parsed, summarized, and validated against repository policy.

**Acceptance Scenarios**:

1. **Given** a package with evaluations, **When** validation completes, **Then** scenarios, activation expectations, graders, rubrics, fixtures, and overfitting warnings are summarized.
2. **Given** missing or malformed evaluations, **When** validation completes, **Then** the workspace explains what is required without offering an authoring form.
3. **Given** a skill identity already present in the repository, **When** it is uploaded, **Then** the workspace identifies it as an update rather than a new skill.

---

### User Story 3 - Preview and Download a Normalized Package (Priority: P3)

A contributor previews canonical repository output and downloads a normalized ZIP only after blocking findings are resolved outside the workspace and a corrected package is re-uploaded.

**Independent Test**: Upload a valid package, compare previewed content and manifest with the downloaded normalized ZIP, and confirm they match.

**Acceptance Scenarios**:

1. **Given** a valid upload, **When** the contributor downloads it, **Then** the ZIP uses canonical repository paths and contains no unrecognized or unsafe entries.
2. **Given** blocking findings, **When** the contributor attempts to download, **Then** download remains disabled and the workspace directs them to fix and re-upload the source package.
3. **Given** a replacement upload, **When** validation succeeds, **Then** all previous results are replaced and cannot enable download for stale content.

### Edge Cases

- ZIP bombs, excessive compression ratios, too many entries, oversized files, duplicate normalized paths, encrypted entries, absolute paths, traversal, control characters, and symbolic-link-like entries.
- Multiple skills, no skill, nested repository roots, unexpected top-level files, malformed frontmatter, unsupported encoding, or conflicting identities.
- Private keys, high-confidence credentials, insecure links, external scripts without integrity, pipe-to-shell commands, and unapproved domains.
- Refresh, navigation, upload cancellation, network failure, and replacing a file while validation is running.

## Requirements

### Functional Requirements

- **FR-001**: The workspace MUST make upload the only submission starting action; it MUST NOT present skill authoring or evaluation editing controls.
- **FR-002**: Contributors MUST be able to upload either one `.zip` package or one `SKILL.md` file.
- **FR-003**: Upload processing MUST be bounded by published archive, entry-count, expanded-size, per-file, and compression-ratio limits.
- **FR-004**: The workspace MUST detect the package root and exactly one skill identity using supported repository paths.
- **FR-005**: The workspace MUST parse and validate skill frontmatter, required sections, resource references, repository identity, ownership coverage, and supported evaluation format.
- **FR-006**: Uploaded content MUST be treated as untrusted, MUST NOT execute, and MUST be rejected for unsafe paths, secrets, insecure references, prohibited commands, or unapproved domains.
- **FR-007**: Findings MUST have stable codes, severity, file or field location, explanation, and corrective guidance.
- **FR-008**: Blocking errors MUST prevent normalized download; warnings MUST remain visible but non-blocking.
- **FR-009**: Evaluation checks MUST detect missing positive scenarios, invalid graders, malformed criteria, skill-name leakage, and technique/vocabulary bias.
- **FR-010**: The workspace MUST preview the parsed skill, resources, evaluations, ownership status, and normalized package manifest without rendering active uploaded content.
- **FR-011**: A successful download MUST be produced from the validated upload revision and remain confined to supported repository roots.
- **FR-012**: Replacing an upload MUST invalidate all prior validation and download state.
- **FR-013**: Upload contents MUST not be retained after the request or sent to GitHub or another external service.
- **FR-014**: The first release MUST NOT edit uploaded files, create skills, execute evaluations, invoke an LLM, or create GitHub branches, commits, issues, or pull requests.
- **FR-015**: Existing catalog browse, search, filter, detail, and download behavior MUST remain unchanged.

### Key Entities

- **UploadedPackage**: file name, media type, byte size, upload revision, and archive entries.
- **ParsedSkill**: plugin, name, description, Markdown, resources, references, ownership status, and repository disposition.
- **ParsedEvaluation**: scenarios, prompts, activation expectations, graders, rubrics, fixtures, and timeout metadata.
- **ValidationFinding**: stable code, severity, location, message, and guidance.
- **ValidatedPackage**: current upload revision, validity, preview, and normalized manifest.

## Assumptions

- ZIP is the standard multi-file interchange format; a lone `SKILL.md` supports quick validation only.
- Contributors correct files in their own editor and re-upload; the workspace is deliberately read-only.
- The repository checkout remains authoritative for plugins, identities, domains, schemas, and contribution policy.

## Success Criteria

- **SC-001**: At least 90% of representative valid repository skills upload and reach a correct ready state without manual path selection.
- **SC-002**: Every unsafe archive in the security corpus is rejected before any content is rendered or packaged.
- **SC-003**: A package at published limits receives complete findings within 3 seconds under normal local conditions.
- **SC-004**: Preview and normalized ZIP manifests match for 100% of successful test packages.
- **SC-005**: Contributors can understand the first blocking problem and its file location without documentation in at least 9 of 10 usability trials.
- **SC-006**: Existing catalog regression journeys continue to pass unchanged.
