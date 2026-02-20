---
name: binlog-generation
description: "MANDATORY: Always generate binary logs when running dotnet build, dotnet test, dotnet pack, msbuild.exe, or any MSBuild-based command. Only activate in MSBuild/.NET build contexts (see shared/domain-check.md for signals). Pass /bl:N.binlog with an incrementing counter (1.binlog, 2.binlog, etc.) to ensure unique filenames."
---

# Always Generate Binary Logs

## CRITICAL: Mandatory Rule

**ALWAYS pass the `/bl` switch when running any MSBuild-based command.** This is a non-negotiable requirement for all .NET builds.

## Commands That Require /bl

You MUST add the `/bl:N.binlog` flag to:
- `dotnet build`
- `dotnet test`
- `dotnet pack`
- `dotnet publish`
- `dotnet restore`
- `msbuild` or `msbuild.exe`
- Any other command that invokes MSBuild

## Naming Convention

Use an incrementing counter for the binlog filename, **continuing from existing binlogs in the directory**:

1. Before the first build, check for existing `*.binlog` files with numeric names (e.g., `1.binlog`, `5.binlog`, `12.binlog`)
2. Find the highest number and start from that number + 1
3. If no numeric binlogs exist, start at 1

```bash
# Example: Directory already contains 5.binlog, 6.binlog, 7.binlog
# First build in this session starts at 8
dotnet build /bl:8.binlog

# Second build
dotnet test /bl:9.binlog

# Third build
dotnet pack /bl:10.binlog
```

**The counter increments across ALL MSBuild invocations**, preserving history across sessions.

## Why This Matters

1. **Unique names prevent overwrites** - You can always go back and analyze previous builds
2. **Failure analysis** - When a build fails, the binlog is already there for immediate analysis
3. **Comparison** - You can compare builds before and after changes
4. **No re-running builds** - You never need to re-run a failed build just to generate a binlog

## Examples

```bash
# ✅ CORRECT - Check existing binlogs first, then use next number
# (Assuming 3.binlog is the highest existing)
dotnet build /bl:4.binlog
dotnet test /bl:5.binlog
dotnet build --configuration Release /bl:6.binlog

# ❌ WRONG - Missing /bl flag
dotnet build
dotnet test

# ❌ WRONG - No filename (will overwrite previous)
dotnet build /bl
dotnet build /bl

# ❌ WRONG - Starting at 1 when higher numbers exist
dotnet build /bl:1.binlog  # if 7.binlog already exists
```

## Remember

- Check for existing `N.binlog` files before the first build
- Start at the highest existing number + 1 (or 1 if none exist)
- Increment the counter for EVERY MSBuild invocation
- The binlog file will be created in the current working directory

## Cleaning the Repository

When cleaning the repository with `git clean`, **always exclude binlog files** to preserve your build history:

```bash
# ✅ CORRECT - Exclude binlog files from cleaning
git clean -fdx -e "*.binlog"

# ❌ WRONG - This deletes binlog files (they're usually in .gitignore)
git clean -fdx
```

This is especially important when iterating on build fixes - you need the binlogs to analyze what changed between builds.
