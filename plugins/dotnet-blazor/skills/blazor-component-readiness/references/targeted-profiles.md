# Targeted follow-up profiles

Targeted work is allowed only for a named question, prior finding, or correction. It is not a
complete readiness assessment. Record the selected profile, every added/removed ID and reason,
package supplement choice, timebox, and excluded probes.

Use rubric IDs from `rubric.json`; this file supplies ranges/themes, not requirement definitions.

| Component shape | Start with |
|---|---|
| Text/value input | Relevant `A11Y-*` and `BEQ-*` rows for naming, keyboard, binding, validation, callbacks, persistence, cleanup, localization, and claimed modes. |
| Binary/selection | Add selected/checked/disabled state, keyboard, forms, and accessible state rows. |
| Grouped/dynamic selection | Add dynamic registration, removal, disabled selection, keyed reorder, callback/focus, and applicable `PERF-*`. |
| Numeric/culture-sensitive | Add typed round trips, nullable/invalid values, range/step, culture, formatting, localization, and serialization rows. |
| File/upload boundary | Add applicable `SEC-*`, `A11Y-*`, and `BEQ-*` rows for untrusted metadata/content, enforcement, progress/cancellation, cleanup, keyboard, and announcements. |

When a distributed package is listed but exact bytes remain unavailable after bounded acquisition,
include the applicable license/package-integrity rows and classify blocked checks `not tested`.
Never call those rows `not applicable` merely because transport failed.

A targeted correction may change only declared IDs and must bind the immediate predecessor plus
new evidence. Unselected rows are not reverified.
