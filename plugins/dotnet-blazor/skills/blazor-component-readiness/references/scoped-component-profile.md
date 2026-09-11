# Scoped component profile (V1)

This explicitly selected profile uses all 51 component-specific, non-extension checks from frozen
rubric `2.0.1`. It preserves every ID, requirement, ownership, area, primary/additional clause,
and canonical position. The ordered compact ID-array SHA-256 is
`2f0c862398f58aa0cef426cb8a2b4c46fa33e2d907a294bd46d7f1fa3b1f4122`.
There is no caller-defined subset, replacement with clause-level rows, or automatic authority
grant from profile metadata.

Register these retained inputs through the existing candidate builder and confirmation flow:

| Input | Evidence-input kind | Meaning |
|---|---|---|
| Byte-identical `requirement-basis.json` from the loaded skill | `scoped-component-profile-v1` | Selects the closed V1 validation policy and binds the frozen bundled requirements. |
| Byte-identical scoped package validation receipt, staged as `scoped-package.validation.json` | `scoped-package-context-v1` | Binds the separate package context by digest, not by an assertion of full coverage. |

Use `--package-context-revision` to locate the exact staged package revision on assessment,
report, and reader commands. The locator is not authority: the revision, scope, required internal
closure, package/source identities, and receipt digest must agree with the confirmed input.
An ordinary `--package-revision` remains the full-package path and cannot be substituted.
Missing, orphan, altered, unknown, or conflicting profile/context inputs fail closed.
When the context revision binds feedback, provide its exact bytes with
`--package-context-feedback`. This verifies the context's receipt; it is not component feedback
and is not copied into the component reader. V1 rejects ordinary `--package-feedback`.

Source equality here means the entire `source` record: availability, repository URI, commit,
mapping, and confidence, not just the repository/commit pair. Preserve that record when reusing
the same still-valid context; place new investigation/reuse narrative in separate retrieval or
assessment context. A materially different source claim requires a valid new binding, not a
prose edit to a confirmed manifest or a relaxed equality check.

For this profile, `package_reference` remains null in both the component assessment and validation
manifest: no ordinary full-package prerequisite is asserted. The typed scoped context is bound
through the confirmed input identity and its distinct descriptor. Reader
`package_validation_sha256` also remains null. Do not reinterpret these ordinary fields as scoped
context, reuse the package findings as component findings, or use this profile for ordinary library
completion. The existing 48-check package selection and ordinary/legacy contracts remain unchanged.
Create its package context using the [explicit scoped-package producer](report-contract.md#explicitly-authorized-package-scope-recovery).

Initialization, validation, verification, corrections, and publication rechecks must all resolve
the same bound profile/context policy. Changed input/context identities follow the existing
fresh-binding and lineage rules; a changed locator alone cannot replace a bound context.
V1 does not use the ordinary historical missing-feedback fallback: a feedback-bound predecessor
requires its exact feedback bytes and report reconstruction. If those bytes are unavailable,
verification fails rather than treating a matching report hash as sufficient.

Keep the complete canonical-verification set internally, including all exact ledgers and dependency
inputs required by the existing contracts. A selected-only component companion is allowed only when
constructed and verified through existing mechanisms with correct identities and provenance.
Never crop an original historical ledger and relabel it under its original identity.
When a valid selected record requires an unexported supersession ancestor, the reader may omit
the structured selected-only companion rather than manufacture an invalid partial bundle.
Its existing evidence view must retain the selected record's identity, applicability, provenance,
digests, and supersession references, and explicitly disclose the omitted companion and internal
dependency. Invalid canonical evidence still fails; this is not a general validation-error fallback.

The V1 reader/export set is narrower: no raw registered inputs or attachments, full requirement basis,
historical ledgers/reports, context-receipt payloads, or package/source archives are exported merely
because they were registered. Enforce disclosure rules across prose, summaries, mapping, technical
JSON, evidence companions, provenance, feedback, attachments, and fallbacks. Exact canonical copies
must stay byte-identical and disclosure-permitted, not secretly filtered. A scope/disclosure failure
does not authorize an unscoped export or weaker evidence validation.

The exported reader is **not a self-contained evidence-validation bundle**. Re-verification that
needs omitted inputs depends on the retained internal workspace. Accurately distinguish internally
retained-but-unexported bytes from unavailable or commitment-only evidence; hashes and references
do not imply attachment delivery. Mentioning an excluded ID does not itself assess that check, but
separate disclosure prohibitions still apply. Reader grouping preserves all individual statuses,
evidence references and qualifications, including mixed results and explicit missing evidence.

After resolving this policy, follow [shared assessment workflow](assessment-workflow.md) for a
requested assessment, or [report](report-contract.md) and [reader](partner-preview.md) for an
existing revision. These task links do not authorize new assessment work merely to render.
