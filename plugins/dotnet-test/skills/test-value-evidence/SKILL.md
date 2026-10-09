---
name: test-value-evidence
description: >-
  Assess tests for an identified fix, feature, PR or commit. USE FOR: review
  changed behavior or regression tests, prove pre-fix/missing behavior is
  detected, revision-bound red-green evidence. Polyglot. DO NOT USE without an
  actual production change: hypothetical mutations or general suite blind
  spots (test-gap-analysis). Also exclude per-test readiness/A-F grades (grade-tests),
  coverage (coverage-analysis), new suites (code-testing), or mutation engines.
license: MIT
---

# Test-value evidence

A passing test proves little unless it detects the relevant wrong behavior.
Assess the changed contracts and their directly related tests, not the entire
repository. Discover and follow the repository's instructions, pinned toolchain,
test commands, assertion conventions, and execution permissions. This skill
does not authorize edits in a read-only review, execution of untrusted code,
workflow dispatch, publication, or access to another session's checkout.

## Inputs and boundaries

Discover the requested diff/revision, production entry points, tests, fixtures,
and available execution artifacts. If the change cannot be identified, report
the missing scope instead of grading the workspace. Existing unchanged tests
can suffice; a source-only diff is not automatically untested.

Use `test-gap-analysis` for general existing-suite blind spots, `grade-tests`
for curated per-test readiness/quality, and existing repository mutation tooling
for mutation campaigns. Do not start a second mutation engine or require
full-suite mutation testing for every change. This skill is self-contained:
sibling skills are optional routing destinations, not required dependencies.

## Workflow

1. **Bind the contract to the tested revision.** For a PR, record its merge
   base and exact head SHA. For a commit, record the selected parent and commit;
   identify the parent explicitly for a merge commit. For uncommitted work,
   record the base SHA plus a reproducible patch identity (saved patch/hash,
   including relevant untracked files), not a fictitious committed revision.
   Evidence expires after relevant source, tests, helpers, configuration, or
   package changes unless revalidated.

   For each distinct behavior change, map:
   `requirement -> input/sequence -> expected public outcome -> production
   entry point -> named test/data case -> distinguishing assertion`.
   Derive the expected outcome independently from requirements or supported
   API/protocol semantics, not the implementation or the test's current value.
   Check build/package/configuration behavior, deleted/renamed code, removed
   tests, and shipping consumers as well as source. Include positive, negative,
   boundary, compatibility, cancellation, cleanup, and multi-module cases only
   where relevant to the changed contract.

   A mock of the changed component, self-comparison, or expected value computed
   by the same production algorithm does not provide an independent oracle.

2. **Choose the meaningful counterfactual.**

   | Change | Counterfactual | Required observation |
   |---|---|---|
   | Bug fix | Keep the regression test and restore the actual pre-fix defect. | The test fails for that defect, then passes with the fix. |
   | New feature | Disable/remove or corrupt the behavior, retaining the API shape and setup needed to build. | An assertion detects absent/wrong behavior, then passes with the feature. |
   | Stronger/test-only coverage | Introduce one plausible wrong public outcome. | The strengthened test detects it; the unmodified product passes. |
   | Behavior-preserving refactor | Compare the preserved contract before and after; optionally probe a relevant fault. | Preservation tests pass on both revisions; passing before is expected. |
   | Documentation/generated metadata only | Establish whether an executable contract changed. | Not applicable only with a concrete reason; generated or build metadata can change behavior. |

   For a fix, prefer the real pre-fix implementation with the same regression
   test, setup, dependencies, and inputs. If new APIs prevent that combination
   from compiling, restore only the defective behavior in the head version.
   Call this a **targeted counterfactual**, not a run on the base revision.
   Record its exact patch/hash. Never change assertions to manufacture red.

   For a feature, a missing-symbol compiler error is not behavioral proof.
   Choose a buildable no-op, disabled registration, old branch, or wrong output.
   A compiler/analyzer diagnostic can be valid behavior only when an executed
   harness asserts that exact diagnostic or its absence.

   Prefer small plausible faults: restore the old boundary, drop a required
   output, skip a callback, ignore an option, or return the wrong value.
   An equivalent mutation is not a gap. A crash, timeout, or exception counts
   only when it is the intended contract signal and diagnostics attribute it
   to the fault, not setup, discovery, flakes, or infrastructure.

3. **Execute paired runs only when authorized.** Implementation normally
   requires execution; read-only/advisory review normally requires inspection.
   A review request alone does not permit source edits or builds/tests.
   Never run untrusted code with privileged credentials.

   In an authorized implementation/verification environment:

   - Use an owned isolated checkout or experiment directory. Preserve the
     user's dirty files and other sessions/main checkout. Preflight concurrent
     processes and shared outputs/resources; do not kill unrelated processes.
   - Run the smallest relevant selection on the correct implementation.
     Confirm the named tests and data cases actually execute and pass.
   - Apply only the chosen defective behavior in the isolated experiment.
     Rebuild and rerun the same tests, assertions, data, dependencies, and
     applicable runtime configuration. Capture the named failure, assertion,
     expected/actual observation, process exit, and diagnostics.
   - Restore the correct implementation, rebuild, and rerun to confirm green
     again. Verify protected source/test bytes, remove only owned temporary
     resources, and never deliver the counterfactual patch in the product.

   Paired runs must not reuse stale build/test outputs or the other variant's
   effective caches. Use separate outputs/caches or clean only owned outputs
   and rebuild; do not rely on `--no-build` or a runner's success exit alone.
   Record selected/executed/passed/failed/skipped counts for each run. If a
   runner does not expose a count, say **unknown** rather than inventing it.
   Zero selection/execution, skips, missing tools, authentication, setup/build,
   unrelated failures, and unattributed timing flakes leave proof unverified.

   For cross-component/acceptance behavior, identify the producer-consumer
   contract first: ownership, lifecycle, handshake/no-handshake, exit semantics,
   fallback, compatibility, cancellation, and multi-module aggregation where
   applicable. Record the exact shipping binaries/packages/extensions and
   release branches. Rebuild/repack each variant, isolate effective package
   caches, prove which variant the real shipping consumer loaded, and run a
   representative composed command. Unit mocks or source-compatible compilation
   alone do not replace shipping-layout validation. Exercise applicable normal,
   suppressed, partial, legacy/incompatible, cancellation, nonzero-exit,
   zero-result, fallback, and multi-module paths. Keep rollout/servicing ownership
   explicit; a feature flag does not lower the evidence bar.

   In read-only/advisory mode, trace both revisions, setup/helpers, inputs, and
   assertions without edits or execution unless separately permitted. Label
   conclusions **static predictions**, never executed proof. Existing artifacts
   count only when their revisions/patches, loaded variant, selection, counts,
   and failure attribution match. Green CI or an author's claim alone is not
   red-green evidence. Say what is inaccessible or missing.

4. **Report only the evidence obtained.** Put a compact **Test-value evidence**
   section in the handoff or authorized review/description. State base/parent
   and head SHAs or tested patch identities. Use one row per changed contract,
   group tests protecting the same contract, and identify unassessed groups.

   | Contract / test case | Counterfactual | Without behavior | Correct implementation | Evidence / gap |
   |---|---|---|---|---|
   | Boundary defect / named case | Restore old comparison; patch identity | Executed: assertion failed | Executed: passed | Commands, exits, expected/actual, counts, artifacts |
   | New output / named case | Skip output write | Static: predicts failure | Not executed | Source/assertion locations; paired execution missing |

   Use **executed: detected**, **executed: survived**, **static: predicts
   detection**, **static: predicts survival**, **unverified**, **equivalent**,
   or **not applicable** accurately. Missing red/green or incomplete selection
   leaves paired proof unverified. Report survival only for an executed,
   non-equivalent fault; do not count equivalent edits as gaps. Preservation
   passes for a refactor are valid evidence, not missing-red defects.

   Include exact commands/exits, toolchain/framework/configuration, revision
   and counterfactual patch identities, per-run counts, and relevant artifact
   paths. Distinguish execution performed now from verified existing artifacts.
   Never combine static predictions with measured mutation counts/scores.
   Evidence for a final PR head does not prove every intermediate commit;
   assess a requested commit against its parent separately.

   Missing evidence is not automatically a proven defect or an inline review
   thread. Publication follows the caller's existing authorization gate; do
   not create a new GitHub object just to store a report or claim instructions
   enforce hooks, CI, or publication.

## Validation

- The evidence identifies the exact change, independent oracle, test/data case,
  and meaningful buildable counterfactual.
- A claimed bug-fix proof fails for the actual old defect and passes with the
  fix; a feature proof detects absent/wrong behavior rather than absent symbols.
- Executed claims have attributable outcomes, commands/exits, counts, and
  matching artifacts/consumer provenance; unavailable details stay explicit.
- Static predictions, survival, equivalence, preservation, and missing evidence
  remain distinct. Existing tests are credited without demanding redundant ones.
- Scope, read-only restrictions, clean variant outputs, parallel safety, and
  owned-resource cleanup were preserved.
