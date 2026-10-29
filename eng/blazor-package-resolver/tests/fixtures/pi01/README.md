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
assembly (flag clear, no reservation), a real Roslyn `DelaySign=true`
assembly (flag clear, nonzero but zero-filled reservation), and a
`PublicSign=true` assembly (flag set, zero-filled reservation) are **three
distinct decisive package-specific negative findings**. Flag clear with
a nonzero signature is structurally inconsistent and escalates. A flag-set
nonzero signature is not yet a verified signature and also escalates.
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

Prioritize unsafe/ambiguous archives, malformed metadata, unclassifiable
PE/CLI candidates and opaque native/bundle or nested containers
(`unknown` / `escalated`) over any individual missing signature. An
otherwise valid native PE is **not** proof that a .NET single-file bundle
is absent. ZIP magic overrides a passive file suffix; archive extensions
are opaque even without a recognizable magic. This expectation oracle
does not recursively unpack any entry and cannot silently turn unknown
nonempty assets into a no-assembly claim. Then prioritize delay-signed,
public-signed, unsigned, and finally signature-present-but-cryptographically-
unverified entries; only a safe, complete no-assembly inventory yields
the vacuous finding. A module-only `.netmodule` is not an assembly: this
oracle permits the exact independently validated pinned module specimen,
and escalates unverified arbitrary module metadata rather than treating
a cleared Assembly-table bit as proof of a valid module. The expected
result shape is the existing `CaseResult` contract, not a readiness
verdict. `pi01_expectations.py` uses standard-library structural inspection
as an independent **expectation oracle**, not a shipping resolver or a
cryptographic strong-name verifier. Decisive signing controls are real
SDK-compiled managed images with one Module row and, except for the
module-only control, one Assembly row. `template/Fixture.csproj`,
`template/Fixture.cs.txt` and the **public-only** `template/public.snk`
produce four pinned binaries under `templates/`; `nonzero.dll` is derived
from the public-signed binary by changing one byte inside its reserved
signature, without touching its valid Module or Assembly metadata. All
five image hashes are pinned; no private signing key is retained.
`template/Validator.csproj` uses .NET
`System.Reflection.Metadata` to independently validate the metadata
tables, public-key presence, flags and reservations. The `.cs.txt`
sources are explicitly compiled by their projects; normal `ResolverChecks`
does not accidentally compile them. Their intermediates go to its
already-excluded `tests/obj` rather than nested fixture folders. Malformed
synthetic PE headers remain escalation controls only; fake Assembly rows cannot
establish a decisive outcome. A synthetic flag-set nonzero reservation
is deliberately not cryptographically valid or a positive control.
The acceptance results retain each assembly's exact path, byte count and
entry SHA-256 without copying either proprietary archive into this repo.
The unresolved evidence decision for Phase B is the implementation and
test vectors for actual cryptographic verification (including valid and
tampered signatures); a structural presence test
must never be promoted to "verified".

The frozen matrix has 77 cases, of which 76 have independently pinned
synthetic archives. It covers empty and non-assembly archives, native PE
and bundle-shaped input, nested unsigned ZIP and archive-suffix controls,
different assembly locations and suffixes, multiple framework variants,
genuine unsigned, delay-signed and public-signed images, nonzero signature
reservations and verified module-only metadata, conflicting and
malformed CLI structure, ambiguous ZIP names/paths, corrupt ZIP, UTF-16
nuspec/DTD and harmless comment decoys. Compressed nuspecs at and over
1 MiB, including an underdeclared inflated entry, pin the declared-size
check followed by bounded streaming **before** XML allocation. Payload
entries are similarly streamed with explicit byte caps. Nine runtime-generated
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

The five image SHA-256 digests are pinned in `pi01_expectations.py`;
`template/public.snk` is an RSA PUBLICKEYBLOB (`0x06`), not a private key.
Rebuild from the `template` project with .NET 10 reference packs and
compare hashes before replacing frozen binaries:

```powershell
dotnet build eng\blazor-package-resolver\tests\fixtures\pi01\template\Fixture.csproj -c Release -t:Rebuild
dotnet build eng\blazor-package-resolver\tests\fixtures\pi01\template\Fixture.csproj -c Release -t:Rebuild -p:SignAssembly=true -p:DelaySign=true -p:AssemblyOriginatorKeyFile=public.snk
dotnet build eng\blazor-package-resolver\tests\fixtures\pi01\template\Fixture.csproj -c Release -t:Rebuild -p:SignAssembly=true -p:PublicSign=true -p:AssemblyOriginatorKeyFile=public.snk
dotnet build eng\blazor-package-resolver\tests\fixtures\pi01\template\Fixture.csproj -c Release -t:Rebuild -p:OutputType=Module -p:ProduceReferenceAssembly=false
python eng\blazor-package-resolver\tests\pi01_expectations.py --write-fixtures
```

Each build overwrites the same output by default: preserve each variant
separately before building the next. The SDK's `PathMap` and deterministic
compilation yield byte-identical images from these pinned inputs.

## Retained package identities (local opt-in only)

| Vendor | Package SHA-256 | CLR assemblies | Frozen expected result |
|---|---|---:|---|
| Syncfusion `Syncfusion.Blazor.Toolkit` 2.0.1 | `87cf6795afe5106c9d6c4db5e63e7f0ef0c741a850243557b3bbab1854c6121d` | 3 (`net8.0`, `net9.0`, `net10.0`) | `unsigned-assembly` / `deterministic`: all have CLR flags `0x1`, 0/0 signature reservation, no public key |
| Infragistics `IgniteUI.Blazor.Lite` 0.3.0 | `e9565c7c7093ff3798b62abdc2e67fe9bc438807eb0fa9b9b43f4573f9df0e63` | 3 (`net8.0`, `net9.0`, `net10.0`) | `signature-unverified` / `escalated`: all have CLR flags `0x9`, nonzero 128-byte reservations and a 160-byte public key, **not** verified signatures |

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
dotnet run --project eng\blazor-package-resolver\tests\fixtures\pi01\template\Validator.csproj --configuration Release -- eng\blazor-package-resolver\tests\fixtures\pi01\templates\unsigned.dll eng\blazor-package-resolver\tests\fixtures\pi01\templates\delay.dll eng\blazor-package-resolver\tests\fixtures\pi01\templates\public.dll eng\blazor-package-resolver\tests\fixtures\pi01\templates\module.netmodule eng\blazor-package-resolver\tests\fixtures\pi01\templates\nonzero.dll
dotnet run --project eng\blazor-package-resolver\tests\fixtures\pi01\template\Validator.csproj --configuration Release -- --retained $env:PI01_SYNCFUSION_NUPKG $env:PI01_INFRAGISTICS_NUPKG
```

No PI-01 production handler, registry entry or implemented-count change
belongs to this Phase A freeze. Coverage stays six implemented and 54 TBD.
