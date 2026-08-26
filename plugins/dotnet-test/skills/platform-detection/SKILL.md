---
name: platform-detection
description: >-
  Identify a .NET project's test platform, framework, command mode, and
  SDK-style vs classic project system. Use only for "which test
  platform/framework?", "VSTest or MTP?", or "what runner does this project
  use?", including bridge settings, UseVSTest opt-outs, and incompatible or
  conflicting VSTest/MTP configuration. Resolves global.json, project,
  packages.config, Directory.Build.props, and Directory.Packages.props
  precedence for MSTest/xUnit/NUnit/TUnit. For running/filtering tests, exact
  commands or flags, TRX/dumps, and test-command/filter errors, use run-tests.
  Do not use for hot reload or migration.
license: MIT
---

# Test Platform and Framework Detection

Determine **which test platform** (VSTest or Microsoft.Testing.Platform) and **which test framework** (MSTest, xUnit, NUnit, TUnit) a project uses.

## Response contract

Honor the user's requested labels and order exactly, substituting the actual
classification for every placeholder. Start with the verdict: never put a
heading, scratch analysis, tool syntax, or an echoed template before it. Follow
with one concise evidence line naming only the repository facts that decide the
result.

`Platform` means the platform that actually executes tests: **VSTest** or
**MTP**. If conflicting or incomplete configuration prevents execution, report
it as unavailable rather than inventing a successful platform. Include command
mode only when the user asks for it. When the user asks which single signal
decides the result, name that signal first and keep bridge or output
prerequisites subordinate rather than presenting every property as co-equal.

When a classic-project request also asks for the command family, add a direct
line such as `Command family: MSBuild + vstest.console.exe`; do not turn it into
an optional alternative or add an unnecessary build qualifier.

For a file-backed request, enumerate the following configuration names once,
then read every relevant file that is present in one batched operation:
`global.json`, `.csproj`, `packages.config`, `Directory.Build.props`, and
`Directory.Packages.props`. A setting absent from the project file may be
defined by imported repository files, so never infer its final value from the
`.csproj` alone. Do not search the web or inspect unrelated files when
repository configuration is sufficient.

## Detecting the project system

Classify the project before selecting a CLI:

- Root `Sdk` attribute or `<Sdk>` declaration: SDK-style.
- `ToolsVersion`, `Microsoft.Common.props` / `Microsoft.CSharp.targets` imports,
  explicit `<Reference>` and `<Compile Include>` items: classic non-SDK.
- `packages.config`: classic NuGet dependency management.

Classic projects can still use VSTest-compatible adapters, but `dotnet test` is
not automatically a valid invocation. Preserve repository scripts/CI commands,
commonly MSBuild followed by `vstest.console.exe`. Mention `MSTest.exe` only
when repository configuration or documentation establishes that legacy runner.

## Detecting the test framework

Read the `.csproj`, adjacent `packages.config`, and
`Directory.Build.props` / `Directory.Packages.props` and look for:

| Package or SDK reference | Framework |
|--------------------------|-----------|
| `MSTest` metapackage, `<Project Sdk="MSTest.Sdk[/version]">`, or `<Sdk Name="MSTest.Sdk">` | MSTest |
| `MSTest.TestFramework` + `MSTest.TestAdapter` | MSTest (also valid for v3/v4) |
| `xunit`, `xunit.v3`, `xunit.v3.mtp-v1`, `xunit.v3.mtp-v2`, `xunit.v3.core.mtp-v1`, `xunit.v3.core.mtp-v2` | xUnit |
| `NUnit` + `NUnit3TestAdapter` | NUnit |
| `TUnit` | TUnit (MTP only) |

In classic projects, package IDs and versions may appear only in
`packages.config`, while the project contains assembly `<Reference>` elements
with `HintPath` values. Use both sources.

## Detecting the executed test platform

If the user explicitly requests `dotnet test` mode, read
[`references/command-mode.md`](references/command-mode.md) before answering.
Do not load that reference for a platform/framework-only request.

On SDK 8/9, `dotnet test` command mode is always VSTest, although a complete
bridge can still execute tests on MTP. Only SDK 10+ `global.json` can select
native MTP command mode. Never collapse command mode and executed platform into
one classification.

When execution is permitted and neither the prompt nor `global.json` identifies
the SDK, run `dotnet --version` once. For read-only identification requests that
prohibit execution, do not probe the installed SDK; use repository facts and
state any necessary SDK assumption.

Evaluate final property values in this order:

1. Explicit `UseVSTest=true` selects VSTest. If `global.json` simultaneously
   selects the native MTP runner, report `Platform: unavailable` because the
   repository and project conflict.
2. A native-MTP selection in `global.json` executes a compatible MTP
   application with final `OutputType=Exe` on MTP. A VSTest-only, library-output,
   or opted-out project is unavailable, not a successful MTP execution.
3. Otherwise, an enabled MTP runner plus
   `TestingPlatformDotnetTestSupport=true` plus final `OutputType=Exe` executes
   on MTP.
4. A runner and bridge with non-executable output is incomplete and unavailable.
   Without the complete runner/bridge/executable combination, a dual-capable
   MSTest or NUnit project executes on VSTest.

Do not confuse the `MSTest` metapackage with the `MSTest.Sdk` project SDK.
`PackageReference Include="MSTest"` plus `EnableMSTestRunner=true` enables the
MSTest MTP runner, but it does **not** implicitly set
`TestingPlatformDotnetTestSupport`.

MSTest.Sdk enables the MTP runner by default. Check its resolved version and
evaluated properties for bridge behavior: versions such as 3.8 also set
`TestingPlatformDotnetTestSupport`, while newer SDKs on .NET 10 may expect native
MTP mode instead. `<UseVSTest>true</UseVSTest>` opts back into VSTest.

| Signal | Meaning |
|--------|---------|
| `<Project Sdk="MSTest.Sdk...">` with no `UseVSTest` | MTP application; inspect the resolved SDK version and evaluated bridge property |
| `MSTest` metapackage + `<EnableMSTestRunner>true>` | MTP runner enabled; does not imply the VSTest-to-MTP bridge |
| `<UseMicrosoftTestingPlatformRunner>true` | Deciding xUnit runner-selection signal |
| `<EnableMSTestRunner>true>` / `<EnableNUnitRunner>true>` | Deciding MSTest/NUnit runner-selection signal |
| `TestingPlatformDotnetTestSupport=true` | Execution prerequisite for a VSTest-to-MTP bridge, not the runner-selection signal |
| `Microsoft.Testing.Platform` package | MTP-capable application; not decisive by itself |
| `TUnit` | MTP-only framework |
| Final evaluated `<OutputType>Exe</OutputType>` | Required executable host shape for package-based MTP applications |

`Microsoft.NET.Test.Sdk` alone is not decisive; it can remain for compatibility
in an MTP-enabled project. When an explicit override decides the result, name
the override only; do not summarize the defaults it supersedes.
When a runner-selection property competes with `Microsoft.NET.Test.Sdk`, name
the runner property as decisive and the package as non-decisive compatibility
support; omit unrelated execution prerequisites unless they are needed to show
that the selected runner can actually execute.

For an incompatible configuration, give one minimal alignment choice after the
verdict without modifying files: either select the project's configured
platform globally or remove the project opt-out to use the globally selected
platform.

### Conditional and per-target-framework properties

Evaluate runner and bridge properties for each target framework. If conditions
produce different executed platforms, report each target explicitly (for
example, `net8.0: VSTest`, `net9.0: MTP`) rather than collapsing the project to
one global platform.
