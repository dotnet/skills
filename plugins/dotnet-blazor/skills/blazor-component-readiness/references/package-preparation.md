# Operator-invoked package preparation

`package prepare` collects reusable local facts, a **full** archive inventory and a derived
layout for the existing pinned `authorized-package-48/1.0.0` package profile. It is optional,
operator-invoked preparation, not a prerequisite for publishing a report or an automatic model
invocation. Confirm the existing scoped-package descriptor and raw inputs first using
[typed input candidates](input-candidates.md). The target comes only from that manifest.

```text
<launcher> package prepare --root <approved-root> --input <confirmed-input.json> \
  --output preparation/generation-01 \
  --source-archive <registered-source.tar.gz> --archive-format tar.gz \
  --release-package <registered-release.nupkg> \
  --spdx22 <registered-spdx22.zip> --spdx22-entry spdx_2.2/manifest.spdx.json \
  --spdx30 <registered-spdx30.zip> --spdx30-entry spdx_3.0/manifest.spdx.json \
  --provenance <registered-provenance.json> --sbom-statement <registered-sbom.json>
```

All raw selectors are distinct exact root-level `evidence_inputs` or `owner_inputs`
registrations, not paths discovered by searching. A nested target can preserve the same target
and release basename. Source selection requires `--archive-format tar.gz|zip`; each SPDX role
requires its entry selector, and vice versa. Omitting optional source or release roles is
allowed. Explicit missing, unregistered, unsafe or changed outer inputs are **fatal**, even
when another role is omitted. Missing/malformed inner payloads in valid bindings are attempted
collector failures, not optional omissions or `not-comparable` facts.

## Five operations, not five readiness decisions

| Operation | Behavior |
| --- | --- |
| `input.validate` | Validate current confirmed package/source/scope/selected-role identities; retain exact pre-input bytes. Must succeed to seal. |
| `package.inspect` | Existing package identity inspector; produces package-inspection JSON. |
| `source.inventory` | Existing bounded archive readers, with preparation-local portable-path/collision guards; full original file and directory names/kinds/sizes, no extraction. |
| `source.resolve` | Derive a layout or retain failed/ambiguous root candidates; only depends on successful inventory. |
| `release.collect` | Existing [offline release collector](offline-release-facts.md), when all five release roles are supplied; independent of source outcomes. |

`succeeded` means the collector completed, not readiness verified. Successful release facts
can include adverse `mismatch` and typed `not-comparable` comparisons. `failed` means an actual
attempt failed; it does not establish a package defect. `not-attempted` names missing roles or
the causal failed prerequisite. No operation allows `not-applicable` in this profile.
Unexpected faults and input/retention/seal failures are nonzero; ordinary collector failures
can coexist with independent usable outputs in a sealed receipt.

Exit **0** means the receipt was sealed for **accounting/correspondence only**. Read the printed
outcome counts and referenced diagnostics, not just the exit code. Existing exits remain
1 validation, 2 usage and 3 environment. The command neither changes confirmed inputs nor
assigns statuses, creates assessments, verifies signatures, downloads content or publishes reports.

## Complete inventory and bounded root selection

The full inventory is independent of the chosen prefix. It includes directories, not just
selected/captured source files. TAR reader-visible global metadata entries are counted separately;
`tar_stream_bytes` includes the entire bounded decompressed stream, and `tar_non_file_bytes`
includes headers, local metadata, padding and terminators. BCL TAR traversal can hide local
format headers; the metadata count does **not** claim every physical TAR header was enumerated.
ZIP framing is not reported as expanded file content.

Existing 256 MiB archive/actual-expanded-byte and 100,000 archive-entry limits apply.
Preparation-local paths retain the existing 1,024-character bound, permit at most 64 segments
and 100,000 distinct explicit/implicit path nodes, and reject traversal, unsupported types,
links, file/directory aliases, case aliases and non-NFC Unicode spellings. Serialized sidecars
remain bounded to 64 MiB. Existing manual inventory/capture behavior, its real extracted-root
requirement and the 1,024-file capture selection limit are unchanged.

Only a retained GitHub codeload acquisition whose confirmed repository/full commit match
its locator and **all logical members** recognizes `<repository-name>-<full-commit>/` as a
packaging wrapper. Exactly one wrapper is removed; meaningful `src`/`lib` directories are not
stripped repeatedly. Top-level regular files select the flat prefix `""`. An empty or
directory-only archive is valid inventory, but has no successful source root.

Otherwise, inspect `source.resolve.json`: it retains ordinally sorted covering prefixes and
`root-<sha256>` IDs over the canonical `[archive_sha256,prefix]` pair. Unknown/nested layouts
remain `failed / ambiguous-source-root`. Choose one listed `--source-root-id` in a **new**
generation; never supply a hand-transcribed prefix or edit the layout result. Stale IDs,
subtree-truncating choices and choices conflicting with a known wrapper fail root resolution.
Even then the full inventory and independent release facts remain eligible outputs.

## Retention, sealing and explicit confirmation

The final generation directory is allocated in place, with a persistent adjacent
`<generation>.preparation-lock` reservation. It is never renamed beneath recorded paths.
Every retry requires a new directory; interrupted generations are not resumed or imported.
Preserve the reservation and the generation's exact input snapshot, roles, facts/inventory,
layout, invocation/diagnostic sidecars and registration map. `preparation.receipt.json` is
written atomically **last**, without overwriting any existing file.

Receipt schema 1 binds producer/collector versions, generation, exact package/source/scope/
selection subject, pre-input digest, five fixed operations, actual in-process parameters,
UTC execution times and `{path,size,sha256}` references. Invocation sidecars distinguish
`wrapper_request` from the resolved `call` API and named arguments, plus the producer's exact
`output_path`. Package calls record the manifest's actual target path; archive calls record
the registered archive and expected digest; release calls record exact inner selectors and
the actual `release.collect.json` destination. In-memory layout arguments bind the full inventory
and input by path/digest. Unattempted operations have no call or output path. Dispatch consumes
the resolved scalar call arguments; validation derives the same call and checks the existing
SPDX fact containers against the selected inner entries.
Its strict canonical JSON contract
rejects unknown/missing/duplicate keys, invalid nulls/outcomes/causes and inconsistent bindings.
`input.validate` parameters are recorded before validation and persisted only after identity
succeeds. Other invocation sidecars are persisted before their calls; they are not fabricated
shell-command transcripts. Diagnostics are bounded and explicitly mark truncation.

`registration-map.json` maps **every eligible successful output**, keyed by
`(generation_id,operation_id,output_id)`. Each has an independently retained root-level
`prepare-<generation-id>-<operation-id>.json` copy with original/retained paths, sizes and hashes:

| Output | Permitted evidence kind |
| --- | --- |
| `package.inspect.json` | `package-inspection` |
| `source.inventory.json` | `source-inventory` |
| `release.collect.json` | `offline-release-facts` |

Root context, snapshots, roles, diagnostics, map and receipt are binding dependencies, **not**
successful evidence additions. Failed/partial outputs are never successful registrations.
Source files remain `source_artifacts`; this command captures none.

Choose any subset of the map's successful retained entries. Use `inputs candidates add-evidence
--path <mapped-basename> --kind <mapped-kind>` on the original latest candidates, then ordinary
`inputs discover` and an explicit `inputs confirm` into new files. Keep all original fields and
registrations unchanged; do not hand-edit generated JSON. Validate the receipt and selected subset:

```text
<launcher> inputs validate --root <approved-root> --manifest <new-confirmed-input.json> \
  --preparation preparation/generation-01/preparation.receipt.json
```

The unchanged pre-input is a valid empty subset. Every mapping must still exist and remain
intact, including unselected ones. Validation rejects missing/duplicate/mixed-generation maps,
altered bytes, arbitrary additions, changed originals and failed-output registrations. The
existing combined **32 supplemental evidence/owner files and 64 MiB** includes base entries
plus selected additions. A receipt may map more outputs than available registration capacity;
it does not grant an exception to the final-input ceilings.

Only afterward use the existing identity/draft/ledger/input-bound bundle sequence. Cite exact
fact/comparison IDs or inventory paths, the registered basename and a truthful mechanical
capture time. Never reuse old EV1 IDs after input changes, promote excluded internal facts to
partner obligations, or infer report approval. No report/reader/revision validation or historical
schema is changed here.

Repeated validation/reuse is allowed with intact compatible bindings; there is no one-use or
age gate. Validation recomputes full inventory correspondence and current input hashes. These
are point-in-time local checks, not execution authentication, filesystem locking against an
external writer, origin verification, project closure or semantic proof.
