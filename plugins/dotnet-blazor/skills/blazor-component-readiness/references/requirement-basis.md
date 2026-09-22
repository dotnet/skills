# Authoritative bundled requirement basis, version 2.1.0

## Source and inventory

The normative requirements are bundled in
[requirement-basis.json](requirement-basis.json), which retains their complete text.
They define the **bundled partner-readiness baseline**, not a universal engineering
or adoption standard for every Blazor library.
Clause names identify section and bullet (`6.intro`/`7.intro` identify section
introductions).

[rubric.json](rubric.json) maps **every canonical ID** to a clause and one of four
classifications. The loader resolves `RequirementBasis.Quotation` from the frozen
crosswalk and includes additional exact clauses when a row spans obligations.
It checks both complete-file digests, plus wording and ownership projections.
The [checklist](checklist.md) is the readable ID index; JSON is authoritative.

## Meaning of the four classifications

- **direct obligation**: the stated baseline obligation, without an extra operational
  delivery requirement.
- **decomposition evidence check**: a bounded check of part of a baseline obligation.
  Evidence protocols establish what an assessment may claim; they do not require
  a vendor to adopt the assessor's exact tooling or supply a new policy deliverable.
- **conditional obligation**: a baseline condition or optional feature applies only in
  its stated circumstances. Keep the row visible and justify exclusions. RTL is
  optional; emergency out-of-band servicing is a consideration, not an unconditional
  release obligation. All scaffolder/AI rows belong in the package inventory.
- **versioned extension**: a separately versioned operational control, **not a
  baseline defect**. The quoted requirement clause supplies context only. `extension_basis`
  states the difference. These controls need separate policy approval before a
  vendor remediation demand. The current contract does not encode such approval;
  therefore its extension rows always retain the policy-clarification label.

Package-only extensions retain their existing classification. The explicitly
removed component extensions do not remain as scored rows or N/A placeholders.
BEQ-05 now checks the existing supported static-SSR behavior obligation, not a
new documentation contract. This is a revised assessment mapping, not a new
partner quality bar or a claim of improved measured performance.
`TA-08` is a supplementary extension outside canonical counts, not a replacement
for any canonical ID. The explicit baseline IsTrimmable obligation is under `TA-01`.

## Ownership and applicability

The catalog has 112 IDs in separate **60 package/conditional** and **52 component** inventories,
including all twelve conditional IDs in the package unit. There is no unified
assessment or package prerequisite for a component. There is no optional overlay selection.
`SEC-01` through `SEC-03` are repository-wide rows in the package assessment.
Their shared action key is `library-security-review`: one exact-package evidence
packet supports three conclusions and one shared security-review request. Component
assessments contain no copies or pointer rows for these IDs. `SEC-10` through
`SEC-13` remain component-specific. Scope is the single ownership axis.

Scope schema 2 binds the exact current ownership map.

`SUP-10` records Microsoft's removal discretion. It does not require a vendor
suspension/reinstatement process or demand a new release workflow.

## Named obligations and limits

Generic accessibility scans do not substitute for Accessibility Insights for Web
FastPass per release and full Assessment per major release. The major-release
screen-reader test names NVDA, JAWS, or Windows Narrator. Parameters explicitly
include BL0007, auto-properties, no required/init, and no internal mutation.
Disposal includes JSDisconnectedException, IAsyncDisposable and JS/.NET reference
cleanup. JS modules are collocated .razor.js loaded in OnAfterRenderAsync.
CSS uses .razor.css, avoids inline styles, and documents a prefixed global surface.
Library .NET/BL* analyzer warnings cannot be waived by a migration plan. Trim/AOT
warnings must be annotated or fixed, not suppressed; AOT is not optional merely
because a vendor did not claim it. Every packaged binary needs source/upstream
attribution. WASM trimming and avoiding bloated transitive dependencies remain
normative without imposing an additional measured-performance-budget requirement.

## Current contract and exact release boundaries

Rubric `2.1.0` is the sole executable assessment contract. Initialization has no
rubric or overlay selector. Persisted `rubric_version` is validator-owned provenance
and must match that exact version and its frozen digests. `overlays: []` remains
part of the canonical artifact shape; nonempty overlays are rejected.

Only assessment schema 2 is accepted, always with current evidence protocols.
Unsupported assessment schemas and rubric identities are rejected, not inferred,
migrated or silently reinterpreted. Unrelated schema-1 formats such as evidence and
comparison inputs retain their own validation rules; they are not schema-1 assessments.
The current artifact fields, requirement-basis digest and ownership bindings remain exact.

Product remediation can only be
verified on a new exact package digest; new evidence on the same package may
correct an assessment claim but is not a replacement release.

The authoritative statuses remain `verified`, `gap`, `owner evidence required`,
`not tested`, `not applicable`.
Partner work status and disposition belong in a separate action/response layer.
