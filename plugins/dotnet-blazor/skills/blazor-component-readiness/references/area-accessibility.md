# Accessibility and localization

Applies to `A11Y-*`.

Keep these evidence layers distinct: source markup/handlers/CSS, automated scan, browser
interaction, accessibility tree, assistive-technology behavior, formal conformance assessment, and
owner attestation. One layer does not silently prove another.

For each claimed render mode, exercise applicable pointer/keyboard behavior, focus entry/exit and
restoration, selection/expansion/validation/async states, computed name/role/state, localized
strings, LTR/RTL, and forced-colors/high-contrast behavior. Record control state, browser, mode,
input sequence, expected result, and observed result.

Apply the [supported-context preflight](area-blazor-runtime.md#cheap-preflight) before scoring.
For an external-name or description comparison, use distinct expected and fallback text, identify
the actual interactive accessibility node, and establish that the referenced targets resolve.
A matched native control can isolate target/capture behavior; it does not prove a vendor API
contract. Preserve release-alignment and unsupported-fixture qualifications. A programmatic
accessibility-tree observation is not proof of label-click activation, visible validation UI, or
assistive-technology speech.

Use `gap` for reproducible semantic, keyboard, focus, announcement, localization, or contrast
failure. Use `owner evidence required` for private formal assessment or support-matrix records. Use
`not tested` when an applicable interaction or assistive-technology probe was not run.

Keep private conformance evidence separate from reproducible behavior: an unrun screen-reader or
live-region probe is `not tested`, not automatically owner evidence required. A row conditioned on
an RTL claim is `not applicable` when RTL is explicitly unclaimed; unknown claim coverage remains
`not tested`.

Source ARIA does not establish computed semantics or conformance. An automated scanner pass does
not override a deterministic browser or assistive-technology failure.
