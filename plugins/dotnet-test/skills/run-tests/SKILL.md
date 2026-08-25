---
name: run-tests
description: >
  Run .NET tests or give the exact repository-compatible command. Use for "run
  the tests", one test/class/category/trait, "what dotnet test command?", TRX,
  crash/hang dumps, "No test matches the given testcase filter", or unrecognized
  options. Handles classic MSBuild/vstest.console/MSTest, SDK-style VSTest,
  bridged/native Microsoft.Testing.Platform, MSTest/xUnit/NUnit/TUnit,
  multi-TFM selection, filters, diagnostics, and argument order. For
  identification-only requests such as "VSTest or MTP?", use
  platform-detection. DO NOT USE for writing tests, hot-reload/no-rebuild
  iteration, migration, CI, coverage analysis, or debugging test logic.
license: MIT
---

# Run .NET Tests

Return or execute the one command that matches the repository's project system,
test platform, framework, and SDK mode.

## Scope and tool policy

Choose the smallest path that satisfies the request:

| Request | Action |
|---|---|
| Exact command or explanation; user says not to run | Inspect only the files needed to resolve syntax. Do not restore, build, or run tests. |
| Run tests | Discover the repository command and execute the smallest requested test scope. |
| Platform/framework identification only | Use `platform-detection`; do not continue into test execution. |
| Hot reload, no rebuild, or a long-lived edit/re-run loop | Use `mtp-hot-reload`. |
| Filter needed and the framework-specific syntax is not already clear | Load `filter-syntax`; do not load it for unfiltered runs. |

Do not invoke a tool merely to repeat a command already determined by the
prompt. Do not build first "just in case": `dotnet test` builds by default.
Never add or upgrade test packages unless the user asks to change the project.

## Inputs to discover

- Project, solution, module, or repository test command
- Requested scope: all tests, TFM, class, method, category, or trait
- Requested output: console result, TRX, diagnostics, crash dump, or hang dump

When those facts are present in the prompt, use them. Otherwise inspect only the
relevant files: `global.json`, the selected project, `packages.config`,
`Directory.Build.props`, `Directory.Packages.props`, then repository
scripts/CI documentation. Load `platform-detection` only when those signals need
precedence analysis; do not duplicate its full analysis in the response.

## Decision table

| Detected mode / platform | Command shape | Never use |
|---|---|---|
| Classic non-SDK | Repository script, or full MSBuild followed by `vstest.console.exe` / `MSTest.exe` | Assuming `dotnet test` is compatible or migrating implicitly |
| VSTest mode / VSTest | `dotnet test [<path>] [VSTEST_OPTIONS]` | MTP-only flags such as `--report-trx` or `--treenode-filter` |
| VSTest mode / MTP bridge | `dotnet test [<path>] [DOTNET_OPTIONS] -- [MTP_OPTIONS]` | Omitting the `--` separator, including on SDK 10 |
| Native MTP mode, SDK 10+ | `dotnet test --project <path> [DOTNET_OPTIONS] [MTP_OPTIONS]` | Bare positional project paths or the bridge separator |

`global.json` controls the `dotnet test` command mode on SDK 10+, not
necessarily the platform that executes tests. A VSTest-mode project with an MTP
runner and `TestingPlatformDotnetTestSupport=true` is still bridge syntax with
`--`. SDK 8/9 only has VSTest command mode.

Keep `dotnet test`/MSBuild options such as `--framework`, `--configuration`,
`--no-build`, and `--verbosity` before `--`. Put only MTP application arguments
after `--` in bridge mode.

## Workflow

### 1. Resolve the repository-compatible runner

For classic projects, signals include `ToolsVersion`, explicit `Compile` and
`Reference` items, legacy imports, and `packages.config`. Prefer a checked-in
script or documented CI command. A typical fallback is:

```powershell
MSBuild.exe MySolution.sln /t:Build /p:Configuration=Debug
vstest.console.exe path\to\MyTests.dll
```

For a requested subset, keep the repository runner and use its filter syntax:
`vstest.console.exe path\to\MyTests.dll
/TestCaseFilter:"TestCategory=Integration"`. Older `MSTest.exe` repositories may
use `/test:<name>` or `/category:<category>` instead. Do not substitute the
later `dotnet test` filter examples for a classic runner.

Use the installed adapter-compatible VSTest/MSTest toolchain. If it is not
available, state the missing prerequisite and the documented command; do not
claim tests ran.

For SDK-style projects, distinguish:

| Signal | Meaning |
|---|---|
| SDK 10+ `global.json` selects `Microsoft.Testing.Platform` | Native MTP command mode |
| VSTest mode + enabled MTP runner + final `TestingPlatformDotnetTestSupport=true` | VSTest-to-MTP bridge |
| VSTest mode without a complete runner-and-bridge combination | VSTest |
| `Microsoft.NET.Test.Sdk` plus adapter, without stronger MTP signals | VSTest |
| `TUnit` | MTP-only; use a configured bridge/native mode or the test executable |

Evaluate properties from the project and imported
`Directory.Build.props`/`Directory.Packages.props`. Respect project-level
overrides and per-target-framework conditions.

### 2. Select the command and requested scope

```shell
# VSTest mode
dotnet test path/to/Tests.csproj

# VSTest mode that bridges to MTP
dotnet test path/to/Tests.csproj -- <MTP_OPTIONS>

# Native MTP mode on SDK 10+
dotnet test --project path/to/Tests.csproj <MTP_OPTIONS>

# One target framework; this stays before the bridge separator
dotnet test path/to/Tests.csproj --framework net9.0 -- <MTP_OPTIONS>
```

For native MTP, use `--project`, `--solution`, or `--test-modules`; positional
paths belong to VSTest mode.

If the user names a subset, do not run the whole suite. Inspect test attributes
only when needed to translate a human label such as "integration" or "smoke"
into the framework's actual category/trait name.

### 3. Apply platform- and framework-correct filters

Load `filter-syntax` only for a filtered request. The common decisions are:

| Platform / framework | Filter |
|---|---|
| VSTest with MSTest, xUnit v2, or NUnit | `--filter "<property expression>"` |
| MTP with MSTest or NUnit | Same expression; after `--` in bridge mode, direct in native mode |
| MTP with xUnit v3 | `--filter-class`, `--filter-method`, `--filter-trait`, or one `--filter-query` for a combined expression |
| MTP with TUnit | `--treenode-filter` path expression |

Examples:

```shell
# VSTest MSTest/NUnit
dotnet test --filter "FullyQualifiedName~OrderServiceTests&TestCategory=Unit"

# SDK 8/9 or SDK 10 VSTest-mode MTP bridge, xUnit v3
dotnet test -- --filter-trait "Category=Integration"

# Native MTP, xUnit v3
dotnet test --project Tests.csproj --filter-class "*ShoppingCartTests*"

# One xUnit v3 combined expression
dotnet test -- --filter-query "/*/*/*IntegrationTests*/*[Category=Smoke]"

# TUnit
dotnet test -- --treenode-filter "/*/*/SmsNotificationTests/*"
```

Do not use VSTest `--filter "ClassName=..."` with xUnit v3 on MTP. Do not use a
generic VSTest expression with TUnit.

### 4. Add reports or diagnostics

| Outcome | VSTest | MTP |
|---|---|---|
| TRX | `--logger trx` | `--report-trx` |
| Results directory | `--results-directory <dir>` | `--results-directory <dir>` |
| Diagnostic log | `--diag <file>` or `--verbosity diagnostic` | `--diagnostic --diagnostic-output-directory <dir>` |
| Crash dump | `--blame-crash` | `--crashdump` |
| Hang timeout | `--blame-hang --blame-hang-timeout 5min` | `--hangdump --hangdump-timeout 5min` |
| Code coverage | `--collect "Code Coverage"` | `--coverage` |

MTP report, dump, and coverage flags require their corresponding registered
extensions (`TrxReport`, `CrashDump`, `HangDump`, or `CodeCoverage`). Some
framework SDKs bundle common extensions; if a flag is unrecognized, inspect
package references before recommending a package change.

Examples:

```shell
# VSTest TRX
dotnet test Tests.csproj --logger trx

# MTP bridge TRX
dotnet test Tests.csproj -- --report-trx

# Native MTP TRX and hang detection
dotnet test --project Tests.csproj --report-trx --hangdump --hangdump-timeout 5min
```

### 5. Execute only when requested

Run the narrowest command that answers the request. Capture the command, exit
code, and test summary. A failed restore/build is not a test failure, and a test
failure is not a tool failure. Report which phase failed and include the
actionable diagnostic. Never claim a clean run unless the command completed
successfully with the intended tests executed.

## Output contract

- Command-only request: lead with the exact command, then one short syntax
  explanation.
- Execution request: report the exact command and passed/failed/skipped counts;
  include the first actionable failure.
- Detection needed only to choose syntax: state the selected mode/platform
  briefly, not a separate detection report.
- Missing prerequisite or incompatible configuration: name it explicitly and
  stop rather than returning a success-shaped fallback.

## Validation

- The command matches classic, VSTest, bridged MTP, or native MTP mode.
- The framework-specific filter targets the requested subset.
- `--framework` and other `dotnet test` options are before any bridge separator.
- TRX, diagnostics, dump, and coverage flags match the platform.
- No restore, build, or test was run for an advisory-only request.
- Reported results match the actual command outcome.
