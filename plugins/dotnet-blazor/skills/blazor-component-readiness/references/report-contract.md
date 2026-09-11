# Report and validation-manifest contract

JSON is canonical. Markdown is generated only by the bundled validator and an earlier revision is
never overwritten.

For an explicitly bound scoped component, read [the V1 profile](scoped-component-profile.md)
before any generic binding, feedback, verification or export step below. Its scoped context,
feedback lineage and disclosure restrictions take precedence; a failed scoped reader is blocked,
not permission for an unscoped fallback.

## Revision layout

Use `revisions/0001`, `0002`, and so on. Each directory contains exactly:

- `input-manifest.json`
- `<kind>.assessment.json`
- `<kind>.evidence.json`
- `<kind>.report.md`
- `<kind>.validation.json`

`<kind>` is `unified`, `package`, or `component`. Revision `0001` has no predecessor. Each later
revision binds the immediately preceding validation-manifest digest and cannot skip or reuse a
sequence. A package/version or confirmed-inventory change starts a new root.

## Assessment rows

Every selected row preserves rubric order and contains its ID, rubric-owned scope/area/wording,
one exact status or an incomplete value, factual observation, selected evidence IDs, owner action,
assessment follow-up, and a rationale when not applicable. Findings and summary groups are factual,
unranked, and traceable to selected rows/evidence.

New assessments use schema version 2 and enforce the current typed public-absence/source-proof
rules. Schema-version-1 assessments remain parseable and verifiable with their legacy evidence
rules so immutable historical revisions and predecessor chains do not retroactively fail. Revision
chains may upgrade from v1 to v2 during an evidence-backed correction, but cannot downgrade or skip
schema generations.

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

The manifest binds plugin/validator/renderer versions; rubric, scope-map, and selected-overlay
digests; input, assessment, evidence, and report digests; selected evidence IDs; assessment kind and
completion state; package assessment/report bindings for component work; optional feedback digest;
and optional predecessor plus declared changed IDs.

It is tamper-evident but unsigned. It proves byte correspondence, not authorship, factual accuracy,
approval, certification, or suitability.

## Ordinary component binding

An ordinary component revision binds the exact validated full-package revision, not a sibling
assessment or the separate 48-check scoped recovery. For scoped work use the dedicated
[profile](scoped-component-profile.md) instead of this command and these ordinary flags.

```text
<launcher> assessment init --kind component --root <output> --input <input> --component <id> --package-revision <revision> --output <assessment>
```

Ordinary component initialization, validation, report render/verify and reader render/verify
require `--package-revision <package-revision>` and, when used,
`--package-feedback <package.feedback.md>`. Verify exact package/source/input binding; package
findings and sibling component evidence cannot become this component's evidence.

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
authorized selection; it does not establish ordinary full-package coverage or satisfy a component
assessment's full-package prerequisite.

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

### Scoped component profile (V1)

Read the complete [scoped component profile](scoped-component-profile.md) before initialization,
ordinary binding examples, feedback, verification or reader export. This compatibility landing
section does not replace the V1 contract or authorize ordinary full-package fallback.

## Feedback and corrections

When feedback exists, read [the feedback contract](feedback-contract.md). The CLI reads but never
rewrites the user-owned file. Render a new revision with `--feedback`, preserving input,
assessment and evidence bytes exactly. Verify using the same exact feedback and binding inputs.
For a profile-bound revision, apply [the profile's feedback/lineage rules](scoped-component-profile.md)
before any ordinary fallback or package-feedback recipe.

A factual correction requires new evidence, the immediate predecessor digest and declared IDs.
Read [targeted profiles](targeted-profiles.md) and the [shared assessment workflow](assessment-workflow.md)
for the requested new evidence and final identity. Copy the prior assessment to a replacement
draft, change only declared IDs, canonicalize to a new file and run:

```text
<launcher> assessment revise --root <output> --input <input.confirmed.json> --assessment <replacement.json> --evidence <replacement.evidence.json> --output <revisions> --predecessor <sha256> --changed-ids <id,id>
```

Pass the applicable ordinary or scoped-context binding and exact feedback flags. Do not edit
prior revisions or claim unselected rows were reverified.

## Decision guidance

The default report remains factual. Only after an explicit verdict/prioritization/remediation/next
steps request, create `decision-guidance.md` beside the relevant feedback file and outside
`revisions/`.

Its first metadata block must include:

```text
Source validation manifest SHA-256: <64-lowercase-hex>
```

Guidance is an unbound, regenerable derivative of exactly that validated revision. It may state the
requested verdict and priorities, but cannot change or supplement canonical facts, rows, evidence,
counts, reports, or manifests. Replace it only after another explicit guidance request.
