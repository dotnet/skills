---
name: migrate-static-to-wrapper
description: >
  ALWAYS USE when asked to migrate, replace, or make testable existing C# static
  calls with a named wrapper or built-in abstraction: DateTime.UtcNow/Now or
  DateTimeOffset.UtcNow to TimeProvider/IClock, File.* to IFileSystem or an
  existing store such as ITextFileStore, and Environment.* to an existing reader
  such as IEnvironmentReader. Covers scoped files/projects, constructor
  injection, replacing temp-file or process-environment tests with fakes,
  "already registered" abstractions, and static classes whose callers/signatures
  must stay unchanged. Preserves DateTimeKind and call count. DO NOT USE for finding
  statics (detect-static-dependencies), choosing/designing a new wrapper
  (generate-testability-wrappers), behavior tests with no chosen seam
  (testability-obstacle), or test-framework migration.
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
- Updating affected tests with fakes when the requested migration names the
  replacement abstraction

## When Not to Use

- No wrapper or abstraction exists yet and one must be designed from scratch (use `generate-testability-wrappers` first).
  A built-in abstraction such as `TimeProvider` or `IFileSystem` always counts as existing.
- The user wants to detect statics, not migrate them (use `detect-static-dependencies`)
- Migrating between test frameworks (use the appropriate migration skill)
- The user primarily asks for a deterministic behavior test and has not selected
  the production seam (use `testability-obstacle`)

> A class that is `static`, or a project with no DI container, is **not** a reason to skip this skill — that is exactly
> what the ambient seam in Step 3 is for. Use it whenever the call sites must keep compiling unchanged.

## Inputs

| Input | Required | Description |
|-------|----------|-------------|
| Static pattern | No | Infer from the request and discovered call sites (e.g., `DateTime.UtcNow`, `File.ReadAllText`) |
| Replacement abstraction | No | Infer from the request and existing project abstractions; stop only when no named/existing abstraction is available |
| Scope | No | Infer from the requested file/project/namespace, otherwise discover the narrowest relevant workspace scope |
| Injection strategy | No | `constructor` (default), `primary-constructor`, or `ambient` |

## Workflow

### Non-negotiable migration boundaries

- **Missing abstraction means stop.** If the named interface/package is absent and
  the request only authorizes call-site replacement, do not add a package, invent
  a local lookalike interface, or edit production code. Report the exact missing
  prerequisite and the authorization needed to continue.
- **One source read stays one replacement read.** Do not hoist or coalesce calls,
  even when sharing a captured timestamp looks cleaner.
- **The requested scope is exhaustive and exclusive.** Replace every named call
  in scope and no adjacent member or file.
- **Repository-backed requests require repository work.** Start by discovering
  files from the current workspace. Do not claim the repository is unavailable
  or ask the user for a path or file contents until workspace-relative discovery
  found no target. Do not say work was implemented unless the diff proves it.
- **Discovered workspace files must be completed in this turn when permitted.**
  Use a host-native shell reader (`sed`/`cat` or `Get-Content`) only after a
  confirmed reader availability, transport, or path-normalization failure and
  only after verifying the canonical path remains inside the current workspace.
  Stop on content-exclusion, permission/policy, workspace-boundary, or unknown
  read failures. Use a shell edit fallback only for a confirmed editor
  availability, transport, or path-normalization failure, never for a stale
  context, concurrent change, permission/policy denial, or path-boundary error.
  Before fallback, resolve the canonical path inside the current workspace,
  freshly read the file, and require an anchored replacement with the expected
  old text and exact match count; abort if either changed. Then re-open the file,
  inspect the diff, and validate. Do not ask the user to paste a readable
  discovered file or report a proposed patch as completed work.

### Step 1: Verify prerequisites

Before modifying any code:

1. **Confirm the wrapper/abstraction exists**: Check that the interface or built-in abstraction is available in the project. For `TimeProvider`, verify the target framework is .NET 8+ or `Microsoft.Bcl.TimeProvider` is referenced. For `System.IO.Abstractions`, verify the NuGet package is referenced. A package that could provide an abstraction is not the same as an abstraction already available to this project.

2. **Confirm production composition exists**: Check `Program.cs`, `Startup.cs`, or manual construction sites. If package, wrapper, or registration work is missing, add it only when the user explicitly authorized those dependency/composition changes. Otherwise stop before editing call sites and report the exact prerequisite; do not turn a scoped migration into first-time abstraction design.

3. **Identify all files in scope**: List the `.cs` files that will be modified. Exclude test projects, `obj/`, `bin/`, and generated code.

4. **Lock and count the member set before editing**: Use the exact member named
   by the user, or infer the smallest unambiguous set from the request and
   discovered call sites. Record that set, then search every member and capture
   the file/line inventory. Do not change the set mid-edit or infer counts from
   a partial read.

### Step 2: Plan the migration for each file

**Migrate exactly what was asked — nothing adjacent.** If the user named a member (`DateTime.UtcNow`), migrate only that member and leave siblings such as `DateTime.Now` untouched. If the user named files, do not touch other files. Preserve a call site whose comment or name marks it as deliberate (e.g. `// intentional local time`) unless the user explicitly names that site and requests a semantics-preserving migration. List everything you deliberately left alone under "Remaining (out of scope)" so the user can ask for it in a follow-up; suggesting is fine, silently widening the scope is not.

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
>
> Preserve the **number, order, and location of reads** as well as the value type.
> Replace each original clock read in place with one provider read. Do not hoist,
> cache, or coalesce two reads into a shared `now` local, even when they are in the
> same object initializer or method. Two consecutive `DateTime.UtcNow` calls could
> observe different instants; making `CreatedAt` and `ExpiresAt` derive from one
> captured value is a behavior change, not a mechanical migration. Reuse a value
> only when the original code already captured and reused one.

### Step 3: Add constructor injection

Add the new dependency following the class's existing pattern:

- **Primary constructor** (C# 12+): Add parameter to primary constructor: `public class OrderProcessor(ILogger<OrderProcessor> logger, TimeProvider timeProvider)`
- **Traditional constructor**: Add `private readonly` field + constructor parameter, matching the existing field naming convention (`_camelCase` or `m_camelCase`)

#### Static classes: use ambient context (no constructor injection)

A `static` class with only static members **cannot** receive constructor injection — adding an instance constructor or instance field would break it. Do **not** convert it to a non-static class just to inject the dependency; that changes its design and every call site. Instead, apply a scoped ambient seam that defaults to the real implementation and can be overridden without leaking process-global state.

When the user wants to keep the class static, the ambient seam below **is the answer** — present it as *the* solution and implement it directly. Do **not** hedge by offering "convert it to a non-static class" or "pass `TimeProvider` as a method parameter" as co-equal alternatives; those change the class's design or public API and are not what was asked. Lead with the seam, then note the parallelism trade-off.

```csharp
public static class TimestampFormatter
{
  private static readonly AsyncLocal<TimeProvider?> s_clock = new();

  private static TimeProvider Clock => s_clock.Value ?? TimeProvider.System;

  public static string Now() => Clock.GetUtcNow().ToString("O");

  public static IDisposable OverrideClock(TimeProvider clock)
  {
      ArgumentNullException.ThrowIfNull(clock);
      var previous = s_clock.Value;
      s_clock.Value = clock;
      return new Scope(() => s_clock.Value = previous);
  }

  private sealed class Scope : IDisposable
  {
      private Action? _restore;

      public Scope(Action restore)
      {
          _restore = restore;
      }

      public void Dispose() => Interlocked.Exchange(ref _restore, null)?.Invoke();
  }
}
```

- Production reads `TimeProvider.System` whenever no override is active; no startup mutation is required.
- Tests create a fresh fake/provider per async flow and dispose the returned scope. Nested disposal restores the outer provider.
- `AsyncLocal<T>` keeps independently established test flows isolated across `await`. Do not store a mutable stack/list in the slot or mutate one fake inherited by multiple child flows.
- Add focused tests for substitution, nested restoration, and parallel async isolation. A build-only check does not prove this seam.
- The same shape works for other statics (`IFileSystem`, custom wrappers): store the abstraction value in `AsyncLocal<T>`, default to the real implementation, and restore the previous value from the scope.

### Step 4: Replace call sites

Perform each replacement mechanically. For each call site:

1. Replace the static call with the wrapper call
2. Preserve the surrounding expression structure and evaluation order; one
   original dependency read remains one wrapper read
3. Add required `using` directives if not already present

After editing, repeat the exact search and require zero occurrences in every
in-scope production file. Re-open each changed file and compare the result to
the pre-edit inventory. A summary count is not evidence if one method was
silently missed.

Also verify the exclusive side of the scope: search or compare every file the
user explicitly said to leave alone and require its original static calls and
content to remain. For a single-file migration, report both numbers even when
they are small: `N/N` in-scope calls replaced and `M` named out-of-scope calls
preserved.

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

Preserve every observable branch that depended on the original static result. For
example, migrating `Environment.GetEnvironmentVariable(name) ?? "production"`
requires tests for both a configured value and `null`/missing input selecting the
fallback. A fake-only happy path is not enough to prove a mechanical migration.
When tests already exist, preserve their framework and assertion style, but make
the replacement dependency observable: include at least one configured/fake
value assertion and one fallback or error-path assertion where the original
static API exposed both outcomes. Merely making the old tests compile is not
complete migration evidence.

When the request explicitly converts affected unit tests away from real file or
environment access, prove those tests no longer touch the process-global
dependency: search them for temp-file, real-disk, or environment-mutation APIs
after editing. Preserve intentional integration tests outside that requested
scope. Report the deterministic fake's configured and fallback/error cases
rather than only saying that a fake was added.

### Step 6: Build verification

After all changes in the current scope, build the affected production project
and run the narrowest affected test project whenever tests exist or were
changed:

```bash
dotnet build <project.csproj>
dotnet test <affected-test-project.csproj>
```

**Report the build result you actually observed.** Only write "build succeeded" when the command exited 0; if it failed — including restore/NuGet failures such as "assets file not found" — say so, quote the error, and either fix it (`dotnet restore`, add the missing package) or hand the user a precise blocker. A false success claim is worse than an unfinished migration.

If the build fails:
- **Missing using**: Add the required `using` directive
- **Missing NuGet package**: add it only when dependency changes were explicitly authorized; otherwise report the unmet prerequisite and stop
- **Constructor mismatch in tests**: Update test instantiation (Step 5)
- **Ambiguous call**: Fully qualify the wrapper call

Do not substitute a successful build for the requested test run. When migration
changes constructor calls, fakes, process-global state, or real I/O, only the
targeted tests prove the complete path. If the test command is blocked, report
that blocker rather than claiming the migration is fully validated.

### Step 7: Report changes

Summarize what was done. Even for one production file and one test file, include
the exact in-scope replacement count, the named out-of-scope files or calls
verified unchanged, and the targeted build/test result:

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
- [ ] A before/after exact-member search proves the in-scope occurrence count
      reached zero
- [ ] No call site outside the requested member/file scope was modified
- [ ] Call sites documented as intentional were left untouched and reported unless
      the user explicitly named them for semantics-preserving migration
- [ ] Constructor injection added to all affected classes
- [ ] Field naming follows existing class conventions
- [ ] Required `using` directives added
- [ ] Required NuGet packages referenced
- [ ] Build succeeds after migration, and the reported result matches the actual command exit code
- [ ] Test files updated with appropriate test doubles
- [ ] Existing configured, fallback/null, and error branches still have direct test evidence
- [ ] The affected targeted tests ran successfully when tests exist or changed
- [ ] No behavioral changes introduced (wrapper delegates directly to the static)
- [ ] Static reads were replaced one-for-one; none were hoisted, cached, or coalesced
- [ ] `DateTimeKind` preserved — former `DateTime.UtcNow` stays `Utc` (`.UtcDateTime`), former `DateTime.Now` stays `Local` (`.LocalDateTime`)

## Common Pitfalls

| Pitfall | Solution |
|---------|----------|
| Replacing statics in test code | Only replace in production code; tests should use fakes/mocks |
| Breaking static classes | Static classes can't have constructors — use the ambient context seam (Step 3) instead of converting them to non-static |
| Missing `FakeTimeProvider` NuGet | Add `Microsoft.Extensions.TimeProvider.Testing` to test project |
| Replacing a `DateTime` value with `.DateTime` off a `DateTimeOffset` | `DateTimeOffset.DateTime` returns `Kind == Unspecified` — use `.UtcDateTime` (for former `DateTime.UtcNow`) or `.LocalDateTime` (for former `DateTime.Now`) to preserve the original `DateTimeKind`. Only change the field/return type to `DateTimeOffset` if the user asked for it. |
| Capturing one provider value for multiple original clock reads | Replace each read in place. Coalescing reads changes observable timing even when it looks cleaner. |
| Migrating too much at once | Stick to the defined scope — one project or namespace per run |
| Migrating `DateTime.Now` when only `UtcNow` was requested | Respect the literal request; list the other call sites as out-of-scope suggestions instead of rewriting them |
| Claiming "Build succeeded" after a failed restore | Read the exit code and output; report the real failure and fix it or surface it as a blocker |
| Adding a package during a call-site-only migration | Stop and request authorization or run wrapper/adoption setup first |
| Forgetting production composition | Verify DI registration, manual construction, or the ambient production default before replacing call sites |
