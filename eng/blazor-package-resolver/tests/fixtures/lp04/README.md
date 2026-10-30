# LP-04 notice preservation contract (Phase A)

**Frozen row:** LP-04; repository-wide; Licensing and provenance; clause 2.12;
classification **decomposition evidence check**.
**Exact check:** "Required third-party license and notice files are preserved."

This freezes the minimum useful language-neutral `notice-coverage-v1` contract
for proposed `lp04-notice-coverage/1.0.0`. It is not a production resolver,
acquisition adapter, registry change, legal approval or overall readiness
verdict. LP-04 remains **tbd**. The accepted parent is LP-03 Phase B commit
`d62478f05df2a520a6d81afca6c289342e6bad06`.

## Readiness boundary and supported profile

License files and detected sidecars prove **only their own presence**.
Package metadata, a LICENSE filename, an SPDX expression, an inventory, and
a package-manager success exit code cannot establish positive notice coverage.

`verified` requires exact root bytes, an explicit scope, complete admitted
exact-version dependency **and** bundled-asset inventories, admitted whole-subject
applicability/obligation reviews with retained evidence, and exhaustive admitted
mappings of **every required part** to inspected package bytes. `not applicable`
requires those same complete inventories and reviews establishing no applicable
preservation surface. Empty inventories must themselves be captured completely.
Missing/incomplete inventories, reviews or mappings remain unresolved, not a gap.

This is a deliberately bounded **exact-byte preservation** profile. Reviews
identify license-text, notice and attribution parts that must be preserved.
The resolver compares their retained bytes with selected full entries or
explicit byte ranges of accepted aggregate entries. It does not interpret
license prose, elect SPDX alternatives, infer legal equivalence, discover
copyright obligations, recognize third-party libraries, resolve dependencies,
reverse minification, unpack nested archives or execute code.

The two separate stages are:

1. **Acquisition/admission (outside the offline resolver).** Preserve full
   already-resolved graphs for every assessed target, exact package identities
   and bytes, all root entry observations and asset provenance, upstream terms,
   whole-subject obligation reviews, and exhaustive notice-location reviews.
   The caller checks producer/reviewer authority, completeness and scope before
   issuing the checked admission record. No adapter is implemented in Phase A.
2. **Offline classification.** Consume only admitted immutable digest-checked
   buffers, compare their structure and bindings, and classify this finite
   profile. No network, restore, credentials, package-manager execution,
   archive extraction, environment-selected policy or reopening input paths.

### Explicit trust and admission protocol

The canonical packet has protocol `notice-coverage-v1`; its admission record
has profile `notice-coverage-admission/1`. It records these separate reviews:

| Admission component | Accepted profile | Caller must check before admitting |
|---|---|---|
| dependencies | `resolved-target-capture/1` | Full resolved export for all selected TFMs, including transitive, build/analyzer and assetless package nodes, without runtime filtering |
| assets | `all-root-entry-capture/1` | Every root archive file entry; first-party/packaging classifications and each bundled third-party component, including generated/minified/bundled JS/CSS/WASM/native material |
| reviews | `whole-subject-obligations/1` | Reviewer authority, retained complete applicable upstream terms, applicability to each entire exact dependency version or bundled component, and every required representation |
| mapping | `exhaustive-notice-mapping/1` | Every obligation part and every acceptable packaged representation/location; explicit acceptance of package-level aggregates; absence claims exclude alternate valid representations |

Each component records `{profile, sha256, reference, authority_reference, state,
aggregates}`. State is `captured` or `unavailable`, never a completeness boolean.
The digest binds the **entire** corresponding checked document, not a subset.
`aggregates` is empty except for mapping; each accepted aggregate is `{path,
sha256}` of an exact full packaged entry. Admission also binds root identity
and assessment digest. Packet and admission digest references must agree with
checked inputs, including null for absent documents.

Producer names, state strings, references and hashes are **not signatures,
authority checks, or proof of truth**. A consistently fabricated complete packet
can satisfy offline structure checks. The caller must enforce this admission
boundary; arbitrary hand-authored records are not admissible merely because
they validate. In particular neither `complete=true`, `required=false`, nor an
invented producer is an alternate admission protocol. The offline result is
qualified on these disclosed prevalidation conditions. Phase A synthetic
records stipulate them only for fictional packages, never for retained vendors.
The resolver does **not** prove the supplied obligation review legally correct.

## Checked roles and canonical schemas

Use the existing checked-input `{role, path, sha256}` convention and `CaseResult`
shape. All file bytes are immutable buffers checked by the shared loader
before classification. Manifest paths use `/`, the foundation's portable
convention; filesystem commands use host-native paths. Field order is not
significant on admission; generators freeze ASCII-escaped UTF-8/LF output.
Every object has exactly the fields below (including nested objects); duplicate
and unknown fields are invalid, not last-wins. Schema version is integer `1`,
never boolean. Null is allowed only at the documented optional bindings.

| Role | Exact fields/content |
|---|---|
| `package` | Exact assessed root nupkg |
| `assessment` | `{schema_version, root, target_frameworks, asset_scope}` |
| `notice-coverage` | `{schema_version, protocol, root, assessment_sha256, dependencies, assets, reviews, mapping, admission_sha256}` |
| `admission` | `{schema_version, profile, root, assessment_sha256, dependencies, assets, reviews, mapping}` |
| `dependency-inventory` | `{schema_version, profile, root, targets, packages, unresolved}` |
| `asset-inventory` | `{schema_version, root, entries, assets}` |
| `obligation-reviews` | `{schema_version, reviews}` |
| `notice-mapping` | `{schema_version, mappings}` |
| `dependency-NNN` | One exact nupkg per distinct dependency ID/version |
| `asset-NNN` | Separately retained exact packaged bytes for each bundled component |
| `reference-NNN` | Retained review basis or exact required preservation-part bytes |
| `entry-NNN` | Retained complete selected packaged notice/license/aggregate entry bytes |

`root` and dependency `identity` are `{id, version, sha256}`. Canonical IDs and
TFMs match `[a-z0-9][a-z0-9.-]{0,63}`. Versions are exact three-component decimal
strings, each component `0` or 1-5 digits without leading zero. No ranges,
prereleases, version normalization or framework compatibility inference.
SHA-256 is exactly 64 lowercase hex characters. Archive observations must
match ID/version and bytes independently; agreement among JSON fields alone
is insufficient. Only ASCII ID lowercasing is contemplated, not Unicode
normalization.

The assessment selects 1-4 distinct exact TFMs and asset scope
`all-root-package-entries`. Scope cannot be inferred from the captures.
This covers all root entries and the dependency-version union of those TFMs,
not arbitrary consumers, other TFMs, RIDs, build closure or repository source.
Changing scope requires a newly bound packet.

### Dependency inventory

Reuse LP-03's `resolved-target-graph/1` semantics, not its license classification
or grants. This normalized inventory is an already-resolved full export,
**not** project.assets.json, a lockfile, a project, SBOM completeness heuristic,
restore emulator, or dependency version solver.

Each target has `{tfm, direct, nodes, node_count, edge_count}`; each node has
`{key, dependencies}`. Keys are canonical `id/version`. Direct/transitive edges
must resolve; every node must be reachable from `direct`. Reject duplicate
nodes/edges, orphan nodes, competing versions of one ID within one TFM and
count discrepancies. Edge counts include root direct edges. Cycles use a
visited set. The exact target multiset must equal assessment TFMs.
`unresolved` contains retained capture diagnostics; it must be empty.

Each package is `{key, identity, role}`. Packages must exactly equal the
distinct-version graph union. Sort keys in ordinal ASCII order to assign
`dependency-000`, etc. Shared versions across TFMs have one evidence/review
set; different versions across TFMs both remain in scope. No highest-version,
first-target or first-review wins. Empty target graphs require explicit zero
counts and a successful full capture.

### Bundled-asset inventory

`entries` inventories **all** root file entries, including nuspec, packaging
files and passive files, as `{path, sha256, kind, subjects}`. Exact path sets
and bytes must match package inspection. Kind is `third-party`, `first-party`,
`packaging`, or `unresolved`. Unresolved provenance cannot become first-party
by default. A third-party entry has nonempty unique subject keys; other kinds
have none. The admission caller, not filename/extension heuristics, establishes
these classifications.

Each asset is `{key, role, package_entry, sha256}`. Keys start with `asset:`;
sort keys ordinally to assign `asset-000`, etc. Asset keys must equal the
third-party entry subject union. Each key occurs on one entry and requires
separately checked bytes matching that exact entry digest. Multiple components
in the same bundle may share the same full entry bytes, with distinct subject
keys and reviews; no component is silently dropped. Per-component extraction,
decoded WASM/native contents, source maps and inferred transforms are outside
v1. If an exact whole-bundle review cannot state supported retained obligations
for each component, applicability remains unresolved. Dependencies also
containing bundled material need whole-version reviews covering that material;
no separate legal inference from LP-03's acceptable-license review is reused.

### Applicability/obligation reviews

Each review has `{subject, sha256, disposition, basis, obligations, owner_fact}`.
Dependency subject keys are `dependency:id/version`; asset keys are `asset:...`.
Subject digest must match its exact dependency archive or bundled entry.
Every subject requires exactly one review; extra or duplicate reviews conflict.
Basis is a separately checked `{role, path, sha256}` reference retaining the
upstream terms and whole-subject applicability/obligation review evidence.
An unreferenced review string is not a retained basis.

Disposition is `obligations`, `no-obligations`, `unresolved`, `unsupported`,
or `owner-private`. `obligations` requires nonempty obligations;
`no-obligations` requires an empty list and admitted retained terms affirmatively
establishing no preservation requirement. Missing reviews never mean none.
An obligation is `{id, parts}`; a part is `{id, kind, evidence}`, where kind is
`license-text`, `notice`, or `attribution` and evidence is the checked reference
to the entire exact required bytes. Obligation IDs are unique per subject,
part IDs unique per obligation. One obligation can require multiple parts;
**every** part must be resolved. Separate obligations can share identical
required bytes. The review identifies completeness and applicability; the
classifier does not infer either from text or an SPDX identifier.

`owner_fact` is null except for `owner-private`, which requires a nonblank
specific private fact, retained basis, no claimed resolved obligations, and
admission prevalidation establishing that only the owner possesses the fact.
The retained basis documents that necessity; it is not the missing private
contract. Ordinary public package/asset/notice inspection, absent source bytes,
unresolved terms, unsupported forms and acquisition failure remain
`not tested`, not owner evidence required. A fabricated owner necessity is
not made authoritative by these syntax checks.

### Exhaustive mapping and exact inspection

Each mapping is `{subject, obligation, part, mode, package_entry, entry, offset,
length}`. The tuple must identify exactly one reviewed part. Duplicate mappings
or extra/out-of-scope parts conflict. Missing mappings remain unresolved.
Each supplied entry reference binds its full separately retained bytes and
the exact inspected packaged path/digest; there is no conventional filename
rule. Entry roles may be reused across mappings, but never silently overridden.

| Mode | Supported evidence |
|---|---|
| `entry` | Offset zero, length equals full retained entry; bytes exactly equal the required part |
| `aggregate` | Explicitly admitted whole aggregate path/digest plus an exact byte offset/length segment equal to the full required part |
| `absent` | Exhaustively reviewed intended path is absent in exact package inspection; null entry, zero offset/length; caller admission excludes alternate acceptable locations |
| `partial` | Offset zero, length equals full retained entry; observed bytes are a strict **nonempty prefix** of the complete required part |

One full entry or aggregate segment can cover several obligations when each
independently binds. One obligation may require several separate entries/ranges;
all must resolve. No substring discovery, line splitting, whitespace
normalization, text decoding transformations, license replacement, block
reordering or inferred equivalence is performed. Byte offsets are UTF-8 **byte**
offsets, not character indices. Accepted aggregate digests bind the whole file.
Unaccepted aggregates, unretained entries, arbitrary modified license text,
non-prefix partial blocks and unsupported transformations remain unresolved.
They are not automatically legal failures. The narrow `partial` mode provides
a reproducible incomplete-representation witness without a license-text engine.

## Safety and finite resource profile

Reuse the accepted production archive integrity/nuspec primitives in Phase B,
not a new ZIP/XML runtime: raw local/central entry identity, flags, CRC/ranges,
pinned casefold aliases, strict encoding, DTD-disabled XML and single root
nuspec identity. Duplicate paths, case/path aliases and unsafe paths reject
the supported profile even if the bytes happen to match. Do not select the
first archive entry or normalize an alias into a valid entry. Outside-profile
Unicode paths, nested archives and ambiguous package metadata remain unresolved.
The Python oracle records these facts at construction; it does not reimplement
that inspector or classify arbitrary vendor archives.

Inclusive limits:

| Surface | Bound |
|---|---|
| Root/dependency archive | 8 MiB each; accepted foundation also bounds 4096 entries, 1 MiB nuspec, and 64 MiB streamed expansion per archive |
| Total checked bytes | 64 MiB |
| Each normalized JSON document | 65536 bytes, root-depth-zero maximum 8 |
| Targets | 1-4 |
| Distinct dependency versions / assets | 64 each |
| Per-target nodes/direct edges/individual adjacency | 64 each; 128 total edges |
| Complete root entry inventory / mappings / reviews | 128 each |
| Obligations per subject / parts per obligation | 8 each; total mapping tuples at most 128 |
| Retained reference/selected entry | 1-16384 bytes, nonblank strict UTF-8 without BOM |
| Separately retained asset | 1 MiB, opaque bytes |
| Paths / ordinary strings / decoded JSON strings | 256 / 256 / 1024 printable ASCII characters |

Identity and obligation token limits above are narrower. No booleans, floats,
nonfinite numbers, controls, non-ASCII JSON strings or deferred Unicode
decoding. Safe paths match `[A-Za-z0-9._-]+` segments joined by `/`; reject
absolute paths, backslashes, empty/dot/parent segments, trailing dot/space
and Windows device basenames. Checked role names are at most 64 characters.
References use `reference-NNN` or `entry-NNN`; indexed roles have three decimal
digits. Unexpected checked roles conflict rather than being ignored.
Limits reject unsupported evidence, never create a preservation gap.

## Ordered branches and status taxonomy

Status is the frozen interpretation in `acceptance-manifest.json`, **not** an
added `CaseResult` property, score, or organizational readiness approval.
Results retain exact input identities, `input:role:sha256` citations, this
qualified evidence contract, and either a finding or missing fact/input/category.

| Branch | Status | Decision / applicability |
|---|---|---|
| `missing-package` | not tested | escalated / unknown |
| `scope-missing` | not tested | escalated / unknown |
| `evidence-missing` | not tested | escalated / unknown |
| `input-invalid` | not tested | escalated / unknown |
| `binding-conflict` | not tested | escalated / unknown |
| `inventories-unresolved` | not tested | escalated / unknown |
| `subject-bytes-missing` | not tested | escalated / unknown |
| `reviews-unresolved` | not tested | escalated / unknown |
| `mapping-unresolved` | not tested | escalated / unknown |
| `owner-evidence-required` | owner evidence required | escalated / applicable |
| `required-representation-missing` | gap | deterministic / applicable |
| `notice-coverage-satisfied` | verified (qualified) | deterministic / applicable |
| `no-applicable-surface` | not applicable | deterministic / not-applicable |

Pre-dispatch loader failures are **rejections**, never row results: duplicate
roles, unsafe binding paths, claimed missing files, digest changes, or extra
binding fields. Omitting a role is different from claiming an unreadable file.

Evaluation order:

1. Shared pinned-input loader; missing root; missing explicit assessment.
2. Shape/resource checks for **all supplied** normalized documents/roles;
   root archive safety and independent root identity/digest.
3. Required packet/admission/inventory presence; all root/scope/document
   bindings; admitted inventory capture state; exact graph closure, scope,
   counts and inventory equality; exact all-entry asset inventory/classification.
4. All subject archive/asset, review and entry bindings, duplicate/extra
   review/part/mapping/role checks. Check **every** supplied binding before
   choosing missing subject bytes or unresolved licensing/mapping.
5. Missing subject bytes; incomplete/unsupported reviews; incomplete/unsupported
   mappings; genuinely owner-private evidence; supported direct missing/partial
   representation; otherwise positive or no applicable obligations.

Inventory completeness precedes representation judgments. Unresolved reviews
or mappings preempt a gap elsewhere, and an owner-private fact preempts a gap.
A later bad binding cannot be hidden by an earlier missing archive. Within
the complete evidence profile, a missing or measured incomplete required
representation is a direct gap; mere absence of acquisition evidence is not.

## Independent oracle, bounded expectations and guard inventory

There are **50 named expectations total**: **43 synthetic replay cases, 5 loader
rejections and 2 optional retained expectations**. Every rationale documents
the protected rule. There are no additional named recipe cases or hidden
input variants. The comprehensive positive includes direct/transitive/shared
multi-TFM versions, a bundled minified asset, one-entry/multi-obligation and
one-obligation/multi-part mappings, and the inclusive 16384-byte text bound.
Dedicated cases isolate a version unique to TFM two, aggregate acceptance,
absent versus incomplete mapping, modified text, and branch precedence.
The acquisition-unavailable witness retains otherwise consistent documents;
capture failure cannot be turned into completeness by their mere presence.
One shared disposition guard handles both unresolved dependency applicability
and unsupported generated-bundle obligation forms.

`lp04_expectations.py` constructs deterministic bounded fictional packages,
records archive identity/entry facts **at byte construction**, and evaluates
only those facts plus the checked normalized packet. Python's ZIP writer
creates fixtures, not a new archive reader. The duplicate fixture replaces
one equal-length generated entry name in writer output; no ZIP parser or
compression implementation is added. No production code/oracle is imported,
and expected branches are explicit normative literals, not production output.
All checks raise explicit errors, remaining active under `python -O`.

`discrimination.json` registers **50 finite acceptance-critical guards and
50 witnesses**, one for each existing named expectation. Replay witnesses
include fixed weakened-rule variants on the **same checked bytes**, not added
cases. Each mutation must reach its prescribed distinct wrong branch, not
merely throw or fail another binding. Nonmutated witnesses are explicitly
marked `existing-branch`, `loader`, or `optional-retained`; they are not counted
as executed mutations. Optional actual retained witnesses are not claimed
executed when local bindings are absent.

The independent `CRITICAL_GUARDS` tuple must match the registration inventory
and every guard needs exactly one witness. **100 omission meta-checks** remove
each registration and each witness in turn and require rejection. Removing
or replacing replay cases cannot silently drop a guard. The complete finite
guard-to-witness table and rule text are retained in `discrimination.json`;
it is acceptance input, not a discovery scan that shrinks with the code.
The acceptance manifest pins this document, oracle, all ledgers, all checked
input digests/counts/bytes and status interpretation.

### Commands and retained roots

From the repository root:

```text
python eng\blazor-package-resolver\tests\lp04_expectations.py
python -O eng\blazor-package-resolver\tests\lp04_expectations.py
python eng\blazor-package-resolver\tests\lp04_expectations.py --mode loaders
python eng\blazor-package-resolver\tests\lp04_expectations.py --mode rejected-inputs
python eng\blazor-package-resolver\tests\lp04_expectations.py --mode bindings
python eng\blazor-package-resolver\tests\lp04_expectations.py --mode discrimination
```

`--write` regenerates only independently specified synthetic expectations.
Normal execution compares every frozen byte and rejects unindexed files.
Do **not** dispatch LP-04 cases through the production registry in Phase A.
Use LP-01/LP-02/LP-03 independent checks, full `ResolverChecks`, and CLI
`coverage` to confirm those accepted handlers and LP-04's `tbd` state remain
unchanged. No workflow or production handler is changed.

The established optional bindings are `LP04_SYNCFUSION_NUPKG` and
`LP04_INFRAGISTICS_NUPKG`, supplied together. `retained-inputs.json` preserves
exact sizes/digests for **Syncfusion.Blazor.Toolkit 2.0.1** and
**IgniteUI.Blazor.Lite 0.3.0** from accepted LP-03. Both frozen expectations are
`scope-missing` / `not tested`: no assessment or checked notice packet is
supplied. Do not infer preservation from a root LICENSE or sidecar. Actual
retained execution checks size/digest on the same buffers before that branch;
the oracle does not pretend to inspect arbitrary vendor archives. No actual
positive or gap is claimed. `--retained` requires both local paths; absent,
partial, misspelled, missing-file, wrong-size and wrong-digest bindings reject.
Negative binding controls run without proprietary files. No downloads.

## Nonclaims and Phase B review questions

No legal advice; no LP-03 license acceptability conclusion; no source/build
correspondence, copyright ownership, vulnerability/support conclusion, proof
that metadata is true, or proof that an obligation review is legally correct.
This package/TFM-scoped decomposition evidence is not repository-wide approval.

Phase B must answer before implementation:

1. Is the disclosed caller admission/authority/completeness boundary sufficient
   for qualified positives, or is an authenticated external admission artifact
   required? A producer string must never silently become proof of authority.
2. Can existing bounded inspection expose exact entry facts safely for all
   selected entries and assets without reopening files or adding an archive
   runtime? Confirm matching path/case/resource and UTF-8 range semantics.
3. Is the narrow exact-entry/accepted-aggregate/strict-prefix profile useful
   enough? Any broader equivalence, transforms, nested assets or obligation
   interpretations need a separately reviewed profile, not silent heuristics.
4. Confirm status and precedence, exhaustive missing-location admission,
   empty-surface proof, multi-TFM union and complete multi-part coverage before
   wiring the registry. Production must not consult fixture IDs, expected
   results, construction facts or this oracle.

Stop at Phase A for independent review. No Phase B handler or acquisition
adapter is authorized by this ledger.
