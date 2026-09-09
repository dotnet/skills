---
name: test-smell-detection
description: >
  Audits existing tests in any language using formal, research-backed test
  smell names and the testsmells.org 19-smell academic taxonomy. Use when the
  caller asks for an academic or citable test-smell review, named smell
  categories, or a formal severity-ranked smell assessment. Covers Assertion
  Roulette, Conditional Test Logic, Mystery Guest, Eager Test, Sleepy Test,
  Unknown Test, Sensitive Equality, and the rest of the catalog across .NET,
  Python, JavaScript/TypeScript, Java, Go, Ruby, Rust, Swift, Kotlin,
  PowerShell, and C++. DO NOT USE FOR a quick pragmatic test review (use
  test-anti-patterns), writing or running tests, framework migration, coverage,
  or assertion-diversity metrics.
license: MIT
---

# Test Smell Detection

Audit test code with the academic taxonomy, code evidence, calibrated
framework idioms, and fixes native to the codebase.

## Scope

- Audit only staged or named tests. Search the current workspace before asking
  for code; never claim a file is missing until that search finds no relevant
  test.
- Read production code only when it changes a verdict.
- For unfamiliar framework APIs, call `test-analysis-extensions` and read the
  matching language extension.
- Read [the complete catalog](references/test-smell-catalog.md) when the caller
  requests all 19 smells, asks for citations, or the code may contain a smell
  outside the high-signal set below. Do not load it for a narrow question that
  this file answers.

## Audit Workflow

1. Search for and read the staged tests; detect language, framework, boundaries,
   and integration markers. This is the first action even when no path is named.
2. Read the tests and only verdict-changing production context.
3. For each candidate, verify the executed check, choose the formal category,
   calibrate, then assign severity. Report proven non-catalog test-validity
   defects separately; do not relabel them as smells.
4. Rank confirmed findings by risk of false confidence or flakiness, then by
   maintenance cost.
5. Give a framework-correct replacement for each actionable finding. Never use
   .NET terminology or APIs in another ecosystem.

## High-Signal Decisions

| Evidence | Academic finding | Do | Never |
|---|---|---|---|
| Assertion behavior changes behind `if`, `switch`, or branching loops | Conditional Test Logic | Split cases or parameterize them | Flag table-driven or parametrized tests merely because a runner loop exists |
| A test relies on an undeclared file, network service, environment value, or database | Mystery Guest or Resource Optimism | Make the dependency explicit and hermetic; distinguish the two using the full catalog | Condemn an integration test merely for exercising its declared real resource |
| Fixed wall-clock sleep waits for an outcome | Sleepy Test | Await or poll the condition with a timeout | Downgrade it only because the test is an integration test |
| Executable test has no assertion, expected-exception marker, or mock verification | Unknown Test | Assert the observable outcome | Call an empty body Unknown Test; the formal name is Empty Test |
| Async assertion/coroutine is created but not awaited or returned | Critical non-catalog false-pass defect | Report it separately and show the required `await`/`return` | Force it into Unknown Test; the assertion statement exists |
| One test exercises many unrelated production behaviors | Eager Test | Separate behavior-focused tests | Flag a deliberate end-to-end workflow without considering its scope |
| Expected numeric literal has no local meaning | Magic Number Test | Name the domain value or derive it from setup | Flag `count == 3` immediately after adding three items |
| Assertion depends on `ToString`, `repr`, `description`, or display formatting that is not the contract | Sensitive Equality | Assert stable fields or use a structural matcher | Flag a test whose explicit contract is the formatted string |
| Test manually manages expected exception flow | Exception Handling | Use the framework's exception assertion and check meaningful details | Claim a capture-and-assert test verifies nothing |
| Shared setup creates state irrelevant to the tests that receive it | General Fixture | Remove unused state or narrow the fixture; rank cheap state low | Condemn relevant shared setup merely because it is shared |
| Test is disabled or skipped | Ignored Test | Report every skip, but rank a tracked, reasoned skip below an unexplained one | Clear a skip because its reason is good, or give both the same urgency |

## Calibration Rules

Apply these before assigning a finding:

- Mock-call verifications, snapshots, bare pytest `assert`, Pester
  `Should -Invoke`, and expected-exception constructs are assertions.
- A literal or snapshot assertion may expose a coverage gap, but is not Unknown
  Test or another smell without separate evidence.
- Count assertion statements. One assertion is never Assertion Roulette;
  missing messages alone are not a smell.
- Same-method tests are not Lazy Test when they cover distinct behaviors,
  boundaries, or state; require redundant equivalent paths.
- General Fixture requires shared lifecycle state. Repeated local construction
  is neither General Fixture nor Test Code Duplication by itself.
- Treat strings returned by the public API as observable contract unless
  production context or requirements make them display-only; interpolation
  alone is not Sensitive Equality.
- Magic Number Test requires an unexplained oracle value. Do not flag ordinary
  setup quantities whose role is locally obvious and irrelevant to the asserted
  behavior.
- Go table-driven subtests, pytest/JUnit/xUnit parameterization, Jest/Vitest
  `.each`, RSpec data tables, Pester `-ForEach`, and Catch2
  `SECTION`/`GENERATE` are not Conditional Test Logic by themselves.
- Go's `if err != nil { t.Fatal(...) }` is idiomatic assertion flow, not
  Exception Handling.
- Integration markers legitimize declared external resources and multi-step
  flows, but not fixed sleeps or assertion-free execution.
- For integration tests, trace helper factories and repository return types
  before judging branch coverage. If a test branches on a subtype or state that
  the real helper can never produce, report the unreachable assertion path and
  explain the resulting false confidence. Do not stop at assigning the generic
  Conditional Test Logic label.
- A local temporary file still meets the formal Mystery Guest definition.
  Hermetic creation and cleanup reduce its severity; they do not change its
  taxonomy.
- A formatting name does not prove display text is the stable contract; confirm
  it from production behavior or requirements before clearing Sensitive
  Equality.
- Do not infer a smell from method names alone. Point to the statement or
  fixture relationship that proves it.
- Do not infer a high-severity non-catalog validity defect from a test name
  alone. Without production code or an explicit contract proving that the test
  is supposed to invoke another component, a name/body mismatch is at most an
  unranked observation, not evidence that the test silently passes broken
  production behavior.
- Catch2 `SECTION` and `GENERATE` are runner-controlled case expansion, and
  `REQUIRE` is a real assertion. When those are the only suspicious constructs,
  the academic-smell verdict is **clean**. Do not reverse that verdict because
  the test could have broader behavioral coverage.
- If no material smell remains after calibration, say that clearly. Never
  manufacture findings to fill a report.
- Never propose `await` for a void or otherwise non-awaitable API. If production
  work is synchronous, remove the sleep and assert immediately.

## Severity

Severity follows demonstrated risk, not a fixed label copied from the catalog:

- **High:** can silently pass while behavior is broken, creates
  nondeterministic failures, or hides unexecuted assertion paths.
- **Medium:** makes failures ambiguous or couples tests to unstable details.
- **Low:** primarily maintenance debt, such as a reasoned skip or over-broad
  cheap fixture.

State the reason for the assigned severity. Downgrade or omit a finding when
the surrounding test type makes the pattern intentional.

## Output Contract

Scale the response to the input:

- For one to three files, give a verdict and one compact table: severity,
  formal smell, evidence, risk, and fix.
- For larger suites, add counts and a short priority order. Do not repeat
  findings across dashboards, prose, and plans.
- Show code only when it clarifies a fix; omit unchanged setup.
- Add brief **Not findings** only for plausibly suspicious idioms.
- Put proven non-catalog validity defects after the academic-smell verdict. On a
  clean input with no production contract, do not assign severity to optional
  coverage observations or let them overturn the clean verdict.
- Do not narrate discovery or catalog loading; return the audit directly.

Every reported smell must have a formal taxonomy name, precise location,
evidence from the code, practical risk, and a concrete framework-correct fix.

## Validation

- Every finding is supported by code, not a keyword or method name.
- Unknown Test and Empty Test remain distinct.
- Every disabled test remains Ignored Test, and every local file dependency
  remains Mystery Guest; rationale and hermetic cleanup change severity only.
- Framework idioms and integration boundaries were calibrated before reporting.
- Clean tests and suspicious-but-valid idioms are not turned into filler.
- Clean framework idioms are not converted into high-severity non-catalog
  findings from naming or absent production context.
- Fixes use the target framework's APIs and preserve the behavior under test.
- Claims about files reviewed, builds, or test runs match actions actually
  performed.
