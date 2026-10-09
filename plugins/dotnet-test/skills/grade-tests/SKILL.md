---
name: grade-tests
description: >
  Grade a curated list of individual tests for readiness, A-F quality, and
  concrete improvements. ALWAYS USE FOR: grade tests, review only a named test,
  per-test readiness decisions, or quality bands for supplied methods, bodies,
  file spans, or bounded PR diffs, including existing tests. Produce a PR-ready
  Pass, Failed, Uncertain, or Not applicable table; unresolved or empty scopes
  omit the grade. Compose read-only per-test mutation evidence when available.
  Polyglot: .NET, Python, TS/JS, Java, Go, Ruby, Rust, Swift, Kotlin,
  PowerShell, C++. DO NOT USE FOR: suite-wide audits (test-engineer or
  test-anti-patterns), writing or fixing tests, or measuring coverage.
license: MIT
---

# Grade Tests

Assess a curated list of test methods and produce a compact,
PR-comment-friendly report. The primary result is one of **Pass**, **Failed**,
**Uncertain**, or **Not applicable**; an A-F quality grade remains secondary
diagnostic information. The skill **does not discover tests on its own** — the
caller (typically a PR automation workflow or a human reviewer holding a
specific list) provides the tests or a bounded diff to assess.

After Step 0 admits a bounded scope, enforce these grading invariants:

- With production context, **load `test-gap-analysis` by name once before
  scoring**, in `per-test-read-only` caller context. Read its owned composition
  reference; do not compute mutation evidence from this grading rubric or run
  its standalone workflow. Report N/A / unverified only when the dependency,
  reference, or required context is actually unavailable.
- Any reported mutation inference must use **Likely killed (inferred)** or
  **Candidate survivor (unverified)**, even when explained in prose.
- Apply only the rubric below, not extra heuristics such as a duplicate-test
  penalty. Do not use sibling tests to alter the individual assessment.
- A **B** quality grade does not require a Failed result; a complete focused
  test may have no actionable change.

> **Language-specific guidance**: If the caller supplies the matching bundled
> extension file path, read it directly. Otherwise call `test-analysis-extensions`
> to discover available extension files, then read the file matching the
> target codebase's language and framework (e.g., `extensions/dotnet.md`,
> `extensions/python.md`, `extensions/typescript.md`, `extensions/go.md`).
> You MUST read the relevant extension file before scoring assertions or
> anti-patterns, because assertion APIs and idiomatic patterns differ
> significantly across frameworks.

## Why a Decision Result Plus Quality Detail

PR reviewers need a simple answer to *does this test need follow-up?* The
four-state result provides that decision; the existing A-F rubric explains its
quality and severity.

## When to Use

- A PR automation workflow needs to post a decision on the tests introduced or
  modified in a pull request.
- A reviewer has a specific list of tests (a file, a class, a method list,
  or a diff hunk) and wants per-test follow-up decisions rather than a suite
  report.
- A maintainer wants to triage which of N tests in a contribution deserve
  follow-up improvements, with quality grades for resolved tests.

## When Not to Use

- The caller wants a full suite audit or comparative metrics — use
  `test-anti-patterns` (pragmatic) or `test-smell-detection` (formal) and
  let the `test-engineer` agent orchestrate its internal quality specialist.
- The caller wants to *write* new tests — use `test-engineer`
  (any language) or `writing-mstest-tests` (MSTest specifically).
- The caller wants to measure code coverage or CRAP scores — use
  `coverage-analysis` or `crap-score` (.NET only).
- The caller wants to fix issues directly in test code — invoke the
  appropriate editing skill.
- No specific list of tests is provided. Do **not** try to grade every test
  in the workspace; ask the caller for an explicit list or scope.

## Inputs

| Input | Required | Description |
|-------|----------|-------------|
| Test methods | Yes | A scope to grade. Provide one of: (a) an explicit list of test method names (fully-qualified, e.g. `Namespace.ClassName.TestMethodName`); (b) one or more file paths plus an explicit instruction to grade every test declared in those files; or (c) a diff hunk / PR identifier whose changed tests should be graded. File paths are recommended but optional when method names are unambiguous in the workspace. Ambiguous requests like *"grade my tests"* with no scope are rejected up-front (see Step 0); this skill is for curated input and does not auto-grade an entire workspace. |
| Test bodies / spans | Recommended | The exact source lines for each test method. If omitted, read them from the listed files. |
| Production code | No | The code under test, for judging whether assertions cover the claimed behavior. When unavailable, mark the mutation assessment N/A / unverified rather than guessing or deducting. |
| Language reference | No | A host-supplied path to the matching bundled `test-analysis-extensions` file. Read it directly instead of invoking its reference-only loader; do not substitute unverified framework guidance. |
| Diff context | No | When grading PR changes, the unified diff for each test method helps focus on what actually changed. |

### Step 0: Validate the input

Before doing anything else, check that the caller provided one of:

1. An explicit list of test method names, **or**
2. One or more file paths plus an explicit instruction to grade every test
   declared in those files (e.g., "grade every test in `OrderTests.cs`"), **or**
3. A diff hunk or PR identifier whose changed tests should be graded.

If the request is ambiguous (e.g., *"Grade my tests"*, *"Are these tests
any good?"* with no scope, *"Review the test suite"*), **do not load
extensions, do not read files, and do not grade anything**. Reply with a
short message asking the caller to provide an explicit list / file(s) /
diff, and optionally point them at the `test-engineer` agent or
`test-anti-patterns` skill for full-suite analysis. Stop there.

If a valid bounded scope resolves to zero eligible tests, return
**Not applicable** with a short explanation and no invented rows.

## Workflow

### Step 1: Detect language and load extension

Identify the target codebase's language and test framework from the file
extensions and the test method markers in the provided list. Call the
`test-analysis-extensions` skill unless the caller already supplied the matching
bundled extension file path. In either case, read that extension file (e.g.,
`extensions/dotnet.md` for MSTest/xUnit/NUnit/TUnit, `extensions/python.md`
for pytest, `extensions/typescript.md` for Jest/Vitest, `extensions/go.md`
for the standard `testing` package). If the input contains tests from
multiple languages, load each relevant extension and grade each test using
its language's conventions.

### Step 2: Resolve the test bodies

For each entry in the input list:

1. If the test body is provided inline, use it directly.
2. Otherwise read the file at the given path and locate the method by its
   fully-qualified name. Capture the full method body, including attributes
   / decorators / fixtures and any helper code that the test calls.
3. If a requested method cannot be found, record it as
   `Uncertain — method not found` with no quality grade and continue. Never
   invent a body to grade. A missing requested method requires human review;
   it is not the same as a valid scope containing no tests.

**Composition checkpoint:** for resolved tests with available production
context, load `test-gap-analysis` now, once for the batch, with
`per-test-read-only` assessment context. Complete its owned reference assessment
before Step 3. Do not skip this load just because a body-level weakness already
seems obvious; a locally invented mutation explanation is not composition.

### Step 3: Assess the claimed behavior and score each resolved test

Keep grading read-only: no build/test runs, mutation execution, file edits,
tool installation, broad suite discovery, or agent delegation. Resolve only
the supplied tests, their relevant fixtures/helpers, and the production call
chain needed for their claims.

Use the inline `test-gap-analysis` assessment from Step 2's checkpoint;
do not load it a second time. Supply each test's
identifier/body, relevant setup/helpers, claimed behavior, assertion semantics,
and available source. Its composition dispatch loads the owned read-only
reference rather than its standalone baseline/verification workflow. Consume
its per-test evidence; do not duplicate its mutation catalog here or invoke an
audit/generation agent.
Convey mode and inputs as assessment context using the host's supported caller
instructions. If the loader accepts only a skill name, load `test-gap-analysis`
by name only; do not invent tool arguments or a mode-specific skill name.

If the skill/reference or production context is unavailable, record
`Pseudo-mutation: N/A / unverified — <reason>` and continue normal body-level
grading. This is not a grade deduction or, by itself, an Uncertain result.
Do not search installation directories or substitute a mutation runner.

Assess only what each test claims: do not borrow another test's assertions,
or demand unrelated branches, outputs, or scenarios. An observable survivor
can support an existing Assertion strength category when it proves that the
test does not verify its claimed outcome; do not introduce mutation points,
weights, ceilings, or an automatic survivor penalty. Apply the existing rubric
normally, including weaknesses it classifies in both Assertion and Anti-pattern
dimensions; do not add another deduction for the same mutation evidence.

Start every test at grade **A (score band 90–100)**, then apply deductions
strictly for **observable issues** in the captured body. Do **not** deduct
for hypothetical concerns (e.g., "could have more negative assertions")
unless the production code clearly demands them and the production code is
available.

When production code is unavailable, grade observable issues in the test body
normally, but do not infer missing behaviors or deduct for them. State
`Production-dependent behavior coverage: Unverified` once in the summary so the
reader can distinguish test-body findings from claims that require source code.

#### Three sub-dimensions

Compute three sub-grades (each A–F) that together drive the overall grade.

##### A. Assertion strength

Read the loaded language extension's assertion API list and classify every
assertion in the test body. Score from highest to lowest:

| Sub-grade | Pattern |
|-----------|---------|
| **A** | At least one meaningful value assertion (equality / structural / exception / state) plus, where appropriate, additional checks (negative, type, collection contents). Mock-call verifications (`Verify`, `toHaveBeenCalledWith`, `Should -Invoke`) and bare assertion forms (pytest `assert`, Go `if got != want { t.Errorf(...) }`, Rust `assert!()`) count as real assertions. |
| **B** | One clear meaningful assertion that verifies the behavior under test. |
| **C** | Only trivial assertions (single `IsNotNull` / `toBeDefined` / `assert x is not None`), or assertions that leave a meaningful part of the test's claimed result unchecked. A focused single-field claim does not require unrelated fields. |
| **D** | One self-referential / tautological assertion (`Assert.AreEqual(x, x)`, `assert dto.name == dto.name`, round-trip identity without a non-trivial input), or broad exception assertions (`Assert.ThrowsException<Exception>`). |
| **F** | No assertions at all; **all** assertions are always-true literals (`Assert.IsTrue(true)`, `assert True`, `expect(true).toBe(true)`) — these verify nothing and are equivalent to having no assertions; or all assertions are silently un-awaited (e.g., `expect(promise).resolves.toBe(x)` without `await`/`return`, async TUnit/xUnit `Assert.ThrowsAsync` without `await`, pytest-asyncio with un-awaited coroutine). |

Exception and error-path tests (`Assert.ThrowsException<T>`, constrained
`pytest.raises`, `expect(fn).toThrow`, `assertThrows`, `#[should_panic]`,
`Should -Throw`, `EXPECT_THROW`, or Go code that verifies an expected non-nil
error) are complete on their own. Give Assertion strength **A** when the test
checks the exact promised error condition for its stated scope. Do not deduct
for having only that assertion, and do not require an error-message assertion
unless the message is part of the documented contract. A Go happy-path test
that only checks `err == nil` while discarding a meaningful returned value is
still **C** because it does not verify the successful result.

##### B. Structure & focus

| Sub-grade | Pattern |
|-----------|---------|
| **A** | Clear Arrange-Act-Assert (or Given-When-Then) separation. Single behavior under test. Body under ~30 lines. Setup uses framework conventions. |
| **B** | One mild structural issue (slightly long body, missing blank lines between phases) but intent is clear. |
| **C** | Multiple behaviors mixed in one test, or AAA phases interleaved enough to slow comprehension. |
| **D** | Conditional logic in the test (`if`/`switch` driving assertions) — except for idiomatic Go/Rust table-driven sub-test loops; or test relies on previous test state (ordering dependency). |
| **F** | Test exceeds ~60 lines and verifies multiple unrelated behaviors; or shares mutable state with other tests through statics/globals without reset. |

##### C. Anti-pattern hygiene

Scan against the catalog below. The Anti-pattern sub-grade is computed
in two passes and combined deterministically:

1. **Hard ceiling pass.** Every **Critical** or **High** finding sets a
   maximum sub-grade (F, D, or C as labeled). Take the **worst** ceiling
   across all matched Critical/High findings — these do not accumulate
   (a single F finding caps the sub-grade at F regardless of how many
   other Critical/High findings are present).
2. **Medium-deduction pass.** Start from **A**, then for each **Medium**
   finding deduct one sub-grade level (A→B, B→C, C→D, D→F). These do
   accumulate across findings.

The final Anti-pattern sub-grade is the **worse** of the two passes
(i.e., `min(hard_ceiling, A − medium_count)`). **Low** findings never
affect the grade — mention them in the note only.

Examples (Critical/High and Medium counts → Anti-pattern sub-grade):

- Zero Critical/High, 1 Medium → **B** (A − 1)
- Zero Critical/High, 3 Medium → **D** (A − 3)
- One C-ceiling (e.g., over-mocking), 0 Medium → **C**
- One C-ceiling, 2 Medium → **C** (`min(C, A − 2 = C) = C`; a third Medium tips to **D**)
- One F-finding (e.g., swallowed exception) plus any number of Medium → **F**

**Critical (drop straight to F or D)**

- No assertions at all → F (also drives Assertion sub-grade to F)
- Swallowed exceptions: `try { … } catch { }` (.NET), bare `except: pass`
  (Python), `try { … } catch (e) {}` (JS/TS/Java), `defer recover()`
  without re-panic (Go), `rescue StandardError` with no assertion (Ruby),
  empty `catch` (Kotlin/Swift) → F
- Assert-in-catch pattern (`Assert.Fail(ex.Message)` instead of
  `Assert.ThrowsException`) → D
- Always-true literal assertions (`Assert.IsTrue(true)`, `assert True`,
  `expect(true).toBe(true)`) → **F** (verifies nothing; also drives
  Assertion sub-grade to F)
- Self-referential / tautological assertions on bound values
  (`Assert.AreEqual(x, x)`, `assert dto.name == dto.name`) → D
- Commented-out assertions → D

**High (drop one or two sub-grades)**

- Wall-clock sleep used for synchronization: `Thread.Sleep`, `Task.Delay`,
  `time.sleep`, `setTimeout`-based wait, `Thread.sleep`, `time.Sleep`,
  `sleep`, `std::thread::sleep`, `Start-Sleep`,
  `std::this_thread::sleep_for` (in a unit test) → D
- Unseeded randomness, wall-clock reads without abstraction
  (`DateTime.Now`, `datetime.now()`, `Date.now()`,
  `System.currentTimeMillis()`, `time.Now()`, `Time.now`,
  `Instant::now()`, `Get-Date`, `system_clock::now`) → D
- Hard-coded environment-dependent paths (`C:\…`, `/tmp/…`, network hosts) → D
- Ordering dependency on mutable static / package globals → D
- Broad exception assertion (`Assert.ThrowsException<Exception>`,
  `pytest.raises(Exception)`, `expect(fn).toThrow(Error)` without matcher,
  `#[should_panic]` without `expected = "…"`, `Should -Throw` without
  `-ExpectedMessage`, `EXPECT_ANY_THROW`) → C
- Over-mocking: more mock setup lines than test logic, or verifying exact
  call sequences instead of outcomes → C
- Implementation coupling: reflection on private members, casting to
  internal types to access state → C

**Medium (drop one sub-grade)**

- Poor name: `Test1`, `TestMethod`, `test`, single-word name that says
  nothing about scenario or expected outcome (judge against the language
  extension's convention) → drop one sub-grade
- Magic values: unexplained `42`, `"foo"`, `0x1234` in arrange/assert
  without naming or comment → drop one sub-grade
- Giant test (>30 lines covering a single behavior) → drop one sub-grade
- Assertion messages that just repeat the assertion text → drop one sub-grade
- Missing AAA / GWT separation when the test is non-trivial → drop one sub-grade

**Low (note only, no deduction)**

- Unused setup/teardown hooks; print debugging left in (`Console.WriteLine`,
  `print`, `console.log`, `System.out.println`, `fmt.Println`, `puts`,
  `dbg!`, `Write-Host`, `std::cout`); inconsistent naming versus siblings;
  leftover TODO comments. Mention in the note column but do not deduct.

#### Combining sub-grades

Convert sub-grades to numeric points: A=4, B=3, C=2, D=1, F=0.
- **Overall score band** = weighted average:
  `0.45 × Assertion + 0.30 × Anti-pattern + 0.25 × Structure`
- Map to letter:
  - ≥ 3.5 → **A** (band 90–100)
  - ≥ 2.8 → **B** (band 80–89)
  - ≥ 2.0 → **C** (band 70–79)
  - ≥ 1.2 → **D** (band 60–69)
  - < 1.2 → **F** (band 0–59)
- The overall grade is **capped at the worst sub-grade** — if any sub-grade
  is **F**, the overall grade is **F**; if the worst sub-grade is **D**,
  the overall grade is at most **D**; and so on. A test that fails on any
  one dimension cannot earn a higher overall grade than that dimension.

Report the **letter grade** and the **score band** (not a single 0–100
number). False precision invites bikeshedding; bands keep the conversation
focused on the rubric.

### Step 4: Assign the decision result

The grade summarizes strength; the result says whether follow-up exists.
An actionable improvement is an evidence-backed change to the test, setup, or
fixtures. Assign exactly one:

- **Pass** — no actionable improvement; positive/context-only notes are allowed.
- **Failed** — at least one actionable improvement, regardless of grade.
- **Uncertain** — missing evidence prevents a decision and needs human review.
- **Not applicable** — a valid scope contains no eligible tests; normally an
  overall result with no rows.

Do not derive status from grade: a complete focused test can be **B / Pass**,
while debug output can make an otherwise excellent test **A / Failed**. Use
Uncertain for an unresolved body, unsupported construct, or essential missing
contract—not merely absent production code. A definite finding wins over
uncertainty.

### Step 5: Build the note

Use one sentence (target ≤ 120 characters) for the most important reason:
`No issues found.`, `Only checks IsNotNull; receipt contents are unverified.`, or
`Method body could not be resolved; human review is required.` Do not invent a
weakness to justify a grade or Failed result.

Keep the action in a separate **How to improve** field. For each Failed test,
name the smallest useful input, assertion, or fixture change and its expected
outcome, grounded in the body, source, or an explicit contract. For example,
`Replace self-comparison with Assert.AreEqual(60m, account.Balance).`, not
`Improve assertions`; `Remove Console.WriteLine after Deposit(25m).`, not
`Clean up`. Prioritize the highest-impact distinct finding, and include other
actionable findings only when they require a different change.

For a behavioral gap, use the distinguishing witness and original/mutant
observations from the shared assessment; check the expected result against
the unmodified source. If essential context is missing, name the evidence
needed instead of inventing an expected value. Pass gets `None`; Uncertain
gets a concrete evidence-resolution step, not a speculative test rewrite.
A rubric-only deduction is not proof of a behavioral gap or an actionable
improvement: a focused **B / Pass** may need no change. **A / Failed** still
needs its concrete action, such as removing debug output.

### Step 6: Report

Produce two sections.

#### 1. Summary

Begin with `**Result: <Pass|Failed|Uncertain|Not applicable>**`, then give result
counts and the highest-priority action. Aggregate using
**Failed → Uncertain → Pass → Not applicable**. For Not applicable, explain the
empty scope and omit the table.

#### 2. Per-test table

```markdown
| Test | Result | Quality | Notes | How to improve |
|------|--------|---------|-------|----------------|
| `Namespace.ClassName.Test_Method_Condition_Expected` | Pass | B (80–89) | One complete value assertion. | None |
| `Namespace.ClassName.Withdraw_SufficientFunds` | Failed | D (60–69) | Balance is compared with itself. | Replace self-comparison with `Assert.AreEqual(60m, account.Balance)` after withdrawing 40m from 100m. |
| `Namespace.ClassName.Test_Missing` | Uncertain | — | Method body could not be resolved; human review is required. | Supply the method body and its referenced fixture. |
```

Keep these two report sections and the original Test/Result/Quality/Notes
fields. When mutation evidence explains a finding or the caller requests
detail, append a compact per-test **Pseudo-mutation evidence** block inside
the per-test section: change, witness, original/mutant observations, relevant
assertion, and classification. Static results are **Likely killed (inferred)**
or **Candidate survivor (unverified)**, never executed Killed/Survived or
empirical killed/total counts. State missing-context N/A / unverified once
per shared limitation. Do not repeat the improvement table in prose.

**Caps and ordering**:
- If the table would exceed **50 rows**, show Failed tests first, then
  Uncertain tests, then a sample of Pass tests. Wrap overflow in a collapsed
  `<details>` block.
- Within the same result, order by quality from worst to best, then by file
  path and method name for determinism.
- If the diff context is provided, prefix each test name with a `(new)` or
  `(modified)` marker.

If multiple languages are present, produce one table per language and
prefix each section with the language name and framework.

## Validation

- [ ] Every test in the input list appears in the table (or is recorded as
      `Uncertain — method not found`).
- [ ] Every resolved test has Pass or Failed plus A-F quality detail.
- [ ] Uncertain is an evidence gap; Not applicable is a valid empty scope.
- [ ] Every grade is justified by at least one observable signal in the
      captured body — no speculative deductions.
- [ ] Every Failed row has a concrete, evidence-backed How to improve action;
      Pass rows have no invented weakness, even when the quality grade is B.
- [ ] Mutation assessment stayed read-only and per-test; unavailable context
      was not penalized, equivalents were excluded, and static labels/counts
      were not presented as executed evidence.
- [ ] Verified observable findings inform existing categories without a
      duplicate deduction or any change to scoring weights and ceilings.
- [ ] Trivial-assertion tests are flagged only when the **only** assertion
      is trivial (a null check before a meaningful assertion is not trivial).
- [ ] Exception-only tests are not penalized for low assertion count.
- [ ] Mock-call verifications and bare assertion forms count as real
      assertions of the appropriate category.
- [ ] Boolean assertions on meaningful properties (`Assert.IsTrue(result.IsValid)`)
      are not classified as always-true; only literal `true`/`false` constants are.
- [ ] Self-referential assertions are flagged separately from normal
      equality assertions.
- [ ] Idiomatic patterns are not flagged: Go/Rust table-driven sub-tests,
      pytest bare `assert`, Go `if got != want { t.Errorf(...) }`,
      JS/TS `expect(mock).toHaveBeenCalledWith(...)`.
- [ ] Async test pitfalls (un-awaited `resolves`/`rejects`/`ThrowsAsync`,
      pytest-asyncio without `await`) drop the Assertion sub-grade to F.
- [ ] The summary leads with the highest-leverage observation, not a recap
      of the table.

## Common Pitfalls

| Pitfall | Solution |
|---------|----------|
| Grading every test in the workspace when no list is provided | Ask the caller for the explicit list; this skill is for curated input. |
| Inflating deductions to justify the grade | Start at A; deduct only for observable issues. |
| Penalizing exception tests for low assertion count | Exception assertions are complete on their own. |
| Downgrading a focused Go error-path test because it checks only `err != nil` | Expected-error existence is the observable contract for that scope; keep it at A unless the production contract requires a specific error identity or message. |
| Treating `IsNotNull` before a value assertion as trivial | Only flag when the null check is the **only** assertion. |
| Treating any Boolean assertion as effectively assertion-free | Only always-true literals (`Assert.IsTrue(true)`, `assert True`) are; meaningful `Assert.IsTrue(result.IsValid)` is a real assertion. |
| Flagging Go/Rust table-driven loops as conditional logic | They are idiomatic; do not deduct. |
| Treating pytest bare `assert` or Go `if got != want { t.Error… }` as missing-framework | Both are canonical; count in the correct assertion category. |
| Penalizing tests when production code is unavailable | Mark concerns about uncovered behaviors as `Unverified` and do not deduct. |
| Using a fake-precise score (e.g., 87/100) | Use the score band only — 90–100, 80–89, 70–79, 60–69, 0–59. |
| Spilling a 500-row table into a PR comment | Apply the row cap from Step 6; collapse extras into `<details>`. |
| Re-reporting an existing finding three times under different categories | Pick the most fitting category and report once. |
| Giving a weak test credit for a sibling's assertions | Use only the current test and helpers/fixtures it executes. |
| Turning pseudo-mutation composition into a suite audit | Pass explicit per-test-read-only mode; no runs, edits, broad discovery, or agent recursion. |
| Inventing weaknesses for A-grade tests to make the note "balanced" | If a test is clean, the note may simply read `No issues found.` |
| Mapping status from grade or comments | Fail only for actionable improvements; a B can Pass and an A can Fail. |
| Confusing Uncertain and Not applicable | Evidence gaps are Uncertain; a valid empty scope is Not applicable. |
