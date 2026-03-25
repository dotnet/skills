---
name: msbuild-server
description: "Guide for using MSBuild Server to improve CLI build performance. Activate when developers report slow incremental builds from the command line, or when CLI builds are noticeably slower than IDE builds. Covers MSBUILDUSESERVER=1 environment variable for persistent server-based caching. Do not activate for IDE-based builds (Visual Studio already uses a long-lived process)."
---

## MSBuild Server for CLI Caching

### When to Use

- Small incremental builds from CLI (`dotnet build`) are slower than expected
- Developers notice that VS builds are faster than CLI builds for the same project
- CI agents run many sequential builds of the same repo

### How It Works

By default, each `dotnet build` invocation starts a fresh MSBuild process. The MSBuild Server keeps a long-lived process that caches evaluation results, resolved assemblies, and other state across builds — similar to how Visual Studio reuses its MSBuild nodes.

### Setup

Set the environment variable:

```bash
# Bash / CI
export MSBUILDUSESERVER=1

# PowerShell
$env:MSBUILDUSESERVER = "1"

# Windows (persistent)
setx MSBUILDUSESERVER 1
```

### Expected Impact

- **First build**: no change (server starts cold)
- **Subsequent incremental builds**: faster due to cached evaluation, resolved references, and warm JIT
- Most noticeable in repos with many projects or complex `Directory.Build.props` chains

### Limitations

- The server process persists in the background — uses some memory
- If builds behave unexpectedly, try `dotnet build-server shutdown` to reset
- Not all MSBuild features are compatible with server mode
