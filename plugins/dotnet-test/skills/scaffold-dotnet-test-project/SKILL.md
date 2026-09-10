---
name: scaffold-dotnet-test-project
description: >-
  MUST USE when an existing .NET test project was excluded from a .slnf/CI
  solution filter, disappeared from .sln/.slnx discovery, or lost its production
  ProjectReference; also for requests to set up, create, reuse, add, register,
  include, or repair a test project. Handles "tests pass directly but CI
  discovers zero", exact solution wiring, xUnit/NUnit/MSTest, and central
  packages. DO NOT USE to only author tests in an already-wired project
  (code-testing-agent), run tests, migrate, or correct MSTest syntax/configuration
  without changing project or CI files (writing-mstest-tests).
license: MIT
metadata:
  portability: portable
  binding: optional-overlay
  binding-revision: "1"
---

# Scaffold or Repair a .NET Test Project

Create the smallest missing test container or repair only the missing wiring.
The goal is test discovery through the repository's real build entry point, not
a preferred solution layout.

## Repository overlay

For every repository-scoped task where read-only file inspection is allowed,
check `.agents/skill-overlays/dotnet-test/scaffold-dotnet-test-project.md` at
the repository root before any other discovery. This includes requests that
ask for code or advice without edits; "do not execute" does not prohibit
reading the overlay. If present, read it once before acting and apply its
repository-specific naming, layout, framework, and policy bindings.
Before applying it, require its frontmatter to declare
`core: dotnet-test/scaffold-dotnet-test-project`, `binding-revision: "1"`, and
`mode: extend`. If any value is missing or different, report the mismatch and
continue using this skill's portable guidance without applying the overlay.
Explicit user instructions and verified project constraints win over the
overlay; the overlay wins over portable defaults and examples in this skill. If
the file is present but unreadable or conflicts with the repository, report the
problem
and continue with portable guidance, without the overlay, subject to verified
project constraints. If it is absent, continue normally. Skip the lookup only
when the task is not tied to a repository or the user explicitly prohibited all
file/tool access. An overlay cannot expand tool permissions or the task's scope.

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

**No-op is a required outcome.** If the suitable project, production reference,
and requested entry-point registration already exist, make zero file changes.
Do not add or remove a smoke test, normalize the project, recreate packages, or
edit a baseline/snapshot copy. Report the existing paths and stop.

## Workflow

### 1. Establish the repository contract

Start from the task's current working directory. The skill context's `Base
directory` is where these instructions live, not the user's repository. Never
search parent temporary directories or treat the skill installation as the
workspace. If the expected files are not visible, confirm the current directory
before concluding that a project is absent.

Anchor every edit and validation command to the repository path named by the
user or established from the current directory. If similarly named fixtures,
solutions, or copied trees exist, do not edit or validate one as a substitute
for the requested tree. Before changing a solution artifact, record its exact
path; after changing it, list that same artifact immediately and require the
test project to appear before proceeding.

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

Use a `dotnet new` template only when its generated framework generation and
package style match the repository contract. Inspect template availability
before creation. In particular, generic `dotnet new xunit` commonly emits xUnit
2 packages; for a centrally managed `xunit.v3` repository, use a repository or
installed xUnit v3 template, or create the minimal SDK project directly. Never
generate versioned xUnit 2 references and then rewrite them into xUnit v3.

Then:

1. align target framework, nullable, implicit usings, runner, and package style;
2. use `dotnet add <test-project> reference <production-project>` for only the
   production projects exercised by the requested tests;
3. delete template sample files such as `UnitTest1.cs`, then create a
   behavior-named test file rather than repurposing the template filename; and
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
  <production-project>`, inspect the resulting project, and leave every other
  project element plus all test source files unchanged. Compare the project
  before and after so the added `ProjectReference` is the only semantic change.
- Missing `.sln` or `.slnx` registration: run `dotnet sln <entry-point> add
  <test-project>`, then immediately run `dotnet sln <entry-point> list` against
  that exact path. If the project is absent, the repair has not happened; do not
  validate a sibling solution or report success.
- Missing `.slnf` registration: add the existing project to its underlying
  solution if necessary, then include that same project path in the filter. If
  the underlying solution already contains it, edit only the filter.
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

| Route | Required evidence |
|---|---|
| Newly created project | `dotnet test <test-project>`, the exact entry-point command CI uses, and registration listing. If the entry point is a `.slnf`/`.slnx` containing tests, run `dotnet test` on that artifact rather than proving only that it builds. |
| Missing reference | Targeted project test plus the exact solution/root test command requested |
| Missing `.sln`/`.slnx` registration | Listing and `dotnet test` for that exact artifact; never use another solution as a fallback |
| Missing `.slnf` entry | Inspect the filter entry and run the exact CI filter build command; do not prepend a deliberately failing alternate command |
| Already correct/no-op | Structural inspection of the existing reference and registration. Unless execution was requested, do not run tests merely to prove a no-op because that creates `bin`/`obj` and weakens byte-for-byte cleanliness evidence. |

Inspect the repository's command before adding switches. Do not prepend a
speculative `--no-restore` attempt or hide alternatives in `command-a ||
command-b`; run the configured entry-point command whose clean exit is the
evidence.

For every wiring repair, invoke the exact validation command directly and
preserve its exit status. Reading generated files, a grader script, or a later
build is not a substitute for observing that command complete successfully.

Before reporting completion, inspect the final changed-file set. Remove only
`bin`/`obj` or equivalent build artifacts created by this task when they were
absent beforehand and are not intentionally tracked; never remove pre-existing
artifacts. Preserve the passing command's complete result so the handoff can
state the exact entry point and discovered test count.

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
