# Licensing and Package Integrity Rubric

Schema `1.0`, scope `lp-pi-22-of-60`. This slice contains 18 rows from a
48-row baseline plus four versioned extensions (`X`). The complete reserved
package envelope has 60 rows, not 60 baseline requirements.

`D` means a deterministic artifact check. `E` means external or contextual
evidence is necessary. `X` identifies a versioned extension, not stronger
evidence. The only statuses are `verified`, `gap`, `not applicable`,
`not tested`, and `owner evidence required`. A deterministic declaration check
does not validate the truth of that declaration.

| Row | Basis | Requirement and decision boundary |
|---|---|---|
| LP-01 | D | Package SPDX expression satisfies the pinned OSI-approved non-copyleft policy. Every AND branch and every offered OR alternative must pass. WITH exceptions and trailing `+` must be parsed, not stripped. Unknown/unapproved/copyleft declarations are gaps. File-only licensing is not tested, with retained text facts and agent review. |
| LP-02 | E | Public source repository for shipped versions (Clause 1.2). Retain an anonymous acquisition observation at the package-declared revision. Repository metadata alone is not tested and does not establish binary correspondence. |
| LP-03 | E | Dependency license policy across the declared evidence scope. Without SBOM evidence: not tested. Any resolved policy violation is a gap, even with other unresolved entries. Otherwise NOASSERTION, NONE, LicenseRef, or unknown licenses leave not tested; only all-resolved approved expressions verify this evidence scope. Retain declared and concluded licenses separately. Structural admission does not prove complete coverage or publisher authenticity. |
| LP-04 | E | Required third-party license and notice files are preserved (Clause 2.12). Package-only evidence stays not tested unless a clear packaged policy violation establishes a gap. Never infer not applicable from a missing notice file. |
| LP-05 | D | `.nuspec` license has type `expression` and nonempty SPDX text. A license file or legacy licenseUrl is not an expression. LP-01 separately evaluates policy. |
| LP-06 | D | `.nuspec` repository URL is populated; presence is not an anonymous availability check. |
| LP-07 | D | `.nuspec` repository commit is a full 40-hex SHA-1 or 64-hex SHA-256 revision, not a branch, abbreviated hash, or tag. |
| LP-08 | D | `.nuspec` authors is populated. |
| LP-09 | D | `.nuspec` projectUrl is populated; this is not a liveness or ownership test. |
| LP-10 | E | Every packaged binary is attributable to public partner source or a referenced NuGet/npm package with a clear upstream; released partner binaries map to the public source commit (Clause 2.5). Assembly InformationalVersion is corroboration, not proof. Unverified prebuilt JS stays not tested. |
| PI-01 | D | Every managed PE/CLI assembly has a signed flag, public key, signature directory, and valid RSA strong-name signature. Unsigned, delay/public-signed without valid RSA, malformed, or invalid signatures are gaps with distinct reasons. |
| PI-02 | D | Authenticode CMS and indirect PE image digest must both verify. Missing/invalid signatures are gaps. Integrity alone is not tested for trusted publisher: expected subject, OS chain, and timestamp validity are additional gates; disclose offline revocation limits. |
| PI-03 | D | NuGet primary CMS signature and package archive integrity must verify. Author or repository primary signatures may qualify; trust is separately checked with `dotnet nuget verify --all`. |
| PI-04 | X | NuGet.org repository primary signature or countersignature, qualified by acquisition source. Other repositories or unbound certificate text do not establish NuGet.org distribution. |
| PI-05 | E | OIDC/Trusted Publishing is used instead of long-lived publish secrets (Clause 2.3). Inspect the actual release workflow and configured trust policy. `id-token: write` alone is not proof; a secret API-key publish path is not Trusted Publishing. |
| PI-06 | E | An SPDX or CycloneDX SBOM is published with every release (Clause 2.4). Local `--sbom` evidence does not establish public release publication, so remains not tested. |
| PI-07 | E | The SBOM enumerates direct and transitive dependencies (Clause 2.4). Counts alone do not prove complete dependency-graph reconciliation, so remain not tested without that evidence. |
| PI-08 | E | Bundled third-party JS/CSS upstream components and versions are listed in the SBOM (Clause 2.12). Not applicable only when there are no web assets, build static-asset manifests, or relevant embedded resources. Otherwise reconcile SBOM/lock with shipped assets. |
| PI-09 | E | Required bundled third-party license and notice files are preserved (Clause 2.12). Same strict absence rule as PI-08; otherwise a clear packaged policy violation is a gap and unproven coverage stays not tested. File hashes alone do not verify preservation. |
| PI-10 | X | SBOM and provenance bind to the exact final signed NuGet package digest (Clause 2.4, operational-2.0.0). Both SBOM digest binding and live verified provenance must match the inspected package SHA-256. A different pre-countersigning release digest does not meet this stronger requirement, even with payload parity. |
| PI-11 | X | Build provenance connects source SHA, workflow, dependencies, signatures, and publication (Clause 2.5). Live verified provenance may bind the package or a parity-matched release package; retain the distinct digests. A successful command without a matching verified statement, expected identity, and suitable predicate is insufficient. |
| PI-12 | X | Release-evidence retention policy and human ownership: always owner evidence required. |

## Policy and cryptographic limits

The embedded SPDX table is curated, not the complete SPDX license list. Unknown
identifiers, malformed expressions, and copyleft alternatives do not pass.
Classification is not legal advice. File text and compound banners need agent
review and version-matched notice reconciliation.

Strong-name checking uses declared PE structures and metadata only. It never
loads an assembly, scans certificate bytes for executable markers, or treats a
strong-name flag as an RSA result. Authenticode compares the signed indirect
digest to an independently computed image digest. NuGet signing compares the
signature content to archive integrity. Report these as separate evidence.

## Deferred rows: explicitly not tested

All following IDs are emitted in `deferredRows` with `deferred: true` and
`status: "not tested"`. None is silently excluded or inferred inapplicable.

| Area | Deferred IDs |
|---|---|
| Security | SEC-01, SEC-02, SEC-03, SEC-04, SEC-05, SEC-06, SEC-07, SEC-08, SEC-09 |
| Build/engineering quality | BEQ-21, BEQ-24 |
| Testing/accessibility | TA-07 |
| Continuous integration | CI-01, CI-05, CI-06, CI-07, CI-08 |
| Support | SUP-01, SUP-02, SUP-03, SUP-04, SUP-05, SUP-06, SUP-07, SUP-08, SUP-10 |
| Supply-chain features | SCF-01, SCF-02, SCF-03, SCF-04, SCF-05, SCF-06 |
| AI | AI-01, AI-02, AI-03, AI-04, AI-05, AI-06 |
