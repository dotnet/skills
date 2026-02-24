---
name: dump-collect
description: "Configure and collect crash dumps for modern .NET applications. USE FOR: enabling automatic crash dumps for CoreCLR or NativeAOT, capturing dumps from running .NET processes, setting up dump collection in Docker or Kubernetes, using dotnet-dump collect or createdump. DO NOT USE FOR: analyzing or debugging dumps, post-mortem investigation with lldb/windbg/dotnet-dump analyze, profiling or tracing."
---

# .NET Crash Dump Collection

This skill configures and collects crash dumps for modern .NET applications (CoreCLR and NativeAOT) on Linux, macOS, and Windows — including containers.

## Stop Signals

🚨 **Read before starting any workflow.**

- **Stop after dumps are enabled or collected.** Do not open, analyze, or triage dump files.
- **If the user already has a dump file**, this skill does not cover analysis. Let them know analysis is out of scope.
- **Do not install analysis tools** (dotnet-dump analyze, windbg). Only install collection tools (dotnet-dump collect). Using `lldb` for on-demand dump capture on macOS is allowed — it ships with Xcode command-line tools and is not being used for analysis.
- **Do not trace root cause** of crashes. Report the dump file location and move on.
- **Do not modify application code.** Configuration is environment-only (env vars, OS settings, container specs).

## Step 1 — Identify the Scenario

Ask or determine:

1. **Goal**: Enable automatic crash dumps, or capture a dump from a running process right now?
2. **Platform**: Linux, macOS, or Windows? Running in a container (Docker/Kubernetes)?
3. **Runtime**: CoreCLR or NativeAOT?

### Detecting CoreCLR vs NativeAOT

**From a binary file:**
```bash
# CoreCLR — has IL metadata / managed entry point
strings <binary> | grep -q "CorExeMain" && echo "CoreCLR"

# NativeAOT — has Redhawk runtime symbols
strings <binary> | grep -q "Rhp" && echo "NativeAOT"

# On macOS/Linux, also try:
nm <binary> 2>/dev/null | grep -qi "Rhp" && echo "NativeAOT"
```

**From a running process (Linux):**
```bash
# CoreCLR — loads libcoreclr
grep -q "libcoreclr" /proc/<pid>/maps && echo "CoreCLR" || echo "Likely NativeAOT"
```

**From a running process (macOS):**
```bash
# CoreCLR — loads libcoreclr.dylib
vmmap <pid> 2>/dev/null | grep -q "libcoreclr" && echo "CoreCLR" || echo "Likely NativeAOT"
```

## Step 2 — Load the Appropriate Reference

Based on the scenario identified in Step 1, read the relevant reference file:

| Scenario | Reference |
|----------|-----------|
| CoreCLR app (any platform) | `references/coreclr-dumps.md` |
| NativeAOT app (any platform) | `references/nativeaot-dumps.md` |
| Any app in Docker or Kubernetes | `references/container-dumps.md` (then also load the runtime-specific reference) |

## Step 3 — Execute

Follow the instructions in the loaded reference to configure or collect dumps. Always:

1. **Confirm the dump output directory exists** and has write permissions before enabling collection.
2. **Report the dump file path** back to the user after collection succeeds.
3. **Verify configuration took effect** — for env vars, echo them; for OS settings, read them back.
