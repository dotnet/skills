# LP-06 expectation freeze (cross-row metadata correction)

The normative `repository-wide` LP-06 row is **NuGet `RepositoryUrl` is
populated**, decomposed from requirement-basis clause 1.4: "NuGet packages must
populate PackageLicenseExpression, RepositoryUrl, RepositoryCommit (commit
SHA), Authors, and ProjectUrl." NuGet's
[nuspec repository reference](https://learn.microsoft.com/en-us/nuget/reference/nuspec#repository)
describes `<repository>` attributes `type`, `url`, `branch` and `commit`,
distinct from `<projectUrl>`. LP-06 asks whether a **single recognized
repository element has a nonblank `url` attribute** in the root package
nuspec. It does not assert URL syntax, absoluteness, reachability, repository
existence/correctness/public availability, correspondence with shipped source,
or commit identity. Those latter concerns do not turn a populated URL into
an LP-06 failure; LP-02, LP-07 and LP-10 have separate obligations. An absent
repository element, missing `url`, and empty/whitespace `url` are three
distinct deterministic **not populated** branches. A relative path or
malformed-but-nonblank URL text is still populated; the finding quotes the
trimmed text, without claiming it is a valid usable URL. `type`, `branch`,
`commit`, and `projectUrl` cannot supply a missing repository URL, and
supported attribute values do not change a present URL's population.

The strict recognized-metadata contract accepts only the four documented
repository attributes and XML namespace declarations; unknown ordinary or
namespaced attributes escalate rather than assuming unreviewed metadata
semantics. A padded but nonblank URL is reported trimmed.
Duplicate repository elements escalate even if identical; conflicting
duplicates escalate separately. A foreign-namespace repository element or
root, unsafe XML/DTD, invalid XML, ambiguous/missing root nuspec, or corrupt/
unavailable package also escalates with a named missing fact rather than
guessing. Recognized nuspec roots include no namespace and the historical
NuGet 2010/07, 2011/08, 2011/10, 2012/06, 2013/01, and 2013/05 namespaces.
Missing physical files or changed digests are errors in the foundation
replay loader; `no-package` is a checked-input absence case, not a missing
file. Findings describe field population only, **not** release readiness.

The cross-row `foreign-metadata-before` and `foreign-metadata-after` controls
place empty `urn:other` metadata beside one recognized metadata element
containing a populated repository URL. NuGet's
[`NuspecCoreReaderBase.MetadataNode`](https://github.com/NuGet/NuGet.Client/blob/dev/src/NuGet.Core/NuGet.Packaging/Core/NuspecCoreReaderBase.cs)
selects the first root child by local name `metadata`, regardless of namespace;
the shared resolver reader selects only root-namespace metadata. Foreign-first
selects different nodes, while recognized-first can appear to agree yet still
leaves competing root metadata. Both freeze `ambiguous-metadata` / `escalated`
/ `unknown`, no field finding or field evidence, an archive-level evidence
pointer and a request for an `unambiguous-nuspec` from the package owner.
Well-formed XML with a recognized sibling must not silently establish a
field. The suite now has 33 cases and 32 pinned archives; all earlier
bytes/expectations and retained outcomes remain unchanged. The new cases
intentionally mismatch the current shared reader pending a separate fix.

`cases.json` freezes the full structured `CaseResult` for each branch. Every
generated synthetic archive fixes its ZIP creator, version, mode, timestamp,
compression, entry order and metadata, and the per-case SHA-256 plus a
portable golden `url.nupkg` SHA-256 prevent host-dependent regeneration.
`lp06_expectations.py` checks exact committed manifest and bytes under both
normal Python and `python -O`; it does not run or implement a resolver.
The source contains the reviewed branch oracle, not inferred production
output. All synthetics are in `packages/`; retained vendor archives are
**not** committed.

Independent inspection of the exact archive and embedded nuspec bytes
established these predeclared LP-06 findings:

| Retained package | Package SHA-256 | Nuspec SHA-256 | `<repository url>` | LP-06 |
|---|---|---|---|---|
| Syncfusion.Blazor.Toolkit 2.0.1 | `87cf6795afe5106c9d6c4db5e63e7f0ef0c741a850243557b3bbab1854c6121d` | `e9a55efdfc3385c1df49206839689f3f01c0bf5731b6130f05a601632a77acb7` | `https://github.com/syncfusion/blazor-toolkit.git` | populated |
| IgniteUI.Blazor.Lite 0.3.0 | `e9565c7c7093ff3798b62abdc2e67fe9bc438807eb0fa9b9b43f4573f9df0e63` | `8399959e38391faa853f874f6d4c2d10a2724f3e978af5fc79fbd46a7a74833c` | `https://github.com/IgniteUI/igniteui-blazor` | populated |

`retained-inputs.json` binds each portable ID/version/size/archive and
nuspec SHA-256 to a source-root hint and relative path from the LP-05
source-manifest approach. Workstation-specific paths are never committed.
The corrected Syncfusion source is the `syncfusion-toolkit-assessment`
archive, **not** another similarly named release fixture. The Infragistics
source is the `igbcheckbox-baseline-20260923-r2/assessment/acquisition`
archive; its component assessment does not decide LP-06. Each expected URL
was independently read from that archive, not copied from an assessment
verdict.

Set `LP06_SYNCFUSION_NUPKG` and `LP06_INFRAGISTICS_NUPKG` to those exact
local archives. Run
`python eng/blazor-package-resolver/tests/lp06_expectations.py --require-retained`
to verify archive size/digest, embedded nuspec digest and the URL assertion.
Without bindings the ordinary check prints `skipped` for each; required mode
fails closed. `--check-negative-bindings` proves missing or misspelled
required-mode bindings fail before archive access, also under `python -O`.

For an opt-in retained resolver replay, verify both sources and copy them
under a temporary `packages/` directory next to a copy of `acceptance.json`;
then run the foundation `replay` command on that temporary suite.
`acceptance-receipt.template.json` records expectation commit, checked
identities, exit codes and exact expected-result comparisons separately.
The validator is **not** a retained resolver replay. LP-05 through LP-09
are implemented; the cross-row metadata controls above are deliberately
ahead of their shared reader.
