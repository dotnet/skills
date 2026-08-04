---
name: generate-testability-wrappers
description: >
  Generate wrapper interfaces and DI registration for hard-to-test static dependencies in C#,
  when the abstraction does NOT exist yet. Produces IFileSystem, IEnvironmentProvider, IConsole,
  IProcessRunner wrappers, or guides first-time adoption of TimeProvider and IHttpClientFactory.
  With no DI container, produces the ambient context seam instead.
  USE FOR: generate wrapper for static, create IFileSystem wrapper, wrap DateTime.Now,
  make a static or a class testable, create abstraction for File.*, generate DI registration,
  adopt TimeProvider when it is not registered yet, IHttpClientFactory setup, testability
  wrapper, how to make statics injectable, adopt System.IO.Abstractions, make code testable
  without adding a DI framework.
  DO NOT USE FOR: detecting statics (use detect-static-dependencies), migrating
  call sites or replacing existing DateTime.*/File.* usages once the wrapper is created
  or already registered in DI (use migrate-static-to-wrapper), general interface design.
license: MIT
---

# Generate Testability Wrappers

Generate wrapper interfaces, default implementations, and DI service registration code for untestable static dependencies. For statics that already have .NET built-in abstractions (`TimeProvider`, `IHttpClientFactory`), guide adoption of the built-in. For statics without built-in alternatives, generate custom minimal wrappers.

## When to Use

- After running `detect-static-dependencies` and identifying which statics to wrap
- When the user asks to make a class testable by replacing statics with injected abstractions
- When adopting `TimeProvider` (.NET 8+) or `System.IO.Abstractions`
- When creating a custom wrapper for `Environment.*`, `Console.*`, or `Process.*`
- When there is no DI container and the seam has to be ambient rather than injected

## When Not to Use

- The user wants to find statics first (use `detect-static-dependencies`)
- The user wants to bulk-replace call sites (use `migrate-static-to-wrapper`)
- The static is already behind an interface

> A project with **no DI container**, or a user who does not want to add one, is **not** a reason to skip this skill —
> that is exactly what the ambient context seam in Step 5 is for. Choose the seam over constructor injection in that
> case; do not decline the request and do not propose registering anything in a service collection.

## Inputs

| Input | Required | Description |
|-------|----------|-------------|
| Static category | Yes | Which category: `time`, `filesystem`, `environment`, `network`, `console`, `process` |
| Target framework | Yes | The `TargetFramework` from `.csproj` (affects which built-in abstractions exist) |
| DI container | No | Which DI framework: `microsoft` (default), `autofac`, `none` (ambient context) |
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

The table picks *which abstraction*. How it reaches the code under test is a separate axis: constructor
injection when a DI container exists, and the **ambient context seam of Step 5** when one does not. Decide that
axis first — check for a host builder, `IServiceCollection`, or an existing container registration — because a
static class cannot take a constructor and a project without a container has nowhere to register anything. In
that case skip Steps 2–4 and go to Step 5; the abstraction chosen above still applies, it is just reached through
the ambient seam.

### Step 2: Generate built-in abstraction adoption (Time, HTTP)

#### TimeProvider (.NET 8+)

No wrapper code needed — guide the user:

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

#### TimeProvider (pre-.NET 8)

Guide: install `Microsoft.Bcl.TimeProvider` NuGet. Same API as above.

#### IHttpClientFactory

No wrapper code needed — register typed clients via `builder.Services.AddHttpClient<MyService>()` and inject `HttpClient` directly into the class constructor.

### Step 3: Generate custom wrappers (Environment, Console, Process)

For categories without built-in abstractions, follow this template:

#### Interface — define the minimal surface

Only include methods that were actually detected in the codebase. Do NOT generate a wrapper for every possible member — wrap only what is used.

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

### Step 5: Generate ambient context alternative (when DI is not available)

If the codebase does not use DI (e.g., old console app, library code), offer the ambient context pattern:

```csharp
public static class Clock
{
    private static readonly AsyncLocal<Func<DateTimeOffset>?> s_override = new();
    public static DateTimeOffset UtcNow
        => s_override.Value?.Invoke() ?? TimeProvider.System.GetUtcNow();

    public static IDisposable Override(DateTimeOffset fixedTime)
    {
        s_override.Value = () => fixedTime;
        return new Scope();
    }
    private sealed class Scope : IDisposable
    {
        public void Dispose() => s_override.Value = null;
    }
}
```

Key trade-offs: `AsyncLocal<T>` ensures parallel tests don't interfere; production cost is one null check per call; the `static readonly` field is essentially free.

Three properties this pattern must keep, because each has broken a real migration:

- **Scope the override and make it reversible.** Return an `IDisposable` that restores the previous value, so a test cannot leak a pinned time into the next one. A bare setter, or a manual `try`/`finally` at each call site, puts that burden on every test author.
- **Use `AsyncLocal<T>`, never `[ThreadStatic]`.** `[ThreadStatic]` does not flow across `await`, so the override silently disappears mid-test.
- **Preserve the semantics of the member you are replacing.** Substituting `DateTime.UtcNow` with a local-time source changes the `DateTimeKind` every existing caller and stored value depends on — pair `UtcNow` with `GetUtcNow()`, and `Now` with `GetLocalNow()`.

The same shape works for non-time statics: swap `TimeProvider.System.GetUtcNow()` for the real static call and keep the override slot, the disposable scope, and the original semantics.

### Step 6: Place generated files

Generate files following the project's existing conventions:
- If there is an `Abstractions/` or `Interfaces/` folder, place the interface there
- If there is an `Infrastructure/` or `Services/` folder, place the implementation there
- Otherwise, create files next to the code that uses the static

Always generate:
1. The interface file (or adoption instructions for built-in abstractions)
2. The default implementation file
3. The DI registration snippet (as a code comment at the bottom of the implementation, or as separate instructions) — **skip this one entirely on the ambient-seam path**: there is no container to register into, and offering one anyway is the failure mode that made a user ask for the seam in the first place

## Validation

- [ ] Generated interface only wraps statics that were actually detected (not speculative)
- [ ] Default implementation delegates to the real static with no behavior changes
- [ ] DI registration uses `AddSingleton` for stateless wrappers, `AddTransient` for stateful ones
- [ ] NuGet packages are recommended where established libraries exist (System.IO.Abstractions, etc.)
- [ ] For .NET 8+, `TimeProvider` is recommended over custom `ISystemClock`
- [ ] Ambient context pattern includes `AsyncLocal<T>`, a scoped `IDisposable` that restores the previous value, and trade-off explanation
- [ ] On the ambient-seam path, no `IServiceCollection` registration is proposed and the replaced member's semantics (`UtcNow` vs `Now`, and its `DateTimeKind`) are preserved

## Common Pitfalls

| Pitfall | Solution |
|---------|----------|
| Declining because the project has no DI container | The ambient seam in Step 5 is the answer for that case — offer it instead of asking the user to adopt a container |
| Wrapping ALL members of a static class | Only wrap methods actually called in the codebase |
| Custom time wrapper on .NET 8+ | Use built-in `TimeProvider` instead |
| Custom file system wrapper | Prefer `System.IO.Abstractions` NuGet — battle-tested, complete |
| Registering scoped when singleton suffices | Stateless wrappers should be `AddSingleton` |
| Forgetting test helper packages | `Microsoft.Extensions.TimeProvider.Testing` for time, `System.IO.Abstractions.TestingHelpers` for filesystem |
| Ambient context without `AsyncLocal` | Non-async `[ThreadStatic]` breaks with `async`/`await` — always use `AsyncLocal<T>` |
