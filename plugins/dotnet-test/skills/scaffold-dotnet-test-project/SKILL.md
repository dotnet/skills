---
name: scaffold-dotnet-test-project
description: >-
  Create the first .NET test project. USE FOR: "solution has no tests", first
  xUnit tests, Tests.csproj/ProjectReference, .sln/.slnx/.slnf registration,
  central packages, no solution, or tests missing from CI. DO NOT USE FOR: a
  suitable existing project (stop; use code-testing-agent) or migration.
license: MIT
---

# Scaffold a .NET Test Project

Create the smallest test project that fits the repository's existing build and
test conventions, wire it to the correct production project and build entry
point, and prove solution-level test discovery sees it. This skill scaffolds the
container for tests; it does not invent a solution-wide test architecture.

## When to Use

- A .NET solution or project has production code but no suitable test project.
- A user asks to "set up tests" or "add a test project" from a vague starting point.
- Tests pass when the new `.csproj` is targeted directly but CI cannot discover it.
- A multi-project solution needs a bounded test project for one production project.

## When Not to Use

- A compatible test project already references the target production project.
  Reuse it and continue with `code-testing-agent`.
- The request is specifically about authoring or modernizing MSTest test code.
  Use `writing-mstest-tests` after the project exists.
- The repository's current build is broken for an unrelated reason. Report that
  blocker; do not redesign project structure to hide it.
- The user asks to migrate xUnit, NUnit, MSTest, TUnit, VSTest, or MTP.

## Inputs

| Input | Required | Description |
|-------|----------|-------------|
| Repository or solution path | No | Discover from the current workspace when omitted |
| Production project | No | Infer the narrowest project in the requested scope |
| Test framework | No | Use an explicit choice; otherwise infer repository convention |
| Build entry point | No | Existing `.sln`, `.slnx`, `.slnf`, or project graph used by CI |

## Workflow

### Step 1: Establish the repository contract

Inspect only the files needed to answer these questions:

1. Which production project is in scope?
2. What command does CI or the repository use to build and test?
3. Does a suitable test project already reference that production project?
4. Which test framework and runner do neighboring test projects use?
5. Are package versions centrally managed by `Directory.Packages.props`,
   `Directory.Build.props`, `global.json`, or an MSBuild SDK declaration?
6. Which target framework(s) must the test project compile against?

Treat a test project as suitable only when its target framework can reference the
production project and its purpose matches the requested layer. Do not create a
second test project merely because its name differs from your preferred name.

**No-op stop condition:** when a suitable project already exists and is registered
in the requested build entry point, do not repair, normalize, convert, or replace
the solution. If the user did not ask for tests yet, report the existing project
path and stop with the workspace byte-for-byte unchanged.

### Step 2: Choose one bounded project

Default to one test project per production project, named according to repository
convention (`Foo.Tests`, `Foo.UnitTests`, and so on). For a vague multi-project
request, start with the project that owns the user-visible behavior or has the
highest-value untested logic; do not create one test project per source project
without evidence that the repository wants that layout.

Match, in order:

1. The user's explicit framework choice.
2. Existing test projects in the same repository.
3. Repository-wide package/SDK conventions.
4. A standard SDK template only when the repository provides no convention.

Never mix frameworks in one test project. Never add package versions directly
when central package management supplies them.

### Step 3: Scaffold and reference the target

Use the matching `dotnet new` template (`xunit`, `nunit`, or `mstest`) rather than
hand-writing template boilerplate. Then make only the repository-specific edits:

1. Align target framework, nullable, implicit-usings, runner, and package style.
2. Add a `ProjectReference` to each production project directly exercised by
   the planned tests. Do not reference every project in the solution.
3. Remove template sample tests that do not test repository behavior.

For xUnit v3 projects that the repository runs through `dotnet test`, preserve or
add both:

```xml
<OutputType>Exe</OutputType>
<TestingPlatformDotnetTestSupport>true</TestingPlatformDotnetTestSupport>
```

`OutputType=Exe` alone makes the self-hosted runner work with `dotnet run`; it is
not evidence that the repository's `dotnet test` command can discover the tests.

Prefer `dotnet add <test-project> reference <production-project>` for project
references. Inspect the resulting project file before continuing.

### Step 4: Register with the real build entry point

Creating a `.csproj` is not enough. Add it to the exact solution artifact used by
the repository:

- `.sln` or `.slnx`: `dotnet sln <solution> add <test-project>`
- `.slnf`: add the project to the underlying solution and include it in the
  filter used by the requested/CI test command.
- No solution artifact: keep the existing project-oriented workflow. Do not
  create a solution solely for aesthetics unless the user asked for one.

Do not substitute a different solution file because it is easier to edit.

### Step 5: Add a real smoke test

Replace template examples with the **smallest smoke suite requested**. The tests
must:

- instantiate or invoke a real symbol from the referenced production project;
- assert a concrete result, not only non-null/truthiness;
- avoid network, wall-clock, process, and real filesystem dependencies.

These tests prove the project reference and discovery path. Stop after every
behavior explicitly named by the user is covered; extra boundary permutations
are out of scope here and belong to `code-testing-agent`.

### Step 6: Verify direct and harness-level execution

Run, in this order:

1. `dotnet test <test-project>` to isolate scaffolding failures.
2. The repository's solution/root test command to prove CI discovery.
3. A solution/project listing command to confirm the new project is registered.

If the direct command passes but the harness-level command discovers no new
test, the scaffolding is incomplete. Fix registration before reporting success.
Do not claim success from `dotnet build` alone.

## Output Contract

Report a compact table:

| Requirement | Evidence |
|-------------|----------|
| Test project created/reused | Project path |
| Production reference | Referenced `.csproj` path |
| Build registration | `.sln`/`.slnx`/`.slnf` entry or project-oriented command |
| Test discovery | Passing harness-level command and discovered test |

If validation is blocked, report the exact failing command and first actionable
error. Do not describe an unrun command as successful.

## Validation

- [ ] A suitable existing test project was ruled out before creating another.
- [ ] Framework, runner, target framework, and package style match the repository.
- [ ] Project references cover only the production projects under test.
- [ ] Central package management was preserved.
- [ ] Template sample tests were removed.
- [ ] At least one real deterministic test asserts a concrete behavior.
- [ ] The test project passes directly.
- [ ] The repository's solution/root command discovers and runs the new test.

## Common Pitfalls

| Pitfall | Corrective action |
|---------|-------------------|
| Creating a project that CI never sees | Register it with the exact solution/filter used by CI and run that command |
| Picking a favorite framework | Infer the repository convention before using a default |
| Adding package versions under CPM | Add versionless references and keep versions in `Directory.Packages.props` |
| Referencing the whole solution | Reference only projects whose APIs the tests compile against |
| Keeping `UnitTest1` | Replace it with a concrete test of repository behavior |
| Creating parallel unit/integration projects from a vague ask | Start with one bounded project; expand only for a demonstrated boundary |
| Treating a green build as test discovery | Run the harness-level test command and observe the test |
