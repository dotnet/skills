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

Produce a formal test-code audit whose findings use the academic taxonomy, cite
specific evidence, distinguish harmful patterns from framework idioms, and
recommend fixes in the codebase's own language and framework.

## Scope

- Audit only the test files or project the caller identified. Discover files
  when a directory is provided; do not require the caller to enumerate them.
- Read production code only when needed to determine whether a value, resource,
  or sequence has an intentional meaning.
- For framework markers or APIs that are not obvious, call
  `test-analysis-extensions` and read the matching language extension.
- Read [the complete catalog](references/test-smell-catalog.md) when the caller
  requests all 19 smells, asks for citations, or the code may contain a smell
  outside the high-signal set below. Do not load it for a narrow question that
  this file answers.

## Audit Workflow

1. Detect the language, framework, test boundaries, and integration markers.
2. Read the requested test code. Read production code only for context that
   changes a verdict.
3. Identify candidates, then apply the calibration rules before reporting any.
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
| Shared setup creates expensive or irrelevant state for most tests | General Fixture | Move setup to the tests or narrower fixture that needs it | Flag cheap shared setup solely because one member is unused |
| Test is disabled or skipped | Ignored Test | Distinguish a tracked, reasoned skip from an unexplained one | Give both the same urgency |

## Calibration Rules

Apply these before assigning a finding:

- Mock-call verifications, snapshots, bare pytest `assert`, Pester
  `Should -Invoke`, and expected-exception constructs are assertions.
- Go table-driven subtests, pytest/JUnit/xUnit parameterization, Jest/Vitest
  `.each`, RSpec data tables, Pester `-ForEach`, and Catch2
  `SECTION`/`GENERATE` are not Conditional Test Logic by themselves.
- Go's `if err != nil { t.Fatal(...) }` is idiomatic assertion flow, not
  Exception Handling.
- Integration markers legitimize declared external resources and multi-step
  flows, but not fixed sleeps or assertion-free execution.
- A local temporary file still meets the formal Mystery Guest definition.
  Hermetic creation and cleanup reduce its severity; they do not change its
  taxonomy.
- Do not infer a smell from method names alone. Point to the statement or
  fixture relationship that proves it.
- If no material smell remains after calibration, say that clearly. Never
  manufacture findings to fill a report.

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

- For one to three files, start with a one-line verdict and use one compact
  findings table: severity, formal smell, location/evidence, risk, and fix.
- For a larger suite, add aggregate counts and a short prioritized remediation
  sequence. Do not repeat every finding in a dashboard, prose section, and
  plan.
- Show replacement code only where it clarifies the fix; keep unchanged setup
  out of snippets.
- End with a brief **Not findings** note only when an idiom was plausibly
  suspicious and deliberately cleared.

Every reported smell must have a formal taxonomy name, precise location,
evidence from the code, practical risk, and a concrete framework-correct fix.

## Validation

- Every finding is supported by code, not a keyword or method name.
- Unknown Test and Empty Test remain distinct.
- Framework idioms and integration boundaries were calibrated before reporting.
- Clean tests and suspicious-but-valid idioms are not turned into filler.
- Fixes use the target framework's APIs and preserve the behavior under test.
- Claims about files reviewed, builds, or test runs match actions actually
  performed.
