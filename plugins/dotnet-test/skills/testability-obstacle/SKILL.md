---
name: testability-obstacle
description: >-
  MUST USE for C#/.NET deterministic tests that require the smallest production seam for
  DateTime/Task.Delay/File/Environment/Guid/Random, static API preservation,
  nested/parallel overrides, or no real I/O. USE ONLY when the target workspace
  contains C# source plus a .csproj or .sln. DO NOT USE for audits, bulk
  migration, code that already has an injectable seam, or an explicit migration
  to a user-named existing abstraction (migrate-static-to-wrapper). Use instead
  of general test generation when the requested test is impossible without a
  production edit and seam selection is still open.
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
- The user already selected an existing replacement such as `TimeProvider` or
  `IFileSystem` and asks to migrate call sites to it. Use
  `migrate-static-to-wrapper`, which also updates affected tests.
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
| Filesystem | Existing repository abstraction; for one write/read operation use an injected delegate when conventions allow, otherwise a one-member interface or an already accepted `System.IO.Abstractions` |
| HTTP | Existing typed `HttpClient`/handler or `IHttpClientFactory` seam |
| Randomness | One generated value: injected delegate with `Random.Shared` as the production default; multiple operations/state: inject `Random` or a minimal generator interface |
| Environment/console/process | Minimal interface containing only members used by the target |

The scoped `AsyncLocal<T>` rule applies to every static API that must retain its
public static shape — clocks, filesystem access, environment lookups, identity
generation, and randomness. The scope captures and restores the previous value;
never implement `Dispose()` as an unconditional assignment to `null`.
Store the provider/value itself in `AsyncLocal<T>`. Do not put a mutable
`Stack<T>`, list, or other shared mutable collection in the slot: child
execution contexts can inherit the same object and corrupt each other's nesting.
When the provider itself is mutable (for example an in-memory store or fake time
provider), establish a fresh provider inside each parallel flow rather than
mutating one inherited instance from a parent context.

Constructor injection is the default for instance classes. Reuse the repository's
DI and naming conventions, but do not add a DI container to a class library just
to satisfy this workflow.

Preserve the existing public construction surface unless the user authorizes an
API change. Keep a public parameterless constructor as the real-dependency default
and place a test-only delegate/provider constructor at the narrowest visibility
the test project can reach. Do not turn the seam into a new public optional
parameter merely for test convenience.

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
advancing `FakeTimeProvider`, then awaiting it. For a deadline or boundary,
advance to immediately before the deadline and assert the task is still
incomplete before advancing across it; an immediate post-start assertion alone
does not prove the boundary. Never wait for wall-clock time.

For a nested ambient override, each scope owns the value that was active when it
started. Dispose scopes in LIFO order with `using` (which emits `try/finally`) or
an explicit `finally`; disposing the inner scope restores the outer value, never
an unconditional `null`. For an environment-backed static API, use this shape:

```csharp
public static class FeatureFlags
{
    private static readonly AsyncLocal<Func<string, string?>?> s_environment = new();

    public static bool IsEnabled(string name)
    {
        var reader = s_environment.Value;
        var value = reader is null
            ? Environment.GetEnvironmentVariable(name)
            : reader(name);

        return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }

    public static IDisposable OverrideEnvironment(Func<string, string?> reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        var previous = s_environment.Value;
        s_environment.Value = reader;
        return new RestoreScope(() => s_environment.Value = previous);
    }

    private sealed class RestoreScope : IDisposable
    {
        private Action? _restore;

        public RestoreScope(Action restore)
        {
            _restore = restore;
        }

        public void Dispose() =>
            Interlocked.Exchange(ref _restore, null)?.Invoke();
    }
}
```

The exception test must observe the outer value after the exception has escaped
the inner `using` scope but before the outer scope is disposed:

```csharp
using var outer = FeatureFlags.OverrideEnvironment(_ => "true");
Assert.True(FeatureFlags.IsEnabled("Preview"));

Assert.Throws<InvalidOperationException>(() =>
{
    using var inner = FeatureFlags.OverrideEnvironment(_ => "false");
    Assert.False(FeatureFlags.IsEnabled("Preview"));
    throw new InvalidOperationException("test");
});

Assert.True(FeatureFlags.IsEnabled("Preview"));
```

Also overlap two async flows that each establish a fresh override and assert
that each flow sees only its own value. Parallel-only tests do not catch the
common "dispose sets null" bug. Do not mutate process environment variables in
these tests; the scoped reader is the deterministic input. Choose an outer value
different from the production fallback so clearing the slot cannot accidentally
pass the restoration assertion.

### Step 3: Preserve behavior and API shape

Keep the production change mechanical:

- Wrap only members used by the target behavior.
- Default implementations delegate directly to the original API.
- Preserve exceptions, path handling, time zone, and `DateTime.Kind`.
- Keep existing public signatures unless the user explicitly permits an API change.
- Do not move business logic into the wrapper or fix unrelated production bugs.

Deterministic serialized text is a deliberate exception to preserving ambient
platform formatting. If the user asks for exact reproducible output across
platforms, use the format's explicit separator (use literal `\n` when none is
specified) and assert that literal content. Keep `Environment.NewLine` only
when platform-native output is part of the existing contract.

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
An existing manual factory must pass the real dependency explicitly (for example,
`new ExpirationPolicy(TimeProvider.System)`). Do not move responsibility into an
optional constructor or add an optional provider parameter to the factory.

Build the affected production project before writing tests. A compile failure here
is a seam problem, not a test problem.

### Step 5: Write deterministic tests

Use the repository's existing test project. If none exists, invoke
`scaffold-dotnet-test-project` first.

Tests must supply controlled dependencies:

- fixed/advanced time rather than wall-clock waiting;
- an in-memory fake filesystem or hand-rolled fake rather than temp/real files;
- no environment mutation, external process, console input, or network.

Before authoring a test, inspect its test project and follow the existing framework,
global-using, and assertion conventions. Use the framework packages already referenced
by that project; never add a hand-rolled `FactAttribute`, a substitute test-framework
type, or unrelated test-project plumbing to make a test compile.

Assert the requested business result and at least one interaction/state observable
that proves the fake dependency drove the path. Include a production-default test
only when it can remain deterministic; never touch the real filesystem merely to
prove the adapter delegates.

Choose the narrowest seam that supports the behavior. A single
`File.WriteAllText` call can be an injected `Action<string, string>` with a real
default; do not create an interface, implementation, friend-assembly setting,
and extra project wiring unless repository conventions or multiple operations
justify them.

Preserve the public API surface as well as existing signatures. Do not add a
public dependency-injecting constructor solely for tests. When a class currently
has only its implicit public parameterless constructor and the exact test
assembly is known, keep that constructor behavior and make the test-only
constructor internal; an `InternalsVisibleTo` entry is justified in this narrow
case because it prevents the seam from becoming public API. Prefer an existing
repository friend-assembly convention when one is present.

Do not add `InternalsVisibleTo` when an existing public seam already accepts the
fake or the test project can otherwise supply it. Friend-assembly access is
justified only when the chosen minimum constructor/delegate seam must remain
internal to preserve the public API and the exact test assembly is known.

### Step 6: Verify the complete path

Run the affected production build, targeted test project, and repository-level
test command. Re-read the diff and confirm:

1. every production change is required by the seam;
2. no real ambient resource is used by the new tests;
3. current-time semantics and public behavior are preserved;
4. existing tests were not replaced or duplicated.

Inspect the test summary, not only the exit code. Zero discovered tests, a build
without the requested test run, or any failing/erroring test means the task is
incomplete. Fix discovery/execution and rerun before reporting success.
When a new test does not compile, correct its imports, assertion overload, or async
test shape against the existing test framework before changing the production seam;
do not emulate missing framework APIs in source.
For a static ambient seam, completion requires executed tests for substitution,
nested restoration, and overlapping async-flow isolation; production compilation
alone is never sufficient. Capture the passing test count or requested test
names in the handoff. If no test was discovered or the output does not prove
execution, correct the project/test source and rerun rather than reporting the
seam as validated.

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
- [ ] The seam did not enlarge the public API when an internal test seam was sufficient.
- [ ] Time conversions preserve local/UTC and `DateTime.Kind` semantics.
- [ ] Static ambient overrides are async-safe, scoped, nested, and reversible.
- [ ] New tests use fixed/in-memory dependencies and no real I/O or wall clock.
- [ ] Production build and targeted/repository tests pass with at least one requested test discovered.

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
| Reporting a zero-test run as success | Fix discovery and require the requested tests to execute and pass |
