---
name: mtp-hot-reload
description: >
  Suggests using Microsoft Testing Platform (MTP) hot reload to iterate fixes
  on failing tests without rebuilding. Use when user says "hot reload tests",
  "iterate on test fix", "run tests without rebuilding", "speed up test loop",
  "fix test faster", or needs to set up MTP hot reload to rapidly iterate on
  test failures. Covers setup (NuGet package, environment variable,
  launchSettings.json) and the iterative workflow for fixing tests.
  DO NOT USE FOR: writing test code, diagnosing test failures, CI/CD pipeline
  configuration, or Visual Studio Test Explorer hot reload (which is a
  different feature).
---

# MTP Hot Reload for Iterative Test Fixing

Set up and use Microsoft Testing Platform hot reload to rapidly iterate fixes on failing tests without rebuilding between each change.

## When to Use

- User has one or more failing tests and wants to iterate fixes quickly
- User wants to avoid rebuild overhead while fixing test code or production code
- User asks about hot reload for tests or speeding up the test-fix loop
- User needs to set up MTP hot reload in their project

## When Not to Use

- User needs to write new tests from scratch (use general coding assistance)
- User needs to diagnose why a test is failing (use diagnostic skills)
- User wants Visual Studio Test Explorer hot reload (different feature, built into VS)
- Project uses VSTest -- hot reload requires Microsoft Testing Platform (MTP)
- User needs CI/CD pipeline configuration

## Inputs

| Input | Required | Description |
|-------|----------|-------------|
| Test project path | No | Path to the test project (.csproj). Defaults to current directory. |
| Failing test name or filter | No | Specific test(s) to iterate on |

## Workflow

### Step 1: Verify the project uses Microsoft Testing Platform

Hot reload requires MTP. It does **not** work with VSTest.

Follow the detection procedure in the `platform-detection` skill to determine the test platform.

If the project uses VSTest, inform the user that MTP hot reload is not available and suggest migrating to MTP first (see `migrate-vstest-to-mtp`), or using Visual Studio's built-in Test Explorer hot reload feature instead.

### Step 2: Add the hot reload NuGet package

Install the `Microsoft.Testing.Extensions.HotReload` package:

```shell
dotnet add <project-path> package Microsoft.Testing.Extensions.HotReload
```

> **Note**: When using `Microsoft.Testing.Platform.MSBuild` (included transitively by MSTest, NUnit, and xUnit runners), the extension is auto-registered when you install its NuGet package -- no code changes needed.

### Step 3: Enable hot reload

Hot reload is activated by setting the `TESTINGPLATFORM_HOTRELOAD_ENABLED` environment variable to `1`.

**Option A -- Set it in the shell before running tests:**

```shell
# PowerShell
$env:TESTINGPLATFORM_HOTRELOAD_ENABLED = "1"

# bash/zsh
export TESTINGPLATFORM_HOTRELOAD_ENABLED=1
```

**Option B -- Add it to `launchSettings.json` (recommended for repeatable use):**

Create or update `Properties/launchSettings.json` in the test project:

```json
{
  "profiles": {
    "<ProjectName>": {
      "commandName": "Project",
      "environmentVariables": {
        "TESTINGPLATFORM_HOTRELOAD_ENABLED": "1"
      }
    }
  }
}
```

### Step 4: Run the tests with hot reload

Run the test project directly (not through `dotnet test`) to use hot reload in console mode:

```shell
dotnet run --project <project-path>
```

To filter to specific failing tests, pass the filter after `--`. The syntax depends on the test framework -- see the `filter-syntax` skill for full details. Quick examples:

| Framework | Filter syntax |
|-----------|--------------|
| MSTest | `dotnet run --project <path> -- --filter "FullyQualifiedName~TestMethodName"` |
| NUnit | `dotnet run --project <path> -- --filter "FullyQualifiedName~TestMethodName"` |
| xUnit v3 | `dotnet run --project <path> -- --filter-method "*TestMethodName"` |
| TUnit | `dotnet run --project <path> -- --treenode-filter "/*/*/ClassName/TestMethodName"` |

The test host will start, run the tests, and **remain running** waiting for code changes.

### Step 5: Iterate on the fix

1. Edit the source code (test code or production code) in your editor
2. The test host detects the changes and re-runs the affected tests automatically
3. Review the updated results in the console
4. Repeat until all targeted tests pass

> **Important**: Hot reload currently works in **console mode only**. There is no support for hot reload in Test Explorer for Visual Studio or Visual Studio Code.

### Step 6: Finalize

Once all tests pass:

1. Stop the test host (Ctrl+C)
2. Run a full `dotnet test` to confirm all tests pass with a clean build
3. Optionally remove `TESTINGPLATFORM_HOTRELOAD_ENABLED` from the environment or keep `launchSettings.json` for future use

## Validation

- [ ] Project uses Microsoft Testing Platform (not VSTest)
- [ ] `Microsoft.Testing.Extensions.HotReload` package is installed
- [ ] `TESTINGPLATFORM_HOTRELOAD_ENABLED` environment variable is set to `1`
- [ ] Tests run and the host remains active waiting for changes
- [ ] Code changes are picked up without manual restart

## Common Pitfalls

| Pitfall | Solution |
|---------|----------|
| Using `dotnet test` instead of `dotnet run` | Hot reload requires `dotnet run --project <path>` to run the test host directly in console mode |
| Project uses VSTest, not MTP | Hot reload requires MTP. Migrate to MTP first or use VS Test Explorer hot reload |
| Forgetting to set the environment variable | Set `TESTINGPLATFORM_HOTRELOAD_ENABLED=1` before running |
| Expecting Test Explorer integration | Console mode only -- no VS/VS Code Test Explorer support |
| Making unsupported code changes (rude edits) | Some changes (adding new types, changing method signatures) require a restart. Stop and re-run |
