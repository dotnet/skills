---
name: migrate-vstest-to-mtp
description: >
  Migrate .NET test projects from VSTest to Microsoft.Testing.Platform (MTP).
  USE FOR: enabling MTP runners (EnableMSTestRunner, EnableNUnitRunner,
  UseMicrosoftTestingPlatformRunner), adding OutputType Exe, removing UseVSTest
  from MSTest.Sdk, upgrading xunit v2 to xunit.v3, upgrading NUnit3TestAdapter
  to 5.0.0+, translating CLI args (--logger to --report-trx, --collect to
  --coverage, --blame-crash to --crashdump), migrating xUnit.net --filter to
  --filter-class/--filter-trait/--filter-query, configuring global.json MTP mode
  (.NET 10+) or TestingPlatformDotnetTestSupport (.NET 9-), replacing VSTest@3
  with DotNetCoreCLI@2, installing MTP extension packages (TrxReport, CrashDump,
  HangDump, CodeCoverage), fixing exit code 8 (--ignore-exit-code 8,
  TESTINGPLATFORM_EXITCODE_IGNORE). Set properties in Directory.Build.props.
  DO NOT USE FOR: framework migration (MSTest↔xUnit/NUnit), MSTest version
  upgrades (use migrate-mstest-* skills), TFM upgrades, UWP/WinUI adapters.
---

# VSTest → Microsoft.Testing.Platform Migration

Migrate a .NET test solution from VSTest to Microsoft.Testing.Platform (MTP). MTP is an executable-first test platform that supports Native AOT, trimming, `dotnet run`, `dotnet watch`, and direct executable execution. The outcome is a solution where all test projects run on MTP, `dotnet test` works correctly, and CI/CD pipelines are updated.

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
   - **MSTest**: References `MSTest`, `MSTest.TestFramework`, or uses `MSTest.Sdk`
   - **NUnit**: References `NUnit` and `NUnit3TestAdapter`
   - **xUnit.net**: References `xunit` (v2) or `xunit.v3` (v3)
2. Check the .NET SDK version (`dotnet --version`) — this determines how `dotnet test` integrates with MTP
3. Check whether a `Directory.Build.props` file exists at the solution or repo root — all MTP properties should go there for consistency
4. Check for `vstest.console.exe` usage in CI scripts or pipeline definitions
5. Check for VSTest-specific `dotnet test` arguments in CI scripts: `--filter`, `--logger`, `--collect`, `--settings`, `--blame*`
6. Run `dotnet test` to establish a baseline of test pass/fail counts

### Step 2: Set up Directory.Build.props

> **Critical**: Always set MTP properties in `Directory.Build.props` at the solution or repo root — never per-project. This prevents inconsistent configuration where some projects use VSTest and others use MTP (an unsupported scenario).

Create or update `Directory.Build.props` with **all** required MTP properties up front. The exact properties depend on the framework (see Step 3), but `OutputType Exe` is always required:

```xml
<!-- Directory.Build.props -->
<Project>
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <!-- Framework-specific runner property goes here (Step 3) -->
    <!-- dotnet test integration property goes here (Step 4) -->
  </PropertyGroup>
</Project>
```

If `Directory.Build.props` is shared with non-test projects, scope it with a condition:

```xml
<PropertyGroup Condition="'$(IsTestProject)' == 'true'">
  <OutputType>Exe</OutputType>
</PropertyGroup>
```

> **Note**: xUnit.net v3 and MSTest.Sdk already set `OutputType Exe` automatically. MSTest.Sdk also sets `EnableMSTestRunner` automatically. When using these, the properties are already handled — but including them in `Directory.Build.props` is harmless and keeps configuration explicit.

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

When using `MSTest.Sdk`, MTP is enabled by default — no `EnableMSTestRunner` or `OutputType Exe` property is needed (the SDK sets both automatically). The only action is: if the project has `<UseVSTest>true</UseVSTest>`, **remove it**. That property is what opts out of MTP.

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

Requires **xunit.v3** (v3 or later). If the project still uses xunit v2, it must be upgraded to v3 first.

1. Replace xunit v2 packages with xunit.v3:

```xml
<!-- Remove these v2 packages -->
<!-- <PackageReference Include="xunit" Version="2.x.x" /> -->
<!-- <PackageReference Include="xunit.runner.visualstudio" Version="2.x.x" /> -->

<!-- Add xunit.v3 -->
<PackageReference Include="xunit.v3" Version="1.0.1" />
```

2. Enable the MTP runner:

```xml
<PropertyGroup>
  <UseMicrosoftTestingPlatformRunner>true</UseMicrosoftTestingPlatformRunner>
</PropertyGroup>
```

> **Tip**: To preserve VSTest compatibility during a transition period, keep `xunit.runner.visualstudio` and `Microsoft.NET.Test.Sdk` alongside `xunit.v3`. Remove them once the migration is complete.

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

In this mode, `dotnet test` arguments are passed directly — no extra `--` separator is needed.

#### .NET 9 SDK and earlier

Use the VSTest-bridge mode by adding this property in `Directory.Build.props`:

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
| `--filter <EXPRESSION>` | Framework-dependent (see below) | |
| `-l\|--logger trx` | `--report-trx` | Requires `Microsoft.Testing.Extensions.TrxReport` NuGet package |
| `--results-directory <DIR>` | `--results-directory <DIR>` | Same |
| `-s\|--settings <FILE>` | `--settings <FILE>` | MSTest and NUnit still support `.runsettings`. MTP also supports `testconfig.json` |
| `-t\|--list-tests` | `--list-tests` | Same |
| `-- <RunSettings args>` | `--test-parameter` | Provided by VSTestBridge |

#### Filter migration (important for xUnit.net)

**MSTest and NUnit**: The `--filter` syntax is identical on both VSTest and MTP. No changes needed.

**xUnit.net (breaking change)**: The VSTest `--filter` syntax is **not supported** on MTP. You must migrate to xUnit.net v3 native filter options. If your CI uses `--filter`, this is a required change.

##### Simple filter translations

| VSTest filter | xUnit.net MTP filter |
|---------------|----------------------|
| `--filter "FullyQualifiedName~MyClass"` | `--filter-class MyNamespace.MyClass` |
| `--filter "FullyQualifiedName~MyMethod"` | `--filter-method MyMethod` |
| `--filter "Category=Integration"` | `--filter-trait "Category=Integration"` |
| `--filter "FullyQualifiedName!~Slow"` | `--filter-not-method Slow` |

Multiple `--filter-*` options can be combined on the same command line. They are ANDed together:

```shell
dotnet test -- --filter-class MyNamespace.IntegrationTests --filter-trait "Category=Smoke"
```

##### Compound expressions with --filter-query

For compound VSTest filter expressions (using `&`, `|`, `!`), use `--filter-query` which supports the [xUnit.net v3 query filter language](https://xunit.net/docs/query-filter-language). The syntax is segment-based:

```
/<assemblyFilter>/<namespaceFilter>/<classFilter>/<methodFilter>
```

Traits use bracket notation: `[name=value]` or `[name!=value]`. Combine expressions within a segment using parentheses with `&` (AND) or `|` (OR). Use `*` as a wildcard at the start or end of any segment.

| VSTest compound filter | xUnit.net MTP --filter-query |
|------------------------|------------------------------|
| `--filter "FullyQualifiedName~IntegrationTests&Category=Smoke"` | `--filter-query "/*/*/IntegrationTests*/*[Category=Smoke]"` |
| `--filter "Category=Unit\|Category=Integration"` | `--filter-query "/[(Category=Unit)\|(Category=Integration)]"` |
| `--filter "FullyQualifiedName~Tests&FullyQualifiedName!~Slow"` | `--filter-query "/*/*/(*Tests*)&(!*Slow*)"` |
| `--filter "FullyQualifiedName~MyMethod"` | `--filter-query "/*/*/*/MyMethod*"` |

> **Reference**: See the [xUnit.net v3 query filter language documentation](https://xunit.net/docs/query-filter-language) for the full specification, including escaping special characters and negation.

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
- `xunit.runner.visualstudio` — only needed for VSTest discovery of xUnit.net
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

- [ ] All test projects have `<OutputType>Exe</OutputType>`
- [ ] Framework-specific runner property is set (`EnableMSTestRunner`, `EnableNUnitRunner`, or `UseMicrosoftTestingPlatformRunner`)
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
| Missing `<OutputType>Exe</OutputType>` | Add to all test projects or `Directory.Build.props` |
| `dotnet test` arguments ignored on .NET 9 and earlier | Use `--` to separate build args from MTP args: `dotnet test -- --report-trx` |
| `--logger trx` produces no output | Replace with `--report-trx` and install `Microsoft.Testing.Extensions.TrxReport` |
| `--collect "Code Coverage"` does nothing | Replace with `--coverage` and install `Microsoft.Testing.Extensions.CodeCoverage` |
| `--filter` fails on xUnit.net v3 | VSTest `--filter` is not supported; use `--filter-class`, `--filter-method`, `--filter-trait` for simple filters, or `--filter-query` for compound expressions |
| Exit code 8 on CI without failures | MTP fails when zero tests run; use `--ignore-exit-code 8` or fix test discovery |
| VSTest task in Azure DevOps fails | Replace `VSTest@3` with `DotNetCoreCLI@2` task |
| NUnit3TestAdapter < 5.0.0 | MTP requires adapter version 5.0.0 or later |
| xUnit.net v2 does not support MTP | Upgrade to xunit.v3 first — MTP support is built into v3 |
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
