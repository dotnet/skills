---
name: migrate-static-to-wrapper
description: >
  Mechanically replace static dependency call sites with wrapper or built-in
  abstraction calls across a bounded scope (file, project, or namespace).
  Performs codemod-style bulk replacement of DateTime.UtcNow to TimeProvider.GetUtcNow(),
  File.ReadAllText to IFileSystem, and similar transformations. Adds constructor
  injection parameters and updates DI registration.
  USE FOR: replace DateTime.UtcNow with TimeProvider, replace DateTime.Now with
  TimeProvider, migrate static calls to wrapper, bulk replace File.* with IFileSystem,
  codemod static to injectable, add constructor injection for time provider,
  mechanical migration of statics, refactor DateTime to TimeProvider, swap static
  for injected dependency, convert static calls to use abstraction, replace statics
  in a class, migrate one file to TimeProvider, scoped migration, update call sites.
  DO NOT USE FOR: detecting statics (use detect-static-dependencies), generating
  wrappers (use generate-testability-wrappers), migrating between test frameworks.
---

# Migrate Static to Wrapper

Perform mechanical, codemod-style replacement of static dependency call sites with calls to injected wrapper interfaces or built-in abstractions. Operates on a bounded scope (single file, project, or namespace) so migrations can be done incrementally.

## When to Use

- After wrappers have been generated (via `generate-testability-wrappers`) or built-in abstractions identified
- Migrating `DateTime.UtcNow` → `TimeProvider.GetUtcNow()` across a project
- Migrating `File.*` → `IFileSystem.File.*` across a namespace
- Adding constructor injection for the new abstraction to affected classes
- Incremental migration: one project or namespace at a time

## When Not to Use

- No wrapper or abstraction exists yet (use `generate-testability-wrappers` first)
- The user wants to detect statics, not migrate them (use `detect-static-dependencies`)
- The code does not use dependency injection and the user hasn't chosen ambient context
- Migrating between test frameworks (use the appropriate migration skill)

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

For each file containing the static pattern, determine:

1. **Which class(es) contain the call sites** — identify the class declarations
2. **Whether the class already has the dependency injected** — check constructors for existing `TimeProvider`, `IFileSystem`, etc. parameters
3. **The replacement expression** for each call site

#### Replacement mapping

| Category | Original | DI replacement |
|----------|----------|----------------|
| Time | `DateTime.Now` | `_timeProvider.GetLocalNow().DateTime` |
| Time | `DateTime.UtcNow` | `_timeProvider.GetUtcNow().DateTime` |
| Time | `DateTime.Today` | `_timeProvider.GetLocalNow().Date` |
| Time | `DateTimeOffset.UtcNow` | `_timeProvider.GetUtcNow()` |
| File | `File.ReadAllText(path)` | `_fileSystem.File.ReadAllText(path)` |
| File | `File.WriteAllText(path, text)` | `_fileSystem.File.WriteAllText(path, text)` |
| File | `File.Exists(path)` | `_fileSystem.File.Exists(path)` |
| File | `Directory.Exists(path)` | `_fileSystem.Directory.Exists(path)` |
| Env | `Environment.GetEnvironmentVariable(name)` | `_env.GetEnvironmentVariable(name)` |
| Console | `Console.WriteLine(msg)` | `_console.WriteLine(msg)` |
| Process | `Process.Start(info)` | `_processRunner.Start(info)` |

Apply the same pattern for other members in each category.

### Step 3: Add constructor injection

Add the new dependency following the class's existing pattern:

- **Primary constructor** (C# 12+): Add parameter to primary constructor: `public class OrderProcessor(ILogger<OrderProcessor> logger, TimeProvider timeProvider)`
- **Traditional constructor**: Add `private readonly` field + constructor parameter, matching the existing field naming convention (`_camelCase` or `m_camelCase`)

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
- [ ] Constructor injection added to all affected classes
- [ ] Field naming follows existing class conventions
- [ ] Required `using` directives added
- [ ] Required NuGet packages referenced
- [ ] Build succeeds after migration
- [ ] Test files updated with appropriate test doubles
- [ ] No behavioral changes introduced (wrapper delegates directly to the static)

## Common Pitfalls

| Pitfall | Solution |
|---------|----------|
| Replacing statics in test code | Only replace in production code; tests should use fakes/mocks |
| Breaking static classes | Static classes can't have constructors — use ambient context for these |
| Missing `FakeTimeProvider` NuGet | Add `Microsoft.Extensions.TimeProvider.Testing` to test project |
| Replacing in expression-bodied members without updating return type | `DateTime` → `DateTimeOffset` when using `TimeProvider.GetUtcNow()` — verify type compatibility |
| Migrating too much at once | Stick to the defined scope — one project or namespace per run |
| Forgetting DI registration | Always verify `Program.cs`/`Startup.cs` has the registration before replacing call sites |
