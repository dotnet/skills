# LP-05 expectation freeze (separate from resolver implementation)

The normative `repository-wide` LP-05 row says **NuGet `PackageLicenseExpression`
is populated**, decomposed from requirement-basis clause 1.4: "NuGet packages
must populate PackageLicenseExpression, RepositoryUrl, RepositoryCommit (commit
SHA), Authors, and ProjectUrl." LP-05 checks the expression *field*, not whether
the license is OSI-approved (LP-01), whether license files/notices are preserved
(LP-04), whether dependencies are licensed (LP-03), or any other package row.
A nonblank `<license type="expression">` in a recognized NuGet nuspec namespace
establishes the field is populated; it does not prove a valid SPDX expression,
legal rights, repository openness, or release-wide compliance. A license file
or legacy `licenseUrl` alone is not `PackageLicenseExpression`. A `licenseUrl`
never changes LP-05: compatible, conflicting, or blank URLs beside a valid
expression all leave the populated-expression finding unchanged; a URL alone
has the same missing-expression finding as no license metadata. URL consistency
and meaning belong to other license rows. With unavailable, unsafe or ambiguous
*expression/package* inputs, escalate the named missing fact rather than guess.
The older typed-facts reader recognizes unqualified roots and the 2010/07,
2011/08, 2011/10, 2012/06, 2013/01 and 2013/05 NuGet namespaces; a foreign
root or foreign `license` child is not accepted by local-name alone.
For recognized `license` elements, NuGet's
[manifest reader](https://github.com/NuGet/NuGet.Client/blob/dev/src/NuGet.Core/NuGet.Packaging/PackageCreation/Authoring/ManifestReader.cs)
parses `type` case-insensitively and reads optional `version`; the
[license schema and pack specification](https://github.com/NuGet/Home/wiki/Packaging-License-within-the-nupkg-(Technical-Spec)#nuspec-schema-changes)
allow `type` and optional `version`. XML namespace declarations are not
license metadata attributes. Freeze only `type`, supported `version="1.0.0"`
and namespace declarations as allowed: a newer version escalates because
its grammar is not established by this version's evidence; an arbitrary
unknown non-namespace attribute escalates under this deliberately strict
input contract. This is about field population, not validating MIT/SPDX.

`cases.json` is a frozen foundation `schema_version: 1` replay suite.
`lp05_expectations.py` verifies its full structured `CaseResult` expectations
and reproducible, SHA-256-pinned synthetic `.nupkg` bytes without dispatching
a resolver. Every ZIP entry fixes creator system, versions, attributes,
timestamp, compression and order; a platform-independent golden SHA-256 and
the committed per-case digests reject host-dependent regeneration. The
synthetic branches include expression (alone, with matching,
conflicting or blank URL; unqualified and 2010/07 and 2011/08 namespaces),
optional supported `version="1.0.0"`, case-insensitive `type="Expression"`
and an XML namespace declaration; unsupported newer version and arbitrary
unknown non-namespace attribute have distinct escalation controls. The other
synthetic branches cover
file, blank-file, URL-only, blank-URL-only, absent expression, blank expression,
missing/unsupported license type, invalid XML, duplicate license declarations,
foreign child/root namespace, no root nuspec, multiple root nuspecs, a
subfolder-only nuspec, DTD/entity-bearing nuspec, unreadable archive, and
unavailable package input. DTD-bearing XML must escalate without expanding
entities. Missing *file on disk* or a changed digest is rejected by the
foundation replay loader before row dispatch; the `no-package` case instead
tests a valid but insufficient checked-input set. The expression finding is a
factual population assertion, **not** a readiness status or score. Frozen
escalations deliberately leave unsafe/ambiguous package and expression states
unresolved; **a blank license file declaration is a deterministic LP-05
missing-expression finding**, not an LP-05 escalation. Its file metadata
quality belongs to LP-04. A conflicting legacy URL is not an LP-05 conflict.

`retained-inputs.json` binds two exact published package identities to
environment-variable names, without checking proprietary binaries into the
repository or hardcoding workstation paths. Absolute local source paths are
recorded only in the session's external acceptance manifest; the committed
source-root hints and relative paths are portable. Those locations are not
identities: the manifest's package ID/version, size, SHA-256, nuspec entry digest and prior manifest
binding are authoritative. **Do not substitute** `release-toolkit.2.0.1.nupkg`
(different bytes) or another IgniteUI fixture. Syncfusion's corrected
`revisions/0001/package.assessment.json` LP-05 row cites
`EV1-377b5c6d8c13d9d364af5687771e6ae5e1644f9653066cb0a5b832c2e2b25b0b`;
its evidence record cites the archived nuspec digest
`e9a55efdfc3385c1df49206839689f3f01c0bf5731b6130f05a601632a77acb7`.
Independent archive inspection confirms the MIT expression; the accompanying
legacy URL is irrelevant to LP-05. Infragistics'
`inputs/input.confirmed.json` identifies the retained
IgbCheckbox component-baseline package; its archived nuspec likewise has
`<license type="expression">MIT</license>`. Its
component report is **not an LP-05 package assessment**: the positive LP-05
expectation in `acceptance.json` is newly derived from these exact archive bytes,
not inherited from a prior verdict.

Run `python eng/blazor-package-resolver/tests/lp05_expectations.py` for
fixture/manifest checks: each retained vendor explicitly prints `skipped`
when its binding is absent. Run it with `--require-retained` after setting
**both** `LP05_SYNCFUSION_NUPKG` and `LP05_INFRAGISTICS_NUPKG` to exact retained
archives: both must print `verified`, including their embedded nuspec hashes,
or the check fails. `--check-negative-bindings` proves missing/misspelled
required-mode bindings fail before archive access; `python -O` must retain
the same checks. This is an expectation-data check, **not** a vendor resolver
replay.

For an opt-in retained-package replay, bind both environment variables to
the retained sources and verify size and digest *before* copying into a
temporary directory:
copy `acceptance.json` there and place the verified packages at its relative
`packages/` paths. Then run the existing
`dotnet run --project eng/blazor-package-resolver/BlazorPackageResolver.csproj -- replay <temporary-acceptance.json>`.
The replay loader checks hashes and passes immutable `CheckedInput.Content`
bytes to the LP-05 handler; that handler must not reopen an input path.
`acceptance-receipt.template.json` records the expectation commit, both
bindings/hash checks, exit codes and exact expected-result comparisons
separately. The expectation validator alone does **not** establish a passing
vendor resolver replay; vendor replay evidence is separate. Ordinary CI
requires only synthetic fixtures, not externally retained vendor packages.

Before the implementation can be accepted against this expanded oracle,
add executable, digest-pinned .NET boundary probes for over-limit package
bytes, ZIP directory size, ZIP entry count, entry-name length, and uncompressed
nuspec size. Each must verify a stable safe escalation (not an exception,
decompression bomb, or unbounded allocation). These are **future
implementation acceptance tests**, not resolver changes in this expectation
commit.
