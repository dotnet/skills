---
name: generate-testability-wrappers
description: >
  Generate wrapper interfaces and DI registration for hard-to-test static dependencies in C#.
  Produces IFileSystem, IEnvironmentProvider, IConsole, IProcessRunner wrappers, or guides adoption
  of TimeProvider and IHttpClientFactory.
  USE FOR: generate wrapper for static, create IFileSystem wrapper, wrap DateTime.Now,
  make static testable, make class testable, create abstraction for File.*, generate
  DI registration, TimeProvider adoption, IHttpClientFactory setup, testability wrapper,
  mock-friendly interface, mock time in tests, create the right abstraction to mock,
  how to mock DateTime, test code using File.ReadAllText, what abstraction for Environment,
  how to make statics injectable, adopt System.IO.Abstractions, make file calls testable.
  DO NOT USE FOR: detecting statics (use detect-static-dependencies), migrating call
  sites (use migrate-static-to-wrapper), general interface design not about testability.
---

# Generate Testability Wrappers

Generate wrapper interfaces, default implementations, and DI service registration code for untestable static dependencies. For statics that already have .NET built-in abstractions (`TimeProvider`, `IHttpClientFactory`), guide adoption of the built-in. For statics without built-in alternatives, generate custom minimal wrappers.

## When to Use

- After running `detect-static-dependencies` and identifying which statics to wrap
- When the user asks to make a class testable by replacing statics with injected abstractions
- When adopting `TimeProvider` (.NET 8+) or `System.IO.Abstractions`
- When creating a custom wrapper for `Environment.*`, `Console.*`, or `Process.*`

## When Not to Use

- The user wants to find statics first (use `detect-static-dependencies`)
- The user wants to bulk-replace call sites (use `migrate-static-to-wrapper`)
- The static is already behind an interface
- The project does not use dependency injection and the user does not want to add it

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

### Step 6: Place generated files

Generate files following the project's existing conventions:
- If there is an `Abstractions/` or `Interfaces/` folder, place the interface there
- If there is an `Infrastructure/` or `Services/` folder, place the implementation there
- Otherwise, create files next to the code that uses the static

Always generate:
1. The interface file (or adoption instructions for built-in abstractions)
2. The default implementation file
3. The DI registration snippet (as a code comment at the bottom of the implementation, or as separate instructions)

## Validation

- [ ] Generated interface only wraps statics that were actually detected (not speculative)
- [ ] Default implementation delegates to the real static with no behavior changes
- [ ] DI registration uses `AddSingleton` for stateless wrappers, `AddTransient` for stateful ones
- [ ] NuGet packages are recommended where established libraries exist (System.IO.Abstractions, etc.)
- [ ] For .NET 8+, `TimeProvider` is recommended over custom `ISystemClock`
- [ ] Ambient context pattern includes `AsyncLocal<T>`, scoped disposal, and trade-off explanation

## Common Pitfalls

| Pitfall | Solution |
|---------|----------|
| Wrapping ALL members of a static class | Only wrap methods actually called in the codebase |
| Custom time wrapper on .NET 8+ | Use built-in `TimeProvider` instead |
| Custom file system wrapper | Prefer `System.IO.Abstractions` NuGet — battle-tested, complete |
| Registering scoped when singleton suffices | Stateless wrappers should be `AddSingleton` |
| Forgetting test helper packages | `Microsoft.Extensions.TimeProvider.Testing` for time, `System.IO.Abstractions.TestingHelpers` for filesystem |
| Ambient context without `AsyncLocal` | Non-async `[ThreadStatic]` breaks with `async`/`await` — always use `AsyncLocal<T>` |

