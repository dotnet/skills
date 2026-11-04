# Security and privacy

Applies to `SEC-*`.

Before claiming a vulnerability, state assets, actor capability, trust boundaries, preconditions,
bounded impact, confidence, and unknown controls. Cover the .NET host, browser DOM/events/storage,
JS modules and serialization, remote assets, server-rendered boundaries, repository/build
authority, signing, and dependency ingestion.

Before SEC-12, inspect the retained source inventory for security/threat-model documents,
including unlinked repository docs; capture/confirm relevant files before scoring. A public,
component-applicable record covering static SSR and Interactive Server guidance, with its
mitigation claims spot-checked against code, can verify SEC-12. This is not a full security
audit or evidence for SEC-01/02/03 private release review. For SEC-12, a public record in the vendor's own repository can satisfy the row only when it applies
to the assessed component, covers both static SSR and Interactive Server guidance, and its
mitigation claims hold where spot-checked against code. Do not require a separate private
release-review record for that satisfied row. A missing/private required record remains `owner
evidence required`; failed acquisition is not proof of absence.

Collect direct evidence for threat modeling/review completion, disclosure route, exact-release
dependency scanning, patch/emergency release capability, telemetry/remote assets, and authorization
assumptions. Browser-originated state is application input, never authorization evidence.

Use `gap` only for concrete insecure behavior, an unsafe documented contract, or a directly missing
required public control. Missing private review records are `owner evidence required`. A current
scan does not prove release-time gating. For SEC-10/11, inventory the exact shipped scripts,
styles/fonts and reached .NET/browser paths; classify default requests separately from
caller-triggered URL features. Complete default-path closure can verify SEC-10 without a
network capture; an incomplete transitive inventory stays `not tested`. Scope the result to
this component and exact distribution, not every release or caller configuration.

A threat model or security review may be private unless the row explicitly requires publication;
do not turn public absence alone into a gap. Conversely, a conditional optional-telemetry row is
`not applicable` when complete exact component/runtime closure exposes no optional telemetry
or consent surface, even without an explicit non-telemetry claim. Absence of a wrapper option
alone is insufficient; classify the reached bundled runtime too. Do not invent a consent probe for an
absent feature.

For browser-input trust rows, exact source may verify that event values are ordinary callback/input
data rather than authorization evidence. Do not mark the row inapplicable merely because the
component itself does not make an authorization decision.

For a concrete exploit claim, use an available security specialist read-only with the exact
package/source snapshot and reproduction, then independently adjudicate it. Do not broaden the
assessment or expose private evidence.
