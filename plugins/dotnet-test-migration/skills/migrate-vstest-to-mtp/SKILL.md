---
name: migrate-vstest-to-mtp
description: >
  Use this skill before answering, planning, or editing whenever .NET tests or
  CI are switching from VSTest to Microsoft.Testing.Platform (MTP), or an MTP
  migration behaves differently. Triggers include "switch from VSTest";
  MSTest/NUnit/xUnit MTP enablement; OutputType=Exe only for test projects in
  Directory.Build.props; EnableMSTestRunner, EnableNUnitRunner,
  UseMicrosoftTestingPlatformRunner, or YTest.MTP.XUnit2; .NET 10 global.json
  test.runner and TestingPlatformDotnetTestSupport; translating VSTest
  filters, logger, coverage, blame, or dump arguments; replacing VSTest@3;
  and exit code 8 or zero tests. Also use for xUnit v3 MTP filters during a
  v2-to-v3 upgrade. Do not use for framework conversion, TFM, UWP, or WinUI.
license: MIT
---

# VSTest -> Microsoft.Testing.Platform Migration

Migrate a .NET test solution from VSTest to Microsoft.Testing.Platform (MTP). The outcome is a solution where all test projects run on MTP, `dotnet test` works correctly, and CI/CD pipelines are updated.

## First Action

Inspect the supplied project, `Directory.Build.props`, `global.json`, and CI
files before searching the web or answering from memory. Resolve the framework
and SDK mode first: .NET 9 and earlier use the compatibility property plus the
`--` separator; .NET 10 native MTP uses `global.json`, removes that property,
and passes MTP arguments without the separator. For central properties, never
condition on `IsTestProject` in `Directory.Build.props`; use a property already
available there, such as `MSBuildProjectName`.

> **Important**: Do not mix VSTest-based and MTP-based .NET test projects in the same solution or run configuration -- this is an unsupported scenario.

## When to Use

- Switching from VSTest to Microsoft.Testing.Platform for any supported test framework
- Enabling `dotnet run` / `dotnet watch` / direct executable execution for test projects
- Enabling Native AOT or trimmed test execution
- Replacing `vstest.console.exe` with `dotnet test` on MTP
- Updating CI/CD pipelines from the VSTest task to the .NET Core CLI task
- Updating `dotnet test` arguments from VSTest syntax to MTP syntax

## When Not to Use

- The project already runs on Microsoft.Testing.Platform and there is no remaining MTP behavioral difference to resolve (e.g., exit code 8 for zero tests discovered)
- Migrating between test frameworks (e.g., MSTest to xUnit.net) -- different effort entirely
- The project builds UWP or packaged WinUI test projects -- MTP does not support these yet
- The solution mixes .NET and non-.NET test adapters (e.g., JavaScript or C++ adapters) -- VSTest is required
- Upgrading MSTest versions -- use `migrate-mstest-v1v2-to-v3` or `migrate-mstest-v3-to-v4`

## Inputs

| Input | Required | Description |
|-------|----------|-------------|
| Project or solution path | No | The `.csproj`, `.sln`, or `.slnx` entry point containing test projects. **Discover it yourself** by globbing the working directory; ask only when nothing is found or the choice is genuinely ambiguous |
| Test framework | No | MSTest, NUnit, xUnit.net v2, or xUnit.net v3. Auto-detected from package references |
| .NET SDK version | No | Determines `dotnet test` integration mode. Prefer the repository's `global.json` or explicitly stated CI SDK; use host `dotnet --version` only when the repository does not pin or state one |
| CI/CD pipeline files | No | Paths to pipeline definitions that invoke `vstest.console` or `dotnet test` |

## Execution and Answer Contract

- Discover project, props, `global.json`, and pipeline files in the current working directory. Open literal search results; the skill directory is not the user's repository. Continue after skill activation and do not ask for a discoverable path.
- For an implementation request, edit the files, then validate the effective MSBuild properties, the translated command, test counts, and requested artifacts. For a question, provide one exact command/configuration for the detected SDK and framework rather than a menu of near-equivalents.
- Preserve all build arguments and avoid introducing `--no-build`, new settings files, or unrelated package upgrades. Never retain `--settings <file>` unless that file exists.
- For .NET 9 and earlier, show the `--` separator. For .NET 10 native MTP mode, explicitly say to remove it.
- When suppressing exit code 8 is intentional, warn that broad suppression can hide an accidental empty run caused by a bad filter; scope it to the known zero-test project/configuration.
- For an exit-code-8 question, show all three concrete forms: `--ignore-exit-code 8`, `TestingPlatformCommandLineArguments`, and `TESTINGPLATFORM_EXITCODE_IGNORE=8`.
- For xUnit v3 filters, give the directly usable `--filter-class`/`--filter-method`/`--filter-trait` command and explain AND behavior. When the resolved xUnit version supports it, include `--filter-query` path syntax for complex expressions and link the xUnit query-filter documentation.
- When translating reporters, coverage, or dumps, name each required package in the answer; a correct-looking option without its owning extension package is incomplete.
- Final results must name the framework runner opt-in, repository-selected SDK integration mode, exact translated command, extension packages, and verification evidence.

## Workflow

### Step 1: Assess the solution

1. Identify the test framework for each test project -- see the `platform-detection` skill for the package-to-framework mapping. Key indicators:
   - **MSTest**: References `MSTest` or `MSTest.TestAdapter`, or uses `MSTest.Sdk` (with `<IsTestApplication>` not set to `false`). Note: `MSTest.TestFramework` alone is a library dependency, not a test project.
   - **NUnit**: References `NUnit3TestAdapter`
   - **xUnit.net**: References `xunit` and `xunit.runner.visualstudio`
2. Resolve the SDK used by the repository/CI from `global.json` or explicit user context. Fall back to `dotnet --version` only when neither exists; the agent host SDK must not silently override a stated .NET 8/9 CI target.
3. Check whether a `Directory.Build.props` file exists at the solution or repo root -- all MTP properties should go there for consistency
4. Check for `vstest.console.exe` usage in CI scripts or pipeline definitions
5. Check for VSTest-specific `dotnet test` arguments in CI scripts: `--filter`, `--logger`, `--collect`, `--settings`, `--blame*`
6. Run `dotnet test` to establish a baseline of test pass/fail counts

### Step 2: Set up Directory.Build.props

> **Critical**: Set MTP runner properties in `Directory.Build.props` at the solution or repo root whenever possible, rather than per-project. This prevents inconsistent configuration where some projects use VSTest and others use MTP (an unsupported scenario).
> **Note**: MTP also requires test projects to have `<OutputType>Exe</OutputType>`. Only `MSTest.Sdk` sets this automatically. For all other setups (MSTest NuGet packages with `EnableMSTestRunner`, NUnit with `EnableNUnitRunner`, xUnit.net with `YTest.MTP.XUnit2`), prefer setting `<OutputType>Exe</OutputType>` centrally in `Directory.Build.props` with a condition that targets only test projects. If you cannot reliably target only test projects from `Directory.Build.props`, setting `<OutputType>Exe</OutputType>` per-project is an acceptable exception.
>
> **Conditioning in `Directory.Build.props`**: Do NOT use `Condition="'$(IsTestProject)' == 'true'"` -- `IsTestProject` is set by the test SDK targets later in evaluation and is not available when `Directory.Build.props` is imported. Use a property that is available early, such as `MSBuildProjectName`, to target test projects by naming convention. For example, if all test projects end in `.Tests`:
>
> ```xml
> <PropertyGroup Condition="$(MSBuildProjectName.EndsWith('.Tests'))">
>   <OutputType>Exe</OutputType>
> </PropertyGroup>
> ```
>
> Adjust the condition (e.g., `.EndsWith('Tests')`, `.Contains('.Test')`) to match the test project naming convention used in the repository.
>
> Put every applicable central property in that same condition: `OutputType`,
> the framework runner opt-in, and `TestingPlatformDotnetTestSupport` on .NET 9
> and earlier. Do not leave an unconditional runner property that still affects
> production projects.

### Step 3: Enable the framework-specific MTP runner

Each framework has its own opt-in property. Add these in `Directory.Build.props` for consistency.

#### MSTest

**Option A -- MSTest NuGet packages (3.2.0+):**

```xml
<PropertyGroup>
  <EnableMSTestRunner>true</EnableMSTestRunner>
  <OutputType>Exe</OutputType>
</PropertyGroup>
```

Ensure the project references MSTest 3.2.0 or later. If the version is already 3.2.0+, no MSTest version upgrade is needed for MTP migration.

**Option B -- MSTest.Sdk:**

When using `MSTest.Sdk`, MTP is enabled by default -- no `EnableMSTestRunner` or `OutputType Exe` property is needed (the SDK sets both automatically). The only action is: if the project has `<UseVSTest>true</UseVSTest>`, **remove it**. That property forces the project to use VSTest instead of MTP.

#### NUnit

Requires `NUnit3TestAdapter` **5.0.0** or later.

1. Update `NUnit3TestAdapter` to 5.0.0+:

```xml
<PackageReference Include="NUnit3TestAdapter" Version="5.0.0" />
```

1. Enable the NUnit runner:

```xml
<PropertyGroup>
  <EnableNUnitRunner>true</EnableNUnitRunner>
  <OutputType>Exe</OutputType>
</PropertyGroup>
```

#### xUnit.net

Add a reference to `YTest.MTP.XUnit2` -- this package provides MTP support for xUnit.net v2 projects without requiring an upgrade to xunit.v3. You must also set `OutputType` to `Exe`:

```xml
<PackageReference Include="YTest.MTP.XUnit2" Version="0.4.0" />
```

```xml
<PropertyGroup>
  <OutputType>Exe</OutputType>
</PropertyGroup>
```

> **Note**: `YTest.MTP.XUnit2` preserves the VSTest `--filter` syntax, so no filter migration is needed for xUnit.net v2. It also supports `--settings` for runsettings (xunit-specific configurations only), `xunit.runner.json`, TRX reporting via `--report-trx`, and `--treenode-filter`.

#### xUnit.net v3

xUnit.net v3 (`xunit.v3` package) has built-in MTP support. Enable it with:

```xml
<PropertyGroup>
  <UseMicrosoftTestingPlatformRunner>true</UseMicrosoftTestingPlatformRunner>
</PropertyGroup>
```

> **Important**: xUnit.net v3 on MTP does NOT support the VSTest `--filter` syntax. You must translate filters to xUnit.net v3's native filter options (see Step 5).

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

In this mode, `dotnet test` arguments are passed directly -- for example, `dotnet test --report-trx`.

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
| `--filter <EXPRESSION>` | `--filter <EXPRESSION>` | Same syntax for MSTest, NUnit, and xUnit.net v2 (with `YTest.MTP.XUnit2`). For xUnit.net v3, see filter migration below |
| `-l\|--logger trx` | `--report-trx` | Requires `Microsoft.Testing.Extensions.TrxReport` NuGet package |
| `--results-directory <DIR>` | `--results-directory <DIR>` | Same |
| `-s\|--settings <FILE>` | `--settings <FILE>` | MSTest and NUnit still support `.runsettings` |
| `-t\|--list-tests` | `--list-tests` | Same |
| `-- <RunSettings args>` | `--test-parameter` | Applicable only to MSTest and NUnit |

#### Filter migration

**MSTest, NUnit, and xUnit.net v2 (with `YTest.MTP.XUnit2`)**: The VSTest `--filter` syntax is identical on both VSTest and MTP. No changes needed.

**xUnit.net v3 (native MTP)**: xUnit.net v3 does NOT support the VSTest `--filter` syntax on MTP. You must translate filters to xUnit.net v3's native filter options.

#### xUnit.net v3 filter flags

| Flag | Description |
|------|-------------|
| `--filter-class "name"` | Run all tests in a given class. Supports wildcards (`*`). |
| `--filter-not-class "name"` | Exclude all tests in a given class |
| `--filter-method "name"` | Run a specific test method |
| `--filter-not-method "name"` | Exclude a specific test method |
| `--filter-namespace "name"` | Run all tests in a namespace |
| `--filter-not-namespace "name"` | Exclude all tests in a namespace |
| `--filter-trait "name=value"` | Run tests with a matching trait |
| `--filter-not-trait "name=value"` | Exclude tests with a matching trait |

Multiple values can be specified with a single flag: `--filter-class Foo Bar`.

#### VSTest → xUnit.net v3 filter translation table

| VSTest `--filter` syntax | xUnit.net v3 MTP equivalent | Notes |
|---|---|---|
| `FullyQualifiedName~ClassName` | `--filter-class *ClassName*` | Wildcards required for substring match |
| `FullyQualifiedName=Ns.Class.Method` | `--filter-method Ns.Class.Method` | Exact match on fully qualified method |
| `Name=MethodName` | `--filter-method *MethodName*` | Wildcards for substring match |
| `Category=Value` (trait) | `--filter-trait "Category=Value"` | Filter by trait name/value pair |
| Complex expressions | `--filter-query "expr"` | Uses xUnit.net query filter language (see below) |

#### xUnit.net v3 query filter language

For complex expressions, use `--filter-query` with a path-segment syntax:

```text
/<assemblyFilter>/<namespaceFilter>/<classFilter>/<methodFilter>[traitName=traitValue]
```

Each segment matches against: assembly name, namespace, class name, method name. Use `*` for "match all" in any segment. Documentation: <https://xunit.net/docs/query-filter-language>

Prefer the directly corresponding `--filter-class` / `--filter-method` /
`--filter-trait` flags when they express the original filter. Use
`--filter-query` only after validating the query with `--list-tests`; a
syntactically accepted query that selects zero tests is not a successful
translation.

#### Translation example

```shell
# VSTest
dotnet test --filter "FullyQualifiedName~IntegrationTests&Category=Smoke"

# xUnit.net v3 MTP -- using individual filters (AND behavior)
dotnet test -- --filter-class *IntegrationTests* --filter-trait "Category=Smoke"

# xUnit.net v3 MTP -- using query language (assembly/namespace/class/method[trait])
dotnet test -- --filter-query "/*/*/*IntegrationTests*/*[Category=Smoke]"
```

> **Note**: When combining `--filter-class` and `--filter-trait`, both conditions must match (AND behavior). For complex expressions, use `--filter-query` with the path-segment syntax. See the [xUnit.net query filter language docs](https://xunit.net/docs/query-filter-language) for full reference.

### Step 6: Install MTP extension packages (if needed)

If CI scripts use TRX reporting, crash dumps, hang dumps, or coverage, add the
owning packages. Under Central Package Management, the project references are:

```xml
<ItemGroup>
  <PackageReference Include="Microsoft.Testing.Extensions.TrxReport" />
  <PackageReference Include="Microsoft.Testing.Extensions.CrashDump" />
  <PackageReference Include="Microsoft.Testing.Extensions.HangDump" />
  <PackageReference Include="Microsoft.Testing.Extensions.CodeCoverage" />
</ItemGroup>
```

Add matching `PackageVersion` entries in `Directory.Packages.props`. Without
Central Package Management, put exact versions resolved from the configured
feed on these references. Keep the versions compatible with the selected MTP
stack; do not copy fixed versions from migration guidance.

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

**If already using DotNetCoreCLI@2**: Update arguments per Step 5 translations. Remember the `--` separator on .NET 9 and earlier:

```yaml
- task: DotNetCoreCLI@2
  displayName: Run tests
  inputs:
    command: 'test'
    arguments: '--no-build -- --report-trx --results-directory $(Agent.TempDirectory)'
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

### Step 9: Remove VSTest-only packages (optional)

Once migration is complete and verified, remove packages that are only needed for VSTest:

- `Microsoft.NET.Test.Sdk` -- not needed for MTP (MSTest.Sdk v4 already omits it by default)
- `xunit.runner.visualstudio` -- only needed for VSTest discovery of xUnit.net (not needed when using `YTest.MTP.XUnit2`)
- `NUnit3TestAdapter` VSTest-only features -- the adapter is still needed but only for the MTP runner

> **Note**: If you need to maintain VSTest compatibility during a transition period, keep these packages.

### Step 10: Verify

1. Run `dotnet build` -- confirm zero errors
2. Run `dotnet test` -- confirm all tests pass
3. Compare test pass/fail counts to the pre-migration baseline
4. Run the test executable directly (e.g., `./bin/Debug/net8.0/MyTests.exe`) -- confirm it works
5. Verify CI pipeline produces the expected test result artifacts (TRX files, code coverage, crash dumps)
6. Test that Test Explorer in Visual Studio (17.14+) or VS Code discovers and runs tests

## Validation

- [ ] All test projects use MTP runner (no VSTest-only configuration remains)
- [ ] `dotnet build` completes with zero errors
- [ ] `dotnet test` passes all tests and test counts match pre-migration baseline
- [ ] Test executable runs directly (e.g., `./bin/Debug/net8.0/MyTests.exe`)
- [ ] CI pipeline produces expected test result artifacts (TRX files, code coverage, crash dumps)
- [ ] Test Explorer in Visual Studio or VS Code discovers and runs tests
- [ ] No `vstest.console.exe` invocations remain in CI scripts
- [ ] `<OutputType>Exe</OutputType>` is set for all non-MSTest.Sdk test projects

## Common Pitfalls

| Pitfall | Solution |
|---------|----------|
| Mixing VSTest and MTP projects in the same solution | Migrate all test projects together -- mixed mode is unsupported |
| `dotnet test` arguments ignored on .NET 9 and earlier | Use `--` to separate build args from MTP args: `dotnet test -- --report-trx` |
| Exit code 8 on CI without failures | MTP fails when zero tests run; use `--ignore-exit-code 8` or fix test discovery |
| MSTest.Sdk v4 + vstest.console no longer works | MSTest.Sdk v4 no longer adds `Microsoft.NET.Test.Sdk` -- add it explicitly or switch to `dotnet test` |
| Missing `<OutputType>Exe</OutputType>` | Required for all setups except MSTest.Sdk (which sets it automatically) |
| Using `Condition="'$(IsTestProject)' == 'true'"` in `Directory.Build.props` | `IsTestProject` is not yet defined when `Directory.Build.props` is evaluated -- use `$(MSBuildProjectName.EndsWith('.Tests'))` (or a similar name-based check) instead |

## Next Steps

- Use `run-tests` for running tests on the new MTP platform
- Use `mtp-hot-reload` for iterative test fixing with hot reload on MTP

## More Info

- [Test platforms overview](https://learn.microsoft.com/dotnet/core/testing/test-platforms-overview)
- [Migrate from VSTest to Microsoft.Testing.Platform](https://learn.microsoft.com/dotnet/core/testing/migrating-vstest-microsoft-testing-platform)
- [Microsoft.Testing.Platform overview](https://learn.microsoft.com/dotnet/core/testing/microsoft-testing-platform-intro)
- [Testing with dotnet test](https://learn.microsoft.com/dotnet/core/testing/unit-testing-with-dotnet-test)
- [Microsoft.Testing.Platform CLI options](https://learn.microsoft.com/dotnet/core/testing/microsoft-testing-platform-cli-options)
- [Microsoft.Testing.Platform extensions](https://learn.microsoft.com/dotnet/core/testing/unit-testing-platform-extensions)
