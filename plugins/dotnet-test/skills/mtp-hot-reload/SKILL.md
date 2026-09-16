---
name: mtp-hot-reload
description: >
  Set up or recover MTP hot reload for a long-lived console-host edit/re-run
  loop in a Microsoft Testing Platform project. Use for explicit MTP console
  requests such as "enable hot reload", "dotnet run to rerun on edit", or
  unsupported/rude edits. Covers setup, run/watch, restarts, filters, and the
  VSTest no-mutation fallback. For one-time runs, exact commands, filter
  errors, TRX/dumps, or merely a failing test, use run-tests. Do not use when
  Test Explorer or an IDE should rerun tests without an MTP console host.
  Excludes editor integration, CI, and writing/debugging tests.
license: MIT
---

# MTP Hot Reload for Iterative Test Fixing

Set up and use a long-lived Microsoft Testing Platform host that applies code
edits and automatically reruns tests.

## When to Use

- User explicitly asks for test hot reload
- User wants a host to stay running and automatically rerun after repeated edits
- User needs to set up MTP hot reload in their project

## When Not to Use

- User needs to write new tests from scratch (use general coding assistance)
- User needs to diagnose why a test is failing (use diagnostic skills)
- User asks about Visual Studio or VS Code Test Explorer hot reload or an IDE-integrated rerun experience (different feature, not MTP console-host hot reload)
- User asks whether the editor can auto-rerun affected tests after source edits without using an MTP console host
- User wants one normal run without rebuilding (use `run-tests`)
- User needs CI/CD pipeline configuration

## Inputs

| Input | Required | Description |
|-------|----------|-------------|
| Test project path | No | Path to the test project (.csproj). Defaults to current directory. |
| Failing test name or filter | No | Specific test(s) to iterate on |

## Response sizing

- If setup is already complete and the user asks only which command to use,
  return one `dotnet run --project <path>` command and one sentence explaining
  that it starts the persistent host. Do not repeat package, launch profile, or
  rude-edit guidance.
- If the package is already installed, show only the remaining enable-and-run
  steps. Do not suggest reinstalling it or add optional persistence/recovery
  paths unless requested.
- For a named test, identify the framework and return one runnable command with
  that framework's filter syntax. Never substitute MSTest/NUnit `--filter` for
  xUnit v3 `--filter-method` or TUnit `--treenode-filter`.
- A directly launched MTP host is itself the persistent watcher: `dotnet run`
  stays alive and the HotReload extension reacts to edits. Do not replace it
  with `dotnet watch run` for the normal MTP path; reserve `dotnet watch` for the
  explicit restart fallback below.

## Workflow

### Step 1: Detect the platform before changing anything

Hot reload requires MTP. It does **not** work with VSTest.

Follow the complete evaluated-property procedure in the `platform-detection`
skill. Read imported props and package versions as well as the project file.
Do this before installing packages, editing files, or returning an MTP launch
command.

**Hard stop for VSTest:** report that MTP hot reload is unavailable for the
project as configured and stop the MTP setup path. Do not install the extension,
create `launchSettings.json`, set the environment variable, change runner
properties/packages, or return a `dotnet run` hot-reload command. Never turn a
setup request into an implicit VSTest-to-MTP migration.

Offer one valid non-MTP fallback that preserves the project:

```shell
dotnet watch --project <project-path> test
```

This rebuilds and reruns the existing VSTest project when files change; it is
not MTP hot reload. Offer an explicit migration as a separate option, but do not
perform it unless the user asks. Exact one-shot test commands remain owned by
`run-tests`.

### Step 2: Add the hot reload NuGet package

First inspect the effective package references. If
`Microsoft.Testing.Extensions.HotReload` is already installed, preserve its
version and skip this step. Otherwise install it:

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

#### Unsupported edits and rude edits

Method-signature changes, new types, and other unsupported edits cannot be
applied to the active process. Never imply that the stale host picked them up.

For a directly launched MTP host:

1. Preserve the exact command, profile, environment, filter, and arguments that
   started the current host.
2. Stop it with `Ctrl+C`.
3. Rebuild the same project: `dotnet build <project-path>`.
4. Rerun the **same original host command**. Do not replace an unknown existing
   invocation with a generic `dotnet run` command.

If repeated unsupported edits are expected, offer a watch-managed restart
fallback:

```shell
# PowerShell
$env:TESTINGPLATFORM_HOTRELOAD_ENABLED = "1"
$env:DOTNET_WATCH_RESTART_ON_RUDE_EDIT = "1"
dotnet watch --project <project-path> run -- <existing-MTP-arguments>
```

`dotnet watch` restarts the process when a rude edit cannot be applied. Without
the auto-restart variable, accept the restart prompt or press `Ctrl+R`. Preserve
any existing test filter after `--`.
`DOTNET_WATCH_RESTART_ON_RUDE_EDIT` is a documented .NET SDK switch, not an MTP
extension setting. When correctness may be questioned, cite the `dotnet watch`
documentation and say that it makes the watcher restart instead of prompting.
Never omit the original filter or replace the requested watch-managed fallback
with a one-shot `dotnet run`.

### Step 6: Finalize

Once all tests pass:

1. Stop the test host (Ctrl+C)
2. Use `run-tests` when the user requests an exact one-shot validation command,
   flags, filter, TRX, or dump
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
| Project uses VSTest, not MTP | Do not mutate it. Offer `dotnet watch --project <path> test` as a rebuild/rerun fallback or a separate explicit migration |
| Forgetting to set the environment variable | Set `TESTINGPLATFORM_HOTRELOAD_ENABLED=1` before running |
| Expecting Test Explorer integration | Console mode only -- no VS/VS Code Test Explorer support |
| Making unsupported code changes (rude edits) | Stop, rebuild, and rerun the same host invocation, or use `dotnet watch` with restart-on-rude-edit behavior |
