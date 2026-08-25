---
name: code-testing-agent
description: >-
  Generate or add unit tests for existing code. ALWAYS USE for "add tests for
  this one function/method/class/file", "write focused tests", "cover this
  untested method", first test methods after project wiring exists, or a
  comprehensive project-wide suite. Polyglot; handles classic packages.config
  MSTest and sparse workspaces. DO NOT USE for .NET test-project,
  ProjectReference, solution, or filter wiring (scaffold-dotnet-test-project);
  only running tests, coverage/audits, migrations, or correcting supplied MSTest
  assertions, attributes, lifecycle, or configuration without designing new
  cases (writing-mstest-tests).
license: MIT
---

# Generate Tests for Existing Code

Add the smallest test suite that proves the requested behavior. Scale discovery,
planning, and delegation to the request instead of running the same pipeline for
every task.

## Route and Size the Request

Confirm that the test project or framework is already usable. For .NET, if the
test project, production `ProjectReference`, `.sln`, `.slnx`, or `.slnf` wiring
is missing, use `scaffold-dotnet-test-project` first.

Choose one execution path:

| Scope | Signals | Execution |
|---|---|---|
| Focused | One function, method, class, file, or explicitly named missing cases | Work inline. Read the target and one representative neighboring test, add only the requested cases, and run the narrowest test command. |
| Broad | An entire project/package, several modules, comprehensive coverage, or a threshold across multiple files | Build a bounded inventory and acceptance checklist, implement in coherent phases, then run one final workspace-level validation. |

A sparse project-wide request remains broad even when only one source module is
currently present. A focused request remains focused even when the repository is
large.

## Establish the Test Contract

Read only enough to determine:

1. the exact production scope and every behavior named by the user;
2. the existing test framework, versions, layout, naming, and assertion style;
3. the narrow and final validation commands;
4. seams for collaborators, time, randomness, environment, network, and files;
5. language- or project-system constraints that affect compilation or discovery.

Treat the current workspace as authoritative, including sparse, synthetic, or
gutted-looking worktrees. Never restore or reconstruct files with `git checkout`,
`git restore`, `git reset`, `git clean`, or equivalent commands.

If there is no representative test, inspect the framework configuration and use
the repository's existing dependencies. Load language reference guidance only
when an API or convention remains uncertain.

## Plan Against Observable Behavior

Turn each explicit requirement into a compact checklist. For broad work, also
inventory each production target once and map it to a planned test file. Keep
this planning inline unless the user asks for a saved plan or the repository
already uses a planning artifact.

Prioritize cases that distinguish plausible bugs:

- normal results with concrete expected values;
- validation and error paths with the precise exception or error contract;
- numeric, collection, and state-transition boundaries;
- collaborator interactions, error propagation, and short-circuit behavior;
- combined properties when the request names an interaction between them.

Coverage is supporting evidence, not a substitute for requested behavior. Do not
inflate test counts with redundant permutations.

## Implement Proportionally

Follow existing framework and project conventions. Reuse test helpers and
fixtures when they clarify intent; avoid introducing abstractions for a handful
of tests.

Each test must:

- invoke a real production symbol;
- fail under a plausible defect in the behavior it protects;
- assert concrete outcomes or interactions rather than existence or truthiness;
- avoid network, real filesystem, wall-clock, process, or nondeterministic state
  unless the user explicitly requests an integration test;
- use realistic, non-degenerate inputs.

Do not change production code merely to make an incorrect expectation pass. If
the requested behavior exposes a production defect, prove the defect, make only
the necessary production fix when it is in scope, and rerun the relevant tests.

### Classic non-SDK .NET

Preserve `packages.config`, target framework, project format, pinned framework
and mock versions, custom base fixtures, and the repository's MSBuild/test-runner
commands. Add every new source file to explicit `<Compile Include>` items. Use
APIs available in the pinned test-framework version; do not modernize the stack
as a side effect of adding tests.

## Control Delegation and Cost

Focused work stays inline and must not create `.testagent/` process artifacts or
fan out to agents.

For broad work, invoke `code-testing-generator` once only when several
independent modules make delegation materially useful. Otherwise execute the
inventory, implementation, and review inline. Do not load
`find-untested-sources`, `test-gap-analysis`, `assertion-quality`, or language
extensions merely to satisfy this workflow. Use at most the one helper that
resolves a concrete discovery, compatibility, or quality gap.

Do not repeat repository-wide discovery in each phase. Cache the target list,
test the active target during fix cycles, and reserve the full command for the
end.

## Verify Before Completion

1. Run the narrowest command covering the changed tests.
2. Fix compile failures and incorrect expectations; never skip or ignore tests
   just to make the run green.
3. For broad work, run the repository-level command once after narrow checks
   pass.
4. Re-open the generated tests and verify every user requirement against a
   concrete test name and assertion.
5. If a coverage threshold was requested, cite only a successful coverage run
   that clears it.

If validation is blocked, report the exact command and first actionable error.
Never describe an unrun, failed, or partial command as successful.

## Handoff

Keep the response proportional. State the changed test files, the important
behaviors and exact test names that prove them, and the final passing command.
Use a table only when several independent requirements are easier to audit that
way; no fixed heading or verbatim restatement is required.
