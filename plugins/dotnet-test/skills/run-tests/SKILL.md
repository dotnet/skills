---
name: run-tests
description: >
  Runs .NET tests with dotnet test. Use when user says "run tests", "run my
  tests", "run these tests", "execute tests", "dotnet test", "test filter",
  "filter by category", "filter by class", "combine filters",
  "run only specific tests", "integration tests", "unit tests",
  "tests not running", "hang timeout", "blame-hang", "blame-crash",
  "crash dump", "TRX report", "TRX", "test report", "generate TRX",
  "TUnit", "treenode-filter", "target framework", "multi-TFM", or needs
  to detect the test platform (VSTest or Microsoft.Testing.Platform),
  identify the test framework, apply test filters, or troubleshoot test
  execution failures. Covers MSTest, xUnit, NUnit, and TUnit across both
  VSTest and MTP platforms. Also use for --filter-class, --filter-trait,
  --report-trx, --logger trx, --blame-hang-timeout, and other
  platform-specific filter and reporting syntax.
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

- User needs to write or generate test code (use `writing-mstest-tests` for MSTest, or general coding assistance for other frameworks)
- User needs to migrate from VSTest to MTP (use `migrate-vstest-to-mtp`)
- User wants to iterate on failing tests without rebuilding (use `mtp-hot-reload`)
- User needs CI/CD pipeline configuration (use CI-specific skills)
- User needs to debug a test (use debugging skills)

## Inputs

| Input | Required | Description |
|-------|----------|-------------|
| Project or solution path | No | Path to the test project (.csproj) or solution (.sln). Defaults to current directory. |
| Filter expression | No | Filter expression to select specific tests |
| Target framework | No | Target framework moniker to run against (e.g., `net8.0`) |

## Critical Rules — Avoid Cross-Platform Mistakes

These are the most common agent mistakes. Internalize before proceeding:

| Rule | Why |
|------|-----|
| **Do NOT use `--logger trx`** for MTP projects | MTP uses `--report-trx` (requires the TrxReport extension package) |
| **Do NOT use `--report-trx`** for VSTest projects | VSTest uses `--logger trx` |
| **Do NOT use `-- --arg`** on .NET SDK 10+ | SDK 10+ passes MTP args directly: `dotnet test --project . --report-trx` |
| **Do NOT omit `--`** on .NET SDK 8/9 with MTP | SDK 8/9 requires the separator: `dotnet test -- --report-trx` |
| **Do NOT use `--filter "ClassName=..."`** with xUnit v3 on MTP | xUnit v3 on MTP uses `--filter-class`, `--filter-method`, `--filter-trait` |
| **Do NOT use bare positional path** on SDK 10+ | Use `--project <path>` or `--solution <path>` instead |
| **Do NOT use `--blame`** for MTP projects | MTP uses `--blame-crash` and `--blame-hang-timeout` separately (each requires its extension package) |
| **Do NOT use `--collect "Code Coverage"`** for MTP | MTP uses `--coverage` (requires the CodeCoverage extension package) |

## Workflow

### Quick Reference

| Platform | SDK | Command pattern |
|----------|-----|----------------|
| VSTest | Any | `dotnet test [<path>] [--filter <expr>] [--logger trx]` |
| MTP | 8 or 9 | `dotnet test [<path>] -- <MTP_ARGS>` |
| MTP | 10+ | `dotnet test --project <path> <MTP_ARGS>` |

**Detection files to always check** (in order): `global.json` -> `.csproj` -> `Directory.Build.props` -> `Directory.Packages.props`

### Step 1: Detect the test platform and framework

1. Run `dotnet --version` in the project directory to determine the SDK version. This accounts for `global.json` SDK pinning.
2. Read `global.json` — on .NET SDK 10+, `"test": { "runner": "Microsoft.Testing.Platform" }` is the **authoritative MTP signal**. If present, the project uses MTP and SDK 10+ syntax (no `--` separator).
3. Read `.csproj`, `Directory.Build.props`, **and** `Directory.Packages.props` for framework packages and MTP properties. **Always check all three files** — MTP properties are frequently set in `Directory.Build.props` rather than individual `.csproj` files.
4. For full detection logic (SDK 8/9 signals, framework identification), see the `platform-detection` skill.

**What to look for in each file:**

| File | Look for | Indicates |
|------|----------|-----------|
| `global.json` | `"test": { "runner": "Microsoft.Testing.Platform" }` | MTP on SDK 10+ |
| `global.json` | `"sdk": { "version": "..." }` | SDK version (determines `--` separator behavior) |
| `.csproj` | `<TestingPlatformDotnetTestSupport>true` | MTP on SDK 8/9 |
| `.csproj` | `MSTest`, `xunit.v3`, `NUnit`, `TUnit` packages | Framework identity |
| `.csproj` | `Microsoft.NET.Test.Sdk` + test adapter | VSTest (unless overridden by MTP signals above) |
| `.csproj` | `<TargetFrameworks>` (plural) | Multi-TFM — may need `--framework` |
| `Directory.Build.props` | `<TestingPlatformDotnetTestSupport>true` | MTP on SDK 8/9 (often set here, not in .csproj) |
| `Directory.Packages.props` | Centrally managed test package versions | Framework identity for CPM repos |

**Quick detection summary:**

| Signal | Means |
|--------|-------|
| `global.json` has `"test": { "runner": "Microsoft.Testing.Platform" }` | **MTP on SDK 10+** — pass args directly, no `--` |
| `<TestingPlatformDotnetTestSupport>true` in csproj or Directory.Build.props | **MTP on SDK 8/9** — pass args after `--` |
| Neither signal present | **VSTest** |

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
- **MTP -- MSTest and NUnit**: Same `--filter` syntax as VSTest; pass after `--` on SDK 8/9, directly on SDK 10+
- **MTP -- xUnit v3**: Uses `--filter-class`, `--filter-method`, `--filter-trait` (not VSTest expression syntax)
- **MTP -- TUnit**: Uses `--treenode-filter` with path-based syntax

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
| Using VSTest `--filter` syntax with xUnit v3 on MTP | xUnit v3 on MTP uses `--filter-class`, `--filter-method`, etc. -- not the VSTest expression syntax |
| Passing MTP args without `--` on .NET SDK 8/9 | Before .NET 10, MTP args must go after `--`: `dotnet test -- --report-trx` |
| Using `-- --arg` separator on .NET SDK 10+ | SDK 10+ passes MTP args directly — do NOT use `--` separator |
| Using `--logger trx` for MTP or `--report-trx` for VSTest | Each platform has its own TRX flag — check the Critical Rules table |
| Only checking `.csproj` for MTP signals | Always check `Directory.Build.props` and `Directory.Packages.props` too — MTP properties are frequently set there |
| Using bare positional path argument on SDK 10+ | SDK 10+ requires named flags: `--project <path>` or `--solution <path>` |

## Troubleshooting

Common error messages and how to resolve them:

| Error | Cause | Fix |
|-------|-------|-----|
| `No test is available` or `No test matches the given testcase filter` | Wrong filter syntax for the platform/framework, or tests not discovered | Verify filter syntax matches the platform (see `filter-syntax` skill). For discovery issues, check that the test SDK and adapter packages are installed |
| `The --report-trx option is unrecognized` | MTP extension package not referenced, or using MTP flag on a VSTest project | Add `<PackageReference Include="Microsoft.Testing.Extensions.TrxReport" />` for MTP, or use `--logger trx` for VSTest |
| `The --blame-hang-timeout option is unrecognized` | Missing HangDump extension on MTP | Add `<PackageReference Include="Microsoft.Testing.Extensions.HangDump" />` |
| `error NETSDK1045: The current .NET SDK does not support targeting .NET X.0` | SDK version in `global.json` doesn't match the project's target framework | Update `global.json` SDK version or install the required SDK |
| `The test runner process exited with non-zero exit code` | MTP test host crashed or test failure | Run with `--blame-crash` (MTP) or `--blame` (VSTest) to collect a crash dump for diagnosis |
| `No test source files were found` / `No test project found` | `dotnet test` can't find a test project in the given path | Specify the path explicitly: `dotnet test <path/to/project.csproj>` (VSTest) or `dotnet test --project <path>` (SDK 10+) |
| Tests discovered but 0 executed | Filter expression matches no tests | Double-check filter property names and values. Common typo: `TestCategory` (MSTest) vs `Category` (NUnit) vs trait syntax (xUnit) |
| Using `--` for MTP args on .NET SDK 10+ | On .NET 10+, MTP args are passed directly: `dotnet test --project . --blame-hang-timeout 5min` — do NOT use `-- --blame-hang-timeout` |
| Multi-TFM project runs tests for all frameworks | Use `--framework <TFM>` to target a specific framework |
| `global.json` runner setting ignored | Requires .NET 10+ SDK. On older SDKs, use `<TestingPlatformDotnetTestSupport>` MSBuild property instead |
| TUnit `--treenode-filter` not recognized | TUnit is MTP-only. On .NET SDK 10+ use `dotnet test`; on older SDKs use `dotnet run` since VSTest-mode `dotnet test` does not support TUnit |
