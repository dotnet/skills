---
name: code-testing-agent
description: >-
  Generates and writes new unit tests for any programming language —
  scaffolds test projects and configures coverage tooling
  (coverlet, pytest-cov, @vitest/coverage-v8) as part of test
  generation. Use when asked to generate tests, generate pytest
  tests, generate Vitest tests, write unit tests, add tests, improve
  coverage, comprehensive tests, or scaffold a new test project or
  suite for an app, service, library, REST API, blueprint, or
  package — including project-wide, multi-file test generation
  across services, repositories, routes, and modules. Supports
  C#/.NET, Python (pytest, Flask/Django), TypeScript/JavaScript
  (Vitest, Jest, Mocha), Go, Rust, Java (JUnit). Runs a research,
  planning, and implementation pipeline so tests compile and pass.
  DO NOT USE FOR: running existing tests (use run-tests); analyzing
  existing coverage reports (use coverage-analysis or crap-score);
  writing, fixing, or modernizing MSTest-specific tests, assertions,
  attributes, or lifecycle (use writing-mstest-tests).
license: MIT
---

# Code Testing Generation Skill

An AI-powered skill that generates comprehensive, workable unit tests for any programming language using a coordinated multi-agent pipeline.

## When to Use This Skill

Use this skill when you need to:

- Generate unit tests for an entire project or specific files
- Improve test coverage for existing codebases
- Create test files that follow project conventions
- Write tests that actually compile and pass
- Add tests for new features or untested code

## When Not to Use

- Running or executing existing tests (use the `run-tests` skill)
- Migrating between test frameworks (use migration skills)
- Writing tests specifically for MSTest patterns (use `writing-mstest-tests`)
- Debugging failing test logic

## How It Works

This skill coordinates multiple specialized agents in a **Research → Plan → Implement** pipeline:

### Pipeline Overview

```text
┌─────────────────────────────────────────────────────────────┐
│                     TEST GENERATOR                          │
│  Coordinates the full pipeline and manages state            │
└─────────────────────┬───────────────────────────────────────┘
                      │
        ┌─────────────┼─────────────┐
        ▼             ▼             ▼
┌───────────┐  ┌───────────┐  ┌───────────────┐
│ RESEARCHER│  │  PLANNER  │  │  IMPLEMENTER  │
│           │  │           │  │               │
│ Analyzes  │  │ Creates   │  │ Writes tests  │
│ codebase  │→ │ phased    │→ │ per phase     │
│           │  │ plan      │  │               │
└───────────┘  └───────────┘  └───────┬───────┘
                                      │
                    ┌─────────┬───────┼───────────┐
                    ▼         ▼       ▼           ▼
              ┌─────────┐ ┌───────┐ ┌───────┐ ┌───────┐
              │ BUILDER │ │TESTER │ │ FIXER │ │LINTER │
              │         │ │       │ │       │ │       │
              │ Compiles│ │ Runs  │ │ Fixes │ │Formats│
              │ code    │ │ tests │ │ errors│ │ code  │
              └─────────┘ └───────┘ └───────┘ └───────┘
```

## Step-by-Step Instructions

### Step 1: Determine the user request

Make sure you understand what user is asking and for what scope.
When the user does not express strong requirements for test style, coverage goals, or conventions, source the guidelines from [unit-test-generation.prompt.md](unit-test-generation.prompt.md). This prompt provides best practices for discovering conventions, parameterization strategies, coverage goals (aim for 80%), and language-specific patterns.

### Step 2: Invoke the Test Generator

Start by calling the `code-testing-generator` agent with your test generation request:

```text
Generate unit tests for [path or description of what to test], following the [unit-test-generation.prompt.md](unit-test-generation.prompt.md) guidelines
```

The Test Generator will manage the entire pipeline automatically.

### Step 3: Execute with bounded context

For multi-file requests:

1. Research only the requested module or project and write a compact `.testagent/research.md`.
2. Reuse manifests, symbol references, and deterministic pairing tools instead of reading every source and test file.
3. For C# multi-file scopes, run `find-untested-sources` once and consume its `source_to_tests`, `untested`, and `suggested_test_path` output; do not repeat that pairing manually.
4. Plan each target file once, then implement phases sequentially.
5. Build and test the narrow target during fix cycles; run workspace-level validation once at the end.
6. Read a language example from `code-testing-extensions` only when the repository has no representative tests and the base extension is insufficient.

## State Management

All pipeline state is stored in `.testagent/` folder:

| File                     | Purpose                      |
| ------------------------ | ---------------------------- |
| `.testagent/research.md` | Codebase analysis results    |
| `.testagent/plan.md`     | Phased implementation plan   |
| `.testagent/status.md`   | Progress tracking (optional) |

## Agent Reference

| Agent                      | Purpose              |
| -------------------------- | -------------------- |
| `code-testing-generator`   | Coordinates pipeline |
| `code-testing-researcher`  | Analyzes codebase    |
| `code-testing-planner`     | Creates test plan    |
| `code-testing-implementer` | Writes test files    |
| `code-testing-builder`     | Compiles code        |
| `code-testing-tester`      | Runs tests           |
| `code-testing-fixer`       | Fixes errors         |
| `code-testing-linter`      | Formats code         |

## Requirements

- Project must have a build/test system configured
- Testing framework should be installed (or installable)
- VS Code with GitHub Copilot extension

## Troubleshooting

### Tests don't compile

The `code-testing-fixer` agent will attempt to resolve compilation errors. Check `.testagent/plan.md` for the expected test structure. Call the `code-testing-extensions` skill and read the language-specific extension file for error code references (e.g., `dotnet.md` for .NET).

### Tests fail

Most failures in generated tests are caused by **wrong expected values in assertions**, not production code bugs:

1. Read the actual test output
2. Read the production code to understand correct behavior
3. Fix the assertion, not the production code
4. Never mark tests `[Ignore]` or `[Skip]` just to make them pass

### Wrong testing framework detected

Specify your preferred framework in the initial request: "Generate Jest tests for..."

### Environment-dependent tests fail

Tests that depend on external services, network endpoints, specific ports, or precise timing will fail in CI environments. Focus on unit tests with mocked dependencies instead.

### Build fails on full solution

During phase implementation, build only the specific test project for speed. After all phases, run a full non-incremental workspace build to catch cross-project errors.
