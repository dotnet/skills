# Status boundaries

Use the boundary labels below in working notes and explanations to force an explicit evidence-state
decision. New assessments use schema version 2. Schema version 1 is retained only for immutable
legacy verification; neither schema stores a duplicate boundary token.

| Status | Boundary label | Required boundary |
|---|---|---|
| `verified` | `direct-evidence-satisfies` | Direct applicable source, exact artifact/configuration, owner attestation, or reproduced behavior satisfies the row. Cite selected evidence. |
| `gap` | `direct-evidence-conflicts` | Direct evidence shows the product, artifact, required documentation, behavior, or required public control conflicts with the row. Cite selected evidence. |
| `owner evidence required` | `owner-held-evidence-only` | The unresolved fact requires an owner-held private record, attestation, approval, definition, or inaccessible control. State the requested evidence. |
| `not tested` | `applicable-evidence-not-obtained` | The row applies and is independently testable, but the required acquisition, probe, or evidence was blocked, not run, or insufficient. State the blocker and smallest next probe. |
| `not applicable` | `confirmed-not-applicable` | The row genuinely has no surface in the confirmed deliverable or explicit support claims. Give the direct applicability rationale; unknown is not inapplicable. |

## Decision order

1. Establish applicability from the confirmed deliverable and claims. A conditional `when claimed`
   or `when supported` row is `not applicable` when the surface is explicitly unclaimed and the
   inspected source/artifact closure exposes no such surface.
2. Decide whether the missing fact is owner-defined or owner-private. If the assessment cannot
   define the acceptance decision, representative scenario, private review, or approval without
   the owner, use `owner evidence required`.
3. Otherwise decide whether an applicable reproducible acquisition or probe was performed. If not,
   use `not tested`.
4. If direct evidence satisfies or conflicts with the row, use `verified` or `gap`. Do not retreat
   to uncertainty after one required conjunct is directly observed missing.

## Paired boundary examples

| Evidence | Status | Why |
|---|---|---|
| The confirmed complete public-policy corpus directly contains no published general response SLA. | `gap` | Publication is the required product surface, so confirmed public absence is a direct conflict. |
| No private threat model or release approval was supplied, and the row does not require publication. | `owner evidence required` | Public absence cannot disprove an inaccessible owner-held record. |
| Every exact DLL is available, but full Authenticode digest/chain/timestamp/revocation verification was not run. | `not tested` | The fact is independently reproducible; the applicable probe is incomplete. |
| Screen-reader announcement behavior applies, but no assistive-technology probe ran. | `not tested` | Missing private conformance evidence does not convert an unrun reproducible behavior probe into owner-only evidence. |
| Optional telemetry is explicitly unclaimed and complete source/runtime inspection exposes no telemetry option or consent surface. | `not applicable` | The conditional feature surface is absent. |
| Auto mode is not in the confirmed support claims. | `not applicable` | A `when supported` row does not become an unrun required probe. |
| A keyed reorder behavior probe passes, but no Razor/source evidence shows `@key`. | not `verified` | Behavior does not prove the requested implementation mechanism. Use source evidence, otherwise `not tested` or `not applicable`. |
| Complete component and inherited-renderer source proves there is no component-owned repeated identity surface. | `not applicable` for `PERF-02` | The implementation mechanism has no applicable owned collection or loop. |
| Complete exact-package, release-asset, and workflow inventories contain no required published SBOM or provenance artifact. | `gap` for `PI-06`, `PI-07`, `PI-10`, and `PI-11` | Publication is required, so complete artifact absence is direct conflicting evidence. |
| A package has third-party assets, but no evidence establishes whether all are represented. | `not tested` for `PI-08` | Asset presence proves applicability, not representation failure. |
| A complete notice map directly omits applicable dependencies or bundled assets. | `gap` for `PI-09` | The representation evidence itself is incomplete. |
| Exact source discards an asynchronous callback or cleanup task. | `gap` | Direct source can establish an implementation defect even when a separate runtime probe was not run. |

## Calibration rules

- A failed package/document/source retrieval is `not tested`, not a `gap`.
- Direct absence of required metadata in the exact nupkg is a `gap`.
- Direct absence of a required published SBOM or provenance artifact is a `PI-06`, `PI-07`,
  `PI-10`, or `PI-11` gap when complete exact-package, public-release-asset, and release-workflow
  inventories cover the row. `PI-08` and `PI-09` require direct asset/notice representation
  evidence; missing SBOM bytes alone do not prove those rows.
- Direct absence of a required published/public/documented commitment is a `gap` only when the
  confirmed owner-controlled public corpus is complete for that surface. An incomplete or blocked
  corpus is `not tested`. For `SUP-03`, `SUP-05`, `SUP-06`, `BEQ-05`, and `CI-09`, a direct
  absence gap in a new schema-v2 assessment uses `public-absence-v1` bound to the accepted typed
  corpus. `CI-09` may instead use `direct-failure-v1` when behavioral assertions are present but
  exact sample compilation fails. Legacy schema-v1 revisions retain their original validation
  rules.
- A row may cite only one directed-gap protocol family. In particular, `CI-09` cannot claim both
  that behavioral assertions are absent and that they are present but compilation failed.
- Do not treat every word `documented` as `public`. Missing private review, retention, approval,
  staffing, or release-governance records is `owner evidence required`.
- If an owner must first define acceptable licenses, representative scenarios, targets, or accepted
  risks, use `owner evidence required`. If that definition exists and only execution is missing,
  use `not tested`.
- Source attributes may verify source structure; they do not verify browser semantics or formal
  conformance.
- Complete implementation source may verify source-level mechanisms and direct defects. Trace the
  component wrapper, inherited runtime, browser interop, styles, tests, and samples rather than
  reading only the public wrapper.
- A successful build does not verify runtime behavior.
- An environmental probe blocker is `not tested` unless direct evidence ties the failure to the
  package.
- For an `A or B` requirement, absence of A is not a `gap` while B remains applicable and untested.
- An unsupported optional mode may be `not applicable` only when explicitly unclaimed and safe.
- A package signature verifies only that signature layer.
- License or notice sidecars verify only those exact files, not complete notice coverage.
- A PE certificate table or signer subject does not verify Authenticode file-digest, chain,
  timestamp, or revocation validity.
- Organization, author, or contact metadata does not verify accountable support ownership,
  backup, or escalation coverage.
- A behavior outcome cannot verify an implementation mechanism such as `@key`, `ShouldRender`,
  awaited callbacks, renderer affinity, or asynchronous disposal.
- Newer direct evidence and named current-toolchain results control the current row even when they
  differ from a retained comparison label. Preserve and explain the evidence delta.
- Feedback and decision guidance never establish a status.
- Match each claim to its exact evidence field: `RepositoryUrl` is not `ProjectUrl`.
- Absence of a feature claim does not establish absence of the feature for `not applicable`.

Do not substitute alternate status words or abbreviations.
