---
name: scaffold-dotnet-test-project
description: >-
  Create or repair .NET test-project wiring. ALWAYS USE for "set up/add a test
  project", missing Tests.csproj/ProjectReference, "tests pass directly but
  CI/solution discovers no tests", or an existing test project omitted from
  .sln/.slnx/.slnf. Handles xUnit/NUnit/MSTest and central packages. DO NOT USE
  to only write tests in an already-wired project (code-testing-agent), run
  tests, or migrate.
license: MIT
---

# Scaffold or Repair a .NET Test Project

Create the smallest missing test container or repair only the missing wiring.
The goal is test discovery through the repository's real build entry point, not
a preferred solution layout.

## Route the Request

Inspect the repository before editing, then choose exactly one path:

| Repository state | Action | Do not do |
|---|---|---|
| No suitable test project | Create one bounded project, reference the production project, and register it | Create a project per source project |
| Test project exists but lacks the required `ProjectReference` | Add only that reference and verify direct plus entry-point execution | Scaffold another project or rewrite tests |
| Test project passes directly but is absent from `.sln`, `.slnx`, or `.slnf` | Register the existing project in the exact entry point CI uses | Recreate the project or switch solution formats |
| Suitable project, reference, and requested entry point are already correct | Leave the workspace unchanged; use `code-testing-agent` if test methods are requested | Normalize or replace working files |

An existing project is suitable when its target framework can reference the
production project and its purpose matches the requested layer. A different
preferred name is not a reason to create a duplicate.

## Workflow

### 1. Establish the repository contract

Read only enough to determine:

1. the production project and requested test scope;
2. the command and `.sln`, `.slnx`, `.slnf`, or project graph used by CI;
3. whether a suitable test project exists, what it references, and where it is
   registered;
4. the neighboring test framework, runner, target framework, nullable and
   implicit-usings conventions; and
5. whether package or SDK versions come from `Directory.Packages.props`,
   `Directory.Build.props`, `global.json`, or an MSBuild SDK declaration.

If the user reports that a test project passes directly but solution-level
discovery finds nothing, treat that as registration evidence. Inspect the entry
point before considering project creation.

### 2. Create only when the project is absent

Choose one test project for the narrowest requested production project. Follow,
in order, the user's explicit framework choice, neighboring test projects,
repository-wide package/SDK conventions, then a standard SDK template.

Use the matching `dotnet new` template instead of hand-writing boilerplate.
Then:

1. align target framework, nullable, implicit usings, runner, and package style;
2. use `dotnet add <test-project> reference <production-project>` for only the
   production projects exercised by the requested tests;
3. remove template sample tests; and
4. omit package versions when central package management supplies them.

For xUnit v3 projects that run through `dotnet test`, preserve or add:

```xml
<OutputType>Exe</OutputType>
<TestingPlatformDotnetTestSupport>true</TestingPlatformDotnetTestSupport>
```

`OutputType=Exe` alone proves only the self-hosted runner path, not discovery by
the repository's `dotnet test` command.

### 3. Repair only the missing edge when the project exists

- Missing production reference: use `dotnet add <test-project> reference
  <production-project>`, inspect the resulting project, and leave package and
  test source files unchanged.
- Missing `.sln` or `.slnx` registration: run `dotnet sln <entry-point> add
  <test-project>`.
- Missing `.slnf` registration: add the existing project to its underlying
  solution if necessary, then include that same project path in the filter.
- Multiple solution artifacts: modify only the one named by the user or invoked
  by CI. Do not substitute an easier format.
- No solution artifact: preserve the existing project-oriented workflow. Do not
  create a solution for aesthetics.

### 4. Add only the requested smoke behavior

For a newly created project, replace template examples with the smallest smoke
suite the user requested. Each test must invoke a real production symbol and
assert a concrete deterministic result without network, wall-clock, process, or
real-filesystem dependencies.

For an existing-project wiring repair, do not add, rewrite, rename, or expand
tests unless the user explicitly asks for test behavior changes. Registration
and test authoring are separate operations.

### 5. Verify the repaired path

Run the narrowest commands that prove the chosen route:

1. `dotnet test <test-project>` to prove the project and reference;
2. the repository's exact solution/root command to prove discovery; and
3. `dotnet sln <entry-point> list` or the equivalent filter inspection to prove
   registration.

For a no-op, inspect rather than rewrite and report the existing paths. A green
`dotnet build` is not test-discovery evidence. If validation is blocked, report
the exact failing command and first actionable error; never describe an unrun or
failed command as successful.

## Output

Keep the handoff proportional to the change:

| Requirement | Evidence |
|---|---|
| Project created, reused, or repaired | Test project path and chosen route |
| Production reference | Referenced `.csproj`, or why no change was needed |
| Build registration | Exact `.sln`/`.slnx`/`.slnf` entry or project workflow |
| Test discovery | Passing harness-level command and discovered test |

## Completion Checks

- Existing projects were checked before creation.
- Only the requested production scope is referenced and tested.
- Framework, runner, target framework, and central package conventions remain
  intact.
- Template samples are removed from a newly created project.
- Existing test code is untouched for a wiring-only repair.
- The exact repository entry point discovers and runs the test project.
