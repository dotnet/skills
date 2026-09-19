---
name: code-testing-agent
description: >-
  ALWAYS USE whenever asked to write, add, or generate unit tests for existing
  code, including one helper, function, class, or missing regression case as well
  as project-wide suites. Also use for "cover this untested method", scaffolding
  tests where none exist, sparse workspaces, classic packages.config MSTest, and
  extending healthy suites. Focused requests use a proportional direct workflow;
  broad requests use the full pipeline. DO NOT USE for only running/diagnosing
  tests, coverage/audits, a test blocked on a missing production seam
  (testability-obstacle), or correcting supplied MSTest assertions, attributes,
  lifecycle, or configuration without designing new cases (writing-mstest-tests).
license: MIT
---

# Code Testing Generation Skill

An AI-powered skill that generates comprehensive, workable unit tests for any programming language using a coordinated multi-agent pipeline.

## Non-negotiable execution contract

Classify scope **before editing**:

- **Broad** (a project/package-wide suite, or multiple production
  files/modules): create `research.md` and `plan.md` in a resolved
  non-stageable `<TESTAGENT_DIR>` before implementation, then `status.md` there
  after the final test-quality review. If these files are absent, the broad
  workflow is incomplete.
- **Focused** (the user explicitly limits work to one function/class/file or one
  missing method): do not create intermediate state files or fan out to multiple
  agents. A sparse project-wide request remains broad even when only one source
  module is present.

For either scope, run the narrowest relevant test command to a clean exit and
finish with a compact `Requirement | Evidence` table. Each requested behavior
must cite an exact test name; validation rows cite the successful command.
For focused work, "no intermediate state files" changes only the process, not the
final evidence contract.

Intermediate state files are internal working data, never deliverables. Keep
`<TESTAGENT_DIR>` non-stageable, never place it or its files in
version-controlled workspace content, and never modify `.gitignore` to hide
them.

Treat completeness as a requirement matrix, not a test-count target. Give every
independently requested state, boundary, error path, or interaction its own
concrete assertion. Combine cases only when one execution genuinely proves the
whole requested combination; do not let a parameterized happy-path case stand in
for an empty state, invalid discriminator, or before/at/after boundary.
For broad requests that name several production modules or layers, give each
named module direct tests for its non-trivial public behavior. Cross-module tests
prove composition, but do not substitute for the requested module-level
coverage. Judge breadth by the behavior matrix, never by matching or exceeding a
raw test count.

For a **broad or comprehensive** request, the explicit matrix is the floor, not
the ceiling. After satisfying it, inspect each target API for observable
equivalence partitions and invariants that the prompt did not name: identity,
empty, singleton and representative interior inputs; exact boundaries plus an
immediately adjacent value; invalid partitions; and ordering, monotonicity,
rollover, capacity, truncation, or state invariants implied by the implementation.
Add one mutation-relevant case per distinct partition not already proved, using
parameterized or table-driven cases for siblings. Stop when remaining inputs
exercise the same branch and invariant, not merely when the explicit checklist
is complete; never add cases only to raise the count.

## When to Use This Skill

Use this skill when you need to:

- Generate unit tests for an entire project or specific files
- Improve test coverage for existing codebases
- Create test files that follow project conventions
- Write tests that actually compile and pass
- Add tests for new features or untested code
- Generate or extend MSTest suites; load `writing-mstest-tests` as supporting
  guidance after this entry skill has established scope and project conventions

## When Not to Use

- Running or executing existing tests (use the `run-tests` skill)
- Migrating between test frameworks (use migration skills)
- Answering an MSTest API/pattern or modernization question that does not ask to
  generate tests (use `writing-mstest-tests`)
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

### Step 2: Size the request before invoking anything

Match the machinery to the scope. Running the full pipeline on a one-file
request costs turns and tool calls without improving the tests.

| Scope | What it looks like | How to run it |
| --- | --- | --- |
| **Focused** | One function, class, or file; "tests for X only"; extending an existing suite with the missing cases | Skip intermediate state files and the sub-agent fan-out. Keep the requirement checklist in your head (or in the final table), read only the target and one neighbouring test for conventions, write the tests, run the narrowest test command, review your own assertions inline. |
| **Broad** | A project, package, or module set; "comprehensive suite"; a coverage threshold to clear across several files | Run the full Research → Plan → Implement pipeline in Step 3, with intermediate state files under `<TESTAGENT_DIR>` and the completion contract below. |

When in doubt, start focused and escalate only if the request turns out to span
several files. Escalating costs one extra pass; running the broad pipeline on a
focused request costs several.

Before ending a focused request, check all three conditions together:

1. every named behavior has a concrete assertion, including each requested
   boundary or error path;
2. the narrow test command exited successfully;
3. the final `Requirement | Evidence` table maps those behaviors to exact test
   names and cites that successful command.

Do not replace this table with a prose list of covered areas, even for a
single-function request.

### Step 3: Invoke the Test Generator (broad scope)

Start by calling the `code-testing-generator` agent with your test generation request:

```text
Generate unit tests for [path or description of what to test], following the [unit-test-generation.prompt.md](unit-test-generation.prompt.md) guidelines. Treat the current workspace as authoritative even when it is sparse, gutted-looking, synthetic, or missing tracked files; never restore or reconstruct it, including with `git checkout`, `git restore`, `git reset`, or `git clean`.
```

The Test Generator will manage the entire pipeline automatically.

If `code-testing-generator` is unavailable, do not skip the workflow. Execute the
same Research → Plan → Implement sequence inline, resolve `<TESTAGENT_DIR>` as
described below, create the intermediate state files there, and apply the same
completion contract.

For broad scope, resolve one absolute `<TESTAGENT_DIR>` before creating
intermediate state files:

1. Prefer a host-provided session artifact or scratch directory.
2. Otherwise, in a Git worktree run
   `git rev-parse --path-format=absolute --git-path testagent`; this returns a
   path in worktree-specific Git metadata that cannot be staged.
3. Outside Git, create a unique directory under the operating system's
   temporary directory.

Pass the absolute directory to every pipeline agent. The path may be inside the
repository's `.git` metadata directory, but it must not be version-controlled
workspace content, appear in `git status`, or be stageable.

### Step 4: Execute with bounded context

For multi-file requests:

1. Turn every explicit user requirement into a checklist before implementation. Include requested layers, collaborators to mock, boundary cases, integrations, coverage thresholds, and report artifacts. Copy multi-condition requirements verbatim — they must each map to one test that exercises the whole combination.
2. Research only the requested module or project and write the checklist plus a compact target inventory to `<TESTAGENT_DIR>/research.md`.
3. Reuse manifests, symbol references, and deterministic pairing tools instead of reading every source and test file.
4. For multi-file scopes in C#, Python, TypeScript/JavaScript, Go, Java, Rust, Ruby, Kotlin, Swift, PowerShell, or C++, run `find-untested-sources` once and consume its pairing and suggested-path output; do not repeat that discovery manually.
5. Plan each target file once, then implement phases sequentially. Map every checklist item to at least one concrete test or explain why it is blocked.
6. Build and test the narrow target during fix cycles; run workspace-level validation once at the end.
7. Before reporting success, re-open the generated tests and verify every checklist item against concrete test names and assertions. Coverage alone is not evidence that a requested mock seam, boundary, state transition, or property combination was tested.
8. Read a language example from `code-testing-extensions` only when the repository has no representative tests and the base extension is insufficient.
9. For .NET, classify SDK-style vs. classic non-SDK before choosing commands or creating files. In classic projects, preserve `packages.config`, existing framework/mock versions and custom base fixtures, add every new test file to the project's explicit `<Compile Include>` items, and use the repository's MSBuild/test-runner commands. Never modernize the project or dependency stack merely to generate tests.
10. For MSTest, inspect the pinned package version before choosing exception
    assertions. MSTest 3.5.x uses `Assert.ThrowsException<T>`; do not substitute
    `[ExpectedException]`, `Assert.Throws<T>`, or `Assert.ThrowsExactly<T>`.

### Completion contract

Every scope must satisfy points 3–5 below. Points 1 and 2 are the **broad-scope**
artifacts: on a focused request the same reasoning happens inline and no
intermediate state files are written.

Do not report completion until all of these are true:

1. *(broad scope)* `<TESTAGENT_DIR>/research.md` records the bounded target
   inventory, existing test conventions, and the acceptance checklist.
2. *(broad scope)* `<TESTAGENT_DIR>/plan.md` maps each checklist item to a planned
   test or an explicit blocker.
3. Generated tests compile and pass with the narrowest relevant test command.
4. Every explicit user requirement is backed by a concrete test and assertion.
   Fix missing mock seams, boundary cases, state transitions, and property
   combinations even when coverage already passes. In the final summary, cite
   at least one generated test name for every checklist item so completion is
   auditable; if an item has no test to cite, keep implementing or report it as
   blocked. For non-behavioral requirements such as scaffolding, scope limits,
   commands, or coverage artifacts, cite the relevant file, command, or report
   instead of forcing a test-name mapping.
   A passing suite with fewer tests is not automatically weaker: judge
   completeness by whether every independently requested behavior has direct,
   nonredundant evidence, not by raw test volume.
   For broad/comprehensive scope, also verify that every observable equivalence
   partition and invariant discovered in the bounded target APIs has one
   mutation-relevant case, even when the prompt did not name it.
   When the request names multiple modules, verify that each module's own
   non-trivial public behavior has direct test evidence in addition to any
   end-to-end composition test.
5. Review the generated tests for behavior gaps and weak assertions. On a broad
   scope, invoke `test-gap-analysis` and `assertion-quality` when available and
   record the findings and fixes in `<TESTAGENT_DIR>/status.md`. On a focused scope,
   do the equivalent review inline — re-read each generated assertion against
   the source — without spawning extra passes.

The final response MUST include a compact `Requirement | Evidence` table.
Behavioral rows cite exact generated test names. Non-behavioral rows cite the
relevant project file, validation command, or coverage report. A generic list
of tested areas is not a substitute for requirement-by-requirement evidence.

**Quote the user's requirement verbatim in each row.** When the request names a
specific combination — "a case where a composite discount, regional tax, and
weight-based shipping all apply", "the difference between summed and chained
discounts", "constructor validation for every class" — the row must cite the one
test that demonstrates exactly that. A test that merely exercises the same
collaborators does not satisfy a requirement about their interaction, and
per-class requirements need a citation per class.

**Cite a clean run, not an attempt.** The commands behind the evidence table must
have finished successfully: quote the final passing test summary and, when
thresholds were requested, the per-module coverage table from a run that exited
0. If the last coverage run exited non-zero, fix it and re-run before reporting;
never infer threshold clearance from a failed or partial run.

Before reporting, inspect the final working-tree changes and confirm that
`research.md`, `plan.md`, `status.md`, and any other intermediate state files are
not among the changes intended for commit.

## State Management

Broad-scope runs store intermediate state files in a non-stageable
`<TESTAGENT_DIR>` backed by host scratch storage, Git metadata, or OS temp. A
focused request does not create these files:

| File                     | Purpose                      |
| ------------------------ | ---------------------------- |
| `<TESTAGENT_DIR>/research.md` | Codebase analysis results    |
| `<TESTAGENT_DIR>/plan.md`     | Phased implementation plan   |
| `<TESTAGENT_DIR>/status.md`   | Final quality review and fixes |

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

Classic non-SDK .NET projects are supported when their existing build/test
toolchain is available. When it is not available on the current machine, the
agent can still add and register version-compatible tests, but must report
execution as blocked rather than substituting `dotnet test`.

## Troubleshooting

### Tests don't compile

The `code-testing-fixer` agent will attempt to resolve compilation errors. Check
`<TESTAGENT_DIR>/plan.md` for the expected test structure. Call the
`code-testing-extensions` skill and read the language-specific extension file
for error code references (e.g., `dotnet.md` for .NET).

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
