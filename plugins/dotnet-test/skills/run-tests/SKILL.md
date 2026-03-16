---
name: run-tests
description: >
  Runs .NET tests with dotnet test. Use when user says "run tests", "execute
  tests", "dotnet test", "test filter", "tests not running", or needs to
  detect the test platform (VSTest or Microsoft.Testing.Platform), identify the
  test framework, apply test filters, or troubleshoot test execution failures.
  Covers MSTest, xUnit, NUnit, and TUnit across both VSTest and MTP platforms.
  DO NOT USE FOR: writing or generating test code, CI/CD pipeline
  configuration, or debugging failing test logic.
---

# Run .NET Tests

Detect the test platform and framework, run tests, and apply filters using `dotnet test`.

## When to Use

- User wants to run tests in a .NET project
- User needs to run a subset of tests using filters
- User needs help detecting which test platform (VSTest vs MTP) or framework is in use
- User wants to understand the correct filter syntax for their setup

## When Not to Use

- User needs to write or generate test code (use general coding assistance)
- User needs CI/CD pipeline configuration (use CI-specific skills)
- User needs to debug a test (use debugging skills)

## Inputs

| Input | Required | Description |
|-------|----------|-------------|
| Project or solution path | No | Path to the test project (.csproj) or solution (.sln). Defaults to current directory. |
| Filter expression | No | Filter expression to select specific tests |
| Target framework | No | Target framework moniker to run against (e.g., `net8.0`) |

## Workflow

### Quick Reference

| Platform | SDK | Command pattern |
|----------|-----|----------------|
| VSTest | Any | `dotnet test [<path>] [--filter <expr>] [--logger trx]` |
| MTP | 8 or 9 | `dotnet test [<path>] -- <MTP_ARGS>` |
| MTP | 10+ | `dotnet test --project <path> <MTP_ARGS>` |

**Detection files to always check** (in order): `global.json` → `.csproj` → `Directory.Build.props` → `Directory.Packages.props`

### Step 1: Detect the test platform and framework

Determine **which test platform** (VSTest or Microsoft.Testing.Platform) and **which test framework** (MSTest, xUnit, NUnit, TUnit) the project uses.

#### Detecting the test framework

Read the `.csproj` file **and** `Directory.Build.props` / `Directory.Packages.props` (for centrally managed dependencies) and look for:

| Package or SDK reference | Framework |
|--------------------------|-----------|
| `MSTest` (metapackage, recommended) or `<Sdk Name="MSTest.Sdk">` | MSTest |
| `MSTest.TestFramework` + `MSTest.TestAdapter` | MSTest (also valid for v3/v4) |
| `xunit`, `xunit.v3`, `xunit.v3.mtp-v1`, `xunit.v3.mtp-v2`, `xunit.v3.core.mtp-v1`, `xunit.v3.core.mtp-v2` | xUnit |
| `NUnit` + `NUnit3TestAdapter` | NUnit |
| `TUnit` | TUnit (MTP only) |

#### Detecting the test platform

The detection logic depends on the .NET SDK version. Run `dotnet --version` to determine it.

##### .NET SDK 10+

On .NET 10+, the `global.json` `test.runner` setting is the **authoritative source**:

- If `global.json` contains `"test": { "runner": "Microsoft.Testing.Platform" }` → **MTP**
- If `global.json` has `"runner": "VSTest"`, or no `test` section exists → **VSTest**

> **Important**: On .NET 10+, `<TestingPlatformDotnetTestSupport>` alone does **not** switch to MTP. The `global.json` runner setting takes precedence. If the runner is VSTest (or unset), the project uses VSTest regardless of `TestingPlatformDotnetTestSupport`.

##### .NET SDK 8 or 9

On older SDKs, check these signals in priority order:

**1. Check the `<TestingPlatformDotnetTestSupport>` MSBuild property.** Look in the `.csproj`, `Directory.Build.props`, **and** `Directory.Packages.props`. If set to `true` in **any** of these files, the project uses **MTP**.

> **Critical**: Always read `Directory.Build.props` and `Directory.Packages.props` if they exist. MTP properties are frequently set there instead of in the `.csproj`, so checking only the project file will miss them.

**2. Check project-level signals:**

| Signal | Platform |
|--------|----------|
| `<Sdk Name="MSTest.Sdk">` as project SDK | **MTP** by default |
| `<UseMicrosoftTestingPlatformRunner>true</UseMicrosoftTestingPlatformRunner>` | **MTP** runner (xUnit) |
| `<EnableMSTestRunner>true</EnableMSTestRunner>` | **MTP** runner (MSTest) |
| `<EnableNUnitRunner>true</EnableNUnitRunner>` | **MTP** runner (NUnit) |
| `Microsoft.Testing.Platform` package referenced directly | **MTP** |
| `TUnit` package referenced | **MTP** (TUnit is MTP-only) |

> **Note**: The presence of `Microsoft.NET.Test.Sdk` does **not** necessarily mean VSTest. Some frameworks (e.g., MSTest) pull it in transitively for compatibility, even when MTP is enabled. Do not use this package as a signal on its own — always check the MTP signals above first.

> **Key distinction**: VSTest is the classic platform that uses `vstest.console` under the hood. Microsoft.Testing.Platform (MTP) is the newer, faster platform. Both can be invoked via `dotnet test`, but their filter syntax and CLI options differ.

### Step 2: Run tests

#### VSTest (any .NET SDK version)

```bash
dotnet test [<PROJECT> | <SOLUTION> | <DIRECTORY> | <DLL> | <EXE>]
```

Common flags:

| Flag | Description |
|------|-------------|
| `--framework <TFM>` | Target a specific framework in multi-TFM projects (e.g., `net8.0`) |
| `--no-build` | Skip build, use previously built output |
| `--filter <EXPRESSION>` | Run selected tests (see [Step 3](#step-3-run-filtered-tests)) |
| `--logger trx` | Generate TRX results file |
| `--collect "Code Coverage"` | Collect code coverage using Microsoft Code Coverage (built-in, always available) |
| `--blame` | Enable blame mode to detect tests that crash the host |
| `--blame-crash` | Collect a crash dump when the test host crashes |
| `--blame-hang-timeout <duration>` | Abort test if it hangs longer than duration (e.g., `5min`) |
| `-v <level>` | Verbosity: `quiet`, `minimal`, `normal`, `detailed`, `diagnostic` |

#### MTP with .NET SDK 8 or 9

With `<TestingPlatformDotnetTestSupport>true</TestingPlatformDotnetTestSupport>`, `dotnet test` bridges to MTP but uses VSTest-style argument parsing. MTP-specific arguments must be passed after `--`:

```bash
dotnet test [<PROJECT> | <SOLUTION> | <DIRECTORY> | <DLL> | <EXE>] -- <MTP_ARGUMENTS>
```

#### MTP with .NET SDK 10+

With the `global.json` runner set to `Microsoft.Testing.Platform`, `dotnet test` natively understands MTP arguments without `--`:

```bash
dotnet test
    [--project <PROJECT_OR_DIRECTORY>]
    [--solution <SOLUTION_OR_DIRECTORY>]
    [--test-modules <EXPRESSION>]
    [<MTP_ARGUMENTS>]
```

Examples:

```bash
# Run all tests in a project
dotnet test --project path/to/MyTests.csproj

# Run all tests in a directory containing a project
dotnet test --project path/to/

# Run all tests in a solution (sln, slnf, slnx)
dotnet test --solution path/to/MySolution.sln

# Run all tests in a directory containing a solution
dotnet test --solution path/to/

# Run with MTP flags
dotnet test --project path/to/MyTests.csproj --report-trx --blame-hang-timeout 5min
```

> **Note**: The .NET 10+ `dotnet test` syntax does **not** accept a bare positional argument like the VSTest syntax. Use `--project`, `--solution`, or `--test-modules` to specify the target.

#### Common MTP flags

These flags apply to MTP on both SDK versions. On SDK 8/9, pass after `--`; on SDK 10+, pass directly.

**Built-in flags (always available):**

| Flag | Description |
|------|-------------|
| `--no-build` | Skip build, use previously built output |
| `--framework <TFM>` | Target a specific framework in multi-TFM projects |
| `--diagnostic` | Enable diagnostic logging for the test platform |
| `--diagnostic-output-directory <DIR>` | Directory for diagnostic log output |

**Extension-dependent flags (require the corresponding extension package to be registered):**

| Flag | Requires | Description |
|------|----------|-------------|
| `--filter <EXPRESSION>` | Framework-specific (not all frameworks support this) | Run selected tests (see [Step 3](#step-3-run-filtered-tests)) |
| `--report-trx` | `Microsoft.Testing.Extensions.TrxReport` | Generate TRX results file |
| `--report-trx-filename <FILE>` | `Microsoft.Testing.Extensions.TrxReport` | Set TRX output filename |
| `--blame-hang-timeout <duration>` | `Microsoft.Testing.Extensions.HangDump` | Abort test if it hangs longer than duration (e.g., `5min`) |
| `--blame-crash` | `Microsoft.Testing.Extensions.CrashDump` | Collect a crash dump when the test host crashes |
| `--coverage` | `Microsoft.Testing.Extensions.CodeCoverage` | Collect code coverage using Microsoft Code Coverage |

> Some frameworks (e.g., MSTest) bundle common extensions by default. Others may require explicit package references. If a flag is not recognized, check that the corresponding extension package is referenced in the project.

#### Alternative MTP invocations

MTP test projects are standalone executables. Beyond `dotnet test`, they can be run directly:

```bash
# Build and run
dotnet run --project <PROJECT_PATH>

# Run a previously built DLL
dotnet exec <PATH_TO_DLL>

# Run the executable directly (Windows)
<PATH_TO_EXE>
```

These alternative invocations accept MTP command line arguments directly (no `--` separator needed).

### Step 3: Run filtered tests

The filter syntax depends on the **platform** and **test framework**.

#### VSTest filters (MSTest, xUnit, NUnit on VSTest)

```bash
dotnet test --filter <EXPRESSION>
```

Expression syntax: `<Property><Operator><Value>[|&<Expression>]`

**Operators:**

| Operator | Meaning |
|----------|---------|
| `=` | Exact match |
| `!=` | Not exact match |
| `~` | Contains |
| `!~` | Does not contain |

**Combinators:** `|` (OR), `&` (AND). Parentheses for grouping: `(A|B)&C`

**Supported properties by framework:**

| Framework | Properties |
|-----------|-----------|
| MSTest | `FullyQualifiedName`, `Name`, `ClassName`, `Priority`, `TestCategory` |
| xUnit | `FullyQualifiedName`, `DisplayName`, `Traits` |
| NUnit | `FullyQualifiedName`, `Name`, `Priority`, `TestCategory` |

An expression without an operator is treated as `FullyQualifiedName~<value>`.

**Examples (VSTest):**

```bash
# Run tests whose name contains "LoginTest"
dotnet test --filter "Name~LoginTest"

# Run a specific test class
dotnet test --filter "ClassName=MyNamespace.MyTestClass"

# Run tests in a category
dotnet test --filter "TestCategory=Integration"

# Exclude a category
dotnet test --filter "TestCategory!=Slow"

# Combine: class AND category
dotnet test --filter "ClassName=MyNamespace.MyTestClass&TestCategory=Unit"

# Either of two classes
dotnet test --filter "ClassName=MyNamespace.ClassA|ClassName=MyNamespace.ClassB"
```

#### MTP filters — MSTest and NUnit

MSTest and NUnit on MTP use the **same `--filter` syntax** as VSTest (same properties, operators, and combinators). The only difference is how the flag is passed:

```bash
# .NET SDK 8/9 (after --)
dotnet test -- --filter "Name~LoginTest"

# .NET SDK 10+ (direct)
dotnet test --filter "Name~LoginTest"
```

#### MTP filters — xUnit (v3)

xUnit v3 on MTP uses **framework-specific filter flags** instead of the generic `--filter` expression:

| Flag | Description |
|------|-------------|
| `--filter-class "name"` | Run all tests in a given class |
| `--filter-not-class "name"` | Exclude all tests in a given class |
| `--filter-method "name"` | Run a specific test method |
| `--filter-not-method "name"` | Exclude a specific test method |
| `--filter-namespace "name"` | Run all tests in a namespace |
| `--filter-not-namespace "name"` | Exclude all tests in a namespace |
| `--filter-trait "name=value"` | Run tests with a matching trait |
| `--filter-not-trait "name=value"` | Exclude tests with a matching trait |

Multiple values can be specified with a single flag: `--filter-class Foo Bar`.

```bash
# .NET SDK 8/9
dotnet test -- --filter-class "MyNamespace.LoginTests"

# .NET SDK 10+
dotnet test --filter-class "MyNamespace.LoginTests"

# Combine: namespace + trait
dotnet test --filter-namespace "MyApp.Tests.Integration" --filter-trait "Category=Smoke"
```

#### MTP filters — TUnit

TUnit uses `--treenode-filter` with a path-based syntax:

```
--treenode-filter "/<Assembly>/<Namespace>/<ClassName>/<TestName>"
```

Wildcards (`*`) are supported in any segment. Filter operators can be appended to test names for property-based filtering.

| Operator | Meaning |
|----------|---------|
| `*` | Wildcard match |
| `=` | Exact property match (e.g., `[Category=Unit]`) |
| `!=` | Exclude property value |
| `&` | AND (combine conditions) |
| `\|` | OR (within a segment, requires parentheses) |

**Examples (TUnit):**

```bash
# All tests in a class
dotnet run --treenode-filter "/*/*/LoginTests/*"

# A specific test
dotnet run --treenode-filter "/*/*/*/AcceptCookiesTest"

# By namespace prefix (wildcard)
dotnet run --treenode-filter "/*/MyProject.Tests.Api*/*/*"

# By custom property
dotnet run --treenode-filter "/*/*/*/*[Category=Smoke]"

# Exclude by property
dotnet run --treenode-filter "/*/*/*/*[Category!=Slow]"

# OR across classes
dotnet run --treenode-filter "/*/*/(LoginTests)|(SignupTests)/*"

# Combined: namespace + property
dotnet run --treenode-filter "/*/MyProject.Tests.Integration/*/*/*[Priority=Critical]"
```

## Validation

- [ ] Test platform (VSTest or MTP) was correctly identified
- [ ] Test framework (MSTest, xUnit, NUnit, TUnit) was correctly identified
- [ ] Correct `dotnet test` invocation was used for the detected platform and SDK version
- [ ] Filter expressions used the syntax appropriate for the platform and framework
- [ ] Test results were clearly reported to the user

## Common Pitfalls

| Pitfall | Solution |
|---------|----------|
| Missing `Microsoft.NET.Test.Sdk` in a VSTest project | Tests won't be discovered. Add `<PackageReference Include="Microsoft.NET.Test.Sdk" />` |
| Using VSTest `--filter` syntax with xUnit v3 on MTP | xUnit v3 on MTP uses `--filter-class`, `--filter-method`, etc. — not the VSTest expression syntax |
| Passing MTP args without `--` on .NET SDK 8/9 | Before .NET 10, MTP args must go after `--`: `dotnet test -- --report-trx` |
| Using `--` for MTP args on .NET SDK 10+ | On .NET 10+, MTP args are passed directly: `dotnet test --report-trx` (using `--` still works but is unnecessary) |
| Multi-TFM project runs tests for all frameworks | Use `--framework <TFM>` to target a specific framework |
| `global.json` runner setting ignored | Requires .NET 10+ SDK. On older SDKs, use `<TestingPlatformDotnetTestSupport>` MSBuild property instead |
| TUnit `--treenode-filter` not recognized | TUnit is MTP-only and requires `dotnet run`, not `dotnet test` with VSTest |
