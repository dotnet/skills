# Trimming, AOT, and performance

Applies to `TA-*` and `PERF-*`.

Apply the skill's [assessed-code execution prerequisite](../SKILL.md#assessed-code-execution-prerequisite)
before restore, publish, runtime or benchmark execution. A disposable consumer is not a sandbox.

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

Current `TA-05` applicability is not waived by an absent vendor AOT claim. Run applicable native
WebAssembly AOT only after the execution prerequisite is satisfied, and keep its evidence separate
from trimming. Applicable AOT work that is not performed remains `not tested` with the actual
blocker; lack of a claim alone is not a `not applicable` rationale.

Investigate the actual DOCX performance obligations within the approved scope.
Use `gap` for demonstrated identity instability, unnecessary repeated/unbounded work
or retained-state problems, not merely a suspicious source pattern. Use `not tested`
when evidence needed for the applicable conclusion was not obtained.
Do not demand a vendor budget, written performance targets or benchmark matrix.
Missing such documents is not an independent readiness shortfall.

For `PERF-06`, inspect relevant component/shared state and reuse applicable
evidence. Investigate a concrete retention concern when authorized, rather than
profiling every component by default. Distinguish retained objects from temporary
allocations, framework caches, disconnected circuits, GC timing, test-host
references and process working set. A small field list proves no universal pass;
a memory increase alone proves no component leak. Preserve workload-specific
facts and actual evidence limits without inventing a universal memory threshold.

Payload, serialization and copy-cost troubleshooting is available only through
[requested guidance](remediation-guidance.md#requested-data-transfer-troubleshooting).
That boundary does not disable investigation needed for an actual DOCX obligation.

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

If an authorized measurement needs workload context, establish the relevant
scenario and inputs first. Describe the configuration and workload actually
observed; do not generalize a narrow measurement to production-wide coverage.
Ask for necessary context, not a new formal budget document or benchmark campaign
merely to fill a report row. Supplied targets may inform explicitly requested work.

For a comparison that adjudicates `TA-02` or `TA-04`, name the supported SDK/workload/toolchain and
target framework. A `verified` or `gap` result backed by supplied toolchain logs uses a canonical
`toolchain-probe-v1` protocol with the exact command, supported-toolchain disposition, result, and
raw-log digest. Otherwise finish the row as `not tested` and put the exact toolchain, workload, or
diagnostic blocker in the observation plus the smallest rerun in `assessment_follow_up`. Never emit
or retain an undefined `unresolved` final status.

Register the protocol as `reproduced-runtime-observation`, use its confirmed basename as locator,
and set `provenance.method` to `protocol:toolchain-probe-v1`.
