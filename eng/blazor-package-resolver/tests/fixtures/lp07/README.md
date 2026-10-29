# LP-07 expectation freeze (cross-row metadata correction)

The normative `repository-wide` LP-07 row is **NuGet `RepositoryCommit`
identifies the commit SHA**, a `Licensing and provenance` decomposition
evidence check from clause 1.4: "NuGet packages must populate
PackageLicenseExpression, RepositoryUrl, RepositoryCommit (commit SHA),
Authors, and ProjectUrl." NuGet's
[nuspec reference](https://learn.microsoft.com/en-us/nuget/reference/nuspec#repository)
defines optional `type`, `url`, `branch` and `commit` attributes on the
`<metadata><repository>` element; it describes `commit` as a SHA-1 hash.
[Git's object-name format](https://git-scm.com/docs/hash-function-transition#_object_names)
also supports full 40-hex SHA-1 and 64-hex SHA-256 IDs. Accepting both full
nonzero formats keeps this row usable with Git's newer object format; NuGet's
attribute is a string and supplies no independent proof of object existence
or type. **Arbitrary nonblank text is not a commit SHA**: LP-07 asserts only
that a single recognized nuspec repository element *declares a full-form
commit SHA*, not that the object exists, is reachable, was built into the
package, or belongs to the advertised repository.

Hex digits are case-insensitive; a finding prints lowercase for consistency.
Outer whitespace is trimmed (as in the existing metadata reader); whitespace
*inside* a hash is invalid. The ASCII hex alphabet and exact 40/64 lengths
are required for a full object ID. An ASCII-hex value of length 40 is a
full SHA-1 ID; length 64 is a full SHA-256 ID. Lengths 4-39 and 41-63
are *abbreviated* object names: they may resolve with a particular repository
but do not identify a full SHA here. Lengths 1-3 cannot form Git abbreviations
and 65+ cannot form either full ID; both are invalid-length values. An
all-zero ID of either length is a null sentinel, not a commit. An absent
repository, missing `commit`, or empty/whitespace `commit` deterministically
does not identify a SHA. The `commit` attribute must occur on the single
direct, recognized `<metadata><repository>` child; `<projectUrl>`, nested
`<commit>` elements and repository elements elsewhere cannot supply it.
The repository's `url` can be absent, blank, malformed or relative, and
its documented `type` and `branch` values cannot change a syntactically
valid commit ID into an LP-07 failure. URL population belongs to LP-06;
public-source mapping to LP-02 and package/source correspondence to LP-10.
The LP-07 finding makes **no** repository, provenance, or readiness claim.

Consistent with the LP-05/LP-06 strict nuspec contract, XML namespace
declarations are not extra attributes, but unknown ordinary or namespaced
repository attributes and unsupported repository text/children escalate.
Duplicate repository elements escalate regardless of whether their commits
agree. The diagnostic distinguishes identical *normalized commit values*
(trimmed and ASCII case-folded, even with differing URLs) from conflicting
commits with the same type and URL; it never compares URLs to classify the
LP-07 commit conflict. A foreign-namespace
repository element (even beside a recognized one), a missing or duplicate
`metadata` element, foreign package root, malformed XML, and DTD/entity
declarations escalate instead of guessing. Recognized roots include the
unqualified and historical NuGet 2010/07, 2011/08, 2011/10, 2012/06, 2013/01
and 2013/05 namespaces. A root nuspec is mandatory and unambiguous: a ZIP
with no nuspec, only a nested nuspec, or multiple root nuspecs escalates.
An unreadable ZIP and a case with no supplied package likewise escalate.
A missing *file on disk* or a changed checked-input digest is a foundation
replay-loader error, not the `no-package` case.

The cross-row `foreign-metadata-before` and `foreign-metadata-after` controls
place an empty `urn:other` metadata sibling beside recognized metadata with
a valid full commit SHA. NuGet's
[`NuspecCoreReaderBase.MetadataNode`](https://github.com/NuGet/NuGet.Client/blob/dev/src/NuGet.Core/NuGet.Packaging/Core/NuspecCoreReaderBase.cs)
selects the first root child by local name `metadata`, ignoring namespace;
the shared reader selects only root-namespace metadata. Foreign-first makes
the readers inspect different nodes; recognized-first still contains
competing root metadata despite agreeing on the field. Both freeze
`ambiguous-metadata` / `escalated` / `unknown`, no finding or field evidence,
an archive-level evidence pointer and `unambiguous-nuspec` requested from the
package owner. This is well-formed but reader-ambiguous XML, not an absent
commit. All earlier bytes/expectations and retained outcomes remain intact;
the two new cases intentionally mismatch the current shared reader.

`cases.json` freezes 55 complete structured `CaseResult` expectations (54
synthetic archives and one absent-input case):
full-form SHA-1/SHA-256 (case and whitespace variants), all recognized
namespaces, unrelated URL/type/branch/foreign sibling content, missing and
malformed commits, null IDs, duplicate/foreign/unknown metadata, unsafe XML,
root nuspec selection and unavailable inputs. Synthetic `.nupkg` files fix
ZIP creator, version, mode, timestamp, compression, entry ordering and
metadata. Every case pins its SHA-256; the portable `sha1.nupkg` golden digest
`c30ce8234e73b2d08f0eaa78aa081c575341e41647345b558d7e12da2183c348`
catches generator drift. `lp07_expectations.py` regenerates every synthetic archive
in memory and compares exact bytes and manifests under normal Python and
`python -O`. It checks source expectations, **not** resolver behavior.
No vendor package bytes or workstation-specific paths are committed.

Independent inspection of the exact archived packages and their root nuspecs
established these predeclared LP-07 expectations:

| Retained package | Package bytes / SHA-256 | Nuspec SHA-256 | Repository metadata in recognized 2013/05 root | LP-07 |
|---|---|---|---|---|
| Syncfusion.Blazor.Toolkit 2.0.1 | 4912251 / `87cf6795afe5106c9d6c4db5e63e7f0ef0c741a850243557b3bbab1854c6121d` | `e9a55efdfc3385c1df49206839689f3f01c0bf5731b6130f05a601632a77acb7` | one direct repository, `type="git"`, URL present, `branch="main"`, `commit="9d5991d49acef4fdf5930952e17d0a3daed63344"` | full SHA-1 declared |
| IgniteUI.Blazor.Lite 0.3.0 | 1417667 / `e9565c7c7093ff3798b62abdc2e67fe9bc438807eb0fa9b9b43f4573f9df0e63` | `8399959e38391faa853f874f6d4c2d10a2724f3e978af5fc79fbd46a7a74833c` | one direct repository, `type="git"`, URL present, no branch, `commit="9995aef5bf771beb94551e4ebabfe6d90c7a4488"` | full SHA-1 declared |

`retained-inputs.json` pins each exact package's ID, version, size, archive
SHA-256, root nuspec entry and SHA-256, source-root hint and relative path.
The binding names are `LP07_SYNCFUSION_NUPKG` (the corrected
`syncfusion-toolkit-assessment` archive, not another same-version package)
and `LP07_INFRAGISTICS_NUPKG` (the
`igbcheckbox-baseline-20260923-r2/assessment/acquisition` archive).
The nuspec commit values were read directly from the pinned ZIP bytes; no
prior assessment result or LP-06 URL verdict establishes LP-07.

Bind **both** environment variables to these exact local archives and run
`python eng\blazor-package-resolver\tests\lp07_expectations.py --require-retained`
to check archive size/digest, root nuspec identity, namespace/structure and
commit. Without bindings the normal check explicitly prints `skipped`;
required mode fails closed. `--check-negative-bindings` proves missing or
misspelled environment variables fail before reading any bound archive,
including under `python -O`.

For an opt-in retained resolver replay, copy verified retained inputs to a
temporary `packages\` directory beside a copy of `acceptance.json`; record
results in `acceptance-receipt.template.json`. This validator is not a
retained resolver replay. LP-05 through LP-09 are implemented; the cross-row
metadata controls above are deliberately ahead of their shared reader.
