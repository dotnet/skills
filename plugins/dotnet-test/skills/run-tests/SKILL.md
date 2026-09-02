---
name: run-tests
description: >
  Run .NET tests or give the exact repository-compatible command. Use for "run
  the tests", one test/class/category/trait, one target framework, "what dotnet
  test command?", `--no-build`, `--diag`, diagnostic logs, classic
  packages.config or MSTest.exe, TRX or coverage collection, crash/hang dumps,
  filter mismatch, `--filter-query`, a single combined filter expression, or
  unrecognized options. Handles VSTest and bridged/native
  Microsoft.Testing.Platform across MSTest/xUnit/NUnit/TUnit, including NUnit
  bridge filters, xUnit v3 class/trait/query filters, multi-TFM, and argument
  order. For identification-only requests, use platform-detection. DO NOT USE
  for writing tests, hot-reload/no-rebuild loops, migration, CI, coverage
  analysis, or debugging test logic.
license: MIT
---

# Run .NET Tests

Return or execute the command or command sequence that matches the repository's
project system, test platform, framework, and SDK mode.

## Scope and tool policy

Choose the smallest path that satisfies the request:

| Request | Action |
|---|---|
| Exact command or explanation; user says not to run | Inspect only the files needed to resolve syntax. Do not restore, build, or run tests. |
| Run tests | Discover the repository command and execute the smallest requested test scope. |
| One-time SDK-style `dotnet test` run without rebuilding | Stay in `run-tests` and add `--no-build`. |
| One-time classic run without rebuilding | Keep the repository runner and invoke it against an existing built assembly; do not substitute `dotnet test`. |
| Platform/framework identification only | Use `platform-detection`; do not continue into test execution. |
| Explicit hot reload or a keep-running edit/re-run loop | Use `mtp-hot-reload`. |
| Filter needed and the framework-specific syntax is not already clear | Load `filter-syntax`; do not load it for unfiltered runs. |

Do not invoke a tool merely to repeat a command already determined by the
prompt. Do not build first "just in case": `dotnet test` builds by default.
Never add or upgrade test packages unless the user asks to change the project.
For an exact-command request, return one runnable command first. Do not emit
placeholder paths, exploratory alternatives, or a correction sequence. Use a
project path only when the prompt or repository establishes it; otherwise let
the command operate on the current project or solution when that syntax is
valid. Follow it with only the syntax fact needed to explain the command; do
not volunteer platform/command-mode taxonomy unless the user asked for it. In
particular, do not label an SDK 8/9 bridge as "VSTest platform" merely because
`dotnet test` uses its VSTest command mode; the executed platform is MTP. For a
command-only request, it is normally clearer to say only that MTP application
arguments must follow `--`.

## Inputs to discover

- Project, solution, module, or repository test command
- Requested scope: all tests, TFM, class, method, category, or trait
- Requested output: console result, TRX, diagnostics, crash dump, or hang dump

When those facts are present in the prompt, use them. Otherwise inspect only the
relevant files: `global.json`, the selected project, `packages.config`,
`Directory.Build.props`, `Directory.Packages.props`, then repository
scripts/CI documentation. For a file-backed request, enumerate those
configuration names once and read all relevant files that are present in one
batch; never infer that a runner or bridge property is absent merely because it
is not in the `.csproj`. Load `platform-detection` only when those signals need
precedence analysis; do not duplicate its full analysis in the response.
If execution is requested and the command depends on the active SDK but neither
the prompt nor `global.json` establishes it, run `dotnet --version` once. For a
command-only request that prohibits execution, do not probe: state the required
SDK assumption or ask for the SDK version when the syntax cannot otherwise be
resolved. Route identification-only requests to `platform-detection`.

## Decision table

| Detected mode / platform | Command shape | Never use |
|---|---|---|
| Classic non-SDK | Repository script, or full MSBuild followed by `vstest.console.exe` / `MSTest.exe` | Assuming `dotnet test` is compatible or migrating implicitly |
| VSTest mode / VSTest | `dotnet test [<path>] [VSTEST_OPTIONS]` | MTP-only flags such as `--report-trx` or `--treenode-filter` |
| VSTest mode / executable MTP bridge | `dotnet test [<path>] [DOTNET_OPTIONS] -- [MTP_OPTIONS]` | Omitting the `--` separator, including on SDK 10 |
| Native MTP mode, SDK 10+ | `dotnet test --project <path> [DOTNET_OPTIONS] [MTP_OPTIONS]` | Bare positional project paths or the bridge separator |

`global.json` controls the `dotnet test` command mode on SDK 10+, not
necessarily the platform that executes tests. A VSTest-mode project with an MTP
runner, `TestingPlatformDotnetTestSupport=true`, and final `OutputType=Exe` is
still bridge syntax with `--`. SDK 8/9 only has VSTest command mode.

`--project` is valid only in SDK 10+ **native MTP command mode** (selected by
`global.json` `test.runner`). Never use it for VSTest mode or an SDK 8/9 bridge;
those forms take a positional project path. Conversely, native MTP options are
direct arguments and must not be placed after a bridge separator.

Keep `dotnet test`/MSBuild options such as `--framework`, `--configuration`,
`--no-build`, and `--verbosity` before `--`. Put only MTP application arguments
after `--` in bridge mode.

## Workflow

1. **Resolve the repository-compatible runner.**

For classic projects, signals include `ToolsVersion`, explicit `Compile` and
`Reference` items, legacy imports, and `packages.config`. Prefer a checked-in
script or documented CI command. A typical fallback is:

```powershell
nuget restore MySolution.sln
MSBuild.exe MySolution.sln /t:Build /p:Configuration=Debug
vstest.console.exe path\to\MyTests.dll /TestAdapterPath:path\to\adapter\build\<tfm>
```

For `packages.config`, restore with NuGet before the build unless the imported
package files are already present. If adapter discovery is not repository-
configured, derive `/TestAdapterPath` from the restored adapter package path or
the adapter `.props`/`.targets` imports; do not guess the package root.

For a requested no-rebuild run, omit the build step and invoke the repository's
test runner only when the expected assembly already exists. Otherwise report the
missing build output rather than silently rebuilding or switching runners.

For a requested subset, keep the repository runner and use its filter syntax:
`vstest.console.exe path\to\MyTests.dll
/TestCaseFilter:"TestCategory=Integration"`. Older `MSTest.exe` repositories may
use `/test:<name>` or `/category:<category>` instead. Do not substitute the
later `dotnet test` filter examples for a classic runner.

Use the installed adapter-compatible VSTest/MSTest toolchain. If it is not
available, state the missing prerequisite and the documented command; do not
claim tests ran.
Classic `packages.config` fallback commands are Windows toolchain commands.
Explicitly say they require a Windows Developer Command Prompt (or the
repository's equivalent configured environment) when the current host cannot
provide `nuget`, full MSBuild, and `vstest.console.exe`.

For SDK-style projects, distinguish:

| Signal | Meaning |
|---|---|
| SDK 10+ `global.json` selects `Microsoft.Testing.Platform` | Native MTP command mode |
| VSTest mode + enabled MTP runner + final `TestingPlatformDotnetTestSupport=true` + final `OutputType=Exe` | Executable VSTest-to-MTP bridge |
| VSTest mode without a complete runner-and-bridge combination | VSTest |
| `Microsoft.NET.Test.Sdk` plus adapter, without stronger MTP signals | VSTest |
| `TUnit` | MTP-only; use a configured bridge/native mode or the test executable |

Evaluate properties from the project and imported
`Directory.Build.props`/`Directory.Packages.props`. Respect project-level
overrides and per-target-framework conditions. A runner and bridge without an
executable final output are an incomplete MTP configuration, not a usable
bridge.

2. **Select the command and requested scope.**

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

When a VSTest class filter must distinguish similarly named classes, combine
the positive selector with an explicit negative selector rather than relying on
an incidental substring difference.

3. **Apply platform- and framework-correct filters.**

Load `filter-syntax` only when the request is filtered and the framework-specific
syntax is not already clear. The common decisions are:

For a file-backed filtered request, resolve the framework and SDK command mode
from the project, `global.json`, and imported props before choosing syntax.
Do not infer VSTest syntax merely from `Microsoft.NET.Test.Sdk` or from the
framework name.

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
dotnet test -- --filter-query "/*/*/*Integration*/*[Category=Smoke]"

# TUnit on SDK 8/9 with a configured VSTest-to-MTP bridge
dotnet test -- --treenode-filter "/*/*/SmsNotificationTests/*"

# TUnit executable fallback when no bridge is configured
dotnet run --project Tests.csproj -- --treenode-filter "/*/*/SmsNotificationTests/*"
```

Do not use VSTest `--filter "ClassName=..."` with xUnit v3 on MTP. Do not use a
generic VSTest expression with TUnit.
When the user requests one combined xUnit query expression, return only one
`--filter-query` command; do not replace it with separate filter flags or offer
speculative alternative grammars.

4. **Add reports or diagnostics.**

| Outcome | VSTest | MTP |
|---|---|---|
| TRX | SDK-style: `--logger "trx;LogFileName=<name>.trx"` when an exact output file is requested, otherwise `--logger trx`; standalone `vstest.console.exe`: `/Logger:trx`; `MSTest.exe`: repository-documented results option | `--report-trx` |
| Results directory | `--results-directory <dir>` | `--results-directory <dir>` |
| Diagnostic log | `--diag <file>` | `--diagnostic --diagnostic-output-directory <dir>` |
| Crash dump | `--blame-crash` | `--crashdump` |
| Hang timeout | `--blame-hang --blame-hang-timeout 5min` | `--hangdump --hangdump-timeout 5min` |
| Code coverage | `--collect "Code Coverage"` | `--coverage` |

MTP report, dump, and coverage flags require their corresponding registered
extensions (`TrxReport`, `CrashDump`, `HangDump`, or `CodeCoverage`). Some
framework SDKs bundle common extensions; if a flag is unrecognized, inspect
package references before recommending a package change.

`--verbosity diagnostic` increases dotnet/MSBuild output verbosity; it does not
write a VSTest diagnostic log file.

Examples:

```shell
# VSTest TRX
dotnet test Tests.csproj --logger "trx;LogFileName=TestResults.trx"

# MTP bridge TRX
dotnet test Tests.csproj -- --report-trx

# Native MTP TRX and hang detection
dotnet test --project Tests.csproj --report-trx --hangdump --hangdump-timeout 5min

# Native MTP diagnostics
dotnet test --project Tests.csproj --diagnostic --diagnostic-output-directory artifacts/diagnostics
```

5. **Execute only when requested.**

Run the narrowest command or sequence that answers the request. Capture each
command, exit code, and test summary. A failed restore/build is not a test
failure, and a test failure is not a tool failure. Report which phase failed and
include the actionable diagnostic. Never claim a clean run unless the sequence
completed successfully with the intended tests executed.
For a filtered run, a successful exit is not enough: confirm the reported test
names or count match the requested scope. If the filter was ignored, correct the
platform-specific syntax and rerun before reporting success.

## Output contract

- Command-only request: lead with the exact command or command sequence, then
  one short syntax explanation.
- Execution request: report the exact command or command sequence and
  a literal `Passed: N, Failed: N, Skipped: N` summary from the completed run;
  include the first actionable failure.
- Detection needed only to choose syntax: state the selected mode/platform
  briefly, not a separate detection report.
- Missing prerequisite or incompatible configuration: name it explicitly and
  stop rather than returning a success-shaped fallback.

## Validation

- The command matches classic, VSTest, bridged MTP, or native MTP mode.
- The framework-specific filter targets the requested subset.
- `--framework` and other `dotnet test` options are before any bridge separator.
- SDK 8/9 bridge commands contain `--`; `--project` appears only in SDK 10+
  native MTP mode.
- TRX, diagnostics, dump, and coverage flags match the platform.
- No restore, build, or test was run for an advisory-only request.
- Reported results match the actual command outcome.
