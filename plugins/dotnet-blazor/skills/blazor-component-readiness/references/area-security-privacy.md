# Security and privacy

Applies to `SEC-*`.

Before claiming a vulnerability, state assets, actor capability, trust boundaries, preconditions,
bounded impact, confidence, and unknown controls. Cover the .NET host, browser DOM/events/storage,
JS modules and serialization, remote assets, server-rendered boundaries, repository/build
authority, signing, and dependency ingestion.

Collect direct evidence for threat modeling/review completion, disclosure route, exact-release
dependency scanning, patch/emergency release capability, telemetry/remote assets, and authorization
assumptions. Browser-originated state is application input, never authorization evidence.

Use `gap` only for concrete insecure behavior, an unsafe documented contract, or a directly missing
required public control. Missing private review records are `owner evidence required`. A current
scan does not prove release-time gating, and absence in inspected source does not prove every
distribution is telemetry-free.

A threat model or security review may be private unless the row explicitly requires publication;
do not turn public absence alone into a gap. Conversely, a conditional optional-telemetry row is
`not applicable` when the confirmed claims plus complete component/runtime closure expose no
optional telemetry or consent surface.

For browser-input trust rows, exact source may verify that event values are ordinary callback/input
data rather than authorization evidence. Do not mark the row inapplicable merely because the
component itself does not make an authorization decision.

For a concrete exploit claim, use an available security specialist read-only with the exact
package/source snapshot and reproduction, then independently adjudicate it. Do not broaden the
assessment or expose private evidence.
