---
name: assess-blazor-package-readiness
description: >
  Audit a Blazor .nupkg or release bundle for package readiness. USE FOR:
  licensing, SPDX/copyleft compliance, third-party notices, .nuspec metadata,
  strong-name assembly signing, Authenticode, NuGet package signatures, SBOMs,
  Trusted Publishing, and SLSA build provenance. DO NOT USE FOR: wiring or
  configuring Ignite UI components in an app (use-igniteui-blazor), authoring
  Razor components (author-component),
  or live browser, accessibility, or bUnit runtime component testing.
license: MIT
---

# Assess Blazor Package Readiness

Produce an evidence-bound licensing and package-integrity assessment, not a
blanket product certification. Inspect the exact package bytes first, then
reconcile supplied source and release evidence. Missing evidence is not a pass.
Do not install the package into an app or execute its assemblies or build hooks.

## Inputs and prerequisites

- Required: the exact `.nupkg` to assess. Discover an already supplied package
  before requesting a download; retain package identity, source, and SHA-256.
- Optional: pre-repository-signing release `.nupkg`, SPDX or CycloneDX JSON SBOM
  (or ZIP containing one `.spdx.json`), DSSE attestation, repository identity,
  npm `package-lock.json`, expected certificate publisher, source checkout,
  release workflow, and release-evidence retention policy.
- .NET SDK 10 or later; first execution restores the pinned dependencies.
  `gh` is needed only for live attestation verification. Network and platform
  certificate trust availability limit what can be verified.

## Workflow

1. **Establish scope and execute the inspector.** Resolve bundled files relative
   to this `SKILL.md`, not the caller's repository. `SKILL_DIR` below denotes
   that known directory; do not search installation directories. Allow one
   listing of its `scripts` directory if the script is missing, then report
   reduced coverage rather than replacing cryptographic checks with guesses.

   ```text
   dotnet run --file "<SKILL_DIR>/scripts/Inspect-BlazorPackage.cs" -- --nupkg "<package.nupkg>"
   ```

   Add only applicable options:

   ```text
   --release-nupkg "<release-package.nupkg>"
   --sbom "<release.spdx.json-or-zip>"
   --attestation "<dsse-bundle.json>"
   --attestation-repo "<owner/repo>"
   --package-lock "<package-lock.json>"
   --workflow "<release-workflow.yml>"
   --expected-publisher "<certificate-subject-substring>"
   --output "<report.json>"
   ```

   Run from the bundled `scripts` directory to use its .NET 10-or-later
   `global.json`; first resolve package and output paths to absolute paths.
   SDK selection starts at the working directory, not the script path.
   A caller repository's `global.json` can otherwise select an unavailable SDK.
   If necessary, copy the skill and the caller's approved `nuget.config` to a
   clean temporary directory outside that tree and run there. Do not edit the
   caller's SDK pin or silently change package sources. Report restore,
   compilation, malformed-input, archive-limit, and external-command errors.
   Never interpret missing or truncated JSON as an assessment.

2. **Interpret structured facts, preserving evidence boundaries.** Read
   [the row rubric](references/rubric-lp-pi.md) only if the user requests the
   LP/PI matrix or row IDs, or an unfamiliar `reasonCode` needs interpretation.
   Otherwise work directly from the JSON facts. Distinguish:
   declaration from legal interpretation; signature presence from cryptographic
   integrity; integrity from trusted signer identity; and an unverified
   attestation from a verified statement bound to the exact artifact digest.
   Strong-name, Authenticode, and NuGet signatures are three separate checks.
   A signed outer NuGet package does not sign unsigned DLLs.

   For OS/root-store NuGet trust verification, run separately:

   ```text
   dotnet nuget verify --all "<package.nupkg>"
   ```

   Retain the command result, OS, trust/revocation limitations, and acquisition
   source. Do not promote an offline CMS integrity result to live trust.
   Public-signed or delay-signed assemblies fail this rubric's requirement for
   a valid RSA strong-name signature; that is not an accusation of tampering.

3. **Review source and release evidence directly when present.** Read plain-text
   workflow YAML, `.csproj`, inherited build files, source, license/notice text,
   and bundler configuration. The optional workflow input reports credential
   evidence, not a complete workflow or expression evaluation. The inspector
   does not reconstruct a JavaScript bundler's dependency graph. Follow
   [the web-assets and provenance playbook](references/web-assets-and-provenance.md)
   for anonymous source retrieval, bundled transitive dependencies, Trusted
   Publishing, and source/build correspondence.

   Keep the inspector's machine findings unchanged. Distinguish additional
   agent-adjudicated findings from machine results, with exact evidence and
   limitations. If source review contradicts a machine result, explain the
   discrepancy in domain language rather than presenting both as conclusions.
   An SBOM's presence does not prove completeness,
   publication, runtime reachability, or package identity. A lockfile alone
   does not prove which modules a shipped chunk contains.

4. **Answer the actual question with checkable evidence.** Lead with the direct
   answer and the relevant measurements, not a compliance template. Scale the
   detail to the request; do not dump every inspector fact into every answer.
   Translate `rows[].id`, `rows[].reasonCode`, and deferred-row IDs into domain
   language: these are internal machine-schema keys, not a required report.
   Do not emit LP-*, PI-*, SEC-*, BEQ-*, TA-*, CI-*, SUP-*, SCF-*, or AI-* IDs,
   "22 of 60"/"38 deferred" counts, or an unrequested compliance matrix.
   Only an explicit request for the rubric table or row IDs permits those.

   Select concrete evidence that settles the requested question:
   - Exact package/file SHA-256 values; `.signature.p7s` author signature versus
     repository countersignature; certificate subject, self-signed/untrusted
     chain, and trusted-timestamp status.
   - Per-assembly CorFlags, public key/token where available, StrongNameSigned
     flag versus actual RSA verification, and zero/invalid delay/PublicSign
     signature blobs. A flag or key alone is not a valid signature. For
     Authenticode, distinguish embedded and calculated PE digests from trust.
   - Actual `.nuspec` fields, license expression versus file/legacy URL,
     repository URL, and the exact full commit and its hexadecimal length.
   - Relevant browser assets in `staticwebassets/`, `contentFiles/`, build or
     buildTransitive props/targets, and DLL manifest resources. No conventional
     web directory does not prove no bundled scripts.
   - Both declared and concluded licenses when restrictive, NOASSERTION,
     LicenseRef-based, or conflicting; SBOM root binding via DESCRIBES or
     metadata.component rather than a nested hash; declared NuGet dependencies
     or lockfile packages missing from the SBOM. Direct inclusion alone does
     not prove complete transitive coverage.
   - Actual publish credential (OIDC login output versus static API key) and
     DSSE/SLSA subject digest and resolved gitCommit versus the package's
     repository commit. Decoding offline provenance does not verify signatures.

   For broad release-readiness or go/no-go requests, use concise plain-English
   sections for verified package checks, concrete blocking defects, unresolved
   external/trust checks, and readiness areas package artifacts cannot settle.
   These can include security review, runtime Blazor rendering and WCAG/keyboard
   accessibility, bUnit/Playwright and CI quality, support SLAs, supply-chain
   governance, and AI safety when relevant. Do not infer inapplicability from
   absent application features. Tie remediation to observed findings; omit
   generic filler. Missing evidence is not a defect or a full shipping approval.

## Validation and stop conditions

- Validate the JSON envelope internally: schema version, complete assessed and
  deferred sets, and canonical statuses. Do not expose schema keys or coverage
  counts unless requested. Distinguish `verified`, `gap`, `not applicable`,
  `not tested`, and `owner evidence required` rather than equating them.
- Public source, attribution completeness, public SBOM publication, full
  dependency closure, workflow authentication, and build correspondence need
  their own evidence. A supplied artifact can establish a concrete defect,
  such as a missing declared dependency or static publishing secret, without
  settling the broader check. Do not contradict it with blanket "not tested".
- Browser checks are inapplicable only after checking archive assets, build
  manifests, and relevant embedded resources.
- Retention always requires owner evidence. Offline attestations are unverified.
  Exact final-package provenance needs root SBOM and live provenance binding to
  the inspected signed package and its repository commit. A payload-identical
  release can support build correspondence, but does not erase distinct outer
  package digests or establish final-package SBOM binding.
- Stop short of legal approval, live revocation guarantees, exhaustive
  dependency discovery, reproducible-build claims, and blanket product signoff.
