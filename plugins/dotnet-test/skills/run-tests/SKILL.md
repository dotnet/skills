---
name: run-tests
description: >
  Run or recommend the exact .NET test command. ALWAYS USE when asked to run,
  filter, or troubleshoot .NET tests or provide precise flags/argument order.
  Supports SDK-style dotnet test and classic non-SDK projects using MSBuild plus
  vstest.console/MSTest or repository scripts. USE FOR: all tests or subsets by
  class/category/trait; multi-TFM --framework; TRX reports; crash/hang dumps;
  VSTest vs Microsoft.Testing.Platform; bridged vs native MTP argument syntax;
  --filter, --filter-class, --filter-trait, --filter-query, and
  --treenode-filter. Detects MSTest/xUnit/NUnit/TUnit and packages.config/classic
  project constraints. DO NOT USE FOR: writing tests (use code-testing-agent),
  MTP hot-reload iteration, CI/CD configuration, or debugging test logic.
license: MIT
---

# Run .NET Tests

Detect the project system, test platform, and framework, then use the
repository-compatible build and test runner.

## When to Use

- User wants to run tests in a .NET project
- User needs to run a subset of tests using filters
- User needs help detecting which test platform (VSTest vs MTP) or framework is in use
- User wants to understand the correct filter syntax for their setup

## When Not to Use

- User needs to write or generate test code (use `code-testing-agent`; use
  `writing-mstest-tests` for a specifically MSTest API/pattern request)
- User needs to migrate from VSTest to MTP (use `migrate-vstest-to-mtp`)
- User wants to iterate on failing tests without rebuilding (use `mtp-hot-reload`)
- User needs CI/CD pipeline configuration (use CI-specific skills)
- User needs to debug a test (use debugging skills)

## Inputs

| Input | Required | Description |
|-------|----------|-------------|
| Project or solution path | No | Path to the test project (.csproj) or solution (.sln, .slnf, .slnx). Defaults to current directory. |
| Filter expression | No | Filter expression to select specific tests |
| Target framework | No | Target framework moniker to run against (e.g., `net8.0`) |

## Critical Rules — Avoid Cross-Platform Mistakes

These are the most common agent mistakes. Internalize before proceeding:

| Rule | Why |
|------|-----|
| **Do NOT assume `dotnet test` for classic non-SDK projects** | `ToolsVersion`, explicit compile items, and `packages.config` often require full MSBuild plus VSTest/MSTest or a repository script |
| **Do NOT use `--logger trx`** for MTP projects | MTP uses `--report-trx` (requires the TrxReport extension package) |
| **Do NOT use `--report-trx`** for VSTest projects | VSTest uses `--logger trx` |
| **Do NOT choose argument syntax from SDK version alone** | On SDK 10+, only native MTP mode passes MTP args directly; VSTest mode bridging to MTP still uses `--` |
| **Do NOT omit `--` in VSTest mode with MTP** | SDK 8/9, and SDK 10 with runner VSTest/unset, require `dotnet test -- --report-trx` when the bridge is enabled |
| **Do NOT use `--filter "ClassName=..."`** with xUnit v3 on MTP | xUnit v3 on MTP uses `--filter-class`, `--filter-method`, `--filter-trait` |
| **Do NOT use bare positional path in native MTP mode** | Use `--project <path>` or `--solution <path>`; VSTest mode retains positional paths |
| **Do NOT use `--blame`** for MTP projects | MTP uses `--blame-crash` and `--blame-hang-timeout` separately (each requires its extension package) |
| **Do NOT use `--collect "Code Coverage"`** for MTP | MTP uses `--coverage` (requires the CodeCoverage extension package) |

## Workflow

### Quick Reference

| `dotnet test` mode / platform | SDK | Command pattern |
|----------|-----|----------------|
| Classic non-SDK / VSTest or MSTest | n/a | Repository script, or MSBuild followed by `vstest.console.exe` / `MSTest.exe` |
| VSTest mode / VSTest | Any | `dotnet test [<path>] [--filter <expr>] [--logger trx]` |
| VSTest mode / MTP bridge | 8+ | `dotnet test [<path>] -- <MTP_ARGS>` |
| Native MTP mode | 10+ | `dotnet test --project <path> <MTP_ARGS>` |

**Detection files to always check** (in order): `global.json` -> `.csproj` ->
`packages.config` -> `Directory.Build.props` -> `Directory.Packages.props` ->
repository scripts/CI documentation

**If the prompt names a subset of tests** (e.g., "integration tests", "smoke tests", a specific class, a specific TFM), plan to apply the matching filter / `--framework` in [Step 3](#step-3-run-filtered-tests) — do not run the whole suite.

### Step 1: Detect the test platform and framework

1. Classify SDK-style vs. classic non-SDK using `platform-detection`.
2. For classic projects, inspect `packages.config`, assembly references, scripts,
   CI, `README*`, and `AGENTS.md`; use their MSBuild/test-runner command. Do not
   migrate or add modern package references.
3. For SDK-style projects, run `dotnet --version` in the project directory.
4. Read `global.json` to determine `dotnet test` mode on SDK 10+.
5. Read `.csproj`, `Directory.Build.props`, and `Directory.Packages.props` to
   determine whether VSTest mode executes VSTest or bridges to MTP.

### Classic non-SDK projects

The checked-in command is authoritative. A common VSTest sequence is:

```powershell
MSBuild.exe MySolution.sln /t:Build /p:Configuration=Debug
vstest.console.exe path\to\MyTests.dll /Logger:trx
```

Filtering uses the runner's syntax, for example
`/TestCaseFilter:"TestCategory=Integration"` with VSTest. Older repositories may
use `MSTest.exe` or a wrapper script instead. If the required Visual Studio
toolchain is unavailable, report the exact missing prerequisite and the
documented command; do not claim success from `dotnet test`.

**What to look for in each file:**

| File | Look for | Indicates |
|------|----------|-----------|
| `global.json` | `"test": { "runner": "Microsoft.Testing.Platform" }` | Native MTP mode on SDK 10+ |
| `global.json` | `"sdk": { "version": "..." }` | SDK version (determines `--` separator behavior) |
| `.csproj` | MTP runner enabled + `<TestingPlatformDotnetTestSupport>true` | VSTest mode redirects the MTP application to MTP |
| `.csproj` | `MSTest`, `xunit.v3`, `NUnit`, `TUnit` packages | Framework identity |
| `.csproj` | `Microsoft.NET.Test.Sdk` + test adapter | VSTest (unless overridden by MTP signals above) |
| `.csproj` | `<TargetFrameworks>` (plural) | Multi-TFM — may need `--framework` |
| `Directory.Build.props` | MTP runner enabled + `<TestingPlatformDotnetTestSupport>true` | VSTest mode redirects the MTP application to MTP |
| `Directory.Packages.props` | Centrally managed test package versions | Framework identity for CPM repos |

**Quick detection summary:**

| Signal | Means |
|--------|-------|
| SDK 10+ global runner is `Microsoft.Testing.Platform` | **Native MTP mode** — pass args directly |
| MTP-capable project + VSTest mode + `TestingPlatformDotnetTestSupport=true` | **MTP bridge** — pass MTP args after `--` |
| VSTest mode without the bridge | **VSTest** |

### Step 2: Run tests

#### SDK-style VSTest (any .NET SDK version)

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

#### MTP through VSTest mode

With `<TestingPlatformDotnetTestSupport>true</TestingPlatformDotnetTestSupport>`,
`dotnet test` bridges to MTP but uses VSTest-style argument parsing. This applies
to SDK 8/9 and SDK 10+ when global runner is VSTest or unset. MTP-specific
arguments must be passed after `--`:

```bash
dotnet test [<PROJECT> | <SOLUTION> | <DIRECTORY> | <DLL> | <EXE>] -- <MTP_ARGUMENTS>
```

#### Native MTP mode with .NET SDK 10+

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
dotnet test --solution path/to/MySolution.slnf
dotnet test --solution path/to/MySolution.slnx

# Run all tests in a directory containing a solution
dotnet test --solution path/to/

# Run with MTP flags
dotnet test --project path/to/MyTests.csproj --report-trx --blame-hang-timeout 5min
```

> **Note**: Native MTP mode does **not** accept a bare positional argument like
> VSTest mode. Use `--project`, `--solution`, or `--test-modules`.

#### Common MTP flags

These flags apply to MTP in both modes. Pass them after `--` in VSTest mode and
directly in native MTP mode.

> **Important:** `dotnet test`/MSBuild flags such as `--framework`, `--no-build`,
> `--configuration`, and `--verbosity` always go before `--`. Only MTP
> application arguments go after `--` in VSTest mode. For example:
> `dotnet test --framework net9.0 -- --report-trx`.

**Built-in flags (always available):**

| Flag | Description |
|------|-------------|
| `--results-directory <DIR>` | Directory for test result output |
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

See the `filter-syntax` skill for the complete filter syntax for each platform and framework combination. Key points:

- **VSTest** (MSTest, xUnit v2, NUnit): `dotnet test --filter <EXPRESSION>` with `=`, `!=`, `~`, `!~` operators
- **MTP -- MSTest and NUnit**: Same `--filter` syntax as VSTest; pass after `--` in VSTest mode and directly in native MTP mode.
- **MTP -- xUnit v3**: Uses `--filter-class`, `--filter-method`, `--filter-trait` (not VSTest expression syntax). For a **single combined expression** (e.g., a class-name pattern AND a trait), use `--filter-query` with the xUnit v3 query filter language: path segments `/<assembly>/<namespace>/<class>/<method>` with `*` wildcards and a `[Trait=Value]` qualifier — for example `dotnet test -- --filter-query "/*/*/*IntegrationTests*/*[Category=Smoke]"`. See the `filter-syntax` skill for the full query language.
- **MTP -- TUnit**: Uses `--treenode-filter` with path-based syntax

#### When the user names a test category, trait, or group

When the prompt names a subset of tests by category (e.g., "integration tests", "unit tests", "smoke tests", "fast tests"), **do not run all tests** — translate the user's vocabulary into the platform-appropriate filter:

1. **Inspect the test source files** for filter-attribute annotations that match the named group:

   | Framework | Attribute | Filter property |
   |-----------|-----------|-----------------|
   | MSTest | `[TestCategory("Integration")]` | `TestCategory` |
   | NUnit | `[Category("Integration")]` | `TestCategory` (mapped) |
   | xUnit v2 | `[Trait("Category", "Integration")]` | `Category` |
   | xUnit v3 | `[Trait("Category", "Integration")]` | `Category` (use `--filter-trait`) |
   | TUnit | `[Category("Integration")]` | `Category` |

2. **Build the filter expression** and combine it with the platform-correct invocation. For "run the integration tests" against an MSTest project:

   | Mode / platform | Framework | Command |
   |-----------------|-----------|---------|
   | VSTest mode / VSTest | MSTest | `dotnet test --filter "TestCategory=Integration"` |
   | VSTest mode / MTP bridge | MSTest | `dotnet test -- --filter "TestCategory=Integration"` |
   | Native MTP mode | MSTest | `dotnet test --filter "TestCategory=Integration"` |
   | VSTest mode / MTP bridge | xUnit v3 | `dotnet test -- --filter-trait "Category=Integration"` |
   | Native MTP mode | xUnit v3 | `dotnet test --filter-trait "Category=Integration"` |
   | VSTest mode / MTP bridge | TUnit | `dotnet test -- --treenode-filter "/*/*/*/*[Category=Integration]"` |
   | Native MTP mode | TUnit | `dotnet test --treenode-filter "/*/*/*/*[Category=Integration]"` |

3. If you cannot find a matching attribute, ask the user to confirm the category name or fall back to a name-pattern filter (e.g., `--filter "FullyQualifiedName~Integration"`).

## Validation

- [ ] Test platform (VSTest or MTP) was correctly identified
- [ ] Project system (SDK-style or classic non-SDK) was correctly identified
- [ ] Test framework (MSTest, xUnit, NUnit, TUnit) was correctly identified
- [ ] Correct repository-compatible runner was used; `dotnet test` syntax was validated only for SDK-style projects
- [ ] When the user named a test category/trait/group, the appropriate filter was applied (not "run all tests")
- [ ] Filter expressions used the syntax appropriate for the platform and framework
- [ ] Test results were clearly reported to the user

## Common Pitfalls

| Pitfall | Solution |
|---------|----------|
| Running `dotnet test` on a classic `packages.config` project | Use its documented MSBuild and VSTest/MSTest command; do not modernize implicitly |
| Missing `Microsoft.NET.Test.Sdk` in an SDK-style VSTest project | Tests won't be discovered. Add the SDK-style package reference. For classic projects, preserve `packages.config` and use the installed adapter/test runner instead |
| Using VSTest `--filter` syntax with xUnit v3 on MTP | xUnit v3 on MTP uses `--filter-class`, `--filter-method`, etc. -- not the VSTest expression syntax |
| Passing MTP args without `--` on .NET SDK 8/9 | Before .NET 10, MTP args must go after `--`: `dotnet test -- --report-trx` |
| Assuming every SDK 10 invocation is native MTP mode | Read global.json; SDK 10 VSTest mode still uses the bridge and `--` for MTP arguments |
| Using `--logger trx` for MTP or `--report-trx` for VSTest | Each platform has its own TRX flag — check the Critical Rules table |
| Only checking `.csproj` for MTP signals | Always check `Directory.Build.props` and `Directory.Packages.props` too — MTP properties are frequently set there |
| Using bare positional path in native MTP mode | Use `--project <path>` or `--solution <path>` |

## Troubleshooting

Common error messages and how to resolve them:

| Error | Cause | Fix |
|-------|-------|-----|
| `No test is available` or `No test matches the given testcase filter` | Wrong filter syntax for the platform/framework, or tests not discovered | Verify filter syntax matches the platform (see `filter-syntax` skill). For discovery issues, check that the test SDK and adapter packages are installed |
| `The --report-trx option is unrecognized` | MTP extension package not referenced, or using MTP flag on a VSTest project | Add `<PackageReference Include="Microsoft.Testing.Extensions.TrxReport" />` for MTP, or use `--logger trx` for VSTest |
| `The --blame-hang-timeout option is unrecognized` | Missing HangDump extension on MTP | Add `<PackageReference Include="Microsoft.Testing.Extensions.HangDump" />` |
| `error NETSDK1045: The current .NET SDK does not support targeting .NET X.0` | SDK version in `global.json` doesn't match the project's target framework | Update `global.json` SDK version or install the required SDK |
| `The test runner process exited with non-zero exit code` | MTP test host crashed or test failure | Run with `--blame-crash` (MTP) or `--blame` (VSTest) to collect a crash dump for diagnosis |
| `No test source files were found` / `No test project found` | `dotnet test` can't find a test project in the given path | Specify `dotnet test <project.csproj>` in VSTest mode or `dotnet test --project <path>` in native MTP mode |
| Tests discovered but 0 executed | Filter expression matches no tests | Double-check filter property names and values. Common typo: `TestCategory` (MSTest) vs `Category` (NUnit) vs trait syntax (xUnit) |
| Using native MTP argument syntax while global.json selects VSTest mode | Use the bridge syntax with `--`; pass arguments directly only in native MTP mode |
| Multi-TFM project runs tests for all frameworks | Use `--framework <TFM>` to target a specific framework |
| `global.json` runner setting ignored | Requires .NET 10+ SDK. On older SDKs, use `<TestingPlatformDotnetTestSupport>` MSBuild property instead |
| TUnit `--treenode-filter` not recognized | TUnit is MTP-only. Use native MTP mode, a configured bridge, or run the test executable directly |
