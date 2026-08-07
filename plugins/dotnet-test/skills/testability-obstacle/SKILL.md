---
name: testability-obstacle
description: >-
  Make a static C# API testable and add tests. USE FOR: deterministic Guid,
  random, environment, time, or filesystem behavior; nested override restore;
  parallel async isolation; no real I/O. DO NOT USE FOR: audits, wrapper-only/
  bulk migration, or an existing injectable seam.
license: MIT
---

# Resolve a Testability Obstacle

Introduce the smallest behavior-preserving seam needed to test a specific C#
behavior, then add deterministic tests that prove both the behavior and the seam.
The production edit is a means to the requested test, not an invitation to
redesign adjacent code.

## When to Use

- A requested test would otherwise read/write the real filesystem.
- Behavior depends on the current time, delay, random value, environment, console,
  process, or another ambient dependency.
- The user explicitly permits or requests a safe production seam.
- Existing tests cannot control a dependency without process-global mutation.

## When Not to Use

- The dependency is already injected or passed as an argument. Write tests with
  a fake through the existing seam using `code-testing-agent`.
- The user wants a repository-wide testability audit. Use
  `detect-static-dependencies`.
- The user wants wrappers generated but not call sites/tests changed. Use
  `generate-testability-wrappers`.
- The user requests a broad mechanical migration. Use
  `migrate-static-to-wrapper`, then generate tests separately.
- The code is not C#/.NET.

## Inputs

| Input | Required | Description |
|-------|----------|-------------|
| Behavior to test | Yes | The method/workflow and expected observable behavior |
| Target scope | No | Discover the narrowest relevant file/project when omitted |
| Allowed production changes | No | Default to the minimum internal/constructor seam |

## Workflow

### Step 1: Prove the obstacle

Read the target production path and its existing tests. Identify the exact ambient
operation preventing a deterministic test and the behavior that must remain
unchanged. Do not run a repository-wide static scan for a single-class request.

If an adequate seam already exists, stop refactoring and use it. This skill adds
no value when a fake can already be supplied.

### Step 2: Select the smallest safe seam

Choose by dependency and repository constraints:

| Dependency | Preferred seam |
|------------|----------------|
| Current time / timers | Inject `TimeProvider`; use `FakeTimeProvider` in tests |
| Filesystem | Existing repository file abstraction; otherwise the smallest interface or `System.IO.Abstractions` when already used/accepted |
| HTTP | Existing typed `HttpClient`/handler or `IHttpClientFactory` seam |
| Randomness | Inject `Random` or a minimal generator interface |
| Environment/console/process | Minimal interface containing only members used by the target |

The scoped `AsyncLocal<T>` rule applies to every static API that must retain its
public static shape — clocks, filesystem access, environment lookups, identity
generation, and randomness. The scope captures and restores the previous value;
never implement `Dispose()` as an unconditional assignment to `null`.

Constructor injection is the default for instance classes. Reuse the repository's
DI and naming conventions, but do not add a DI container to a class library just
to satisfy this workflow.

For a static class or a public API that cannot change, use a scoped ambient seam
only when constructor/parameter injection is impossible. The override must:

- flow across `await` (`AsyncLocal<T>`, not `[ThreadStatic]`);
- return `IDisposable` and restore the previous value, including nested scopes;
- default to the real production dependency;
- avoid a process-global mutable fake that makes tests non-parallel.

Use built-in fake-time-aware overloads instead of inventing an `IDelay` wrapper:

| Ambient operation | Replacement |
|-------------------|-------------|
| `Task.Delay(delay, token)` | `Task.Delay(delay, timeProvider, token)` |
| `new CancellationTokenSource(delay)` | `new CancellationTokenSource(delay, timeProvider)` |
| `PeriodicTimer(period)` | `new PeriodicTimer(period, timeProvider)` when the target framework provides it |

Test delayed behavior by starting the operation, proving it is incomplete,
advancing `FakeTimeProvider`, then awaiting it. Never wait for wall-clock time.

For a nested ambient override, disposing the inner scope must restore the outer
value, not clear the slot. Capture the previous value per scope:

```csharp
public static IDisposable OverrideClock(Func<DateTimeOffset> clock)
{
    var previous = s_clock.Value;
    s_clock.Value = clock;
    return new Scope(() => s_clock.Value = previous);
}
```

Add tests for both nesting and parallel async flows; parallel-only tests do not
catch the common "dispose sets null" bug.

### Step 3: Preserve behavior and API shape

Keep the production change mechanical:

- Wrap only members used by the target behavior.
- Default implementations delegate directly to the original API.
- Preserve exceptions, path handling, time zone, and `DateTime.Kind`.
- Keep existing public signatures unless the user explicitly permits an API change.
- Do not move business logic into the wrapper or fix unrelated production bugs.

For time replacements:

- `DateTime.UtcNow` -> `timeProvider.GetUtcNow().UtcDateTime`
- `DateTime.Now` -> `timeProvider.GetLocalNow().LocalDateTime`
- `DateTimeOffset.UtcNow` -> `timeProvider.GetUtcNow()`
- `DateTimeOffset.Now` -> `timeProvider.GetLocalNow()`

### Step 4: Keep production defaults wired

Update every composition root or constructor call affected by the seam. Production
must still use real time/filesystem/etc. by default. If the project uses DI,
register the default implementation with the lifetime matching repository
conventions. If it does not use DI, compose explicitly; do not introduce a
container.

Build the affected production project before writing tests. A compile failure here
is a seam problem, not a test problem.

### Step 5: Write deterministic tests

Use the repository's existing test project. If none exists, invoke
`scaffold-dotnet-test-project` first.

Tests must supply controlled dependencies:

- fixed/advanced time rather than wall-clock waiting;
- an in-memory fake filesystem or hand-rolled fake rather than temp/real files;
- no environment mutation, external process, console input, or network.

Assert the requested business result and at least one interaction/state observable
that proves the fake dependency drove the path. Include a production-default test
only when it can remain deterministic; never touch the real filesystem merely to
prove the adapter delegates.

### Step 6: Verify the complete path

Run the affected production build, targeted test project, and repository-level
test command. Re-read the diff and confirm:

1. every production change is required by the seam;
2. no real ambient resource is used by the new tests;
3. current-time semantics and public behavior are preserved;
4. existing tests were not replaced or duplicated.

## Output Contract

Provide a compact `Requirement | Evidence` table. Cite the production seam,
production default wiring, exact test names, and passing commands. If a package
restore or build blocks validation, report that blocker rather than claiming the
tests pass.

## Validation

- [ ] The original obstacle was concrete and in the requested path.
- [ ] An existing seam was reused when available.
- [ ] The new abstraction exposes only members required by the target behavior.
- [ ] Production defaults still delegate to the original dependency.
- [ ] Time conversions preserve local/UTC and `DateTime.Kind` semantics.
- [ ] Static ambient overrides are async-safe, scoped, nested, and reversible.
- [ ] New tests use fixed/in-memory dependencies and no real I/O or wall clock.
- [ ] Production build and targeted/repository tests pass.

## Common Pitfalls

| Pitfall | Corrective action |
|---------|-------------------|
| Refactoring before proving a blocker | Reuse an existing seam and write the test directly |
| Wrapping an entire static API | Expose only members exercised by the target |
| Converting `UtcNow` with `.DateTime` | Use `.UtcDateTime` to preserve `DateTimeKind.Utc` |
| Mutable static fake shared by tests | Use constructor injection or a scoped `AsyncLocal<T>` override |
| Adding DI to a library with no container | Compose the dependency explicitly |
| Using temp files as a shortcut | Supply an in-memory fake; the scenario requires no real I/O |
| Stopping after the refactor builds | Write and run the behavior tests that justified the seam |
