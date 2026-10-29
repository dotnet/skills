# LP-05 expectation freeze (no resolver implementation)

The normative `repository-wide` LP-05 row says **NuGet `PackageLicenseExpression`
is populated**, decomposed from requirement-basis clause 1.4: "NuGet packages
must populate PackageLicenseExpression, RepositoryUrl, RepositoryCommit (commit
SHA), Authors, and ProjectUrl." LP-05 checks the expression *field*, not whether
the license is OSI-approved (LP-01), whether license files/notices are preserved
(LP-04), whether dependencies are licensed (LP-03), or any other package row.
A nonblank `<license type="expression">` in a recognized NuGet nuspec namespace
establishes the field is populated; it does not prove a valid SPDX expression,
legal rights, repository openness, or release-wide compliance. A license file
or legacy `licenseUrl` alone is not `PackageLicenseExpression`. With absent or
unsafe/ambiguous inputs, escalate the named missing fact rather than guess.
The older typed-facts reader recognizes unqualified roots and the 2010/07,
2011/08, 2011/10, 2012/06, 2013/01 and 2013/05 NuGet namespaces; a foreign
root or foreign `license` child is not accepted by local-name alone. A matching
compatibility `licenseUrl` alongside an expression does not remove the expression.

`cases.json` is a frozen foundation `schema_version: 1` replay suite.
`lp05_expectations.py` verifies its full structured `CaseResult` expectations
and reproducible, SHA-256-pinned synthetic `.nupkg` bytes without dispatching
a resolver. The synthetic branches are expression (including compatible
legacy URL, unqualified and recognized older namespace), file, legacy URL,
missing metadata, blank expression/file/URL, unsupported type or invalid XML,
duplicate/conflicting metadata,
foreign child/root namespace, unreadable archive, and unavailable package
input. Missing *file on disk* or a changed digest is rejected by the
foundation replay loader before row dispatch; the `no-package` case instead
tests a valid but insufficient checked-input set. The expression finding is a
factual population assertion, **not** a readiness status or score. Frozen
escalations deliberately leave malformed and conflicting states unresolved.

`retained-inputs.json` binds two exact published package identities to
environment-variable names, without checking proprietary binaries into the
repository or hardcoding workstation paths. Absolute local source paths are
recorded only in the session's external acceptance manifest; the committed
source-root hints and relative paths are portable. Those locations are not
identities: the manifest's
package ID/version, size, SHA-256, nuspec entry digest and prior manifest
binding are authoritative. **Do not substitute** `release-toolkit.2.0.1.nupkg`
(different bytes) or another IgniteUI fixture. Syncfusion's corrected
`revisions/0001/package.assessment.json` LP-05 row cites
`EV1-377b5c6d8c13d9d364af5687771e6ae5e1644f9653066cb0a5b832c2e2b25b0b`;
its evidence record cites the archived nuspec digest
`e9a55efdfc3385c1df49206839689f3f01c0bf5731b6130f05a601632a77acb7`.
Independent archive inspection confirms an MIT expression plus matching legacy
URL. Infragistics' `inputs/input.confirmed.json` identifies the retained
IgbCheckbox component-baseline package; its archived nuspec likewise has
`<license type="expression">MIT</license>` and matching legacy URL. Its
component report is **not an LP-05 package assessment**: the positive LP-05
expectation in `acceptance.json` is newly derived from these exact archive bytes,
not inherited from a prior verdict.

For a future Phase B replay, bind both environment variables to the retained
sources and verify size and digest *before* copying into a temporary directory:
copy `acceptance.json` there and place the verified packages at its relative
`packages/` paths. Then run the existing
`dotnet run --project eng/blazor-package-resolver/BlazorPackageResolver.csproj -- replay <temporary-acceptance.json>`.
The replay loader checks hashes and passes immutable `CheckedInput.Content`
bytes to the LP-05 handler; that handler must not reopen an input path.
`acceptance-receipt.template.json` records the expectation commit, both
bindings/hash checks, exit codes and exact expected-result comparisons
separately. Do **not** claim either vendor replay passed while the LP-05
registry entry remains `tbd`. Ordinary CI requires only synthetic fixtures,
not externally retained vendor packages.
