---
name: test-gap-analysis
description: "Performs pseudo-mutation analysis on production code in any language to find gaps in existing test suites. Use when the user asks to find weak tests, discover untested edge cases, check if tests would catch a bug, or evaluate test effectiveness through mutation-style reasoning. Analyzes production code for mutation points (boundaries, boolean flips, null/None/nil returns, exception/error removal, arithmetic changes) and checks whether tests would detect each mutation. Polyglot: .NET (MSTest/xUnit/NUnit/TUnit), Python (pytest/unittest), TS/JS (Jest/Vitest/Mocha/node:test), Java (JUnit/TestNG), Go, Ruby (RSpec/Minitest), Rust, Swift, Kotlin (JUnit/Kotest), PowerShell (Pester), C++ (GoogleTest/Catch2). DO NOT USE FOR: writing new tests (use code-testing-agent, or writing-mstest-tests for MSTest), detecting anti-patterns (use test-anti-patterns), measuring assertion diversity (use assertion-quality), or running actual mutation testing tools (Stryker, mutmut, PIT, cargo-mutants)."
license: MIT
---

# Test Gap Analysis via Pseudo-Mutation

Analyze production code in any supported language by reasoning about hypothetical mutations and checking whether existing tests would catch them. This reveals blind spots where tests pass but would continue to pass even if the code were broken.

> **Language-specific guidance**: Call the `test-analysis-extensions` skill to discover available extension files, then read the file matching the target codebase (e.g., `extensions/dotnet.md`, `extensions/python.md`, `extensions/typescript.md`). The extension file helps you find test files, recognize framework-specific assertion APIs, and identify language-specific null/None/nil patterns and error-handling idioms that map to the mutation catalog below.

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

- User wants to write new tests from scratch (use `code-testing-agent` for any language, or `writing-mstest-tests` for MSTest specifically)
- User wants to detect test anti-patterns like flakiness or poor naming (use `test-anti-patterns`)
- User wants to measure assertion variety (use `assertion-quality`)
- User wants to run an actual mutation testing framework (Stryker for .NET/JS/TS, mutmut for Python, PIT for Java, go-mutesting for Go, cargo-mutants for Rust, mutant for Ruby) — help them directly with the tool
- User only wants code coverage numbers (out of scope)

## Inputs

| Input | Required | Description |
|-------|----------|-------------|
| Production code | Yes | The source files to analyze for mutation points |
| Test code | Yes | The test files that cover the production code |
| Focus area | No | A specific mutation category or code region to focus on |

## Workflow

### Step 1: Detect language and load extension

Identify the target codebase's language and test framework. Call the `test-analysis-extensions` skill and read the matching extension file. The mutation catalog below uses language-neutral concepts; the extension file tells you how each concept maps in the language you are analyzing (e.g., `null` vs `None` vs `nil` vs `undefined`, `throw` vs `raise` vs `panic!` vs `return err`).

### Step 2: Gather production and test code

Read both the production code and its corresponding test files. If the user points to a directory, identify production/test pairs by convention — defaults differ by language: `.cs` ↔ `*Tests.cs`/`*.Tests.cs` (.NET), `foo.py` ↔ `test_foo.py`/`foo_test.py` (Python), `foo.ts` ↔ `foo.test.ts`/`foo.spec.ts` (JS/TS), `Foo.java` ↔ `FooTest.java`/`FooTests.java` (Java), `foo.go` ↔ `foo_test.go` (Go), `foo.rb` ↔ `foo_spec.rb`/`test_foo.rb` (Ruby), `lib.rs` ↔ inline `#[cfg(test)] mod tests` or `tests/foo.rs` (Rust), `Foo.swift` ↔ `FooTests.swift` (Swift), `Foo.kt` ↔ `FooTest.kt`/`FooSpec.kt` (Kotlin), `Foo.ps1` ↔ `Foo.Tests.ps1` (Pester), `foo.cpp` ↔ `foo_test.cpp`/`test_foo.cpp` (C++).

Establish which production methods are exercised by which test methods — trace this through method calls in test code, setup, helper methods, and shared examples.

### Step 3: Identify mutation points

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
| `return result` | `return null` / `return None` / `return nil` / `return undefined` | Null/None/nil handling downstream |
| `return result` | `return default(T)` / `return T()` / `return ""` / `return 0` | Default value handling |
| `return true` | `return false` | Boolean return verification |
| `return list` | `return new List<T>()` / `return []` / `return Array.Empty<T>()` / `return make([]T, 0)` / `return Vec::new()` / `return @[]` | Empty collection handling |
| `return count` | `return 0` or `return count + 1` | Numeric return verification |
| `return string` | `return ""` or `return null`/`None`/`nil` | String return verification |
| `return Ok(x)` | `return Err(...)` (Rust) | Result/error variant |
| `return value, nil` | `return zero, err` (Go) | Error tuple |

#### Exception / Error Removal Mutations

| Original | Mutation | What it tests |
|----------|----------|---------------|
| `throw new ArgumentNullException(...)` (.NET) / `raise ValueError(...)` (Python) / `throw new Error(...)` (JS) / `throw new IllegalArgumentException(...)` (Java) / `panic!(...)` (Rust) / `panic(...)` (Go) / `raise ArgumentError` (Ruby) / `throw RuntimeException(...)` (Kotlin) / `throw FooError.bar` (Swift) / `throw "..."` (Pester) / `throw std::invalid_argument(...)` (C++) | _(remove entire throw/raise/panic)_ | Guard clause verification |
| `if (x == null) throw ...` / `if x is None: raise ...` / `if (!x) throw ...` / `if x == nil { return err }` (Go) / `assert!(x.is_some())` (Rust) | _(remove entire guard)_ | Null/None/nil guard testing |
| `if (!IsValid()) throw ...` / `if not is_valid(): raise ...` / etc. | _(remove entire check)_ | Validation testing |
| `return err` after error check (Go) | _(remove or swallow error)_ | Error propagation |
| `?` operator (Rust) | `.unwrap()` or `.expect(...)` | Error short-circuit |

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

#### Null / None / Nil-Check Removal Mutations

| Original | Mutation | What it tests |
|----------|----------|---------------|
| `if (x == null) return ...` / `if x is None: return ...` / `if (!x) return ...` / `if x == nil { return ... }` / `unless x; return; end` (Ruby) / `if x.is_none() { return ... }` (Rust) | _(remove null/None/nil check)_ | Null path coverage |
| `if (x != null) { ... }` / `if x is not None: ...` / `if x: ...` / `if x != nil { ... }` / `x?.let { ... }` (Kotlin) / `if let Some(x) = ... { ... }` (Rust) | _(always enter block)_ | Null/None/nil guard necessity |
| `x ?? defaultValue` (.NET/JS/Swift) / `x or defaultValue` (Python) / `x \|\| defaultValue` (JS) / `x.unwrap_or(defaultValue)` (Rust) / `x \|\| defaultValue` (Kotlin: `x ?: defaultValue`) | `x` (drop coalescing) | Null coalescing coverage |
| `x?.Method()` (.NET/Swift/Kotlin) / `x && x.method()` (JS) / `x and x.method()` (Python) | `x.Method()` | Null-conditional coverage |
| `x!` (.NET/TS/Swift) / `x!!` (Kotlin) / `.unwrap()` (Rust) | `x` | Null-forgiving / unwrap necessity |

### Step 4: Evaluate each mutation against tests

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

### Step 5: Calibrate findings

Before reporting, apply these calibration rules:

- **Don't flag trivial code.** Simple property getters (`return _name;`), auto-properties, and boilerplate don't need mutation analysis. Focus on logic, conditions, calculations, and error handling.
- **Consider defensive depth.** If a null guard has a survived mutation but the caller also checks for null, note the redundancy but rate it lower priority.
- **Equivalent mutations are not gaps.** If changing `>=` to `>` doesn't alter behavior because the `==` case is impossible given the domain, mark it Equivalent and skip.
- **Private methods reached through public API are valid targets.** Trace through the call chain — a private method called from a tested public method may still have survived mutations if the test doesn't assert the specific behavior affected.
- **Rate by risk, not count.** A single survived mutation in payment calculation logic is more important than five survived mutations in logging code.

### Step 6: Report findings

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
| Analyzing trivial code | Skip auto-properties, simple getters, `@dataclass`/`record`/`data class` accessors, `#[derive]` impls — focus on logic |
| Reporting equivalent mutations as gaps | If the mutation doesn't change behavior, it's not a gap — mark Equivalent |
| Ignoring call chains | A private/internal/unexported helper called from a tested public method is reachable — trace the chain |
| Over-counting mutations in generated code | Skip auto-generated code (`*.g.cs`, `*.designer.cs`, `*_pb.go`, `*.pb.dart`), designer files, migration files, generated mocks/stubs |
| Recommending a new test for every survived mutation | Multiple survived mutations in the same method often share a single missing test — recommend one test that kills several |
| Ignoring production context | A survived mutation in `ToString()` / `__repr__` / `toString()` formatting is less important than one in `CalculateTotal()` — prioritize by business risk |
| Claiming 100% kill rate is required | Some mutations in low-risk code are acceptable to leave — acknowledge this in the report |
| Not considering integration with other skills | If gaps are found, mention that `code-testing-agent` (any language) or `writing-mstest-tests` (MSTest-specific) can help write the missing tests, and `test-anti-patterns` can audit existing test quality |
| Forgetting Go's error idiom | Removing `if err != nil { return err }` is a valid mutation target only when the function actually does something else with `err` (e.g., wrap, log, branch). Bare passthroughs in idiomatic Go are not meaningful gaps. |
| Forgetting Rust's `?` operator | `?` propagates `Err`/`None` short-circuits. Mutating `expr?` → `expr.unwrap()` panics instead of returning — flag as Exception/Panic mutation when tests should observe the propagated error. |
