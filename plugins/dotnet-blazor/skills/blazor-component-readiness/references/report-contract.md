# Report and validation-manifest contract

JSON is canonical. Markdown is generated only by the bundled validator and an earlier revision is
never overwritten.

For component work, read [standalone component scope](scoped-component-profile.md).
A scope or disclosure failure blocks publication; it never permits a package-wide fallback.

## Revision layout

Use `revisions/0001`, `0002`, and so on. Each directory contains exactly:

- `input-manifest.json`
- `<kind>.assessment.json`
- `<kind>.evidence.json`
- `<kind>.report.md`
- `<kind>.validation.json`

`<kind>` is `package` or `component`. Unified and noncurrent contracts are rejected,
including historical verification; existing files remain untouched. Revision `0001` has no predecessor. Each later
revision binds the immediately preceding validation-manifest digest and cannot skip or reuse a
sequence. A package/version or confirmed-inventory change starts a new root.

## Assessment rows

Every selected row preserves rubric order and contains its ID, rubric-owned scope/area/wording,
one exact status or an incomplete value, factual observation, selected evidence IDs, owner action,
assessment follow-up, and a rationale when not applicable. Findings and summary groups are factual,
unranked, and traceable to selected rows/evidence.

Preserve all currently required status fields under [status boundaries](status-boundaries.md).
Where the current contract makes `owner_action` or `assessment_follow_up` optional, it may state a
concise, evidence-grounded missing record/owner decision, supported next diagnostic, or artifact
needed for reassessment. Such optional fields may remain empty when no safe prescription exists.
Do not invent an implementation cause or perform new research/probes merely to populate them.
Preserve existing useful actions; extended examples belong in requested guidance. This authoring
rule adds no validation requirement and does not invalidate retained current-format revisions.

Only assessment schema 2 and rubric 2.1.0 are accepted, always with the current typed
public-absence/source-proof, static-SSR behavior and Auto-transition rules. Unsupported assessment schemas or rubric
identities are rejected, not migrated, reinterpreted or rendered. `rubric_version` is persisted
provenance, not an initialization choice; the canonical `overlays` array must remain empty.
Unrelated schema-1 evidence, inventory, comparison and library-state formats retain their own
contracts. Reader output versions are separate from the assessment contract.

The generated report includes:

1. completion state and exact package/component/rubric identity;
2. assessment inputs, retrieval attempts, source availability, source-artifact captures, documents,
   owner inputs, confirmed inventory, modes, and exclusions;
3. counts for all five statuses plus incomplete;
4. factual summaries/findings;
5. optional feedback rendered verbatim;
6. the complete canonical requirements table;
7. fixed limitations distinguishing structural validation from factual truth.

When optional feedback context is supplied, retain a separate readable companion beside the
feedback source or report. It may summarize each relevant item as `changed`, `qualified`, or
`unchanged`, identify unmapped or ambiguous entries, preserve attribution and source links, and
explain whether the item was context only or supported an evidence-backed correction. Do not place
free-form source prose into the canonical feedback table, mutate immutable revisions, or treat
feedback-only commentary as evidence. The companion is supplemental and does not replace the
canonical report or validation manifest.

The canonical table header is:

```text
| ID | Scope | Area | Requirement | Status | Factual observation | Evidence and provenance | Owner action | Assessment follow-up / rationale |
```

## Validation manifest

### Readable projection

After canonical verification, the [partner-preview reader](partner-preview.md) is the default
local delivery view. `reader render` and `reader verify` produce and check a separate derived
directory; `report render`, `report verify`, canonical bytes and five-file revisions are unchanged.
Its four-column table groups only existing rubric facts and retains each check's exact result,
qualifications, ownership and evidence. `mapping.json` preserves the internal check mapping;
the reader manifest binds the exact source validation digest, rubric, optional package binding
and every derived file. Invalid projection or unavailable private inputs must fall back to the
canonical local report with the reader limitation explicitly reported, never an unverified rewrite.

The manifest binds plugin/validator/renderer versions; rubric and scope-map digests with an empty
overlays array; input, assessment, evidence, and report digests; selected evidence IDs; assessment kind and
completion state; an optional explicit package binding for component work; optional feedback digest;
and optional predecessor plus declared changed IDs.

It is tamper-evident but unsigned. It proves byte correspondence, not authorship, factual accuracy,
approval, certification, or suitability.

## Ordinary component binding

A component revision is standalone by default. It needs no full-package assessment
or scoped package context. Only an explicitly supplied current full-package revision
may be bound; neither sibling assessments nor the separate 48-check package scope
can substitute for that declared binding.

```text
<launcher> assessment init --kind component --root <output> --input <input> --component <id> --output <assessment>
```

When a package reference is deliberately declared, initialization, validation,
report render/verify and reader render/verify use the exact `--package-revision`
and its `--package-feedback` when required. Missing, wrong-identity, stale or
digest-mismatched declared bindings fail without fallback. An older immutable
revision is not stale merely because a newer one exists: compare the declared
reference. Never infer an unrelated package assessment or create one to fill a
missing binding. Package findings and sibling evidence cannot become component evidence.

Run `assessment validate` before `report render`, then `report verify` on the created revision.
Reject an extra byte, stale feedback, wrong package binding, missing selected evidence, changed
identity, evidence not bound to confirmed input bytes, linked revision/artifact paths, mutable
earlier revision, or unverified handoff.

### Explicitly authorized package-scope recovery

The validator supports one closed 48-check package selection from the frozen bundled requirements,
not an arbitrary requirement-ID filter. Ordinary package and component behavior is unchanged when
no explicit scope/profile input is present.
For an explicitly requested scoped package, produce the descriptor from the existing unscoped
confirmed intake, without creating or depending on a package report:

```text
readiness inputs scope --root <inputs-root> --manifest <unscoped-confirmed.json> --output <inputs-root>/authorized-package-report-scope.json
```

Register the resulting `authorized-package-report-scope.json` through the existing candidate builder
as an `evidence_inputs` entry of kind `authorized-package-report-scope`, then discover and confirm
the new input generation. Its existing `path` and `kind` fields bind the basename, size and SHA-256.
The descriptor binds the exact package and entire source record, the current rubric and bundled
requirement-basis digest, and the fixed ordered selection. It does not contain its enclosing input's
digest; confirmation binds the descriptor in that input instead. Do not manually reconstruct,
edit or overwrite it. Scope selection establishes neither publisher approval nor execution authority.

Initialization, validation, report verification and the reader recognize that retained entry
automatically. There is no ID-list switch to omit later or use as a fallback. Empty, malformed,
unsupported, duplicated or altered scope inputs fail closed, as do wrong definition/package/source
identities and any mismatch in the exact approved canonical selection, count or selected-set digest.
The definition and selection are pinned outside caller-supplied metadata. The current descriptor
schema is 2; older descriptors are rejected, not silently rebound to a different subject.

Existing schemas carry the scope transitively through the confirmed input digest, assessment
selection, evidence identity, report/receipt hashes and reader file hashes. Every consumer resolves
the manifest and checks those bindings. The report declares the frozen bundled basis and scope identities;
the reader also retains the canonical scope manifest. The frozen rubric scope-map digest keeps its
original meaning and is not replaced by the selected-set digest. Completion refers only to the
authorized selection; it does not establish ordinary full-package coverage.
Component assessment has no package-assessment prerequisite.

The authorized evidence companion must contain only selected records. Preserve full historical
ledgers separately; do not crop and relabel an original ledger. A new confirmed input changes
evidence identity and therefore requires a new, accurately attributed evidence-backed revision,
not an unchanged-reuse claim. Within a scoped revision lineage, a changed finding requires new
evidence even when its status token did not change.

The scoped partner reader does not export underlying raw evidence attachments, which may contain
internal historical findings. It preserves selected claims, methods, locators, identities and
qualifications, and states that source bytes remain in the local validation inputs. It must not
fall back to copying historical reports, unselected ledger records or archives. Excluded identifiers,
known extension text and broader-coverage claims are rejected in the selected outputs; the final
human/agent usefulness review must additionally detect paraphrased or semantically unrelated
content. Missing bound feedback cannot bypass scoped report verification. A failed scoped reader
is a blocked deliverable, not authorization to publish an unscoped fallback.

### Component scope and disclosure

Read [standalone component scope](scoped-component-profile.md) before initialization,
optional binding, feedback, verification or reader export. The retired component profile and
package-context descriptors are rejected, not migrated. Preserve selected-only evidence exports
and exact feedback lineage in the normal component path.

## Feedback and corrections

When feedback exists, read [the feedback contract](feedback-contract.md). The CLI reads but never
rewrites the user-owned file. Render a new revision with `--feedback`, preserving input,
assessment and evidence bytes exactly. Verify using the same exact feedback and binding inputs.
For a component revision, apply [the component feedback/lineage rules](scoped-component-profile.md).
Missing declared feedback or binding inputs cannot fall back to an unbound verification.
Preserve prior feedback files and supply them through repeatable `--feedback-history <file>`
options on `report render/verify`, `assessment revise` and `reader render/verify` when a component
predecessor binds different commentary. Resolution is by the declared digest, not directory
discovery or reconstruction. Historical feedback is verification input, not reader content.

A factual correction requires new evidence, the immediate predecessor digest and declared IDs.
Read [targeted profiles](targeted-profiles.md) and the [shared assessment workflow](assessment-workflow.md)
for the requested new evidence and final identity. Copy the prior assessment to a replacement
draft, change only declared IDs, canonicalize to a new file and run:

```text
<launcher> assessment revise --root <output> --input <input.confirmed.json> --assessment <replacement.json> --evidence <replacement.evidence.json> --output <revisions> --predecessor <sha256> --changed-ids <id,id>
```

Pass any explicitly declared package binding and exact feedback flags. Do not edit
prior revisions or claim unselected rows were reverified.

## Decision guidance

The default report remains factual. Ordinary requests for recommendations, implementation examples,
remediation, practical guidance or next steps opt into `decision-guidance.md`; verdict/prioritization
requests remain supported. No filename request or second confirmation is needed. Read
[remediation guidance](remediation-guidance.md) before authoring.

For revision-specific guidance, use the existing validated revision and requested unresolved
findings. General documentation-testing or payload/serialization questions may be answered
inline without a report or failed criterion. Do not create an assessment, finding, status change
or invented validation digest to unlock advice. Do not rerun the assessment, collect evidence,
execute probes or perform network research to create guidance.
When the cause is not established, request the exact missing evidence or owner decision instead
of inventing a workflow/tool prescription. Local reads, digest checks and the companion write
are sufficient; factual corrections remain a separate task.

For a revision-specific request, place the companion under the confirmed output root and outside `revisions/`, beside relevant
feedback when that location is permitted, otherwise at `<output>/decision-guidance.md`.
No feedback file is required. Do not put it inside a deterministic reader directory.

Its first metadata block must include:

```text
Source validation manifest SHA-256: <64-lowercase-hex>
```

Revision-specific guidance is an unbound, regenerable derivative of exactly that validated revision. It may state the
requested verdict and priorities, but cannot change or supplement canonical facts, rows, evidence,
counts, reports, or manifests. Replace it only after another explicit guidance request.
Use the [per-finding shape](remediation-guidance.md#per-finding-shape), preserve baseline versus
versioned-extension classifications, and link the companion in the final handoff without editing
the canonical report or reader. General requested advice has no source-validation metadata
requirement and does not mutate any existing artifacts. Advice alone grants no execution
permission. A factual-only request creates no companion.
