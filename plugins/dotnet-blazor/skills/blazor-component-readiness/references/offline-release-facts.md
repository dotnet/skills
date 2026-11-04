# Offline release facts

For an explicitly requested authorized-48 preparation pass that also needs package inspection,
full source inventory and derived layout, use [operator-invoked package preparation](package-preparation.md).
It wraps this unchanged collector and retains successful outputs for separate confirmation;
it is not mandatory assessment/report publication or an automatic model launch.

Use `release facts` only for already retained, confirmed inputs. It does not retrieve releases,
fetch JSON-LD contexts, use credentials, authenticate signatures, score requirements or establish
publication freshness. Internal provenance comparisons are not additional partner obligations.

Read [input candidates](input-candidates.md) for registration, explicit confirmation, exact capture
metadata and the shared identity/input-bound evidence sequence. This route does not require
the full-assessment workflow, component probes, library coordination or report/reader production.

```text
<launcher> release facts --root <approved-root> --input <pre-output-confirmed.json> \
  --release-package <registered-release.nupkg> \
  --spdx22 <registered-spdx22.zip> --spdx22-entry spdx_2.2/manifest.spdx.json \
  --spdx30 <registered-spdx30.zip> --spdx30-entry spdx_3.0/manifest.spdx.json \
  --provenance <registered-provenance.json> --sbom-statement <registered-sbom.json> \
  --output <new-release-facts.json>
```

The target package comes from the manifest, not another command-line identity. The other five
roles select distinct, unique root-level registrations in `evidence_inputs` or `owner_inputs`.
The target may be nested to preserve identical target/release filenames. Register actual public
release files with `inputs candidates add-evidence`, not as owner attestations. Input and output
paths must remain beneath `--root`; outputs never overwrite existing files. All confirmed inputs
are revalidated before collection, and selected bytes must still match their registered digests
and sizes. No caller hash override, authority flag or conclusion filter is accepted.

This is a finite literal parser, not full SPDX, RDF, Sigstore or SLSA validation:

- SPDX-2.2 UTF-8 JSON: one `documentDescribes` package, unique element IDs, and one file whose
  `fileName` is the target basename or `./<basename>`, present in that package's `hasFiles`.
  Capture document/package/file declarations, package verification code and file SHA256.
- SPDX3: the single context reference `https://spdx.org/rdf/3.0.1/spdx-context.json`, one
  `SpdxDocument` and one exact-name `software_File`. `verifiedUsing` supports inline objects
  or uniquely resolved local graph-ID strings, with `Hash` or `PackageVerificationCode` types
  and `sha256`/`sha1` algorithms. Top-level `spdxId` and `@id` aliases must not collide.
  Selected document/file identities and aliases must also be unique across inline occurrences.
  Inline records are literal occurrences at exact JSON pointers, not globally merged identities.
  A required ID reference must have exactly one top-level target and one declaration occurrence
  across the document; repeated inline IDs do not authorize arbitrary reference resolution.
- Sigstore bundle v0.3, DSSE `application/vnd.in-toto+json`, in-toto Statement/v1 and predicates
  `https://slsa.dev/provenance/v1` or `https://spdx.dev/Document/v2.2`. The exact target basename
  selects one subject with one SHA256. SLSA workflow/v1 source dependency must uniquely match
  `git+<workflow.repository>@<workflow.ref>` with one `gitCommit`. Signatures are checked only
  for base64 encoding, never authenticated. The embedded SPDX2 document is also parsed.

Missing, null, duplicate, unresolved, unsupported or malformed required values are fatal; they
are never disguised as `not-comparable`. A complete collection may contain `match`, `mismatch`
and `not-comparable`. The latter applies only to supported, unambiguous operands with incompatible
declared kinds/algorithms. In particular, file-associated `PackageVerificationCode` is **not**
a package-file checksum. Its separately labeled literal-value equality makes no identity or
authentication claim. Embedded-SBOM comparison uses structural JSON equality: object order
is ignored, array order and absent/null are retained. Both matches and mismatches report:
`Compared JSON structure only; RDF equivalence was not assessed.`
Independent document digest facts describe the serialized bytes, separately from structural comparison.
Different document hashes establish different serialized bytes, not RDF equivalence or
non-equivalence. Structural JSON comparison outcomes likewise establish no RDF-level relationship.

Result schema 1 (`producer_version: release-facts/1.0.0`, `normalization_version: 1`) contains
the confirmed manifest's raw digest/path, six role references to existing manifest pointers,
typed `facts`, eleven `comparisons` with full operands/reasons, all ten required `operations`,
`diagnostics`, cumulative `accounting`, and `execution`. Every fact has a packet-local ID,
kind, algorithm (or null), value and `{role, container, pointer}` source locator. Containers
identify raw bytes, exact ZIP entries, `nuspec:<entry>` or decoded `dsseEnvelope.payload`;
pointers are JSON pointers, except the inspected nuspec metadata paths. Fact IDs are not EV1 IDs.
The result retains declared verification IDs/types without claiming graph-wide consistency.
Success means every required operation actually ran. Failures emit a bounded operation trace
(`succeeded`, `failed`, `not-attempted`) and byte accounting on stderr, without publishing a result.
Existing exits remain 0 success, 1 validation, 2 usage and 3 environment.

Existing limits still apply: 256 MiB target, 32 registered supplemental/owner files totaling
64 MiB. An additional collective 64 MiB producer allowance charges all five selected raw inputs,
all actually expanded supplemental ZIP entries (including release-package content and sidecars),
decoded DSSE payloads/signature encodings and the literal embedded-predicate form, then exact
serialized output including diagnostics. Target expansion is separately bounded to 256 MiB.
Every ZIP is limited to 100,000 entries, safe regular paths, no duplicate/colliding names, and
declared/actual expanded-length checks. Every parsed document is limited to depth 64 and
100,000 JSON tokens. A 16 KiB allowance is kept available for fatal diagnostics; it is not
charged as consumed content. Accounting uses fixed-width decimal strings and counts cumulative
content, **not** total process memory or workspace storage. Successfully decoded bytes are
charged even when a subsequent canonical-base64 check rejects their original encoding.

For repeatability, `ReleaseFactsResult.NormalizedProjection()` replaces only
`execution.started_at_utc`, `execution.completed_at_utc`, `execution.duration_ms` and
`execution.root` with null. It preserves every input, fact, comparison, operation and accounting
field. Repeat into new files using the same confirmed input/root. Different physical root
lengths can change exact serialized-byte accounting; that accounting is intentionally not hidden.

Avoid output self-reference: follow the shared
[post-output confirmation](input-candidates.md#post-output-confirmation) before final identity.
Collect against a pre-output confirmed manifest, then use
`inputs candidates add-evidence --path <result-basename> --kind offline-release-facts` on the
latest candidates and discover/confirm a **new** manifest. Initialize/export the final assessment
identity afterward. Keep the previously approved inputs and add only the authorized derived
result. Confirmation remains a separate explicit `inputs confirm` command, never hidden in an
evidence producer and never permission to add unrelated inputs.
Consume the retained result with `evidence draft-add --kind reviewer-generated-analysis
--root <approved-root> --manifest <post-output-confirmed> --evidence-input <result-basename>`, an accurate
mechanical method/capture instant, and specific fact/comparison IDs in the claim or method.
The registered mode derives the locator and digest together from validated bytes. Do not supply
manual `--locator`, `--content` or `--nupkg`; the producer label `release-facts/1.0.0` is not the
result filename. Missing/stale registrations require a new discovered and explicitly confirmed
input, not a repaired locator or altered old manifest.
Continue with `ledger-build`, `ledger-validate` and `bundle --root <approved-root> --manifest
<post-output-confirmed>`. Input-bound acceptance checks the final manifest identity and every
selected record's binding before publication. Structural-only bundle/ledger validation does not
complete this handoff. Rebuild downstream identities and evidence with existing producers after
input changes; do not force EV1 changes or copy old IDs instead of regeneration.
An integration-only unscored identity skeleton is not a vendor
assessment. Do not add excluded internal provenance facts to accepted partner-report rows.
