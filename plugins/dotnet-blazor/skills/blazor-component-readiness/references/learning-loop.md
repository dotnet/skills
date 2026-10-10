# Learning loop

Use only after a completed assessment and only when explicitly asked to improve the workflow.

## Capture

Record a local, privacy-safe observation containing skill/rubric version, generic component shape,
mode/timebox, guidance that prevented an error, unclear or missing guidance, highest-cost step,
safe evidence reuse, difficult status boundaries, reusable probe recipe, proposed smallest change,
and supporting workflow evidence.

Exclude package-owner secrets, proprietary source, credentials, private locators, unrelated
organization context, and the full session transcript.

## Decide where a lesson belongs

| Lesson | Destination |
|---|---|
| Demonstrated baseline requirement problem | `rubric.json`, with generated checklist update and compatibility review |
| General evidence/probe rule | `SKILL.md` or the applicable area reference |
| Output/manifest rule | `report-contract.md` |
| Feedback rule | `feedback-contract.md` |
| Library isolation/resume rule | `library-assessment.md` |
| Package/component-specific fact | Keep with that assessment only |
| One-off investigation detail | Do not add to the public workflow |

Generalize only repeated evidence from at least two independent assessments or a public standard.
Do not turn one package's `gap` into a universal rule. Preserve exact-snapshot evidence identities;
a changed observation gets a new immutable evidence record rather than rewriting an existing one.

Any workflow change needs a deterministic regression covering the prior failure. Model evaluations
belong to the later evaluation phase, not to a routine assessment.
