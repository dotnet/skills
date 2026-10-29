# PI-01 strong-name expectation freeze (Phase A only)

The next core row after LP-10 is **PI-01**: "Every shipped assembly is
strong-name signed." The frozen `data/rubric.json` marks it
`repository-wide`, a decomposition evidence check for clause 2.1, among the
60 repository-wide rows of 112 core rows. There is **no LP-11 row**.
The pinned `data/requirement-basis.json` clause 2.1 says: "Signed assemblies.
All shipped assemblies are strong-name signed and Authenticode-signed using
the partner's verified publisher identity." Authenticode and publisher
identity are separate PI-02 evidence, not proof for PI-01.

## Evidence boundary and order

The only checked input is the exact SHA-256-pinned `.nupkg`. Inspect ZIP
entries (without extraction or code execution) for PE/CLI assembly metadata
at **any path**, including `lib`, `ref`, `runtimes`, tools and analyzers;
do not infer an assembly from a `.dll` extension alone. An unsigned
assembly (no CLR strong-name flag or reservation) and a zero-filled
delay-sign reservation are decisive package-specific negative findings.
A safely inventoried package with no CLR assemblies has a *vacuous,
package-only* positive finding; separately installed dependencies and
other releases remain untested. Cryptographically validating every
assembly signature against its metadata public key, and detecting
post-signing modification, is required before a signature-present package
can have a positive finding. A nonzero reservation and a CLR flag are
**not** that validation. NuGet signing (`.signature.p7s`), nuspec repository
URL/commit, PDBs, SourceLink, strong-name token strings and publisher
claims cannot substitute. No source checkout or network query is implied
by the presence of a repository URL; repository evidence cannot turn an
unsigned shipped assembly into a signed one.

Prioritize unsafe/ambiguous archives and metadata or unclassifiable PE/CLI
candidates (`unknown` / `escalated`) over any individual missing signature.
Then prioritize zero-filled reservations, then absent reservations, then
signature-present-but-cryptographically-unverified entries; only a safe,
complete no-assembly inventory yields the vacuous finding. The expected
result shape is the existing `CaseResult` contract, not a readiness
verdict. `pi01_expectations.py` uses standard-library structural inspection
as an independent **expectation oracle**, not a shipping resolver or a
cryptographic strong-name verifier. Synthetic PE/CLI headers are intentionally
signature-shaped controls, not runnable release assemblies; they freeze
the discrimination and priority rules, not positive cryptographic proof.
The acceptance results retain each assembly's exact path, byte count and
entry SHA-256 without copying either proprietary archive into this repo.
The unresolved evidence decision for Phase B is the implementation and
test vectors for actual cryptographic verification (including valid,
tampered and public/delay-signed assemblies); a structural presence test
must never be promoted to "verified".

The frozen matrix has 57 cases, of which 56 have independently pinned
synthetic archives. It covers empty and non-assembly archives, native PE,
different assembly locations and suffixes, multiple framework variants,
unsigned, delay-sign, nonzero signature reservations, conflicting and
malformed CLI structure, ambiguous ZIP names/paths, corrupt ZIP, UTF-16
nuspec/DTD and harmless comment decoys. Nine additional runtime-generated
exact-boundary controls exercise at/over entry bytes,
total uncompressed bytes, ZIP name length and entry count, plus
over-limit package bytes without committing giant archives.
The generator pins every archive byte and expected JSON document and
rejects extra/missing synthetic `.nupkg` files. Resource bounds in this
expectation oracle are 64 MiB archive bytes, 4096 entries, 1024 characters
per ZIP name, 1 MiB nuspec, 8 MiB per entry, 32 MiB total non-manifest
uncompressed bytes; production Phase B must independently test and choose
its own safe limits. The oracle does not recursively inspect nested
archives, and does not attest that an arbitrary ZIP without valid NuGet
publication metadata is a published release.

## Retained package identities (local opt-in only)

| Vendor | Package SHA-256 | CLR assemblies | Frozen expected result |
|---|---|---:|---|
| Syncfusion `Syncfusion.Blazor.Toolkit` 2.0.1 | `87cf6795afe5106c9d6c4db5e63e7f0ef0c741a850243557b3bbab1854c6121d` | 3 (`net8.0`, `net9.0`, `net10.0`) | `unsigned-assembly` / `deterministic`: all three have CLR flags `0x1`, no signature reservation |
| Infragistics `IgniteUI.Blazor.Lite` 0.3.0 | `e9565c7c7093ff3798b62abdc2e67fe9bc438807eb0fa9b9b43f4573f9df0e63` | 3 (`net8.0`, `net9.0`, `net10.0`) | `signature-unverified` / `escalated`: all three have CLR flags `0x9` and nonzero 128-byte reservations, **not** verified signatures |

`retained-inputs.json` pins the vendor/package identities and per-entry
digests; `acceptance.json` freezes replay-ready results with relative
package paths but is not replayable until the exact retained packages are
bound/copied locally. `acceptance-receipt.template.json` records unfilled
Phase B replay slots. The actual archives never enter Git.

Run from the repository root; the bindings must both be set for the
required-retained mode:

```powershell
python eng\blazor-package-resolver\tests\pi01_expectations.py
python -O eng\blazor-package-resolver\tests\pi01_expectations.py
$env:PI01_SYNCFUSION_NUPKG = 'C:\path\to\syncfusion.blazor.toolkit.2.0.1.nupkg'
$env:PI01_INFRAGISTICS_NUPKG = 'C:\path\to\igniteui.blazor.lite.0.3.0.nupkg'
python eng\blazor-package-resolver\tests\pi01_expectations.py --require-retained
python -O eng\blazor-package-resolver\tests\pi01_expectations.py --require-retained
python eng\blazor-package-resolver\tests\pi01_expectations.py --check-negative-bindings
python -O eng\blazor-package-resolver\tests\pi01_expectations.py --check-negative-bindings
```

No PI-01 production handler, registry entry or implemented-count change
belongs to this Phase A freeze. Coverage stays six implemented and 54 TBD.
