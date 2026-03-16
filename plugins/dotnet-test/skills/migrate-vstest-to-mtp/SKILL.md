---
name: migrate-vstest-to-mtp
description: >
  Migrates .NET test projects from VSTest to Microsoft.Testing.Platform (MTP).
  Use when user asks to "migrate to MTP", "switch from VSTest", "enable
  Microsoft.Testing.Platform", "use MTP runner", or mentions EnableMSTestRunner,
  EnableNUnitRunner, UseMicrosoftTestingPlatformRunner, or dotnet test exit
  code 8. Supports MSTest, NUnit, and xUnit.net v2.
  Covers runner enablement, CLI argument translation, Directory.Build.props
  and global.json configuration, CI/CD pipeline updates, and MTP extension
  packages. DO NOT USE FOR: migrating between test frameworks
  (MSTest/xUnit/NUnit), xUnit.net v2 to v3 migration, MSTest version upgrades
  (use migrate-mstest-* skills), TFM upgrades, or UWP/WinUI test projects.
---

# VSTest → Microsoft.Testing.Platform Migration

Migrate a .NET test solution from VSTest to Microsoft.Testing.Platform (MTP). The outcome is a solution where all test projects run on MTP, `dotnet test` works correctly, and CI/CD pipelines are updated.

> **Important**: Do not mix VSTest-based and MTP-based .NET test projects in the same solution or run configuration — this is an unsupported scenario.

## When to Use

- Switching from VSTest to Microsoft.Testing.Platform for any supported test framework
- Enabling `dotnet run` / `dotnet watch` / direct executable execution for test projects
- Enabling Native AOT or trimmed test execution
- Replacing `vstest.console.exe` with `dotnet test` on MTP
- Updating CI/CD pipelines from the VSTest task to the .NET Core CLI task
- Updating `dotnet test` arguments from VSTest syntax to MTP syntax

## When Not to Use

- The project already runs on Microsoft.Testing.Platform — migration is done
- Migrating between test frameworks (e.g., MSTest to xUnit.net) — different effort entirely
- The project builds UWP or packaged WinUI test projects — MTP does not support these yet
- The solution mixes .NET and non-.NET test adapters (e.g., JavaScript or C++ adapters) — VSTest is required
- Upgrading MSTest versions — use `migrate-mstest-v1v2-to-v3` or `migrate-mstest-v3-to-v4`

## Inputs

| Input | Required | Description |
|-------|----------|-------------|
| Project or solution path | Yes | The `.csproj`, `.sln`, or `.slnx` entry point containing test projects |
| Test framework | Yes | MSTest, NUnit, or xUnit.net. Auto-detect from package references if not provided |
| .NET SDK version | No | The SDK version used to build. Determines `dotnet test` integration mode. Auto-detect from `global.json` or `dotnet --version` |
| CI system | No | Azure DevOps, GitHub Actions, or other. Determines pipeline update guidance |

## Workflow

### Step 1: Assess the solution

1. Identify the test framework for each test project:
   - **MSTest**: References `MSTest` or `MSTest.TestAdapter`, or uses `MSTest.Sdk` (with `<IsTestApplication>` not set to `false`). Note: `MSTest.TestFramework` alone is a library dependency, not a test project.
   - **NUnit**: References `NUnit3TestAdapter`
   - **xUnit.net**: References `xunit` and `xunit.runner.visualstudio`
2. Check the .NET SDK version (`dotnet --version`) — this determines how `dotnet test` integrates with MTP
3. Check whether a `Directory.Build.props` file exists at the solution or repo root — all MTP properties should go there for consistency
4. Check for `vstest.console.exe` usage in CI scripts or pipeline definitions
5. Check for VSTest-specific `dotnet test` arguments in CI scripts: `--filter`, `--logger`, `--collect`, `--settings`, `--blame*`
6. Run `dotnet test` to establish a baseline of test pass/fail counts

### Step 2: Set up Directory.Build.props

> **Critical**: Always set MTP properties in `Directory.Build.props` at the solution or repo root — never per-project. This prevents inconsistent configuration where some projects use VSTest and others use MTP (an unsupported scenario).

Create or update `Directory.Build.props` with the MTP properties. The exact properties depend on the framework (see Step 3):

```xml
<!-- Directory.Build.props -->
<Project>
  <PropertyGroup>
    <!-- Framework-specific runner property goes here (Step 3) -->
    <!-- dotnet test integration property goes here (Step 4) -->
  </PropertyGroup>
</Project>
```

> **Note**: The framework-specific runner properties (`EnableMSTestRunner`, `EnableNUnitRunner`) automatically set `<OutputType>Exe</OutputType>` on projects that reference the corresponding test framework. You do not need to set `OutputType` manually. MSTest.Sdk also sets `EnableMSTestRunner` automatically. For xUnit.net, `YTest.MTP.XUnit2` handles this automatically.

### Step 3: Enable the framework-specific MTP runner

Each framework has its own opt-in property. Add these in `Directory.Build.props` for consistency.

#### MSTest

**Option A — MSTest NuGet packages (3.2.0+):**

```xml
<PropertyGroup>
  <EnableMSTestRunner>true</EnableMSTestRunner>
</PropertyGroup>
```

Ensure the project references MSTest 3.2.0 or later. Recommend updating to the latest version.

**Option B — MSTest.Sdk:**

When using `MSTest.Sdk`, MTP is enabled by default — no `EnableMSTestRunner` or `OutputType Exe` property is needed (the SDK sets both automatically). The only action is: if the project has `<UseVSTest>true</UseVSTest>`, **remove it**. That property forces the project to use VSTest instead of MTP.

#### NUnit

Requires `NUnit3TestAdapter` **5.0.0** or later.

1. Update `NUnit3TestAdapter` to 5.0.0+:

```xml
<PackageReference Include="NUnit3TestAdapter" Version="5.0.0" />
```

2. Enable the NUnit runner:

```xml
<PropertyGroup>
  <EnableNUnitRunner>true</EnableNUnitRunner>
</PropertyGroup>
```

#### xUnit.net

Add a reference to `YTest.MTP.XUnit2` — this package provides MTP support for xUnit.net v2 projects without requiring an upgrade to xunit.v3:

```xml
<PackageReference Include="YTest.MTP.XUnit2" Version="*" />
```

> **Note**: `YTest.MTP.XUnit2` preserves the VSTest `--filter` syntax, so no filter migration is needed for xUnit.net. It also supports `--settings` for runsettings (xunit-specific configurations only), `xunit.runner.json`, TRX reporting via `--report-trx`, and `--treenode-filter`.

### Step 4: Configure dotnet test integration

The `dotnet test` integration depends on the .NET SDK version.

#### .NET 10 SDK and later (recommended)

Use the native MTP mode by adding a `test` section to `global.json`:

```json
{
  "sdk": {
    "version": "10.0.100"
  },
  "test": {
    "runner": "Microsoft.Testing.Platform"
  }
}
```

In this mode, `dotnet test` arguments are passed directly — for example, `dotnet test --report-trx`.

> **Important**: `global.json` does not support trailing commas. Ensure the JSON is strictly valid.

#### .NET 9 SDK and earlier

Use the VSTest mode of `dotnet test` command to run MTP test projects by adding this property in `Directory.Build.props`:

```xml
<PropertyGroup>
  <TestingPlatformDotnetTestSupport>true</TestingPlatformDotnetTestSupport>
</PropertyGroup>
```

> **Important**: In this mode, you must use `--` to separate `dotnet test` build arguments from MTP arguments. For example: `dotnet test --no-build -- --list-tests`.

### Step 5: Update dotnet test command-line arguments

VSTest-specific arguments must be translated to MTP equivalents. Build-related arguments (`-c`, `-f`, `--no-build`, `--nologo`, `-v`, etc.) are unchanged.

| VSTest argument | MTP equivalent | Notes |
|-----------------|----------------|-------|
| `--test-adapter-path` | Not applicable | MTP does not use external adapter discovery |
| `--blame` | Not applicable | |
| `--blame-crash` | `--crashdump` | Requires `Microsoft.Testing.Extensions.CrashDump` NuGet package |
| `--blame-crash-dump-type <TYPE>` | `--crashdump-type <TYPE>` | Requires CrashDump extension |
| `--blame-hang` | `--hangdump` | Requires `Microsoft.Testing.Extensions.HangDump` NuGet package |
| `--blame-hang-dump-type <TYPE>` | `--hangdump-type <TYPE>` | Requires HangDump extension |
| `--blame-hang-timeout <TIMESPAN>` | `--hangdump-timeout <TIMESPAN>` | Requires HangDump extension |
| `--collect "Code Coverage;Format=cobertura"` | `--coverage --coverage-output-format cobertura` | Per-extension arguments |
| `-d\|--diag <LOG_FILE>` | `--diagnostic` | |
| `--filter <EXPRESSION>` | `--filter <EXPRESSION>` | Same syntax for MSTest, NUnit, and xUnit.net (with `YTest.MTP.XUnit2`) |
| `-l\|--logger trx` | `--report-trx` | Requires `Microsoft.Testing.Extensions.TrxReport` NuGet package |
| `--results-directory <DIR>` | `--results-directory <DIR>` | Same |
| `-s\|--settings <FILE>` | `--settings <FILE>` | MSTest and NUnit still support `.runsettings` |
| `-t\|--list-tests` | `--list-tests` | Same |
| `-- <RunSettings args>` | `--test-parameter` | Applicable only to MSTest and NUnit |

#### Filter migration

**MSTest, NUnit, and xUnit.net (with `YTest.MTP.XUnit2`)**: The VSTest `--filter` syntax is identical on both VSTest and MTP. No changes needed.

### Step 6: Install MTP extension packages (if needed)

If CI scripts use TRX reporting, crash dumps, or hang dumps, add the corresponding NuGet packages:

```xml
<!-- TRX report generation (replaces --logger trx) -->
<PackageReference Include="Microsoft.Testing.Extensions.TrxReport" Version="1.6.2" />

<!-- Crash dump collection (replaces --blame-crash) -->
<PackageReference Include="Microsoft.Testing.Extensions.CrashDump" Version="1.6.2" />

<!-- Hang dump collection (replaces --blame-hang) -->
<PackageReference Include="Microsoft.Testing.Extensions.HangDump" Version="1.6.2" />

<!-- Code coverage (replaces --collect "Code Coverage") -->
<PackageReference Include="Microsoft.Testing.Extensions.CodeCoverage" Version="17.13.0" />
```

### Step 7: Update CI/CD pipelines

#### Azure DevOps

**If using the VSTest task (`VSTest@3`)**: Replace with the .NET Core CLI task (`DotNetCoreCLI@2`):

```yaml
# Before (VSTest task)
- task: VSTest@3
  inputs:
    testAssemblyVer2: '**/*Tests.dll'
    runSettingsFile: 'test.runsettings'

# After (.NET Core CLI task)
- task: DotNetCoreCLI@2
  displayName: Run tests
  inputs:
    command: 'test'
    arguments: '--no-build --configuration Release'
```

**If using DotNetCoreCLI@2 without .NET 10 native MTP**: Add `--` before MTP arguments:

```yaml
- task: DotNetCoreCLI@2
  displayName: Run tests
  inputs:
    command: 'test'
    arguments: '--no-build -- --report-trx --results-directory $(Agent.TempDirectory)'
```

**If using DotNetCoreCLI@2 with .NET 10 native MTP mode** (global.json configured): Pass MTP arguments directly:

```yaml
- task: DotNetCoreCLI@2
  displayName: Run tests
  inputs:
    command: 'test'
    arguments: '--no-build --report-trx --results-directory $(Agent.TempDirectory)'
```

#### GitHub Actions

Update `dotnet test` invocations in workflow files with the same argument translations from Step 5.

#### Replace vstest.console.exe

If any script invokes `vstest.console.exe` directly, replace it with `dotnet test`. The test projects are now executables and can also be run directly.

### Step 8: Handle behavioral differences

#### Zero tests exit code

VSTest silently succeeds when zero tests are discovered. MTP fails with **exit code 8**. Options:

- Pass `--ignore-exit-code 8` when running tests
- Add to `Directory.Build.props`:

```xml
<PropertyGroup>
  <TestingPlatformCommandLineArguments>$(TestingPlatformCommandLineArguments) --ignore-exit-code 8</TestingPlatformCommandLineArguments>
</PropertyGroup>
```

- Use environment variable: `TESTINGPLATFORM_EXITCODE_IGNORE=8`

#### Console.InputEncoding preservation

MTP always preserves the console codepage. If CI sets `chcp 65001` (UTF8) and tests start child processes with `CreateNoWindow = true`, those child processes may receive a UTF8 BOM that VSTest previously did not pass. Workaround:

```csharp
Console.InputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
```

### Step 9: Remove VSTest-only packages (optional)

Once migration is complete and verified, remove packages that are only needed for VSTest:

- `Microsoft.NET.Test.Sdk` — not needed for MTP (MSTest.Sdk v4 already omits it by default)
- `xunit.runner.visualstudio` — only needed for VSTest discovery of xUnit.net (not needed when using `YTest.MTP.XUnit2`)
- `NUnit3TestAdapter` VSTest-only features — the adapter is still needed but only for the MTP runner

> **Note**: If you need to maintain VSTest compatibility during a transition period, keep these packages.

### Step 10: Verify

1. Run `dotnet build` — confirm zero errors
2. Run `dotnet test` — confirm all tests pass
3. Compare test pass/fail counts to the pre-migration baseline
4. Run the test executable directly (e.g., `./bin/Debug/net8.0/MyTests.exe`) — confirm it works
5. Verify CI pipeline produces the expected test result artifacts (TRX files, code coverage, crash dumps)
6. Test that Test Explorer in Visual Studio (17.14+) or VS Code discovers and runs tests

## Validation

- [ ] Framework-specific runner property is set (`EnableMSTestRunner`, `EnableNUnitRunner`) or `YTest.MTP.XUnit2` package is added for xUnit.net — runner properties also set `<OutputType>Exe</OutputType>` automatically
- [ ] `dotnet test` integration is configured (global.json for .NET 10+ or `TestingPlatformDotnetTestSupport` for .NET 9 and earlier)
- [ ] All VSTest-specific CLI arguments are translated to MTP equivalents
- [ ] Required MTP extension NuGet packages are installed (TrxReport, CrashDump, HangDump, CodeCoverage as needed)
- [ ] CI/CD pipeline tasks are updated (no VSTest@3 task, correct argument syntax)
- [ ] Project builds with zero errors
- [ ] All tests pass with `dotnet test`
- [ ] No tests were lost during migration (compare test counts)
- [ ] Test executable runs directly without `dotnet test`

## Common Pitfalls

| Pitfall | Solution |
|---------|----------|
| Mixing VSTest and MTP projects in the same solution | Migrate all test projects together — mixed mode is unsupported |
| Missing runner property or MTP package | Add `EnableMSTestRunner`, `EnableNUnitRunner`, or `YTest.MTP.XUnit2` — runner properties also set `OutputType Exe` automatically |
| `dotnet test` arguments ignored on .NET 9 and earlier | Use `--` to separate build args from MTP args: `dotnet test -- --report-trx` |
| `--logger trx` produces no output | Replace with `--report-trx` and install `Microsoft.Testing.Extensions.TrxReport` |
| `--collect "Code Coverage"` does nothing | Replace with `--coverage` and install `Microsoft.Testing.Extensions.CodeCoverage` |
| `--filter` fails on xUnit.net | Use `YTest.MTP.XUnit2` which preserves VSTest `--filter` syntax for xUnit.net v2 |
| Exit code 8 on CI without failures | MTP fails when zero tests run; use `--ignore-exit-code 8` or fix test discovery |
| VSTest task in Azure DevOps fails | Replace `VSTest@3` with `DotNetCoreCLI@2` task |
| NUnit3TestAdapter < 5.0.0 | MTP requires adapter version 5.0.0 or later |
| xUnit.net v2 does not support MTP | Add `YTest.MTP.XUnit2` package — provides MTP support for xUnit.net v2 without requiring an upgrade to v3 |
| Test Explorer not discovering tests | Update Visual Studio to 17.14+ or use VS Code with C# DevKit |
| MSTest.Sdk v4 + vstest.console no longer works | MSTest.Sdk v4 no longer adds `Microsoft.NET.Test.Sdk` — add it explicitly or switch to `dotnet test` |
| Properties set per-project instead of in Directory.Build.props | Centralize in `Directory.Build.props` to avoid inconsistent configuration |

## More Info

- [Test platforms overview](https://learn.microsoft.com/dotnet/core/testing/test-platforms-overview)
- [Migrate from VSTest to Microsoft.Testing.Platform](https://learn.microsoft.com/dotnet/core/testing/migrating-vstest-microsoft-testing-platform)
- [Microsoft.Testing.Platform overview](https://learn.microsoft.com/dotnet/core/testing/microsoft-testing-platform-intro)
- [Testing with dotnet test](https://learn.microsoft.com/dotnet/core/testing/unit-testing-with-dotnet-test)
- [Microsoft.Testing.Platform CLI options](https://learn.microsoft.com/dotnet/core/testing/microsoft-testing-platform-cli-options)
- [Microsoft.Testing.Platform extensions](https://learn.microsoft.com/dotnet/core/testing/unit-testing-platform-extensions)
