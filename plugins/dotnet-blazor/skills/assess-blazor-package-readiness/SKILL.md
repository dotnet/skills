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
- .NET SDK 10 or later; first execution restores the two pinned dependencies.
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
   [the row rubric](references/rubric-lp-pi.md). Distinguish:
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
   and bundler configuration. The inspector deliberately does not parse YAML
   or reconstruct a JavaScript bundler's dependency graph. Follow
   [the web-assets and provenance playbook](references/web-assets-and-provenance.md)
   for anonymous source retrieval, bundled transitive dependencies, Trusted
   Publishing, and source/build correspondence.

   Keep the inspector's machine findings unchanged. Put agent-adjudicated
   changes in the human report with the row, prior status, new status, exact
   evidence, and limitation. An SBOM's presence does not prove completeness,
   publication, runtime reachability, or package identity. A lockfile alone
   does not prove which modules a shipped chunk contains.

4. **Report partial coverage explicitly.** Use only `verified`, `gap`,
   `not applicable`, `not tested`, and `owner evidence required`. Include a
   22-row table with row ID, basis, status, reason code, and evidence pointer.
   Preserve `agentReviewRecommended` and explain unresolved rows.

   State: **22 of 60 package rows assessed: 18 baseline rows and four versioned
   extension rows (PI-04, PI-10, PI-11, PI-12); 38 rows deferred. This is not
   full package signoff or the full 48-row baseline assessment.**

   List all 38 deferred IDs explicitly (the reference lists them) with
   `not tested`, rather than hiding them in a count. Never infer that SCF or AI
   requirements are inapplicable because a `.nupkg` lacks application features.
   Call out actual gaps first, then unresolved evidence and required owner
   decisions. Do not collapse `not tested` into `gap`.

## Validation and stop conditions

- Confirm `schemaVersion: "1.0"`, `scope: "lp-pi-22-of-60"`, 22 unique assessed
  IDs, 38 unique deferred IDs, and only canonical statuses.
- On package-only evidence, LP-02, LP-03, LP-04, LP-10, PI-05, PI-06, PI-07,
  PI-10, and PI-11 remain `not tested`, except an explicit packaged banner may
  establish a clear LP-04 policy gap.
- PI-08 and PI-09 are `not applicable` only after no web assets or relevant
  embedded manifest resources are found, including build manifests.
- PI-12 always requires owner evidence. No offline attestation file verifies
  PI-10 or PI-11. PI-10 requires both SBOM and verified provenance to bind the
  exact final signed package digest. PI-11 can use verified build provenance
  for a release digest only with byte-identical non-signature archive entries;
  that parity does not satisfy PI-10 when the outer package digests differ.
- Stop short of legal approval, live revocation guarantees, exhaustive
  dependency discovery, reproducible-build claims, and full 60-row signoff.
