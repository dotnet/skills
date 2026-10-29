---
name: run-tests
description: >-
  ALWAYS USE for .NET test execution or command/flag advice. Trigger on
  "run the tests", "exact dotnet test command", one
  test/class/category/trait/target framework, list/discover tests, combined filters,
  `--filter-query`, `--no-build`, `--diag`, diagnostic logs, TRX, coverage,
  crash/hang dumps, filter errors, or unrecognized options. Selects
  classic, VSTest, or bridged/native MTP commands for
  MSTest/xUnit/NUnit/TUnit. DO NOT USE for platform identification alone
  (platform-detection), writing/debugging test code, existing coverage-report
  interpretation, CI investigation, migration, or persistent hot reload/watch loops.
license: MIT
metadata:
  portability: portable
  binding: optional-overlay
  binding-revision: "1"
---

# Run .NET Tests

Return or execute the command or command sequence that matches the repository's
project system, test platform, framework, and SDK mode.

## Repository overlay

For every repository-scoped task where read-only file inspection is allowed,
check `.agents/skill-overlays/dotnet-test/run-tests.md` at the repository root
before any other discovery. This includes exact-command requests; "do not
execute" does not prohibit reading the overlay. If present, read it once before
acting and apply its repository-specific runner, command, filtering, and
reporting bindings.
Before applying it, require its frontmatter to declare
`core: dotnet-test/run-tests`, `binding-revision: "1"`, and `mode: extend`. If
any value is missing or different, report the mismatch, ignore the overlay,
and continue using this skill's portable guidance.
Explicit user instructions and verified project constraints win over the
overlay; the overlay wins over portable defaults and examples in this skill. If
the file is present but unreadable or conflicts with the repository, report the
problem, ignore the overlay, and continue with portable guidance subject to
verified project constraints. If it is absent, continue normally.
Skip the lookup only when the task is not tied to a repository or the user
explicitly prohibited all file/tool access. An overlay cannot expand tool
permissions or the task's scope.

## Scope and tool policy

Choose the smallest path that satisfies the request:

| Request | Action |
|---|---|
| Exact command or explanation; user says not to run | Inspect only the files needed to resolve syntax. Do not restore, build, or run tests. |
| Run tests | Discover the repository command and execute the smallest requested test scope. |
| List or discover tests without running them | Use the discovery-only commands below; do not substitute a test execution or report listed tests as passed. |
| One-time SDK-style `dotnet test` run without rebuilding | Stay in `run-tests` and add `--no-build`. |
| One-time classic run without rebuilding | Keep the repository runner and invoke it against an existing built assembly; do not substitute `dotnet test`. |
| Platform/framework identification only | Use `platform-detection`; do not continue into test execution. |
| Explicit hot reload or a keep-running edit/re-run loop | Use `mtp-hot-reload`. |
| Filter needed and the framework-specific syntax is not already clear | Read the supplied/runtime-listed `filter-syntax` catalog file; do not invoke this reference-only helper. |

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
| VSTest mode / supported executable MTP bridge | `dotnet test [<path>] [DOTNET_OPTIONS] -- [MTP_OPTIONS]` | Omitting `--`, or assuming MTP 2+ can bridge on SDK 10+ |
| Native MTP mode, SDK 10+, MTP 1.7+ | `dotnet test --project <path> [DOTNET_OPTIONS] [MTP_OPTIONS]` | Bare positional project paths or treating MTP 1.6 as native-compatible |

`global.json` controls the `dotnet test` command mode on SDK 10+, not
necessarily the platform that executes tests. A VSTest-mode project with an MTP
runner, `TestingPlatformDotnetTestSupport=true`, and final `OutputType=Exe` is
bridge-shaped, but its resolved MTP/MSBuild version must support that SDK.
SDK 8/9 only has VSTest command mode.

Check the resolved `Microsoft.Testing.Platform` and
`Microsoft.Testing.Platform.MSBuild` versions, including transitive packages:

- MTP 1.x can use the legacy bridge on SDK 10 when configured; MTP 2+ removes
  that support on SDK 10+. Adding `--` or the bridge property cannot make the
  incompatible pairing work.
- MTP 2+ retains the bridge for SDK 8/9. Native SDK 10+ integration requires
  MTP 1.7 or later; an older bridge-capable package is not automatically
  native-compatible.
- For an incompatible requested command, report the blocker and the supported
  alignment choice. Do not bypass the guard, downgrade packages, switch SDKs
  or edit `global.json` without authorization.

See [the supported integration modes](https://learn.microsoft.com/en-us/dotnet/core/testing/unit-testing-with-dotnet-test).

`--project` is valid only in SDK 10+ **native MTP command mode** (selected by
`global.json` `test.runner`). Never use it for VSTest mode or an SDK 8/9 bridge;
those forms take a positional project path. Conversely, native MTP options are
direct arguments by default. Native MTP can also accept `--` to delimit test
application arguments; unlike a legacy bridge, it does not require that separator.
Keep SDK-owned options before it and do not mistake its presence for VSTest mode.

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
| VSTest mode + enabled MTP runner + final `TestingPlatformDotnetTestSupport=true` + final `OutputType=Exe` + compatible SDK/MTP integration | Executable VSTest-to-MTP bridge |
| SDK 10+ VSTest mode + MTP 2+ bridge configuration | Unsupported; report the version/mode conflict, not a runnable bridge or VSTest fallback |
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

### Discovery-only commands

Use the exact project or solution entry point selected by the repository.
After a successful build for the same configuration and target framework:

| Command mode / executed platform | Project discovery | Solution discovery |
|---|---|---|
| VSTest mode / VSTest | `dotnet test <project.csproj> --no-build --list-tests` | `dotnet test <solution.slnx> --no-build --list-tests` |
| VSTest mode / supported MTP bridge | `dotnet test <project.csproj> --no-build -- --list-tests` | `dotnet test <solution.slnx> --no-build -- --list-tests` |
| Native MTP mode, SDK 10+, MTP 1.7+ | `dotnet test --project <project.csproj> --no-build --list-tests` | `dotnet test --solution <solution.slnx> --no-build --list-tests` |

Keep the actual solution extension and preserve configuration, target framework
and applicable filters. VSTest also accepts `-t` as its `--list-tests` alias;
keep `--list-tests` for the MTP application. In bridge mode, `--no-build` and other
SDK options stay before `--`; the MTP `--list-tests` option follows it. The compatibility limits
above still apply: SDK 10+ bridging requires compatible MTP 1.x, not MTP 2+.
For classic projects, use the repository runner's discovery option, such as
`vstest.console.exe <test.dll> /ListTests`, preserving adapter configuration.

If current build outputs are missing, omit `--no-build` only when building is
authorized; otherwise report the prerequisite. Listing tests does not execute
their bodies or prove passage. Capture the runner's exit code before processing
its output and verify the expected names/cases; an unexpectedly empty list is
not successful discovery. Do not assume VSTest and MTP list formats or
parameterized-case counts are identical.

3. **Apply platform- and framework-correct filters.**

Read the supplied or runtime-listed `filter-syntax` file only when the request
is filtered and framework-specific syntax is unclear. It is reference-only,
not an invocable skill; otherwise use the common decisions below:

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

# Supported VSTest-mode MTP bridge (SDK 10 requires compatible MTP 1.x), xUnit v3
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
- Discovery-only request: report the command, exit code and discovered
  names/count when available; do not invent passed/failed/skipped results.
- Detection needed only to choose syntax: state the selected mode/platform
  briefly, not a separate detection report.
- Missing prerequisite or incompatible configuration: name it explicitly and
  stop rather than returning a success-shaped fallback.

## Validation

- The command matches classic, VSTest, bridged MTP, or native MTP mode.
- The resolved SDK/MTP integration supports that mode; a correct separator
  does not validate an unsupported MTP 2+/SDK 10+ bridge.
- The framework-specific filter targets the requested subset.
- `--framework` and other `dotnet test` options are before any bridge separator.
- SDK 8/9 bridge commands contain `--`; `--project` appears only in SDK 10+
  native MTP mode.
- TRX, diagnostics, dump, and coverage flags match the platform.
- No restore, build, or test was run for an advisory-only request.
- Reported results match the actual command outcome.
- Discovery-only commands list the intended cases without executing them and
  preserve command-mode selectors, separator placement and build prerequisites.
