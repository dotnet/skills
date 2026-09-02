---
name: test-gap-analysis
description: >-
  Pseudo-mutation analysis ONLY: find caller-visible production-code changes
  that existing assertions would not catch, then optionally close verified
  gaps. Activate only when the request asks whether a bug/change/mutation could
  survive, names behavioral blind spots, or asks for missing edge cases tied to
  production behavior. Polyglot. DO NOT USE FOR: suite organization, taxonomy,
  metadata, or distribution reports (test-tagging); .NET line-vs-branch or
  Cobertura interpretation, arithmetic, plateaus, project-wide coverage gaps,
  or coverage-backed test/CRAP priorities (coverage-analysis; use native
  coverage tooling outside .NET); named-target CRAP (crap-score); new suites
  (code-testing-agent); assertion/smell audits; or mutation tools.
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

Run the narrowest existing test command once. Choose it from the project
manifest; Microsoft.Testing.Platform executables may require `dotnet run`.
Confirm tests executed: exit 0 with build-only output is not green. If that one
attempt cannot run the suite, do not troubleshoot the runner or try alternate
commands for an advisory review; continue statically and label all candidates
**unverified**. Do not infer a project-configuration cause from missing output;
name a cause only when the command reports it.

For an advisory review such as "would tests catch this?", stop execution after
that baseline. Source-to-assertion mapping is sufficient evidence for **No
coverage** and **Candidate survivor (unverified)**. Trace or run the unmodified
code once only when an original value is unclear. Apply mutations only for
explicit verification, an exhaustive audit, or closing gaps with tests.

Any focused mutation budget limits execution, not discovery. Keep every
distinct unasserted public outcome in the inventory.

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
does not pin any exact amount. For each actionable money row, derive one witness
input and its exact original result through the complete call chain; do not
recommend a generic "assert the exact amount" without supplying that amount.

**Ordered guards and retries:** inventory `invalid below minimum | first valid |
last allowed or retryable | first blocked | later blocked`. For an upper guard
such as `value >= limit`, use `limit - 1`, `limit`, and `limit + 1`; the last
witness exposes narrowing to `value == limit`. Inventory every accepted and
rejected error class. When type matching is polymorphic, include a representative
derived accepted type that would expose exact-runtime-type narrowing. A test at
the first blocked value does not protect the last allowed or later blocked value.

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

Reserve execution for wholly untested public branches before another variant
of a partially covered helper. If more than five high-risk behaviors are
unasserted, execute the top 3-5 and keep the rest visible as **No coverage** or
**Candidate survivor (unverified)**.

Execution never replaces the ledger. Before mutating or answering, classify
every required outcome, including each invalid input, guard boundary,
classifier arm, action, and denial.

### 4. Admit only observable candidates

First replay each exact mutation against every existing asserted input or
sequence with all arguments fixed. Any changed return, exception, state, or side
effect is **Likely killed**; a dedicated single-purpose test is unnecessary.
Never compare the mutant on one input with the original on another.

For survivors, choose a witness before execution or reporting and state
`witness -> original observation -> mutant observation`. Reuse it in the
smallest test. Admit it only when the last two differ publicly after tracing the
full call chain; otherwise choose a distinguishing witness or drop it.

Exclude:

- edits that require inserting or reordering statements rather than changing or
  removing an existing expression, condition, constant, return, or side effect;
- edits that do not compile, including removal of a declaration whose value is
  still referenced;
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

Missing direct assertions do not prove **No coverage**: first trace existing
assertions through public callers and shared branches. Missing assertions make
an **observable** candidate a survivor; they do not make an inert mutation
meaningful.

### 5. Rank and classify

Rank: (1) security denials, financial outcomes, errors, and state changes;
(2) wholly unasserted public outcomes; (3) boundaries or exact values reached by
weak assertions; (4) alternate variants of already-asserted behavior.

Finish the inventory before selecting mutations or a verdict. One killed
attempt, exception type, or switch arm does not clear its siblings.

Choose the verdict from the completed inventory:

- **Strong** when core branches and primary boundaries are protected and only a
  few validation or default-case variants remain;
- **Mixed** when meaningful coverage exists but at least one important outcome
  partition is unprotected;
- **Weak** when important outcomes are broadly unprotected.

A handful of validation gaps does not make an otherwise broad suite **Mixed**
unless validation is the named risk or the gaps threaten security, data, or
other contract-critical behavior.

Stop when existing assertions kill the remaining candidates or no credible
public survivor remains. Do not mutate every operator merely to fill a report or
calculate a score.

| Result | Meaning |
|---|---|
| **Likely killed** | An existing assertion observes the changed outcome |
| **Candidate survivor (unverified)** | Observable change appears unasserted; not executed |
| **Survived** | Exact observable mutation executed and tests stayed green |
| **No coverage** | No test reaches the public outcome; report the missing branch without inventing a survivor |
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

1. Add focused tests only for executed **Survived** mutations or demonstrated
   **No coverage** behavior.
2. Cover every distinct gap in the requested scope before adding tests
   for alternate variants of an already-covered behavior.
3. Before editing, create a survivor-to-test checklist. Before stopping, map
   every verified survivor to an added test and every added test back to a
   verified survivor; a passing final suite alone does not prove completeness.
4. Preserve production code and existing tests when requested.
5. Prefer one behavior-focused test that kills related mutations over one test
   per syntax change.
6. Re-apply the original mutation and prove the new test kills it, then restore
   the source and run the narrow suite cleanly.
7. If the fixture or repository supplies a canonical mutation verifier, run
   that exact command after the tests are added and cite its successful result.
   Hand-created substitute mutations, a broad green suite, or a test-count
   increase do not replace the supplied oracle. Once every requested survivor
   maps to a focused test and the canonical verifier passes, stop; extra tests
   are not an advantage.

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
- Cross-check every exact amount or boundary result against the unmodified
  implementation or an existing exact assertion. If it cannot be checked,
  state the behavioral relation without inventing a number.
- Do not label a finding high-risk merely because a mutation survived.
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
- [ ] Every public entry-point branch and each accepted exception type in scope
      is explicitly accounted for
- [ ] A supplied canonical mutation verifier was run and reported, not replaced
      with an ad-hoc proxy
