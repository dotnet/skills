---
name: binlog-generation
description: "Only activate in MSBuild/.NET build context. Pass /bl:{} to generate a unique binlog filename automatically on every build."
---

# Generate Binary Logs

**Pass the `/bl` switch when running any MSBuild-based command.** This is a non-negotiable requirement for all .NET builds.

## Commands That Require /bl

You MUST add the `/bl:{}` flag to:
- `dotnet build`
- `dotnet test`
- `dotnet pack`
- `dotnet publish`
- `dotnet restore`
- `msbuild` or `msbuild.exe`
- Any other command that invokes MSBuild

## Preferred: Use `{}` for Automatic Unique Names

> **Note:** The `{}` placeholder requires MSBuild 17.8+ / .NET 8 SDK or later.

The `{}` placeholder in the binlog filename is replaced by MSBuild with a unique identifier, guaranteeing no two builds ever overwrite each other — without needing to track or check existing files.

```bash
# Every invocation produces a distinct file automatically
dotnet build /bl:{}
dotnet test /bl:{}
dotnet build --configuration Release /bl:{}
```

**PowerShell requires escaping the braces:**

```powershell
# PowerShell: escape { } as {{ }}
dotnet build -bl:{{}}
dotnet test -bl:{{}}
```

## Why This Matters

1. **Unique names prevent overwrites** - You can always go back and analyze previous builds
2. **Failure analysis** - When a build fails, the binlog is already there for immediate analysis
3. **Comparison** - You can compare builds before and after changes
4. **No re-running builds** - You never need to re-run a failed build just to generate a binlog

## Examples

```bash
# ✅ CORRECT - {} generates a unique name automatically (bash/cmd)
dotnet build /bl:{}
dotnet test /bl:{}

# ✅ CORRECT - PowerShell escaping
dotnet build -bl:{{}}
dotnet test -bl:{{}}

# ❌ WRONG - Missing /bl flag entirely
dotnet build
dotnet test

# ❌ WRONG - No filename (overwrites the same msbuild.binlog every time)
dotnet build /bl
dotnet build /bl
```

## When a Specific Filename Is Required

If the binlog filename needs to be known upfront (e.g., for CI artifact upload), or if `{}` is not available in the installed MSBuild version, pick a name that won't collide with existing files:

1. Check for existing `*.binlog` files in the directory
2. Choose a name not already taken (e.g., by incrementing a counter from the highest existing number)

```bash
# Example: directory contains 3.binlog — use 4.binlog
dotnet build /bl:4.binlog
```

## Cleaning the Repository

When cleaning the repository with `git clean`, **always exclude binlog files** to preserve your build history:

```bash
# ✅ CORRECT - Exclude binlog files from cleaning
git clean -fdx -e "*.binlog"

# ❌ WRONG - This deletes binlog files (they're usually in .gitignore)
git clean -fdx
```

This is especially important when iterating on build fixes - you need the binlogs to analyze what changed between builds.
