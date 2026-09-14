# Shared assessment workflow

Use this sequence for a requested canonical assessment or factual correction, not an unscored
offline-facts handoff, a worksheet, or merely rendering an existing revision. Read only the
selected task's prerequisites from `SKILL.md`; a link for a conditional task is not an instruction
to execute it. Before generic initialization or binding, an explicitly selected scoped unit
must read [its profile](scoped-component-profile.md). Ordinary component binding is owned by
[the report contract](report-contract.md#ordinary-component-binding).

## 1. Confirm scope and inputs

Inspect every supplied local artifact before asking for missing information. Keep the exact
package/version/digest, requested kind, approved external output root, exclusions and timebox.
Do not begin assessment probes from an unconfirmed manifest or broaden an assigned unit.
Supplied artifacts are starting inputs, not proof that the requested investigation is complete.

Enforce the skill's [assessed-code execution prerequisite](../SKILL.md#assessed-code-execution-prerequisite)
before executable collection. Input confirmation does not authorize execution or establish host
isolation; continue permitted static work and preserve actual prerequisite blockers.

When confirmed inputs are not already sufficient, follow [artifact acquisition](artifact-acquisition.md)
and [documentation discovery](documentation-discovery.md). Latest stable is only a draft default;
an explicit version remains authoritative. Failed retrieval is an attempt, never a product gap or
proof of closed source. Source is `source-available`, `closed-source`, or `unresolved`.
Package-only can confirm `components: []` and requires no component selection or component-specific
source closure. Applicable original-library source closure for package analyzer/trim/AOT checks
remains required by [acquisition](artifact-acquisition.md#original-library-source-closure).
For selected components, confirm IDs, claimed modes, allowed source and lifecycle applicability.
Source-closure details stay in acquisition; component probe procedures stay in the selected areas.

Show the package, sources, documents, evidence inputs, modes, exclusions and supplied feedback
to the owner before scoring. Use [input candidates](input-candidates.md) for typed construction,
`inputs discover`, explicit `inputs confirm`, and `inputs validate`; never hand-author candidates
or copy a confirmed manifest back into discovery. Library inventory and separate execution are
conditional routes in `SKILL.md`, not prerequisites for a package or unified assessment.

## 2. Collect evidence and establish final identity

Before collection, read [provenance and integrity](area-provenance-integrity.md), then only the
area owners selected by `SKILL.md`. Apply the provenance owner's target and command checks before
every package probe, using the active confirmed manifest and the new final manifest after any
reconfirmation. Keep evidence claim-bounded and component evidence isolated.
Never treat feedback as evidence or unapproved operational extensions as baseline defects.

Attempt each authorized, accessible family in scope before final status assignment, subject to
stage 1's execution prerequisite: exact package/artifact, source, documentation and applicable
consumer/runtime or trim/toolchain checks. Missing supplied probe results do not themselves
establish a blocker. Stop at the approved timebox; applicable unperformed probes remain
`not tested` with their actual blocker and smallest next probe.

Use confirmed catalogs and preparation indexes to locate row-relevant supplied material, then
inspect its contents: source, documentation, owner inputs, and historical execution/scan records.
Before calling evidence missing, distinguish an absent record from a present record that lacks
the required fact, identity binding or coverage. Carry the supported fact and exact limitation
forward; an index or summary does not replace inspection of the relevant underlying record.
If a tool reports truncated output saved to a file, search/read that file for relevant evidence
before concluding evidence is absent; targeted searches or ranges are sufficient.

Use the [input producer](input-candidates.md) for typed `evidence draft-add`, actual capture
metadata, and the post-output confirmation sequence. Preserve the pre-output manifest and
authorized derived result; append only that result to new candidates, discover and explicitly
confirm a new manifest before initializing/exporting identity. This is not permission to add
unrelated inputs. Registered reviewer analysis requires `--root --manifest --evidence-input`;
the producer derives the locator and digest together, not from a producer label.

Initialize from the final confirmed manifest, then use `assessment export-identity`,
`evidence ledger-build`, `evidence ledger-validate`, and input-bound
`evidence bundle --root <output> --manifest <final-confirmed-input>` in the producer's
[canonical sequence](input-candidates.md#canonical-identity-and-ledger-sequence).
Structural-only bundling does not accept input linkage. Rebuild downstream artifacts through
the existing producers after input changes; never hand-author canonical identity/subject bytes,
force EV1 changes, or copy old IDs instead of regenerating them.

## 3. Apply statuses and reconcile coverage

Read [status boundaries](status-boundaries.md) before assigning any row. Use the confirmed
applicability and the status-specific evidence, provenance, rationale and ownership contracts.
Earlier row decisions are provisional: after further collection or input reconfirmation,
recompute affected statuses, selected evidence, findings and summaries before finalizing.
Before bulk row assignment or findings/summary generation, finish each requirement's decision
in the existing assessment **DRAFT** fields:

| Draft field | Decision to record |
|---|---|
| `observation` | The requirement-specific inspected fact, actual outcome, and identity/coverage limits. Name what present evidence does not establish rather than calling it absent. |
| `evidence_ids` | Select the records that support this row's fact and boundary, not everything in the same area. |
| `status`, `not_applicable_rationale` | Apply the status boundary to this requirement; explain it in the existing observation/rationale fields, not a new schema field. Confirm a no-surface rationale before `not applicable`. |
| `owner_action` | Name the exact owner-held fact, decision or record still needed, when applicable. |
| `assessment_follow_up` | Name the remaining fact and smallest next acquisition/probe, with its actual blocker; do not invent an action for an already satisfied fact. |

Shared evidence and prose are valid when they support every mapped requirement. Before applying
a bulk mapping, check each distinct row's required fact, applicability, limitation and next
action; revise unsupported assignments rather than requiring unique prose or evidence records.
Low record count alone is not failure when each mapping is supported. Derive findings and
summaries from those decisions, not family prefixes; do not accept a blanket not-tested template
as a completed assessment.

Before closing, reconcile the immutable ledger with confirmed scope and the row decisions.
Counts, hashes and a validated revision do not establish coverage or investigation completion.

## 4. Produce and verify the canonical revision

The selected profile/binding rules above take precedence over generic commands. The bundled
`2.0.1` rubric is the sole executable assessment contract, with validator-owned version provenance.
Initialize the requested kind, never substitute unified for an assigned package/component:

```text
<launcher> assessment init --kind unified --root <output> --input <input.confirmed.json> --component <component-id> --output <unified.assessment.json>
<launcher> assessment init --kind package --root <output> --input <input> --output <assessment>
```

Use [ordinary component initialization](report-contract.md#ordinary-component-binding) or the
explicit profile's scoped-context path for a component. Edit a **DRAFT**, then canonicalize to
a **new** file before validation, rendering or revision. Canonicalization checks JSON structure,
not evidence, claims, bindings or completion. Only assessment schema 2 is accepted; unsupported
assessment schemas and rubric identities are rejected, not migrated.

Follow [report and binding rules](report-contract.md) before these commands. Add the selected
component's required binding/feedback flags throughout, not just during initialization:

```text
<launcher> assessment canonicalize --assessment <draft> --output <canonical>
<launcher> assessment validate --root <output> --input <input.confirmed.json> --assessment <assessment.json> --evidence <evidence.json>
<launcher> report render --root <output> --input <input.confirmed.json> --assessment <assessment.json> --evidence <evidence.json> --output <revisions>
<launcher> report verify --root <output> --revision <revisions/0001>
```

For an existing revision's factual correction, use the report owner's
[feedback and corrections](report-contract.md#feedback-and-corrections), not a new unrelated lineage.
After canonical verification, read [the reader contract](partner-preview.md) and produce/verify
the local delivery view. No reacquisition or new testing is required merely to render a reader.
Guidance is separate and requires an explicit request.

## 5. Completion or truthful partial work

Run automatically before completion: `inputs validate` for each confirmed input,
`evidence ledger-validate` for each ledger, and rebuild the chosen input-bound bundle against
the final manifest. Then `assessment validate`, `report verify`, and the applicable reader
verification. Verify the exact ordinary package or scoped-context binding for each component.
Recompute each returned validation-manifest SHA-256; a recorded digest alone is not verification.

Confirm applicable area-specific checks, including every claimed mode and all ten raw-bound
lifecycle dispositions when required. Library reconciliation/indexing and worker acceptance
remain conditional tasks, not requirements for other kinds. Before cleanup, preserve declared
supporting raw outcomes in their approved retained locations; a retained summary does not
substitute for its named logs/results. Check actual files and digests, not an EV1 label, to
determine what survives. Remove only disposable scratch no longer needed by selected claims or
verification. If a declared outcome is unavailable, identify it and qualify the dependent claim.
Keep non-exported raw inputs local according to the reader contract, not in a broadened export.

After verification, resolve every deliverable path in the final response against the approved
output root and confirm it exists. Use the actual nested output locations, not shortened
basenames or intended paths. Do not expose secrets, unrelated private URLs or machine-specific
absolute paths in reports.

If any check fails or requested accessible work remains, preserve valid immutable revisions,
mark the unit blocked/incomplete, and report the exact command, exit code and smallest next
action. Never claim readiness from an unverified report. Separate structural validation from
unresolved factual limitations and organizational approval; positive artifact checks do not
prove factual truth or investigation completion.
