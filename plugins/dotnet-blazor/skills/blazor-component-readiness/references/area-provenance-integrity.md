# Provenance and integrity

Applies to `LP-*` and `PI-*`. Follow `artifact-acquisition.md` first.

## Evidence precedence

When evidence conflicts, prefer:

1. exact published or supplied candidate nupkg;
2. deterministic consumer/browser observation against those bytes;
3. source at the exact mapped commit;
4. current public source/configuration;
5. official documentation or owner evidence;
6. unsupported inference.

Lower-ranked evidence cannot override contradictory higher-ranked evidence.

## Collect and separate

- exact package identity, digest, nuspec, files, target frameworks, dependencies, and bundled assets;
- source mapping and exact commit, when available;
- strong-name identity, Authenticode, package author signature, and repository countersignature as
  separate layers;
- SBOM/provenance correspondence to the final package digest;
- build/sign/SBOM/release evidence without assuming configured intent executed;
- license/notice evidence for direct, transitive, and bundled assets.

Repository evidence may be reused only for the same package ID/version/digest and source commit.
Component evidence cannot prove repository-wide rows.

Every selected record must bind to the confirmed input manifest. Documentation binds exact URL and
captured digest; package evidence binds the exact nupkg or recomputed archive entry; source binds an
exact confirmed `source:<path>` capture and digest; owner evidence binds exact basename,
classification, and digest. Treat `owner-supplied-public-evidence` and
`owner-supplied-internal-evidence` as distinct first-class provenance kinds.

Use `gap` for directly observed missing/contradictory required public artifacts. Use
`owner evidence required` for inaccessible private approval, retention, signing, or legal records.
A rebuilt package cannot verify the distributed artifact.

For `PI-06`, `PI-07`, `PI-10`, and `PI-11`, publication is the required surface. When complete
exact-package, public-release-asset, and release-workflow inventories contain no required
SPDX/CycloneDX SBOM or provenance artifact, use `gap`; the absence is direct evidence, not an unrun
probe. Use `not tested` when that coverage is incomplete or blocked.

For `PI-08` and `PI-09`, missing SBOM bytes alone are insufficient. First establish the applicable
third-party asset or notice surface, then inspect its representation. Use `gap` only when complete
typed asset/dependency/notice evidence directly shows missing or incomplete representation. Use
`not tested` when the representation evidence is absent or incomplete, and `not applicable` only
when complete inventories prove there is no applicable third-party asset or notice surface.

For dependency-license acceptability, a complete dependency inventory defines what must be
reviewed but does not make the legal acceptance decision. Without a supplied owner-approved
decision record, use `owner evidence required`; do not relabel the missing decision as an unrun
technical probe.

### Verified-boundary protocols

- `LP-04`: license files and detected sidecars prove only their own presence. `verified` requires a
  canonical `notice-coverage-v1` protocol whose complete dependency inventory, bundled-asset
  inventory, and notice mapping are each digest-bound confirmed inputs.
- `PI-02`: a PE certificate table or signer subject proves only embedded signature material.
  `verified` requires an `authenticode-verification-v1` protocol covering every shipped DLL entry,
  the exact package-entry digest, expected and observed identity, file-digest verification, chain
  disposition, timestamp disposition, revocation disposition, and a digest-bound raw verification
  log.

The validator enforces these protocol shapes and package-entry coverage. It does not independently
replace the platform signature verifier or legal notice review; the protocol binds those direct
results so weaker metadata hints cannot be promoted to `verified`.

Register notice protocols as `reviewer-generated-analysis` with method
`protocol:notice-coverage-v1`. Register Authenticode protocols as
`reproduced-runtime-observation` with method `protocol:authenticode-verification-v1`. In both cases,
the locator is the confirmed protocol basename and its digest must match `evidence_inputs`.
Notice coverage may resolve `complete` or `incomplete`; Authenticode artifacts may record valid,
invalid, not-tested, or not-applicable layer dispositions. `verified` requires the all-valid shape,
while `gap` requires a structured failed/incomplete outcome rather than a malformed protocol.
When exact DLLs exist but the Authenticode protocol lacks digest, chain, timestamp, revocation, or
expected-identity results, use `not tested` unless the unresolved prerequisite is specifically an
owner-only identity declaration.
