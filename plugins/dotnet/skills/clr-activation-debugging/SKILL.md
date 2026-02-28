---
name: clr-activation-debugging
description: >-
  Diagnoses .NET Framework CLR activation issues using CLR activation logs
  (CLRLoad logs) produced by mscoree.dll. Use when: the shim picks the wrong
  runtime, fails to load any runtime, shows unexpected .NET 3.5 Feature-on-Demand
  (FOD) dialogs, unexpectedly does NOT show FOD dialogs, loads both v2 and v4
  into the same process causing failures, or any time someone is wondering
  "what is happening with .NET Framework activation?"
---

# CLR Activation Debugging

Diagnose .NET Framework runtime activation issues by analyzing CLR activation logs (CLRLoad logs) produced by the shim (mscoree.dll). These logs record every decision the shim makes when selecting and loading a CLR version.

## When to Use

- A process fails to load the CLR at all ("Unable to find a version of the runtime to use")
- The shim picks the wrong CLR version (e.g., v2.0 instead of v4.0)
- Unexpected .NET 3.5 Feature-on-Demand (FOD) install dialogs appear
- FOD dialogs are expected but do NOT appear
- Both CLR v2 and CLR v4 load into the same process, causing failures
- A COM object fails to activate because the shim can't resolve the runtime
- Legacy hosting APIs (CorBindToRuntime) bind to an unexpected version

## When Not to Use

- **Modern .NET (CoreCLR / .NET 5+)** — this skill covers .NET Framework only (the mscoree.dll shim)
- **Assembly binding failures** — use Fusion logs (fuslogvw.exe), not CLR activation logs
- **Runtime crashes after the CLR has loaded** — activation succeeded; the problem is elsewhere

## Background

### The Shim Architecture

The .NET Framework shim has two layers:
- **mscoree.dll** (the "shell shim") — the public-facing DLL that is the registered `InprocServer32` for CLR-hosted COM objects and the entry point for `_CorExeMain`, legacy APIs, etc.
- **mscoreei.dll** — the actual shim implementation where the runtime selection logic, logging, and activation decisions live. mscoree.dll forwards into mscoreei.dll.

When reading logs, the `caller-name:mscoreei.dll` in FOD command lines reflects this — it's mscoreei.dll doing the work.

### .NET 3.5 / v2.0.50727 Version Mapping

.NET 2.0, 3.0, and 3.5 all share the same CLR runtime version: **v2.0.50727**. The "3.0" and "3.5" releases were library additions on top of CLR v2.0. For activation purposes, they are all "v2.0.50727." When the shim resolves to v2.0.50727 or FOD offers to install "NetFx3", it's installing the CLR v2.0 runtime (plus the 3.0/3.5 libraries). Similarly, CLR v4.0 (v4.0.30319) covers all .NET Framework versions from 4.0 through 4.8.x.

### .NET 3.5 Availability on Recent Windows

On recent Windows versions (Windows 11 Insider Preview Build 27965 and future platform releases), .NET Framework 3.5 is **no longer available as a Windows optional component (Feature-on-Demand)**. It must be installed from a standalone MSI. This means the FOD dialog (`fondue.exe /enable-feature:NetFx3`) will not succeed on these systems even if it fires. On Windows 10 and Windows 11 through 25H2, FOD remains available. .NET Framework 3.5 reaches end of support on January 9, 2029.

### Shim HRESULT Codes

When the shim fails, it returns specific HRESULTs in the `0x8013xxxx` range. These are the errors you'll see from callers (not in the activation logs themselves, which log human-readable messages):

| HRESULT | Symbol | Meaning |
|---------|--------|---------|
| `0x80131700` | `CLR_E_SHIM_RUNTIMELOAD` | Cannot find or load a suitable runtime version. **This is the most common shim error** — it's what callers see when capped legacy activation fails on a v4-only machine. |
| `0x80131701` | `CLR_E_SHIM_RUNTIMEEXPORT` | Found a runtime but failed to get a required export or interface from it. |
| `0x80131702` | `CLR_E_SHIM_INSTALLROOT` | The .NET Framework install root is missing or invalid in the registry. |
| `0x80131703` | `CLR_E_SHIM_INSTALLCOMP` | A required component of the installation is missing. |
| `0x80131704` | `CLR_E_SHIM_LEGACYRUNTIMEALREADYBOUND` | A different runtime is already bound as the legacy runtime. A legacy API tried to bind to a version that conflicts with the one already chosen. |
| `0x80131705` | `CLR_E_SHIM_SHUTDOWNINPROGRESS` | The shim is shutting down and cannot service the request. |

If a user reports one of these HRESULTs (especially `0x80131700`), CLR activation logs are the right diagnostic tool.

## Prerequisites

CLR activation logging must be enabled to produce log files. If the user doesn't have logs yet, instruct them to enable logging:

**Via environment variable (recommended — scoped to current session):**
```
set COMPLUS_CLRLoadLogDir=C:\CLRLoadLogs
```

**Via registry (machine-wide — affects all .NET Framework processes):**
```
HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\.NETFramework
  CLRLoadLogDir = "C:\CLRLoadLogs" (REG_SZ)
```

On 64-bit systems, also set under `Wow6432Node` if 32-bit processes are involved.

> ⚠️ **The log directory must already exist.** The shim will not create it. If it doesn't exist, no logs will be written and there will be no error or indication of failure.

Logs are written as `{ProcessName}.CLRLoad{NN}.log` (NN = 00–99, one per process instance). **Logs cannot be read until the process exits** — the file is held open.

After capturing, **remove the env var or registry key** to stop logging.

## Inputs

| Input | Required | Description |
|-------|----------|-------------|
| CLR activation log files | Yes | One or more `.CLRLoad*.log` files |
| Symptom description | Recommended | What the user observed (FOD dialog, wrong runtime, failure, etc.) |
| Expected behavior | Recommended | What the user expected to happen |

## Workflow

### Step 1: Load Reference Material

Try to load the reference files in this order — they contain the detailed log format, decision flow, and CLSID registry documentation:

1. `references/log-format.md` — Log line format, fields, and all known log message types
2. `references/activation-flow.md` — The shim's decision tree for runtime selection
3. `references/com-activation.md` — COM (DllGetClassObject) activation specifics, CLSID registry layout

If reference files are not available, proceed using the inline knowledge below.

### Step 2: Survey the Log Files

Get the big picture before diving into any single log:

1. **List all log files** and group by process name — this shows which executables triggered CLR activation
2. **For each process, scan for outcome lines:**
   - `Decided on runtime: vX.Y.Z` — successful resolution
   - `ERROR:` — failed resolution
   - `Launching feature-on-demand` — FOD dialog was shown
   - `Could have launched feature-on-demand` — FOD would have fired but was suppressed
   - `V2.0 Capping is preventing consideration` — v4+ was skipped due to capping

```
grep -l "ERROR:\|Launching feature-on-demand\|Could have launched" *.log
grep -c "Launching feature-on-demand" *.log
```

3. **Build a summary table:**

| Process | Log Files | Outcome | Runtime Selected | FOD? |
|---------|-----------|---------|-----------------|------|
| ... | ... | ... | ... | ... |

### Step 3: Analyze Problematic Logs

For each log file with an unexpected outcome, trace the full activation flow. Read the log top-to-bottom and identify:

> ⚠️ **Nested log entries:** The shim's own internal calls can trigger additional log entries within an activation sequence that is already being logged. For example, a `DllGetClassObject` call may internally call `ComputeVersionString`, which calls `FindLatestVersion`, each generating log lines. When the FOD check runs ("Checking if feature-on-demand installation would help"), it re-runs the entire version computation — producing a second `ComputeVersionString` block within the same activation. Don't mistake these nested/re-entrant entries for separate activation attempts.

#### 3a. Entry Point

The first `FunctionCall:` or `MethodCall:` line tells you how activation was triggered:

| Entry Point | Meaning |
|-------------|---------|
| `_CorExeMain` | Managed EXE launch — the binary IS a .NET assembly |
| `DllGetClassObject. Clsid: {guid}` | COM activation — something CoCreated a COM class routed through mscoree.dll |
| `ClrCreateInstance` | Modern (v4+) hosting API |
| `CorBindToRuntimeEx` | Legacy (v1/v2) hosting API — binds the process to one runtime |
| `ICLRMetaHostPolicy::GetRequestedRuntime` | Policy-based hosting API (often called internally after other entry points) |
| `LoadLibraryShim` | Legacy API to load a framework DLL by name |

#### 3b. Input Parameters

Immediately after the entry point, the log dumps the version computation inputs:

- **`IsLegacyBind`**: Is this a legacy (pre-v4) activation path? If 1, the shim uses the single-runtime "legacy" view of the world. Legacy APIs (`CorBindToRuntimeEx`, `DllGetClassObject` for legacy COM, `LoadLibraryShim`, etc.) set this.
- **`IsCapped`**: If 1, the shim's roll-forward semantics are capped at Whidbey (v2.0.50727) — it will NOT consider v4.0+ when enumerating installed runtimes. This is the mechanism that makes v4 installation non-impactful: legacy codepaths continue to behave as if v4 doesn't exist. On a v4-only machine with no .NET 3.5, a capped enumeration sees **no runtimes at all**. Capping does NOT prevent loading v4+ if a specific v4 version string is explicitly provided (e.g., via `CorBindToRuntimeEx("v4.0.30319", ...)` or via config with `useLegacyV2RuntimeActivationPolicy`).
- **`SkuCheckFlags`**: Controls SKU (edition) compatibility checking.
- **`ShouldEmulateExeLaunch`**: Whether to pretend this is an EXE launch for policy purposes.
- **`LegacyBindRequired`**: Whether a legacy bind is strictly required.

#### 3c. Config File Processing

Look for config file parsing results:

- `Parsing config file: {path}` — the shim is looking for a `.config` file
- `Config File (Open). Result:00000000` — config file found and opened successfully
- `Config File (Open). Result:80070002` — **config file not found** (HRESULT for ERROR_FILE_NOT_FOUND)
- `Found config file: {path}` — config was successfully read
- `UseLegacyV2RuntimeActivationPolicy is set to {0|1}` — whether `<startup useLegacyV2RuntimeActivationPolicy="true">` is present. When 1, all runtimes are treated as candidates for legacy codepaths — meaning legacy shim APIs can enumerate and choose v4+. This can be used with multiple `<supportedRuntime>` entries, with other config options, or even with no `<supportedRuntime>` entries at all (in which case legacy APIs can simply enumerate v4). **Side effect:** turns off in-proc SxS with pre-v4 runtimes — locks them out of the process.
- `Config file includes SupportedRuntime entry. Version: vX.Y.Z, SKU: {sku}` — each `<supportedRuntime>` found in config

**Key insight:** If a process has no config file AND is doing a capped legacy bind, the shim has nothing to direct it to v4.0. It will enumerate installed runtimes (capped to ≤v2.0), find nothing if 3.5 isn't installed, and fail. This is by design — v4 is intentionally invisible to these codepaths to keep v4 installation non-impactful.

#### 3d. Version Resolution

- `Installed Runtime: vX.Y.Z. VERSION_ARCHITECTURE: N` — what's installed on the machine
- `{exe} was built with version: vX.Y.Z` — version from the binary's PE header (managed assemblies only; native EXEs won't have this)
- `Using supportedRuntime: vX.Y.Z` — the shim picked a version from the config's `<supportedRuntime>` list
- `FindLatestVersion is returning the following version: vX.Y.Z ... V2.0 Capped: {0|1}` — result of policy-based latest-version search
- `Default version of the runtime on the machine: vX.Y.Z` or `(null)` — what the shim settled on; `(null)` means nothing was found
- `Decided on runtime: vX.Y.Z` — **final decision** — this is the version that will be loaded

#### 3e. Failure and FOD Path

If version resolution fails:

1. `ERROR: Unable to find a version of the runtime to use` — the shim found no suitable runtime
2. `SEM_FAILCRITICALERRORS is set to {value}` — checks the process error mode:
   - **Value 0**: Error dialogs and FOD are ALLOWED
   - **Nonzero** (any bit set, commonly 0x8001): Error dialogs and FOD are SUPPRESSED. The `SEM_FAILCRITICALERRORS` flag (0x0001) is inherited from the parent process.
3. `Checking if feature-on-demand installation would help` — the shim re-runs version computation to see if installing .NET 3.5 would resolve the request
4. Then either:
   - `Launching feature-on-demand installation. CmdLine: "...\fondue.exe" /enable-feature:NetFx3` — **FOD dialog shown**
   - `Could have launched feature-on-demand installation if was not opted out.` — **FOD suppressed** because `SEM_FAILCRITICALERRORS` was set

#### 3f. Multiple Activations in One Process

A single log can contain multiple activation sequences. Each begins with a new `FunctionCall:` or `MethodCall:` entry. A common pattern:

1. First activation via `ClrCreateInstance` / `GetRequestedRuntime` → succeeds (loads v4.0 via config)
2. Second activation via `DllGetClassObject` (COM) → legacy bind, capped → fails

This happens when a native EXE (like link.exe or mt.exe) loads the CLR successfully for its primary work, then a secondary COM activation request (e.g., for diasymreader) triggers a separate legacy resolution that can't find v2.0.

### Step 4: Check System State (if needed)

When log analysis points to a registration or configuration issue, check:

**CLSID Registration** (for COM activation issues):
```powershell
# Check the CLSID entry
Get-ItemProperty 'Registry::HKCR\CLSID\{guid}'
Get-ItemProperty 'Registry::HKCR\CLSID\{guid}\InprocServer32'
Get-ChildItem 'Registry::HKCR\CLSID\{guid}\InprocServer32' | ForEach-Object {
    Write-Output "--- $($_.PSChildName) ---"
    Get-ItemProperty "Registry::$($_.Name)"
}
```

Key values under `InprocServer32`:
- `(Default)` should be `mscoree.dll` for CLR-hosted COM objects
- **Version subkeys** (e.g., `2.0.50727`, `4.0.30319`) indicate which runtime versions registered this CLSID
- **`ImplementedInThisVersion`** under a version subkey means that runtime version natively implements the COM class (not via managed interop)
- **`Assembly`** and **`Class`** under a version subkey indicate a managed COM interop registration
- **`RuntimeVersion`** under a version subkey specifies which CLR version should host this object

**Installed runtimes:**
```powershell
Get-ChildItem 'Registry::HKLM\SOFTWARE\Microsoft\.NETFramework\policy'
```

**Process error mode** (why FOD did/didn't fire):
The `SEM_FAILCRITICALERRORS` flag is inherited from the parent process. If a build system or script sets it (or calls `SetErrorMode`), all child processes inherit it.

### Step 5: Diagnose and Report

Produce a clear diagnosis covering:

1. **What happened** — which process(es) had activation issues and what the symptom was
2. **Why it happened** — trace through the specific decision path in the shim that led to the outcome
3. **What controls the behavior** — identify the specific inputs (config file presence, error mode, CLSID registration, capping state) that determined the outcome
4. **What changed** (if applicable) — if the user says behavior changed, identify which input could have changed (error mode from parent process, config file, CLSID registration, installed runtimes)

## Common Scenarios

### Unexpected FOD Dialogs

**Pattern:** `DllGetClassObject` → `IsCapped: 1` → no config file → `(null)` → `SEM_FAILCRITICALERRORS: 0` → FOD launched

**Root cause:** A native EXE is doing COM activation of a CLSID registered under mscoree.dll. This takes the legacy codepath, which is capped at v2.0. With no config file (and no `useLegacyV2RuntimeActivationPolicy`), v4 is invisible to this codepath. On a machine without .NET 3.5, there are no runtimes visible, and with `SEM_FAILCRITICALERRORS` not set, the FOD dialog fires.

**Key question:** Why did `SEM_FAILCRITICALERRORS` change? It's inherited from the parent. Different launch methods (script vs. direct invocation, different build systems) produce different error modes. The underlying capped-legacy-bind-on-v4-only-machine failure is always there — it's just that `SEM_FAILCRITICALERRORS` controls whether it manifests as a visible dialog or a silent failure.

### Wrong Runtime Selected

**Pattern:** `supportedRuntime` entries in config list multiple versions; the shim picks the first one that's installed. If v2.0 is listed first and .NET 3.5 is installed, v2.0 wins even though v4.0 is also available.

**Key insight:** Config `<supportedRuntime>` entries are evaluated in order. First installed match wins.

### Both v2 and v4 Loaded

**Pattern:** Multiple activation sequences in the same process log — one binds v4, another binds v2 (or vice versa). Side-by-side loading of CLR v2 and v4 in the same process IS supported but can cause issues with shared state.

**Key insight:** Look for separate `Decided on runtime` lines with different versions in the same log file.

### Legacy Runtime Already Bound

**Pattern:** A legacy codepath succeeds early in the process (e.g., `CorBindToRuntimeEx` with an explicit v4 version, or config with `useLegacyV2RuntimeActivationPolicy`). This sets the legacy runtime to v4.0. All subsequent legacy activations — including capped COM activations that would otherwise fail — silently succeed by reusing the already-bound legacy runtime.

**Key insight:** The ORDER of activations within a process matters. If v4.0 is bound as the legacy runtime first, capped COM activations work. If the capped COM activation happens first (before any legacy runtime is bound), it fails. This means behavior can depend on which component activates first — a race condition in concurrent code can change the outcome.

## Common Pitfalls

| Pitfall | Correct Approach |
|---------|-----------------|
| Assuming `IsCapped: 1` means v4.0 can never load | Capping only restricts roll-forward enumeration. v4.0 can still be loaded if: a specific version string is passed explicitly, config has `useLegacyV2RuntimeActivationPolicy="true"` with `<supportedRuntime version="v4.0"/>`, or the legacy runtime is already bound to v4+. |
| Thinking capping is broken or a bug | Capping is intentional — it makes v4 installation non-impactful. On a v4-only machine, legacy codepaths correctly see no runtimes. This is working as designed. |
| Assuming FOD is controlled per-process | `SEM_FAILCRITICALERRORS` is inherited from the parent process. A change in the parent (build system, script, shell) changes behavior for all children. |
| Looking only at the first activation in a log | A single log can contain multiple independent activation sequences. The problematic one is often a secondary COM activation, not the initial CLR load. |
| Assuming a missing config file is benign | For native EXEs doing COM activation with legacy/capped bind, the config file (with `useLegacyV2RuntimeActivationPolicy`) is the primary way to make legacy codepaths see v4.0. No config = capped = v4 invisible. |
| Adding `<supportedRuntime>` without `useLegacyV2RuntimeActivationPolicy` | Without `useLegacyV2RuntimeActivationPolicy="true"`, rolling forward to v4 via config works for the primary EXE load, but legacy codepaths (COM activation, P/Invoke to mscoree.h APIs) remain capped at v2.0. Both are needed for legacy codepaths. |
| Setting `useLegacyV2RuntimeActivationPolicy` without understanding the trade-off | This attribute turns off in-proc SxS — it locks pre-v4 runtimes out of the process. This is usually fine for build tools but should be considered for apps that need to host both v2 and v4. |

## Validation

Before delivering a diagnosis, verify:

- [ ] All log files with errors or FOD triggers were analyzed (not just the first one)
- [ ] The entry point for each problematic activation was identified
- [ ] The capping and legacy bind state was noted for each activation sequence
- [ ] Config file presence/absence was checked
- [ ] SEM_FAILCRITICALERRORS state was noted for FOD-related issues
- [ ] Multiple activations within a single log were individually traced
- [ ] The diagnosis explains the specific decision path, not just the outcome
