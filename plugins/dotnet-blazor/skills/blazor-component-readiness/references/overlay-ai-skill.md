# AI-skill overlay

Assess the `ai-skill` family when the confirmed deliverable includes an AI skill or plugin that
generates or recommends component code. Otherwise retain all six canonical rows as N/A with an
explicit no-deliverable rationale.

Use the six `AI-*` requirements exactly as defined in `rubric.json`. Verify the documented
contribution path, applicable repository guidance, update ownership for framework/library changes,
supported dependency constraints, portability without undocumented lock-in, and applicable
responsible-AI review. `AI-06` applies only to a new AI skill and requires RAI review before
merge, not merely an eventual review. Follow [the newness boundary](area-conditional-families.md):
known-existing skills retain a change-specific N/A for this row only; missing newness leaves
its applicability unresolved and draft status unassigned.

Assess generated code as code against the core requirements. Fluent or plausible output is not
evidence of runtime correctness, security, accessibility, package compatibility, or release
readiness. Missing private review records for an applicable obligation are `owner evidence required`; direct generated output
that violates a selected requirement may be a `gap`.

For `AI-04` and `AI-05`, dependency inventory covers not only package references and framework APIs
but also external runtime assets recommended or required by shipped AI guidance or generated code,
including script and stylesheet URLs, hosted service or upload endpoints, fonts, icon kits, module
imports, and similar runtime dependencies. Distinguish executable or runtime dependencies loaded or
called by generated code from harmless documentation hyperlinks, issue/reference links, or local
sample placeholders. For `AI-04`, a demonstrated runtime dependency outside .NET or qualifying
partner libraries supports `gap`. For `AI-05`, an external runtime asset is not inherently
proprietary or closed-source; `gap` requires direct evidence of that disposition. In either row, an
external runtime asset without bound identity, source, and license/support disposition prevents
`verified`; when that closure is absent, use `not tested` or `owner evidence required` according to
the repository-standard status boundary.
