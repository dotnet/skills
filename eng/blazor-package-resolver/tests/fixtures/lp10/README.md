# LP-10 package-only expectation freeze (Phase A)

The immutable `repository-wide` LP-10 row in `data/rubric.json` is **"Every
packaged binary is attributable to public partner source or a referenced
NuGet/npm package with a clear upstream; released partner binaries map to the
public source commit."** It is a `Licensing and provenance` decomposition
evidence check of clause 2.5. The bundled
`data/requirement-basis.json` says: "Clean, attributable dependency graph.
Every binary in the package is either (a) built from source in the partner's
public repository, or (b) a referenced NuGet/npm package with a clear
upstream. No unattributed binaries." The copied rubric and crosswalk are
byte-pinned by `data/provenance.json`; do not reinterpret a separate row as
LP-10 proof.

The [NuGet package creation reference](https://learn.microsoft.com/en-us/nuget/create-packages/creating-a-package)
describes the `.nupkg` as a ZIP containing compiled code and other files, and
the [nuspec reference](https://learn.microsoft.com/en-us/nuget/reference/nuspec#dependencies-element)
describes `<dependencies>` as *other packages to install*, not an ownership
map for files already in this ZIP. [PDB symbols](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/symbols)
can associate source locations and a binary for debugging; an unverified PDB
name or SourceLink-like JSON is not a verified release-build-to-source
correspondence. No live feed, source host, npm registry or unstated checkout is
consulted by this oracle.

## Evidence boundary

Inventory *all* distributable executable/compiled code entries, regardless
of archive location: managed PE assemblies in `lib/`, `ref/`,
`runtimes/*/lib/` and elsewhere; native PE/ELF/Mach-O libraries or programs in
`runtimes/*/native/`, `native/` or other locations; analyzers, source
generators, tasks and tools in `analyzers/`, `tools/`, `build/` and
`buildTransitive/`; and executable payloads in `content/`, `contentFiles/`,
`staticwebassets/`, `wwwroot/`, root or arbitrary folders. Include WebAssembly
modules and distributed JS bundles/scripts as code-attribution candidates
even though JS is text: excluding compiled/bundled JS merely because it has
no PE header would evade the clause. An embedded archive or opaque `.bin`
which might contain executable code cannot be waved away as non-code. Path
suffix and a PE/ELF/WASM magic prefix identify **candidates**, not valid
assemblies, author, license, upstream or a passing result. This matrix uses
deliberately PE/ELF-*shaped* bytes, not real compiled assemblies, plus a
minimal WASM module and a tiny JS module to test inventory boundaries, not
reproducible builds. The misleading-text `.dll` case must not pass vacuously.

Passive images, stylesheets, text documentation, licenses, XML comments,
package container machinery and the NuGet `.signature.p7s` are not compiled
code for this narrow LP-10 binary-attribution check. Bundled third-party
CSS/JS upstream *listing* and license notices are separate PI-08/PI-09 and
LP-04 obligations; JS code origin still belongs here. Embedded code in
opaque/nested resources needs inspection before a no-code finding. A
repository-wide row cannot be certified from one package: this result is a
factual statement about only the checked package's entries.

Two disjoint explanations are permissible **per code entry**: (a) partner
source in a **public** repository at the released source commit, with a
credible mapping from that exact entry's bytes through its build/release to
that commit; or (b) a *referenced* NuGet/npm package and clear, independently
verified upstream with an explicit mapping to that bundled entry. A nuspec
NuGet dependency is a declaration for a separately restored package, not an
identification of a bundled DLL. An in-package `package.json` is a claim, not
verification that the bundled JS came from that npm version. Similar names
or file hashes without a verified owner/build link do not establish either
explanation. Every `lib/net8.0`, `lib/net9.0` and `lib/net10.0` binary needs
its own correspondence; one assembly's match does not cover its siblings.

Checked ZIP bytes determine entry names, order, exact size/SHA-256 and
recognizable format hints, embedded manifest ID/version, declared NuGet
dependency groups, repository URL/commit **claims**, and presence/absence of
supporting files. They cannot establish public availability or existence of
the repository commit, a released build's derivation from that commit, a
file's owner, an npm/NuGet upstream's identity, transitively restored package
contents, or correctness of an SBOM/provenance claim. The following are
independent: LP-02 public shipped source; LP-06 RepositoryUrl population;
LP-07 commit *shape*; PI-01/02 assembly signing, PI-03/04 package signing;
PI-06/07/08/10 SBOM and upstream *enumeration*/digest binding; PI-11
end-to-end build/publish provenance. SourceLink/PDB debug evidence and a
declaration of a dependency are neither substitutes for LP-10 per-file
correspondence nor automatically required forms of it. A supplied
RepositoryUrl and full-looking RepositoryCommit **never** cause a pass here.
Missing fields or supporting artifacts alone are not deterministic
attribution failures either: external evidence may still establish a link.

This Phase A uses **one checked `package` role only**. The existing
`schema_version: 1` replay accepts other explicitly pinned, SHA-256-checked
roles (`repository snapshot`, `owner record`, `release assets`, etc.) and
`CaseResult` accepts an `applicable` escalation with `missing_fact`,
`required_input` and `next_category`; no schema change or ambient access is
needed. Future positive branches must specify and pin exact source
commit/tree or release-built artifact evidence, a per-entry owner/build map,
and for claimed third-party payloads the referenced NuGet/npm identity,
actual upstream and byte/component correspondence. Validate each against
the checked entry, not simply metadata text. If those independently
verifiable inputs are not supplied, **escalate**. Phase A deliberately
freezes missing-input branches rather than inventing a positive example
whose fake bytes claim public provenance.

| Branch family | Applicability / decision | Required input / next category |
|---|---|---|
| Safely inventoried package with no executable/code candidates (including separately declared dependencies only) | `applicable` / `deterministic`: vacuous **package-only** fact | None |
| PE/ELF, analyzer, tool, runtime or build executable without public source/build correspondence | `applicable` / `escalated` | `pinned-public-source-commit-and-binary-build-map` / `source-owner` |
| JS, WASM, unknown ownership, bundled declared NuGet/npm payloads | `applicable` / `escalated` | `per-entry-owner-and-source-or-upstream-map` / `package-owner` |
| Opaque/misleading/nested payload | `applicable` / `escalated` | `payload-format-and-per-entry-owner-map` / `package-owner` |
| Present or malformed PDB/SourceLink-like file, SBOM or attestation | `applicable` / `escalated` | `verified-per-entry-source-build-or-upstream-map` or `valid-verified-per-entry-source-build-or-upstream-map` / `release-owner` |
| Ambiguous manifest/ZIP, unsafe XML/path, missing root nuspec, corrupt archive, absent package input | `unknown` / `escalated` | Appropriate `unambiguous-nuspec`, `safe-nuspec`, `safe-unambiguous-nupkg`, `valid-nupkg`, `package`, etc. / `package-owner` or `input-owner` |

Unknown means there is no safe authoritative inventory/manifest to decide the
per-package obligation, not that a malformed archive proves source misconduct.
The branch priority is checked-input/archive safety, authoritative nuspec
metadata, then inventory and the attribution evidence. The result's
`evidence_contract` pins candidate paths, sizes, SHA-256 and format **hints**
where safely available; no `finding` is emitted for an escalation. The
`evidence_references` and `evidence_pointers` cite checked ZIP bytes or
manifest metadata, never a made-up public source inspection.

## Frozen controls

`cases.json` freezes **43 synthetic cases, 42 uniquely hashed package
inputs**, including three no-code cases and one no-input case. The controlled
matrix covers managed/reference/native (PE, ELF and Mach-O)/runtime/
analyzer/tool/build/buildTransitive/script code, JS in
static web assets and `contentFiles`, WASM, opaque/nested resources,
dependency-only versus bundled dependency, repository URL and SHA separately,
nuget/npm declarations without file ownership, matching-looking or malformed
symbol/SourceLink and SBOM/provenance files, duplicated repository/metadata,
foreign metadata sibling, DTD, malformed XML, ambiguous/missing root nuspec,
duplicate/traversal ZIP paths and unreadable bytes. A package dependency
without embedded code is vacuous **only about this package**, not about the
transitive dependency or its license. XML/ZIP safety branches deliberately
escalate rather than choose first entries.

`lp10_expectations.py` regenerates every synthetic archive **in memory**,
pins portable ZIP creator/mode/date/compression/order and every result field,
checks the exact stored bytes and SHA-256 of all entries, validates the XML,
dependency and code-format controls, and compares all four generated JSON
documents byte-for-byte. The golden empty archive SHA-256 is
`02432c373761e7b24d4f2707f16592755b296bddb7c85541409b026aa1ef18dd`.
The code uses explicit exceptions instead of assertions, so `python -O`
performs the same checks.

## Exact retained archive observations

`retained-inputs.json` fixes the independently observed entry paths,
uncompressed lengths and SHA-256 hashes for **every** `*.dll` and `*.js` in
both pinned archives; it also records archive/nuspec identity, dependency
groups, passive CSS, present supporting artifacts and absent standalone
provenance artifacts. No retained commercial bytes or absolute workstation
paths are committed. These are byte observations, **not** release/source
builds, public-repository verifications or verdicts from earlier work.

| Archive | Exact SHA-256 / entries | In-scope code entries | Nuspec source claim and declared dependencies | Other observed artifacts / LP-10 |
|---|---|---|---|---|
| Syncfusion.Blazor.Toolkit 2.0.1 | `87cf6795afe5106c9d6c4db5e63e7f0ef0c741a850243557b3bbab1854c6121d` / 41 | 3 PE-signature `lib/net{8,9,10}.0/Syncfusion.Blazor.Toolkit.dll`; 21 `staticwebassets/scripts/*.js` | `https://github.com/syncfusion/blazor-toolkit.git`, `9d5991d49acef4fdf5930952e17d0a3daed63344`; net8/9/10 `Microsoft.AspNetCore.Components.Web` 8.0.23/9.0.12/10.0.2 and `Microsoft.Extensions.Localization` 8.0.23/9.0.12/10.0.2 | Generated CSS bundle, LICENSE, README, package signature; **applicable/escalated** |
| IgniteUI.Blazor.Lite 0.3.0 | `e9565c7c7093ff3798b62abdc2e67fe9bc438807eb0fa9b9b43f4573f9df0e63` / 35 | 3 PE-signature `lib/net{8,9,10}.0/IgniteUI.Blazor.Lite.dll`; 7 `staticwebassets/*.js` including `lit-html.js` | `https://github.com/IgniteUI/igniteui-blazor`, `9995aef5bf771beb94551e4ebabfe6d90c7a4488`; net8/9/10 `Microsoft.AspNetCore.Components.Web` 8.0.0/9.0.0/10.0.0 | Eight theme CSS files, LICENSE, THIRD-PARTY-LICENSES, README, package signature; **applicable/escalated** |

Both use one recognized 2013/05 root nuspec. Neither ZIP contains a
standalone `.pdb`, `.snupkg`, source map, SBOM, build attestation or
`package.json`. We did **not** inspect embedded PDB/SourceLink data or
external release artifacts. In particular, a script name such as
`lit-html.js`, a third-party license file, an exact-looking repository commit
or a package signature cannot independently prove which upstream produced
the script or which public source commit produced any of the DLLs. Each
retained `acceptance.json` result therefore names
`per-entry-owner-and-source-or-upstream-map` from `package-owner`. The
`acceptance-receipt.template.json` records a future replay, not an assertion
that this TBD resolver has passed.

Run from the repository root:

```powershell
python eng\blazor-package-resolver\tests\lp10_expectations.py
python -O eng\blazor-package-resolver\tests\lp10_expectations.py
python eng\blazor-package-resolver\tests\lp10_expectations.py --check-negative-bindings
```

Set `LP10_SYNCFUSION_NUPKG` and `LP10_INFRAGISTICS_NUPKG` to the **exact
retained local files**, then run `--require-retained` with normal Python and
`python -O`. Unbound normal mode prints `skipped`, required mode fails closed,
and `--check-negative-bindings` proves missing, misspelled and wrong-existing
bindings cannot pass in either interpreter mode. To replay a retained case
*after* Phase B, copy `acceptance.json` and the two verified archives into a
temporary sibling `packages\` directory and capture a receipt. In Phase A,
both synthetic and retained replays must fail at the **LP-10 unimplemented
dispatcher mismatch**, while the inputs/schema/hash validations succeed;
registry coverage remains **five implemented and 55 TBD**. Do not edit the
frozen expected results merely to make replay pass.
