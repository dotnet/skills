<!--
Sync Impact Report
- Version change: template -> 1.0.0
- Added principles: Skills Are Versioned Products; Deterministic Validation Before LLM Judgment; Independent Author, Executor, and Judge Roles; Reproducible Evidence; Secure Treatment of Repository Content; Open Agent Compatibility; Accessible, Incremental User Value
- Added sections: Technical and Product Constraints; Development Workflow and Quality Gates
- Removed sections: none
- Templates reviewed: plan-template.md, spec-template.md, tasks-template.md (no changes required)
- Follow-up TODOs: none
-->

# Agent Skill Repository Constitution

## Core Principles

### I. Skills Are Versioned Products

Every skill MUST have a clear purpose, activation boundary, required inputs, bounded workflow,
validation guidance, and ownership. Skill content MUST follow the repository's supported directory
and manifest conventions. Changes MUST preserve provenance, licensing, and compatibility metadata.
A skill without testable value or an accountable owner MUST NOT be promoted as stable.

Rationale: Skills influence agent behavior and therefore require the same discipline as shipped
software.

### II. Deterministic Validation Before LLM Judgment

Every skill and evaluation artifact MUST pass deterministic validation before any LLM-based
assessment is considered. Deterministic checks MUST cover syntax, schema, references, packaging
boundaries, required metadata, and explicitly asserted outcomes. An LLM judgment MUST supplement,
never replace, deterministic evidence.

Rationale: Model judgments are probabilistic; structural and objective failures must remain
repeatable and inexpensive to diagnose.

### III. Independent Author, Executor, and Judge Roles

Skill authoring, scenario execution, and result judgment MUST remain logically separate. A judge
MUST NOT be told which candidate is expected to win. Baseline and skill-enabled results SHOULD be
presented in randomized or blinded order where supported. A single self-reviewing model verdict
MUST NOT be the sole acceptance gate.

Rationale: Role separation reduces confirmation bias, leakage, and incentives to optimize for the
judge rather than the user outcome.

### IV. Reproducible Evidence

Evaluations MUST record the skill revision, scenario revision, agent model, judge model, rubric
version, configuration, assertions, outputs, tool activity, usage metrics, timestamps, and failure
details needed to reproduce or explain a verdict. Published comparisons MUST use repeated trials
when model variance could change the conclusion. Evaluation artifacts MUST be machine-readable and
retain human-readable summaries.

Rationale: A score without its conditions and evidence cannot support trustworthy decisions or
regression analysis.

### V. Secure Treatment of Repository Content

Skill instructions, references, scripts, assets, evaluation prompts, and model output MUST be
treated as untrusted content. Browsing or packaging MUST NOT execute skill content. Downloads MUST
remain confined to the selected skill directory, and path traversal, unsafe links, or secret
exposure MUST fail closed. Secrets, credentials, and private endpoints MUST NOT be committed or
included in evaluation artifacts.

Rationale: The repository distributes instructions and executable-adjacent content across trust
boundaries.

### VI. Open Agent Compatibility

Canonical skill content MUST follow the open Agent Skills conventions used by this repository.
Agent-specific integrations for Codex, Cursor, Copilot, or other runtimes MUST be adapters around
the canonical skill rather than incompatible copies. Runtime assumptions and degraded behavior
MUST be documented and testable.

Rationale: Skills should remain portable, inspectable, and maintainable across evolving agent
runtimes.

### VII. Accessible, Incremental User Value

Product work MUST be delivered as independently testable user journeys ordered by value. Public
interfaces MUST meet WCAG 2.2 AA expectations and support keyboard, assistive-technology, desktop,
tablet, and mobile use. Each milestone MUST produce a demonstrable outcome; speculative platform
work without a current user journey MUST be deferred.

Rationale: A polished repository experience is valuable only when users can successfully discover,
understand, and use its skills.

## Technical and Product Constraints

- The repository MUST preserve its existing plugin and skill layout unless a migration is specified
  with backward-compatibility and rollback steps.
- The skill catalog is read-only and anonymous for its first release. Accounts, ratings, comments,
  publishing, automated installation, and LLM evaluation UI are separate features.
- The catalog implementation MUST use a supported C#/.NET API, a React front end, and Microsoft's
  Fluent design system unless this constitution is amended.
- Repository files remain the authoritative catalog source; duplicated mutable catalog content
  requires an explicit consistency design.
- External dependencies MUST be pinned, reviewed for provenance, and minimized.
- Upstream attribution and the ability to synchronize with `dotnet/skills` MUST be preserved.
- Performance, accessibility, security, and observability requirements MUST be represented by
  executable checks or documented manual verification.

## Development Workflow and Quality Gates

1. Every nontrivial feature MUST have a specification with prioritized, independently testable
   user journeys and measurable success criteria.
2. The implementation plan MUST document constitution compliance before research and again after
   contracts and data design.
3. Research MUST resolve all technical unknowns before implementation tasks are generated.
4. Contracts, data models, security boundaries, and a runnable quickstart MUST be defined before
   implementation.
5. Tasks MUST be traceable to requirements and user stories, with tests written before the
   corresponding implementation where behavior is testable.
6. Pull requests MUST pass formatting, static analysis, unit, contract, integration, accessibility,
   security, and relevant skill-evaluation checks.
7. Changes to evaluation infrastructure MUST update result investigation and schema documentation
   in the same pull request.
8. Scope changes that invalidate requirements or design decisions MUST update the specification and
   plan before implementation continues.

## Governance

This constitution is the highest-priority project governance document. Repository instructions and
feature plans MUST comply with it; conflicts MUST be resolved in favor of this constitution.

Amendments require:

1. A documented rationale and affected principles or constraints.
2. A migration and compatibility impact assessment.
3. Updates to dependent templates, specifications, plans, and contributor guidance.
4. A semantic version change: MAJOR for incompatible governance changes, MINOR for new or materially
   expanded requirements, and PATCH for nonsemantic clarification.

Every implementation plan and pull-request review MUST verify constitution compliance. Any approved
exception MUST be recorded in the plan's Complexity Tracking section with the rejected simpler
alternative and a removal or review condition.

**Version**: 1.0.0 | **Ratified**: 2026-07-26 | **Last Amended**: 2026-07-26
