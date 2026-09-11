<!-- Generated from rubric.json. Do not edit by hand. -->
# Blazor component readiness checklist

**Rubric version:** 2.0.1

**Scope schema version:** 2

This checklist is the operational index of [rubric.json](rubric.json). The exact
normative requirement text and its versioned binding are in
[requirement-basis.json](requirement-basis.json). Read the [crosswalk rules](requirement-basis.md)
before treating an observation as a partner obligation. Deterministic validation
does not establish certification, factual truth, or Microsoft approval.

## Inventory and assessment rules

There are **121 canonical IDs: 60 package/conditional and 61 component rows**.
Every ID appears exactly once across the split assessment. The twelve conditional
rows are always present in the package assessment: score them, or record `not
applicable` with a substantive rationale. Absence is never completion. Repository
templates require a deliberate scaffolder-scope decision; no AI deliverable normally
means explicit AI not-applicable rows.

`SEC-01` through `SEC-03` belong only to the package assessment, matching their
library-level meaning and evidence. Use **one shared package security review
action and evidence packet** supporting three conclusions. Component assessments
contain neither duplicated conclusions nor pointer rows for these IDs.

Statuses remain `verified`, `gap`, `owner evidence required`, `not tested`, and
`not applicable`. Status is an assessment conclusion, not work status or partner
disposition. Existing published wording therefore needs no lossy status migration.

Basis legend: **D** direct obligation; **E** decomposition evidence check;
**C** conditional obligation; **X** versioned operational extension. X rows retain
canonical inventory slots but are **not baseline defects or vendor remediation demands
without separate policy approval**. Their requirement clause is context, not authority for
the stronger control. Exact operational basis is versioned in rubric.json.

## Licensing and provenance

| ID | Ledger | Clause | Basis | Requirement |
|---|---|---|---|---|
| LP-01 | Package | 1.1 | D | OSI-approved, non-copyleft license. |
| LP-02 | Package | 1.2 | D | Public source for shipped versions. |
| LP-03 | Package | 1.3 | D | OSI-approved, non-copyleft direct/transitive licenses. |
| LP-04 | Package | 2.12 | E | Preserve required license/notice files. |
| LP-05 | Package | 1.4 | E | PackageLicenseExpression. |
| LP-06 | Package | 1.4 | E | RepositoryUrl. |
| LP-07 | Package | 1.4 | E | RepositoryCommit SHA. |
| LP-08 | Package | 1.4 | E | Authors. |
| LP-09 | Package | 1.4 | E | ProjectUrl. |
| LP-10 | Package | 2.5 | E | Every binary attributable to public partner source or clear NuGet/npm upstream; exact partner source mapping. |

## Package integrity, SBOM, and provenance

| ID | Ledger | Clause | Basis | Requirement |
|---|---|---|---|---|
| PI-01 | Package | 2.1 | E | Strong-name every assembly. |
| PI-02 | Package | 2.1 | E | Authenticode every assembly with verified publisher identity. |
| PI-03 | Package | 2.2 | D | NuGet-sign every shipped .nupkg. |
| PI-04 | Package | 2.2 | X | Separate NuGet.org repository signature expectation. |
| PI-05 | Package | 2.3 | D | OIDC/trusted publishing, no long-lived secrets. |
| PI-06 | Package | 2.4 | D | SPDX/CycloneDX SBOM every release. |
| PI-07 | Package | 2.4 | E | Direct and transitive dependency inventory. |
| PI-08 | Package | 2.12 | E | Bundled third-party JS/CSS upstream and version in SBOM. |
| PI-09 | Package | 2.12 | E | Preserve bundled third-party notices. |
| PI-10 | Package | 2.4 | X | Final signed package digest binding. |
| PI-11 | Package | 2.5 | X | Source/workflow/dependencies/signatures/publication provenance. |
| PI-12 | Package | 8.3 | X | Release evidence retention policy. |

## Security and vulnerability management

| ID | Ledger | Clause | Basis | Requirement |
|---|---|---|---|---|
| SEC-01 | Package | 2.6 | E | Library threat model; shared package action. |
| SEC-02 | Package | 2.6 | E | Library security review before release; same action. |
| SEC-03 | Package | 2.6 | E | Address or document acceptance of findings before release; same action. |
| SEC-04 | Package | 2.7 | E | SECURITY.md private disclosure contact. |
| SEC-05 | Package | 2.7 | E | Report/fix privately; disclose after fix. |
| SEC-06 | Package | 2.9 | E | Release dependency scanning; PR scanning optional. |
| SEC-07 | Package | 2.9 | E | No known unpatched High/Critical direct/transitive CVEs at ship. |
| SEC-08 | Package | 2.8 | E | .NET monthly servicing cadence after fix readiness. |
| SEC-09 | Package | 2.8 | C | Exploited critical issues may warrant an out-of-band release. |
| SEC-10 | Component | 2.10 | E | No default phone-home, telemetry, or remote assets. |
| SEC-11 | Component | 2.10 | C | Telemetry opt-in and documented if present. |
| SEC-12 | Component | 2.11 | D | Static SSR and Interactive Server threat mitigation. |
| SEC-13 | Component | 2.11 | E | Browser input is not authorization evidence. |

## Accessibility

| ID | Ledger | Clause | Basis | Requirement |
|---|---|---|---|---|
| A11Y-01 | Component | 3.1 | D | WCAG 2.2 AA for default interactive configurations. |
| A11Y-02 | Component | 3.1 | E | No known AA failures at ship. |
| A11Y-03 | Component | 3.3 | E | Accessibility Insights for Web FastPass clean each release. |
| A11Y-04 | Component | 3.3 | E | Full Accessibility Insights Assessment each major release. |
| A11Y-05 | Component | 3.4 | E | NVDA, JAWS, or Windows Narrator each major release. |
| A11Y-06 | Component | 3.1 | E | Keyboard operation. |
| A11Y-07 | Component | 3.1 | E | Applicable focus behavior. |
| A11Y-08 | Component | 3.1 | E | Semantic roles, names, values, states, relationships. |
| A11Y-09 | Component | 3.4 | E | Selection/expansion/validation/async announcements. |
| A11Y-10 | Component | 3.2 | D | Windows High Contrast and forced-colors. |
| A11Y-11 | Component | 3.5 | E | All user-visible strings localizable. |
| A11Y-12 | Component | 3.5 | C | RTL optional, assessed when claimed. |

## Blazor engineering quality

| ID | Ledger | Clause | Basis | Requirement |
|---|---|---|---|---|
| BEQ-01 | Component | 4.1 | D | Latest stable .NET on GA day; earlier versions optional. |
| BEQ-02 | Component | 4.2 | E | Explicit supported render modes. |
| BEQ-03 | Component | 4.2 | E | Document supported modes and clear error elsewhere. |
| BEQ-04 | Component | 4.2 | E | Prerendering must not throw. |
| BEQ-05 | Component | 4.2 | X | Separate SSR usefulness contract. |
| BEQ-06 | Component | 4.2 | E | Correct supported Interactive Server behavior. |
| BEQ-07 | Component | 4.2 | E | Correct supported Interactive WebAssembly behavior. |
| BEQ-08 | Component | 4.2 | E | Correct supported Auto transitions. |
| BEQ-09 | Component | 4.3 | E | Parameter auto-properties, no required/init (BL0007), no internal mutation. |
| BEQ-10 | Component | 4.3 | E | EditorRequired where appropriate. |
| BEQ-11 | Component | 4.4 | D | EventCallback, not Action/Func. |
| BEQ-12 | Component | 4.5 | E | Observe callbacks; DispatchExceptionAsync for fire-and-forget errors. |
| BEQ-13 | Component | 4.5 | E | InvokeAsync + StateHasChanged for external events. |
| BEQ-14 | Component | 4.5 | E | No blocking or unsupported threading on renderer context. |
| BEQ-15 | Component | 4.6 | E | Dispose owned resources; catch JSDisconnectedException. |
| BEQ-16 | Component | 4.6 | E | IAsyncDisposable for owned subscriptions/timers/CTS/JS references. |
| BEQ-17 | Component | 4.7 | E | Collocated .razor.js from OnAfterRenderAsync; dispose JS/.NET references. |
| BEQ-18 | Component | 4.7 | E | No JS interop in OnInitializedAsync. |
| BEQ-19 | Component | 2.11 | E | Typed serialization and untrusted-value escaping. |
| BEQ-20 | Component | 4.8 | D | .razor.css, avoid inline styles, documented prefixed global CSS. |
| BEQ-21 | Package | 4.11 | D | No new .NET/BL* analyzer warnings; migration plans do not substitute. |
| BEQ-22 | Component | 4.12 | E | XML documentation for all public APIs. |
| BEQ-23 | Component | 4.12 | E | Public docs and component samples for every supported mode. |
| BEQ-24 | Package | 4.10 | D | SemVer, Experimental, Obsolete guidance for at least one minor version. |

## Trimming and AOT

| ID | Ledger | Clause | Basis | Requirement |
|---|---|---|---|---|
| TA-01 | Component | 4.9; 5.7 | E | IsTrimmable=true; clean trimmed WASM publish; avoid bloated transitive dependencies. |
| TA-02 | Component | 4.9 | E | Annotate/fix trim and AOT warnings, do not suppress. |
| TA-03 | Component | 4.9 | E | Trimmed runtime smoke test. |
| TA-04 | Component | 4.9 | E | Reflection/dynamic-code annotations. |
| TA-05 | Component | 4.9 | E | Clean WASM AOT compilation, not merely when vendor claims support. |
| TA-06 | Component | 4.9 | E | AOT runtime smoke test. |
| TA-07 | Package | 4.9 | X | Separately documented trim/AOT matrix. |

## Performance

| ID | Ledger | Clause | Basis | Requirement |
|---|---|---|---|---|
| PERF-01 | Component | 5.1 | D | Minimize renders, ShouldRender where appropriate, small trees. |
| PERF-02 | Component | 5.2 | D | @key on repeated elements. |
| PERF-03 | Component | 5.3 | D | Avoid heavy OnParametersSet/render work. |
| PERF-04 | Component | 5.4 | D | Virtualize or document consumer virtualization. |
| PERF-05 | Component | 5.5 | D | Narrow cascading values or IsFixed. |
| PERF-06 | Component | 5.6 | D | Small per-circuit state, no large retained graphs. |
| PERF-07 | Component | 5.6 | X | Payload/allocation/copy measurements. |
| PERF-08 | Component | 5.7 | X | Measured WASM size budget, beyond normative trimming guidance. |
| PERF-09 | Component | 5.1 | X | Published performance targets. |
| PERF-10 | Component | 5.1 | X | Representative benchmark evidence matrix. |

## CI, documentation, and release validation

| ID | Ledger | Clause | Basis | Requirement |
|---|---|---|---|---|
| CI-01 | Package | 8.3 | X | PR restore/build/test/package CI. |
| CI-02 | Component | 8.3 | X | Per-defect regression tests. |
| CI-03 | Component | 4.2 | X | Automated browser tests. |
| CI-04 | Component | 3.3 | X | Additional agreed accessibility CI cadence. |
| CI-05 | Package | 2.9 | X | Enforced scan/release gates. |
| CI-06 | Package | 8.3 | X | Branch protection and review checks. |
| CI-07 | Package | 2.3 | X | Untrusted-build/privileged-publish separation. |
| CI-08 | Package | 2.2 | X | Immutable artifact promotion. |
| CI-09 | Component | 4.12 | X | Executable documentation behavior assertions. |
| CI-10 | Component | 8.3 | X | Probe prerequisite audit. |
| CI-11 | Component | 8.3 | E | Recheck applicable release requirements. |

## Support, servicing, and lifecycle

| ID | Ledger | Clause | Basis | Requirement |
|---|---|---|---|---|
| SUP-01 | Package | 8.1 | E | Active support for security, accessibility, engineering. |
| SUP-02 | Package | 8.1 | E | Single published contact. |
| SUP-03 | Package | 8.1 | E | Published response SLA. |
| SUP-04 | Package | 8.1 | X | Separate supported-version matrix. |
| SUP-05 | Package | 2.8 | X | Separately documented security servicing cadence. |
| SUP-06 | Package | 8.2 | D | Sufficient advance public EOL notice. |
| SUP-07 | Package | 8.4 | E | Public non-security shipped-version issues. |
| SUP-08 | Package | 8.4 | E | Private security disclosure until fix. |
| SUP-09 | Component | 8.3 | D | Reverify each release. |
| SUP-10 | Package | 8.5 | D | Microsoft may stop promotion for nonadherence; no vendor suspension workflow is required. |

## Conditional scaffolders

| ID | Ledger | Clause | Basis | Requirement |
|---|---|---|---|---|
| SCF-01 | Package | 6.intro | C | dotnet scaffold, not dotnet new; dotnet/scaffolding contribution. |
| SCF-02 | Package | 6.1 | C | Generated accessibility/Blazor quality. |
| SCF-03 | Package | 6.2 | C | .NET and qualifying partner dependencies only. |
| SCF-04 | Package | 6.3 | C | Signed nuget.org packages and pinned partner versions. |
| SCF-05 | Package | 6.4 | C | No arbitrary script execution. |
| SCF-06 | Package | 6.5 | C | Updates track library changes. |

## Conditional AI skills

| ID | Ledger | Clause | Basis | Requirement |
|---|---|---|---|---|
| AI-01 | Package | 7.intro | C | dotnet-blazor plugin contribution in dotnet/skills. |
| AI-02 | Package | 7.intro | C | Contribution format, evals, ownership. |
| AI-03 | Package | 7.2; 7.3 | C | Accurate .NET release day and prompt library-pattern updates. |
| AI-04 | Package | 7.4 | C | .NET and qualifying partner dependencies only. |
| AI-05 | Package | 7.4 | C | No proprietary/closed-source dependencies. |
| AI-06 | Package | 7.1 | C | Responsible AI review before merge. |

## Outside the canonical count

The supplementary trim-analysis audit ID in rubric.json's `extensions` array is
separate from these 121 rows. Its old equivalent-configuration wording is not a
waiver of the baseline IsTrimmable requirement. Stronger CI/provenance/performance
controls already occupying canonical slots remain in those slots with X labels.
Do not add a supplementary row to canonical assessments or remove X rows.

## Frozen legacy artifacts

[rubric.v1.3.0.json](rubric.v1.3.0.json) and
[checklist.v1.3.0.md](checklist.v1.3.0.md) preserve the previous 110-row meaning and
conditional-overlay behavior. They are historical contracts, not the new
normative interpretation. Revisions cannot silently change rubric generations.
