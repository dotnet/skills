# dotnet-test

Skills and agents for running, generating, analyzing, migrating, and improving .NET tests across all major frameworks (MSTest, xUnit, NUnit, TUnit) and platforms (VSTest, Microsoft.Testing.Platform).

## When to use this plugin

- **Run tests** — execute `dotnet test` with automatic platform/framework detection and filter syntax
- **Generate tests** — scaffold comprehensive unit tests for any language via a multi-agent pipeline
- **Migrate tests** — upgrade MSTest v1/v2 → v3 → v4, xUnit v2 → v3, or VSTest → Microsoft.Testing.Platform
- **Audit test quality** — detect anti-patterns, test smells, assertion gaps, and coverage risks
- **Improve testability** — find static dependencies, generate wrappers, and migrate call sites to injectable abstractions
- **Measure coverage** — collect code coverage, compute CRAP scores, and surface risk hotspots

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

### Test quality & analysis

| Skill | Description |
|---|---|
| **test-anti-patterns** | Quick pragmatic scan for ~15 common test quality issues with severity ranking |
| **test-smell-detection** | Deep formal audit using academic test smell taxonomy (19 smell types) |
| **assertion-quality** | Measure assertion variety and depth — find shallow tests that barely verify anything |
| **test-gap-analysis** | Pseudo-mutation analysis to find test blind spots that coverage numbers miss |
| **test-tagging** | Tag tests with standardized traits (smoke, regression, boundary, critical-path, etc.) |

### Coverage & risk

| Skill | Description |
|---|---|
| **coverage-analysis** | Project-wide code coverage collection with CRAP score computation and risk hotspot reporting |
| **crap-score** | Calculate CRAP (Change Risk Anti-Patterns) scores for individual methods, classes, or files |

### Testability improvement

| Skill | Description |
|---|---|
| **detect-static-dependencies** | Scan C# code for hard-to-test statics (DateTime.Now, File.*, HttpClient, etc.) |
| **generate-testability-wrappers** | Generate wrapper interfaces or guide adoption of built-in abstractions (TimeProvider, IFileSystem) |
| **migrate-static-to-wrapper** | Bulk-replace static call sites with injected wrapper calls and add constructor injection |

### Reference data (loaded by other skills)

| Skill | Description |
|---|---|
| **code-testing-extensions** | Language-specific guidance files loaded by the code-testing pipeline |
| **platform-detection** | Detect VSTest vs MTP and identify the test framework from project files |
| **filter-syntax** | Test filter syntax reference for VSTest and MTP across all frameworks |
| **dotnet-test-frameworks** | Framework detection patterns, assertion APIs, skip annotations, and lifecycle methods |

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

- .NET SDK installed (`dotnet` on PATH)
- A project with an existing test framework (MSTest, xUnit, NUnit, or TUnit) for execution and analysis skills
