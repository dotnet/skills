# Deterministic discovery candidates

Use the bundled validator's `inputs candidates` commands to construct discovery input.
Supply observed facts as options; do not write candidate JSON, copy a confirmed manifest
back into discovery, add `content_sha256`, or generate your own candidate-building script.
The commands own the schema, object keys and field order. `inputs discover` computes
digests from retained bytes and applies the existing completeness and semantic checks.

Resolve `<launcher>` using the loaded skill. Full options are available with:

```text
<launcher> inputs candidates --help
<launcher> inputs candidates add-document --help
```

Start with the actual acquisition and source facts. For example, this fragment records a
published package acquired via the documented fallback and a source mapping not yet resolved:

```text
<launcher> inputs candidates init --acquisition published --package-locator https://www.nuget.org/api/v2/package/Example.Widgets/1.0.0 --package-method package-feed --source-availability unresolved --output candidates-00.json
<launcher> inputs candidates add-document --input candidates-00.json --url https://example.test/widgets/docs --path acquisition/docs.html --output candidates-01.json
<launcher> inputs candidates add-retrieval --input candidates-01.json --subject package --locator https://www.nuget.org/api/v2/package/Example.Widgets/1.0.0 --method package-feed --result succeeded --output candidates-02.json
```

This is a construction example, not a complete assessment or evidence that those example
resources exist. Replace example values with the actual facts. At initialization, use
`--repository-uri`, `--source-commit`, `--source-mapping`, and `--source-confidence`
when supported by the discovered source mapping. Do not infer closed source from a failed fetch.

Read `inputs candidates --help` before constructing inputs. It exposes the existing finite
choices enforced by both construction and discovery, including retrieval subjects/results,
source and owner provenance fields, render modes, and lifecycle tokens. Free-form evidence
kinds and exclusion subjects remain descriptive text. Construction checks token validity,
not whole-input completeness, and preserves the original valid values.

Before constructing a scoped component input, compare the complete source record with its
[bound package context](scoped-component-profile.md), including mapping
and confidence. Rewording mapping prose changes that identity. Carry forward an unchanged,
still-true source record and record new reuse/acquisition details separately; do not copy a
materially false mapping merely to satisfy equality.

Canonical documentation/retrieval URLs exclude queries and fragments. Preserve the actual
requested URL, final URL, and returned bytes in acquisition records. Do not strip a query from
an earlier fetch and relabel its content. If a permitted query-free URL is available, retrieve
it as a separate attempt and retain any redirect relationship; otherwise report the locator
limitation rather than editing generated candidates or weakening the grammar.

`--package-method` uses the existing package-origin subset of `add-retrieval --method`;
the generated help lists both. Repository fetching is not a package-origin method.
NuGet v3 and v2 both use `package-feed`;
retain their distinct URLs, results, and fallback details. HTTP downloads of source archives,
documentation, or release metadata use `direct-download`, not provider-specific labels.
Git retrieval uses `repository-fetch`. Do not invent method names or treat public downloads
as `owner-supplied`. Invalid method values fail before any candidate is written.

Each add command reads the previous candidate file and writes a **new** file. Continue
from the latest output; preserve the earlier file. Paths embedded in candidates are relative
to the later `inputs discover --root`, not relative to the candidate file itself.
Quote shell arguments containing whitespace. The code does not retrieve files or establish
that a locator is official; it records the supplied facts without inventing provenance.

PowerShell orchestration should invoke the resolved launcher explicitly or use an unambiguous
helper name such as `Invoke-Readiness`; aliases can take precedence over a short function name.
Capture the CLI exit code before another command changes it. CLI completion and writing a
separate operation log are different outcomes: a failed log write does not prove that the CLI
failed or that no output exists. Preserve any created output, inspect its actual validity and
identity, and write new command receipts rather than repeatedly rewriting a growing log or
retrying against an existing candidate path.

Use the relevant typed commands for the rest of the discovered input closure:

| Command | Facts recorded |
| --- | --- |
| `add-document` | Official URL and retained local content path. |
| `add-package-source` | Source kind, locator and retained path, such as extracted metadata or README. |
| `add-source-artifact` | Repository-relative source path and retained local path. |
| `add-retrieval` | Actual subject, locator, method, result and optional detail, including failed attempts. |
| `add-evidence` | Supplemental evidence filename directly under the later `--root`, and its descriptive kind. |
| `add-owner-input` | Actual owner-input filename directly under the later `--root`, and provenance; public downloads are not owner attestations. |
| `add-component` | Confirmed component identity, modes, source paths and explicit lifecycle applicability. |
| `add-exclusion` | Explicit subject and rationale. |

`add-component` accepts repeated `--mode`, `--source-path`, and `--lifecycle-trigger`
options. Only record real confirmed components when in scope; do not invent a component
to satisfy validation. Empty collections in a newly initialized candidate mean no records
have been added yet, not an owner-confirmed absence or an applicability decision.
For package-only scope, omit `add-component`; discovery and confirmation accept an empty selected
component list. It does not claim the package contains no controls. Package/source/evidence
requirements still apply, and full-library inventories still require confirmed components.

Retain supplemental evidence and owner files at the root-level filenames you register:
`add-evidence --path release-metadata.json`, not `--path acquisition/release-metadata.json`.
Do not strip directories from a recorded path unless those exact bytes are actually retained
at the registered root-level filename. The builder does not copy, relocate or overwrite files.
Documentation, package-source and source-artifact content paths may remain nested relative
paths. Construction validates the existing string grammar without changing valid original
values; file existence, hashes and whole-input completeness remain discovery checks.

After the complete candidate record and owner confirmation required by the skill:

```text
<launcher> inputs discover --root <output> --nupkg <exact.nupkg> --candidates <latest-candidates.json> --output <input.draft.json>
<launcher> inputs confirm --root <output> --draft <input.draft.json> --output <input.confirmed.json>
<launcher> inputs validate --root <output> --manifest <input.confirmed.json>
```

Successful construction proves the serialized input shape, not source mapping truth,
completeness, owner confirmation or report readiness. Unknown options, malformed existing
candidate records and existing output paths are errors; do not strip fields or hand-repair
the generated files after an error. The existing raw-candidate discovery parser remains
strict and is not a substitute for the construction commands in ordinary assessment runs.

Before authoring evidence drafts, read `<launcher> evidence draft-add --help` for the
existing provenance kinds, locator grammars, and typed options. Supply actual observed
claims and inspection/capture facts; never invent provenance metadata.
Evidence drafts are not canonical ledgers, and their logical locators are not filesystem paths.
For ordinary authoring, use the typed producer rather than transcribing computed hashes into JSON:

For a new capture, obtain UTC mechanically at the capture step and pass it explicitly in the
same shell/block. These Bash examples run in the shell where you defined `readiness` using
SKILL.md; a later tool call does not inherit that function. For PowerShell, use the shipped
launcher with `$SkillDir` and scratch configured by SKILL.md, not a `readiness` function.

Bash:

```bash
CAPTURED_AT="$(date -u '+%Y-%m-%dT%H:%M:%SZ')" &&
readiness evidence draft-add --output evidence-draft-01.json \
  --claim "<observed atomic claim>" --scope repository-wide \
  --kind package-artifact-metadata --locator package:whole-nupkg \
  --method "<actual capture method>" --captured-at "$CAPTURED_AT" --nupkg "<exact.nupkg>"
```

PowerShell:

```powershell
$CapturedAt = [DateTimeOffset]::UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", [System.Globalization.CultureInfo]::InvariantCulture)
& (Join-Path $SkillDir "scripts/validator/run-validator.ps1") evidence draft-add `
  --output evidence-draft-01.json --claim "<observed atomic claim>" --scope repository-wide `
  --kind package-artifact-metadata --locator package:whole-nupkg `
  --method "<actual capture method>" --captured-at $CapturedAt --nupkg "<exact.nupkg>"
```

When recording an actual earlier capture, use its already-recorded canonical UTC instant
instead of obtaining a new clock value. Do not substitute a run-start value, file mtime,
guessed time or backfill. Timestamp validation checks format, not the truth of capture metadata.
Repeating the same evidence command into a new output with the same explicit capture value
remains deterministic; do not obtain a new instant merely to repeat that recorded capture.

`draft-add` computes the digest itself and accepts no hash override. Package artifact
records require `--nupkg`; other existing provenance kinds support the manual `--content
<retained regular file>` mode with an explicit logical locator. These modes are mutually exclusive. Retained content is
bounded by the existing 64 MiB supplemental-input limit, while the draft JSON remains
bounded by the existing 4 MiB authored-ledger limit. The existing nine kinds and
locator grammars are shown by command help. `--input` accepts any valid existing draft
through the existing parser normalization and never rewrites that original file;
invalid records are rejected rather than repaired. Supply claims, scope/component,
kind, logical locator, method, capture time, and any supersession IDs explicitly. The
output is an untrusted draft, not evidence validation or readiness. Existing valid
manual drafts remain supported by `ledger-build`; the producer does not replace
their existing integrity or binding checks.

For reviewer-generated analysis of a registered artifact, use the registered mode instead.
At this capture step, the derived result must already exist and be retained, registered and
explicitly confirmed in the final manifest. Only then obtain this capture's UTC value and invoke
the producer in the same shell/block; do not precompute it before generating the result.

Bash (in the defining shell):

```bash
CAPTURED_AT="$(date -u '+%Y-%m-%dT%H:%M:%SZ')" &&
readiness evidence draft-add --output "<new-evidence-draft.json>" \
  --claim "<specific observed fact>" --scope repository-wide --kind reviewer-generated-analysis \
  --root "<output>" --manifest "<final.confirmed.json>" --evidence-input "<result-basename>" \
  --method "<actual mechanical capture method>" --captured-at "$CAPTURED_AT"
```

PowerShell:

```powershell
$CapturedAt = [DateTimeOffset]::UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", [System.Globalization.CultureInfo]::InvariantCulture)
& (Join-Path $SkillDir "scripts/validator/run-validator.ps1") evidence draft-add `
  --output "<new-evidence-draft.json>" --claim "<specific observed fact>" `
  --scope repository-wide --kind reviewer-generated-analysis `
  --root "<output>" --manifest "<final.confirmed.json>" --evidence-input "<result-basename>" `
  --method "<actual mechanical capture method>" --captured-at $CapturedAt
```

All three registration options are required together. The producer validates the confirmed
manifest and current files, then derives locator and digest from that exact `evidence_inputs`
registration. Do not supply `--locator`, `--content` or `--nupkg`; a producer name/version is
not the retained result's basename. This mode does not infer provenance from the registration
kind, accept owner-input substitutions, or discover/confirm inputs. Claims, method and capture
instant remain explicit. `--input` still means the previous evidence draft for immutable append.

## Post-output confirmation

If an authorized collection produced a result after confirmation, preserve its original manifest
and bytes. Use `inputs candidates add-evidence` on the retained latest candidates, write a new
candidate, `inputs discover` a new draft and explicitly `inputs confirm` a new final manifest.
Keep all existing approved registrations; add only the authorized derived result. Do not reuse
an old identity or mutate a manifest to resolve stale bindings.

## Canonical identity and ledger sequence

For an explicitly selected scoped component, read [the complete profile](scoped-component-profile.md)
before initialization: it uses `--package-context-revision` and, when bound,
`--package-context-feedback`, not the ordinary package binding below.
An authorized identity-only handoff may retain an unscored skeleton; it does not require
row scoring, assessment validation or report/reader production.

Initialize the assessment from the final, post-output confirmed inputs before building evidence ledgers. Export
its identity through the validator; do not hand-author hashes/IDs, extract JSON with a
generic serializer, or reformat the exported file. `assessment canonicalize` emits the
full assessment document, not the standalone identity required by evidence commands.

For a package-only assessment:

```text
<launcher> assessment init --kind package --root <output> --input <input.confirmed.json> --output <assessment.json>
<launcher> assessment export-identity --assessment <assessment.json> --output <assessment-identity.json>
<launcher> evidence ledger-build --kind repository --subject <assessment-identity.json> --draft <evidence-draft.json> --nupkg <exact.nupkg> --output <repository-ledger.json>
<launcher> evidence ledger-validate --ledger <repository-ledger.json>
<launcher> evidence bundle --assessment <assessment-identity.json> --source-ledger <repository-ledger.json> --ids <EV1-id,...> --root <output> --manifest <input.confirmed.json> --output <evidence.json>
```

Completed handoffs require this input-bound bundle mode. It checks current file bytes, exact
manifest/package/component identity and the existing binding rule for every selected record.
It neither scores rows nor changes the input set. An authorized unscored identity-only handoff
does not need invented row findings or report rendering. Existing structural-only bundling
(no root/manifest) and `ledger-validate` remain supported but do not accept input linkage.
Partial bound options or failed binding never fall back to structural success. Rebuild identities,
ledgers and selected bundles using the producers when inputs change; retain the existing EV1
algorithm rather than forcing every identifier to change or copying old identifiers blindly.

### Passing selected evidence IDs without retyping

For a single validated, immutable ledger, this optional Bash/`jq` example separates assessor
selection from ID transport. Run the blocks in the same shell, using the `readiness` function
from SKILL.md. Inspect the listing's zero-based index, ID, claim, kind, and locator; choose
records for their actual relevance, not their position alone. New output paths are required.

```bash
set -euo pipefail
set -o noclobber
LEDGER="<retained-ledger.json>"
LISTING="<disposable-ledger-listing.tsv>"
if ! command -v jq >/dev/null 2>&1; then
  printf '%s\n' "optional ledger projection skipped: jq is unavailable" >&2
  exit 2
fi
jq -r '.records | to_entries[] | [.key, .value.stable_id, .value.claim, .value.provenance.kind, .value.provenance.locator] | @tsv' "$LEDGER" > "$LISTING"
```

Read the listing in bounded ranges. The following consumes the assessor's explicit indexes
against that same ledger and assigns the generated IDs to one explicitly chosen requirement
in a new untrusted draft. Replace `[1,0]` and the requirement placeholder with your selection;
they are examples, not defaults. Do not reuse indexes with a different or rebuilt ledger.

```bash
INDEXES_JSON='[1,0]'
ROW_ID="<requirement-id>"
IDS_CSV=$(jq -r --argjson indexes "$INDEXES_JSON" '
  .records as $records |
  if ($indexes | type) != "array" or ($indexes | length) == 0 then error("nonempty index array required")
  elif any($indexes[]; type != "number") then error("numeric indexes required")
  elif any($indexes[]; . != floor or . < 0 or . >= ($records | length)) then error("integer index out of range")
  elif ($indexes | unique | length) != ($indexes | length) then error("duplicate index")
  else [$indexes[] as $index | $records[$index].stable_id] | join(",")
  end' "$LEDGER")
jq --arg ids "$IDS_CSV" --arg row "$ROW_ID" '
  if ([.rows[] | select(.id == $row)] | length) != 1 then error("requirement must resolve exactly once")
  else .rows |= map(if .id == $row then .evidence_ids = ($ids | split(",")) else . end)
  end' \
  "<assessment-draft.json>" > "<assessment-with-selected-ids.json>"
# Structural-only ID transport demonstration, not accepted input linkage.
readiness evidence bundle --assessment "<assessment-identity.json>" \
  --source-ledger "$LEDGER" --ids "$IDS_CSV" --output "<new-evidence.json>"
```

The listing and row draft are disposable inspection/authoring outputs, not replacements for the
machine-owned ledger or canonical assessment. This is not automatic select-all: selection remains
assessor-owned. Stop on missing, ambiguous, malformed, duplicate, or incompatible records; do not
fuzzy-match claims, repair/truncate IDs, deduplicate selections, sort bundle selection, or infer
provenance. This example selects evidence for one row, not every row automatically. For additional
rows, explicitly select the relevant records and read their IDs from the same ledger rather than
typing hash strings. Include every intentionally used ID in the explicit bundle selection.
Preserve its order; use ordinary `assessment canonicalize` for ordinal row/finding/summary
reference ordering, then validate the final assessment and bundle. The draft above changes no
statuses or conclusions and is not a completed assessment. The demonstration's structural-only
bundle is not a completed handoff either: rebuild the chosen selection with
`--root <output> --manifest <final-confirmed-input>` as in the normal bound sequence above.

If `jq` is unavailable, record that this optional example could not run; do not install it or
claim the handoff succeeded. Preserve any partial output on failure and follow the run's recovery
policy. Canonical ledgers, identities, and accepted reports remain unchanged.

For unified or component scope, use that scope's existing `assessment init` options
(including the selected component and required ordinary package or scoped-context revision), then export its identity
the same way. A component ledger uses:

```text
<launcher> evidence ledger-build --kind component --subject <assessment-identity.json> --draft <evidence-draft.json> --nupkg <exact.nupkg> --output <component-ledger.json>
```

Existing ledger-kind and scope restrictions still apply. A package assessment has no
component identity and cannot produce a component ledger. Reuse the exported identity
for bundle construction; select actual ledger record IDs. Export is only a deterministic
projection of an existing canonical assessment, not validation of its evidence, bindings,
or readiness. For scored canonical assessments, assessment validation and report verification
remain mandatory; the explicitly authorized unscored identity-only handoff is not a report.
