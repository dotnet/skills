# Blazor package resolver bootstrap

This standalone, dependency-free .NET CLI is a **non-scoring dispatch foundation**, not
a skill, assessment, readiness authority, or component resolver. The full 112-row
normative rubric and JSON crosswalk/basis are copied byte-for-byte from
`fefdaccbfdc0f34cec3d3ca5b8f7ea3fce4a68e3`, the tip of
`https://github.com/dotnet/skills` branch `shneuvil-public-blazor-readiness`
at copy time. Branch tips can move; the copied bytes remain independently
pinned by the original paths, Git blob IDs and SHA-256 digests in
`data/provenance.json`. The loader checks both digests, crosswalk clauses,
source version and all core scopes. Only the 60 `repository-wide` core rows
enter the registry; the 52 component rows and the separate TA-08 extension
are excluded. LP-01 through LP-03 and LP-05 through LP-10 have deterministic checked-byte
handlers: `lp01-license-policy/1.0.0`, `lp02-public-source/1.0.0`,
`lp03-dependency-licenses/1.0.0`,
`lp05-license-expression/1.0.0`, `lp06-repository-url/1.0.0`,
`lp07-repository-commit/1.0.0`, `lp08-authors/1.0.0`,
`lp09-project-url/1.0.0`, `lp10-binary-attribution/1.0.0`, and
`pi01-strong-name/1.0.0`. The other 50 package rows remain `tbd`.
`tbd` and `implemented` are
**work states**, not evaluation results.

LP-01's independently reviewed Phase A package-license policy
freeze is at `tests/fixtures/lp01/README.md`. It pins SPDX license-list
3.27.0 and an explicit versioned, conservative non-copyleft policy,
198 synthetic cases/197 archives, six separately frozen pre-dispatch input
rejections, a prevalidated caller-accepted owner record with a separately
checked grant per distinct normalized SPDX license ID, and two opt-in retained-package MIT
classifications that **escalate** pending owner grant and release/source
scope evidence. The production resolver replays those exact expectations;
an eligible package expression without an owner record is never a
repository-wide license verdict. Coverage is ten implemented and 50 TBD.
Run `python eng\blazor-package-resolver\tests\lp01_expectations.py`
to check the frozen oracle and policy bytes independently of production.

LP-03's **offline production handler** implements the accepted Phase A contract
at [`tests/fixtures/lp03/README.md`](tests/fixtures/lp03/README.md), frozen at
`a8824050de091f44d90848847c33f499d0db834c`. That historical Phase A ledger,
oracle and all its checked bytes remain unchanged, including its pre-production
status statements. Production consumes checked buffers only; it never consults
that ledger, fixture names, case IDs, expected results or the Python oracle.
The 43 exact replays, five loader rejections, two retained expectations and
214 checked inputs remain the same 50 named expectations.

The handler requires an explicit, nonempty root/TFM assessment, a complete
already-resolved `resolved-target-graph/1` export and
`lp03-resolved-target-capture/1.0.0` receipt, exact dependency archives and
retained packaged grants, caller-prevalidated whole-version applicability
reviews, and the unchanged LP-01 policy 1.0.0 / SPDX 3.27.0 pins. It unions
all selected TFMs, retains different versions across TFMs, rejects competing
versions within one TFM, and assigns dependency/grant indices by ordinal
distinct-version key. All bindings are checked before missing bytes/licensing;
unresolved licensing precedes owner evidence, which precedes a supported gap.
Only a current single catalog ID in the finite allowlist can qualify; custom,
deprecated, compound and unclassified OSI IDs remain unresolved. Inventory
and package declarations alone never create a positive.

Reuse is deliberately low-level: `CheckedInput`, registry digest admission,
`CaseResult` and canonical replay are unchanged. The byte-pinned LP-01 policy
loader and grant-whitespace predicate are shared through visibility-only
changes, not its compound-expression evaluator or owner semantics. ZIP
directory, raw entry identity/alias/range/CRC validation, strict XML encoding
and the DTD-disabled nuspec reader are reused. LP-02's canonical safe-path
predicate is reused, but not its source-specific inspections or archive
materializer. LP-03 retains only observed identities/declarations and the
selected packaged grant digest; no extraction or scan of license prose occurs.
A safe selected packaged path such as `licenses/COPYING.txt` is supported;
there is no special `LICENSE.txt` filename rule.

Limits remain: 8 MiB per root/dependency archive, 64 MiB total checked bytes,
64 KiB normalized JSON with root-depth-zero maximum 8, four TFMs, 64 distinct
versions/per-target nodes, 64 direct/individual adjacency edges and 128 total
edges per target, and 1-16384 strict UTF-8 grant bytes. Existing archive safety
also bounds entries to 4096, nuspecs to 1 MiB and streamed decompression to
64 MiB per archive; it does not materialize every expanded entry. There is no
additional whole-packet decompression budget: at most 65 archives are inspected
sequentially within those finite per-archive limits. Unsupported/malformed
inputs fail closed, not through parser/solver/scanner emulation.

Acquisition completeness and review authority/grant sufficiency are explicit
caller admission trust boundaries, not authenticated by producer strings,
references or hashes. No acquisition adapter is added. There is no network,
restore, credential use, solving, code execution or input-path reopening in
LP-03; no legal approval, notice compliance (LP-04), source correspondence
(LP-02/LP-10), vulnerability/support finding or proof of metadata truth.

`python eng\blazor-package-resolver\tests\lp03_expectations.py` (also `-O`)
checks the unchanged oracle: 49 fixed weakened-rule variants / 61 executions
(original 24/28 preserved), 66 guards / 77 witnesses, and 132 omission
meta-checks. `ResolverChecks -- lp03` independently materializes all 33 fixed
in-memory recipes in C#, executes every registered witness against production,
compares full canonical results, checks the five loaders before dispatch and
tests parser/resource/checked-buffer/culture boundaries. It never calls Python.
The full suite also runs these checks. Optional actual retained runs require
both `LP03_SYNCFUSION_NUPKG` and `LP03_INFRAGISTICS_NUPKG`; exact lengths,
digests and actual nuspec identities are verified from the same buffers.
Both roots still return `scope-missing` / `not tested`, with no positive or gap.
Absent/partial/misspelled/missing-file/wrong-size/wrong-digest bindings are
separately rejected. Supply the corresponding `LP01_*` and `LP02_*` pairs to
include their retained regressions in the full suite.

LP-01 checks archive-wide entry identity using a pinned Unicode 15.0.0
casefold table (generated from Python Unicode 15.0.0 `str.casefold()` for
all changed scalar values; `data\lp01-casefold-15.0.0.json`, SHA-256
`1c1dd710665f23ad059dc9c5c986d48f9cad77c3f58de6cf0cad4590e066e7e7`)
before reading any ZIP entry by name. Its bounded nuspec reader prohibits
DTD/entities before expansion, accepts clean UTF-8 (with or without BOM) and
BOM-marked UTF-16 LE/BE, and rejects unsupported declared encodings.
LP-01 additionally checks raw central/local ZIP names and flags using strict
flagged UTF-8 or unflagged CP437, rejects NUL/control names and casefold
aliases, and streams every entry to verify its declared length and CRC before
inspecting license evidence. License version accepts only two to four ASCII
digit components no greater than 1.0.0.0; grant completeness uses the
frozen Python whitespace semantics, including U+001C--U+001F.
The ZIP validator requires disjoint, contiguous local-header/data/descriptor
ranges from byte zero to the central directory, with the directory ending
exactly at a comment-free EOCD. It rejects Zip64 records and markers,
unsupported flags or compression methods, backslash entry names, malformed
extra-field framing, and Unicode name-remapping extra fields instead of
allowing archive readers to select different package contents.
The 20 bytes preceding the EOCD cannot start with a Zip64 locator signature,
even when those bytes are framed as another field or entry data. Signed and
unsigned data descriptors are checked against the declared CRC and lengths
and the next physical boundary; a CRC equal to the descriptor signature does
not choose the signed form by itself.
Policy 1.0.0 and SPDX 3.27.0 are checked input bytes pinned independently
by their SHA-256 identities, not downloaded or inferred from LP-05.
Only an eligible package declaration with a canonical, checked owner record
and complete, separately checked grants for every normalized license ID
can reach the positive branch. Caller prevalidation accepts owner authority,
grant sufficiency and release/source correspondence; the resolver does not
cryptographically establish authority, inspect the repository or legally
interpret grant text. The 198-case replay, six pre-dispatch loader rejections,
Unicode/encoding boundaries and opt-in retained replays run in ResolverChecks.

Run from the repository root with the normal SDK in `global.json`:

```text
dotnet run --project eng/blazor-package-resolver/BlazorPackageResolver.csproj -- coverage
dotnet run --project eng/blazor-package-resolver/BlazorPackageResolver.csproj -- replay eng/blazor-package-resolver/tests/fixtures/cases.json
dotnet run --project eng/blazor-package-resolver/BlazorPackageResolver.csproj -- replay eng/blazor-package-resolver/tests/fixtures/lp01/cases.json
dotnet run --project eng/blazor-package-resolver/BlazorPackageResolver.csproj -- replay eng/blazor-package-resolver/tests/fixtures/lp02/cases.json
dotnet run --project eng/blazor-package-resolver/BlazorPackageResolver.csproj -- replay eng/blazor-package-resolver/tests/fixtures/lp03/cases.json
dotnet run --project eng/blazor-package-resolver/BlazorPackageResolver.csproj -- replay eng/blazor-package-resolver/tests/fixtures/lp05/cases.json
dotnet run --project eng/blazor-package-resolver/BlazorPackageResolver.csproj -- replay eng/blazor-package-resolver/tests/fixtures/lp06/cases.json
dotnet run --project eng/blazor-package-resolver/BlazorPackageResolver.csproj -- replay eng/blazor-package-resolver/tests/fixtures/lp07/cases.json
dotnet run --project eng/blazor-package-resolver/BlazorPackageResolver.csproj -- replay eng/blazor-package-resolver/tests/fixtures/lp08/cases.json
dotnet run --project eng/blazor-package-resolver/BlazorPackageResolver.csproj -- replay eng/blazor-package-resolver/tests/fixtures/lp09/cases.json
dotnet run --project eng/blazor-package-resolver/BlazorPackageResolver.csproj -- replay eng/blazor-package-resolver/tests/fixtures/lp10/cases.json
dotnet run --project eng/blazor-package-resolver/BlazorPackageResolver.csproj -- replay eng/blazor-package-resolver/tests/fixtures/pi01/cases.json
dotnet run --project eng/blazor-package-resolver/tests/ResolverChecks.csproj
dotnet run --project eng/blazor-package-resolver/tests/ResolverChecks.csproj -- lp03
```

`coverage` emits ordered, canonical UTF-8 JSON with the two source digests and
per-row scope, text, clause, and work state. `replay` reads a `schema_version: 1`
suite with any number of cases; it validates each case's unique ID, rationale,
evidence pointers, and all relative pinned input paths and SHA-256 identities.
Roles can include package, repository snapshot, release assets, workflow, corpus,
and owner record. Conflicting roles, missing files, digest changes, unexpected
fields, and mismatched frozen expected results fail explicitly. The output is
canonical JSON; no readiness status, score, or verdict is emitted.
Each handler receives role-keyed `CheckedInput` entries with the same read-only
bytes used for SHA-256 verification; handlers consume those bytes rather than
reopening a path. Results retain only relative paths, roles, and digests, so
replays do not depend on the checkout location.

LP-02 establishes only qualified public source availability at the exact
package-declared revision. Its offline handler binds the package identity,
GitHub HTTPS repository and exact commit to a machine-generated anonymous
codeload receipt, retained archive, and digests of a library project marker
and implementation file. It does **not** establish that the published
binaries were built from that revision, repository ownership, complete build
closure, reproducibility or readiness. Codeload can serve fork-network commit
objects through the declared repository URL, so successful retrieval also
does not prove membership in that repository's branches or tags; LP-02 does
not perform a membership check. Missing/blocked/not-found/auth-required
and unsupported observations remain unresolved (`not tested`), never
closed-source or a gap. The narrowly scoped, separate `acquire-lp02`
command disables application credentials, cookies, proxies, redirects and
decompression; replay never retrieves anything. The full contract,
acquisition procedure, schema, numerical limits, 33 frozen oracle cases,
15 focused runtime cases (48 total named synthetic cases) and two opt-in retained escalations are in
[`tests/fixtures/lp02/README.md`](tests/fixtures/lp02/README.md).
The accepted base has no readiness skill acquisition infrastructure; this
implementation reuses the existing checked-input/digest/replay and bounded
archive/nuspec surfaces instead of importing a second evidence framework.

LP-05 through LP-09 inspect only the checked package bytes through their shared
nuspec reader. It bounds package and ZIP directory size, entry count,
entry-name length and nuspec read size; rejects
ambiguous or missing root nuspecs, foreign NuGet namespaces and
DTD/entity-bearing XML; and does not extract files. A root-level child whose
local name is exactly `metadata` but whose namespace differs from a recognized
root's namespace makes metadata ambiguous when recognized metadata is also
present, regardless of sibling order. All five rows escalate without a field
finding or field evidence; the frozen cases cite the archive-level ambiguity.
This takes priority over duplicate recognized metadata when a foreign sibling
is present. A case-variant `Metadata` child is not this exact-local-name
ambiguity; zero or duplicate recognized metadata without a foreign sibling
retains its existing invalid-metadata behavior. A nonblank recognized
`license type="expression"` establishes only that `PackageLicenseExpression`
is populated. The supported `type` is case-insensitive; optional `version`
must parse as `System.Version` and not exceed `1.0.0`. XML namespace
declarations are not license metadata attributes; unknown ordinary attributes
escalate. A `licenseUrl` does not affect that field. The frozen 32-case LP-05
synthetic matrix, the opt-in identities of both retained vendor packages,
and replay instructions are in
`tests/fixtures/lp05/README.md`. Runtime-generated boundary checks in
`tests/Lp05BoundaryChecks.cs` exercise each production byte/count/size limit
and malformed ZIP directory/entry data without checked-in giant fixtures.
LP-06 establishes only whether the single recognized direct metadata
`repository` element has a nonblank `url` attribute. It does not validate
URL syntax, reachability, repository correctness, source correspondence or
commit identity. Its frozen 33-case synthetic matrix, the opt-in retained
identities and replay instructions are in `tests/fixtures/lp06/README.md`.
Tests check that repository elements elsewhere in the nuspec do not count,
and exercise the same ZIP/package/nuspec resource limits for LP-06.
LP-07 inspects the single direct recognized `repository` element's `commit`
attribute independently of its URL, type or branch. It reports a declared
nonzero full 40-hex SHA-1 or 64-hex SHA-256 object ID, not object existence,
repository correctness, or package/source correspondence. Its frozen 55-case
synthetic matrix, opt-in retained identities and replay instructions are in
`tests/fixtures/lp07/README.md`. Tests cover ASCII-only hash boundaries,
normalized commit-only duplicate comparison and the same resource limits.
LP-08 reports only whether one direct, recognized nuspec `authors` element
has a nonblank comma-separated entry. It trims outer whitespace but preserves
case, internal whitespace and Unicode; `owners` and malformed/foreign metadata
cannot establish Authors. It does not verify identities, publisher, ownership,
package validity or source correspondence. Its frozen 52-case synthetic matrix
and opt-in retained identities are in `tests/fixtures/lp08/README.md`;
`tests/Lp08BoundaryChecks.cs` adds namespace, Unicode and decision-order
probes, and the shared resource-limit checks exercise LP-08 too.
LP-09 reports whether a single direct, exact-case `projectUrl` element in
the metadata's namespace has nonblank XML-decoded text after outer trim.
It preserves internal text and does not validate URI syntax, scheme,
reachability, source identity or correspondence to the independent LP-06
RepositoryUrl. Case variants, foreign namespaces, duplicates and malformed
element shapes escalate rather than selecting a value. Its frozen 67-case
matrix and opt-in retained identities are in `tests/fixtures/lp09/README.md`;
`tests/Lp09BoundaryChecks.cs` adds decision-priority, namespace, XML text,
Unicode and culture probes. The shared ZIP/package/nuspec resource-limit
checks exercise LP-09 too. `tests/MetadataSiblingChecks.cs` probes
cross-row metadata sibling order, namespace, case, priority, culture and
repeatability without changing the frozen archives.
LP-10 independently inventories every non-manifest packaged entry, using
bounded ZIP/XML reads and magic-first executable detection; it never
executes, evaluates, or recursively unpacks packaged code. It classifies
compiled PE/ELF/Mach-O/WASM, script/JS and active MSBuild or markup text,
and escalates unsafe, opaque, or incomplete inventory before attribution.
Repository metadata, declared dependencies, signatures, PDBs, SourceLink
and SBOMs do not establish ownership or source correspondence. Code
branches require per-entry public release-commit build, exact source blob
or pinned transform, or referenced-package/upstream byte maps; safely
passive-only packages yield a package-specific vacuous no-code finding.
MSBuild inspection joins direct text and CDATA across comments and processing
instructions before testing active functions and hashing locators; prolog
recognition remains bounded and fail-closed. Supplemental oracle-derived
probes cover these boundaries, XML declaration case, Unicode root prefixes
and UTF-8-BOM library XML.
The frozen 174-case matrix, numerical limits and two opt-in retained
identities are documented in `tests/fixtures/lp10/README.md`.
`tests/Lp10BoundaryChecks.cs` independently checks inspected-byte path
independence, culture, uncompressed resource thresholds, and both retained
results when `LP10_SYNCFUSION_NUPKG` and `LP10_INFRAGISTICS_NUPKG` point
to the exact pinned local archives. Both must be set together; proprietary
bytes stay outside the repository.
PI-01 inventories the entire checked package for managed assemblies, bounds
ZIP/manifest/entry reads, and fails closed on opaque containers, native
bundle candidates, malformed metadata, unaccounted PE overlays, or unsupported
certificate profiles. It distinguishes unsigned, delay-signed, public-signed,
cryptographically invalid and verified strong-name signed assemblies; the
positive branch requires an unambiguous complete inventory. Strong-name RSA
verification uses the assembly public key and detects changed signature or
signed PE bytes. Its Authenticode certificate inspection accounts for
certificate-table bytes but does **not** establish publisher identity or
certificate-chain trust (the separate PI-02 requirement). Complete metadata
table/signature/CustomAttribute decoding and full assembly-qualified enum
identity checks precede decisive signing states; untrusted packaged code is
never executed or loaded. Valid managed modules with no Assembly table are
non-assemblies; malformed modules or modules claiming strong-name signing
cannot establish a complete inventory. ZIP names require flagged valid UTF-8
or unflagged ASCII with matching local and central records. PI-01 alias checks
fail closed for unsupported Unicode folds, and its evidence uses Python-style
lowercase ASCII escapes and Unicode scalar path ordering without changing
other rows' canonical JSON. The 138-case frozen matrix, evidence contracts and
opt-in retained identities are in `tests/fixtures/pi01/README.md`.
`tests/Pi01BoundaryChecks.cs` checks the package-only byte/count thresholds,
culture and checked-buffer path independence; when both `PI01_SYNCFUSION_NUPKG`
and `PI01_INFRAGISTICS_NUPKG` bind the exact local archives it also verifies
their frozen results. Unsupported external enum identities require additional
evidence rather than an inferred backing type.
Portable retained-package replay identities and result hashes are in each
row's `tests/fixtures/lp05/acceptance-results.json` and
`tests/fixtures/lp06/acceptance-results.json` and
`tests/fixtures/lp07/acceptance-results.json` and
`tests/fixtures/lp08/acceptance-results.json` and
`tests/fixtures/lp09/acceptance-results.json`. LP-10 has frozen
`tests/fixtures/lp10/acceptance.json` outcomes but no committed
acceptance receipt. The real-package
acceptance replays are opt-in, separate from ordinary CI and from any
readiness decision. LP-02 separately retains both declared-source-unverified
expectations until an actual anonymous capture and bounded source mapping
are supplied.

Each case's `expected` is a structured `CaseResult`: `row_id`, `resolver_id`,
`resolver_version`, stable `branch_id`, `decision` (`deterministic` or `escalated`),
`applicability` (`applicable`, `not-applicable`, or `unknown`), exact `inputs`,
`evidence_references` and/or `evidence_contract`, plus either a factual `finding`
or a specific `missing_fact`, `required_input`, and `next_category`. Branches of
the same future row may take different paths. Deterministic branches need a
finding and evidence; unknown applicability must escalate. A known applicable
branch may also escalate when a named fact/input remains missing. The dispatcher
escalates the other 51 unimplemented rows as `package-dispatcher`, not as a claimed row
resolver. Registry construction rejects missing/extra IDs, component or extension
IDs, handlers without registrations, implementation declarations without matching
handlers, and handlers on TBD rows.

## Sequential row workflow

Freeze reviewed case expectations and pinned input digests **before** implementing
one row. Add synthetic applicable, not-applicable, unknown, missing, and conflict
branches as relevant. Implement and register that row's handler only after its
expectations are fixed; rerun the replay and tests. A mismatch is a failing
contract, **not** permission to update expectation and implementation together.
Review an expectation change independently and explain its source evidence.
When separately supplied, real Syncfusion **and** Infragistics pinned acceptance
inputs must replay for that row; commercial inputs are opt-in and never required
for ordinary CI. LP-01, LP-05 through LP-10 and PI-01 were replayed against both exact retained
packages; local detailed receipts and proprietary package bytes are not
shipped with the repository. Old protocols may inform later row work, but
port no old file without an active consumer and a test. No prior PR or
readiness branch is a dependency of this tool.
