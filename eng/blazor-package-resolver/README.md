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
are excluded. All 60 package rows are `tbd`, with no
registered handlers. `tbd` is a **work state**, not an evaluation result.

Run from the repository root with the normal SDK in `global.json`:

```text
dotnet run --project eng/blazor-package-resolver/BlazorPackageResolver.csproj -- coverage
dotnet run --project eng/blazor-package-resolver/BlazorPackageResolver.csproj -- replay eng/blazor-package-resolver/tests/fixtures/cases.json
dotnet run --project eng/blazor-package-resolver/tests/ResolverChecks.csproj
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

Each case's `expected` is a structured `CaseResult`: `row_id`, `resolver_id`,
`resolver_version`, stable `branch_id`, `decision` (`deterministic` or `escalated`),
`applicability` (`applicable`, `not-applicable`, or `unknown`), exact `inputs`,
`evidence_references` and/or `evidence_contract`, plus either a factual `finding`
or a specific `missing_fact`, `required_input`, and `next_category`. Branches of
the same future row may take different paths. Deterministic branches need a
finding and evidence; unknown applicability must escalate. A known applicable
branch may also escalate when a named fact/input remains missing. The dispatcher
escalates every unimplemented row as `package-dispatcher`, not as a claimed row
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
for ordinary CI. Neither vendor was replayed in this bootstrap. Old protocols
may inform later row work, but port no old file without an active consumer and a
test. No prior PR or readiness branch is a dependency of this tool.
