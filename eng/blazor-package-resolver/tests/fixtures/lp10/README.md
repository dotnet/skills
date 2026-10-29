# LP-10 package-only expectation freeze (Phase A correction)

The pinned `repository-wide` LP-10 row in `data/rubric.json` is **"Every
packaged binary is attributable to public partner source or a referenced
NuGet/npm package with a clear upstream; released partner binaries map to the
public source commit."** It is a `Licensing and provenance` decomposition
evidence check of clause 2.5. The bundled
`data/requirement-basis.json` says: "Clean, attributable dependency graph.
Every binary in the package is either (a) built from source in the partner's
public repository, or (b) a referenced NuGet/npm package with a clear
upstream. No unattributed binaries." The copied rubric and crosswalk are
byte-pinned by `data/provenance.json`.

The [NuGet package creation reference](https://learn.microsoft.com/en-us/nuget/create-packages/creating-a-package)
describes the `.nupkg` as a ZIP with compiled code and other files; the
[nuspec reference](https://learn.microsoft.com/en-us/nuget/reference/nuspec#dependencies-element)
describes `<dependencies>` as *other packages to install*, not ownership of
files in this ZIP. [PDB symbols](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/symbols)
can associate source locations and binaries for debugging, but a PDB or
SourceLink-like claim is not release-source correspondence. There is no live
feed, source host, npm registry, build environment, shell or unstated
checkout in this expectation oracle.

## What the checked bytes can establish

Inventory *all* shipped code locations, including managed and native files
under `lib`, `ref`, `runtimes/*/lib`, `runtimes/*/native`, `native`, `tools`,
`analyzers`, `build`, `buildTransitive`, `contentFiles`, `staticwebassets`,
`wwwroot`, or elsewhere. JS bundles/modules/scripts count as **source-form
executable text** even when uncompressed UTF-8 rather than machine-code
binaries: excluding them would let bundled third-party executable code evade
attribution. Source-form scripts and inline/command-bearing MSBuild are also
in scope. A `.dll` extension or PE/ELF/Mach-O/WASM signature establishes only
a *candidate*, not a successfully parsed program, author, license, public
source, or attribution. The synthetic PE/ELF/Mach-O bytes are deliberately
signature-shaped, **not** release builds. Each target-framework variant and
each tool (whether referenced or not) is inventoried independently.

Detection is **magic-first**, then bounded ASCII-case-insensitive suffix and
content rules. PE, ELF, 32/64/fat Mach-O and WASM signatures are compiled
candidates even inside `.d.ts` or another passive-looking suffix. ZIP (also
`.nupkg`, `.jar`, `.dat`), gzip, `!<arch>` ar, CFB, CAB and `ustar` tar
signatures are **unsupported executable/container candidates**, not
silently treated as attributed or recursively unpacked. `.js`, `.mjs`,
`.cjs` (including uppercase `.JS`) and `.ps1`, `.psm1`, `.sh`, `.cmd`,
`.bat` at *any* location are source-form candidates when safely decodable.
A shebang at byte zero or immediately after a UTF-8 BOM is a source-form
candidate regardless of suffix; a mid-file shebang is not. `.js.gz` and
`.js.br` remain independent packaged candidates, not an already-counted
decompressed `.js`. A code suffix with incompatible bytes is unsupported.
Potentially consumer-compiled `.pp`, `.razor`, non-declaration `.ts/.mts/.cts`,
`.cs/.fs/.vb/.c/.cpp/.h/.hpp/.java/.py` and unknown nonempty or unknown
empty entries are unsupported until a safely inspectable source-form rule is
frozen. Opaque/nested files must never enable vacuous no-code.

Only the bounded allowlist can be passive **after magic, shebang, and active
content checks**: root nuspec as manifest (not payload), NuGet container
bookkeeping (`_rels/.rels`, `[Content_Types].xml`, `.psmdcp`), declarations
(`.d.ts/.d.mts/.d.cts`), symbol/source-map files, notices/licenses,
README/docs, ordinary images/CSS, specific supporting JSON names, safe
declaration-only build files and passive XML markup. Passive is **not**
attributed; it is merely outside this executable-code slice. Bundled
third-party CSS/JS upstream *listing* and license notices are separately
PI-08/PI-09 and LP-04. Package signing and assembly signatures belong to
PI-01 through PI-04. LP-02 public shipped source, LP-06 URL population,
LP-07 commit *shape*, PI-06/07/10 SBOM and digest binding, and PI-11
end-to-end build/publish provenance are distinct. These facts can support
later attribution but do not by themselves prove LP-10.

MSBuild `.props/.targets/.proj/.tasks/.overridetasks` and other recognized
`Project` XML are inspected **as inert raw bytes**. Never execute/evaluate
imports, conditions, SDKs, properties, tasks, shells or targets. Supported
root namespaces are empty and MSBuild 2003. The bounded UTF-8 parser
prohibits DTD/entities and does not resolve external references; malformed
XML, foreign `Project` roots, unsupported encoding, or limit exceedance
escalate with unknown applicability. Explicit active locators include inline
`UsingTask` `Task/Code` bodies; `Code/@Source`; `Exec`/`*.Exec` or
unrecognized/custom task children of a `Target`; `PreBuildEvent`,
`PostBuildEvent`, `RunCommand`, `*ToolExe`, `*ToolPath`,
`CustomBeforeMicrosoft*`, `CustomAfterMicrosoft*` and property functions.
Only a literal or `$(MSBuildThisFileDirectory)` `Code/@Source` path naming
one unambiguous in-package entry promotes a previously passive file to a
source-form candidate. Any unresolved source/factory is unsupported.
Reference-only `UsingTask AssemblyFile/AssemblyName`, imports and ordinary
items/properties are *not* attribution. References never remove or attribute
existing candidates, and paths outside the ZIP do not create packaged bytes.
Well-formed `html/svg/xslt/ps1xml` with script blocks, event handlers or
`javascript:` attributes are active text; passive markup, comments and
mere quoted `Exec` strings are not. Opaque/unsafe markup escalates.

## Total order and evidence contract

The checked package is the **only** replay input in Phase A. The existing
`schema_version: 1` result already supports applicable-but-escalated
`missing_fact`, `required_input` and `next_category`; future explicitly
SHA-256-pinned source snapshots, owner maps or release assets are permissible
without changing the schema. No metadata declaration, signature, PDB,
SourceLink file, SBOM, provenance document, notice or filename resemblance
switches the attribution branch.

| Priority | Checked-byte branch | Applicability / decision | Required input / next category |
|---|---|---|---|
| 1 | Missing/unsafe ZIP or nuspec, path aliases, corrupt XML, incomplete inventory or exceeded limits | `unknown` / `escalated` | Safe/complete unambiguous package, nuspec or bounded inspection / `package-owner` (missing checked input / `input-owner`) |
| 2 | Any unsupported executable-looking entry, unresolved source/factory, opaque/nested candidate | `applicable` / `escalated` | `safely-inspectable-payload-and-per-entry-source-map` / `package-owner` |
| 3 | Safely inventoried compiled code only | `applicable` / `escalated` | `per-entry-owner-and-public-release-commit-build-or-upstream-byte-map` / `source-owner` |
| 4 | Safely inventoried executable source-form text only | `applicable` / `escalated` | `per-entry-owner-and-public-release-commit-blob-or-upstream-byte-map` / `source-owner` |
| 5 | Both compiled and source-form text | `applicable` / `escalated` | `per-entry-compiled-build-and-text-blob-or-upstream-byte-maps` / `source-owner` |
| 6 | Only safely excluded passive/declaration entries | `applicable` / `deterministic`: vacuous **package-only** fact | None |

For compiled partner entries, map the exact packaged bytes and owner through
the release build to **public partner source at the released commit**. For
source-form text, map **exact packaged bytes** to a public release-commit
source blob; a binary-build map is not the text-source contract. Otherwise,
map each entry to a *referenced* NuGet/npm package with independently clear
upstream and byte/component correspondence. Neither a nuspec dependency
declaration nor an in-package `package.json` establishes which bundled file
it produced. If authoritative evidence is absent, escalate; do not claim a
negative attribution finding merely because package metadata is missing.
No-code holds only for this one safely inspected package and says nothing
about separately installed dependencies or repository-wide certification.
Sorted (ordinal) entry records pin original ZIP-entry SHA-256, size, class,
exclusion reason and MSBuild/markup structural locator count, first locator,
and SHA-256 of the full sorted locator/snippet list. A capped display snippet
is **never** an attribution transform; compare exact original bytes or
provide an explicit, independently verified transform map.

## Numeric inspection limits

The accepted shared reader already caps checked package bytes at **64 MiB**,
ZIP central directory at **8 MiB**, entries at **4096**, entry-name length
at **512** and root nuspec at **1 MiB**. LP-10 additionally freezes maximum
**8 MiB uncompressed per non-nuspec entry** and **32 MiB sum of non-nuspec
uncompressed entries**, both inclusive; header-declared size does not
authorize reading past the ceiling. MSBuild XML has **256 KiB UTF-8 bytes**,
**32 levels including the root**, and **4096 nodes including the root**
(each inclusive). The structural snippet display cap is **160 UTF-8 bytes**;
longer text records a truncated flag, while the entry SHA-256 still covers
every original byte. At/over cases for each ceiling are pinned in
`cases.json`: `per-entry-*`, `total-*`, `msbuild-xml-*`,
`msbuild-depth-*`, `msbuild-nodes-*`, `snippet-*`. Crossing an inventory
limit escalates as `incomplete-inventory`, never vacuous no-code. Nested
archives and precompressed payloads are **not recursively expanded**.
Large highly compressible synthetic controls use fixed ZIP_DEFLATED level 9
and still test *uncompressed* thresholds.

## Synthetic and retained controls

`cases.json` freezes **115 synthetic cases, 114 pinned input files with 114
distinct SHA-256 identities**, and one absent-input case. The controlled
families cover separate and mixed code (including vendor-shaped PE+JS with
and without repository/dependency claims), executable scripts and shebangs,
passive/declaration-only artifacts, source pointers, inline/command-bearing
MSBuild, active/passive markup, archive/XML safety and the numeric bounds.
Unchanged baseline archives retain their bytes; the generator regenerates
each archive in memory, checks its exact bytes/hash and compares complete
manifest documents. The golden empty archive SHA-256 remains
`02432c373761e7b24d4f2707f16592755b296bddb7c85541409b026aa1ef18dd`.
Both normal Python and `python -O` use explicit checks, not assertions.
Sorting is tested with reversed ZIP order; alias and backslash controls
check the **raw** ZIP central-directory names because Python's Windows ZIP
reader can normalize backslashes before returning names.

`retained-inputs.json` pins every independently inspected retained entry:
the root nuspec identity, **40/34 classified non-manifest entries** and
all SHA-256 hashes, lengths, categories, passive exclusion reasons and
hashed structural locators, not vendor bytes or workstation paths.
`code_candidates` and `excluded_entries` partition `scanned_entries`;
entry names and ZIP order are not themselves proof of source ownership.

| Archive | SHA-256 / entries | Safely classified candidates | Excluded, declaration and source claims | LP-10 |
|---|---|---|---|---|
| Syncfusion.Blazor.Toolkit 2.0.1 | `87cf6795afe5106c9d6c4db5e63e7f0ef0c741a850243557b3bbab1854c6121d` / 41 | 3 PE DLLs (`lib/net8.0`, `net9.0`, `net10.0`); 21 bundled `staticwebassets/scripts/*.js`; **2 active static-web-assets `.props`** with property functions (44 locators each). 26 candidates total. | 14 passive: 3 declaration-only `.props`, 3 XML API docs, CSS bundle, logo, notices/README, ZIP metadata, signature. Claims `https://github.com/syncfusion/blazor-toolkit.git` at `9d5991d49acef4fdf5930952e17d0a3daed63344`; per TFM (8/9/10) Components.Web 8.0.23/9.0.12/10.0.2 and Localization 8.0.23/9.0.12/10.0.2. | Mixed compiled+source text, applicable/escalated |
| IgniteUI.Blazor.Lite 0.3.0 | `e9565c7c7093ff3798b62abdc2e67fe9bc438807eb0fa9b9b43f4573f9df0e63` / 35 | 3 corresponding PE DLLs; 7 `staticwebassets/*.js` including `lit-html.js`; **2 active static-web-assets `.props`** with property functions (32 locators each). 12 candidates total. | 22 passive: 3 declaration-only `.props`, 3 XML API docs, `api.d.ts` type declaration, eight theme CSS files, README/LICENSE/THIRD-PARTY-LICENSES, ZIP metadata, signature. Claims `https://github.com/IgniteUI/igniteui-blazor` at `9995aef5bf771beb94551e4ebabfe6d90c7a4488`; per TFM Components.Web 8.0.0/9.0.0/10.0.0. | Mixed compiled+source text, applicable/escalated |

Both use one recognized 2013/05 root nuspec. Neither ZIP contains a
standalone `.pdb`, `.snupkg`, source map, SBOM, build attestation or
`package.json`. We did **not** inspect embedded debug/SourceLink data or
external release artifacts. The `.props` property functions are executable
source-form build logic and are therefore *not* passive references. A
script's suggestive name, third-party license file, repository claim or
package signature cannot establish its actual upstream. The retained
`acceptance.json` outcomes require *both* compiled-build and source-text
blob/upstream byte maps. `acceptance-receipt.template.json` records a future
replay, not an LP-10 pass.

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
all synthetic and retained replays must fail at the **LP-10 unimplemented
dispatcher mismatch**, while checked inputs/schema/hashes load successfully;
coverage remains **five implemented and 55 TBD**. Do not edit frozen
expectations to force a pass.
