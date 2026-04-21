---
name: exp-test-gap-analysis
description: "Performs pseudo-mutation analysis on .NET production code to find gaps in existing test suites. Use when the user asks to find weak tests, discover untested edge cases, check if tests would catch a bug, or evaluate test effectiveness through mutation-style reasoning. Analyzes production code for mutation points (boundary conditions, boolean flips, null returns, exception removal, arithmetic changes) and checks whether existing tests would detect each mutation. Works with MSTest, xUnit, NUnit, and TUnit. DO NOT USE FOR: writing new tests (use writing-mstest-tests), detecting test anti-patterns (use test-anti-patterns), measuring assertion diversity (use exp-assertion-quality), or running actual mutation testing tools."
---

# Test Gap Analysis via Pseudo-Mutation

Analyze .NET production code by reasoning about hypothetical mutations and checking whether existing tests would catch them. This reveals blind spots where tests pass but would continue to pass even if the code were broken.

## Why Pseudo-Mutation Matters

Code coverage tells you what code ran during tests. It does **not** tell you whether tests would fail if that code were wrong. A method can have 100% line coverage but zero tests that would catch a sign flip, an off-by-one error, or a removed null check.

Pseudo-mutation analysis asks: _"If I changed this line, would any test fail?"_ When the answer is "no," you've found a test gap.

| Coverage Metric | What It Measures | What It Misses |
|----------------|-----------------|----------------|
| Line coverage | Which lines executed | Whether assertions verify those lines' behavior |
| Branch coverage | Which branches taken | Whether both branches produce different asserted outcomes |
| **Mutation score** | Whether tests detect code changes | Nothing — this is the gold standard |

This skill performs **static pseudo-mutation** — reasoning about mutations without actually running them — to approximate mutation testing at the speed of code review.

## When to Use

- User asks "would my tests catch a bug in this code?"
- User wants to find weak or shallow tests
- User wants to evaluate test effectiveness beyond coverage
- User asks for mutation testing or mutation analysis
- User asks "where are my tests blind?"
- User wants to prioritize which tests to strengthen

## When Not to Use

- User wants to write new tests from scratch (use `writing-mstest-tests`)
- User wants to detect test anti-patterns like flakiness or poor naming (use `test-anti-patterns`)
- User wants to measure assertion variety (use `exp-assertion-quality`)
- User wants to run an actual mutation testing framework like Stryker (help them directly)
- User only wants code coverage numbers (out of scope)

## Inputs

| Input | Required | Description |
|-------|----------|-------------|
| Production code | Yes | The source files to analyze for mutation points |
| Test code | Yes | The test files that cover the production code |
| Focus area | No | A specific mutation category or code region to focus on |

## Workflow

### Step 1: Gather production and test code

Read both the production code and its corresponding test files. If the user points to a directory, identify production/test pairs by convention (e.g., `Calculator.cs` tested by `CalculatorTests.cs`).

Establish which production methods are exercised by which test methods — trace this through method calls in test code, setup, and helper methods.

### Step 2: Identify mutation points

Scan the production code and annotate every location where a mutation could reveal a test gap. Use the mutation catalog below.

#### Boundary Mutations

| Original | Mutation | What it tests |
|----------|----------|---------------|
| `<` | `<=` | Off-by-one at upper bound |
| `>` | `>=` | Off-by-one at lower bound |
| `<=` | `<` | Boundary inclusion |
| `>=` | `>` | Boundary inclusion |
| `== 0` | `== 1` or `<= 0` | Zero-boundary handling |
| `i < length` | `i < length - 1` or `i <= length` | Loop boundary |
| `index + 1` | `index` or `index + 2` | Index arithmetic |

#### Boolean and Logic Mutations

| Original | Mutation | What it tests |
|----------|----------|---------------|
| `&&` | `\|\|` | Condition independence |
| `\|\|` | `&&` | Condition necessity |
| `!condition` | `condition` | Negation correctness |
| `if (x)` | `if (!x)` | Branch selection |
| `true` (constant) | `false` | Hardcoded assumption |
| `flag \|\| other` | `other` | Short-circuit first operand |

#### Return Value Mutations

| Original | Mutation | What it tests |
|----------|----------|---------------|
| `return result` | `return null` | Null handling downstream |
| `return result` | `return default` | Default value handling |
| `return true` | `return false` | Boolean return verification |
| `return list` | `return new List<T>()` | Empty collection handling |
| `return count` | `return 0` or `return count + 1` | Numeric return verification |
| `return string` | `return ""` or `return null` | String return verification |

#### Exception Removal Mutations

| Original | Mutation | What it tests |
|----------|----------|---------------|
| `throw new ArgumentNullException(...)` | _(remove entire throw)_ | Guard clause verification |
| `throw new InvalidOperationException(...)` | _(remove entire throw)_ | State validation testing |
| `if (x == null) throw ...` | _(remove entire guard)_ | Null guard testing |
| `if (!IsValid()) throw ...` | _(remove entire check)_ | Validation testing |

#### Arithmetic Mutations

| Original | Mutation | What it tests |
|----------|----------|---------------|
| `a + b` | `a - b` | Addition correctness |
| `a - b` | `a + b` | Subtraction correctness |
| `a * b` | `a / b` | Multiplication correctness |
| `a / b` | `a * b` | Division correctness |
| `a % b` | `a / b` | Modulo correctness |
| `x++` | `x--` | Increment direction |
| `-value` | `value` | Sign flip |

#### Null-Check Removal Mutations

| Original | Mutation | What it tests |
|----------|----------|---------------|
| `if (x == null) return ...` | _(remove null check)_ | Null path coverage |
| `if (x != null) { ... }` | _(always enter block)_ | Null guard necessity |
| `x ?? defaultValue` | `x` | Null coalescing coverage |
| `x?.Method()` | `x.Method()` | Null-conditional coverage |
| `x!` | `x` | Null-forgiving operator necessity |

### Step 3: Evaluate each mutation against tests

For each identified mutation point, reason about whether existing tests would detect the change:

1. **Find covering tests** — Which test methods exercise the mutated line? Follow call chains through helpers and setup methods.
2. **Check assertion relevance** — Do those tests assert something that would change if the mutation were applied? A test that calls the method but only asserts an unrelated property would NOT catch the mutation.
3. **Classify the mutation** as:

| Verdict | Meaning | Action |
|---------|---------|--------|
| **Killed** | At least one test would fail if this mutation were applied | No action needed — tests are effective here |
| **Survived** | No test would fail — the mutation would go undetected | This is a test gap — recommend a test improvement |
| **No coverage** | No test exercises this code path at all | Worse than survived — the code is untested |
| **Equivalent** | The mutation produces identical behavior (e.g., `x * 1` → `x / 1`) | Skip — not a real mutation |

### Step 4: Calibrate findings

Before reporting, apply these calibration rules:

- **Don't flag trivial code.** Simple property getters (`return _name;`), auto-properties, and boilerplate don't need mutation analysis. Focus on logic, conditions, calculations, and error handling.
- **Consider defensive depth.** If a null guard has a survived mutation but the caller also checks for null, note the redundancy but rate it lower priority.
- **Equivalent mutations are not gaps.** If changing `>=` to `>` doesn't alter behavior because the `==` case is impossible given the domain, mark it Equivalent and skip.
- **Private methods reached through public API are valid targets.** Trace through the call chain — a private method called from a tested public method may still have survived mutations if the test doesn't assert the specific behavior affected.
- **Rate by risk, not count.** A single survived mutation in payment calculation logic is more important than five survived mutations in logging code.

### Step 5: Report findings

Present the analysis in this structure:

1. **Summary** — Overall mutation score and key findings:
   ```
   | Metric              | Value    |
   |---------------------|----------|
   | Mutation points      | 42       |
   | Killed               | 28 (67%) |
   | Survived             | 10 (24%) |
   | No coverage          | 2 (5%)   |
   | Equivalent (skipped) | 2 (5%)   |
   ```

2. **Survived Mutations (Test Gaps)** — For each survived mutation, report:
   - **Location**: File, method, line
   - **Mutation category**: Boundary / Boolean / Return value / Exception / Arithmetic / Null-check
   - **Original code**: The current code
   - **Hypothetical mutation**: What would change
   - **Why it survives**: Which tests cover this code and why their assertions miss it
   - **Recommended fix**: A concrete test assertion or new test case that would kill this mutation

   Group by priority: high-risk survived mutations first (business logic, calculations, security checks), lower-risk last (logging, formatting).

3. **No-Coverage Zones** — Code paths that no test reaches at all. These are worse than survived mutations.

4. **Killed Mutations (Strengths)** — Briefly note areas where tests are effective. Highlight well-tested methods and strong assertion patterns. Don't enumerate every killed mutation — summarize.

5. **Recommendations** — Prioritized list:
   - Which survived mutations to address first (by risk)
   - Specific test methods to add or strengthen
   - Patterns the team can adopt to prevent future gaps (e.g., always test boundary values, always assert exception types)

## Validation

- [ ] Every mutation point was classified (Killed / Survived / No coverage / Equivalent)
- [ ] Every survived mutation includes the original code, the hypothetical change, and why tests miss it
- [ ] Every survived mutation includes a concrete recommended fix (a test assertion or test case)
- [ ] Equivalent mutations are correctly identified and excluded from the score
- [ ] Trivial code (simple getters, auto-properties) is excluded from analysis
- [ ] Findings are prioritized by risk, not just listed in source order
- [ ] Report includes strengths (killed mutations) alongside gaps
- [ ] Mutation categories are correctly labeled

## Common Pitfalls

| Pitfall | Solution |
|---------|----------|
| Analyzing trivial code | Skip auto-properties, simple getters, and boilerplate — focus on logic |
| Reporting equivalent mutations as gaps | If the mutation doesn't change behavior, it's not a gap — mark Equivalent |
| Ignoring call chains | A private helper called from a tested public method is reachable — trace the chain |
| Over-counting mutations in generated code | Skip auto-generated code, designer files, and migration files |
| Recommending a new test for every survived mutation | Multiple survived mutations in the same method often share a single missing test — recommend one test that kills several |
| Ignoring production context | A survived mutation in `ToString()` formatting is less important than one in `CalculateTotal()` — prioritize by business risk |
| Claiming 100% kill rate is required | Some mutations in low-risk code are acceptable to leave — acknowledge this in the report |
| Not considering integration with other skills | If gaps are found, mention that `writing-mstest-tests` can help write the missing tests, and `test-anti-patterns` can audit existing test quality |
