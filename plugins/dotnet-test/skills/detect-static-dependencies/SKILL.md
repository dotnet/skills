---
name: detect-static-dependencies
description: >
  ACTIVATION PREREQUISITE: the request or discovered target must explicitly
  identify C#, .NET, `.cs`, or `.csproj`; otherwise stay dormant without
  invoking this skill. USE FOR: locating
  System.DateTime.Now/UtcNow, System.IO.File/Directory, System.Environment,
  HttpClient, Console, or Process usage in C#; auditing C# code for hard-to-test
  framework dependencies; or verifying those C# calls are already abstracted.
  DO NOT USE FOR: any target lacking the activation prerequisite; generating
  wrappers (use generate-testability-wrappers); migrating code (use
  migrate-static-to-wrapper); or general code review.
license: MIT
---

# Detect Static Dependencies

Scan a C# codebase for calls to hard-to-test static APIs and produce a ranked report showing which statics appear most frequently, which files are most affected, and which abstractions already exist in the .NET ecosystem to replace them.

## When to Use

- Auditing a project's testability before adding unit tests
- Understanding the scope of static coupling in a legacy codebase
- Prioritizing which statics to wrap first (highest-frequency wins)
- Creating a migration plan for incremental testability improvements

## Response Guidelines

- Scale the response to the user's request. A question about a specific category (e.g., "find time statics") should focus on that category with file locations and counts, not produce a full report across all categories.
- When the user provides a specific file or directory path, scan only that scope — do not expand to the entire solution unless asked.
- The full structured report format in Step 4 is for comprehensive audit requests. For focused questions, return only the relevant subset (e.g., category summary + affected files for the requested category).

## Execution Contract

- A relative path named in the prompt is enough to start. Discover it with the
  available file-listing tools and scan it immediately; do not ask the user to
  provide or re-upload files before both discovery and a content search fail.
- Start with a recursive, line-numbered content search over eligible `.cs`
  files. Do not search only for the `static` keyword: ambient calls inside
  LINQ expressions, lambdas, callbacks, and interpolated strings usually have
  no `static` modifier.
- If a file-reading tool fails on a path that listing or search proved exists,
  classify the failure before retrying. Fall back to another available
  mechanism such as `rg -n`, grep, or a shell file reader only for confirmed
  tool availability, transport, or path-normalization failures and only after
  verifying the canonical path remains inside the workspace. Stop on
  content-exclusion, permission/policy, workspace-boundary, or unknown failures.
  Search output can seed the occurrence ledger; open only the surrounding code
  needed to verify receiver provenance.
- Never stop after loading this skill or announcing a scan plan. Return the
  completed audit in the same response. If every fallback genuinely fails,
  report the verified partial findings and the exact limitation; do not invent
  findings or replace the audit with a request to rerun.

## When Not to Use

- The user wants wrappers generated (hand off to `generate-testability-wrappers`)
- The user wants mechanical migration done (hand off to `migrate-static-to-wrapper`)
- The statics are already behind interfaces or `TimeProvider`
- The code is not C# / .NET

## Inputs

| Input | Required | Description |
|-------|----------|-------------|
| Target path | No | A file, directory, project (.csproj), or solution (.sln) to scan. Defaults to the current workspace. |
| Exclusion patterns | No | Glob patterns to skip (e.g., `**/obj/**`, `**/Migrations/**`) |
| Category filter | No | Limit to specific categories: `time`, `filesystem`, `environment`, `network`, `console`, `process` |

## Workflow

### Step 1: Determine scan scope

Resolve the target to a set of `.cs` files:
- Treat a prompt-named workspace-relative path as the target; locate it rather
  than asking the user for an absolute path.
- If omitted, scan every eligible `.cs` file under the current workspace; do not
  pick one project and silently omit its siblings.
- If a `.cs` file, scan that single file.
- If a directory, scan all `.cs` files recursively (excluding `obj/`, `bin/`).
- If a `.csproj`, find its directory and scan `.cs` files within.
- If a `.sln`, parse it, find all project directories, and scan `.cs` files across all projects.

Always exclude `obj/`, `bin/`, and any user-specified exclusion patterns.

### Step 2: Search for static dependency patterns

Scan each file for calls matching these categories:

Treat pattern matches as candidates, not findings. Before counting an instance call, trace how its
receiver enters the class. A collaborator supplied through a constructor, parameter, property, or
dependency injection (DI) is already a test seam. In particular, an injected `HttpClient` is
testable with a controlled `HttpMessageHandler`; do not count its calls or recommend replacing it
merely because the injected type is concrete.

| Category | Patterns to search for | Recommended replacement |
|----------|----------------------|------------------------|
| **Time** | `DateTime.Now`, `DateTime.UtcNow`, `DateTime.Today`, `DateTimeOffset.Now`, `DateTimeOffset.UtcNow`, `Task.Delay(`, `new CancellationTokenSource(TimeSpan` | `TimeProvider` (.NET 8+) |
| **File System** | `File.ReadAllText(`, `File.WriteAllText(`, `File.Exists(`, `File.Delete(`, `File.Copy(`, `File.Move(`, `Directory.Exists(`, `Directory.CreateDirectory(`, `Directory.GetFiles(`, `Directory.Delete(`, `Path.GetTempPath(`, and instance members that hit the disk (`new FileInfo(...)`, `new DirectoryInfo(...)`, `.LastWriteTimeUtc`, `new StreamReader(path)`) | `IFileSystem` (System.IO.Abstractions NuGet) |
| **Randomness / identity** | `new Random(`, `Random.Shared`, `Guid.NewGuid(` | `TimeProvider`-style seam: inject `Random` / an `IGuidProvider` |
| **Culture / serialization** | `CultureInfo.CurrentCulture`, `CultureInfo.CurrentUICulture`, `JsonSerializer.Serialize(`, `JsonSerializer.Deserialize(` | Pass culture/options explicitly, or inject a serializer abstraction |
| **Environment** | `Environment.GetEnvironmentVariable(`, `Environment.SetEnvironmentVariable(`, `Environment.MachineName`, `Environment.UserName`, `Environment.CurrentDirectory`, `Environment.Exit(` | Custom `IEnvironmentProvider` |
| **Network** | `new HttpClient(`, `.GetAsync(`, `.PostAsync(`, `.SendAsync(` (confirm the receiver is an `HttpClient`; exclude calls whose receiver is injected or produced by an injected factory) | Inject `HttpClient` (commonly supplied by `IHttpClientFactory`) |
| **Console** | `Console.WriteLine(`, `Console.ReadLine(`, `Console.Write(`, `Console.ReadKey(` | `IConsole` wrapper or `ILogger` |
| **Process** | `Process.Start(`, `Process.GetCurrentProcess(`, `Process.GetProcessesByName(` | Custom `IProcessRunner` |

For time calls, inspect use as well as count. Two ambient clock reads in one
logical operation are two call sites and a consistency defect: for example,
separate `DateTime.UtcNow` reads for `CreatedAt` and
`ExpiresAt = DateTime.UtcNow.AddDays(30)` can drift. Recommend one captured
instant. With `TimeProvider`, retain `DateTimeOffset` where possible; when the
existing member requires UTC `DateTime`, use `GetUtcNow().UtcDateTime`, never
`.DateTime`, which loses the UTC kind. Treat capturing one instant as an
optional behavior-level follow-up: a mechanical wrapper migration must preserve
the original reads one-for-one unless the user separately approves that
semantic change.

### Step 3: Aggregate and rank results

Count each call site across the entire scan scope — including the instance-member call sites covered by the rules below, not only `static` ones.

**Counting rules — inaccurate totals are the main way this report loses to an ad-hoc scan:**

- **Build one occurrence ledger before writing prose.** Give each included call
  site exactly one row containing category, exact pattern, `file:line`, and
  recommended seam. Derive every category, pattern, and per-file count by
  grouping that same ledger; never recount independently while writing tables.
- **Keep the three count domains separate.** `Files scanned` includes every
  eligible source file; `affected files` includes only files with ledger rows;
  `call sites` is the number of ledger rows. Never substitute one for another.
- **One authoritative total.** Every call site you found belongs in the category summary and the grand total. Never park real findings in an "additional observations" section that the totals exclude.
- **Classify by what the member touches, not by whether it is `static`.** Instance members that reach the same untestable resource still count and belong in the matching category (`new FileInfo(path).LastWriteTimeUtc` → File System; `new HttpClient().GetAsync(...)` → Network). Say "hidden dependency", not "static", when the member is an instance call.
- **Check receiver provenance before counting instance calls.** Count a resource access only when the code under test acquires or constructs the dependency itself. Exclude constructor-, parameter-, property-, and DI-injected collaborators from the "needs wrapping" total, including concrete `HttpClient` instances.
- **Exclude deterministic pure helpers from the "needs wrapping" total.** `Path.Combine`, `Path.GetExtension`, `Path.GetFileName`, and `Math.*`/`string.*` statics take no ambient input and are trivially testable. List them, if at all, in a separate "no action needed" note — never as testability blockers.
- **Cover every category before reporting** — time, file system, environment, network, console, process, randomness (`new Random()`, `Guid.NewGuid()`), culture (`CultureInfo.CurrentCulture`), and serialization/statics such as `JsonSerializer`. Omitting a category that is present is an under-count.
- **Give `file:line` for every occurrence** so the user can jump straight to it.
- **Reconcile before publishing.** The category totals, the top-patterns table, and the per-file table must sum to the same grand total.
- **Treat exclusions as a scope decision, not a category.** Remove `obj/`,
  `bin/`, generated, and user-excluded files before building the ledger. Do not
  include their files or call sites in any reported count. State the exclusions
  once rather than mixing excluded candidates into the arithmetic.
- **Label truncated rankings.** In a comprehensive audit, list all distinct
  patterns when needed for reconciliation. If the user asked only for a top-N
  subset, label it as a subset and do not imply that its rows sum to the grand
  total.

Produce a summary with:

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

Based on the report, recommend which category to tackle first (highest count, best built-in support). Keep this to a few lines.

Mention `generate-testability-wrappers` or `migrate-static-to-wrapper` only when the user's next action clearly needs them — a hand-off note, not a sales pitch. Never end an audit with promotional next-steps that dilute the findings.

## Validation

- [ ] All `.cs` files in scope were scanned (check count)
- [ ] Report includes category totals, top patterns, and affected files
- [ ] Category totals, top patterns, and per-file counts reconcile to the same grand total
- [ ] Files scanned, affected files, and call sites are reported as different quantities
- [ ] Every aggregate was derived from one occurrence ledger rather than independently recounted
- [ ] Every occurrence carries a `file:line` location
- [ ] No findings are held outside the totals in an "additional" section
- [ ] Calls on injected collaborators are excluded from the "needs wrapping" total
- [ ] Deterministic pure helpers (`Path.Combine`, `Math.*`) are not counted as testability blockers
- [ ] Each detected pattern has a recommended replacement listed
- [ ] `obj/` and `bin/` directories were excluded
- [ ] Migration priority is ordered by impact (count × ease of replacement)

## Common Pitfalls

| Pitfall | Solution |
|---------|----------|
| Scanning `obj/` or generated code | Always exclude `obj/`, `bin/`, and `*.Designer.cs` |
| Counting calls on injected collaborators | Trace the receiver: an injected `HttpClient`, `TimeProvider`, interface, or other caller-supplied dependency already has a seam and needs no replacement |
| Missing statics inside lambdas/LINQ | Search covers all code within `.cs` files, including lambdas |
| Recommending `TimeProvider` on < .NET 8 | Check `TargetFramework` in `.csproj` — if < net8.0, recommend `NodaTime.IClock` or custom `ISystemClock` |
| Ignoring test projects | Only scan production code — exclude `*.Tests.csproj` projects from the scan |
| Under-counting by relegating findings | Real call sites belong in the category totals, not in a trailing "also noticed" paragraph that the totals ignore |
| Calling an instance member a static | `new FileInfo(p).LastWriteTimeUtc` is an instance call but still a hidden file-system dependency — count it under File System and describe it accurately |
| Recommending a wrapper for `Path.Combine` | Pure, deterministic helpers need no seam; listing them as blockers makes the recommendations wrong |
