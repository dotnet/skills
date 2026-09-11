<!-- Generated from rubric.json. Do not edit by hand. -->
# Blazor component readiness checklist

**Rubric version:** 1.3.0
**Scope schema version:** 1

A public, versioned vendor self-assessment baseline. It is not certification or a Microsoft acceptance requirement.

## Status vocabulary

- `verified`
- `gap`
- `owner evidence required`
- `not tested`
- `not applicable`

## Licensing and provenance

- **LP-01** (`repository-wide`) Uses an OSI-approved, non-copyleft license.
- **LP-02** (`repository-wide`) Public source repository for shipped versions.
- **LP-03** (`repository-wide`) Acceptable licenses for all direct and transitive dependencies.
- **LP-04** (`repository-wide`) Required third-party notices preserved.
- **LP-05** (`repository-wide`) NuGet `PackageLicenseExpression`.
- **LP-06** (`repository-wide`) NuGet `RepositoryUrl`.
- **LP-07** (`repository-wide`) NuGet `RepositoryCommit`.
- **LP-08** (`repository-wide`) NuGet `Authors`.
- **LP-09** (`repository-wide`) NuGet `ProjectUrl`.
- **LP-10** (`repository-wide`) Released package maps to an exact public source commit.

## Package integrity, SBOM, and provenance

- **PI-01** (`repository-wide`) Every shipped assembly is strong-name signed.
- **PI-02** (`repository-wide`) Every shipped assembly is Authenticode signed with the maintainer's release identity.
- **PI-03** (`repository-wide`) NuGet package has a valid author signature.
- **PI-04** (`repository-wide`) NuGet.org repository signature is present where expected.
- **PI-05** (`repository-wide`) OIDC/trusted publishing is used instead of long-lived publish secrets.
- **PI-06** (`repository-wide`) SPDX or CycloneDX SBOM is published for every release.
- **PI-07** (`repository-wide`) SBOM includes direct and transitive NuGet/npm dependencies.
- **PI-08** (`repository-wide`) Bundled JS, CSS, fonts, themes, and other third-party assets are represented.
- **PI-09** (`repository-wide`) Required third-party license notices are represented.
- **PI-10** (`repository-wide`) SBOM and provenance bind to the exact final signed NuGet package digest.
- **PI-11** (`repository-wide`) Build provenance connects source SHA, workflow, dependencies, signatures, and publication.
- **PI-12** (`repository-wide`) Release evidence is retained long enough for validation and incident response.

## Security and vulnerability management

- **SEC-01** (`component-specific`) Documented threat model covers .NET, JS, browser, host page, render modes, and release pipeline.
- **SEC-02** (`component-specific`) Security review is completed before release.
- **SEC-03** (`component-specific`) Findings are resolved or accepted with documented rationale.
- **SEC-04** (`repository-wide`) `SECURITY.md` provides a private reporting channel.
- **SEC-05** (`repository-wide`) Coordinated disclosure process exists.
- **SEC-06** (`repository-wide`) Dependency vulnerability scanning runs for every release.
- **SEC-07** (`repository-wide`) No known unpatched High/Critical vulnerabilities at release time.
- **SEC-08** (`repository-wide`) Vulnerability servicing aligns with the supported .NET cadence.
- **SEC-09** (`repository-wide`) Emergency out-of-band release capability exists for exploited critical issues.
- **SEC-10** (`component-specific`) No unexpected default telemetry, phone-home behavior, or remote asset loading.
- **SEC-11** (`component-specific`) Optional telemetry is opt-in and documented.
- **SEC-12** (`component-specific`) Static SSR and Interactive Server trust boundaries are reviewed.
- **SEC-13** (`component-specific`) Browser event/input values are not represented as server authorization evidence.

## Accessibility

- **A11Y-01** (`component-specific`) WCAG 2.2 AA conformance for the supported configuration.
- **A11Y-02** (`component-specific`) No known AA failures at ship.
- **A11Y-03** (`component-specific`) Automated accessibility scanning is clean for every release.
- **A11Y-04** (`component-specific`) A full accessibility assessment is completed at least once per major release.
- **A11Y-05** (`component-specific`) Representative supported screen-reader smoke testing is recorded.
- **A11Y-06** (`component-specific`) Keyboard-only operation is verified.
- **A11Y-07** (`component-specific`) Focus order, trapping, restoration, and visible focus are verified where applicable.
- **A11Y-08** (`component-specific`) Roles, names, values, states, and relationships are correct.
- **A11Y-09** (`component-specific`) Selection, expansion/collapse, validation, and async loading are announced.
- **A11Y-10** (`component-specific`) Windows High Contrast and CSS `forced-colors` are supported.
- **A11Y-11** (`component-specific`) User-facing strings are localizable.
- **A11Y-12** (`component-specific`) RTL support is recorded when claimed.

## Blazor engineering quality

- **BEQ-01** (`component-specific`) Latest stable .NET is supported on GA day.
- **BEQ-02** (`component-specific`) Supported render modes are explicit.
- **BEQ-03** (`component-specific`) Unsupported modes fail safely or are clearly documented.
- **BEQ-04** (`component-specific`) Prerendering does not throw.
- **BEQ-05** (`component-specific`) Static SSR output has a documented usefulness/accessibility contract.
- **BEQ-06** (`component-specific`) Interactive Server is tested.
- **BEQ-07** (`component-specific`) Interactive WebAssembly or standalone WASM is tested when supported.
- **BEQ-08** (`component-specific`) Auto transition behavior is tested when supported.
- **BEQ-09** (`component-specific`) `[Parameter]` properties follow framework guidance and are not mutated by the component.
- **BEQ-10** (`component-specific`) `[EditorRequired]` is used where appropriate.
- **BEQ-11** (`component-specific`) Events use `EventCallback`/`EventCallback<T>`.
- **BEQ-12** (`component-specific`) Callback tasks are awaited and failures reach the host error path.
- **BEQ-13** (`component-specific`) `InvokeAsync` and `StateHasChanged` are used correctly.
- **BEQ-14** (`component-specific`) Blocking and unobserved fire-and-forget work are absent.
- **BEQ-15** (`component-specific`) Timers, subscriptions, cancellation tokens, JS references, and modules are cleaned up.
- **BEQ-16** (`component-specific`) `IAsyncDisposable` is used when cleanup crosses JS or async boundaries.
- **BEQ-17** (`component-specific`) JS interop uses narrow module-scoped APIs.
- **BEQ-18** (`component-specific`) Initialization-time JS interop is avoided.
- **BEQ-19** (`component-specific`) Serialization is typed and correctly escapes untrusted values.
- **BEQ-20** (`component-specific`) CSS isolation or a documented scoped global-style contract is used.
- **BEQ-21** (`repository-wide`) Nullable and .NET/Blazor analyzer results are clean or have an accepted migration plan.
- **BEQ-22** (`component-specific`) Public APIs have accurate XML documentation.
- **BEQ-23** (`component-specific`) Public samples cover every supported render mode.
- **BEQ-24** (`repository-wide`) SemVer, experimental API, obsolete API, and compatibility policy are explicit.

## Trimming and native AOT

- **TA-01** (`component-specific`) Package-based trimmed WASM publish succeeds.
- **TA-02** (`component-specific`) Trim analyzer warnings are resolved or accepted with evidence.
- **TA-03** (`component-specific`) Browser/runtime smoke test succeeds for the trimmed artifact.
- **TA-04** (`component-specific`) Reflection/dynamic-code surfaces have appropriate annotations or generated alternatives.
- **TA-05** (`component-specific`) Native WASM AOT publish succeeds when claimed.
- **TA-06** (`component-specific`) Native AOT runtime smoke test succeeds when claimed.
- **TA-07** (`repository-wide`) The supported trim/AOT matrix is documented.
- **TA-08** (`repository-wide`) The package explicitly opts into trim analysis with `<IsTrimmable>true</IsTrimmable>` or documents an equivalent supported configuration.

## Performance

- **PERF-01** (`component-specific`) `ShouldRender` is used only when justified.
- **PERF-02** (`component-specific`) `@key` is used where identity stability requires it.
- **PERF-03** (`component-specific`) Expensive render-time work is absent.
- **PERF-04** (`component-specific`) Large data sets use appropriate virtualization.
- **PERF-05** (`component-specific`) Cascading values do not cause unnecessary broad rerenders.
- **PERF-06** (`component-specific`) Interactive Server state and per-circuit memory are bounded.
- **PERF-07** (`component-specific`) Server-to-browser payload size, allocation, and copy costs are understood.
- **PERF-08** (`component-specific`) WASM dependency and bundle size are measured against a budget.
- **PERF-09** (`component-specific`) Startup, render, interaction, and large-data targets are documented.
- **PERF-10** (`component-specific`) Measurements cover supported representative scenarios.

## CI, documentation, and release validation

- **CI-01** (`repository-wide`) PR CI restores, builds, tests, and packages relevant targets.
- **CI-02** (`component-specific`) Deterministic regression tests cover every accepted product defect.
- **CI-03** (`component-specific`) Browser tests cover claimed render modes and JS behavior.
- **CI-04** (`component-specific`) Accessibility smoke tests run at the agreed cadence.
- **CI-05** (`repository-wide`) Dependency scans and release checks are required gates.
- **CI-06** (`repository-wide`) Default and release refs require appropriate review/checks.
- **CI-07** (`repository-wide`) Untrusted build execution is separated from privileged signing/publishing.
- **CI-08** (`repository-wide`) Signing/publishing consumes a verified immutable artifact.
- **CI-09** (`component-specific`) Documentation examples compile and assert behavior, not only syntax.
- **CI-10** (`component-specific`) Node, .NET, browser, and workload prerequisites are correct.
- **CI-11** (`component-specific`) Release checklist revalidates every applicable requirement.

## Support, servicing, and lifecycle

- **SUP-01** (`repository-wide`) Active maintainer/support owner is identified.
- **SUP-02** (`repository-wide`) Public contact exists.
- **SUP-03** (`repository-wide`) Response SLA is published.
- **SUP-04** (`repository-wide`) Supported versions are documented.
- **SUP-05** (`repository-wide`) Security patch cadence is documented.
- **SUP-06** (`repository-wide`) Public EOL notice precedes support termination.
- **SUP-07** (`repository-wide`) Non-security issues are tracked publicly.
- **SUP-08** (`repository-wide`) Security issues use coordinated disclosure.
- **SUP-09** (`component-specific`) Requirements are reverified for every release.
- **SUP-10** (`repository-wide`) The release process defines how readiness regressions suspend a release or supported status and how revalidation restores it.

## Optional overlay: Scaffolder readiness overlay

**Overlay ID:** `scaffolder`
**Overlay version:** 1.0.0
**Selection:** Explicit only

- **SCF-01** Integrates through the documented scaffolding mechanism for the target ecosystem.
- **SCF-02** Generated output meets the core accessibility and Blazor requirements.
- **SCF-03** Dependencies are limited to .NET and explicitly approved third-party libraries.
- **SCF-04** Packages and generated dependency versions are signed and pinned.
- **SCF-05** Scaffolding does not perform undocumented arbitrary script execution.
- **SCF-06** The scaffolder stays current with supported library versions.

## Optional overlay: AI-skill readiness overlay

**Overlay ID:** `ai-skill`
**Overlay version:** 1.0.0
**Selection:** Explicit only

- **AI-01** Contributed through the documented skill or plugin path for the target ecosystem.
- **AI-02** Follows the target repository's contribution guidance.
- **AI-03** Updated for each supported framework or library guidance change.
- **AI-04** Generated code uses only explicitly supported dependencies.
- **AI-05** Generated guidance remains portable and avoids undocumented proprietary lock-in.
- **AI-06** Applicable responsible-AI review is completed before release.
