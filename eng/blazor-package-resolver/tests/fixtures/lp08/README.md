# LP-08 expectation freeze (cross-row metadata correction)

The normative `repository-wide` LP-08 row is **NuGet `Authors` is populated**,
a `Licensing and provenance` decomposition evidence check from clause 1.4:
"NuGet packages must populate PackageLicenseExpression, RepositoryUrl,
RepositoryCommit (commit SHA), Authors, and ProjectUrl." This row checks only
whether the package's root nuspec contains one direct, recognized
`<metadata><authors>` element with author text. It makes no claim about author
identity validity, verified publisher or package ownership, legal sufficiency,
contactability, source correspondence, package validity, or overall readiness.
`<owners>` is not `<authors>`: NuGet's
[nuspec reference](https://learn.microsoft.com/en-us/nuget/reference/nuspec#authors)
calls authors a comma-separated list and says both authors and owners in the
nuspec are ignored for nuget.org package ownership. The current
[nuspec schema](https://github.com/NuGet/NuGet.Client/blob/dev/src/NuGet.Core/NuGet.Packaging/compiler/resources/nuspec.xsd)
declares one required `authors` element of `xs:string`, not a validated person
or account identity. NuGet's
[`NuspecReader.GetAuthors`](https://github.com/NuGet/NuGet.Client/blob/dev/src/NuGet.Core/NuGet.Packaging/NuspecReader.cs)
returns the metadata string, not an independently verified author list.
MSBuild's `Authors` input is documented as semicolon-separated, but this
oracle examines the **embedded nuspec text**, not the build property.

A plain nonblank string or a comma-separated string with at least one nonblank
entry establishes *population*. Outer whitespace is trimmed for the finding;
internal whitespace, newlines, comma spacing, Unicode spelling and case are
preserved verbatim after XML parsing. There is no case folding, Unicode
normalization, list reordering, identity lookup, or assertion that every list
entry is valid. An empty list slot alongside a real entry does not erase the
population fact; it does not certify the list either. An absent element, an
empty or whitespace-only element (including Unicode whitespace), or only
commas and whitespace has no author entry and is deterministically **not
populated**. A punctuation-only non-comma value remains nonblank text, not a
validated author identity. This is a deliberately narrow text check, not a
general nuspec schema-validity check.

Predefined and numeric XML entities contribute their decoded characters.
CDATA contributes literal text; comments contribute none. A comment-only or
whitespace-only CDATA element is blank. A DTD or custom entity declaration is
unsafe and escalates without entity expansion. The direct element's XML
namespace must match the recognized `package`/`metadata` namespace; recognized
roots include unqualified and historical NuGet 2010/07, 2011/08, 2011/10,
2012/06, 2013/01 and 2013/05 namespaces. Namespace declarations are not
`authors` attributes, but other attributes (ordinary or namespaced) and
nested child elements conflict with the simple-text contract and escalate.
Foreign-namespace `authors` elements, including alongside a valid element,
escalate rather than being accepted by local name or silently ignored. Duplicate
direct authors elements escalate even when their outer-trimmed text agrees;
conflicting values are diagnosed separately but never resolved by choosing
one. A nested authors element or one outside `metadata` cannot populate this
field. A missing or duplicate recognized `metadata` element, foreign root,
malformed XML, absent/ambiguous root nuspec, unreadable archive or unavailable
package escalates with a named missing fact. Missing *file on disk* or a
changed input SHA-256 instead fails the foundation replay loader before row
dispatch.

The cross-row `foreign-metadata-before` and `foreign-metadata-after` controls
place empty `urn:other` metadata beside recognized metadata with populated
authors. NuGet's
[`NuspecCoreReaderBase.MetadataNode`](https://github.com/NuGet/NuGet.Client/blob/dev/src/NuGet.Core/NuGet.Packaging/Core/NuspecCoreReaderBase.cs)
selects the first root child by local name `metadata`, regardless of namespace;
the shared reader selects only root-namespace metadata. Foreign-first inspects
different nodes; recognized-first still contains competing root metadata.
Both freeze `ambiguous-metadata` / `escalated` / `unknown`, no finding or field
evidence, an archive-level evidence pointer and `unambiguous-nuspec`
requested from the package owner. These well-formed controls are not claims
that authors are missing. All earlier bytes/expectations and retained
outcomes remain intact; the two new cases intentionally mismatch the
current shared reader.

`cases.json` freezes 52 complete structured `CaseResult` expectations (51
synthetic archives and one absent-input case). Named branches cover plain,
comma-separated, padded, internal-whitespace/newline and Unicode author text;
entities, CDATA and comments; all recognized namespaces; other metadata and
misplaced/owners/attribute-only declarations; empty, whitespace and
delimiter-only values; duplicate, foreign and malformed authors; unsafe or
invalid XML; root nuspec selection and unavailable inputs. Synthetic `.nupkg`
files fix ZIP creator, version, mode, timestamp, compression, entry ordering
and metadata. Each case pins its SHA-256; the portable `plain.nupkg` golden
digest `c70513c61299ae132e4e05960850d9dbfe6e7517f0835cd9c08eb0dd78c3435e`
catches generator drift. `lp08_expectations.py` regenerates every synthetic
archive in memory and checks complete documents, archive bytes and author text
under normal Python and `python -O`. It checks source expectations, **not**
resolver behavior. No vendor bytes or workstation-specific paths are committed.

Independent inspection of the exact archived packages and their root nuspecs
established these predeclared LP-08 expectations:

| Retained package | Package bytes / SHA-256 | Nuspec SHA-256 | Direct authors in recognized 2013/05 root | LP-08 |
|---|---|---|---|---|
| Syncfusion.Blazor.Toolkit 2.0.1 | 4912251 / `87cf6795afe5106c9d6c4db5e63e7f0ef0c741a850243557b3bbab1854c6121d` | `e9a55efdfc3385c1df49206839689f3f01c0bf5731b6130f05a601632a77acb7` | `Syncfusion Inc.` | populated |
| IgniteUI.Blazor.Lite 0.3.0 | 1417667 / `e9565c7c7093ff3798b62abdc2e67fe9bc438807eb0fa9b9b43f4573f9df0e63` | `8399959e38391faa853f874f6d4c2d10a2724f3e978af5fc79fbd46a7a74833c` | `Infragistics` | populated |

`retained-inputs.json` pins each exact package's ID, version, size, archive
SHA-256, sole root nuspec entry and SHA-256, source-root hint and relative
path. Bind **both** `LP08_SYNCFUSION_NUPKG` and `LP08_INFRAGISTICS_NUPKG` to
the exact retained local archives, then run
`python eng\blazor-package-resolver\tests\lp08_expectations.py --require-retained`.
This verifies the package identity, nuspec structure and literal authors
text, not a prior assessment verdict. Without bindings the normal check
explicitly prints `skipped`; required mode fails closed. Run
`--check-negative-bindings` to prove that missing, misspelled and wrong
existing files cannot pass even under `python -O`.

For an opt-in retained resolver replay, copy verified retained inputs to a
temporary `packages\` directory beside a copy of `acceptance.json`; record
results in `acceptance-receipt.template.json`. This validator is not a
retained resolver replay. LP-05 through LP-09 are implemented; the cross-row
metadata controls above are deliberately ahead of their shared reader.
