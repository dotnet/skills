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

For `PERF-06`, trace reached component/shared state from its circuit-scoped root through
insertion, disposal, pruning and circuit teardown, rather than profiling every component by default.
A source-proven repeatable live-circuit path that adds entries without effective removal in that workload is a
`gap` even when targets are weakly referenced: retained keys/wrappers still grow. Name the
retained objects and workload; do not claim whole-component retention without proof.
Distinguish temporary allocations, framework caches, disconnected circuits, GC timing,
test-host references and process working set. A small field list proves no universal pass;
a memory increase alone proves no component leak. No universal memory threshold is required.

Payload, serialization and copy-cost troubleshooting is available only through
[requested guidance](remediation-guidance.md#requested-data-transfer-troubleshooting).
That boundary does not disable investigation needed for an actual DOCX obligation.

Separate mechanism rows from measured outcomes. Complete source can establish whether `ShouldRender`
or `@key` is present and whether a cascading-value surface exists; a passing reorder probe cannot
prove `@key`. For PERF-03, trace active parameter hooks, the reached inherited render branch,
serialization and caches. A complete traced path can verify this row without benchmarks only when
parameter-hook/render work is light, or expensive computation is offloaded to async lifecycle
methods or memoized. Bounded-but-heavy work, a wrapper scan or a cache marker alone cannot
verify it. Do not infer PERF-01
rerender frequency, PERF-05 descendant propagation, consumer-fragment cost or production latency.

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

For `TA-02`, any reachable source-observed trim/AOT warning suppression establishes a `gap`,
even if justified; no toolchain log is needed for that conflict. Inspect attributes, pragmas
and project suppression settings, not annotations alone. This does not prove warning-free output.

Search from the confirmed repository root for suppression attributes, pragmas and project settings,
including inherited library bases and reached helpers; a component-subdirectory search alone is not
closure. Only component-reachable findings decide TA-02.

For a source-observed suppression, cite `vendor-source-repository` evidence with a
`source:<relative logical path>` locator and the confirmed source capture's digest. The path must
be in the assessed component's allowed-source list; state the suppression and its reachability in
the claim/observation. This TA-02 `gap` remains valid when a toolchain log is also supplied, even
if a passed probe is cited. Other evidence kinds alone do not substitute for this source evidence.

For TA-02/TA-04 conclusions based on toolchain logs, name the supported SDK/workload/toolchain
and target framework; use `toolchain-probe-v1` with exact command, result and raw-log digest.
When any `toolchain-log` is supplied, TA-02 `verified` still requires a `passed` protocol;
TA-04 `verified`/`gap` still requires a `passed`/`failed` protocol respectively.
Without required results, retain `not tested` with the blocker and smallest follow-up.
TA-04 asks for annotation adequacy, not attribute presence: if suppression adequacy cannot be
checked statically, it remains `not tested` pending publish/runtime validation. TA-01/03/05/06
also require their executed publish/compile/runtime evidence. Never use `unresolved` as a status.

Register the protocol as `reproduced-runtime-observation`, use its confirmed basename as locator,
and set `provenance.method` to `protocol:toolchain-probe-v1`.
