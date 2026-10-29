# Shared analysis context

Reuse this context when analysis skills compose; it is not a new invocation
API, output schema, scoring rubric or mandatory report file. A focused standalone
review may keep it in memory. Do not invoke another skill just to construct it.

1. **Scope and authority:** record selected files/tests, requested dimensions,
   read-only/edit and execution permissions. A prior audit or tool invocation
   cannot expand them. Curated grading admits only its supplied tests.
2. **Inventory once:** retain language/framework/version, exact test identifiers,
   source/test regions, fixture/helper paths and unresolved entries. Mixed files
   such as Rust inline test modules belong in both relevant inventories.
   Reuse caller-provided bodies and known paths; read only missing or changed
   context. Heuristic source references are not proof of assertion coverage.
3. **Evidence ledger:** per test/public outcome, record the actual assertion
   predicate, independent expected outcome, protected behavior, plausible missed
   defect and evidence location. Credit sound tests; unknown context stays
   unknown. Preserve static predictions separately from executed results and
   measured coverage, with command/revision identity when supplied.
4. **Findings:** key observations by test, location and behavioral consequence.
   Merge the same observation across dimensions without discarding distinct
   risks. Derive totals from the ledger; do not count one defect several times
   or mix unknown entries into a proven-clean denominator.
5. **Invalidate narrowly:** refresh evidence when relevant source, tests,
   fixtures, configuration or artifacts change. Never treat stale evidence
   as validation or rerun unchanged discovery merely for a new dimension.

Each consumer retains its own purpose: `assertion-quality` measures assertion
depth/categories; `test-anti-patterns` ranks pragmatic risks;
`test-smell-detection` applies its formal taxonomy; `test-gap-analysis` owns
counterfactual reasoning; `grade-tests` owns curated readiness and A-F grading.
Passing this context does not require invoking all of them or replacing their
output contracts with one universal score.

Read-only composition does not authorize builds, tests, mutations, tagging or
file edits. A child inherits the stricter caller limits. Missing framework
support is reported explicitly; do not borrow a superficially similar
ecosystem's assertion, fixture or execution semantics.
