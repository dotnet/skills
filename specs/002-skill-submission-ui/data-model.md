# Data Model: Guided Skill Submission Workspace

## SkillDraft

| Field | Type | Rules |
|---|---|---|
| schemaVersion | string | Must match current options |
| plugin | string | Existing repository plugin |
| name | string | 1-64 lowercase alphanumeric/hyphen; unique; no edge/consecutive hyphens |
| description | string | 1-1024 chars; purpose and activation boundary |
| purpose | string | Required outcome paragraph |
| whenToUse / whenNotToUse | string[] | At least one each |
| inputs | DraftInput[] | At least one; unique names |
| workflowSteps | WorkflowStep[] | At least two ordered steps |
| validationSteps | string[] | At least one observable check |
| pitfalls | Pitfall[] | Optional problem/solution pairs |
| owners | string[] | One team or at least two individual aliases |
| resources | ResourceDraft[] | Max 20, safe unique relative paths |
| scenarios | EvaluationScenario[] | 1-20; at least one positive activation |
| motivation | string | Required contribution justification |
| draftRevision | string | Relates edits to validation response |

## Supporting entities

- **DraftInput**: name, required flag, description.
- **WorkflowStep**: title, actionable instructions, optional checkpoint; order is authoritative.
- **Pitfall**: problem and corrective solution.
- **ResourceDraft**: path beneath `scripts/`, `references/`, or `assets/`; kind; media type; UTF-8/base64 content; encoding; size. Paths must be normalized, unique, bounded, and traversal-free.
- **EvaluationScenario**: id, unique name, natural prompt, activation expectation, graders, independent outcome rubric, timeout, optional fixture paths.
- **EvaluationGrader**: supported type plus conditional substring, pattern, or safe path.
- **ValidationFinding**: stable code, `error|warning`, field path, message, corrective guidance.
- **CanonicalSubmission**: revision, validity, findings, canonical SKILL.md/eval.yaml, CODEOWNERS additions, contribution summary, catalog preview, and package manifest.
- **CatalogPreview**: plugin, name, description, Markdown body, and resource metadata.

## State transitions

```text
New -> Editing -> Validate -> Invalid | ValidWithWarnings | Valid
Any edit after validation -> Stale
Package request -> Revalidate -> ZIP | Current findings
Successful ZIP -> PackagedRevision
Any later edit -> UnpackagedRevision
```

The server persists none of these states.

## Package layout

```text
plugins/<plugin>/skills/<skill>/SKILL.md
plugins/<plugin>/skills/<skill>/{scripts,references,assets}/...
tests/<plugin>/<skill>/eval.yaml
tests/<plugin>/<skill>/fixtures/...
.github/CODEOWNERS.additions
_submission/CONTRIBUTION.md
```