---
name: platform-detection
description: >-
  Identify a .NET project's test platform, framework, command mode, and
  SDK-style vs classic project system. Use only for "which test
  platform/framework?", "VSTest or MTP?", or "what runner does this project
  use?" Resolves global.json, project, packages.config, Directory.Build.props,
  and Directory.Packages.props precedence for MSTest/xUnit/NUnit/TUnit. For
  running/filtering tests, exact commands or flags, TRX/dumps, and
  test-command/filter errors, use run-tests. Do not use for hot reload or
  migration.
license: MIT
---

# Test Platform and Framework Detection

Determine **which test platform** (VSTest or Microsoft.Testing.Platform) and **which test framework** (MSTest, xUnit, NUnit, TUnit) a project uses.

## Response contract

When the requested output includes `Platform:`, report the platform that actually
executes tests: **VSTest** or **MTP**. Do not put the `dotnet test` command mode
on that line. If command mode matters, report it separately:

```text
dotnet test mode: VSTest
Platform: MTP
Framework: MSTest
```

Thus an SDK 9 bridged project is `Platform: MTP`, even though its
`dotnet test mode` is VSTest.

**Detection files to always check** (in order): `global.json` → `.csproj` →
`packages.config` → `Directory.Build.props` → `Directory.Packages.props`

## Detecting the project system

Classify the project before selecting a CLI:

- Root `Sdk` attribute or `<Sdk>` declaration: SDK-style.
- `ToolsVersion`, `Microsoft.Common.props` / `Microsoft.CSharp.targets` imports,
  explicit `<Reference>` and `<Compile Include>` items: classic non-SDK.
- `packages.config`: classic NuGet dependency management.

Classic projects can still use VSTest-compatible adapters, but `dotnet test` is
not automatically a valid invocation. Preserve repository scripts/CI commands,
commonly MSBuild followed by `vstest.console.exe` or `MSTest.exe`.

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

## Detecting the test platform

Detect two separate axes:

1. **`dotnet test` mode** — VSTest mode or native MTP mode. This controls CLI
   syntax.
2. **Executed test platform** — VSTest or MTP. VSTest mode can bridge to and
   execute an MTP test application.

Run `dotnet --version` first because mode selection depends on the SDK.

### Step 1: Detect `dotnet test` mode

- SDK 10+ with `global.json` `"test": { "runner":
  "Microsoft.Testing.Platform" }` → native **MTP mode**.
- SDK 10+ with runner `VSTest` or no `test` section → **VSTest mode**.
- SDK 8/9 → **VSTest mode** (the only `dotnet test` mode available).

### Step 2: Detect the platform executed by that mode

When mode is native MTP, first verify that the project is an MTP application and
has not explicitly opted into VSTest. A compatible project executes on MTP; a
VSTest-only or opted-out project is a configuration conflict, not an MTP
execution.

When mode is VSTest, first establish that an MTP runner is enabled (MSTest.Sdk,
`EnableMSTestRunner`, `EnableNUnitRunner` with a compatible adapter,
`UseMicrosoftTestingPlatformRunner`, or an MTP-only framework). Then check
`<TestingPlatformDotnetTestSupport>` in the `.csproj`,
`Directory.Build.props`, and `Directory.Packages.props`:

- MTP runner enabled **and** bridge `true` → the VSTest target redirects to
  `InvokeTestingPlatform`, so the executed platform is **MTP**. MTP arguments go
  after `--`.
- Runner or bridge absent → the bridge alone cannot create an MTP application;
  a dual-capable MSTest/NUnit project executes through **VSTest** by default.

Do not confuse the `MSTest` metapackage with the `MSTest.Sdk` project SDK.
`PackageReference Include="MSTest"` plus `EnableMSTestRunner=true` enables the
MSTest MTP runner, but it does **not** implicitly set
`TestingPlatformDotnetTestSupport`. In VSTest command mode, execution remains on
VSTest unless that bridge property evaluates to `true`.

MSTest.Sdk enables the MTP runner by default. Check its resolved version and
evaluated properties for bridge behavior: versions such as 3.8 also set
`TestingPlatformDotnetTestSupport`, while newer SDKs on .NET 10 may expect native
MTP mode instead. `<UseVSTest>true</UseVSTest>` opts back into VSTest.

| Signal | Meaning |
|--------|---------|
| `<Project Sdk="MSTest.Sdk...">` with no `UseVSTest` | MTP application; inspect the resolved SDK version and evaluated bridge property |
| `MSTest` metapackage + `<EnableMSTestRunner>true>` | MTP runner enabled; does not imply the VSTest-to-MTP bridge |
| `<UseMicrosoftTestingPlatformRunner>true` | xUnit MTP runner enabled; still check bridge/mode for `dotnet test` |
| `<EnableMSTestRunner>true>` / `<EnableNUnitRunner>true>` | MTP runner enabled; still check bridge/mode |
| `Microsoft.Testing.Platform` package | MTP-capable application; still check bridge/mode |
| `TUnit` | MTP only; on SDK 8/9 prefer `dotnet run` when no bridge is configured |

> **Critical**: `global.json` decides command mode, not necessarily the executed
> platform. For example, SDK 10 with runner `VSTest` plus
> `TestingPlatformDotnetTestSupport=true` is **VSTest mode executing MTP**.
>
> `Microsoft.NET.Test.Sdk` alone is not decisive; it can remain for compatibility
> in an MTP-enabled project.
> **Key distinction**: VSTest is the established platform that uses
> `vstest.console` under the hood. Microsoft.Testing.Platform (MTP) is the newer
> platform. In compatible SDK-style projects both can be invoked via
> `dotnet test`; classic projects may require their standalone runner.

### Conflicting native-MTP and VSTest opt-out settings

If `global.json` selects native MTP command mode while a project explicitly opts
out of MTP (for example, `MSTest.Sdk` with
`<UseVSTest>true</UseVSTest>`), report the configuration conflict instead of
pretending either platform can execute successfully. Recommend aligning the
project and repository command mode; do not silently override either setting.

### Conditional and per-target-framework properties

Evaluate runner and bridge properties for each target framework. If conditions
produce different executed platforms, report each target explicitly (for
example, `net8.0: VSTest`, `net9.0: MTP`) rather than collapsing the project to
one global platform.
