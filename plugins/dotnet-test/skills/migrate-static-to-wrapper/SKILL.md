---
name: migrate-static-to-wrapper
description: >
  Replace existing static dependency call sites with a wrapper or built-in
  abstraction that already exists or is registered in DI, across a bounded scope
  (file, project, namespace).
  USE FOR: replace DateTime.UtcNow/DateTime.Now with TimeProvider and add the
  constructor parameter, migrate static call sites to a wrapper already in DI,
  bulk replace File.* with IFileSystem, scoped migration of statics in only
  certain files, update unit tests to a fake time source, make an existing
  static or utility class testable by adding an ambient
  TimeProvider/IFileSystem seam while every current call site keeps compiling,
  behavior-preserving time refactors that must keep the same DateTimeKind.
  DO NOT USE FOR: detecting statics (use detect-static-dependencies), designing a
  brand-new wrapper interface that does not exist yet (use
  generate-testability-wrappers), migrating between test frameworks.
license: MIT
---

# Migrate Static to Wrapper

Perform mechanical, codemod-style replacement of static dependency call sites with calls to injected wrapper interfaces or built-in abstractions. Operates on a bounded scope (single file, project, or namespace) so migrations can be done incrementally.

## When to Use

- After wrappers have been generated (via `generate-testability-wrappers`) or built-in abstractions identified
- Migrating `DateTime.UtcNow` → `TimeProvider.GetUtcNow()` across a project
- Migrating `File.*` → `IFileSystem.File.*` across a namespace
- Adding constructor injection for the new abstraction to affected classes
- Making a `static` utility class testable by adding an ambient seam (Step 3) while its existing call sites keep
  compiling unchanged
- Incremental migration: one project or namespace at a time

## When Not to Use

- No wrapper or abstraction exists yet and one must be designed from scratch (use `generate-testability-wrappers` first).
  A built-in abstraction such as `TimeProvider` or `IFileSystem` always counts as existing.
- The user wants to detect statics, not migrate them (use `detect-static-dependencies`)
- Migrating between test frameworks (use the appropriate migration skill)

> A class that is `static`, or a project with no DI container, is **not** a reason to skip this skill — that is exactly
> what the ambient seam in Step 3 is for. Use it whenever the call sites must keep compiling unchanged.

## Inputs

| Input | Required | Description |
|-------|----------|-------------|
| Static pattern | Yes | What to replace (e.g., `DateTime.UtcNow`, `File.ReadAllText`) |
| Replacement abstraction | Yes | What to use instead (e.g., `TimeProvider`, `IFileSystem`) |
| Scope | Yes | File path, project (.csproj), namespace, or directory to migrate |
| Injection strategy | No | `constructor` (default), `primary-constructor`, or `ambient` |

## Workflow

### Step 1: Verify prerequisites

Before modifying any code:

1. **Confirm the wrapper/abstraction exists**: Check that the interface or built-in abstraction is available in the project. For `TimeProvider`, verify the target framework is .NET 8+ or `Microsoft.Bcl.TimeProvider` is referenced. For `System.IO.Abstractions`, verify the NuGet package is referenced.

2. **Confirm DI registration exists**: Check `Program.cs` or `Startup.cs` for the service registration. If missing, add it before proceeding.

3. **Identify all files in scope**: List the `.cs` files that will be modified. Exclude test projects, `obj/`, `bin/`, and generated code.

### Step 2: Plan the migration for each file

**Migrate exactly what was asked — nothing adjacent.** If the user named a member (`DateTime.UtcNow`), migrate only that member and leave siblings such as `DateTime.Now` untouched. If the user named files, do not touch other files. Never migrate a call site whose comment or name marks it as deliberate (e.g. `// intentional local time`). List everything you deliberately left alone under "Remaining (out of scope)" so the user can ask for it in a follow-up; suggesting is fine, silently widening the scope is not.

For each file containing the static pattern, determine:

1. **Which class(es) contain the call sites** — identify the class declarations
2. **Whether the class already has the dependency injected** — check constructors for existing `TimeProvider`, `IFileSystem`, etc. parameters
3. **The replacement expression** for each call site

#### Replacement mapping

| Category | Original | DI replacement |
|----------|----------|----------------|
| Time | `DateTime.Now` | `_timeProvider.GetLocalNow().LocalDateTime` |
| Time | `DateTime.UtcNow` | `_timeProvider.GetUtcNow().UtcDateTime` |
| Time | `DateTime.Today` | `_timeProvider.GetLocalNow().LocalDateTime.Date` |
| Time | `DateTimeOffset.Now` | `_timeProvider.GetLocalNow()` |
| Time | `DateTimeOffset.UtcNow` | `_timeProvider.GetUtcNow()` |
| File | `File.ReadAllText(path)` | `_fileSystem.File.ReadAllText(path)` |
| File | `File.WriteAllText(path, text)` | `_fileSystem.File.WriteAllText(path, text)` |
| File | `File.Exists(path)` | `_fileSystem.File.Exists(path)` |
| File | `Directory.Exists(path)` | `_fileSystem.Directory.Exists(path)` |
| Env | `Environment.GetEnvironmentVariable(name)` | `_env.GetEnvironmentVariable(name)` |
| Console | `Console.WriteLine(msg)` | `_console.WriteLine(msg)` |
| Process | `Process.Start(info)` | `_processRunner.Start(info)` |

Apply the same pattern for other members in each category.

> **Preserve `DateTimeKind` — this is the most common silent regression.** `TimeProvider.GetUtcNow()` / `GetLocalNow()` return a `DateTimeOffset`. Converting back to `DateTime` **must keep the original `Kind`**, otherwise you introduce a behavioral change even though the code still compiles:
>
> - `DateTime.UtcNow` has `Kind == Utc` → use `.UtcDateTime` (**not** `.DateTime`, which yields `Kind == Unspecified`).
> - `DateTime.Now` has `Kind == Local` → use `.LocalDateTime` (**not** `.DateTime`).
> - When a call site consumes a `DateTimeOffset` directly (a field/parameter/return already typed `DateTimeOffset`), drop the `.UtcDateTime`/`.LocalDateTime` suffix and assign the `DateTimeOffset` as-is — don't force it back through `DateTime`.
>
> Match the **target member's type**: if the surrounding field/property is `DateTime`, keep it `DateTime` (via the Kind-correct property above); do not change it to `DateTimeOffset` as part of a "mechanical" migration — that is a design change, not a delegation.

### Step 3: Add constructor injection

Add the new dependency following the class's existing pattern:

- **Primary constructor** (C# 12+): Add parameter to primary constructor: `public class OrderProcessor(ILogger<OrderProcessor> logger, TimeProvider timeProvider)`
- **Traditional constructor**: Add `private readonly` field + constructor parameter, matching the existing field naming convention (`_camelCase` or `m_camelCase`)

#### Static classes: use ambient context (no constructor injection)

A `static` class with only static members **cannot** receive constructor injection — adding an instance constructor or instance field would break it. Do **not** convert it to a non-static class just to inject the dependency; that changes its design and every call site. Instead, apply the **ambient context** pattern: expose a static, settable seam that defaults to the real implementation and is overridden once at composition/test setup.

When the user wants to keep the class static, the ambient seam below **is the answer** — present it as *the* solution and implement it directly. Do **not** hedge by offering "convert it to a non-static class" or "pass `TimeProvider` as a method parameter" as co-equal alternatives; those change the class's design or public API and are not what was asked. Lead with the seam, then note the parallelism trade-off.

```csharp
public static class TimestampFormatter
{
    // Ambient seam — defaults to the real clock, swap in tests.
    public static TimeProvider Clock { get; set; } = TimeProvider.System;

    public static string Now() => Clock.GetUtcNow().ToString("O");
}
```

- Production: leave `Clock` at its `TimeProvider.System` default, or assign the DI-resolved `TimeProvider` once at startup (`TimestampFormatter.Clock = app.Services.GetRequiredService<TimeProvider>();`).
- Tests: override `Clock` with a `FakeTimeProvider` and **always restore it in a `finally`** so a failing assertion can't leak the fake into other tests:

  ```csharp
  var original = TimestampFormatter.Clock;
  TimestampFormatter.Clock = new FakeTimeProvider(instant);
  try
  {
      // exercise code under test
  }
  finally
  {
      TimestampFormatter.Clock = original;
  }
  ```

- **Parallelism caveat**: a mutable static seam is process-global. Tests that mutate it must **not** run in parallel with each other (or with code that reads it) — put them in a non-parallel collection/class (e.g. xUnit `[Collection]` with parallelization disabled, or MSTest `[DoNotParallelize]`). Only if the class is *not* required to stay static and its tests must run fully parallel should you consider converting the caller to an instance with constructor injection instead — otherwise keep the ambient seam.
- The same seam works for other statics (`IFileSystem`, custom wrappers): a `public static <Abstraction> X { get; set; }` defaulting to the real implementation, with the same restore-in-`finally` and non-parallel discipline.

### Step 4: Replace call sites

Perform each replacement mechanically. For each call site:

1. Replace the static call with the wrapper call
2. Preserve the surrounding code structure (whitespace, comments, chaining)
3. Add required `using` directives if not already present

#### Adding using directives

| Abstraction | Using directive |
|------------|-----------------|
| `TimeProvider` | None (in `System` namespace) |
| `IFileSystem` | `using System.IO.Abstractions;` |
| `IHttpClientFactory` | `using System.Net.Http;` (usually already present) |
| Custom wrappers | `using <wrapper namespace>;` |

### Step 5: Update affected test files

If test files exist for the migrated classes:

1. **Update constructor calls** — add the new parameter to test class instantiation
2. **Use test doubles**:
   - `TimeProvider` → `new FakeTimeProvider()` from `Microsoft.Extensions.TimeProvider.Testing`
   - `IFileSystem` → `new MockFileSystem()` from `System.IO.Abstractions.TestingHelpers`
   - Custom wrappers → `new Mock<IWrapperName>()` or hand-rolled fake

### Step 6: Build verification

After all changes in the current scope:

```bash
dotnet build <project.csproj>
```

**Report the build result you actually observed.** Only write "build succeeded" when the command exited 0; if it failed — including restore/NuGet failures such as "assets file not found" — say so, quote the error, and either fix it (`dotnet restore`, add the missing package) or hand the user a precise blocker. A false success claim is worse than an unfinished migration.

If the build fails:
- **Missing using**: Add the required `using` directive
- **Missing NuGet package**: Run `dotnet add package <name>`
- **Constructor mismatch in tests**: Update test instantiation (Step 5)
- **Ambiguous call**: Fully qualify the wrapper call

### Step 7: Report changes

Summarize what was done:

```
## Migration Summary

**Pattern**: DateTime.UtcNow → TimeProvider.GetUtcNow()
**Scope**: MyProject/Services/

### Files Modified (production)
| File | Call Sites Replaced | Injection Added |
|------|--------------------:|:----------------|
| OrderProcessor.cs | 3 | Yes (constructor) |
| NotificationService.cs | 1 | Yes (primary ctor) |

### Files Modified (tests)
| File | Change |
|------|--------|
| OrderProcessorTests.cs | Added FakeTimeProvider parameter |

### Remaining (out of scope)
- MyProject/Legacy/ — 8 call sites not migrated (different namespace)
```

## Validation

- [ ] All call sites in scope were replaced (none missed)
- [ ] No call site outside the requested member/file scope was modified
- [ ] Call sites documented as intentional (e.g. local time) were left untouched and reported
- [ ] Constructor injection added to all affected classes
- [ ] Field naming follows existing class conventions
- [ ] Required `using` directives added
- [ ] Required NuGet packages referenced
- [ ] Build succeeds after migration, and the reported result matches the actual command exit code
- [ ] Test files updated with appropriate test doubles
- [ ] No behavioral changes introduced (wrapper delegates directly to the static)
- [ ] `DateTimeKind` preserved — former `DateTime.UtcNow` stays `Utc` (`.UtcDateTime`), former `DateTime.Now` stays `Local` (`.LocalDateTime`)

## Common Pitfalls

| Pitfall | Solution |
|---------|----------|
| Replacing statics in test code | Only replace in production code; tests should use fakes/mocks |
| Breaking static classes | Static classes can't have constructors — use the ambient context seam (Step 3) instead of converting them to non-static |
| Missing `FakeTimeProvider` NuGet | Add `Microsoft.Extensions.TimeProvider.Testing` to test project |
| Replacing a `DateTime` value with `.DateTime` off a `DateTimeOffset` | `DateTimeOffset.DateTime` returns `Kind == Unspecified` — use `.UtcDateTime` (for former `DateTime.UtcNow`) or `.LocalDateTime` (for former `DateTime.Now`) to preserve the original `DateTimeKind`. Only change the field/return type to `DateTimeOffset` if the user asked for it. |
| Migrating too much at once | Stick to the defined scope — one project or namespace per run |
| Migrating `DateTime.Now` when only `UtcNow` was requested | Respect the literal request; list the other call sites as out-of-scope suggestions instead of rewriting them |
| Claiming "Build succeeded" after a failed restore | Read the exit code and output; report the real failure and fix it or surface it as a blocker |
| Forgetting DI registration | Always verify `Program.cs`/`Startup.cs` has the registration before replacing call sites |
