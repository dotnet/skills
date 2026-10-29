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
`.nupkg`, `.jar`, `.dat`), gzip, `!<arch>` ar, CFB, CAB, `ustar` tar, 7z,
xz, zstd, bzip2, and RAR 4/5 signatures are **unsupported
executable/container candidates**, even inside passive-looking `.png`,
`.pdb`, `.js.map` or `.txt` files; they are neither attributed nor
recursively unpacked. Container formats without reliable signatures remain
a bounded detection limitation. Unknown nonempty files and recognized
precompressed code suffixes still fail closed as specified below. `.js`, `.mjs`,
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

MSBuild `.props/.targets/.proj/.tasks/.overridetasks` and nonstandard
extensions with an actual `Project` XML root (after a BOM, declaration and
comments, not a quoted `<Project>` in prose or a JS comment) are inspected
**as inert raw bytes**. Never execute/evaluate imports, conditions, SDKs,
properties, tasks, shells or targets. Supported root namespaces are empty
and MSBuild 2003. Safely declared UTF-8 and BOM-marked UTF-16 LE/BE are
decoded strictly; DTD/entities are prohibited, and the XML parser does not
resolve external references. Unsupported encodings, malformed XML,
foreign roots and limit exceedance escalate with unknown applicability;
UTF-16 without a BOM and invalid UTF-8 `Project` roots cannot become
passive documentation. DTD/entity detection checks actual XML declaration
tokens, not matching words inside harmless XML comments or CDATA. No-code
nuspec-comment and declaration-only `.props` comment/CDATA decoys preserve
this boundary without relaxing the UTF-16 DTD/entity safety controls.
A nonstandard file whose `Project` root follows an otherwise valid
processing instruction or an actual DOCTYPE/entity prolog is **not**
passive: it escalates as incomplete/unknown, even when its only caller is
a non-command MSBuild task. The synthetic caller controls pin both cases;
DOCTYPE-prefixed HTML without a `Project` root remains unsupported markup.
The internal-subset comment/quote control fails closed even when a quote in
a DTD comment would move a naive delimiter scanner past the actual root.
Nuspec inspection uses the same encoding-aware DTD/entity prohibition
before metadata can be cited; UTF-16 benign, DTD and entity controls on
both no-code and compiled archives pin structural precedence.

Supported `Import/@Project` paths are literal relatives or begin
`$(MSBuildThisFileDirectory)`; separators/dot segments are normalized
against the importer directory, and only a unique, safe in-package target
is re-inspected as MSBuild regardless of suffix. No imported file runs.
Cycles, excessive depth/count, wildcards, other properties, absent or
out-of-package targets, and unsafe target XML escalate as incomplete
inventory. External references remain notes, not packaged-byte attribution;
an unresolved import cannot justify a no-code finding. A `Code/@Source`
promotes a safe in-package file **only** when prefixed with
`$(MSBuildThisFileDirectory)`; bare literals and unresolved expressions
escalate. Reference-only `UsingTask AssemblyFile/AssemblyName`, including
`TaskHostFactory` without a body, and non-command built-in/custom task
invocations inside targets are declarations/orchestration, not packaged
code. Inline `UsingTask` task/code bodies, `Exec`/`*.Exec` task children,
command-bearing `PreBuildEvent`, `PostBuildEvent`, `RunCommand`,
`*ToolExe`, `*ToolPath`, `CustomBeforeMicrosoft*`,
`CustomAfterMicrosoft*`, and unsupported inline syntax (including
nested `Exec` under a custom task) are executable
or unsafe candidates. References never remove, reclassify or attribute
independently inventoried packaged tools.

The **sole passive property-function pattern** is an exact
`$([System.IO.Path]::GetFullPath('$(MSBuildThisFileDirectory)<relative>'))`
expression in an `ItemGroup` item attribute or item metadata, with a
normalized safe relative literal and no other/nested function. This
declarative SDK static-web-assets path expression is not an executable
source candidate. In conditions, property values or any other context,
or for any unknown static/instance function, classification escalates as
unsupported executable content. Well-formed `html/svg/xslt/ps1xml` with
script blocks, event handlers or `javascript:` attributes are active text;
passive markup, comments and mere quoted `Exec` strings are not.
Non-XML HTML such as `<!DOCTYPE html><script>...` or void elements is
unsupported markup, not silently passive.

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
| 4 | Safely inventoried executable source-form text only | `applicable` / `escalated` | `per-entry-owner-and-public-release-commit-blob-or-verified-transform-or-upstream-byte-map` / `source-owner` |
| 5 | Both compiled and source-form text | `applicable` / `escalated` | `per-entry-compiled-build-and-text-blob-or-verified-transform-or-upstream-byte-maps` / `source-owner` |
| 6 | Only safely excluded passive/declaration entries | `applicable` / `deterministic`: vacuous **package-only** fact | None |

For compiled partner entries, map the exact packaged bytes and owner through
the release build to **public partner source at the released commit**. For
source-form text, map **exact packaged bytes** to a public release-commit
source blob **or** verify a reproducible build/transform from the public
release commit to those exact packaged bytes; a bare binary-build claim is
not the text-source contract. Line-ending changes, BOM changes,
minification, bundling and precompression require an explicit pinned
transform, never fuzzy normalization. Otherwise,
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
authorize reading past the ceiling. MSBuild XML has **256 KiB original bytes**,
**32 levels including the root**, and **4096 nodes including the root**
(each inclusive). An import traversal is capped at **8 files on a chain
including the importing root**, and **32 resolved Import declarations**
across the package (inclusive). Cycles and unresolved imports escalate.
The structural snippet display cap is **160 UTF-8 bytes**;
longer text records a truncated flag, while the entry SHA-256 still covers
every original byte. At/over cases for each ceiling are pinned in
`cases.json`: `per-entry-*`, `total-*`, `msbuild-xml-*`,
`msbuild-depth-*`, `msbuild-nodes-*`, `import-depth-*`,
`import-count-*`, `nonstandard-root-*`, `snippet-*`. Crossing an inventory
limit escalates as `incomplete-inventory`, never vacuous no-code. Nested
archives and precompressed payloads are **not recursively expanded**.
Large highly compressible synthetic controls use fixed ZIP_DEFLATED level 9
and still test *uncompressed* thresholds.

## Synthetic and retained controls

`cases.json` freezes **169 synthetic cases, 168 pinned input files with 168
distinct SHA-256 identities**, and one absent-input case. The controlled
families cover separate and mixed code (including vendor-shaped PE+JS with
and without repository/dependency claims), executable scripts and shebangs,
passive/declaration-only artifacts, safe versus unresolved imports,
encoding-aware UTF-8/UTF-16 nuspec and MSBuild (including LE/BE and
BOM-prefixed imported files), source pointers, container signatures,
non-XML HTML, inline/command-bearing MSBuild, active/passive markup,
archive/XML safety and the numeric bounds.
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
| Syncfusion.Blazor.Toolkit 2.0.1 | `87cf6795afe5106c9d6c4db5e63e7f0ef0c741a850243557b3bbab1854c6121d` / 41 | 3 PE DLLs (`lib/net8.0`, `net9.0`, `net10.0`); 21 bundled `staticwebassets/scripts/*.js`. **24 candidates total.** | 16 passive: five declaration-only `.props` including the two SDK static-web-assets files, 3 XML API docs, CSS bundle, logo, notices/README, ZIP metadata, signature. Claims `https://github.com/syncfusion/blazor-toolkit.git` at `9d5991d49acef4fdf5930952e17d0a3daed63344`; per TFM (8/9/10) Components.Web 8.0.23/9.0.12/10.0.2 and Localization 8.0.23/9.0.12/10.0.2. | Mixed compiled+source text, applicable/escalated |
| IgniteUI.Blazor.Lite 0.3.0 | `e9565c7c7093ff3798b62abdc2e67fe9bc438807eb0fa9b9b43f4573f9df0e63` / 35 | 3 corresponding PE DLLs; 7 `staticwebassets/*.js` including `lit-html.js`. **10 candidates total.** | 24 passive: five declaration-only `.props` including the two SDK static-web-assets files, 3 XML API docs, `api.d.ts` type declaration, eight theme CSS files, README/LICENSE/THIRD-PARTY-LICENSES, ZIP metadata, signature. Claims `https://github.com/IgniteUI/igniteui-blazor` at `9995aef5bf771beb94551e4ebabfe6d90c7a4488`; per TFM Components.Web 8.0.0/9.0.0/10.0.0. | Mixed compiled+source text, applicable/escalated |

Both use one recognized 2013/05 root nuspec. Neither ZIP contains a
standalone `.pdb`, `.snupkg`, source map, SBOM, build attestation or
`package.json`. We did **not** inspect embedded debug/SourceLink data or
external release artifacts. The two static-web-assets `.props` files in
each package match only the narrowly allowlisted declarative path
expression, with **zero active locators**. A
script's suggestive name, third-party license file, repository claim or
package signature cannot establish its actual upstream. The retained
`acceptance.json` outcomes require *both* compiled-build and source-text
blob/verified-transform or referenced-upstream byte maps.
`acceptance-receipt.template.json` records a future
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
