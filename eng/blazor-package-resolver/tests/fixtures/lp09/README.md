# LP-09 expectation freeze (before resolver implementation)

The normative `repository-wide` LP-09 row is **NuGet `ProjectUrl` is populated**,
a `Licensing and provenance` decomposition evidence check from clause 1.4:
"NuGet packages must populate PackageLicenseExpression, RepositoryUrl,
RepositoryCommit (commit SHA), Authors, and ProjectUrl." This is only a
field-population fact about the embedded **root nuspec**, not a package
validity, source provenance, security, or overall-readiness finding. The
[NuGet nuspec reference](https://learn.microsoft.com/en-us/nuget/reference/nuspec#projecturl)
calls the optional `<metadata><projectUrl>` a package home-page URL and
explicitly distinguishes it from the
[repository](https://learn.microsoft.com/en-us/nuget/reference/nuspec#repository)
`url` attribute, which describes a source-control location. The
[current nuspec XSD](https://github.com/NuGet/NuGet.Client/blob/dev/src/NuGet.Core/NuGet.Packaging/compiler/resources/nuspec.xsd)
declares `projectUrl` as at most one optional `xs:anyURI` element directly
under `metadata`. The reference notes that this latest XSD is not a
published, version-specific schema. The
[MSBuild pack mapping](https://learn.microsoft.com/en-us/nuget/reference/msbuild-targets#pack-target)
maps `PackageProjectUrl` to the nuspec ProjectUrl; this oracle checks the
*archived nuspec text*, not MSBuild inputs, feeds, or package web pages.

Exactly one direct, recognized `<projectUrl>` with nonblank decoded text
establishes *population*. Outer whitespace is trimmed for this oracle's
finding; internal spaces/newlines, Unicode characters, spelling and case
remain unchanged. XML predefined and numeric entities decode into text,
CDATA contributes literal text, and comments contribute no text. A plain,
padded, relative, malformed, opaque, or non-HTTP value is therefore still
**populated**. This does **not** certify that the string is a valid URI or URL,
has an HTTP(S) scheme, is reachable, under partner control, safe, publicly
sourced, or corresponds to `RepositoryUrl`/`RepositoryCommit`; it does not
enforce nuget.org's 4,000-character upload limit or any whole-document
schema-validity rule. A single empty element, whitespace-only text (including
Unicode whitespace), comment-only text, or whitespace-only CDATA is
deterministically **not populated**. A repository `url`, repository
`projectUrl` attribute, metadata attribute, wrong-case `<ProjectUrl>`,
misplaced element, or nested element cannot supply the direct field.
An unrelated malformed repository URL cannot invalidate a present ProjectUrl.

[NuGet's `NuspecReader.GetProjectUrl`](https://github.com/NuGet/NuGet.Client/blob/dev/src/NuGet.Core/NuGet.Packaging/NuspecReader.cs)
calls `GetMetadataValue("projectUrl")`. Its
[core reader](https://github.com/NuGet/NuGet.Client/blob/dev/src/NuGet.Core/NuGet.Packaging/Core/NuspecCoreReaderBase.cs)
indexes raw XML element values and uses the first duplicate; **the reader
does not itself trim `GetMetadataValue`**. The outer trim here is an explicit
reporting/population convention consistent with LP-06/LP-08, not a claim that
NuGet normalizes URLs, decodes percent escapes, collapses internal whitespace,
or validates them before retrieval. XML decoding is performed by the XML
parser, not URL decoding. Duplicate direct ProjectUrl elements escalate even
when their outer-trimmed texts agree: a first-element choice would mask the
ambiguous nuspec. Conflicting and identical duplicates have separate
diagnostics; neither is an LP-09 verdict about which URL is right.

The direct element's namespace must match the recognized `package` and
`metadata` namespace. The unqualified and historical NuGet 2010/07, 2011/08,
2011/10, 2012/06, 2013/01 and 2013/05 roots are recognized; a foreign root
or metadata element escalates. Namespace declarations are not ProjectUrl
attributes, but other ordinary/namespaced attributes or child elements
contradict its simple-text shape and escalate. A foreign-namespace
`projectUrl` (including one alongside a recognized value) escalates instead
of being read by local name or silently ignored. A DTD or custom entity
declaration is unsafe and escalates without expansion; an undefined entity
or malformed XML escalates as invalid XML. Missing/duplicate metadata,
missing/ambiguous root nuspec, a nested-only nuspec, unreadable archive, and
an unavailable package escalate with a named missing fact. A missing *file on
disk* or a changed checked-input digest is a foundation loader error before
LP-09 dispatch, not the `no-package` case.

`cases.json` freezes **57 complete structured CaseResult expectations**
(56 synthetic archives and one absent-input case). The branch matrix covers:

| Branch family | Controls |
|---|---|
| Populated direct text | plain, padded, internal whitespace/newline, Unicode and Unicode padding, entities, CDATA/comments, non-HTTP, relative, malformed and opaque values |
| Namespace and field separation | unqualified and six historical namespaces; namespace declaration; project plus different/malformed repository URL; unrelated foreign sibling |
| Not populated | absent, repository-only/attribute-only, metadata attribute, nested/outside-metadata, wrong-case, empty, whitespace, Unicode whitespace, comment-only, empty CDATA |
| Field ambiguity/shape | identical/padded/conflicting/blank duplicates; foreign-only/alongside/default-namespace child; ordinary/namespaced attributes; nested content |
| Input safety/availability | foreign root/metadata, missing/duplicate metadata, malformed XML/undefined entity, DTD, missing/multiple/nested-only root nuspec, corrupt archive, no supplied package |

These controls prevent whole-document searches, first-element selection,
local-name-only XML matching, and treating LP-06 RepositoryUrl as ProjectUrl.
Each synthetic `.nupkg` fixes ZIP creator, version, mode, timestamp,
compression, entry order and metadata, and pins its SHA-256 in `cases.json`.
The portable `plain.nupkg` golden SHA-256
`fb6338ff44ac3b28dbcd09c25abce79e5306327320d2ac818e1b597505ecdf98`
also detects generator drift. `lp09_expectations.py` regenerates every
archive in memory and checks complete documents, archive bytes and inspected
field shape under normal Python and `python -O`. It validates source
expectations, **not** resolver behavior.

Independent inspection of the exact archived packages and their root nuspecs
established these predeclared LP-09 expectations:

| Retained package | Package bytes / SHA-256 | Nuspec SHA-256 | Single direct ProjectUrl in recognized 2013/05 metadata | LP-09 |
|---|---|---|---|---|
| Syncfusion.Blazor.Toolkit 2.0.1 | 4912251 / `87cf6795afe5106c9d6c4db5e63e7f0ef0c741a850243557b3bbab1854c6121d` | `e9a55efdfc3385c1df49206839689f3f01c0bf5731b6130f05a601632a77acb7` | `https://www.syncfusion.com/blazor-toolkit` | populated |
| IgniteUI.Blazor.Lite 0.3.0 | 1417667 / `e9565c7c7093ff3798b62abdc2e67fe9bc438807eb0fa9b9b43f4573f9df0e63` | `8399959e38391faa853f874f6d4c2d10a2724f3e978af5fc79fbd46a7a74833c` | `https://www.infragistics.com/products/ignite-ui-blazor` | populated |

`retained-inputs.json` pins each exact package's ID, version, size, archive
SHA-256, sole root nuspec entry and SHA-256, source-root hint and relative
path. No vendor bytes or workstation-specific paths are committed. Bind
**both** `LP09_SYNCFUSION_NUPKG` and `LP09_INFRAGISTICS_NUPKG` to the
corresponding retained local archives, then run
`python eng\blazor-package-resolver\tests\lp09_expectations.py --require-retained`.
This verifies the package identity, nuspec namespace/structure and exact
ProjectUrl text, not a prior assessment verdict. Without bindings the normal
check explicitly prints `skipped`; required mode fails closed. Run
`--check-negative-bindings` to prove that missing, misspelled and wrong
existing files cannot pass, including under `python -O`.

Only **after** separate approval of this expectation freeze may Phase B add a
resolver, registry entry and implementation tests. For future retained
acceptance replay, copy verified vendor packages into a temporary `packages\`
directory beside a copy of `acceptance.json`, then record results in
`acceptance-receipt.template.json`. This validator is not that replay. In
Phase A LP-05/06/07/08 remain implemented and unchanged; LP-09 and the other
55 package rows remain `tbd`. An LP-09 synthetic replay must therefore
predictably mismatch these expectations as `package-dispatcher/unimplemented`,
not because the fixtures or schema are invalid.
