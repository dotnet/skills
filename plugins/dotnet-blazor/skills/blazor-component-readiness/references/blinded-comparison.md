# Blinded comparison input gate

This is evaluation methodology, not an ordinary readiness requirement. Use it only when the user
explicitly requests a blinded accuracy, replay, or oracle comparison.

## Freeze before the blind worker starts

Create a conclusion-free draft that contains:

- the selected assessment kinds (`unified`, `package`, `component`, or package plus component);
- exact package ID/version and the allowed nupkg input;
- every exact source snapshot identity, including availability, repository, commit, and source
  archive input when source is available;
- the complete allowed-input set with neutral IDs, kinds, relative paths, sizes, and CLI-computed
  digests;
- every raw package/document/source retrieval record;
- every raw probe input;
- named toolchain and browser identities with raw identity inputs when relevant;
- `available` or `blocked` disposition for every retrieval, probe, toolchain, and browser;
- an exact blocker for every `blocked` item.
- exact `available` or `blocked` accounting for every required evidence surface.

Schema version 2 requires these fixed vendor-neutral coverage surfaces:

- common: `exact-package`, `official-public-documents`, `owner-held-records`;
- package: `dependency-and-notice-inventory`, `release-source-and-workflows`,
  `signing-sbom-provenance`, `support-and-lifecycle`;
- component: `component-api-and-base-source`, `browser-interop-and-style-assets`,
  `tests-and-samples`, `claimed-mode-runtime`, `accessibility-and-localization`,
  `trim-aot-toolchains`, `performance-measurements`, `regression-and-release-mapping`.

An available surface must bind every typed role below to an allowed input with the exact accepted
kind. Each binding also records `origin_kind` and `origin_id`; the validator requires the input to
be owned by the named exact package, source identity, available retrieval/probe, toolchain, or
browser as allowed for that role. A blocked surface carries an exact blocker and may bind any
available partial roles.

Each semantic coverage role owns one input ID and distinct bytes/path. Reusing an input ID, path, or
digest across roles is rejected until a future schema defines an explicit safe alias rule.

| Surface | Required role -> allowed-input kind |
|---|---|
| `exact-package` | `package` -> `nupkg` |
| `official-public-documents` | `documentation-corpus` -> `public-document-corpus` |
| `owner-held-records` | `owner-record-corpus` -> `owner-record-corpus` |
| `dependency-and-notice-inventory` | `dependency-inventory` -> `dependency-inventory`; `asset-inventory` -> `asset-inventory`; `notice-mapping` -> `notice-mapping` |
| `release-source-and-workflows` | `source-snapshot` -> `source-archive`; `workflow-inventory` -> `workflow-inventory` |
| `signing-sbom-provenance` | `assembly-signing` -> `assembly-signing`; `package-signing` -> `package-signing`; `sbom-provenance` -> `sbom-provenance` |
| `support-and-lifecycle` | `public-support-corpus` -> `public-support-corpus`; `release-lifecycle-corpus` -> `release-lifecycle-corpus` |
| `component-api-and-base-source` | `component-source-closure` -> `component-source-closure` |
| `browser-interop-and-style-assets` | `browser-interop-source` -> `browser-interop-source`; `style-asset-inventory` -> `style-asset-inventory` |
| `tests-and-samples` | `test-inventory` -> `test-inventory`; `sample-inventory` -> `sample-inventory` |
| `claimed-mode-runtime` | `mode-claims` -> `target-manifest`; `runtime-observations` -> `runtime-observation` |
| `accessibility-and-localization` | `accessibility-observations` -> `accessibility-observation`; `localization-claims` -> `localization-claim-corpus` |
| `trim-aot-toolchains` | `toolchain-identities` -> `toolchain-identity`; `trim-aot-observations` -> `toolchain-probe` |
| `performance-measurements` | `performance-scenarios` -> `performance-scenario`; `performance-results` -> `performance-result` |
| `regression-and-release-mapping` | `defect-regression-map` -> `regression-map`; `release-revalidation` -> `release-revalidation` |

For source-available components, `component-source-closure` covers the public wrapper and inherited
base/runtime paths, while `browser-interop-source` and `style-asset-inventory` cover the browser and
style side. A generic source archive or an unregistered relabeled file cannot satisfy those roles.

Schema-version-1 confirmed comparison manifests remain canonical and validate under the legacy
exact-input gate. New freezes emit schema version 2.

Run:

```text
<launcher> comparison inputs-freeze --root <bundle-root> --draft <comparison-inputs.draft.json> --output <comparison-inputs.confirmed.json>
<launcher> comparison inputs-validate --root <bundle-root> --manifest <comparison-inputs.confirmed.json>
```

The gate fails when an input is missing, stale, unreferenced, outside the allowed set,
conclusion-bearing, left `unresolved`, or when a required coverage surface is omitted. Available
trim/AOT probes require a named available toolchain. The blind worker must receive only the
confirmed manifest and allowed files, re-run `inputs-validate`, and reconcile every coverage
surface before reading evidence or starting assessment work.

Do not include statuses, findings, recommendations, scorecards, feedback, oracle conclusions,
revealing filenames, or historical report text. Unblind and compare only after the worker's
assessment is frozen.
