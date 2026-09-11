# Trimming, AOT, and performance

Applies to `TA-*` and `PERF-*`.

For package/library analyzer, trim and AOT checks, require the
[original source closure](artifact-acquisition.md#original-library-source-closure), not library
or worker orchestration. Apply only the requested ownership's rows; package-only does not need
component performance or lifecycle probes.
Pin and record the supported SDK before consumer restore; for `net10.0`, do not inherit a
preview SDK merely because it is the host default. If public NuGet v3 is unavailable, a public
v2 endpoint is an allowed read-only fallback; retain both outcomes and never change reviewed
manifests or configuration.

Restore the exact package into a disposable consumer. Publish with trimming and trim analysis;
separate package-attributable warnings from app/toolchain warnings, then load and exercise the
published output. Inspect reflection, dynamic code, serialization, and JS interop. Configuration
alone does not prove runtime behavior; successful publish without browser exercise is incomplete.

Run native WebAssembly AOT only when claimed or explicitly requested, and keep its evidence
separate from trimming.

Before performance measurement, define representative item count/depth/templates/interactions and
relevant server circuits, WebAssembly startup, bundle, payload, allocation, latency, and retained
state. Source may identify risks but cannot verify a budget.

Use `gap` for demonstrated identity instability, repeated/unbounded work, retained-state problems,
or a measured budget failure. Use `owner evidence required` for private targets/budgets/records and
`not tested` when an applicable deterministic benchmark or runtime probe was not performed.

Separate mechanism rows from measured outcomes. Complete source can establish whether `ShouldRender`
or `@key` is present and whether a cascading-value surface exists; a passing reorder probe cannot
prove `@key`. Absence of expensive render-time work or bounded rerender cost still needs a complete
execution-path analysis or measurement rather than a narrow wrapper scan.

For `PERF-02`, use `not applicable` only when complete component and inherited-renderer source
proves the component owns no repeated identity surface where `@key` could apply, such as a rendered
loop or component-owned collection. If such a surface may exist but source does not establish
whether `@key` is used, use `not tested`; a passing reorder outcome cannot close the mechanism row.

For `PERF-05`, a mutable `CascadingValue` or missing `IsFixed` establishes an applicable rerender
risk, not unnecessary broad descendant rerenders. Without descendant render counts or equivalent
direct runtime evidence, use `not tested`. Use `gap` only when a representative update directly
demonstrates unnecessary broad rerenders. Use `not applicable` only when complete source proves no
applicable cascading-value update surface exists.

If the owner has not defined the representative scenarios, data sizes, targets, or budgets needed
for a measurement row, use `owner evidence required`. If those inputs are confirmed and only the
benchmark was not run, use `not tested`.

For a comparison that adjudicates `TA-02` or `TA-04`, name the supported SDK/workload/toolchain and
target framework. A `verified` or `gap` result backed by supplied toolchain logs uses a canonical
`toolchain-probe-v1` protocol with the exact command, supported-toolchain disposition, result, and
raw-log digest. Otherwise finish the row as `not tested` and put the exact toolchain, workload, or
diagnostic blocker in the observation plus the smallest rerun in `assessment_follow_up`. Never emit
or retain an undefined `unresolved` final status.

Register the protocol as `reproduced-runtime-observation`, use its confirmed basename as locator,
and set `provenance.method` to `protocol:toolchain-probe-v1`.
