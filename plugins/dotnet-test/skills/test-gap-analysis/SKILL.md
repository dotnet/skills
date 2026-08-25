---
name: test-gap-analysis
description: >-
  Find changes existing tests miss. EXISTING SUITES ONLY. ALWAYS USE FOR: "are
  these tests strong enough?", "what changes would still pass?",
  boundary/guard/logic/error-propagation gaps, or survived/pseudo-mutation
  analysis. C#/Rust; others on demand. NEVER USE for new suites
  (code-testing-agent), assertion/smell/coverage audits, or mutation tools.
license: MIT
---

# Test Gap Analysis

Answer one question: **which meaningful production-code changes would the
existing tests fail to detect?** Use mutation reasoning to find candidates, then
verify only the gaps you intend to report.

## Scope before work

1. Discover production and test files from manifests and file types, not only
   product wording. After a narrow search misses, inspect the current directory
   broadly before asking for paths.
2. Classify the request:

   | Request | Analysis |
   |---|---|
   | One function, class, file, or named risk | **Focused**: inventory every meaningful behavior in that scope, then execute only the 3-5 highest-risk candidate gaps |
   | General "are these tests strong?" for a small component | **Focused**: cover each distinct boundary, guard, error, and calculation behavior without multiplying syntax variants |
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

- Before selecting mutations, map each public method's switch/condition arms,
  compound-input partitions, guard boundaries (invalid and nearest valid),
  errors, constants/rates, rounding, and composition steps to assertions,
  including private-helper behavior.
- The 3-5 budget limits execution, not discovery. Keep every distinct
  unasserted behavior in the inventory.
- Run the narrowest existing test command once. Require evidence that tests were
  executed; exit 0 with only build output is not green. Inspect the project:
  executable Microsoft.Testing.Platform tests may require `dotnet run`.
- If the suite cannot run, continue with static reasoning but label every
  proposed survivor **unverified**. Never claim empirical verification after a
  failed restore, build, or test run.

### 2. Choose risk-ranked candidates

Prioritize changes that could alter user-visible, financial, security, or error
behavior:

| Category | C# example | Rust example |
|---|---|---|
| Boundary | `>=` to `>` | `<=` to `<` |
| Logic | `&&` to &#124;&#124;, remove a condition | flip/remove a boolean condition |
| Guard/error | remove `ArgumentNullException` guard | `?` to `unwrap()`/`expect()`, change `Err` to `Ok` |
| Arithmetic/return | `+` to `-`, wrong default | arithmetic flip, `Some`/`None` or `Ok`/`Err` swap |

Skip generated files, auto-properties, trivial forwarding code, logging-only
changes, and equivalent mutations. Prefer one candidate per distinct behavior
over many syntactic variants of the same gap.

Rank candidates in this order:

1. Entirely unasserted public behavior or production branches.
2. Security, financial, state-transition, and error-propagation behavior.
3. Boundaries and exact outputs reached by weak assertions.
4. Alternate operators, constants, rounding modes, and similar variants of
   behavior that already has a meaningful assertion.

Do not spend the focused execution budget on multiple variants of a covered
branch while a separate production branch has no relevant assertion.
If more than five high-risk behaviors are unasserted, execute the top 3-5 and
keep the rest visible: use **No coverage** only when no test reaches the behavior;
otherwise report **Candidate survivor (unverified)**, never **Survived**.

### 3. Determine whether each candidate is already killed

For each candidate:

1. Find the test that reaches the changed behavior.
2. Check whether an assertion observes the changed result, exception, state, or
   error variant.
3. Classify it:

   | Result | Meaning |
   |---|---|
   | **Likely killed** | An existing assertion should fail; verify if execution is available |
   | **Candidate survivor** | Covering tests appear not to detect the change; execute it before reporting **Survived** |
   | **No coverage** | No test reaches the behavior |
   | **Equivalent** | The change cannot alter behavior; omit it from findings |

Keep a candidate only when a public observation differs between the original
and mutation. If no public assertion can distinguish them, classify it
**Equivalent**; do not recommend an internal-state test solely to expose it.

### 4. Verify reportable survivors

If execution is available, a static candidate is not yet a verified survivor:

1. Apply one candidate mutation.
2. Inspect the diff and confirm exactly one intended expression changed.
   A no-op replacement or multi-site edit is not evidence. For value swaps, use
   a temporary sentinel or replace the complete expression; sequential
   replacements can accidentally rewrite the first replacement.
3. Run the narrowest covering test.
4. Still green means **Survived**; red means **Killed** for that exact edit only.
5. Revert the mutation immediately.
6. Confirm the original source and test are green before moving on.

Never leave a mutation in the workspace. When a user explicitly asks to
"verify", every survivor and every killed claim used in the verdict or strengths
must have run evidence. Otherwise label the claim static or return a smaller
answer.

Before reporting, reconcile results with the behavior inventory. Every
unasserted high-risk path must be **Survived**, **Candidate survivor
(unverified)**, **No coverage**, or **Equivalent**; an executed kill on one path
cannot silently clear an untested sibling.

**Stop conditions:**

- Drop a candidate as soon as an existing assertion clearly kills it.
- If representative high-risk candidates are killed and no credible survivor
  remains, conclude that the suite is strong; do not search for trivial gaps to
  fill a report.
- Do not mutate every operator merely to calculate a score. Report a mutation
  score only after an explicit exhaustive audit.

### 5. Close gaps only when requested

1. Add focused tests only for verified **Survived** or **No coverage** behavior.
2. Cover every distinct verified gap in the requested scope before adding tests
   for alternate variants of an already-covered behavior.
3. Preserve production code and existing tests when requested.
4. Prefer one behavior-focused test that kills related mutations over one test
   per syntax change.
5. Re-apply the original mutation and prove the new test kills it, then restore
   the source and run the narrow suite cleanly.

## Output contract

Scale the response to the request.

For focused or small analysis, return:

1. A one-line verdict: **Strong**, **Mixed**, or **Weak**, with the reason.
2. A compact findings table containing one row per distinct actionable
   **Survived**, **Candidate survivor (unverified)**, or **No coverage** behavior.
   Consolidate related low-risk variants instead of silently dropping a
   separate high-risk behavior:

   | Risk | Location | Category/change | Result/evidence | Smallest test |
   |---|---|---|---|---|

3. One short strengths sentence naming important killed behavior.
4. When the request names exclusions, one short scope sentence naming the
   generated, trivial, or unrelated code intentionally skipped.

For an exhaustive audit, add counts for Killed / Survived / No coverage /
Equivalent and group findings by risk. Do not publish in-flight reasoning or a
score based on candidates you did not execute.

For test additions, name the tests added, the verified mutations they kill, and
the successful final command.

## Reliability rules

- A passing test that does not assert the changed outcome does not kill a
  mutation.
- Coverage is per behavior partition. One switch/ternary arm or compound input
  does not prove siblings: read does not prove write; null does not prove empty
  or whitespace. A kill clears only the edit and path that ran.
- Private helpers reached through a public method remain in scope.
- Error semantics are language-specific: in Rust, `?` propagation versus panic
  is observable behavior; in C#, exception type and parameter guards are
  observable behavior.
- Derive recommended exact values through the complete production call chain
  and probe the unmodified implementation when practical. For calculated
  outputs, cover boundaries, rates/constants, rounding, and operation order;
  isolate each stage plus one composition case. Never invent a numeric
  expectation or generalize one mutation into unexecuted claims.
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
