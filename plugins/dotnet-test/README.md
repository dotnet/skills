# dotnet-test

Skills and a GitHub Copilot `test-engineer` agent for running, generating,
repairing, analyzing, and improving tests. Originally built for .NET (MSTest,
xUnit, NUnit, TUnit) and platforms (VSTest, Microsoft.Testing.Platform), the
test-engineering workflows are **polyglot** and also work with Python
(pytest/unittest), TypeScript/JavaScript (Jest/Vitest/Mocha/Jasmine/node:test),
Java (JUnit 4/5/TestNG), Go (testing/testify), Ruby (RSpec/Minitest), Rust
(built-in/proptest), Swift (XCTest/Swift Testing), Kotlin (JUnit/Kotest),
PowerShell (Pester), and C++ (GoogleTest/Catch2/doctest/Boost.Test).

> **Test framework/platform migration** (MSTest/xUnit upgrades, xUnit → MSTest, VSTest → Microsoft.Testing.Platform) lives in the separate [`dotnet-test-migration`](../dotnet-test-migration/) plugin.

## When to use this plugin

- **Run tests** *(.NET only)* — execute SDK-style projects with `dotnet test`, or preserve a classic project's checked-in MSBuild + VSTest/MSTest command
- **Generate tests** *(polyglot)* — scaffold comprehensive unit tests for any language via a multi-agent pipeline
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
| **code-testing** | Implicit entry skill for generating, repairing, and strengthening tests; broad work delegates to `test-engineer` |
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
| **test-gap-analysis** | Analyze test blind spots through pseudo-mutations, expose read-only per-test evidence for grading, and verify or close gaps only when requested (any language) |
| **test-tagging** | Tag tests with standardized traits (smoke, regression, boundary, critical-path, etc.); auto-edits where the framework has canonical syntax, report-only otherwise |
| **grade-tests** | Assess curated tests with Pass, Failed, or Uncertain decisions, A-F quality detail, notes, and concrete improvement actions; unresolved or empty scopes omit the grade, and a valid empty scope returns Not applicable (any language) |

Grading composes `test-gap-analysis` in explicit `per-test-read-only` mode:
no test runs, production edits, mutation execution, suite audit, or agent
recursion. Mutation methodology stays in that skill's bundled reference;
grading retains its own scoring weights and ceilings. Each test owns only its
claimed behavior, not its siblings' assertions or unrelated scenarios.
Static evidence uses inferred likely kills or unverified candidate survivors,
not executed mutation counts. Missing production context is N/A / unverified,
not a deduction. The existing result and quality fields remain independent:
a focused B can Pass without improvements, and an A can Fail for actionable
debug output.

Focused grading checks can run without an evaluation matrix:

```powershell
python -B tests\dotnet-test\grade-tests\test_regressions.py -v
python -B tests\dotnet-test\grade-tests\test_composition.py --cli <copilot-executable> --model <model-id> --results-dir <scratch-results>
```

The first command replays goldens and rejects malformed actions, extra test
rows, and misleading mutation evidence. The second uses the shipping Copilot
CLI, a copy of the production plugin, isolated configuration, and a
host-supplied path to the actual bundled Python assertion reference. It requires
successful grading and gap-analysis loads plus the owned read-only reference
read and their completions before the final grading report. It verifies the
target row's concrete improvement, rejects standalone execution/delegation and
N/A fallbacks, and checks that every fixture and plugin file is unchanged.
Generation/repair dormancy cases replay golden patches and prove that the
requested assertions reject wrong costs/flags while production stays
byte-for-byte unchanged. The CLI integration needs Copilot access via
an existing token or authenticated GitHub CLI; it does not change user settings.

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
skills and `grade-tests` for `test-analysis-extensions`, `code-testing`
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

Use this single entry-point agent for end-to-end test work:

| Agent | Purpose |
|---|---|
| **test-engineer** | Generates, repairs, runs, audits, and improves tests while coordinating the internal specialists below |

> **Test framework/platform migration** is handled by the `test-migration` agent in the separate [`dotnet-test-migration`](../dotnet-test-migration/) plugin.

### Internal subagents

These specialists are available to `test-engineer` (`user-invocable: false`);
you do not need to call them directly. The agent owns research, planning,
implementation, and review inline by default, and delegates only substantial
work that benefits from separate context.

| Agent | Called by | Purpose |
|---|---|---|
| **test-quality-auditor** | test-engineer | Runs multi-skill audit pipelines for comprehensive test-suite assessment |
| **testability-migration** | test-engineer | Performs explicit .NET production-code testability refactors and adds deterministic tests |
| **code-testing-researcher** | test-engineer | Analyzes codebase structure, testing patterns, and testability |
| **code-testing-planner** | test-engineer | Creates phased test implementation plans from research findings |
| **code-testing-implementer** | test-engineer | Implements one phase from the plan, runs build-test-fix cycles |
| **code-testing-builder** | code-testing-implementer | Runs build/compile commands and reports results |
| **code-testing-tester** | code-testing-implementer | Runs test commands and reports pass/fail results |
| **code-testing-fixer** | code-testing-implementer | Fixes compilation errors in source or test files |
| **code-testing-linter** | code-testing-implementer | Runs code formatting and linting |

> **VS Code — optional nested delegation:** The pipeline does not require
> phase-agent fan-out. When substantial work warrants a subagent invoking
> another available named agent, VS Code gates that nested delegation behind a
> setting that is **off by default**. To allow it, enable:
>
> ```jsonc
> "chat.subagents.allowInvocationsFromSubagents": true
> ```
>
> Without it, `test-engineer` or a delegated implementer completes the phases
> inline; required validation and review are unchanged. The GitHub Copilot CLI
> has no such gate, but delegation remains optional.

## Prerequisites

### For polyglot skills and agents

The `test-engineer` agent, `code-testing` skill, generation workers, internal
quality auditor, and six test-analysis skills (`test-anti-patterns`,
`test-smell-detection`, `assertion-quality`, `test-gap-analysis`,
`test-tagging`, `grade-tests`) work with any supported language above. You just
need a working test runtime for the target language (for example `pytest`,
`npm test`, `mvn`, `go`, `cargo test`, Pester, or CMake plus a C++ test runner).

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
