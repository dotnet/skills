# dotnet-test

Skills and agents for running, generating, analyzing, migrating, and improving tests. Originally built for .NET (MSTest, xUnit, NUnit, TUnit) and platforms (VSTest, Microsoft.Testing.Platform); the test-generation pipeline and the five test-analysis skills (anti-patterns, smells, assertion quality, gap analysis, tagging) plus the `test-quality-auditor` agent are **polyglot** and also work with Python (pytest/unittest), TypeScript/JavaScript (Jest/Vitest/Mocha/Jasmine/node:test), Java (JUnit 4/5/TestNG), Go (testing/testify), Ruby (RSpec/Minitest), Rust (built-in/proptest), Swift (XCTest/Swift Testing), Kotlin (JUnit/Kotest), PowerShell (Pester), and C++ (GoogleTest/Catch2/doctest/Boost.Test).

## When to use this plugin

- **Run tests** *(.NET only)* — execute `dotnet test` with automatic platform/framework detection and filter syntax
- **Generate tests** *(polyglot)* — scaffold comprehensive unit tests for any language via a multi-agent pipeline
- **Migrate tests** *(.NET only)* — upgrade MSTest v1/v2 → v3 → v4, xUnit v2 → v3, or VSTest → Microsoft.Testing.Platform
- **Audit test quality** *(polyglot)* — detect anti-patterns, test smells, assertion gaps, and (for .NET) coverage risks
- **Improve testability** *(.NET only)* — find static dependencies, generate wrappers, and migrate call sites to injectable abstractions
- **Measure coverage** *(.NET only)* — collect code coverage, compute CRAP scores, and surface risk hotspots

## Skills

### Test execution

| Skill | Description |
|---|---|
| **run-tests** | Run .NET tests via `dotnet test` with platform/framework auto-detection and filter support |
| **mtp-hot-reload** | Rapid test-fix iteration using MTP hot reload (edit code → re-run without rebuilding) |

### Test generation

| Skill | Description |
|---|---|
| **code-testing-agent** | Multi-agent pipeline (Research → Plan → Implement → Build → Test → Fix → Lint) that generates tests for any language |
| **writing-mstest-tests** | Best practices and modern APIs for writing MSTest 3.x/4.x tests |

### Test migration

| Skill | Description |
|---|---|
| **migrate-mstest-v1v2-to-v3** | Upgrade MSTest v1 (assembly refs) or v2 (NuGet 1.x–2.x) to v3 |
| **migrate-mstest-v3-to-v4** | Upgrade MSTest v3 to v4 — handles all source and behavioral breaking changes |
| **migrate-xunit-to-xunit-v3** | Upgrade xUnit.net v2 to v3 |
| **migrate-vstest-to-mtp** | Migrate from VSTest runner to Microsoft.Testing.Platform |

### Test quality & analysis *(polyglot)*

These five skills work across all supported languages by loading a per-language reference file from `test-analysis-extensions`.

| Skill | Description |
|---|---|
| **test-anti-patterns** | Quick pragmatic scan for common test quality issues with severity ranking (any language) |
| **test-smell-detection** | Deep formal audit using academic test smell taxonomy (19 smell types, any language) |
| **assertion-quality** | Measure assertion variety and depth — find shallow tests that barely verify anything (any language) |
| **test-gap-analysis** | Pseudo-mutation analysis to find test blind spots that coverage numbers miss (any language) |
| **test-tagging** | Tag tests with standardized traits (smoke, regression, boundary, critical-path, etc.); auto-edits where the framework has canonical syntax, report-only otherwise |

### Coverage & risk *(.NET only)*

| Skill | Description |
|---|---|
| **coverage-analysis** | Project-wide code coverage collection with CRAP score computation and risk hotspot reporting |
| **crap-score** | Calculate CRAP (Change Risk Anti-Patterns) scores for individual methods, classes, or files |

For non-.NET languages, use the native coverage tool: `coverage.py`/`pytest-cov` (Python), `jest --coverage`/`c8`/`nyc`/`vitest --coverage` (JS/TS), JaCoCo (Java), `go test -coverprofile` (Go), SimpleCov (Ruby), `cargo-tarpaulin`/`cargo-llvm-cov` (Rust), `xcrun llvm-cov` (Swift), Kover (Kotlin), Pester's built-in code coverage (PowerShell), `gcov`/`llvm-cov` (C++).

### Testability improvement *(.NET only)*

| Skill | Description |
|---|---|
| **detect-static-dependencies** | Scan C# code for hard-to-test statics (DateTime.Now, File.*, HttpClient, etc.) |
| **generate-testability-wrappers** | Generate wrapper interfaces or guide adoption of built-in abstractions (TimeProvider, IFileSystem) |
| **migrate-static-to-wrapper** | Bulk-replace static call sites with injected wrapper calls and add constructor injection |

### Reference data (loaded by other skills)

| Skill | Description |
|---|---|
| **code-testing-extensions** | Language-specific guidance loaded by the code-testing pipeline (test generation) |
| **test-analysis-extensions** | Language-specific guidance loaded by the polyglot analysis skills (test markers, assertion APIs, sleeps, skips, mystery-guest indicators, integration markers, tag-support capability) |
| **platform-detection** *(.NET)* | Detect VSTest vs MTP and identify the test framework from project files |
| **filter-syntax** *(.NET)* | Test filter syntax reference for VSTest and MTP across all frameworks |
| **dotnet-test-frameworks** *(.NET)* | Framework detection patterns, assertion APIs, skip annotations, and lifecycle methods (kept for backward compatibility with .NET-only skills like `writing-mstest-tests`) |

## Agents

### User-facing agents

These are the entry-point agents you invoke directly:

| Agent | Purpose |
|---|---|
| **code-testing-generator** | Orchestrates the full test generation pipeline (research → plan → implement → build → test → fix → lint) |
| **test-migration** | Auto-detects framework/version and routes to the correct migration skill |
| **test-quality-auditor** | Runs multi-skill audit pipelines for comprehensive test suite assessment |
| **testability-migration** | End-to-end testability improvement: detect → generate wrappers → migrate call sites |

### Internal subagents

These are pipeline stages invoked automatically by the agents above (`user-invocable: false`). You do not need to call them directly:

| Agent | Called by | Purpose |
|---|---|---|
| **code-testing-researcher** | code-testing-generator | Analyzes codebase structure, testing patterns, and testability |
| **code-testing-planner** | code-testing-generator | Creates phased test implementation plans from research findings |
| **code-testing-implementer** | code-testing-generator | Implements one phase from the plan, runs build-test-fix cycles |
| **code-testing-builder** | code-testing-implementer | Runs build/compile commands and reports results |
| **code-testing-tester** | code-testing-implementer | Runs test commands and reports pass/fail results |
| **code-testing-fixer** | code-testing-implementer | Fixes compilation errors in source or test files |
| **code-testing-linter** | code-testing-implementer | Runs code formatting and linting |

## Prerequisites

### For polyglot skills and agents

The test-generation pipeline (`code-testing-generator` and friends) and the five test-analysis skills (`test-anti-patterns`, `test-smell-detection`, `assertion-quality`, `test-gap-analysis`, `test-tagging`) plus the `test-quality-auditor` agent work with any of the supported languages above. You just need a working test runtime for the language you're targeting (e.g., `python` + `pytest`, `node` + `npm test`, `mvn` / `gradle`, `go`, `bundle exec rspec`, `cargo test`, `swift test`, `pwsh` + Pester, `cmake` + your C++ test runner). The skills will detect the framework automatically.

### For .NET-only skills and agents

- .NET SDK installed (`dotnet` on PATH)
- A project with an existing test framework (MSTest, xUnit, NUnit, or TUnit) for execution, migration, coverage, CRAP, testability, and the experimental `dotnet-experimental` skills.
