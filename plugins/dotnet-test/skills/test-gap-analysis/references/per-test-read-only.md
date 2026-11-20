# Per-Test Read-Only Assessment

This is the composition contract for `mode: per-test-read-only`, not a suite
audit or a mutation runner. `test-gap-analysis` owns this methodology and the
[mutation catalog](mutation-catalog.md); consumers own decisions and scoring.

## Inputs and execution boundary

The caller supplies resolved test identifiers/bodies, relevant setup and
helpers, language/framework assertion semantics, and available production
context. A batch may contain several tests, but assess each independently.

- Read only those tests, their fixtures/helpers, and the production call chain
  needed to explain their claimed outcomes. No broad suite discovery.
- Do not run tests, build/restore, execute/import production code, install
  tooling, apply mutations, edit files, invoke agents, or recurse into another
  grading/audit workflow. A read of this reference is not execution permission.
- Missing source, an unresolved call chain, or unsupported assertion semantics
  yields **N/A / unverified** for the affected assessment, with the reason.
  Assess any remaining known behavior normally; absence is not a weakness.
- Do not replace the caller's report with a suite-wide Strong/Mixed/Weak verdict,
  a mutation score, or a coverage dashboard.

## Assessment

1. **Bind the claim.** State the behavior this individual test promises from
   its name, arranged inputs, act, assertions, and any explicit contract.
   Resolve a contradiction as an evidence gap instead of inventing intent.
   An exception-only test owns that invalid-input outcome, not happy paths;
   a price-only test does not own an unrelated flag or formatting field.
   Do not demand every method branch, adjacent scenario, or sibling test case.
2. **Trace observations.** Map each supplied input/sequence through the public
   caller to its return value, exception, state, or external side effect and
   the assertion that observes it. Include relevant fixture cleanup/assertions
   and helpers executed by this test, but never borrow another test's checks
   or deduct because another test is absent.
3. **Probe meaningful changes.** Use the applicable categories in the owned
   mutation catalog without enumerating every operator. Fix all arguments when
   comparing original and mutant. First replay the change mentally on this
   test's existing inputs and assertions. A changed observation that makes a
   relevant assertion fail is **Likely killed (inferred)**, even if the check
   is indirect or the return value is not asserted directly.
4. **Prove an actionable survivor.** For a change the current test appears to
   miss, supply a distinguishing witness within its claimed behavior:
   `input/sequence -> original observation -> mutant observation`.
   Explain why the current test's assertions still pass, and how the smallest
   input/assertion/fixture change would expose the difference. If a new input
   is needed, distinguish that proposed witness from the current test input;
   do not pretend an assertion on the proposed witness already exists.
   A witness from an unrelated behavior is not a finding against this test.
5. **Filter equivalence and uncertainty.** Omit non-compiling changes,
   impossible inputs, equivalent guards/boundaries, private representation
   changes with no public effect, and duplicate syntax variants. In particular,
   removing a guard that falls through to the same public exception is not a
   survivor; `<` to `<=` at a floor is equivalent when both return the floor.
   Do not require exception message/parameter metadata unless it is an
   established contract. If original and mutant cannot be distinguished from
   available context, mark the assessment unverified rather than deducting.
6. **Return evidence, not grades.** Report only credible distinct findings and
   relevant protected behavior. An empty findings list is valid; do not invent
   a mutant, improvement, count, or denominator to fill the report.

## Return contract

For each requested test, return:

| Field | Content |
|---|---|
| Test / claim | Stable identifier and the individual behavior assessed |
| Availability | Assessed, or N/A / unverified with the missing evidence |
| Change / witness | Exact existing expression/condition/side effect changed; fixed input or sequence |
| Observations | Original versus mutant caller-visible outcomes on that same witness |
| Detection evidence | This test's relevant assertion and why it would fail or still pass; distinguish current inputs from a proposed witness |
| Classification | Likely killed (inferred), or Candidate survivor (unverified) |
| Smallest improvement | Concrete input, expected assertion, or fixture change for each candidate survivor; none when protected |

These fields form an evidence ledger, not a mandatory extra table in the
consumer's final response. Preserve source/test locations when available.
Keep evidence proportional to the claim; stop once the claim is protected or
no credible observable candidate remains.

Only already-supplied execution evidence for this exact test, source revision,
and mutation may use **Killed (executed)** or **Survived (executed)**. A killed
result requires a relevant assertion failure, not a build/runner failure.
Keep such evidence separate from static classifications. Never label a static
assessment Killed/Survived alone or report empirical killed/total counts,
percentages, or a mutation score from source reasoning.

## Examples

- A test withdraws 40 from a balance of 100 and compares the balance with
  itself. Omitting the decrement leaves 100 instead of 60; the self-comparison
  still passes. **Candidate survivor (unverified)**; replace that comparison
  with the literal expected balance 60. Do not credit a sibling balance test.
- A test claims to verify the correct standard fee calculation, but checks
  only that the cost is positive. A fee change yields 12 instead of 10 for
  its arranged order while that check still passes. Recommend equality to 10
  for that order, not generic "stronger assertions". If the test deliberately
  claims only positivity, that same change is not a gap against its claim;
  do not require an exact fee or unrelated cancellation coverage.
- A test asserts the promised exception type for one invalid input. Removing
  its guard still throws that same type later and changes no contracted side
  effect. Omit the equivalent mutation; do not demand message assertions.
- A focused test pins the result at a threshold. An inclusive-to-exclusive
  change alters that asserted result: **Likely killed (inferred)**. Do not
  deduct for not testing a different scenario, or call this an executed kill.
