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
| One component or named risk | Inventory and report every high-risk public outcome in scope; execute only the smallest decisive set, normally 1-2 candidates |
| General small-component review | Inventory distinct outcomes; report only caller-visible gaps |
| Explicit survivor verification | Execute each candidate called **Survived** or **Killed** in the final answer |
| Explicit exhaustive audit | Read [references/mutation-catalog.md](references/mutation-catalog.md) and classify all meaningful candidates |
| Add tests to an existing suite | Analyze first; add tests only for verified survivors or demonstrated no-coverage outcomes |
| Create a new suite | Stop and use `code-testing-agent` |

Do not expand a focused request into a repository audit, plan artifact, or
dashboard. Use source and tests directly for familiar frameworks. Invoke
`test-analysis-extensions` only when discovery or assertion semantics are
unclear.

### 2. Establish one baseline

Run the narrowest existing test command once. Confirm tests executed; exit 0
with build-only output is not green. Microsoft.Testing.Platform executables may
require `dotnet run`. If the suite cannot run, continue statically and label all
candidates **unverified**.

### 3. Inventory public outcomes before mutations

For each public entry point, map:

- input partitions: classifier arms, compound conditions, invalid and
  nearest-valid guard boundaries, and default cases;
- each independent observation: returned field/variant, exception
  type/parameter, public state transition, or external side effect;
- private-helper composition, constants/rates, rounding, retries, cancellation,
  and error propagation as observed through the public caller.

Use `public input/sequence -> expected outcome -> existing assertion -> gap`.
One asserted return field does not cover another. One allowed result does not
cover its denial.

**Authorization:** enumerate each relevant identity/role, resource class, and
action from the caller's view. Untested `false`, forbidden, and unchanged-role
outcomes are first-class security gaps. Do not analyze variants of an allowed
path while a denial outcome remains uninventoried.

### 4. Admit only observable candidates

Before execution, state `public input/sequence -> original observation -> mutant
observation`. Admit the candidate only when the last two differ under the
current public contract after tracing the full call chain.

Exclude:

- a removed guard or short circuit that falls through to the same result,
  exception, state, and side effects;
- private representation changes that every public input sequence observes
  identically, even if the suite stays green;
- a mutation whose proposed test passes against both original and mutant;
- hypothetical future impact, generated/trivial code, logging/formatting-only
  changes, impossible values, and duplicate syntax variants.

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

Without an explicit verification request, execute only the top 1-2 candidates
needed to settle the verdict. Do not mutate to confirm obvious no coverage.
This cap limits execution only: keep every remaining high-risk outcome,
including each denial, visible as an unverified candidate or no coverage.

### 6. Verify without creating false positives

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

## Output contract

Scale the response to the request.

For focused or small analysis, return:

1. A one-line verdict: **Strong**, **Mixed**, or **Weak**, with the reason.
2. One compact row per actionable **Survived**, **Candidate survivor
   (unverified)**, or **No coverage** outcome. Include every high-risk outcome;
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
  is observable behavior; in C#, exception type and parameter guards are
  observable behavior.
- Derive exact values through the complete call chain and probe the unmodified
  implementation when practical. Never invent an expectation or generalize one
  mutation into unexecuted claims.
- Lead with strengths when substantive mutations are killed. One minor survivor
  does not make a suite weak.
- Never recommend a redundant test for behavior the existing suite already
  protects.

## Validation

- [ ] Scope stayed proportional to the request
- [ ] The original suite passed, or static-only limits are explicit
- [ ] Every high-risk public outcome in scope was inventoried
- [ ] Original and mutant have different caller-visible observations
- [ ] Every reported survivor was executed when tooling was available
- [ ] Every temporary mutation was reverted
- [ ] Findings exclude trivial, generated, and equivalent changes
- [ ] Recommendations target only demonstrated gaps
