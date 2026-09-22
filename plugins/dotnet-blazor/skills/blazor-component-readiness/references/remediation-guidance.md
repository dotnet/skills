# Requested remediation guidance

Use this route for explicitly requested recommendations, implementation examples,
remediation, documentation-testing help or data-transfer troubleshooting. The request is the opt-in: do not
require the filename or a second confirmation. Factual-report-only requests create no companion.

For a general documentation-testing or data-transfer question, use the requested
help sections below before the revision-specific procedure. An assessment or
failed row is not a prerequisite. Use supplied context, explain limitations,
and answer inline when no verified report exists. Do not create an assessment
or a source-validation digest to make such advice possible.

## Read, advise, preserve

For advice about an existing assessment, read its assessment, report, evidence and validation manifest, plus relevant
retained inputs and the requested area guidance. Confirm the source validation-manifest SHA-256
locally. If the existing validated revision or necessary context is unavailable, report that
limitation; do not create an assessment to make advice possible.

Create only `decision-guidance.md` under the confirmed output root, outside `revisions/` and
deterministic reader directories, following [the report contract](report-contract.md#decision-guidance).
No feedback file is required. Reuse the existing delivered report; do not rerender it.
Do not rerun the assessment, reacquire evidence, execute probes, run the illustrative commands,
or perform network research to produce this companion. Use the verified references below and
already supplied sources; additional research or implementation needs a separate specific request.
Keep canonical bytes, statuses, evidence, counts, reports and manifests unchanged.

For finding-specific remediation, cover only requested unresolved findings
(`gap`, `owner evidence required`, or `not tested`).
Group related rows when useful; do not automatically advise on every row or on verified/N/A rows.
Carry supported partial facts and their exact limits forward. Missing evidence is not an
implementation diagnosis: request the missing record, owner decision or diagnostic result rather
than prescribing a tool/workflow when the cause is unknown.

General documentation-testing or data-transfer questions do not need a failed
row or an existing assessment. Answer from relevant supplied context and the
references below, with explicit limits. Do not create an assessment, invent a
finding, add a criterion, demand an evidence matrix or change a status to enable
advice. If no validated report is available, give clearly unbound explanatory
guidance inline rather than fabricating a source-validation digest or companion.
For an existing report, a requested general topic may be discussed even when its
related DOCX check is verified; label it as advice, not an unresolved finding.
These exceptions do not authorize probes, installation, external research or execution.

## Requested documentation-testing help

BEQ-23 already covers locating public samples and their supported-mode coverage.
Do not automatically compile every example or require assertions in vendor
documentation. When asked how to check an example, suggest using its exact
package/API and documented context, then checking both compilation and the
specific observable behavior it promises. An unknown attribute can compile
without configuring the intended parameter. Explain that risk without assuming
every sample is defective or requiring a new testing framework.
Actual compilation or browser exercise is separate authorized work; describing
a recipe is not evidence that it ran.

## Requested data-transfer troubleshooting

Start with the concrete interaction, workload and available source or trace.
Inspect relevant serialization/send paths and, when captured, call frequency,
payload sizes and repeated transmission. Distinguish application payload bytes
from framing/compression and total app traffic. A full-dataset transfer may be
intentional; source alone does not establish excessive cost or a defect.

Explain candidate causes, what the available observations establish, and the
smallest additional evidence needed. Allocation/copy attribution across .NET,
serialization and browser code may need profiling and may remain incomplete.
Do not promise a diagnosis, invent a performance threshold or request a general
vendor performance dossier. No measurement campaign starts merely because the
component is data-heavy. Reproduction/profiling requires separate authorization
and required isolation. This does not disable normal work needed for PERF-06
or another actual DOCX requirement.

## Per-finding shape

Start with `Source validation manifest SHA-256: <64-lowercase-hex>`. For each requested row/group use:

1. **Current finding:** row IDs, exact statuses/classifications, observed shortfall and selected
   evidence from the validated revision.
2. **Supported next step:** a bounded source-backed implementation pattern only when the cause
   is established; otherwise the exact missing evidence or owner decision. Do not invent a fix.
3. **Evidence to support reassessment:** concrete artifacts/results, package/release/source/run
   identity, scope and coverage; explain what those records would establish, not a guaranteed pass.
4. **Implementation references:** relevant verified links below or supplied first-party sources.
   Cite the source/review date; examples are illustrative, not evidence that an operation ran.
5. **Owner decisions and limitations:** remaining legal, policy, risk, trust, representative-scenario
   and organizational decisions. Preserve uncertainty and any unsupported target/platform details.
6. **Authority disclaimer:** illustrative options, not mandatory designs, Microsoft endorsements,
   certification or guarantees of acceptance. Optional hardening is separate from rubric closure.

Link the companion and existing factual report in the final handoff. Report source identity and
unchanged-artifact checks separately from advice; guidance does not re-adjudicate any status.

## SBOM and package patterns

Apply only to the requested rows when retained evidence establishes the relevant implementation
cause. These examples do not authorize execution, installation, access changes or publication.
`PI-03`, `PI-05`, `PI-06` are direct obligations; `PI-07` through `PI-09` are decomposition
evidence checks. `PI-10` and `PI-11` are **versioned extensions**, not extra baseline obligations.

| Rows | Illustrative pattern | Evidence to support reassessment |
|---|---|---|
| `PI-06`, `PI-07` | Generate an SPDX SBOM with Microsoft [sbom-tool][sbom] from the exact final release drop and corresponding build-component inputs, validate it, and publish it with that release. Reconcile detected dependencies with direct/transitive inventories; successful generation alone does not establish completeness. | Published SBOM bytes/location, generation and validation results, exact release/package identity, and complete dependency coverage. If only publication evidence is missing, request that release asset/inventory instead of assuming generation is absent. |
| `PI-08`, `PI-09` | Inventory bundled third-party JS/CSS/assets, map each upstream component/version into the SBOM, and retain its required license/notice files. Tool detection is a starting point, not proof of asset or notice coverage. | Digest-bound package-entry, dependency, asset and notice inventories with component/version mappings; owner legal decisions remain separate. |
| `PI-03` | Use [NuGet package signing][signing] with an owner-approved certificate and timestamp service, then [verify the signature][verify] against the expected signer before publishing those final bytes. Keep author signing, repository signing and DLL Authenticode separate. | Exact `.nupkg` SHA-256, registered/expected signer, signature verification output, timestamp disposition and publication identity. An unsigned package or absent verification record does not identify which signing architecture the owner must choose. |
| `PI-05` | Configure [NuGet Trusted Publishing][trusted] for the owning account, repository and workflow filename; bind the environment when one is used. Request the short-lived OIDC-derived key in the publish job immediately before push. `NuGet/login` uses job-level `id-token: write`; do not expose that permission to untrusted build steps. | Policy identity/scope, actual job permissions, login/push results, package ID/version/digest and publication record. OIDC alone proves neither privilege separation nor immutable transfer. |
| `PI-10`, `PI-11` | Bind SBOM/provenance subjects to the exact final signed package digest and connect source SHA, workflow/run, dependency inputs, signatures and publication. [GitHub attestations][attest] can attest the artifact using `subject-path` and the existing SBOM using `sbom-path`. Verify the subject and expected repository/workflow identity; an attestation does not create or validate SBOM completeness. | Final package SHA-256, SBOM/provenance subject and predicate, exact source/workflow/run, dependency/signature/publication records and attestation verification output. Name whether each digest binds author-signed upload bytes or repository-signed distribution bytes; do not reuse a raw digest across that transformation. Distinguish file checksums from SPDX package-verification codes. |

For an established `PI-08` embedded-component omission, retain the correctly represented
dependencies and correct the omitted component's evidenced identity/version and correspondence
in the release SBOM, using the [SBOM tooling reference][sbom] where appropriate. Alternatively,
remove the distributed content if the owner determines it is unnecessary and removal is compatible
with the release. Include shipped source-map `sourcesContent` in that decision, not only the
runtime bundle or package-manager paths. For reassessment, request the resulting exact package
and entry digests, complete distributed-asset inventory, attribution/version evidence and updated
SBOM with component-to-bytes mappings (or evidence that the content is no longer distributed).
Missing identity/version/correspondence instead calls for those exact records, not a guessed
component, removal prescription or guaranteed pass. Advice changes neither the retained status
nor the separate notice/legal requirements.

Illustrative commands for an already provisioned, owner-approved toolchain; placeholders must be
resolved from that release, not copied literally. Do not execute them while writing guidance:

```text
sbom-tool generate -b <final-release-drop> -bc <build-component-inputs> -pn <package-name> -pv <version> -ps <supplier> -nsb <approved-namespace-base> -mi SPDX:2.2
sbom-tool validate -b <final-release-drop> -o <validation-result.json> -mi SPDX:2.2
dotnet nuget sign <package.nupkg> --certificate-path <approved-certificate> --timestamper <approved-timestamp-service>
dotnet nuget verify <final-signed-package.nupkg> --all --certificate-fingerprint <expected-signer-sha256>
```

The generation/validation pair uses the same drop and format; it is not a signing sequence.
Generate the final-package SBOM after signing has established the intended release bytes.
Choose SPDX 3.0 only with matching producer/validator/consumer support; [sbom-tool][sbom] documents
both formats. For SBOM attestation verification, use the predicate type matching the actual SBOM,
not a copied SPDX 2.3 example for a different SPDX version. Verification may need network access
or platform trust stores during separately authorized execution; guidance itself stays local.

## Release patterns

`CI-05`, `CI-07`, and `CI-08` are **versioned extensions**. Keep their distinct questions separate:

| Row | Illustrative pattern | Evidence to support reassessment |
|---|---|---|
| `CI-05` | Make dependency scans and release checks required gates on the applicable release path. Required-gate policy is not privilege separation. | Required-check/release policy plus successful gate/run records for the exact source and package; owners select policy, bypass authority and risk decisions. |
| `CI-07` | Separate untrusted restore/build/test from privileged signing/publishing. Transfer the built artifact; grant credentials/OIDC only to the trusted release job, which does not execute the untrusted build again. Environment approval alone does not isolate source execution within a privileged job. | Actual job graph, executable steps, permissions, environment/trust controls, transferred artifact identity and privileged-job result. Do not infer isolation from a login action or workflow name. |
| `CI-08` | Build once and record the **unsigned build digest**. Transfer and compare that digest at the privileged boundary. Author-sign, record the **signed final digest**, verify signatures and subject bindings, then upload those exact bytes without rebuilding/repacking. Require author-signed/upload digest equality. Signing may change bytes: do not require unsigned and signed digests to be equal. | Unsigned build/transfer equality, signed final/upload equality, verification results and upload receipt. For repository-transformed distribution bytes, retain both raw digests and establish correspondence through the preserved author signature, repository signature or countersignature, and exact package identity. Same filenames, workspace paths or signer certificates alone are insufficient. |

NuGet.org automatically [repository-signs uploaded packages][signed-packages]. Do not conflate
author-signed upload bytes with repository-signed distribution bytes. Do not require raw
upload/distribution digest equality after repository signing or countersigning changes the archive.
Retain both digests, verify the preserved author signature and signed package content against the
upload, verify the repository signature or countersignature, and match the package ID/version.
For an untransformed distribution, raw digest equality remains the direct identity check.
Missing signature/correspondence records remain unresolved; a different distribution digest alone
is not a failed immutable upload boundary or a guarantee that a sample pipeline satisfies `CI-08`.

## Verified references

Reviewed **2026-09-14**. These are tool/platform documentation, not additional acceptance authority.
No repository workflow implementation is inferred from a project name. Pin any implementation
example separately after inspecting its actual file. Extra attestations or stronger restrictions
not required by a selected row are optional hardening.

[sbom]: https://github.com/microsoft/sbom-tool/blob/4091b7bcce1640c4db42b5fad63d7d1b7bc0e4cf/README.md
[trusted]: https://learn.microsoft.com/nuget/nuget-org/trusted-publishing
[signing]: https://learn.microsoft.com/nuget/create-packages/sign-a-package
[verify]: https://learn.microsoft.com/dotnet/core/tools/dotnet-nuget-verify
[attest]: https://docs.github.com/actions/how-tos/secure-your-work/use-artifact-attestations/use-artifact-attestations
[signed-packages]: https://learn.microsoft.com/nuget/reference/signed-packages-reference
