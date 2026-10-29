# Bounded generation workflow

This is the shared broad-generation procedure owned by `test-engineer`.
The `code-testing` router reads it only for an inline fallback when that agent
is unavailable. It is not a second agent or a new public skill.

Apply the [shared test engineering contract](../unit-test-generation.prompt.md),
including operation/resource permissions, report-safe naming and completion.
Use this workflow for project/package-wide or multi-module generation. Focused
work uses that contract's focused workflow instead and creates no state files.

## 1. Establish scope and state

Capture every explicit requirement: modules/layers, public behavior,
collaborator seams, boundary combinations, integration needs, coverage thresholds
and artifacts. Treat a module name as an inventory heading, not one test.
Expand each bounded target into its non-trivial public operations and observable
validation paths, branches and state transitions. Do not inventory unrelated code.

Resolve one absolute non-stageable `<TESTAGENT_DIR>`:

1. Prefer host-provided session scratch/artifact storage.
2. Otherwise use `git rev-parse --path-format=absolute --git-path testagent`
   for worktree-specific Git metadata.
3. Outside Git, use a unique directory under OS temporary storage.

Keep `research.md`, `plan.md` and final `status.md` there, not in version-controlled
workspace content. Pass the absolute path to any worker. Never change `.gitignore`
or stage these artifacts. On a denial, do not probe alternative locations;
continue permitted work with the reasoning in context and report missing artifacts.

Use a single pass when the bounded suite fits in context. Iterate only for
concrete remaining requirements or measured gaps; extend the same inventory
and plan, not a fresh discovery/orchestration cycle.

## 2. Research once

Batch independent manifest, target and representative-test reads. Record in
`research.md`:

- the requirement checklist and bounded public API/outcome inventory;
- canonical test locations, source-to-test references and pinned framework APIs;
- test/fixture conventions, isolation needs and available seams;
- exact scoped build/test/discovery commands and required final entry point;
- requested coverage/report artifacts and observed capability blockers.

Apply the shared contract's mandatory `find-untested-sources` pairing policy at
every scope, including tiny or explicit target lists. Reuse an existing
completed, current helper result covering the same scope rather than rerunning
the same inventory.
Pairing is heuristic reference evidence, not behavioral coverage; inspect
relevant test bodies before treating a contract as tested.

Apply the shared contract's **Evidence-informed selection** to this same
inventory, not a second audit worklist. Record supplied pairing diagnostics,
coverage provenance/scope and reliable complexity only when applicable; missing
or stale metrics stay unknown. Native non-.NET evidence must be verified;
do not invoke .NET coverage/CRAP skills against another language.

For .NET runner/mode/filter selection use `run-tests`; preserve classic project
commands, `packages.config`, explicit compile registration and pinned API versions.
For other languages use the repository's runner and matching bundled reference.
Examples are fallback answers to specific gaps, not mandatory reading.

## 3. Plan and implement

Map each requirement to test names, inputs, independently derived expected
outcomes and canonical files in `plan.md`. Group collaborating targets into
coherent phases. Planning is not permission to edit; honor a plan-only request.
Record which evidence changed each priority and its limits. A paired but weakly
asserted high-risk branch can outrank an unpaired trivial file. Use measured
missing paths to select discriminating cases, not duplicate line-hitting tests.
Use CRAP only for requested risk ranking with reliable matching inputs; an
unavailable score does not block a contract-based plan.

Implement phases sequentially, inline by default. Read complete relevant target
logic and collaborators; choose inputs that distinguish requested modes instead
of merely executing them with equal outputs. Keep existing tests and runtime
behavior intact except for explicitly authorized repairs. Test-only Rust regions,
imports and setup are editable under the shared operation policy.

Register new files/projects exactly once where the build system requires it.
Do not upgrade a framework, migrate a project format or refactor a seam just
to generate tests. Add dependencies only when required and permitted.

Delegate substantial independent work only when separate context helps. Supply
the bounded phase, exact paths/commands, permissions, relevant guidance,
`<TESTAGENT_DIR>` and known blockers. Consume the resulting evidence; do not
launch builder/tester/linter agents just to rerun commands.

## 4. Verify proportionately

Run the narrowest command compiling changed tests and relevant dependencies.
A fresh-build test run can satisfy both build and test gates; TypeScript
transpilation alone is not type-checking. Build every affected target framework
where applicable. Use the recorded bounded workspace command when changes span
projects/registration or the repository requires that entry point.

On an actionable compiler error, make a concrete in-scope correction before
rebuilding, at most three attempts. For test failures, use the shared failure
classification, at most five corrective attempts while making progress. Stop
earlier for a repeated diagnostic, denied capability or out-of-scope fix.
Do not treat these limits as permission to add retries to the tests.
An authoritative host policy rejection instead latches the shared authorization
stop condition immediately; do not spend corrective attempts on alternate
command forms or workers for the denied objective.

Final validation must use fresh outputs, never `--no-build` after test changes.
Confirm the repository's real discovery entry point sees the intended tests,
including explicit classic-project registration and parameterized cases. A
successful scoped run does not prove the CI command selects them. Reuse a run
that already proves discovery and requested reports; do not demand a guessed
generic benchmark command instead of the repository's real entry point.

Required final nonzero exits, missing reports, cancelled execution and unexpected
zero tests block success, even when baseline evidence proves a failure pre-existed.
Retain valid failing regression evidence; never adapt it to a broken product.

## 5. Review behavior and close bounded gaps

Map explicit requirements to final test bodies and apply the shared contract's
behavior-review gate. Supply the same inventory, permissions and evidence;
do not repeat its skill invocations or interpret a static review as permission
to execute mutations.
Merge findings by affected outcome and consequence, retain credited sound
tests, and attach the smallest test repair to the existing plan. Do not close a
finding merely because its line now executes or the suite is green.

Check:

- Every requested behavior targets the named symbol, not an adjacent helper.
- Combined and positional requirements have inputs satisfying every qualifier.
- Exact and adjacent boundaries, invalid/empty/interior partitions and meaningful
  ordering, rollover, capacity or state invariants are represented where applicable.
- Expected values are independent, round-trip contracts remain valid, and
  secondary observables are contractual rather than incidental.
- Cross-module tests do not substitute for explicitly requested per-module tests.

Add one mutation-relevant case per distinct missing observable partition;
parameterize only equivalent sibling inputs. Preserve valid zero/singleton
boundary tests alongside non-degenerate cases. Never add tests solely for counts.
Collect coverage when requested and verify the actual scope-specific threshold;
configuration alone does not prove it. Static mutation thinking is not an
executed mutation score or revision-bound red-green proof.

Recheck only affected behavior after a fix, then rerun covering validation if
tests changed. Update the same findings ledger with repaired test names and
fresh results, retaining pre-change metrics as historical selection evidence.
Stop when all feasible requirements and distinct bounded
partitions are proved, or report concrete blockers. Do not recursively expand
into unrelated modules.

## 6. Persist and report

Write `status.md` with completed requirements, quality findings/fixes, exact
commands/results, evidence-based priorities, metric limitations, report artifacts
and blockers. Review the final working-tree
changes for only authorized deliverables and no intermediate artifacts.

The final response must apply the shared completion contract, including the
`| Requirement | Evidence |` table and exact test names. Report `SUCCESS` only
when required validation passed; use `PARTIAL`/`BLOCKED` for remaining failures,
denials or unverified coverage. Keep one owner responsible for this handoff.
