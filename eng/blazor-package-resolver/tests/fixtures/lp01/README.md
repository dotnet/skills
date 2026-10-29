# LP-01 expectation freeze (Phase A only)

The frozen `repository-wide` Licensing and provenance LP-01 row, clause 1.1,
reads **"Uses an OSI-approved, non-copyleft license."** The requirement basis
specifies an OSI-approved license (MIT, Apache-2.0, BSD or equivalent) and
excludes GPL/AGPL for components surfaced from VS. This suite is a
**package classification and optional caller-prevalidated owner/repository
evidence contract**, not an assertion that the owner's entire repository,
other releases, dependencies (LP-03), or notices (LP-04) meet the requirement.
LP-05 establishes only that PackageLicenseExpression
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
count is within the cap. The precise precedence is **raw UTF-8 bytes/characters first, allowed ASCII
characters second, tokenization/token cap third, then parsing with depth and
alternative caps at the point reached**. Thus an invalid early character
preempts later token overflow, token overflow preempts a trailing syntax error,
and earlier syntax preempts later depth/alternative overflow; a reached depth
or alternative cap preempts trailing syntax. At/over fixtures for all four
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

The required checked roles are `package`, `policy`, and `corpus`. An optional
single `owner-record` and exactly one `grant-<lowercase-license-id>` role
per distinct license ID in the normalized owner expression permit a positive
branch under **the same resolver version**; absence of the owner record
escalates. A secondary `owner-record-conflict` always conflicts rather than
winning by input order. Unknown, extra, duplicate, or missing roles are
rejected or escalated as appropriate **before conversion to a role dictionary**.
The foundation hashes each checked input, with normalized relative paths and
lowercase SHA-256, before dispatch. No absolute path, `..`, `.` segment,
backslash alias or non-normalized separator is accepted in a checked input
or owner grant binding. `rejected-inputs.json` freezes six shared-loader
rejections separately because invalid input paths/duplicate roles cannot be
replayed as successful `cases.json` cases.

The owner record is canonical UTF-8 JSON without BOM, with LF newline and
the **fixed v1 field order below regardless of parsed input order**, no
duplicate/unknown top-level or nested binding keys, and **at most 8192 raw
bytes**. String scalars and prevalidation references are nonempty, at most
256 characters each, NFC, with no NUL/control characters or unpaired
surrogates. Exact JSON types are required; booleans are not integers.
`prevalidation_references` is an ordered array of at most 32 bounded strings.
The 8192/8193-byte records exercise the document cap while every constituent
field remains within its own limit. Its exact v1 fields, in canonical order:

| Field | Required constraint |
|---|---|
| `schema_version` | Integer `1` exactly |
| `caller_accepts_prevalidated_authority` | Boolean `true` for resolution; missing/false escalates |
| `owner_identity`, `owner_reference` | Nonempty, each at most 256 characters; trusted caller-validated identity and reference, not a signature |
| `package_id`, `package_sha256` | Exact checked nuspec ID and checked package digest |
| `repository_url`, `source_commit` | Exact checked nuspec repository URL and full lowercase 40- or 64-hex source commit |
| `release_ref`, `release_version` | Nonempty `refs/tags/` ref accepted from caller, and exact checked nuspec package version. No independent release-ref truth source is supplied |
| `spdx_grant_expression` | Canonical normalized SPDX expression; must be equivalent to the checked package expression |
| `grant_bindings` | Nonempty, sorted array with one binding for **each** distinct license ID in the expression, including all AND and OR terms; no missing, extra or duplicate binding |
| `prevalidation_references` | Bounded caller-supplied references to the prevalidation, with no claim the resolver verifies their authority |
| `policy_version`, `policy_sha256` | Exact checked v1 policy version and digest |
| `spdx_version`, `spdx_sha256` | Exact checked SPDX catalog version and digest |

Every `grant_bindings` element has **exactly**, in field order:
`license_id` (case-sensitive SPDX ID), `role` (the matching checked
`grant-<lowercase-license-id>`), `path` (matching normalized relative checked
input path), `sha256` (matching lowercase digest of independently checked
bytes), and `caller_accepts_complete_grant` (Boolean `true` for resolution).
An `MIT AND Apache-2.0` declaration with only an MIT grant escalates; an OR
also requires grants for **all** offered license IDs. Normalization is
canonical AST normalization: recursively flatten identical AND/OR operators,
sort child nodes deterministically, deduplicate identical nodes, preserve
AND versus OR and WITH/custom-reference semantics. These operations apply
only to expression **equivalence**, after raw parsing and resource checks.
`MIT AND MIT` needs one MIT binding; reordered/flattened equivalent
expressions match, but changed AND/OR grouping does not. No identifier case
folding or deprecated-license substitution is allowed.

The fixture oracle verifies canonical bytes, structural constraints and
cross-input digests, including exact package SHA-256, nuspec package ID,
repository URL, source commit and release version, policy and SPDX versions
and digests, and each grant role/path/digest. For v1, the **caller**
prevalidates owner authority, the release ref's relation to the checked
source/repository/version, grant applicability and legal sufficiency (including
any added restrictions), and explicitly accepts those assertions in the
record and each grant binding. The resolver **does not cryptographically
establish owner authority, tag existence, repository contents or
release-to-source derivation; it does not legally interpret grant text**.
No bespoke vendor signature is invented. A different well-formed release ref
is therefore a caller-accepted assertion, **not** a detectable mismatch without
a second checked scope source. The checked grant bytes must be nonempty,
UTF-8 without BOM and at most **16384 raw bytes**, with **16384 accepted**
and **16385 rejected** in independent checked-grant controls. Grant declaration
lines use the exact case-sensitive `SPDX-License-Identifier:` marker at the
start of a line, allowing only leading ASCII space/tab, ASCII space/tab after
the colon, and trailing ASCII space/tab. Only LF, CRLF and CR split lines.
No comment prefix is recognized; a line containing a differently formatted
or case-variant marker fails closed as ambiguous. **Every** declaration line
is checked against its binding: any contradictory ID fails closed. Removing
all recognized declaration lines and whitespace must leave nonempty grant
text, so no-space, tab-spaced, repeated and indented tags alone cannot count
as a grant, regardless of newline convention. This is a structural screen,
not proof of completeness or a comparison to one MIT fixture. Untagged
caller-prevalidated text and benign copyright variations can qualify; a
grant whose actual restrictions differ from the record remains the caller's
prevalidation responsibility. The local `.gitattributes` preserves grant
fixture bytes without Git newline conversion, including CR-only and CRLF
controls, so their pinned SHA-256 identities are stable in the commit.

`owners/` holds synthetic owner records; `grants/mit-full.txt` and
`grants/bsd-full.txt` are **example grant fixtures**, not vendor evidence.
The 84 owner cases include 12 reachable deterministic positives (including
multi-license AND and OR, equivalent normalized expressions, a bounded
8192-byte record, an exact 16384-byte complete grant, and benign grant
variation). Their findings explicitly
disclose that caller-prevalidated authority was accepted at the input boundary
and was **not cryptographically proven**. Missing one term, contradictory
grant identifiers, non-equivalent ASTs, unaccepted authority/completeness,
tag-only/inconsistent/missing grant bytes, malformed or oversized canonical
records, invalid paths/roles and conflicting records escalate. Before any
owner record is considered, **the same package-reader classification used by
package-only controls** checks ZIP/root nuspec, XML/DTD, namespace and metadata
ambiguity, direct license type/version/attributes, expression grammar and
resource limits, and explicit packaged license declarations. Owner-bound
controls cover duplicate license and metadata, URL/file license types,
unsupported version/attributes, DTD/entity, foreign metadata, multiple root
nuspecs, contradictory packaged LICENSE.txt, malformed/over-limit expression
and GPL. Their non-positive package branches take precedence over even valid
owner evidence: GPL yields a package-scoped copyleft negative, never a
grant-contradiction or positive. These are
frozen expectation branches, **not** production implementation or a statement
that the retained vendors supplied owner evidence.

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

`cases.json` freezes **178 cases** (94 package and 84 owner) and **177**
reproducible, SHA-256-pinned synthetic archives, alongside six separately
frozen pre-dispatch rejections in `rejected-inputs.json`.
`lp01_expectations.py` checks the exact JSON, ZIP bytes, owner
records and separately checked grant bytes, including
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
