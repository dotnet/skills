# LP-01 expectation freeze (Phase A only)

The frozen `repository-wide` Licensing and provenance LP-01 row, clause 1.1,
reads **"Uses an OSI-approved, non-copyleft license."** The requirement basis
specifies an OSI-approved license (MIT, Apache-2.0, BSD or equivalent) and
excludes GPL/AGPL for components surfaced from VS. This suite is a
**package-only evidence classification**, not an assertion that the owner's
entire repository, other releases, dependencies (LP-03), or notices (LP-04)
meet the requirement. LP-05 establishes only that PackageLicenseExpression
is populated; LP-01 must independently classify a supported expression.
The output is the existing `CaseResult`, never a readiness verdict or score.

## Frozen policy inputs

The checked `corpus` is the unmodified SPDX
[`json/licenses.json`](https://github.com/spdx/license-list-data/blob/d46e94e2c78ceede1cfc63cfa0396472d2798d4c/json/licenses.json)
at tag `v3.27.0`, commit `d46e94e2c78ceede1cfc63cfa0396472d2798d4c`,
SHA-256 `157789ba91984aecba5c67b84168dbbd41a235b910a7569aa64bf42a88b24290`.
Its 699 license IDs and `isOsiApproved` flags are authoritative for
**OSI-list membership only**, not copyleft status or permission to redistribute
a particular package. The independently pinned, reviewable
`policy/lp01-policy-1.0.0.json` classifies a *narrow* set of OSI-approved
identifiers as non-copyleft, strong copyleft or weak copyleft. These
classifications are explicitly **local policy judgments**, not an SPDX
`isCopyleft` field (none exists in this catalog). Policy version, upstream
version and catalog hash must agree, categories must be disjoint, and every
classified ID must be current and OSI-approved. Anything not classified,
even when OSI-approved, escalates to the policy owner; it cannot silently
inherit a permissive classification. Policy and corpus are checked, pinned
inputs in *each* synthetic and retained case, except the missing-policy and
missing-corpus controls. Missing or inconsistent policy fails closed; a changed file digest
fails at the foundation replay loader before row dispatch. The
`no-corpus`, `policy-version-mismatch`, `policy-overlap` and `catalog-version-mismatch`
controls have pinned, intentionally inconsistent bytes; no online lookup is
necessary or authorized to substitute for them.

SPDX IDs are exact and case-sensitive. Only ASCII space, tab, CR and LF
are accepted as expression whitespace; NBSP and em-space are not silently
normalized. `AND` binds more tightly than `OR`, parentheses group, and
every possible `OR` alternative must consist **entirely** of approved,
explicitly non-copyleft licenses for an *eligible package declaration*.
`MIT OR GPL-3.0-only` is an unresolved choice, not a pass; `MIT AND GPL`
is a package-scoped negative; an OR whose *every* alternative contains
strong/weak copyleft is also negative, including `GPL-3.0-only OR MPL-2.0`.
Weak copyleft is not silently treated as non-copyleft. Non-OSI IDs are
negative when every alternative contains one; mixed alternatives escalate.
Syntactically valid `DocumentRef-other:LicenseRef-local` is a custom
reference and escalates, as do `WITH` exceptions, deprecated IDs/`+`
aliases, unknown IDs and unclassified OSI IDs. Reserved operators cannot
appear as exception identifiers (`MIT WITH OR`, `MIT WITH WITH`); these,
invalid operator casing and incomplete/trailing expressions are malformed
and escalate. No normalization turns deprecated identifiers into current
ones or strips an exception. The oracle caps the UTF-8 expression at
**4096 raw UTF-8 bytes and 4096 raw characters BEFORE any trim or normalization**,
**255 tokens**, **16 parenthesis levels** and **128 expanded alternatives**,
counted before deduplication (128 simple OR operands already use 255 tokens).
Composite `(8 alternatives) AND (16 alternatives)` reaches 128 alternatives
in fewer than 255 tokens; the 8-by-17 control exceeds that separate limit.
ASCII trailing-space byte at/over controls and multibyte at/over controls
show that non-ASCII input at the raw byte cap is invalid syntax, while one
raw UTF-8 byte over the cap is `expression-limit`, even though the character
count is within the cap. Over-limit inputs with malformed syntax prove
**limit-before-syntax** precedence. At/over fixtures for all four
bounds test fail-closed `expression-limit`; limits are expectation-contract
choices for Phase B, not an assertion that the current reader already
enforces them. These decisions are bounded to the pinned catalog and local
policy, not legal opinions.

**Package metadata alone cannot resolve the repository-wide LP-01
obligation.** Every allowlisted expression without an accepted owner record,
including both retained MIT packages, escalates with applicable scope,
explicit missing owner-authorized license grant and release/source
correspondence, and package policy classification in the evidence contract.
Neither a populated expression nor a matching URL removes that missing fact.
Decisive negatives report only that an authoritative *package declaration*
offers no acceptable alternative; they are not repository-wide conclusions.

## Prevalidated owner evidence contract (v1)

The **five checked input roles** are `package`, `policy`, `corpus`,
`owner-record`, and `grant`. Package-only cases omit the last two roles;
this is an *escalation*, not an alternative resolver version. One optional
`owner-record-conflict` role is a fail-closed control: two conflicting
records cannot be selected by order. The foundation hashes each checked
input, with relative paths and lowercase SHA-256, before dispatch. The
owner record is canonical UTF-8 JSON with LF newline, sorted only by the
fixed field insertion order in `owner_record()` (not alphabetically),
no unknown or duplicate keys, and **at most 8192 raw bytes**. Its exact
v1 fields are:

| Field | Required constraint |
|---|---|
| `schema_version` | Integer `1` exactly |
| `caller_accepts_prevalidated_authority` | Boolean `true` for resolution; missing/false escalates |
| `owner_identity`, `owner_reference` | Nonempty, each at most 256 characters; trusted caller-validated identity and reference, not a signature |
| `package_id`, `package_sha256` | Exact checked nuspec ID and checked package digest |
| `repository_url`, `source_commit` | Exact checked nuspec repository URL and full lowercase 40- or 64-hex source commit |
| `release_ref`, `release_version` | Prevalidated full release ref and exact checked nuspec package version; v1 fixture pins `refs/tags/v1.0.0` and `1.0.0` |
| `spdx_grant_expression` | Exact recognized checked package expression (no lossy case/whitespace normalization) |
| `grant_path`, `grant_sha256` | Exact separate checked `grant` role relative path and SHA-256 |
| `grant_evidence_prevalidated_complete` | Boolean `true` for resolution; a tag-only file is insufficient even when asserted true |
| `policy_version`, `policy_sha256` | Exact checked v1 policy version and digest |
| `spdx_version`, `spdx_sha256` | Exact checked SPDX catalog version and digest |

Every string is nonempty and bounded to 256 characters; the record may
not add unrecognized fields or switch versions. The fixture oracle verifies
canonical bytes and all checked cross-input equalities; a production resolver
must reject duplicate JSON keys, unknown fields, wrong types, oversized
documents, missing checked roles, hash mismatches and ambiguous records,
and must **never infer trust from a file path or vendor name**. For v1,
the caller prevalidates owner authority, the repository/release relationship,
and grant completeness, then explicitly accepts that boundary in the record.
The resolver checks the record and separate grant bytes, but **does not
cryptographically establish owner identity, authorization, tag existence,
repository contents, or the release-to-source derivation**. No bespoke vendor
signature is invented. A changed grant byte or path, owner claim, package
digest, repository URL, commit, version, release ref, expression or pinned
policy fails closed. A checked grant has a **16384-byte raw cap** before
interpretation; the over-limit case escalates instead of reading an
unbounded grant. An in-package SPDX identifier alone is not the grant.

`owners/` holds synthetic owner records; `grants/mit-full.txt` is an
**example fixture** with complete MIT permission/conditions/warranty text
and its explicit MIT identifier, not a real vendor grant. The 32 owner cases
include two reachable deterministic positives (`MIT` and `MIT AND MIT`);
their findings explicitly disclose that caller-prevalidated authority was
accepted at the input boundary and was **not cryptographically proven**.
The other cases omit or deny acceptance, mismatch every binding, supply
tag-only/inconsistent/missing grant input, corrupt or oversize a record,
exceed the grant cap, or supply conflicting records. A contradictory package GPL declaration
cannot be rescued by an MIT owner record. These are frozen expectation
branches, **not** production implementation or a statement that the
retained vendors supplied owner evidence.

A URL alone, including `https://licenses.nuget.org/MIT`, is not package
license evidence. A URL beside a recognized expression is not independently
parsed as a license grant, even if its string hints at a different license.
A `type=file` declaration without exact supported in-package evidence
escalates. One synthetic file carries *only*
`SPDX-License-Identifier: MIT`: that tag permits classification of the
identifier but provides **no MIT grant text**, so `file-declared` remains
escalated pending a complete owner-authorized grant and release/source
scope. Full license text interpretation is deliberately **not supported**
by this oracle; a file-only text/filename is no deterministic fallback.
A different synthetic file explicitly declares GPL against
a nuspec MIT expression, so the conflict escalates; filenames and vague
license prose are not a safe conflict detector. No real
retained notice is silently promoted into an SPDX annotation. Shared
nuspec-reader safety and namespace rules take precedence over individual
license facts: malformed XML/DTD, foreign/duplicate metadata, missing or
ambiguous root nuspec and corrupt ZIP escalate. An unavailable package
or a missing/inconsistent policy produces `unknown` applicability.
Unbound/incorrect SHA-256 input paths fail in the shared replay loader;
they are not equivalent to a valid case with no package role.

`cases.json` freezes 121 cases and 120 reproducible, SHA-256-pinned synthetic
archives. `lp01_expectations.py` checks the exact JSON, ZIP bytes, owner
records and separate grant bytes, including
portable entry metadata; `--write-fixtures` only regenerates these
reviewed expectation artifacts. No shipping resolver, registry entry,
runner implemented count or prior LP-05--LP-10/PI-01 fixture changes are
part of this freeze. With this branch's PI-01 tip, coverage remains
**7 implemented / 53 TBD**. The production resolver must later consume
the checked bytes, including pinned policy and catalog, and supply its own
safe bounded ZIP and SPDX parser; the Python oracle is not production code.

## Retained package identities (local opt-in)

| Vendor | SHA-256 | Nuspec SHA-256 | Package policy evidence (LP-01 escalated) |
|---|---|---|---|
| Syncfusion `Syncfusion.Blazor.Toolkit` 2.0.1 | `87cf6795afe5106c9d6c4db5e63e7f0ef0c741a850243557b3bbab1854c6121d` | `e9a55efdfc3385c1df49206839689f3f01c0bf5731b6130f05a601632a77acb7` | MIT is OSI-approved and on local non-copyleft allowlist |
| Infragistics `IgniteUI.Blazor.Lite` 0.3.0 | `e9565c7c7093ff3798b62abdc2e67fe9bc438807eb0fa9b9b43f4573f9df0e63` | `8399959e38391faa853f874f6d4c2d10a2724f3e978af5fc79fbd46a7a74833c` | MIT is OSI-approved and on local non-copyleft allowlist |

Both exact nuspecs declare `<license type="expression">MIT</license>` and a
legacy MIT license URL. The archives contain an MIT-headed packaged license
file; the Infragistics archive also has third-party notices, whose
dependency coverage belongs to LP-03. `retained-inputs.json` pins the
archives, nuspec and notice entry identities, without storing proprietary
bytes or absolute workstation paths in Git. Its source hints are not
identities. The component baseline report is not an LP-01 package verdict.
`acceptance.json` still freezes **two applicable escalations** with independent
MIT classification in `evidence_contract` and an explicit missing owner
grant and release/source correspondence. An owner-authorized license grant
and release-to-repository scope evidence are needed before asserting the
full normative row. A future policy decision must review all remaining
OSI IDs and exception interactions explicitly rather than loosening
fail-closed handling by inference.

Run from the repository root:

```powershell
python eng\blazor-package-resolver\tests\lp01_expectations.py
python -O eng\blazor-package-resolver\tests\lp01_expectations.py
$env:LP01_SYNCFUSION_NUPKG = 'C:\path\to\syncfusion.blazor.toolkit.2.0.1.nupkg'
$env:LP01_INFRAGISTICS_NUPKG = 'C:\path\to\igniteui.blazor.lite.0.3.0.nupkg'
python eng\blazor-package-resolver\tests\lp01_expectations.py --require-retained
python -O eng\blazor-package-resolver\tests\lp01_expectations.py --require-retained
python eng\blazor-package-resolver\tests\lp01_expectations.py --check-negative-bindings
python -O eng\blazor-package-resolver\tests\lp01_expectations.py --check-negative-bindings
```

Default unbound mode prints `skipped` for each vendor; required mode fails
before archive access unless both bindings are present. To replay retained
cases **after Phase B**, copy `acceptance.json`, the pinned `policy/`
files, and verified vendor packages into the same temporary directory
layout. Never check proprietary archives into this repository.
`acceptance-receipt.template.json` keeps byte verification distinct from
future production replay: its replay fields remain null in Phase A.
