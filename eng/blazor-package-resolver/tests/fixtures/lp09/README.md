# LP-09 expectation freeze (cross-row metadata correction)

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
and no competing case-variant element establishes *population*. Outer
whitespace is trimmed for this oracle's finding; internal spaces/newlines,
Unicode characters, spelling and case remain unchanged. XML predefined and
numeric entities decode into text; CDATA contributes literal text, while
comments and processing instructions contribute no text. A plain, padded,
relative, malformed, opaque, or non-HTTP value is therefore still
**populated**. In particular, `https://[invalid` is populated although
`.NET Uri.TryCreate(value, RelativeOrAbsolute)` rejects it. This does **not**
certify URI or URL validity, an HTTP(S) scheme, reachability, partner
control, safety, public source identity, or correspondence to
`RepositoryUrl`/`RepositoryCommit`; it does not
enforce nuget.org's 4,000-character upload limit or any whole-document
schema-validity rule. A single empty element, whitespace-only text (including
U+00A0 NO-BREAK SPACE), comment-only text, processing-instruction-only text,
or whitespace-only CDATA is deterministically **not populated**. A repository
`url`, repository
`projectUrl` attribute, metadata attribute, misplaced element, or nested
element cannot supply the direct field. U+200B ZERO WIDTH SPACE is a Unicode
format character, **not** whitespace under Python `str.strip()` or .NET
`string.Trim()`; by itself it is a populated value, not a valid URL claim.
An unrelated malformed repository URL cannot invalidate a present ProjectUrl.

[NuGet's `NuspecReader.GetProjectUrl`](https://github.com/NuGet/NuGet.Client/blob/dev/src/NuGet.Core/NuGet.Packaging/NuspecReader.cs)
calls `GetMetadataValue("projectUrl")`. Its
[core reader](https://github.com/NuGet/NuGet.Client/blob/dev/src/NuGet.Core/NuGet.Packaging/Core/NuspecCoreReaderBase.cs)
indexes raw XML element values and uses the first duplicate; **the reader
does not itself trim `GetMetadataValue`**. The XSD and
[NuGet ManifestReader's exact `case "projectUrl"`](https://github.com/NuGet/NuGet.Client/blob/dev/src/NuGet.Core/NuGet.Packaging/PackageCreation/Authoring/ManifestReader.cs)
require the canonical XML spelling, but `NuspecCoreReaderBase` indexes
metadata with `StringComparer.OrdinalIgnoreCase`; NuGet Gallery's
case-insensitive metadata reading can therefore see `<ProjectUrl>`. A
standalone case variant, or one beside an exact `<projectUrl>`, **escalates**
with archive-level evidence and a request for a corrected nuspec: neither
deterministic absence nor a chosen populated value is safe across readers.
The outer trim here is an explicit reporting/population convention consistent
with LP-06/LP-08, not a claim that NuGet normalizes URLs, decodes percent
escapes, collapses internal whitespace, or validates them before retrieval.
XML decoding is performed by the XML
parser, not URL decoding. Duplicate direct ProjectUrl elements escalate even
when their outer-trimmed texts agree: a first-element choice would mask the
ambiguous nuspec. Conflicting and identical duplicates have separate
diagnostics; neither is an LP-09 verdict about which URL is right.

The direct element's namespace must **equal** its recognized `package` and
`metadata` namespace; a different prefix spelling bound to that same
namespace is equivalent. The unqualified and historical NuGet 2010/07, 2011/08,
2011/10, 2012/06, 2013/01 and 2013/05 roots are recognized; a foreign root
or metadata element escalates. Namespace declarations are not ProjectUrl
attributes, but other ordinary/namespaced attributes or child elements
contradict its simple-text shape and escalate. A foreign-namespace
`projectUrl` (including one alongside a recognized value, a child in a
*different recognized NuGet namespace*, or an unqualified `xmlns=""` child
under namespaced metadata) escalates instead of being read by local name,
by membership in any recognized namespace, or silently ignored. After
archive/XML/root/metadata safety checks, inspect **all direct field
candidates** before deciding population: foreign-namespace and case-variant
elements, duplicate exact elements, and unsupported attributes/children
escalate before a sole exact-case value can establish populated, blank, or
missing. In particular, the mixed exact-plus-variant case must not take
the populated branch simply because the exact element appeared first.
A DTD or custom entity declaration is unsafe and escalates without expansion;
an undefined entity or malformed XML escalates as invalid XML. Missing/duplicate metadata,
missing/ambiguous root nuspec, a nested-only nuspec, unreadable archive, and
an unavailable package escalate with a named missing fact. A missing *file on
disk* or a changed checked-input digest is a foundation loader error before
LP-09 dispatch, not the `no-package` case.

The cross-row `foreign-metadata-before` and `foreign-metadata-after` controls
place empty `urn:other` metadata beside one recognized metadata element with
a populated ProjectUrl. NuGet's
[`NuspecCoreReaderBase.MetadataNode`](https://github.com/NuGet/NuGet.Client/blob/dev/src/NuGet.Core/NuGet.Packaging/Core/NuspecCoreReaderBase.cs)
selects the first root child by local name `metadata` regardless of namespace,
while the shared resolver reader now returns typed metadata ambiguity when a
foreign sibling accompanies recognized metadata. Foreign-first makes NuGet
report no ProjectUrl; recognized-first can make NuGet report the recognized
URL, but still has competing root metadata. Both orderings replay to the
frozen `ambiguous-metadata` / `escalated` / `unknown` result, no field finding
or field evidence, an archive-level evidence pointer and `unambiguous-nuspec`
requested from the package owner.
This is a well-formed XML structure/reader ambiguity, not a missing or blank
ProjectUrl. Earlier bytes/expectations and retained outcomes are unchanged;
both cases replay to their frozen expectations.

`cases.json` freezes **67 complete structured CaseResult expectations**
(66 synthetic archives and one absent-input case). The branch matrix covers:

| Branch family | Controls |
|---|---|
| Populated direct text | plain, padded, internal whitespace/newline, Unicode padding and U+200B, entities, CDATA/comments/processing instructions, non-HTTP, relative, malformed and `Uri.TryCreate`-rejected values |
| Namespace and field separation | unqualified and six historical namespaces; same-namespace prefix and namespace declaration; project plus different/malformed repository URL; unrelated foreign sibling |
| Not populated | absent, repository-only/attribute-only, metadata attribute, nested/outside-metadata, empty, whitespace including U+00A0, comment-only, PI-only, empty CDATA |
| Field ambiguity/shape | standalone or mixed exact-plus-case-variant, identical/padded/conflicting/blank duplicates; foreign-only/alongside/different-recognized-namespace/`xmlns=""` child; ordinary/namespaced attributes; nested content |
| Input safety/availability | foreign root/metadata, both orderings of foreign plus recognized metadata, missing/duplicate metadata, malformed XML/undefined entity, DTD, missing/multiple/nested-only root nuspec, corrupt archive, no supplied package |

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

For an opt-in retained acceptance replay, copy verified vendor packages into
a temporary `packages\` directory beside a copy of `acceptance.json`, then
record results in `acceptance-receipt.template.json`. This validator is not
that replay. LP-05 through LP-09 are implemented, and the other 55 package
rows remain `tbd`; both cross-row metadata controls now replay to their frozen
expectations.
