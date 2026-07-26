# Research: Guided Skill Submission Workspace

## Stateless authoring

**Decision**: Keep the active draft in browser session storage and send it to the same-origin API only for stateless validation, canonical rendering, and ZIP generation.

**Rationale**: Supports refresh recovery without accounts, retention policy, or a database while centralizing security-sensitive repository rules.

**Alternatives**: Server persistence adds privacy/identity scope; browser-only generation duplicates and drifts from repository parsing.

## Current evaluation format

**Decision**: Generate the Vally format documented by current `CONTRIBUTING.md`: top-level metadata and `stimuli` containing prompts, graders, rubric, and optional `expect_activation: false`.

**Rationale**: Active repository documentation and workflows are authoritative over older bundled `scenarios/assertions` guidance.

**Alternatives**: Legacy schema is rejected as stale; a UI-only intermediate schema would not be ready to commit.

## Canonical rendering

**Decision**: Validation returns canonical SKILL.md, eval.yaml, ownership additions, contribution summary, preview data, and package manifest; package generation invokes the same renderer after revalidation.

**Rationale**: Prevents preview and ZIP output from disagreeing.

## Deterministic findings

**Decision**: Findings have stable code, error/warning severity, field path, message, and corrective guidance. Errors block download.

**Rationale**: Enables precise tests, accessible summaries, and safe rule evolution.

## Repository-driven rules

**Decision**: Read plugin names and identities from the catalog snapshot, allowed domains from `eng/known-domains.txt`, and mirror contributor/validator naming, reference, ownership, and safety rules.

**Rationale**: Preserves the repository as source of truth. Invoking the full validator on each edit is too costly for an interactive UI.

## Safe packages and resources

**Decision**: Use platform ZIP support. Allow bounded UTF-8 scripts/references and allowlisted base64 assets. Normalize every entry and confine it to skill, test, `.github`, or `_submission` roots. Never execute or HTML-render resource content.

**Rationale**: Minimizes dependencies and attack surface while supporting real skills.

## Validation timing

**Decision**: Run lightweight client checks during editing and authoritative server validation on step changes, preview, and package requests.

**Rationale**: Provides timely feedback without request churn or noisy accessibility announcements.