---
name: test-code-changes
description: >-
  Use when a task changes C#/.NET production behavior (bug fix, feature,
  endpoint, page, component, CLI option) to add or extend the durable tests
  that prove it in the repository's test project, instead of curl, script, or
  temporary-project checks, and to choose the final test run. DO NOT USE for
  docs, comment, formatting, or other non-behavioral edits; tests for code the
  task did not change (code-testing); test command syntax only (run-tests); or
  read-only test reviews.
license: MIT
---

# Test Code Changes

The production change is the deliverable; tests in the repository's own test project are its proof.
Keep that durable test even when the harness is awkward.

## Decide whether the change needs a durable test

| Change in this task | Durable test? |
|---|---|
| Behavior change in production code: logic, endpoint, page or component, CLI option, serialization, DI wiring, behavior-changing configuration | Yes, unless no stable observable seam exists; then say why in the handoff |
| Refactor intended to preserve behavior | Run the existing covering tests; add a characterization test only where the touched behavior has none |
| Build or packaging property, version, docs, comments, formatting | No new test. Verify by evaluating the property or inspecting the output |
| User says not to add tests | Respect it; still run the existing covering tests unless told not to run them |

## Workflow

Before the first edit, locate the tests and decide where they will go.

### 1. Locate the tests

- Find test projects by searching `*.csproj` files for `ProjectReference` entries pointing to each
  production project you'll change. Also check the solution file and `*.Tests` naming.
- Open one neighboring test file and its fixture or helper to identify the harness:
  `WebApplicationFactory`/`TestServer`, bUnit, a Playwright fixture, Verify snapshots, or a custom
  base class.

### 2. Choose where the tests go, in this order

1. Extend the nearest test class and reuse its fixture.
2. If the test project holds only a placeholder (`UnitTest1`, `Assert.True(true)`), add a
   behavior-named file to it.
3. If no test project covers the changed project, create one minimal project only when the change
   has a testable seam and the repository has a build entry point. Skip this for prototypes and
   for tasks the user framed as a quick change. If no test project covers the change and you
   create none, list the behavior under `Not covered`.
   - Use `scaffold-dotnet-test-project` (when available). Otherwise create it with the
     repository's test framework, then run `dotnet add <test.csproj> reference <prod.csproj>` and
     `dotnet sln <sln> add <test.csproj>` for the solution CI uses (with no solution file, don't
     create one).
   - Put the test project in a sibling folder, outside the production project's folder. If it must
     nest under an SDK-style production project that picks up the folder through default items,
     exclude it in the production project. For `tests/`, set
     `<DefaultItemExcludes>$(DefaultItemExcludes);tests/**</DefaultItemExcludes>`.
     Nested test sources can cause CS0579 (duplicate attributes); `Compile Remove` alone can fix
     the build while still letting the Web SDK publish test-folder assets.
4. If the existing harness can't run here (missing browser, workload or service), test at a lower
   layer that can run, such as an in-process host, a component render or a service-level test,
   rather than abandon the test.

Only when the change is in ASP.NET Core, Blazor, gRPC or a reverse proxy, read
[references/web-and-ui-harnesses.md](references/web-and-ui-harnesses.md) before writing the test.
If that file is missing, continue with the order above and say so in the handoff.

### 3. Derive cases from the request, not from the implementation

- The literal example from the request or issue.
- One case per rule or branch you added: each validation rule, status code, guard and fallback.
- A **contrast** case proving behavior that must not change.
- Boundaries the change introduces: empty, zero, limit, limit ± 1.
- Sibling variants the change must also cover: overloads, the async path, other endpoints of the
  same resource, other locales or platforms.

Assert the observable contract: return value, status code, emitted text, persisted state, rendered
markup or event effect. When the full output is predictable, assert all of it; a presence,
non-null, type-only or `Contains` check is not enough. After covering the scenarios above, stop
adding inputs that only repeat a branch.

### 4. Prefer red-green for regressions when cheap

Run the new test before the fix, or confirm it would fail on the original behavior.

### 5. Treat ad-hoc checks as drafts, not proof

If you probe with `curl`, a `node` or `python` script, a REPL, `dotnet run --file` or a temporary
project:

- move the assertions that matter into the repository's test project or established suite before
  finishing;
- delete the scratch files and projects;
- never keep a probe script as a substitute for a repository test; tests in another language
  (such as TypeScript Playwright tests) belong only in a suite the repository already has.

### 6. Treat existing tests as evidence

- Add new tests rather than repurposing an existing test's scenario.
- When the request intentionally changes behavior, update only the expectations that encode the
  old behavior, and name them in the handoff.
- Never delete, skip or loosen a failing test to get green: fix the code, or report the failure.
- Review each snapshot `.received` diff before accepting it, then remove leftover `.received` files.

### 7. Verify at the right breadth

- A user-named test subset or an instruction not to run tests overrides the default run below.
  Follow that instruction and report the omitted final run as not done.
- Iterate with the narrowest filter.
- **After the final edit, including test-only or formatting edits**, run unfiltered every test
  project that directly references a changed production project or holds tests you added or
  changed.
- If that is more than about three projects, or one is very large or slow (tens of thousands of
  tests, or a browser, device or real-service suite), run the covering test classes plus one
  broader scope (their namespace, or the most affected project unfiltered), and state the scope.
  That declared scope counts as the final run.
- If a suite needs an unavailable service (database, broker, browser), run the in-scope tests
  that don't need it (repository fakes, in-memory providers, a unit category). Report tests
  affected only by the outage as blocked or, if they never executed, not run. Do not report them
  as passed or as code failures.
- Apply the same final-run breadth to established suites in another language, using their own
  runner and equivalent filters. For .NET command syntax, use `run-tests` (when available);
  otherwise use the repository's documented .NET test command. If none is documented, use the
  existing build and test runner for classic non-SDK projects. For SDK-style projects that support
  `dotnet test`, use `dotnet test <test.csproj>` in VSTest command mode or
  `dotnet test --project <test.csproj>` when `global.json` selects native Microsoft.Testing.Platform
  mode.

## Handoff

Before the handoff, check `git status` and make sure no scratch files, probe scripts or `.received`
files from this task remain. End with this block, scaled to the change:

```text
Tests added or changed: <test names and files; mark a new test project as new>
Expectations updated for intended behavior changes: <test names, or none>
Final run after the last edit: <exact command> -> Passed: N, Failed: N, Skipped: N
Not covered: <behavior or suite part, and why>
```

Never claim a run that didn't complete. A failed restore or build is not a test result: name the
failing phase and its first error.

## Stop conditions

- The production change remains the primary deliverable; don't reduce fix effort to write tests.
- Don't expand into a broad suite or coverage drive the user didn't ask for; explicit broad test
  requests belong to `code-testing` (when available).
- Don't create test projects for docs, config-only or packaging tasks; test behavior-changing
  configuration only in an existing test project.
- Don't add test packages beyond what the new test needs; reuse the repository's test framework.
- If `code-testing` also loads, use it for test design. Unless the user asked for broad or
  comprehensive tests, keep tests for this change focused and use this skill for placement,
  contrast cases and the final run. Without `code-testing`, apply these rules alone.
