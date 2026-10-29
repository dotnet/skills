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
nonzero signature requires RSA verification against the Assembly metadata
public key: a valid signature is a deterministic non-vacuous positive,
an invalid or tampered signature is a deterministic negative, and an
unsupported algorithm/key shape escalates with its exact reason.
A safely inventoried package with no CLR assemblies has a *vacuous,
package-only* positive finding; separately installed dependencies and
other releases remain untested. Cryptographically validating every
assembly signature against its metadata public key, and detecting
post-signing modification, is required for a signature-present positive
finding. The expectation oracle verifies RSA PKCS#1 v1.5 with supported
SHA-1/SHA-256 keys over unpadded PE headers and section raw data, omitting
the signature reservation and zeroing the PE checksum and Authenticode
directory. A nonzero reservation and a CLR flag alone are **not**
validation. The verifier treats every nonempty PE overlay outside declared
sections as incomplete inventory unless it is exactly a bounded,
8-byte-aligned WIN_CERTIFICATE table with zero-filled alignment and
well-formed DER PKCS#7 SignedData containing Authenticode
`SpcIndirectDataContent` (`1.3.6.1.4.1.311.2.1.4`) for a PE image.
It parses the supported digest, X.509 certificate and signer structures,
rejects unknown signed/unsigned attributes and unsupported content OIDs,
and screens certificate bytes for nested PE, ZIP, gzip, CAB and 7z content.
An arbitrary `id-data` SignedData envelope is not fully inventoried.
This validates the certificate **structure only**, not Authenticode
publisher identity or trust (the separate PI-02 row); bytes before or
after the table, nonzero 1-7-byte WIN_CERTIFICATE alignment padding, or
nonzero bytes in unhashed PE header/section padding escalate. Thus a verified outer assembly
with an appended unsigned PE cannot become a package-wide positive.
NuGet signing (`.signature.p7s`), nuspec repository
URL/commit, PDBs, SourceLink, strong-name token strings and publisher
claims cannot substitute. No source checkout or network query is implied
by the presence of a repository URL; repository evidence cannot turn an
unsigned shipped assembly into a signed one.

Prioritize unsafe/ambiguous archives, malformed metadata, unclassifiable
PE/CLI candidates and opaque native/bundle or nested containers
(`unknown` / `escalated`) over any individual missing signature. An
otherwise valid native PE is **not** proof that a .NET single-file bundle
is absent. Embedded ZIP local, central, or EOCD signatures (including
prefixed ZIP content) override a passive file suffix; archive extensions
are opaque even without a recognizable magic. This expectation oracle
does not recursively unpack any entry and cannot silently turn unknown
nonempty assets into a no-assembly claim. Then prioritize delay-signed,
public-signed, unsigned, invalid cryptographic signatures, and unsupported
algorithm/key shapes. A fully verified assembly inventory yields a
non-vacuous positive; only a safe, complete no-assembly inventory yields
the vacuous finding. A module-only `.netmodule` is not an assembly: this
oracle permits the exact independently validated pinned module specimen,
and escalates unverified arbitrary module metadata rather than treating
a cleared Assembly-table bit as proof of a valid module. The expected
result shape is the existing `CaseResult` contract, not a readiness
verdict. `pi01_expectations.py` uses standard-library ZIP/PE preflight and
the compiled `template/Validator.csproj` for .NET metadata and
cryptographic verification; both are **expectation-only**, not a shipping
resolver. Decisive signing controls are real
SDK-compiled managed images with one Module row and, except for the
module-only control, one Assembly row. `template/Fixture.csproj`,
`template/Fixture.cs.txt` and the **public-only** `template/public.snk`
produce four pinned binaries under `templates/`; `nonzero.dll` is read
directly from its pinned template (not reconstructed in memory) and was derived
from the public-signed binary by changing one byte inside its reserved
signature, without touching its valid Module or Assembly metadata. All
six image hashes are pinned. `verified.dll` was fully signed with an
ephemeral 2048-bit RSA test key; its private material was deleted after
the signed image was pinned. The fixture and its embedded public key suffice
to verify it, but a rebuild with a new throwaway key changes its hash.
`template/Validator.csproj` uses .NET
`System.Reflection.Metadata` to independently validate the metadata
tables, public-key presence, flags and reservations. Before *any* decisive
assembly state it checks all present rows in the supported table schemas,
row lengths, #Strings/#Blob/#GUID heap indices, direct/coded table
references, ordered nonzero list starts, required names, signatures, MVIDs
and public keys; optional null constants and absent public keys are
permitted only where the metadata schema allows them. Unexpected table
shapes escalate. In particular a later TypeDef Name index 65535 outside
the 310-byte #Strings heap is unsafe, not unsigned.
Companion controls corrupt a MethodDef #blob signature index, a missing MethodDef.Signature, and a
CustomAttribute coded parent reference to prove the whole table traversal
fails closed, not merely the first type definition.
The `.cs.txt`
sources are explicitly compiled by their projects; normal `ResolverChecks`
does not accidentally compile them. Their intermediates go to its
already-excluded `tests/obj` rather than nested fixture folders. Malformed
synthetic PE headers remain escalation controls only; fake Assembly rows cannot
establish a decisive outcome. A forged Module+Assembly row-count control
with empty definitions also escalates. The nonzero-signature surrogate
fails RSA verification; changing either the signature bytes or the
signed PE content of the genuine control also produces `invalid-signature`.
The acceptance results retain each assembly's exact path, byte count and
entry SHA-256 without copying either proprietary archive into this repo.
Phase B must implement and replay this frozen verification contract; it
must never promote structural signature presence to verified validity.

The frozen matrix has 104 cases, of which 103 have independently pinned
synthetic archives. It covers empty and non-assembly archives, native PE
and bundle-shaped input, nested/prefixed unsigned ZIP and archive-suffix controls,
different assembly locations and suffixes, multiple framework variants,
genuine unsigned, delay-signed, public-signed and verified-signed images,
invalid signatures from both byte- and signed-content tampering, unsupported
strong-name algorithms, nonzero signature
reservations and verified module-only metadata, conflicting and
malformed CLI structure, ambiguous ZIP names/paths, corrupt ZIP, UTF-16
nuspec/DTD and harmless comment decoys. Compressed nuspecs at and over
1 MiB, including an underdeclared inflated entry, pin the declared-size
check followed by bounded streaming **before** XML allocation. Payload
entries are similarly streamed with explicit byte caps. Nine runtime-generated
exact-boundary controls exercise at/over entry bytes,
total uncompressed bytes, ZIP name length and entry count, plus
over-limit package bytes without committing giant archives.
Overlay controls distinguish an appended unsigned PE, nonzero unhashed
header padding, a genuinely signed, public-only Authenticode-profile CMS
certificate, a certificate followed by an unsigned PE, an invalid
certificate record, a PE hidden inside certificate data, arbitrary
`id-data` and unknown eContent OIDs, gzip/CAB/7z payloads (including
gzip-like content under an otherwise supported Authenticode OID), and both
nonzero and MZ bytes in certificate alignment padding (including exact
`dwLength=45` with three alignment bytes). The CMS fixture is signed
with a disposable throwaway key and contains its public X.509 certificate,
but never its private key. Its indirect-data PE digest is a synthetic
placeholder, not an Authenticode verification of the enclosing PE.
The oracle pins its SHA-256
`ea7d26478fd30cb5c56fe0845fa92a10d9de8a7a3ee6ac6e73a334e979f0f885`.
The frozen branch counts are:
`verified-signed` 8, `invalid-signature` 3,
`signature-unverified` 1 (unsupported hash), `unsigned-assembly` 10,
`delay-signed` 4, `public-signed` 4, `no-assemblies` 6,
`opaque-container` 14, `unsafe-assembly` 37, `unsafe-package` 16, and
`missing-package` 1.
The generator pins every archive byte and expected JSON document and
rejects extra/missing synthetic `.nupkg` files. Resource bounds in this
expectation oracle are 64 MiB archive bytes, 4096 entries, 1024 characters
per ZIP name, 1 MiB nuspec, 8 MiB per entry, 32 MiB total non-manifest
uncompressed bytes; production Phase B must independently test and choose
its own safe limits. The oracle does not recursively inspect nested
archives, and does not attest that an arbitrary ZIP without valid NuGet
publication metadata is a published release.

The six image SHA-256 digests are pinned in `pi01_expectations.py`;
`template/public.snk` is an RSA PUBLICKEYBLOB (`0x06`), not a private key.
Rebuild from the `template` project with .NET 10 reference packs and
compare hashes before replacing frozen binaries:

```powershell
dotnet build eng\blazor-package-resolver\tests\fixtures\pi01\template\Fixture.csproj -c Release -t:Rebuild
dotnet build eng\blazor-package-resolver\tests\fixtures\pi01\template\Fixture.csproj -c Release -t:Rebuild -p:SignAssembly=true -p:DelaySign=true -p:AssemblyOriginatorKeyFile=public.snk
dotnet build eng\blazor-package-resolver\tests\fixtures\pi01\template\Fixture.csproj -c Release -t:Rebuild -p:SignAssembly=true -p:PublicSign=true -p:AssemblyOriginatorKeyFile=public.snk
dotnet build eng\blazor-package-resolver\tests\fixtures\pi01\template\Fixture.csproj -c Release -t:Rebuild -p:OutputType=Module -p:ProduceReferenceAssembly=false
```

Each build overwrites the same output by default: preserve each variant
separately before building the next. The SDK's `PathMap` and deterministic
compilation yield byte-identical images from those four pinned inputs.
To create a **new** fully signed control, run
`Validator.dll --generate-key <temporary-private-key.snk>` and rebuild
with `-p:SignAssembly=true -p:AssemblyOriginatorKeyFile=<absolute-private-key-path>`;
preserve the resulting assembly, delete the private key, and update the
explicitly pinned hash and frozen expectations. The existing pinned
`verified.dll` does not require the private key for validation.
`--write-fixtures` never rewrites either signed or nonzero templates.

## Retained package identities (local opt-in only)

| Vendor | Package SHA-256 | CLR assemblies | Frozen expected result |
|---|---|---:|---|
| Syncfusion `Syncfusion.Blazor.Toolkit` 2.0.1 | `87cf6795afe5106c9d6c4db5e63e7f0ef0c741a850243557b3bbab1854c6121d` | 3 (`net8.0`, `net9.0`, `net10.0`) | `unsigned-assembly` / `deterministic`: all have CLR flags `0x1`, 0/0 signature reservation, no public key |
| Infragistics `IgniteUI.Blazor.Lite` 0.3.0 | `e9565c7c7093ff3798b62abdc2e67fe9bc438807eb0fa9b9b43f4573f9df0e63` | 3 (`net8.0`, `net9.0`, `net10.0`) | `verified-signed` / `deterministic`: all have CLR flags `0x9`, nonzero 128-byte reservations, a 160-byte public key, and valid RSA PKCS#1 v1.5 SHA-1 signatures |

Each retained Infragistics PE also contains an exactly 12,280-byte
WIN_CERTIFICATE table with the supported Authenticode
`SpcIndirectDataContent` profile, matched signer certificates and an
RFC 3161 timestamp. Its certificate structure is inventoried here;
certificate-chain trust, publisher identity and the Authenticode PE
digest remain outside PI-01.

`retained-inputs.json` pins the vendor/package identities and per-entry
digests and per-assembly PE hash, key hash, algorithm and cryptographic
outcome; `acceptance.json` freezes replay-ready results with relative
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
dotnet run --project eng\blazor-package-resolver\tests\fixtures\pi01\template\Validator.csproj --configuration Release -- eng\blazor-package-resolver\tests\fixtures\pi01\templates\unsigned.dll eng\blazor-package-resolver\tests\fixtures\pi01\templates\delay.dll eng\blazor-package-resolver\tests\fixtures\pi01\templates\public.dll eng\blazor-package-resolver\tests\fixtures\pi01\templates\module.netmodule eng\blazor-package-resolver\tests\fixtures\pi01\templates\nonzero.dll eng\blazor-package-resolver\tests\fixtures\pi01\templates\verified.dll
dotnet run --project eng\blazor-package-resolver\tests\fixtures\pi01\template\Validator.csproj --configuration Release -- --retained $env:PI01_SYNCFUSION_NUPKG $env:PI01_INFRAGISTICS_NUPKG
```

No PI-01 production handler, registry entry or implemented-count change
belongs to this Phase A freeze. Coverage stays six implemented and 54 TBD.
