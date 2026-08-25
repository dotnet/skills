---
name: test-gap-analysis
description: >-
  Verify whether tests catch meaningful code changes and close only
  proven gaps. ALWAYS USE FOR: "are these tests strong enough?", "what changes
  would still pass?", boundary/guard/logic/error-propagation bugs, missing edge
  cases in an existing suite, survived mutations, or pseudo-mutation analysis.
  Polyglot: C# and Rust. DO NOT USE FOR: a new suite (code-testing-agent),
  assertion/smell/coverage audits, or mutation tools.
license: MIT
---

# Test Gap Analysis

Answer one question: **which meaningful production-code changes would the
existing tests fail to detect?** Use mutation reasoning to find candidates, then
verify only the gaps you intend to report.

## Scope before work

1. Discover the relevant production and test files; do not ask for paths that
   are available in the workspace.
2. Classify the request:

   | Request | Path |
   |---|---|
   | One function, class, file, or named risk | **Focused**: inspect that scope and test at most 3-5 highest-risk candidates |
   | General "are these tests strong?" for a small component | **Focused**: sample the important boundaries, guards, errors, and calculations; stop once the verdict is supported |
   | Explicit exhaustive audit | **Broad**: classify all meaningful mutation points; read [references/mutation-catalog.md](references/mutation-catalog.md) |
   | Add or fix tests in an existing suite | Analyze first, then add tests only for verified survivors |
   | Create a suite where none exists | Stop and use `code-testing-agent` |

Do not turn a focused question into a repository-wide audit. Do not create plan
artifacts or a dashboard for a small suite.

### Language lookup is conditional

Use the source and tests directly for familiar frameworks. Invoke
`test-analysis-extensions` only when framework-specific discovery or assertion
semantics are unclear. Routine C#/xUnit/MSTest/NUnit and Rust `#[test]` analysis
does not require loading an extension.

## Workflow

### 1. Establish the baseline once

- Read production and test files together and map public behavior to covering
  tests, including calls through private helpers.
- Run the narrowest existing test command once. Record whether it is green.
- If the suite cannot run, continue with static reasoning but label every
  proposed survivor **unverified**. Never claim empirical verification after a
  failed restore, build, or test run.

### 2. Choose risk-ranked candidates

Prioritize changes that could alter user-visible, financial, security, or error
behavior:

| Category | C# example | Rust example |
|---|---|---|
| Boundary | `>=` to `>` | `<=` to `<` |
| Logic | `&&` to `||`, remove a condition | flip/remove a boolean condition |
| Guard/error | remove `ArgumentNullException` guard | `?` to `unwrap()`/`expect()`, change `Err` to `Ok` |
| Arithmetic/return | `+` to `-`, wrong default | arithmetic flip, `Some`/`None` or `Ok`/`Err` swap |

Skip generated files, auto-properties, trivial forwarding code, logging-only
changes, and equivalent mutations. Prefer one candidate per distinct behavior
over many syntactic variants of the same gap.

### 3. Determine whether each candidate is already killed

For each candidate:

1. Find the test that reaches the changed behavior.
2. Check whether an assertion observes the changed result, exception, state, or
   error variant.
3. Classify it:

   | Result | Meaning |
   |---|---|
   | **Killed** | An existing test would fail |
   | **Survived** | Covering tests still pass |
   | **No coverage** | No test reaches the behavior |
   | **Equivalent** | The change cannot alter behavior; omit it from findings |

### 4. Verify reportable survivors

If execution is available, a static candidate is not yet a finding:

1. Apply one candidate mutation.
2. Run the narrowest covering test.
3. Still green means **Survived**; red means **Killed**.
4. Revert the mutation immediately.
5. Confirm the original test is green before moving on.

Never leave a mutation in the workspace. When a user explicitly asks to
"verify", every reported survivor must have run evidence. Otherwise, unavailable
tooling is an acceptable reason to return a smaller, clearly static answer.

**Stop conditions:**

- Drop a candidate as soon as an existing assertion clearly kills it.
- If representative high-risk candidates are killed and no credible survivor
  remains, conclude that the suite is strong; do not search for trivial gaps to
  fill a report.
- Do not mutate every operator merely to calculate a score. Report a mutation
  score only after an explicit exhaustive audit.

### 5. Close gaps only when requested

1. Add focused tests only for verified **Survived** or **No coverage** behavior.
2. Preserve production code and existing tests when requested.
3. Prefer one behavior-focused test that kills related mutations over one test
   per syntax change.
4. Re-apply the original mutation and prove the new test kills it, then restore
   the source and run the narrow suite cleanly.

## Output contract

Scale the response to the request.

For focused or small analysis, return:

1. A one-line verdict: **Strong**, **Mixed**, or **Weak**, with the reason.
2. A compact findings table containing only actionable survivors/no-coverage
   behavior (at most five unless the user asks for more):

   | Risk | Location | Category/change | Result/evidence | Smallest test |
   |---|---|---|---|---|

3. One short strengths sentence naming important killed behavior.

For an exhaustive audit, add counts for Killed / Survived / No coverage /
Equivalent and group findings by risk. Do not publish in-flight reasoning or a
score based on candidates you did not execute.

For test additions, name the tests added, the verified mutations they kill, and
the successful final command.

## Reliability rules

- A passing test that does not assert the changed outcome does not kill a
  mutation.
- Private helpers reached through a public method remain in scope.
- Error semantics are language-specific: in Rust, `?` propagation versus panic
  is observable behavior; in C#, exception type and parameter guards are
  observable behavior.
- Lead with strengths when substantive mutations are killed. One minor survivor
  does not make a suite weak.
- Never recommend a redundant test for behavior the existing suite already
  protects.

## Validation

- [ ] Scope stayed proportional to the request
- [ ] The original suite passed, or static-only limits are explicit
- [ ] Every reported survivor was executed when tooling was available
- [ ] Every temporary mutation was reverted
- [ ] Findings exclude trivial, generated, and equivalent changes
- [ ] Recommendations target only demonstrated gaps
