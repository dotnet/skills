---
name: generate-testability-wrappers
description: >
  DO NOT USE when the target already consumes an injected interface or built-in
  abstraction such as IFileSystem or TimeProvider, even if the request says
  "generate a wrapper"; no new wrapper is needed. Use only when C# source calls
  an ambient/static dependency and no injectable seam exists: first-time
  TimeProvider, IHttpClientFactory, or System.IO.Abstractions adoption; minimal
  Environment/Console/Process wrappers; IProcessRunner; DI registration; or an
  ambient seam that preserves a static API. Exclude static detection
  (detect-static-dependencies), migration to an existing/registered abstraction
  (migrate-static-to-wrapper), one blocked behavior plus deterministic tests
  (testability-obstacle), and general interface design.
license: MIT
---

# Generate Testability Wrappers

Generate wrapper interfaces, default implementations, and DI service registration code for untestable static dependencies. For statics that already have .NET built-in abstractions (`TimeProvider`, `IHttpClientFactory`), guide adoption of the built-in. For statics without built-in alternatives, generate custom minimal wrappers.

## When to Use

- After running `detect-static-dependencies` and identifying which statics to wrap
- When the user asks to make a class testable by replacing statics with injected abstractions
- When adopting `TimeProvider` (.NET 8+) or `System.IO.Abstractions`
- When creating a custom wrapper for `Environment.*`, `Console.*`, or `Process.*`
- When a released static API needs an ambient seam because signatures cannot change

## When Not to Use

- The user wants to find statics first (use `detect-static-dependencies`)
- The user wants to bulk-replace call sites (use `migrate-static-to-wrapper`)
- The static is already behind an interface

If the target already consumes an injected interface or built-in abstraction, stop:
do not add a second wrapper, project, registration, or test around that seam.

> A missing DI package does not by itself force an ambient seam. For an
> instantiable class, prefer constructor injection and compose it explicitly or
> show the requested registration. Use Step 5 when the API is static and its
> signatures must stay static, or when the user explicitly forbids caller
> construction/DI changes.

## Inputs

| Input | Required | Description |
|-------|----------|-------------|
| Static category | Yes | Which category: `time`, `filesystem`, `environment`, `network`, `console`, `process` |
| Target framework | Yes | The `TargetFramework` from `.csproj` (affects which built-in abstractions exist) |
| Composition | No | Existing DI framework, explicit/manual construction, or immutable static API |
| Namespace | No | Target namespace for generated wrapper code |

## Workflow

### Step 1: Determine the abstraction strategy

Based on the category and target framework:

| Category | .NET 8+ | .NET 6-7 | .NET Framework |
|----------|---------|----------|----------------|
| Time | `TimeProvider` (built-in) | `TimeProvider` via `Microsoft.Bcl.TimeProvider` NuGet | Custom `ISystemClock` |
| File system | `System.IO.Abstractions` (NuGet) | Same | Same |
| HTTP | `IHttpClientFactory` (built-in) | Same | Same |
| Environment | Custom `IEnvironmentProvider` | Same | Same |
| Console | Custom `IConsole` | Same | Same |
| Process | Custom `IProcessRunner` | Same | Same |

The table picks *which abstraction*. How it reaches the code under test is a
separate axis:

- instantiable class: constructor injection, even if current callers compose the
  object manually;
- existing container: add compile-ready registration following its conventions;
- public static API/signatures that cannot change: Step 5's ambient seam.

Check for a host builder, `IServiceCollection`, existing registrations, and
construction sites. Do not infer "must remain static" merely because the project
currently has no container.

### Step 2: Generate built-in abstraction adoption (Time, HTTP)

#### TimeProvider (.NET 8+)

No wrapper code needed. Complete all four parts: production registration,
constructor injection, a `FakeTimeProvider` test, and the testing package.

1. Register in DI:
```csharp
builder.Services.AddSingleton(TimeProvider.System);
```

2. Inject into classes:
```csharp
public class OrderProcessor(TimeProvider timeProvider)
{
    public bool IsExpired(Order order)
        => timeProvider.GetUtcNow() > order.ExpiresAt;
}
```

3. Test with `FakeTimeProvider`:
```csharp
// Requires Microsoft.Extensions.TimeProvider.Testing NuGet
var fakeTime = new FakeTimeProvider(new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero));
var processor = new OrderProcessor(fakeTime);
fakeTime.Advance(TimeSpan.FromDays(1));
Assert.True(processor.IsExpired(order));
```

The assertion must prove a time-dependent result after the fake is pinned or
advanced. Merely constructing `FakeTimeProvider` is not a test. When the project
has no container but the target is an instantiable class, inject
`TimeProvider` anyway and show explicit production construction with
`TimeProvider.System`; do not replace it with a custom static clock.

Before calling the adoption complete, verify the repository contains or the
answer supplies every required artifact: the testing package reference, the
production composition/registration, every affected constructor call, and a
runnable fake-time test. A code fragment that omits one of those integration
points is guidance, not a completed adoption.

#### TimeProvider (pre-.NET 8)

Guide: install `Microsoft.Bcl.TimeProvider` NuGet. Same API as above.

#### IHttpClientFactory

No wrapper code needed. Register a typed client via
`builder.Services.AddHttpClient<MyService>()` and inject `HttpClient` directly
into the class constructor. Preserve cancellation by passing the caller's token
to the HTTP operation.

For tests, provide a complete fake `HttpMessageHandler` whose `SendAsync`
returns a deterministic `HttpResponseMessage`, construct `HttpClient` with that
handler, and exercise the typed client without network access. Compile and run
the focused test when the task asks for implementation; do not stop at a
schematic handler method.

When production uses a typed-client registration, test that same registration
pipeline: configure its primary handler in `ServiceCollection`, resolve the typed
client, and call it. A test that manually constructs `HttpClient` proves the class
but not the DI registration the task asked to adopt.

### Step 3: Generate custom wrappers (Environment, Console, Process)

For categories without built-in abstractions, follow this template:

#### Interface — define the minimal surface

Only include methods that were actually detected in the codebase. Do NOT generate a wrapper for every possible member — wrap only what is used.

Prefer a stateless operation-shaped interface. For example, if a caller only
needs to start a process, wait, and return its exit code, expose one `Run`
operation rather than a stateful wrapper that leaks `Process` lifecycle.

```csharp
namespace <Namespace>;

/// <summary>
/// Abstraction over <static class> for testability. 
/// </summary>
public interface I<WrapperName>
{
    // One method per detected static call
    <return type> <MethodName>(<parameters>);
}
```

#### Default implementation — delegate to the real static

```csharp
namespace <Namespace>;

/// <summary>
/// Default implementation that delegates to <static class>.
/// </summary>
public sealed class <WrapperName> : I<WrapperName>
{
    public <return type> <MethodName>(<parameters>)
        => <StaticClass>.<Method>(<arguments>);
}
```

#### DI registration

```csharp
// In Program.cs or Startup.cs:
builder.Services.AddSingleton<I<WrapperName>, <WrapperName>>();
```

Treat registration as a deliverable, not a sentence in the summary. Add it to
the repository's existing registration surface when one exists. Otherwise show
the exact compile-ready statement and identify where the caller should place
it. Stateless delegating wrappers are singleton; if state must be retained,
explain why a shorter lifetime is required.

### Step 4: Generate file system wrapper adoption

Prefer the established `System.IO.Abstractions` NuGet package over custom wrappers:

1. Install the package:
```
dotnet add package System.IO.Abstractions
```

2. Register in DI:
```csharp
builder.Services.AddSingleton<IFileSystem, FileSystem>();
```

3. Inject `IFileSystem` into classes:
```csharp
public class ConfigLoader(IFileSystem fileSystem)
{
    public string LoadConfig(string path)
        => fileSystem.File.ReadAllText(path);
}
```

4. Test with `MockFileSystem`:
```
dotnet add <TestProject> package System.IO.Abstractions.TestingHelpers
```
```csharp
var mockFs = new MockFileSystem(new Dictionary<string, MockFileData>
{
    { "/config.json", new MockFileData("{\"key\": \"value\"}") }
});
var loader = new ConfigLoader(mockFs);
Assert.Equal("{\"key\": \"value\"}", loader.LoadConfig("/config.json"));
```

Package-first adoption is exclusive: add both package references, use
`IFileSystem` in production, register or explicitly compose `FileSystem`, and
seed `MockFileSystem` before exercising the consumer. Do not also generate a
second custom filesystem interface, and do not present an unseeded mock whose
test could pass without proving the requested read/write behavior.

### Step 5: Generate a signature-preserving ambient context

Use this pattern when the API must remain static or its released signatures
cannot accept a dependency:

```csharp
public static class Clock
{
    private static readonly AsyncLocal<Func<DateTime>?> s_override = new();
    public static DateTime UtcNow
        => s_override.Value?.Invoke() ?? TimeProvider.System.GetUtcNow().UtcDateTime;

    internal static IDisposable Override(DateTime fixedUtcTime)
    {
        if (fixedUtcTime.Kind != DateTimeKind.Utc)
            throw new ArgumentException("The override must be UTC.", nameof(fixedUtcTime));

        var previous = s_override.Value;
        s_override.Value = () => fixedUtcTime;
        return new Scope(previous);
    }
    private sealed class Scope : IDisposable
    {
        private readonly Func<DateTime>? _previous;
        private bool _disposed;

        public Scope(Func<DateTime>? previous)
        {
            _previous = previous;
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            s_override.Value = _previous;
            _disposed = true;
        }
    }
}
```

Key trade-offs: `AsyncLocal<T>` ensures parallel tests don't interfere; production cost is one null check per call; the `static readonly` field is essentially free.

Three properties this pattern must keep, because each has broken a real migration:

- **Scope the override and make it reversible.** Return an `IDisposable` that restores the previous value, so a test cannot leak a pinned time into the next one. A bare setter, or a manual `try`/`finally` at each call site, puts that burden on every test author.
- **Use `AsyncLocal<T>`, never `[ThreadStatic]`.** `[ThreadStatic]` does not flow across `await`, so the override silently disappears mid-test.
- **Preserve the semantics of the member you are replacing.** Substituting `DateTime.UtcNow` with a local-time source changes the `DateTimeKind` every existing caller and stored value depends on — pair `UtcNow` with `GetUtcNow()`, and `Now` with `GetLocalNow()`.
- **Prove the test assembly can reach the override.** An `internal` override is
  inaccessible from a separate test assembly unless the production project adds
  the exact `InternalsVisibleTo` for that test assembly. Otherwise use an
  already-public seam only when a public API change is authorized. Never show a
  test calling an inaccessible member.

The same shape works for non-time statics: swap `TimeProvider.System.GetUtcNow()` for the real static call and keep the override slot, the disposable scope, and the original semantics.

### Step 6: Place generated files

Generate files following the project's existing conventions:
- If there is an `Abstractions/` or `Interfaces/` folder, place the interface there
- If there is an `Infrastructure/` or `Services/` folder, place the implementation there
- Otherwise, create files next to the code that uses the static

Always generate:
1. The interface file (or adoption instructions for built-in abstractions)
2. The default implementation file
3. The compile-ready DI registration, applied to an existing registration
   surface or shown at the exact composition point.
4. A deterministic substitution example or focused test that exercises the
   consumer without the ambient resource.

Skip registration entirely on the ambient-seam path: there is no container to
register into, and offering one anyway is the failure mode that made a user ask
for the seam in the first place.

Before reporting completion, verify the delivered output contains every item
the prompt requested. In particular, do not summarize "singleton registration"
when no registration code was added or shown, and do not claim testability
without demonstrating how the consumer receives a fake.
For console wrappers, exercise the consumer with a fake that both captures the
prompt and supplies the returned input; a build or banner-only run does not prove
the prompt flow.

## Validation

- [ ] Generated interface only wraps statics that were actually detected (not speculative)
- [ ] Default implementation delegates to the real static with no behavior changes
- [ ] DI registration uses `AddSingleton` for stateless wrappers, `AddTransient` for stateful ones
- [ ] NuGet packages are recommended where established libraries exist (System.IO.Abstractions, etc.)
- [ ] For .NET 8+, `TimeProvider` is recommended over custom `ISystemClock`
- [ ] TimeProvider adoption includes injection, production composition,
      `FakeTimeProvider`, its testing package, and an assertion on a
      time-dependent result
- [ ] HTTP adoption includes a complete fake-handler test and preserves
      cancellation
- [ ] On injection paths, registration or explicit composition is compile-ready,
      and a fake demonstrates the consumer without the real ambient dependency
- [ ] Ambient context pattern includes `AsyncLocal<T>`, a scoped `IDisposable` that restores the previous value, and trade-off explanation
- [ ] On the ambient-seam path, no `IServiceCollection` registration is proposed, the separate test assembly can reach the override, and the replaced member's return type and semantics (`UtcNow` vs `Now`, and its `DateTimeKind`) are preserved

## Common Pitfalls

| Pitfall | Solution |
|---------|----------|
| Treating "no DI package" as "must be ambient" | Inject into instantiable classes and compose explicitly; reserve Step 5 for static/signature-preserving APIs |
| Wrapping ALL members of a static class | Only wrap methods actually called in the codebase |
| Custom time wrapper on .NET 8+ | Use built-in `TimeProvider` instead |
| Custom file system wrapper | Prefer `System.IO.Abstractions` NuGet — battle-tested, complete |
| Registering scoped when singleton suffices | Stateless wrappers should be `AddSingleton` |
| Forgetting test helper packages | `Microsoft.Extensions.TimeProvider.Testing` for time, `System.IO.Abstractions.TestingHelpers` for filesystem |
| Ambient context without `AsyncLocal` | Non-async `[ThreadStatic]` breaks with `async`/`await` — always use `AsyncLocal<T>` |
| Showing an internal ambient override to external tests | Add the exact friend assembly or use an authorized public seam; compile the test project |
