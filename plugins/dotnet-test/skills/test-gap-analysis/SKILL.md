---
name: test-gap-analysis
description: >-
  Find or close verified behavioral gaps in existing tests. EXISTING SUITES
  ONLY. USE FOR: "add missing edge cases", "would tests catch this bug?",
  weak tests, boundary/guard/logic/error-propagation gaps, or survived/
  pseudo-mutation analysis. Polyglot. DO NOT USE FOR: .NET line-vs-branch or
  Cobertura interpretation, arithmetic, plateaus, project-wide coverage gaps,
  or coverage-backed test/CRAP priorities (coverage-analysis; use native
  coverage tooling outside .NET); named-target CRAP (crap-score); test-mix/
  happy-vs-error classification, tagging, or trait distributions
  (test-tagging); new suites (code-testing-agent); assertion/smell audits; or
  mutation tools.
license: MIT
---

# Test Gap Analysis

Answer one question: **which caller-visible production behaviors could change
without an existing test failing?** Mutation reasoning is a probe, not the goal.
Inventory public outcomes first, then verify only credible gaps.

## Decision flow

### 1. Set scope

Discover production and test files from manifests and file types. After a narrow
search misses, inspect the current directory broadly before asking for paths.

| Request | Action |
|---|---|
| One component or named risk | Inventory every high-risk public outcome in scope; do not edit production code unless verification was requested |
| General small-component review | Inventory distinct outcomes and report caller-visible gaps from source/assertion mapping |
| Explicit survivor verification | Inventory all requested outcomes; execute one representative observable candidate for each distinct high-risk outcome under verification, then classify it as **Survived** or **Killed** |
| Explicit exhaustive audit | Read [references/mutation-catalog.md](references/mutation-catalog.md) and classify all meaningful candidates |
| Add tests to an existing suite | Analyze first; add tests only for verified survivors or demonstrated no-coverage outcomes |
| Create a new suite | Stop and use `code-testing-agent` |

When the request names a risk, turn it into a one-line public-outcome allowlist
before reading code. An outcome is not in scope merely because the same method writes it.
For `money math`, allow computed or returned amounts, rates, tier/boundary
choice, percentage base/order, floors/caps, and rounding; exclude non-monetary
state predicates (including derived booleans), identity, and formatting. Private
code is in scope only to trace an allowed outcome.

Do not expand a focused request into a repository audit, plan artifact, or
dashboard. Use source and tests directly for familiar frameworks. Invoke
`test-analysis-extensions` only when discovery or assertion semantics are
unclear.

### 2. Establish one baseline

Run the narrowest existing test command once. Confirm tests executed; exit 0
with build-only output is not green. Microsoft.Testing.Platform executables may
require `dotnet run`. If the suite cannot run, continue statically and label all
candidates **unverified**.

For an advisory review such as "would tests catch this?", stop execution after
that baseline. Source-to-assertion mapping is sufficient evidence for **No
coverage** and **Candidate survivor (unverified)**. Trace or run the unmodified
code once only when an original value is unclear. Apply mutations only for
explicit verification, an exhaustive audit, or closing gaps with tests.

### 3. Inventory public outcomes

For each public entry point, map:

- input partitions: classifier arms, compound conditions, invalid and
  nearest-valid guard boundaries, and default cases;
- each independent observation: returned field/variant, exception type,
  invalid-input acceptance, public state transition, or external side effect;
- private-helper composition, constants/rates, rounding, retries, cancellation,
  and error propagation as observed through the public caller.

Use `public input/sequence -> expected outcome -> existing assertion -> gap`.
One asserted return field does not cover another. One allowed result does not
cover its denial.

**Money math:** inventory the no-op path, every rate/tier and exact boundary,
operation order, percentage base or composition, floor/cap, and rounding. Trace
private helpers through the public result. A test asserting only a broad range
does not pin any exact amount.

**Ordered guards and retries:** inventory `invalid below minimum | first valid |
last allowed or retryable | first blocked | later blocked when equality
narrowing could distinguish it`, plus every accepted and rejected error class. A
test at the first blocked value does not protect the last allowed value or, by
itself, rule out an equality-narrowing gap at a later blocked value.

**Authorization:** enumerate each relevant identity/role, resource class, and
action from the caller's view. Untested `false`, forbidden, and unchanged-role
outcomes are first-class security gaps. Do not analyze variants of an allowed
path while a denial outcome remains uninventoried. Check each public surface:

- permission-returning APIs: every distinct role/resource class and every
  returned capability independently;
- action-dispatch APIs: each read/write/delete-style action branch, especially
  paths that must return denial;
- role/state transitions: accepted, rejected, invalid, null, and empty inputs,
  including outcomes that must leave state unchanged.

Mutation execution never substitutes for this ledger. Report every missing
high-risk denial even when only one representative candidate was run.

### 4. Admit only observable candidates

State `public input/sequence -> original observation -> mutant observation`.
Admit the candidate only when the last two differ under the current public
contract after tracing the full call chain.

Exclude:

- edits that require inserting or reordering statements rather than changing or
  removing an existing expression, condition, constant, return, or side effect;
- overflow behavior, exception message/`ParamName` metadata, or other semantics
  not established by the current contract, source intent, or tests;
- a removed guard or short-circuit that falls through to the same result,
  exception, state, and side effects;
- private representation changes that every public input sequence observes
  identically, even if the suite stays green;
- a mutation whose proposed test passes against both original and mutant;
- a standalone auto-property or trivial one-line wrapper/predicate with no
  meaningful branch, calculation, or side effect, unless the user names it;
- hypothetical future impact, generated code, logging/formatting-only changes,
  impossible values, and duplicate syntax variants.

Missing assertions make an **observable** candidate a survivor. They do not make
an inert mutation meaningful.

### 5. Rank and classify

Rank: (1) security denials, financial outcomes, errors, and state changes;
(2) wholly unasserted public outcomes; (3) boundaries or exact values reached by
weak assertions; (4) alternate variants of already-asserted behavior.

Finish the outcome inventory before selecting mutations. One killed attempt,
exception type, or switch arm does not clear its siblings.

| Result | Meaning |
|---|---|
| **Likely killed** | An existing assertion observes the changed outcome |
| **Candidate survivor (unverified)** | Observable change appears unasserted; not executed |
| **Survived** | Exact observable mutation executed and tests stayed green |
| **No coverage** | No test reaches the public outcome |
| **Equivalent** | No public observation changes; omit from findings |

Outside explicit verification, an exhaustive audit, or a requested test
addition, execute no mutations. Do not mutate to confirm obvious no coverage.
For explicit verification, execute one representative candidate per distinct
high-risk outcome in scope; do not stop after the first one or two while another
guard, action branch, error class, or denial remains unclassified. Omit
equivalent syntax variants.

### 6. Verify without creating false positives

Enter this phase only for explicit verification, an exhaustive audit, or a
requested test addition.

1. Apply one candidate and confirm the diff changes exactly one intended
   expression.
2. Run the narrowest covering test: green means **Survived**, red means
   **Killed**, for that edit only.
3. Revert immediately and confirm the clean source/test baseline.
4. After a green run, re-check the public counterfactual. Execution proves the
   suite missed the edit, not that the edit changes behavior; drop inert or
   unobservable mutants.

Never leave mutations in the workspace. Before reporting, reconcile every
unasserted high-risk outcome as **Survived**, **Candidate survivor
(unverified)**, **No coverage**, or omitted **Equivalent**. Stop when no credible
public gap remains; do not fill a report with internal details or calculate a
score unless the user requested an exhaustive audit.

### 7. Close gaps only when requested

Add focused tests only for verified survivors or demonstrated no-coverage
outcomes. Cover distinct gaps before variants of covered behavior. Map every
survivor to an added test and every added test to evidence. Preserve production
and existing tests when requested, then re-apply each mutation to prove the new
test kills it before restoring the source and running cleanly.

When the repository supplies a mutation-verification script, use it as the final
check from its expected working directory. Do not substitute a manual harness
or claim success if the canonical command errors.

## Output contract

Scale the response to the request.

For focused or small analysis, return:

1. A one-line verdict: **Strong**, **Mixed**, or **Weak**, with the reason.
2. One compact row per actionable **Survived**, **Candidate survivor
   (unverified)**, or **No coverage** outcome. Before adding a row, apply the
   outcome allowlist when the request names a risk, then apply the
   observable-candidate rules; omit any candidate that fails either filter.
   Include every high-risk outcome, use one row per distinct public outcome, and
   consolidate only related low-risk variants:

   | Risk | Public outcome | Change | Result/evidence | Smallest test |
   |---|---|---|---|---|

3. One short strengths sentence naming important killed behavior.
4. When the request names exclusions, one short scope sentence naming the
   generated, trivial, or unrelated code intentionally skipped.

Do not repeat the table in prose or report discarded mutants, tool chronology,
or in-flight reasoning.

For an exhaustive audit, add counts for Killed / Survived / No coverage /
Equivalent and group findings by risk. Count only executed or definitively
classified candidates.

For test additions, name the tests added, the verified mutations they kill, and
the successful final command.

## Reliability rules

- A passing test that does not assert the changed outcome does not kill a
  mutation.
- Coverage is per behavior partition. One switch/ternary arm or compound input
  does not prove siblings: allow does not prove deny; read does not prove write;
  null does not prove empty or whitespace when those inputs have different
  caller-visible outcomes. A kill clears only the edit and path that ran.
- Private helpers reached through a public method remain in scope.
- Error semantics are language-specific: in Rust, `?` propagation versus panic
  is observable behavior; in C#, exception type and whether an input guard
  accepts or rejects a value are observable behavior.
- Before publishing an exact amount or boundary result, derive it through the
  complete call chain and cross-check it against the unmodified implementation
  or an existing exact assertion. If it cannot be checked, state the behavioral
  relation without inventing a number.
- Calibrate the verdict to breadth and contract impact. A broadly protected
  suite with one narrow survivor is still **Strong**; do not label a finding
  high-risk or a suite **Mixed** merely because a mutation survived.
- When core state changes and primary boundaries are asserted, uncovered
  symmetric guard variants with the same return/exception contract are minor
  improvements: consolidate them and keep a **Strong** verdict unless validation
  behavior was the named risk.
- Never recommend a redundant test for behavior the existing suite already
  protects.

## Validation

- [ ] Scope stayed proportional to the request
- [ ] The original suite passed, or static-only limits are explicit
- [ ] Every high-risk public outcome in scope was inventoried
- [ ] Original and mutant have different caller-visible observations
- [ ] Every outcome labeled **Survived** was executed; unexecuted candidates use
      **Candidate survivor (unverified)**
- [ ] Every temporary mutation was reverted
- [ ] Findings exclude trivial, generated, and equivalent changes
- [ ] Recommendations target only demonstrated gaps
