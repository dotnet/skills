---
name: detect-static-dependencies
description: >
  Scan C# source files for hard-to-test static dependencies — DateTime.Now/UtcNow,
  File.*, Directory.*, Environment.*, HttpClient, Console.*, Process.*, and other
  untestable statics. Produces a ranked report of static call sites by frequency.
  USE FOR: find untestable statics, scan for static dependencies, testability audit,
  identify hard-to-mock code, find DateTime.Now usage, detect static coupling,
  testability report, static analysis for testability.
  DO NOT USE FOR: generating wrappers (use generate-testability-wrappers),
  migrating code (use migrate-static-to-wrapper), general code review,
  or finding statics that are already behind abstractions.
---

# Detect Static Dependencies

Scan a C# codebase for calls to hard-to-test static APIs and produce a ranked report showing which statics appear most frequently, which files are most affected, and which abstractions already exist in the .NET ecosystem to replace them.

## When to Use

- Auditing a project's testability before adding unit tests
- Understanding the scope of static coupling in a legacy codebase
- Prioritizing which statics to wrap first (highest-frequency wins)
- Creating a migration plan for incremental testability improvements

## When Not to Use

- The user wants wrappers generated (hand off to `generate-testability-wrappers`)
- The user wants mechanical migration done (hand off to `migrate-static-to-wrapper`)
- The statics are already behind interfaces or `TimeProvider`
- The code is not C# / .NET

## Inputs

| Input | Required | Description |
|-------|----------|-------------|
| Target path | Yes | A file, directory, project (.csproj), or solution (.sln) to scan |
| Exclusion patterns | No | Glob patterns to skip (e.g., `**/obj/**`, `**/Migrations/**`) |
| Category filter | No | Limit to specific categories: `time`, `filesystem`, `environment`, `network`, `console`, `process` |

## Workflow

### Step 1: Determine scan scope

Resolve the target to a set of `.cs` files:
- If a `.cs` file, scan that single file.
- If a directory, scan all `.cs` files recursively (excluding `obj/`, `bin/`).
- If a `.csproj`, find its directory and scan `.cs` files within.
- If a `.sln`, parse it, find all project directories, and scan `.cs` files across all projects.

Always exclude `obj/`, `bin/`, and any user-specified exclusion patterns.

### Step 2: Search for static dependency patterns

Scan each file for calls matching these categories:

#### Time
| Pattern | Replacement |
|---------|-------------|
| `DateTime.Now` | `TimeProvider.GetLocalNow()` |
| `DateTime.UtcNow` | `TimeProvider.GetUtcNow()` |
| `DateTime.Today` | `TimeProvider.GetLocalNow().Date` |
| `DateTimeOffset.Now` | `TimeProvider.GetLocalNow()` |
| `DateTimeOffset.UtcNow` | `TimeProvider.GetUtcNow()` |
| `Task.Delay(` | `TimeProvider.Delay()` |
| `new CancellationTokenSource(TimeSpan` | `TimeProvider.CreateCancellationTokenSource()` |

#### File System
| Pattern | Replacement |
|---------|-------------|
| `File.ReadAllText(` | `IFileSystem` (System.IO.Abstractions) |
| `File.WriteAllText(` | `IFileSystem` |
| `File.Exists(` | `IFileSystem` |
| `File.Delete(` | `IFileSystem` |
| `File.Copy(` | `IFileSystem` |
| `File.Move(` | `IFileSystem` |
| `Directory.Exists(` | `IFileSystem` |
| `Directory.CreateDirectory(` | `IFileSystem` |
| `Directory.GetFiles(` | `IFileSystem` |
| `Directory.Delete(` | `IFileSystem` |
| `Path.Combine(` | `IFileSystem.Path` |
| `Path.GetTempPath(` | `IFileSystem.Path` |

#### Environment
| Pattern | Replacement |
|---------|-------------|
| `Environment.GetEnvironmentVariable(` | `IEnvironmentProvider` wrapper |
| `Environment.SetEnvironmentVariable(` | `IEnvironmentProvider` wrapper |
| `Environment.MachineName` | `IEnvironmentProvider` wrapper |
| `Environment.UserName` | `IEnvironmentProvider` wrapper |
| `Environment.CurrentDirectory` | `IEnvironmentProvider` wrapper |
| `Environment.Exit(` | `IEnvironmentProvider` wrapper |

#### Network
| Pattern | Replacement |
|---------|-------------|
| `new HttpClient(` | `IHttpClientFactory` |
| `HttpClient.GetAsync(` | `IHttpClientFactory` |
| `HttpClient.PostAsync(` | `IHttpClientFactory` |
| `HttpClient.SendAsync(` | `IHttpClientFactory` |
| `Dns.GetHostEntry(` | Custom wrapper |
| `Dns.GetHostAddresses(` | Custom wrapper |

#### Console
| Pattern | Replacement |
|---------|-------------|
| `Console.WriteLine(` | `IConsole` wrapper or `ILogger` |
| `Console.ReadLine(` | `IConsole` wrapper |
| `Console.Write(` | `IConsole` wrapper |
| `Console.ReadKey(` | `IConsole` wrapper |

#### Process
| Pattern | Replacement |
|---------|-------------|
| `Process.Start(` | `IProcessRunner` wrapper |
| `Process.GetCurrentProcess(` | `IProcessRunner` wrapper |
| `Process.GetProcessesByName(` | `IProcessRunner` wrapper |

### Step 3: Aggregate and rank results

Count each static call pattern across the entire scan scope. Produce a summary with:

1. **Category summary** — total call sites per category (time, filesystem, env, etc.)
2. **Top patterns** — the 10 most frequent individual patterns ranked by count
3. **Most affected files** — files with the highest number of static dependencies
4. **Existing abstractions available** — for each category, note the recommended .NET abstraction:
   - Time → `TimeProvider` (built-in since .NET 8)
   - File system → `System.IO.Abstractions` (NuGet package)
   - HTTP → `IHttpClientFactory` (built-in)
   - Environment → custom `IEnvironmentProvider`
   - Console → custom `IConsole` or `ILogger`
   - Process → custom `IProcessRunner`

### Step 4: Present the report

Format the output as a structured report:

```
## Static Dependency Report

**Scope**: <project/solution name>
**Files scanned**: <count>
**Total static call sites**: <count>

### Category Summary
| Category     | Call Sites | Recommended Abstraction |
|-------------|-----------|------------------------|
| Time         | 42        | TimeProvider (.NET 8+) |
| File System  | 31        | System.IO.Abstractions |
| Environment  | 12        | IEnvironmentProvider   |
| ...          | ...       | ...                    |

### Top 10 Patterns
| # | Pattern             | Count | Files |
|---|---------------------|-------|-------|
| 1 | DateTime.UtcNow     | 28    | 14    |
| 2 | File.ReadAllText    | 18    | 9     |
| ...                                      |

### Most Affected Files
| File                          | Static Calls | Categories          |
|-------------------------------|-------------|---------------------|
| Services/OrderProcessor.cs    | 12          | Time, FileSystem    |
| ...                                                               |

### Migration Priority
1. **Time** (42 sites) — Use `TimeProvider`, zero NuGet dependencies on .NET 8+
2. **File System** (31 sites) — Use `System.IO.Abstractions` NuGet package
3. ...
```

### Step 5: Suggest next steps

Based on the report, recommend:
- Which category to tackle first (fewest dependencies, best built-in support)
- Whether to use `generate-testability-wrappers` for custom wrapper generation
- Whether to use `migrate-static-to-wrapper` for mechanical bulk migration

## Validation

- [ ] All `.cs` files in scope were scanned (check count)
- [ ] Report includes category totals, top patterns, and affected files
- [ ] Each detected pattern has a recommended replacement listed
- [ ] `obj/` and `bin/` directories were excluded
- [ ] Migration priority is ordered by impact (count × ease of replacement)

## Common Pitfalls

| Pitfall | Solution |
|---------|----------|
| Scanning `obj/` or generated code | Always exclude `obj/`, `bin/`, and `*.Designer.cs` |
| Counting wrapped calls as statics | Check if the call is behind an interface or injected service before counting |
| Missing statics inside lambdas/LINQ | Search covers all code within `.cs` files, including lambdas |
| Recommending `TimeProvider` on < .NET 8 | Check `TargetFramework` in `.csproj` — if < net8.0, recommend `NodaTime.IClock` or custom `ISystemClock` |
| Ignoring test projects | Only scan production code — exclude `*.Tests.csproj` projects from the scan |
