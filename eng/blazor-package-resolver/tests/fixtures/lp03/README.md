# LP-03 dependency-license expectation contract (Phase A)

**Frozen row:** LP-03; repository-wide; Licensing and provenance; clause 1.3.
**Direct obligation:** "All direct and transitive dependencies use OSI-approved,
non-copyleft licenses."

This is the Phase A contract for the proposed
`lp03-dependency-licenses/1.0.0`, not a production resolver, acquisition
implementation, registry change, legal approval or overall readiness verdict.
LP-03 remains `tbd`. The accepted base is LP-02 correction commit
`3db9cf49730748bb70a43311928fc99d4ef040d7`.

## Readiness boundary and supported profile

`verified` requires a complete, exact-version direct/transitive inventory for
**every explicitly assessed target**, exact root and dependency bytes, and
applicable retained license evidence for **every distinct dependency version**.
An inventory, a filename, an SPDX tag, PackageLicenseExpression, an SPDX URL,
or a package-manager success exit code alone is insufficient. A demonstrated
incompatible license supports `gap`. Missing or ambiguous evidence is
unresolved; its absence is not a license classification.

The smallest supported evidence combination is:

1. The checked root nupkg and an independent `assessment` selecting its exact
   ID, version, SHA-256 and nonempty set of target frameworks.
2. A checked **full resolved target graph export**, plus a checked machine-generated
   acquisition receipt binding its bytes, root, exact target set, per-target
   node/edge counts and complete distinct-version package inventory.
3. Every inventoried dependency nupkg, its observed identity and single license
   declaration, its separately retained packaged grant, and a checked,
   **caller-prevalidated applicability review** binding that dependency digest,
   grant digest and license ID to the entire dependency version.
4. The exact accepted LP-01 policy 1.0.0 and SPDX 3.27.0 catalog bytes.

Two trust boundaries are explicit, not disguised as cryptographic proof:

- **Acquisition/closure:** the accepted bounded producer must export the entire
  already-resolved package graph for all requested targets, including direct,
  transitive, build/analyzer and assetless package nodes, without filtering
  down to runtime assemblies. It must observe package identities, license
  declarations and actual packaged grant entries from the checked archives.
  The graph is the complete resolved export, not a caller's selected list.
  Receipt counts, reachability and hashes detect inconsistency, not deliberate
  fabrication or a producer that consistently omits a dependency. Producer
  strings and capture references are not signatures or authority checks.
- **Applicability review:** before admitting a review, the caller validates the
  reviewer's authority, the grant's completeness, and its applicability to the
  **entire exact dependency version**, including bundled material. A packaged
  LICENSE file or metadata declaration cannot establish those facts by itself.
  Review references identify that prevalidation, not network lookups. The
  resolver does not infer a grant from its prose or prove reviewer authority.
  This deliberately reuses LP-01's disclosed prevalidation boundary, not a
  new automatic license scanner or a claim that vendors supplied reviews.

The two accepted boundaries are conditions of a qualified positive. Arbitrary
caller-written captures/reviews are not admissible acquisition or legal
evidence merely because they satisfy the JSON schema. The caller must enforce
admission; the offline classifier cannot authenticate either boundary.
Synthetic records below stipulate these conditions **only for fictional
packages**. They are never evidence about the retained vendors.

The supported graph is `resolved-target-graph/1`, a language-neutral JSON
export of a previously resolved graph. It is **not** `project.assets.json`,
packages.lock.json, a project, a version-range solver, a NuGet dependency
selection algorithm, an SBOM completeness heuristic or a restore emulator.
No arbitrary source format is accepted by renaming it to a graph. A future
bounded acquisition adapter must obtain and preserve this full export and
issue the receipt; Phase A does not implement such an adapter. No `complete`,
`accepted` or `success` boolean substitutes for this evidence.

Acquisition may use package tooling in its own stage. The future resolver
uses only immutable, SHA-256-checked buffers: **no network, restore,
credentials, package-manager execution, environment-selected policy, Git
inspection, code execution or reopening paths**. Existing bounded package
inspection can be reused; do not rebuild ZIP/XML runtimes. Outside-profile
packages and unsupported capture formats escalate, never silently fall back.

## Checked roles, schemas and exact bindings

`cases.json` and `acceptance.json` use the foundation's schema-version-1
replay and `CaseResult` shape. Phase A does **not** run these through the
unimplemented production LP-03 dispatcher. A future replay must compare the
frozen expected results byte-for-byte, not regenerate them from production.

All JSON objects have **exactly** the fields shown below; duplicate and unknown
fields are invalid at every level. Field order is insignificant when reading;
the generator freezes one stable order and ASCII-escaped UTF-8/LF output.
Only integer schema version `1` is supported; booleans are not integers.

| Checked role | Content |
|---|---|
| `package` | Exact assessed root nupkg |
| `assessment` | `{schema_version, root, target_frameworks}` |
| `dependency-graph` | `{schema_version, root, targets, unresolved}` |
| `acquisition-receipt` | `{schema_version, producer, root, graph_sha256, target_frameworks, result, completeness_basis, packages, capture_reference}` |
| `license-reviews` | `{schema_version, reviews}`; absent reviews never create a license conclusion |
| `dependency-NNN` | One nupkg per distinct dependency ID/version |
| `grant-NNN` | Separately retained packaged grant bytes for the corresponding dependency |
| `policy`, `corpus` | Exact byte-pinned LP-01 policy and SPDX catalog |

`root` and every package `identity` are `{id, version, sha256}`. IDs are
canonical lowercase ASCII `[a-z0-9][a-z0-9.-]{0,63}`. Versions are **exact**
three-component decimal strings, each component `0` or 1-5 digits without
leading zero. v1 does not support ranges, floating versions, prereleases,
build metadata, version normalization or inferring the selected version.
TFMs are opaque exact lowercase ASCII `[a-z0-9][a-z0-9.-]{0,63}` names, not
framework compatibility rules. SHA-256 is exactly 64 lowercase hex characters.
Root ID/version must match the inspected root archive, not just another
caller-supplied field. Every dependency identity/declaration and packaged
grant path/digest must match inspection of that exact archive.
Only ASCII ID lowercasing is used when capturing a nuspec identity; license
IDs remain case-sensitive and no general Unicode or version normalization
is implied.

`target_frameworks` in the assessment is authoritative for **assessment scope**.
It must not be inferred from whichever targets were successfully acquired.
The graph, receipt target set and completeness-basis target set must match it
exactly, without duplicates or silently ignored extra targets. A finding
applies only to this explicit target set, not unassessed TFMs or arbitrary
application consumer dependency overrides. RIDs/conditional consumer graphs
are outside v1; they require a new explicit scope/profile, not silent folding.

Each graph target is:

```json
{
  "tfm": "net8.0",
  "direct": ["sample.a/1.0.0"],
  "nodes": [
    {"key": "sample.a/1.0.0", "dependencies": ["sample.b/1.0.0"]},
    {"key": "sample.b/1.0.0", "dependencies": []}
  ]
}
```

Keys are canonical `id/version`, with no unresolved version expressions.
Every direct and transitive edge must name an existing exact node. Every node
must be reachable from `direct`; no orphan inventory, duplicate nodes,
duplicate direct edges or duplicate adjacency edges are allowed. Cycles are
traversed with a visited set, not treated as package-manager resolution.
Within **one target**, two versions of the same ID are unresolved conflicts.
Across different targets, different versions are permitted and **both** must
be inventoried and evidenced. The distinct-version union is sorted by ordinal
ASCII key; its zero-based position assigns `dependency-000`/`grant-000`, etc.
Repeated use of an exact version across TFMs uses one evidence set while
retaining every target's edges. One ID/version cannot name competing digests.
No "highest version wins" or first-target wins rule exists.

`unresolved` is a bounded array of capture diagnostics. It must be empty for
a conclusive result. A zero-dependency finding requires a captured, empty
direct/node graph **for every assessed target** with matching zero counts;
absence of a graph is not a vacuous positive.

The receipt producer is exactly `lp03-resolved-target-capture/1.0.0`.
`result` is `captured` or `unavailable`; unrecognized states are unsupported.
`graph_sha256` binds the entire checked graph. `completeness_basis` is:

```json
{
  "profile": "resolved-target-graph/1",
  "targets": [{"tfm": "net8.0", "node_count": 2, "edge_count": 2}]
}
```

`edge_count` includes root direct edges plus every node's outgoing edges.
Counts alone are insufficient: they must match the retained full graph,
target membership, edge closure, reachability and full package union.
For `unavailable`, the producer emits null graph digest, empty package
inventory and empty basis targets; it cannot represent success. Checked
partial exports may be retained for investigation but remain unresolved.

Each receipt `packages` element is:

```json
{
  "key": "sample.a/1.0.0",
  "identity": {"id": "sample.a", "version": "1.0.0", "sha256": "<64 lowercase hex>"},
  "role": "dependency-000",
  "declared_expression": "MIT",
  "grant": {
    "role": "grant-000",
    "path": "grants/retained.txt",
    "sha256": "<64 lowercase hex>",
    "package_entry": "LICENSE.txt"
  }
}
```

The inventory must equal the graph union, not a superset or subset.
`declared_expression` and `grant` may be null when acquisition lacks that
evidence; that never establishes compatible or incompatible licensing.
`grant.path` and digest bind the separately checked bytes;
`package_entry` and digest bind their actual source within the dependency
archive. v1 selects one inspected packaged grant entry per dependency version;
its safe canonical path is matched **exactly, case-sensitively**, not inferred
from a conventional filename. `LICENSE.txt` above is an example, not a required
name: an actual `licenses/COPYING.txt` entry is equally admissible when its
path, bytes, digest and whole-version applicability review all bind.
No directory scanning, filename guessing or merging multiple grant files is
promised. v1 supports packaged grants only. An external URL, partial excerpt,
tag-only file or caller-named arbitrary text is not a complete grant.
Other evidence sources need a separately reviewed profile.

Each applicability review is:

```json
{
  "key": "sample.a/1.0.0",
  "package_sha256": "<exact dependency digest>",
  "grant_sha256": "<exact retained grant digest>",
  "license_id": "MIT",
  "scope": "entire-dependency-version",
  "basis": "published-package-grant",
  "disposition": "applicable-single-license",
  "reviewer_reference": "reviewer authority prevalidation reference",
  "authority_reference": "grant authority and completeness prevalidation reference",
  "review_reference": "exact-version whole-package applicability prevalidation reference"
}
```

References are mandatory nonblank bounded ASCII strings, not acceptance
booleans. Missing reviews escalate. Duplicate reviews or reviews for a version
outside the inventory conflict; review ordering does not select a winner.
The observed declaration, reviewed license ID and retained grant must agree.
A reviewer cannot override a contradictory declaration to force a positive.
For conclusive classification, disposition is `applicable-single-license`,
basis is `published-package-grant` and the review accepts a complete grant
for the entire dependency version. The classifier only screens supported
structure and checked bindings; grant **sufficiency is caller-prevalidated**.
Fictional negative grants explicitly offer a sole incompatible license by
reference; they do not copy the referenced GPL/LGPL/CC license texts.
Fictional MIT/BSD positives retain complete example grant texts reused from LP-01.

`disposition: unresolved` expresses incomplete applicability research and
routes to `not tested`. The sole owner-only route requires all of:
`license_id`/declaration `LicenseRef-owner-grant`,
`basis: unpublished-owner-grant`, `disposition: owner-grant-required`,
retained packaged evidence that an individual unpublished grant is necessary,
and prevalidated authority/review references establishing that necessity.
The retained file here is evidence of the missing private grant, **not** the
grant itself. Ordinary unavailable public bytes, unknown terms, ambiguity or
failed acquisition must not be mislabeled as requiring an owner.

## Policy, ambiguity and limits

Reuse accepted `lp01-policy-1.0.0.json`, SHA-256
`b395256c51e7c0ae68bf3a213aab90bee0479f6115c3507e456269d0cdd41f47`,
and SPDX license-list 3.27.0, SHA-256
`157789ba91984aecba5c67b84168dbbd41a235b910a7569aa64bf42a88b24290`.
The local copies are byte-identical to LP-01; no live catalog lookup or
runtime policy replacement is authorized. SPDX supplies OSI membership,
not copyleft classification; the finite LP-01 allowlist and strong/weak
copyleft lists are disclosed local policy judgments. No new legal policy
is inferred here. Unclassified OSI IDs, deprecated IDs, unknown IDs and
custom terms are unresolved, not automatically non-OSI gaps.

Only a **single exact current SPDX license ID** is conclusive in v1.
OR/dual choices, AND/conjunctive terms, WITH exceptions, aliases, custom
references and arbitrary license prose all escalate conservatively.
There is deliberately no expression AST or legal interpretation engine.
The one owner-only sentinel above is never a license classification.
Current SPDX IDs explicitly not OSI-approved, or IDs on the accepted
strong/weak copyleft lists, are incompatible **only with applicable retained
evidence**. A conflict between declarations/reviews is unresolved binding
conflict, not a demonstrated incompatible license.

Numerical profile (inclusive bounds):

| Surface | Bound |
|---|---|
| Checked root or dependency archive | 8 MiB each; accepted bounded foundation inspection, no extraction to disk |
| Aggregate checked bytes per case | 64 MiB |
| Each normalized JSON document | 65536 bytes, depth 8, UTF-8 without BOM |
| Targets | 1-4 requested, at most 4 graph/basis targets |
| Distinct dependency versions | 64 across the union and per-target node list |
| Root direct or single-node outgoing edges | 64 |
| All edges per target, including root direct edges | 128 |
| Capture diagnostics/reviews/inventory entries | 64 each |
| Retained grant | 1-16384 bytes, nonblank strict UTF-8 without BOM |
| Ordinary reference, expression or field name | 1-256 printable ASCII characters |
| Checked or packaged grant path | 1-256 characters; canonical safe ASCII relative path |

JSON string decoding has an additional 1024-character ceiling before field
validation. Target/ID limits are 64, package-key limit 128, checked-role limit
64. No booleans, floating-point/nonfinite numbers, controls, non-ASCII JSON
strings or deferred Unicode decoding are supported. Paths use normalized
`/` inside manifests (the foundation convention), never OS-specific aliases,
absolute paths, empty/dot/dot-dot segments or backslashes. Nothing is executed
or extracted. Existing archive safety applies; this contract adds no custom
ZIP codecs, XML evaluation, license-text parser or case-folding machinery.
Oversized/unsupported evidence is `not tested`, not a licensing gap.

## Ordered branches, results and readiness interpretation

The result remains the existing non-scoring `CaseResult`: exact inputs,
`input:role:sha256` references, contract, finding **or** missing fact/input/
next category. `verified`, `gap`, `not tested` and `owner evidence required`
are the **frozen interpretation in `acceptance-manifest.json`**, not new
production result properties or organizational approval.

| Branch | Interpretation | Decision / applicability |
|---|---|---|
| `missing-package` | not tested | escalated / unknown |
| `scope-missing` | not tested | escalated / unknown |
| `evidence-missing` | not tested | escalated / unknown |
| `input-invalid` | not tested | escalated / unknown |
| `policy-unavailable` | not tested | escalated / unknown |
| `binding-conflict` | not tested | escalated / unknown |
| `closure-unresolved` | not tested | escalated / unknown |
| `dependency-bytes-missing` | not tested | escalated / unknown |
| `license-evidence-unresolved` | not tested | escalated / unknown |
| `owner-evidence-required` | owner evidence required | escalated / applicable |
| `incompatible-dependency-license` | gap | deterministic / applicable |
| `dependencies-policy-satisfied` | verified (qualified) | deterministic / applicable |

Pre-dispatch loader failures are **rejections**, never row results: duplicate
roles, unsafe paths, absent pinned files, changed pinned bytes or unknown
input-binding fields. A deliberately omitted role is distinguishable from a
manifest claiming a file that cannot be read. Optional known roles absent
from the manifest may escalate; a bad claimed digest always rejects first.

Ordered evaluation:

1. Shared pinned-input validation; then missing root, then missing assessment.
2. Parse/shape/resource checks for all supplied normalized documents and roles.
3. Exact policy/catalog pins.
4. Inspected root identity/digest versus scope; receipt existence and root binding.
5. Acquisition state; graph existence and exact root/graph digest bindings.
6. Exact target sets, resolved edges, same-target version uniqueness,
   reachability, completeness counts and inventory equality.
7. All dependency identity/declaration/role and actual packaged grant bindings,
   and all review bindings; extra evidence roles conflict.
8. Missing dependency bytes; incomplete/unsupported license evidence; genuinely
   owner-only evidence; demonstrated incompatible license; otherwise positive.

At steps 7-8, validate **all** binding conflicts before choosing a missing
package or license outcome; sorted node traversal cannot hide a later bad
binding. This deliberately conservative profile requires a coherent complete
evidence packet before reporting a gap: an incomplete graph, unavailable
dependency, missing other grant or ambiguous other license takes precedence
over an otherwise evidenced incompatible node. It may defer a real
counterexample for repair, but cannot create a gap from missing information.
All review/binding conflicts remain unresolved rather than selecting whichever
license appears first. No negative finding is emitted on escalations.

## Independent oracle, fixtures and retained roots

There are **50 named expectations total**: 43 synthetic replay cases, 5
pre-dispatch loader rejections and 2 retained-root expectations. Each replay
case's rationale states the normative rule; there is no cross-runtime ZIP,
XML, package-manager or expression-parser differential matrix.
The retained roots cover the `scope-missing` branch; the other 11 branches
have synthetic replay witnesses. The unique-ID and total-count checks include
all three groups, not just replay cases.

`lp03_expectations.py` constructs known deterministic synthetic nupkgs with
literal exact dependency groups, graph exports, receipts, retained grants and
reviews. It records identity/declaration/grant facts **at byte construction**
and uses those facts for oracle comparisons, rather than implementing a
second archive/XML runtime. It is **not a general nupkg classifier** and
cannot classify arbitrary vendor archives. Phase B must use the existing
bounded archive/nuspec reader to check these same inspected-byte facts.
The independently specified branch table is compared with the receipt oracle
and complete expected `CaseResult` objects; no production code is imported or
invoked. Checks use explicit exceptions, not Python `assert`, so optimized
execution retains the same safeguards.

### Discriminating correction to the unaccepted matrix

The initial Phase A matrix at
`b91de23a90490f6f219e3183d5c19d160da498ea` was not accepted: several
bad packets failed earlier redundant checks, and important omitted-evidence
and second-target decisions were not distinguishable. The first correction,
`489e75508834c740aaf191c85dea39541b685f2b`, replaced **17** replay IDs
with **20** new IDs, retained **23**, and changed
no policy classifications, readiness interpretations or production behavior.
The prospective resolver version stays 1.0.0; this is a correction before
Phase A acceptance, not a released resolver upgrade.

The following IDs are retired (all use the `lp03-` prefix):

```text
missing-scope, single-mit-at-grant-limit, direct-transitive,
multi-tfm-shared, direct-gpl, metadata-only, owner-grant,
wrong-root-digest, missing-tfm, unreachable-node, wrong-grant-binding,
wrong-package-binding, wrong-graph-binding, wrong-review-binding,
missing-policy, incomplete-before-gap, binding-before-gap
```

The replacement/addition IDs and the isolated obligations are:

| Case ID suffix | Distinguishing rule |
|---|---|
| `missing-reviews`, `missing-one-review` | Entire review document, or exactly one review, absent with all grants/packages intact |
| `multi-tfm-second-gap`, `multi-tfm-second-unresolved` | Only a dependency unique to target two supplies a gap or missing grant |
| `osi-unclassified` | Current OSI-approved EUPL-1.2 remains outside every finite local classification |
| `deprecated-id` | Deprecated BSD-2-Clause-FreeBSD is unresolved before its non-OSI catalog flag could imply a gap |
| `with-expression` | Do not strip an exception to obtain MIT |
| `archive-declaration-mismatch` | Receipt/review agree with each other, not the actual archive declaration |
| `review-unresolved`, `review-nonaccepting-basis` | Each review acceptance field is independently necessary |
| `review-package-binding` | Only the review's package digest is wrong |
| `packaged-grant-binding` | Retained bytes and all outer digests agree, but not the packaged grant entry |
| `receipt-target-mismatch` | Only the receipt target set differs from scope/graph/basis |
| `wrong-graph-root` | The graph is rehashed correctly but targets different root bytes |
| `dependency-role-index` | Both receipt and checked dependency role are consistently renamed to the wrong index |
| `duplicate-reviews`, `out-of-inventory-review` | Neither identical duplicate nor extra unassessed review may be ignored |
| `unexpected-evidence-role` | An extra valid checked grant role conflicts with the inventory |
| `owner-before-gap` | A genuine owner-only requirement wins over another fully evidenced incompatible node |
| `later-binding-before-unresolved` | First-node missing grant cannot hide a later-node review/package binding conflict |

Retained cases are tightened rather than duplicated: `multi-tfm-versions`
also covers shared transitive-version deduplication, direct/transitive evidence
and the inclusive 16384-byte grant bound. `same-tfm-version-conflict` now has
every package, review and count present, so missing inventory cannot mask
version ambiguity. `incomplete-basis` has otherwise complete GPL evidence
to protect closure-before-gap precedence. `dangling-edge` retains all
existing nodes' reachability, and `unknown-field` uses a valid string value
so an unrelated unsupported JSON scalar cannot mask the unknown-field check.
Missing assessment scope is still exercised against both actual retained
roots when their bindings are supplied. Changed-byte loader controls and
actual retained bindings retain the root-byte pin checks.

Every changed receipt, graph, review and retained input is rehashed. In
particular, the packaged-grant mismatch updates the retained path, checked
digest, receipt digest and review digest together while leaving the actual
dependency archive intact; only the claimed packaged-entry binding fails.

### Final bounded guard-inventory correction

The correction on top of `489e75508834c740aaf191c85dea39541b685f2b`
keeps **exactly the same 43 replay, 5 loader and 2 retained expectations**.
`cases.json`, all 214 checked inputs (576483 bytes), their input manifest,
the loader controls and retained expectations are byte-identical.
No retired replay ID is reintroduced. The only classification-profile change
removes the arbitrary literal `LICENSE.txt` restriction in favor of the actual
inspected source path and digest. Root/dependency archive facts are looked up
by their checked byte digests; digest comparisons and inspected ID/version
comparisons are independent, with unchanged normal classification semantics.

The two-guard follow-up to `8626b46ee47780096750e21476bc4269d86f36b8`
adds only `empty-target-scope` and `capture-producer`, preserving all prior
64 guard registrations and 75 witnesses without changing classification
semantics or any named expectation/input bytes. The first recipe copies the
existing zero-dependencies positive and consistently empties assessment,
graph, receipt and count-target lists. The second copies a complete positive
and changes only the receipt producer to an unaccepted value. Every affected
digest is recomputed. Both normally return `input-invalid`; weakening only
the respective nonempty-scope or accepted-producer guard must instead yield
the wrong `dependencies-policy-satisfied` branch.

`discrimination.json` now freezes **66 acceptance-critical guard IDs** and
**77 registered witnesses**:

- The original **24 weakened variants / 28 existing-case executions** are
  retained unchanged.
- **33 explicit in-memory recipes** copy existing complete packets; together
  with the original probes, **49 distinct weakened variants / 61 executions**
  must reach their prescribed wrong branch.
- **16 existing-case branch witnesses** register remaining already-covered
  branch obligations and an explicit rejected counterexample branch. These
  recheck existing expected results; they are **not** additional weakened
  implementations or newly named replay expectations.

For each weakened probe, the ordinary oracle on that exact packet must first
reach its explicit expected branch; weakening the named guard must then reach
the different, explicit **wrong branch**. Throwing an exception, or falling
through to another `binding-conflict`, does not count. All unrelated outer
checked-input digests and changed graph/receipt/review bindings are recomputed
and passed through the checked-input loader. These copies and any newly
constructed archive observations are used in memory only, then discarded.
The checked fixture files and the normal evidence policy are not changed.

The recipes isolate checked grant path and digest, review grant digest,
actual packaged entry path, graph digest, graph target membership independently
of receipt membership, reachability, missing policy and missing catalog,
dependency digest/ID/version/key, grant-role index, inventory subset/superset/
duplicates, and each owner sentinel prerequisite. The graph-target recipe
changes only target two in the graph and its counts, leaving scope and receipt
targets intact. The unreachable-node recipe retains inventory and evidence
but removes incoming edges, with counts corrected. Duplicate-node counts
describe the distinct-node view so count mismatch cannot mask uniqueness.
Additional fixed probes protect root digest and version, catalog byte pin,
review/declaration agreement, independently supplied assessment scope,
whole-version review scope, unresolved diagnostics and graph node/edge
uniqueness. The positive alternate-filename probe rejects reinstatement of
the removed `LICENSE.txt` restriction.

The three owner probes individually change license sentinel, basis or
disposition while preserving all other bindings and the other dependency's
complete incompatible evidence. Their ordinary result is unresolved; weakening
the individual prerequisite incorrectly yields `owner-evidence-required`.
The missing-policy/catalog weakening deliberately supplies the known pinned
test buffers as an ambient default. The inventory weakening deliberately
collapses duplicates, ignores extras and evaluates only the inventory/graph
intersection. These are explicit **bad implementations**, not authorized
normal fallback behavior. The missing-assessment weakening deliberately
invents scope from the receipt; the ordinary classifier never does so.

The required guard-ID list is independently enumerated, manual and finite.
Every registered ID must have a nonempty list of known, discriminating witness
IDs; unknown/duplicate IDs, orphan witnesses and equal normal/wrong branches
fail. The meta-check removes each of the 66 registrations and separately
empties each witness list: **132/132 omissions must reject**. Removing a
registration therefore cannot silently shrink the required inventory.

The normal and optimized commands run the entire fixed suite. There is no
source rewriting, dynamic evaluation, arbitrary mutation generation/search,
package execution, model evaluation or production mutation. The inventory
records normative guard families, not a second language/runtime grammar or
an exhaustive proof against every possible incorrect implementation.

### Explicit finite guard table

Witness IDs below are the exact IDs in `discrimination.json`; that document
also freezes each rule's description, source case, normal branch, wrong
branch and, when applicable, fixed recipe and weakened rule.
`replay:` uses an unchanged existing packet with a weakened rule;
`memory:` uses one fixed in-memory recipe; `branch:` rechecks an unchanged
case's branch without claiming a weakened-oracle execution.

| Guard ID | Registered witness IDs |
|---|---|
| `optional-reviews` | `replay:optional-reviews:missing-reviews`, `replay:optional-reviews:missing-one-review` |
| `first-target-licenses` | `replay:first-target-licenses:multi-tfm-second-gap`, `replay:first-target-licenses:multi-tfm-second-unresolved` |
| `unclassified-allow` | `replay:unclassified-allow:osi-unclassified` |
| `unclassified-gap` | `replay:unclassified-gap:osi-unclassified` |
| `deprecated-id` | `replay:deprecated-id:deprecated-id` |
| `strip-compound` | `replay:strip-compound:with-expression`, `replay:strip-compound:dual-expression`, `replay:strip-compound:conjunctive-expression` |
| `archive-declaration` | `replay:archive-declaration:archive-declaration-mismatch` |
| `review-disposition` | `replay:review-disposition:review-unresolved` |
| `review-basis` | `replay:review-basis:review-nonaccepting-basis` |
| `review-package` | `replay:review-package:review-package-binding` |
| `packaged-grant` | `replay:packaged-grant:packaged-grant-binding` |
| `receipt-targets` | `replay:receipt-targets:receipt-target-mismatch` |
| `graph-root` | `replay:graph-root:wrong-graph-root` |
| `dependency-role` | `replay:dependency-role:dependency-role-index` |
| `duplicate-reviews` | `replay:duplicate-reviews:duplicate-reviews` |
| `out-of-inventory-review` | `replay:out-of-inventory-review:out-of-inventory-review` |
| `unexpected-role` | `replay:unexpected-role:unexpected-evidence-role` |
| `gap-before-owner` | `replay:gap-before-owner:owner-before-gap` |
| `early-missing-license` | `replay:early-missing-license:later-binding-before-unresolved` |
| `root-identity` | `replay:root-identity:wrong-root-id`, `memory:root-version` |
| `receipt-root` | `replay:receipt-root:wrong-root-version` |
| `completeness-counts` | `replay:completeness-counts:incomplete-basis` |
| `same-target-versions` | `replay:same-target-versions:same-tfm-version-conflict` |
| `policy-pin` | `replay:policy-pin:changed-policy`, `memory:changed-corpus-pin` |
| `checked-grant-path` | `memory:checked-grant-path` |
| `checked-grant-digest` | `memory:checked-grant-digest` |
| `review-grant-digest` | `memory:review-grant-digest` |
| `packaged-entry-path` | `memory:packaged-entry-path` |
| `receipt-graph-digest` | `memory:receipt-graph-digest` |
| `graph-target-set` | `memory:graph-target-set` |
| `reachable-nodes` | `memory:unreachable-node` |
| `policy-required` | `memory:missing-policy` |
| `corpus-required` | `memory:missing-corpus` |
| `dependency-digest` | `memory:dependency-digest` |
| `dependency-identity` | `memory:dependency-id`, `memory:dependency-version` |
| `grant-role` | `memory:grant-role-index` |
| `inventory-equality` | `memory:inventory-missing`, `memory:inventory-extra`, `memory:inventory-duplicate` |
| `owner-license` | `memory:owner-license` |
| `owner-basis` | `memory:owner-basis` |
| `owner-disposition` | `memory:owner-disposition` |
| `any-safe-packaged-entry` | `memory:alternate-packaged-entry` |
| `root-digest` | `memory:root-digest` |
| `dependency-key` | `memory:dependency-key` |
| `review-license` | `memory:review-license` |
| `graph-diagnostics` | `memory:graph-diagnostics` |
| `graph-uniqueness` | `memory:duplicate-node`, `memory:duplicate-direct-edge`, `memory:duplicate-adjacency-edge` |
| `assessment-required` | `memory:missing-assessment` |
| `review-whole-version-scope` | `memory:review-scope` |
| `empty-target-scope` | `memory:empty-target-scope` |
| `capture-producer` | `memory:capture-producer` |
| `root-required` | `branch:root-required` |
| `graph-required` | `branch:graph-required` |
| `capture-required` | `branch:capture-required` |
| `successful-acquisition` | `branch:successful-acquisition` |
| `empty-captured-closure` | `branch:empty-captured-closure` |
| `compatible-union` | `branch:compatible-union` |
| `weak-copyleft-policy` | `branch:weak-copyleft-policy` |
| `non-osi-policy` | `branch:non-osi-policy` |
| `missing-grant-precedence` | `branch:missing-grant-precedence` |
| `unknown-license` | `branch:unknown-license` |
| `dependency-required` | `branch:dependency-required` |
| `edge-closure` | `branch:edge-closure` |
| `duplicate-json-fields` | `branch:duplicate-json-fields` |
| `exact-document-fields` | `branch:exact-document-fields` |
| `safe-grant-paths` | `branch:safe-grant-paths` |
| `grant-resource-limit` | `branch:grant-resource-limit` |

`input-manifest.json` lists every synthetic input's exact byte count/digest.
`acceptance-manifest.json` binds this contract's exact bytes, all manifests,
the discrimination specification, counts and branch/status mapping.
`--write` materializes the **oracle**, never production output. It only removes
obsolete generated inputs previously indexed with unchanged bytes; unindexed
or manually changed inputs fail normal verification. `.gitattributes` preserves
the exact generated bytes through Git newline conversion.

`retained-inputs.json` reuses accepted LP-01 root identities:

| Package | Bytes | SHA-256 | Retained LP-03 result |
|---|---:|---|---|
| Syncfusion.Blazor.Toolkit 2.0.1 | 4912251 | `87cf6795afe5106c9d6c4db5e63e7f0ef0c741a850243557b3bbab1854c6121d` | `scope-missing` / not tested |
| IgniteUI.Blazor.Lite 0.3.0 | 1417667 | `e9565c7c7093ff3798b62abdc2e67fe9bc438807eb0fa9b9b43f4573f9df0e63` | `scope-missing` / not tested |

Only root bytes are pinned; no assessment targets, acquisition, closure,
dependency packages, applicable grants or reviews are asserted for them.
These are frozen expectations, not fresh vendor observations or positives.
No packages are downloaded. Both local `LP03_SYNCFUSION_NUPKG` and
`LP03_INFRAGISTICS_NUPKG` bindings are required for an actual opt-in retained
root check. Missing/partial/misspelled bindings, wrong sizes and wrong digests
fail explicitly; ordinary execution reports retained checks as not run when
both bindings are absent. Negative binding checks for absent, partial,
misspelled, wrong-size and wrong-digest inputs run without vendor files.

From the repository root:

```powershell
python eng\blazor-package-resolver\tests\lp03_expectations.py
python -O eng\blazor-package-resolver\tests\lp03_expectations.py
python eng\blazor-package-resolver\tests\lp03_expectations.py --retained
python -O eng\blazor-package-resolver\tests\lp03_expectations.py --retained
python eng\blazor-package-resolver\tests\lp01_expectations.py
python eng\blazor-package-resolver\tests\lp02_expectations.py
dotnet run --project eng\blazor-package-resolver\tests\ResolverChecks.csproj -- lp02
dotnet run --project eng\blazor-package-resolver\tests\ResolverChecks.csproj
dotnet run --project eng\blazor-package-resolver\BlazorPackageResolver.csproj -- coverage
git diff --check
```

The `--retained` command is optional and must fail if exact local bindings
are unavailable. It checks bytes and the missing-scope result only, not a
vendor license or closure positive.

## Explicit nonclaims and Phase B decisions still open

No legal advice, organizational legal approval or comprehensive
license-compliance opinion; no notice-file/attribution fulfillment (LP-04);
no source correspondence, build reproduction or provenance conclusion
(LP-02/LP-10); no vulnerability, security, maintenance or support conclusion;
no proof that package metadata, producer observations or reviews are legally
correct. The root package's own license is LP-01, not a substitute for LP-03.
No conclusion covers unassessed TFMs/RIDs, private elections, unobserved
consumer graphs or dependencies omitted by a dishonest acquisition producer.

Before Phase B, independently review:

- Which bounded acquisition adapter can produce and preserve the full
  resolved export, and how its provenance/admission is enforced without
  implementing a solver? No supported raw lock/assets adapter is promised.
- Who admits acquisition and applicability reviews, and where independently
  reproducible evidence versus genuinely owner-only facts is recorded?
  Neither trust boundary is solved by hashes or a producer/reviewer string.
- Which accepted package-inspection helper supplies exact identities and
  packaged grant byte bindings under the existing safe archive profile?
- Is the conservative packet-completeness-before-gap precedence appropriate,
  and is a richer per-dependency explanation needed within the existing
  result schema? Changing precedence requires an explicit contract revision.
- Are prerelease versions, RID-specific contexts, compound license choices,
  external grants or cryptographic attestations necessary? They are
  intentionally **not** implied capabilities of v1.

Stop at Phase A. No production handler/registry change, Phase B, LP-04 work,
paid/model evaluation, push or pull request is part of this freeze.
