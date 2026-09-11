# Authoritative bundled requirement basis, version 2.0.1

## Source and inventory

The normative requirements are bundled in
[requirement-basis.json](requirement-basis.json), which retains their complete text.
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

Existing provenance, CI, and performance extensions keep their canonical ID
slots. This avoids quietly deleting rows to improve coverage or defect counts.
`TA-08` is a supplementary extension outside canonical counts, not a replacement
for any canonical ID. The explicit baseline IsTrimmable obligation is under `TA-01`.

## Ownership and applicability

The canonical ledger has 121 IDs, split **60 package/conditional + 61 component**.
The count is 110 previous core IDs, minus the noncanonical supplementary trim
audit, plus twelve conditional IDs. No selected overlay adds or removes rows.
`SEC-01` through `SEC-03` are repository-wide rows in the package assessment.
Their shared action key is `library-security-review`: one exact-package evidence
packet supports three conclusions and one shared security-review request. Component
assessments contain no copies or pointer rows for these IDs. `SEC-10` through
`SEC-13` remain component-specific. Scope is the single ownership axis.

The 2.0 scope schema and its ownership are preserved in 2.0.1. Frozen
1.3.0 assessments retain their original 46 package / 64 component ownership.
Neither their row scopes nor their evidence are rewritten retroactively.

`SUP-10` records Microsoft's removal discretion. It does not require a vendor
suspension/reinstatement process. Restoring its meaning is an assessment correction,
not a demand to implement a release workflow.

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
normative even though measured performance budgets are extensions.

## Compatibility and exact release boundaries

The loader supports exactly rubric `1.3.0` and `2.0.1`; unknown versions fail.
New initialization defaults to `2.0.1`. `--rubric-version 1.3.0` is an explicit
legacy authoring/reproduction path, not an alias for the new rubric.
The frozen legacy rubric/checklist remain byte-identical. Legacy assessments
retain their exact row wording, statuses, scopes and optional-overlay behavior.
The 2.0.1 maintenance revision preserves the 2.0.0 requirements and selections
while binding the bundled requirement text under its updated metadata and digest.
Existing 2.0.0 artifacts require their original tool; they are not silently
accepted or rewritten under the changed binding.
New rubric assessments require current evidence protocols; selecting an older
assessment schema must not bypass those protocols.

Assessment JSON and raw evidence protocols do not change shape. The rubric digest
binds the new meaning, and its loader also pins the crosswalk digest. Corrections
between rubric generations require a new assessment lineage, not a silent revision
that reinterprets old row IDs. Retain the old immutable revision, feedback payloads,
and digest references as the migration evidence. Product remediation can only be
verified on a new exact package digest; new evidence on the same package may
correct an assessment claim but is not a replacement release.

The authoritative statuses remain `verified`, `gap`, `owner evidence required`,
`not tested`, `not applicable`. No retroactive vocabulary rewrite is necessary.
Partner work status and disposition belong in a separate action/response layer.
