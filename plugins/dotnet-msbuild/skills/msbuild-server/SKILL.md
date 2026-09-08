---
name: msbuild-server
description: "Use MSBuild Server for repeated command-line builds and diagnose stale output after enabling it. INVOKE for slow `dotnet build` incremental loops, CLI builds slower than IDE builds, persistent server caching, DOTNET_CLI_USE_MSBUILD_SERVER, and build-server shutdown troubleshooting. NEVER INVOKE for Visual Studio or other IDE-only build slowness: the IDE already keeps a long-lived MSBuild process. For one build that exits, use the skill only to explain why the server has no amortized benefit."
license: MIT
---

# MSBuild Server for CLI Caching

Use the MSBuild Server to cache evaluation results across CLI builds, matching the performance advantage Visual Studio gets from its long-lived MSBuild process.

## When to Use

- Small incremental builds from CLI (`dotnet build`) are slower than expected
- Developers notice that VS builds are faster than CLI builds for the same project
- CI agents run many sequential builds of the same repo

## When Not to Use

- IDE-based builds (Visual Studio already uses a long-lived MSBuild process)
- One-off builds where cold-start overhead is acceptable
- Build correctness issues are suspected (disable the server to isolate the problem)

## Inputs

| Input | Required | Description |
|-------|----------|-------------|
| Shell context | No | The shell where the environment variable will be set (bash, PowerShell, or Windows persistent) |

## Workflow

### Step 1: Confirm CLI context

Verify the developer is building from the command line (`dotnet build`), not from Visual Studio or another IDE. The MSBuild Server provides no benefit inside an IDE.

### Step 2: Set the environment variable

First check the active SDK. .NET 11 and later enable MSBuild Server by default.
For earlier SDKs, or when explicit enablement is required, set the .NET CLI
variable below. The CLI derives the internal MSBuild setting from this value.

```bash
# Bash / CI
export DOTNET_CLI_USE_MSBUILD_SERVER=true

# PowerShell
$env:DOTNET_CLI_USE_MSBUILD_SERVER = "true"

# Windows (persistent)
setx DOTNET_CLI_USE_MSBUILD_SERVER true
```

### Step 3: Validate improvement

Run two sequential builds of the same project and compare times:

1. First build (cold): `dotnet build` -- server starts, no cache benefit
2. Second build (warm): `dotnet build` -- should be noticeably faster

The most noticeable improvement is in repos with many projects or complex `Directory.Build.props` chains.

## Validation

- [ ] `DOTNET_CLI_USE_MSBUILD_SERVER=true` is set in the shell
- [ ] Second sequential build is faster than the first
- [ ] `dotnet build-server shutdown` followed by a rebuild confirms the server restarts cleanly

## Common Pitfalls

| Pitfall | Solution |
|---------|----------|
| Expecting improvement in Visual Studio | VS already uses long-lived MSBuild nodes; the server adds no benefit |
| Build correctness issues after enabling | Run `dotnet build-server shutdown` to reset; if issues persist, disable the server |
| Server process using unexpected memory | The server persists in background; shut down with `dotnet build-server shutdown` when idle |
