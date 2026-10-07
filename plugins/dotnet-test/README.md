# dotnet-test

Skills and GitHub Copilot custom agents for running, generating, analyzing, and improving tests. Originally built for .NET (MSTest, xUnit, NUnit, TUnit) and platforms (VSTest, Microsoft.Testing.Platform); the test-generation pipeline and the six test-analysis skills (anti-patterns, smells, assertion quality, gap analysis, tagging, grade tests) plus the `test-quality-auditor` agent are **polyglot** and also work with Python (pytest/unittest), TypeScript/JavaScript (Jest/Vitest/Mocha/Jasmine/node:test), Java (JUnit 4/5/TestNG), Go (testing/testify), Ruby (RSpec/Minitest), Rust (built-in/proptest), Swift (XCTest/Swift Testing), Kotlin (JUnit/Kotest), PowerShell (Pester), and C++ (GoogleTest/Catch2/doctest/Boost.Test).

> **Test framework/platform migration** (MSTest/xUnit upgrades, xUnit → MSTest, VSTest → Microsoft.Testing.Platform) lives in the separate [`dotnet-test-migration`](../dotnet-test-migration/) plugin.

## When to use this plugin

- **Run tests** *(.NET only)* — execute SDK-style projects with `dotnet test`, or preserve a classic project's checked-in MSBuild + VSTest/MSTest command
- **Generate tests** *(polyglot)* — scaffold unit tests for any language with a scope-sized Research → Plan → Implement workflow
- **Migrate tests** *(.NET only)* — see the separate [`dotnet-test-migration`](../dotnet-test-migration/) plugin (MSTest v1/v2 → v3 → v4, xUnit v2 → v3, xUnit → MSTest, VSTest → Microsoft.Testing.Platform)
- **Audit test quality** *(polyglot)* — detect anti-patterns, test smells, assertion gaps, and (for .NET) coverage risks
- **Improve testability** *(.NET only)* — find static dependencies, generate wrappers, and migrate call sites to injectable abstractions
- **Measure coverage** *(.NET only)* — collect code coverage, compute CRAP scores, and surface risk hotspots

## Skills

### Test execution

| Skill | Description |
|---|---|
| **run-tests** | Run .NET tests with project-system/platform/framework detection, including classic non-SDK runner commands |
| **mtp-hot-reload** | Rapid test-fix iteration using MTP hot reload (edit code → re-run without rebuilding) |

### Test generation

| Skill | Description |
|---|---|
| **code-testing-agent** | Scope-sized test generation for any language: focused additions stay direct; broad requests use the generator-owned Research → Plan → Implement pipeline with proportionate validation and review |
| **scaffold-dotnet-test-project** *(.NET)* | Create a missing test project or repair its project/solution/filter wiring |
| **writing-mstest-tests** | Version-compatible MSTest authoring for modern and classic projects, including MSTest 3.x/4.x APIs |

### Test migration

Moved to the [`dotnet-test-migration`](../dotnet-test-migration/) plugin (`migrate-mstest-v1v2-to-v3`, `migrate-mstest-v3-to-v4`, `migrate-xunit-to-xunit-v3`, `migrate-xunit-to-mstest`, `migrate-vstest-to-mtp`, and the `test-migration` orchestrator agent).

### Test quality & analysis *(polyglot)*

These six skills are all polyglot. They work across all supported languages by loading a per-language reference file from `test-analysis-extensions`. `grade-tests` additionally embeds its own decision and scoring rubric so per-test Pass / Failed / Uncertain outcomes and supporting A-F quality grades stay consistent across calls.

| Skill | Description |
|---|---|
| **test-anti-patterns** | Quick pragmatic scan for common test quality issues with severity ranking (any language) |
| **test-smell-detection** | Deep formal audit using academic test smell taxonomy (19 smell types, any language) |
| **assertion-quality** | Measure assertion variety and depth — find shallow tests that barely verify anything (any language) |
| **test-gap-analysis** | Verify test blind spots through pseudo-mutations and optionally add focused tests that kill them (any language) |
| **test-tagging** | Tag tests with standardized traits (smoke, regression, boundary, critical-path, etc.); auto-edits where the framework has canonical syntax, report-only otherwise |
| **grade-tests** | Assess a curated list of test methods and produce a compact PR-ready table with Pass, Failed, or Uncertain decisions, A-F quality detail for resolved tests, and one-line notes; unresolved or empty scopes omit the grade, and a valid scope with no tests returns Not applicable (any language) |

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
| **testability-obstacle** | Resolve one concrete ambient-dependency blocker and test the behavior through fixed/in-memory dependencies |

### Detection and reference data

| Skill | Description |
|---|---|
| **code-testing-extensions** | Language-specific guidance loaded by the code-testing pipeline (test generation) |
| **test-analysis-extensions** | Language-specific guidance loaded by the polyglot analysis skills (test markers, assertion APIs, sleeps, skips, mystery-guest indicators, integration markers, tag-support capability) |
| **platform-detection** *(.NET)* | Directly detect SDK-style vs classic, VSTest vs MTP, and the test framework from project files |
| **filter-syntax** *(.NET)* | Test filter syntax reference for VSTest and MTP across all frameworks |

Three reference skills (`code-testing-extensions`, `test-analysis-extensions`,
and `filter-syntax`) set `disable-model-invocation: true`, so the CLI keeps them
out of the model-facing skill menu and a consumer loads them by name. They
deliberately have no direct `tests/dotnet-test/<skill>/eval.yaml`: the
experiment's skilled arm loads a single skill, which the model could never
invoke here, so such an eval would compare two identical arms and score judge
noise. They are measured through consumer outcomes — the polyglot analysis
skills and `grade-tests` for `test-analysis-extensions`, `code-testing-agent`
for `code-testing-extensions`, and `run-tests` and `mtp-hot-reload` for
`filter-syntax`. The `run-tests` eval covers VSTest expressions, MTP argument
passing, xUnit v3 native filters, and TUnit tree-node filters.

`platform-detection` is model-invocable because identifying a project's runner
is also a direct user task; `run-tests` and migration skills still load it as
shared detection guidance. Its command-mode rules use an on-demand reference so
platform/framework-only requests do not load or echo CLI-mode detail.
`filter-syntax` remains reference-only and is measured through the
filtered-command scenarios in the `run-tests` eval.

## Agents

The agents below are GitHub Copilot `.agent.md` definitions. Codex plugin installs expose this
plugin's skills, but not these agents or their static handoffs.

### User-facing agents

These are the entry-point agents you invoke directly:

| Agent | Purpose |
|---|---|
| **test-quality-auditor** | Routes focused quality requests, including curated per-test decisions, and runs multi-skill pipelines for comprehensive suite assessment |
| **testability-migration** | End-to-end testability improvement: detect → generate wrappers → migrate call sites → add deterministic tests when requested |

> **Test framework/platform migration** is handled by the `test-migration` agent in the separate [`dotnet-test-migration`](../dotnet-test-migration/) plugin.

### Internal subagents

These agents are internal (`user-invocable: false`); you do not need to call them directly. For broad requests, the `code-testing-agent` skill invokes the named `code-testing-generator` once when available. The generator owns the pipeline and returns evidence for the caller to reuse. Research, planning, implementation, and review remain required, but run inline by default, including bounded project-wide suites. The other agents below are optional workers for substantial work that benefits from separate context, used only when available in the runtime. Focused additions stay direct, without intermediate state artifacts or agent fan-out.

| Agent | Caller | Purpose |
|---|---|---|
| **code-testing-generator** | code-testing-agent skill (broad requests) | Owns the full test generation pipeline, with phases inline by default |
| **code-testing-researcher** | code-testing-generator (optional) | Analyzes codebase structure, testing patterns, and testability |
| **code-testing-planner** | code-testing-generator (optional) | Creates phased test implementation plans from research findings |
| **code-testing-implementer** | code-testing-generator (optional) | Implements one phase from the plan, runs build-test-fix cycles |
| **code-testing-builder** | code-testing-generator or delegated implementer (optional) | Runs build/compile commands and reports results |
| **code-testing-tester** | code-testing-generator or delegated implementer (optional) | Runs test commands and reports pass/fail results |
| **code-testing-fixer** | code-testing-generator or delegated implementer (optional) | Fixes compilation errors in source or test files |
| **code-testing-linter** | code-testing-generator or delegated implementer (optional) | Runs code formatting and linting |

> **VS Code — optional nested delegation:** The pipeline does not require phase-agent fan-out. When substantial work warrants a subagent invoking another available named agent, VS Code gates that *nested* delegation behind a setting that is **off by default**. To allow it, enable this in your VS Code settings:
>
> ```jsonc
> "chat.subagents.allowInvocationsFromSubagents": true
> ```
>
> Without it, the generator or delegated implementer completes the phases inline; required validation and review are unchanged. The GitHub Copilot CLI has no such gate, but delegation is still optional: phases stay inline by default, and agents are used only for substantial separate-context work when available.

## Prerequisites

### For polyglot skills and agents

The test-generation pipeline (`code-testing-generator` and friends) and the six test-analysis skills (`test-anti-patterns`, `test-smell-detection`, `assertion-quality`, `test-gap-analysis`, `test-tagging`, `grade-tests`) plus the `test-quality-auditor` agent work with any of the supported languages above. You just need a working test runtime for the language you're targeting (e.g., `python` + `pytest`, `node` + `npm test`, `mvn` / `gradle`, `go`, `bundle exec rspec`, `cargo test`, `swift test`, `pwsh` + Pester, `cmake` + your C++ test runner). The skills will detect the framework automatically.

### For .NET-only skills and agents

- .NET SDK installed (`dotnet` on PATH)
- A project with an existing test framework (MSTest, xUnit, NUnit, or TUnit) for execution, migration, coverage, CRAP, testability, and the experimental `dotnet-experimental` skills.

### Classic non-SDK .NET projects

The test-generation and analysis heuristics support classic projects with
`packages.config`, explicit `<Compile Include>` items, older MSTest/Moq stacks,
and custom base fixtures. Generation preserves those conventions and registers
every new test file in the project.

Execution requires the repository's existing Windows/Visual Studio toolchain
(commonly full MSBuild plus `vstest.console.exe` or `MSTest.exe`). Coverage and
CRAP analysis accept existing Cobertura reports; they do not inject SDK-style
coverage packages into classic projects. If the required runner or coverage
workflow is absent, the skill reports the limitation rather than migrating the
project.

Testability wrappers and migrations are separate, explicit opt-in workflows.
Test generation and quality audits do not introduce production seams, and all
testability workflows must honor repository rules that prohibit such refactors.
