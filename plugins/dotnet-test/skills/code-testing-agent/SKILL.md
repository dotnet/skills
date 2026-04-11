---
name: code-testing-agent
description: >-
  Generates comprehensive, workable unit tests for any programming language
  using a multi-agent pipeline. Use when asked to generate tests, write unit
  tests, improve test coverage, add test coverage, create test files, or test a
  codebase. Supports C#, TypeScript, JavaScript, Python, Go, Rust, Java, and
  more. Orchestrates research, planning, and implementation phases to produce
  tests that compile, pass, and follow project conventions.
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

### Step 3: Research Phase (Automatic)

The `code-testing-researcher` agent analyzes your codebase to understand:

- **Language & Framework**: Detects C#, TypeScript, Python, Go, Rust, Java, etc.
- **Testing Framework**: Identifies MSTest, xUnit, Jest, pytest, go test, etc.
- **Project Structure**: Maps source files, existing tests, and dependencies
- **Build Commands**: Discovers how to build and test the project

Output: `.testagent/research.md`

### Step 4: Planning Phase (Automatic)

The `code-testing-planner` agent creates a structured implementation plan:

- Groups files into logical phases (2-5 phases typical)
- Prioritizes by complexity and dependencies
- Specifies test cases for each file
- Defines success criteria per phase

Output: `.testagent/plan.md`

### Step 5: Implementation Phase (Automatic)

The `code-testing-implementer` agent executes each phase sequentially:

1. **Read** source files to understand the API
2. **Write** test files following project patterns
3. **Build** using the `code-testing-builder` sub-agent to verify compilation
4. **Test** using the `code-testing-tester` sub-agent to verify tests pass
5. **Fix** using the `code-testing-fixer` sub-agent if errors occur
6. **Lint** using the `code-testing-linter` sub-agent for code formatting

Each phase completes before the next begins, ensuring incremental progress.

### Coverage Types

- **Happy path**: Valid inputs produce expected outputs
- **Edge cases**: Empty values, boundaries, special characters
- **Error cases**: Invalid inputs, null handling, exceptions

## State Management

All pipeline state is stored in `.testagent/` folder:

| File                     | Purpose                      |
| ------------------------ | ---------------------------- |
| `.testagent/research.md` | Codebase analysis results    |
| `.testagent/plan.md`     | Phased implementation plan   |
| `.testagent/status.md`   | Progress tracking (optional) |

## Examples

### Example 1: Full Project Testing

```text
Generate unit tests for my Calculator project at C:\src\Calculator
```

### Example 2: Specific File Testing

```text
Generate unit tests for src/services/UserService.ts
```

### Example 3: Targeted Coverage

```text
Add tests for the authentication module with focus on edge cases
```

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

The `code-testing-fixer` agent will attempt to resolve compilation errors. Check `.testagent/plan.md` for the expected test structure. Check the `extensions/` folder for language-specific error code references (e.g., `extensions/dotnet.md` for .NET).

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
