---
name: msbuild-server
description: "Guide for using MSBuild Server to improve CLI build performance. Only activate in MSBuild/.NET build context. Activate when developers report slow incremental builds from the command line, or when CLI builds are noticeably slower than IDE builds. Covers MSBUILDUSESERVER=1 environment variable for persistent server-based caching. Do not activate for IDE-based builds (Visual Studio already uses a long-lived process)."
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

```bash
# Bash / CI
export MSBUILDUSESERVER=1

# PowerShell
$env:MSBUILDUSESERVER = "1"

# Windows (persistent)
setx MSBUILDUSESERVER 1
```

### Step 3: Validate improvement

Run two sequential builds of the same project and compare times:

1. First build (cold): `dotnet build` -- server starts, no cache benefit
2. Second build (warm): `dotnet build` -- should be noticeably faster

The most noticeable improvement is in repos with many projects or complex `Directory.Build.props` chains.

## Validation

- [ ] `MSBUILDUSESERVER=1` is set in the shell
- [ ] Second sequential build is faster than the first
- [ ] `dotnet build-server shutdown` followed by a rebuild confirms the server restarts cleanly

## Common Pitfalls

| Pitfall | Solution |
|---------|----------|
| Expecting improvement in Visual Studio | VS already uses long-lived MSBuild nodes; the server adds no benefit |
| Build correctness issues after enabling | Run `dotnet build-server shutdown` to reset; if issues persist, disable the server |
| Server process using unexpected memory | The server persists in background; shut down with `dotnet build-server shutdown` when idle |
