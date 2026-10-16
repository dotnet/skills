---
name: dump-collect
description: "Modern .NET dump collection only (CoreCLR or NativeAOT). NEVER INVOKE FOR .NET Framework 4.x/clr.dll processes; use an approved Windows mechanism such as Sysinternals ProcDump directly. USE FOR: enabling automatic crash dumps, capturing dumps from running modern .NET processes, setting up dump collection in Docker or Kubernetes, using dotnet-dump collect or createdump. DO NOT USE FOR: analyzing or debugging dumps, post-mortem investigation with lldb/windbg/dotnet-dump analyze, profiling, or tracing."
license: MIT
---

# .NET Crash Dump Collection

This skill configures and collects crash dumps for modern .NET applications (CoreCLR and NativeAOT) on Linux, macOS, and Windows — including containers.

## Stop Signals

🚨 **Read before starting any workflow.**

- **Stop after dumps are enabled or collected.** Do not open, analyze, or triage dump files.
- **End the response after collection verification and the artifact path.** Do not append analysis
  commands, debugger suggestions, or an offer to interpret the dump.
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

**From a binary file (Linux/macOS):**
```bash
# NativeAOT — has Redhawk runtime symbols
nm <binary> 2>/dev/null | grep -qi "Rhp" && echo "NativeAOT"
strings <binary> | grep -q "Rhp" && echo "NativeAOT"
```

Do not identify CoreCLR from `CorExeMain` strings in an apphost executable.
For a running process, inspect loaded modules as shown below. For a binary that
is not running, a matching `.runtimeconfig.json` suggests a framework-dependent
CoreCLR app but is not by itself proof of the runtime that a process loaded.

**From a binary file (Windows):**
```powershell
# CoreCLR — has a CLI header (IL entry point)
dumpbin /clrheader <binary.exe> | Select-String "CLI Header" -Quiet

# NativeAOT — no CLI header, has Redhawk symbols
dumpbin /symbols <binary.exe> | Select-String "Rhp" -Quiet
```

**From a running process (Linux):**
```bash
# CoreCLR — positive loaded-module evidence
grep -q 'libcoreclr\.so' /proc/<pid>/maps && echo "CoreCLR"

# NativeAOT — positive Redhawk symbol evidence after CoreCLR is excluded
BINARY=$(readlink /proc/<pid>/exe)
nm "$BINARY" 2>/dev/null | grep -qi "Rhp" && echo "NativeAOT"
```

**From a running process (macOS):**
```bash
# CoreCLR — positive loaded-module evidence
vmmap <pid> | grep -q 'libcoreclr\.dylib' && echo "CoreCLR"

# NativeAOT — positive Redhawk symbol evidence after CoreCLR is excluded
BINARY=$(ps -o comm= -p <pid>)
nm "$BINARY" 2>/dev/null | grep -qi "Rhp" && echo "NativeAOT"
```

**From a running process (Windows PowerShell):**
```powershell
# CoreCLR — loads coreclr.dll
(Get-Process -Id <pid>).Modules.ModuleName -contains "coreclr.dll"

# .NET Framework — loads clr.dll (this skill does not apply)
(Get-Process -Id <pid>).Modules.ModuleName -contains "clr.dll"
```

> **If the app is .NET Framework (`clr.dll`), stop.** This skill covers modern .NET (CoreCLR and NativeAOT) only.
>
> **If neither CoreCLR nor NativeAOT is detected, stop.** This skill only applies to .NET applications — do not proceed.

## Step 2 — Load the Appropriate Reference

Based on the scenario identified in Step 1, read the relevant reference file:

| Scenario | Reference |
|----------|-----------|
| CoreCLR app (any platform) | `references/coreclr-dumps.md` |
| NativeAOT app (any platform) | `references/nativeaot-dumps.md` |
| Any app in Docker or Kubernetes | `references/container-dumps.md` (then also load the runtime-specific reference) |

## Step 3 — Execute

Follow the instructions in the loaded reference to configure or collect dumps. Always:

1. **Confirm the dump output directory exists and is writable by the actual service or container
   identity**, not only by the current shell user. Use an identity-aware check such as
   `sudo -u <service-user> test -w <directory>` on Linux, or run `test -w` as the configured
   container UID.
2. **Report the dump file path** back to the user after collection succeeds.
3. **Verify configuration took effect** — for env vars, echo them; for OS settings, read them back.
4. **Remind the user to disable automatic dumps if they were enabled temporarily** — remove or unset `DOTNET_DbgEnableMiniDump` and related env vars to avoid accumulating dump files.
5. **Use non-interactive container commands.** `docker exec` and `kubectl exec` do not need `-t` or
   `-it` for `dotnet-dump ps`, collection, file checks, or copy operations.
