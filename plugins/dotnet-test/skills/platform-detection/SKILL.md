---
name: platform-detection
description: "Reference data for detecting the test platform (VSTest vs Microsoft.Testing.Platform) and test framework (MSTest, xUnit, NUnit, TUnit) from project files. DO NOT USE directly — loaded by run-tests, mtp-hot-reload, and migrate-vstest-to-mtp when they need detection logic."
user-invocable: false
---

# Test Platform and Framework Detection

Determine **which test platform** (VSTest or Microsoft.Testing.Platform) and **which test framework** (MSTest, xUnit, NUnit, TUnit) a project uses.

**Detection files to always check** (in order): `global.json` → `.csproj` → `Directory.Build.props` → `Directory.Packages.props`

## Detecting the test framework

Read the `.csproj` file **and** `Directory.Build.props` / `Directory.Packages.props` (for centrally managed dependencies) and look for:

| Package or SDK reference | Framework |
|--------------------------|-----------|
| `MSTest` (metapackage, recommended) or `<Sdk Name="MSTest.Sdk">` | MSTest |
| `MSTest.TestFramework` + `MSTest.TestAdapter` | MSTest (also valid for v3/v4) |
| `xunit`, `xunit.v3`, `xunit.v3.mtp-v1`, `xunit.v3.mtp-v2`, `xunit.v3.core.mtp-v1`, `xunit.v3.core.mtp-v2` | xUnit |
| `NUnit` + `NUnit3TestAdapter` | NUnit |
| `TUnit` | TUnit (MTP only) |

## Detecting the test platform

The detection logic depends on the .NET SDK version. Run `dotnet --version` to determine it.

### .NET SDK 10+

On .NET 10+, the `global.json` `test.runner` setting is the **authoritative source**:

- If `global.json` contains `"test": { "runner": "Microsoft.Testing.Platform" }` → **MTP**
- If `global.json` has `"runner": "VSTest"`, or no `test` section exists → **VSTest**

> **Important**: On .NET 10+, `<TestingPlatformDotnetTestSupport>` alone does **not** switch to MTP. The `global.json` runner setting takes precedence. If the runner is VSTest (or unset), the project uses VSTest regardless of `TestingPlatformDotnetTestSupport`.

### .NET SDK 8 or 9

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
