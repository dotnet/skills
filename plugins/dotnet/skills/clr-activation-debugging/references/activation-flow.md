# CLR Shim Activation Flow Reference

## Overview

The CLR shim is the .NET Framework's runtime selection and loading layer. It consists of two DLLs:
- **mscoree.dll** (the "shell shim") — the public-facing entry point. It is the registered `InprocServer32` for CLR-hosted COM objects, the target of `_CorExeMain` for managed EXE launch, and the DLL that legacy hosting APIs (`CorBindToRuntimeEx`, etc.) are exported from.
- **mscoreei.dll** — the actual shim implementation where all the runtime selection logic, logging, activation decisions, and policy evaluation live. mscoree.dll loads and forwards into mscoreei.dll.

The shim supports loading CLR v2.0 and CLR v4.0 side-by-side in the same process (in-proc SxS). Note that .NET 2.0, 3.0, and 3.5 all share CLR v2.0 (v2.0.50727), while .NET 4.0 through 4.8.x all share CLR v4.0 (v4.0.30319).

## Key Concepts

### The v4 In-Proc SxS Design

.NET 4.0 added the ability for multiple runtimes (v2.0 and v4.0) to coexist in the same process. This required a complete rethinking of the hosting API surface. A new set of APIs — the **metahost APIs** (defined in metahost.h: `ICLRMetaHost`, `ICLRMetaHostPolicy`, `ICLRRuntimeInfo`) — was introduced with a multi-runtime view of the world. The entire existing mscoree.h API surface became "legacy."

The central design constraint: **installing v4 must be non-impactful** — it must not change the behavior of any existing component already on the machine.

### Legacy Shim APIs vs. Metahost APIs

**Metahost APIs** (`IsLegacyBind: 0`): The v4+ hosting APIs. These can enumerate all installed runtimes, target specific versions, and support side-by-side loading. This is the normal path for managed EXE launch (`_CorExeMain`) and v4+ COM activation.

**Legacy shim APIs** (`IsLegacyBind: 1`): The entire pre-v4 API surface. These have a **single-runtime-per-process** view of the world, because before v4 there could only be one runtime in a process. The legacy API category encompasses:

1. **`CorBindToRuntimeEx` and friends** — Most flat exports of mscoree.dll defined in mscoree.h (`GetCORSystemDirectory`, `GetCORVersion`, `LoadLibraryShim`, etc.), plus the strong name APIs from strongname.h
2. **Pre-v4 COM activation** — `CoCreateInstance` of a CLSID whose latest registration is against a pre-v4 runtime version. This includes `new` on such a coclass from managed code, or `Activator.CreateInstance` via `Type.GetTypeFromCLSID`.
3. **Pre-v4 IJW (mixed-mode) activation** — Calling into a native export on a pre-v4 mixed-mode assembly
4. **Native activation of runtime-provided COM CLSIDs** — e.g., `CoCreateInstance` on `ICLRRuntimeHost`'s CLSID
5. **Native activation of managed framework CLSIDs** — e.g., `CoCreateInstance` on `System.ArrayList`'s CLSID (extremely rare)

### The Legacy Runtime

Because legacy APIs have a single-runtime view, once any legacy codepath chooses a runtime version, that becomes **the** legacy runtime for the process (`g_pLegacyAPIRuntimeInfo`). All subsequent legacy API calls see and use this same runtime for the remainder of the process lifetime. After a version has been chosen by one of these codepaths, that's the version ALL of them see.

If v4.0 is bound as the legacy runtime (through any mechanism — explicit version string, config with `useLegacyV2RuntimeActivationPolicy`, etc.), all subsequent legacy codepaths will use v4.0.

### Whidbey Capping

All legacy shim API codepaths had roll-forward semantics — they would find and use the latest installed runtime. **Whidbey capping** restricts ("caps") these roll-forward semantics at v2.0 (codename "Whidbey"), meaning **by default, none of the legacy codepaths see v4 at all**. This is what makes v4 installation non-impactful: existing components using legacy APIs continue to behave exactly as they did before v4 was installed.

When `IsCapped: 1` in the logs, the shim will not consider any runtime above v2.0.50727 when enumerating installed runtimes. This has an important and intentional side effect: **on a v4-only machine (no .NET 3.5 installed), legacy codepaths see NO runtimes at all** — the machine appears to have nothing installed from their perspective.

**Capping does NOT prevent loading v4+ if:**
- A specific legitimate post-Whidbey version string is explicitly passed to a legacy API (e.g., `CorBindToRuntimeEx("v4.0.30319", ...)`) — the APIs will happily load it
- A config file `<supportedRuntime>` explicitly names a v4+ version AND `useLegacyV2RuntimeActivationPolicy="true"` is set
- The legacy runtime is already bound to v4+ (all subsequent legacy calls reuse it)

### useLegacyV2RuntimeActivationPolicy

This config attribute is the primary mechanism for making legacy codepaths see v4. Setting `useLegacyV2RuntimeActivationPolicy="true"` in the `<startup>` element tells the shim to treat all runtimes as candidates for legacy API codepaths. It is **mostly equivalent to calling `CorBindToRuntimeEx` with the full v4 version string**.

It can be used:
- With one or more `<supportedRuntime>` entries to direct which runtime is chosen
- With other config options
- With **no `<supportedRuntime>` entries at all** — in which case legacy APIs can simply enumerate and discover v4

```xml
<!-- Common usage: direct legacy codepaths to v4.0 -->
<configuration>
  <startup useLegacyV2RuntimeActivationPolicy="true">
    <supportedRuntime version="v4.0"/>
  </startup>
</configuration>
```

```xml
<!-- Also valid: just let legacy APIs enumerate v4 -->
<configuration>
  <startup useLegacyV2RuntimeActivationPolicy="true">
  </startup>
</configuration>
```

**Side effect:** Enabling this attribute turns off in-proc SxS with pre-v4 runtimes — it locks them out of the process. The legacy runtime becomes v4, and pre-v4 runtimes cannot load.

**Common reasons to use it:**
- Loading pre-v4 mixed-mode (IJW) assemblies into v4
- Making legacy API calls (P/Invoke to `GetCORSystemDirectory`, etc.) return v4 paths
- Ensuring COM activation of managed objects uses the current runtime (v4) instead of falling back to v2

**Why it isn't the default:** If it were the default, installing v4 would be impactful — it would change behavior of existing components, violating the core design constraint.

### Feature-on-Demand (FOD)

When the shim fails to find a runtime, it can trigger installation of .NET 3.5 via Windows Feature-on-Demand. The FOD path:

1. Only activates on Windows 8+ (not on ARM, not in AppX)
2. Re-runs version computation to check if v2.0.50727 would resolve the request
3. If yes, and if the `SEM_FAILCRITICALERRORS` process error mode flag is NOT set, launches `fondue.exe /enable-feature:NetFx3`
4. If `SEM_FAILCRITICALERRORS` IS set, logs "Could have launched..." but does not show the dialog
5. FOD only fires once per process (`g_FeatureOnDemandHasBeenLaunched` flag)

FOD only supports installing .NET 3.5 (v2.0.50727). It cannot install v4.0+.

> ⚠️ **Recent Windows versions:** Starting with Windows 11 Insider Preview Build 27965 and future platform releases, .NET 3.5 is **no longer available as a Windows optional component**. It must be installed from a standalone MSI. On these systems, the FOD dialog (`fondue.exe /enable-feature:NetFx3`) will not succeed even if it fires. This change does not affect Windows 10 or Windows 11 through 25H2. .NET Framework 3.5 reaches end of support on January 9, 2029.

### SEM_FAILCRITICALERRORS Inheritance

The `SEM_FAILCRITICALERRORS` flag is part of the process error mode, set via `SetErrorMode()`. It is **inherited by child processes**. This means:

- If a build system or script calls `SetErrorMode(SEM_FAILCRITICALERRORS)`, all processes it spawns will have the flag set
- If you launch a process directly from a clean cmd.exe, the flag is typically 0
- This is the most common reason FOD behavior changes between different launch methods

## The Version Resolution Decision Tree

This is the order in which `ComputeVersionString` resolves a runtime version:

```
1. COMPLUS_OnlyUseLatestCLR=1? (internal testing only — not supported)
   └─ Yes → FindLatestVersion (uncapped) → done

2. Config file has <supportedRuntime> entries?
   └─ Yes → For each entry (in order):
      ├─ Is this version installed? Check SKU compatibility.
      ├─ If IsCapped AND UseLegacyV2RuntimeActivationPolicy=0:
      │   └─ Only consider v2.0.x entries (skip v4.0+)
      ├─ If IsCapped AND UseLegacyV2RuntimeActivationPolicy=1:
      │   └─ Consider ALL entries including v4.0+ (cap is lifted;
      │      chosen runtime becomes the legacy runtime)
      └─ First installed match wins → done

2b. UseLegacyV2RuntimeActivationPolicy=1 but NO <supportedRuntime> entries?
   └─ Legacy APIs can enumerate all installed runtimes (cap lifted)
      → Falls through to FindLatestVersion (uncapped)

3. Config file has <requiredRuntime> element? (legacy v1.0/v1.1)
   └─ Yes → Use that version → done

4. COMPLUS_Version environment variable set?
   └─ Yes → Use that version → done

5. Host provided a default version?
   └─ Yes → Use that version → done

6. Legacy runtime already bound (g_pLegacyAPIRuntimeInfo != NULL)?
   └─ Yes → Use that version → done

7. Binary has a PE header version (managed assemblies only)?
   └─ Yes → Use that version → done

8. None of the above?
   └─ FindLatestVersion (respects capping)
      ├─ If capped: enumerate installed runtimes ≤v2.0.50727
      ├─ If uncapped: enumerate all installed runtimes
      └─ Return highest match, or (null) if none found
```

If resolution returns `(null)` → runtime not found → enter error/FOD path.

## Entry Point Details

### _CorExeMain (Managed EXE Launch)

1. OS loader recognizes .NET PE header, calls `_CorExeMain`
2. Shim reads PE header for built-with version
3. Looks for `{exe}.config` for `<supportedRuntime>` entries
4. `IsLegacyBind: 0`, `IsCapped: 0` (normal modern bind)
5. Follows decision tree above
6. Loads runtime, transfers control to managed entry point

### DllGetClassObject (COM Activation)

This is the most complex path and the most common source of activation issues.

1. Something calls `CoCreateInstance` for a CLSID registered under mscoree.dll
2. Shim looks up the CLSID in the registry:
   - `HKCR\CLSID\{guid}\InprocServer32` → checks if `(Default)` is mscoree.dll
   - Enumerates version subkeys (e.g., `2.0.50727`, `4.0.30319`)
   - Reads `RuntimeVersion`, `Assembly`, `Class`, `ImplementedInThisVersion` from subkeys
3. Determines if this is a legacy or modern COM object:
   - If CLSID has only old version subkeys or is a known framework COM object → **legacy path**
   - Legacy path sets `IsLegacyBind: 1`, `IsCapped: 1`
4. Version resolution follows the decision tree
5. If the legacy runtime is already bound, reuses it (skips the entire search)

**Critical implication:** For native EXEs doing COM activation, if no config file exists and the legacy runtime is not already bound, a capped legacy bind will enumerate only ≤v2.0 runtimes. On a machine without .NET 3.5, this finds nothing and triggers the error/FOD path.

### CorBindToRuntimeEx (Legacy Hosting)

1. Caller specifies a version (or NULL for default)
2. If version is NULL, uses `FindLatestVersion` (respects capping)
3. If version specified, attempts to load that exact version
4. On success, sets `g_pLegacyAPIRuntimeInfo` (the process-global legacy runtime)
5. Subsequent calls must match the same runtime or fail with `CLR_E_SHIM_LEGACYRUNTIMEALREADYBOUND`

### LoadLibraryShim (Legacy DLL Loading)

1. Loads a framework DLL by name (e.g., `diasymreader.dll`)
2. If version specified, loads from that runtime's directory
3. If no version, uses legacy runtime if bound, otherwise finds latest

## Config File Resolution

The shim looks for config files at:

1. **Activation config**: Set via `COMPLUS_ApplicationMigrationRuntimeActivationConfigPath` (rare)
2. **Host config**: Provided by hosting API caller (rare)
3. **App config**: `{exe_path}.config` (most common)

### Config for legacy codepaths

To make legacy shim API codepaths (including capped COM activation) see v4.0:

```xml
<?xml version="1.0"?>
<configuration>
  <startup useLegacyV2RuntimeActivationPolicy="true">
    <supportedRuntime version="v4.0"/>
  </startup>
</configuration>
```

**`useLegacyV2RuntimeActivationPolicy="true"` is the key piece** — it tells the shim to treat all runtimes as candidates for legacy codepaths, lifting the Whidbey cap. The `<supportedRuntime>` element directs which version is chosen, but even without it, the attribute alone allows legacy APIs to enumerate v4.

Without `useLegacyV2RuntimeActivationPolicy`, rolling forward to v4 via `<supportedRuntime>` works fine for the primary EXE load (which uses the metahost path), but legacy codepaths (COM activation, P/Invoke to mscoree.h APIs, etc.) remain capped and will still look for v2.0.

**Trade-off:** Setting `useLegacyV2RuntimeActivationPolicy="true"` turns off in-proc SxS with pre-v4 runtimes. This is usually acceptable for build tools and utilities, but should be considered for applications that need to host both v2 and v4.

## Environment Variables That Affect Activation

| Variable | Effect |
|----------|--------|
| `COMPLUS_CLRLoadLogDir` | Directory for activation logs (**must already exist**; no error if missing) |
| `COMPLUS_Version` | Force a specific runtime version |
| `COMPLUS_DefaultVersion` | Default version for FindLatestVersion |
| `COMPLUS_Fod` | If 0, disable FOD entirely |
| `COMPLUS_FodConservativeMode` | If 1, only log FOD commands (don't execute) when error dialogs suppressed |
| `COMPLUS_ErrorDialog` | Override SEM_FAILCRITICALERRORS for error dialog display |
| `COMPLUS_ApplicationMigrationRuntimeActivationConfigPath` | Override config file path |
| `COMPLUS_OnlyUseLatestCLR` | ⚠️ **Internal testing only — not supported.** If 1, ignores capping and uses latest installed runtime. Do not use in production. |
