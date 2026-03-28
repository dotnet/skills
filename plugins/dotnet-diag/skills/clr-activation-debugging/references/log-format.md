# CLR Activation Log Format Reference

## Enabling Logging

Set one of:
- **Environment variable:** `COMPLUS_CLRLoadLogDir=C:\path\to\logdir`
- **Registry:** `HKLM\SOFTWARE\Microsoft\.NETFramework\CLRLoadLogDir` (REG_SZ)
- On 64-bit, also `HKLM\SOFTWARE\Wow6432Node\Microsoft\.NETFramework\CLRLoadLogDir` for 32-bit processes

> ⚠️ **The log directory must already exist.** The shim will not create it. If it doesn't exist, no logs will be written and there will be no error or indication of failure.

Logs are written by mscoreei.dll (the shim implementation DLL loaded by mscoree.dll). They are written while the process runs and **cannot be opened until the process exits**.

## File Naming

```
{ProcessName}.CLRLoad{NN}.log
```

- `{ProcessName}` = the EXE name (e.g., `mt.exe`, `csc.exe`, `MSBuild.exe`)
- `{NN}` = sequence number 00–99 (increments if the previous file exists, one per process instance)
- Multiple log files for the same EXE indicate multiple process invocations

## Line Format

Every line in the log follows this format:

```
ThreadID,TickCount.Milliseconds,Message
```

- **ThreadID**: OS thread ID (decimal) that produced the log entry
- **TickCount.Milliseconds**: Time in `GetTickCount()` units — seconds since system boot, with millisecond precision. Useful for ordering events and measuring elapsed time within and across logs.
- **Message**: The log message (see below)

### Header Lines

The first three lines of every log are:

```
{tid},{tick},CLR Loading log for {full_path_to_exe}
{tid},{tick},Log started at {time} on {date}
{tid},{tick},-----------------------------------
```

The process path in the first line identifies exactly which binary triggered CLR activation.

## Log Message Reference

### Entry Points

| Message | Meaning |
|---------|---------|
| `FunctionCall: _CorExeMain` | Managed EXE launch — the OS loader recognized a .NET assembly |
| `FunctionCall: DllGetClassObject. Clsid: {guid}, Iid: {iid}` | COM activation — CoCreateInstance routed through mscoree.dll |
| `FunctionCall: ClrCreateInstance, Clsid: {guid}, Iid: {iid}` | Modern v4+ hosting API entry |
| `LegacyFunctionCall: CorBindToRuntimeEx. Version: {ver}, BuildFlavor: {flavor}, Flags: {hex}` | Legacy v1/v2 hosting API binding |
| `LegacyFunctionCall: LoadLibraryShim. DllName: {dll}, Version: {ver}` | Legacy API to load a framework DLL |
| `LegacyFunctionCall: GetFileVersion. Filename: {path}` | Shim reading PE version from a binary |
| `MethodCall: ICLRMetaHostPolicy::GetRequestedRuntime. Version: {ver}, Metahost Policy Flags: {hex}, Binary: {path}` | Policy-based runtime request |
| `MethodCall: ICLRRuntimeInfo::GetInterface. Clsid: {guid}, Iid: {iid}` | Requesting an interface from a loaded runtime |

### Version Computation

| Message | Meaning |
|---------|---------|
| `Input values for ComputeVersionString follow this line` | Start of a version resolution block |
| `IsLegacyBind is: {0\|1}` | Whether this is a legacy (pre-v4) activation path |
| `IsCapped is {0\|1}` | Whether enumeration is restricted to ≤v2.0.50727 |
| `SkuCheckFlags are {value}` | SKU compatibility check mode |
| `ShouldEmulateExeLaunch is {0\|1}` | Whether to use EXE launch policies |
| `LegacyBindRequired is {0\|1}` | Whether legacy binding is strictly required |
| `Installed Runtime: vX.Y.Z. VERSION_ARCHITECTURE: N` | A runtime version found installed on the machine |

### Config File Processing

| Message | Meaning |
|---------|---------|
| `Parsing config file: {path}` | Looking for an application config file |
| `Config File (Open). Result:00000000` | Config file found and opened successfully |
| `Config File (Open). Result:80070002` | Config file **not found** (ERROR_FILE_NOT_FOUND) |
| `Config File (Read). Result:00000000` | Config file read successfully |
| `Found config file: {path}` | Config file successfully parsed |
| `UseLegacyV2RuntimeActivationPolicy is set to {0\|1}` | Value of `<startup useLegacyV2RuntimeActivationPolicy="true\|false">` |
| `Config file includes SupportedRuntime entry. Version: {ver}, SKU: {sku}` | A `<supportedRuntime>` element from the config |
| `Found a supportedRuntime tag in the config file` | At least one `<supportedRuntime>` was present |

### Runtime Selection Outcomes

| Message | Meaning |
|---------|---------|
| `Using supportedRuntime: vX.Y.Z` | Shim selected this version from config's `<supportedRuntime>` list |
| `{exe} was built with version: vX.Y.Z` | PE header version from a managed binary |
| `FindLatestVersion is returning the following version: vX.Y.Z Input VERSION_ARCHITECTURE: N, V2.0 Capped: {0\|1}` | Result of policy-based latest-version search |
| `Default version of the runtime on the machine: vX.Y.Z` | Resolved default version |
| `Default version of the runtime on the machine: (null)` | **No runtime found** — resolution failed |
| `Decided on runtime: vX.Y.Z` | **Final decision** — this version will be loaded |
| `Runtime has been loaded. Version: vX.Y.Z` | CLR successfully loaded into the process |
| `V2.0 Capping is preventing consideration of a newer runtime` | A v4+ runtime was skipped because capping is active |

### Errors and FOD

| Message | Meaning |
|---------|---------|
| `ERROR: Unable to find a version of the runtime to use.` | Version resolution failed completely |
| `ERROR: Version vX.Y.Z is not present on the machine.` | A specific requested version is not installed |
| `SEM_FAILCRITICALERRORS is set to {value}` | Process error mode check before showing dialogs. 0 = dialogs allowed. Nonzero = dialogs suppressed. |
| `Checking if feature-on-demand installation would help` | Shim is re-running version computation to check if installing .NET 3.5 would help |
| `Launching feature-on-demand installation. CmdLine: {cmd}` | **FOD dialog is being shown** — fondue.exe is launched |
| `Could have launched feature-on-demand installation if was not opted out. CmdLine: {cmd}` | FOD was suppressed because SEM_FAILCRITICALERRORS is set |

### Process Lifecycle

| Message | Meaning |
|---------|---------|
| `FunctionCall: OnShimDllMainCalled. Reason: {code}` | DllMain callback (1=PROCESS_ATTACH, 0=PROCESS_DETACH, 2=THREAD_ATTACH, 3=THREAD_DETACH) |
| `FunctionCall: RealDllMain. Reason: {code}` | Actual DllMain processing |
| `LegacyFunctionCall: CorExitProcess. Code: {exit_code}` | Process exiting through legacy API |

### Runtime Info Queries

| Message | Meaning |
|---------|---------|
| `MethodCall: ICLRRuntimeInfo::GetVersionString. Version: vX.Y.Z` | Querying runtime version |
| `MethodCall: ICLRRuntimeInfo::GetRuntimeDirectory. Version: vX.Y.Z` | Querying runtime install path |
| `MethodCall: ICLRRuntimeInfo::LoadLibrary. Name: {dll}. Version: vX.Y.Z` | Loading a framework DLL |

## Well-Known CLSIDs

These CLSIDs frequently appear in COM activation logs:

| CLSID | Name | Notes |
|-------|------|-------|
| `{E5CB7A31-7512-11D2-89CE-0080C792E5D8}` | CorSymWriter_SxS / CLR Meta Data | Debug symbol writer — common trigger for legacy COM activation in native build tools |
| `{0A29FF9E-7F9C-4437-8B11-F424491E3931}` | NDP SymBinder | Debug symbol binder |
| `{9280188D-0E8E-4867-B30C-7FA83884E8DE}` | CLRMetaHost | ICLRMetaHost — the v4+ entry point for hosting |
| `{2EBCD49A-1B47-4A61-B13A-4A03701E594B}` | CLRMetaHostPolicy | ICLRMetaHostPolicy — policy-based hosting |
| `{CB2F6723-AB3A-11D2-9C40-00C04FA30A3E}` | CorRuntimeHost | Legacy v1/v2 hosting |
| `{F7721072-BF57-476D-89F8-A7625D27683A}` | CLRStrongName | Strong name APIs |
| `{B79B0ACD-F5CD-409B-B5A5-A16244610B92}` | ALink | Assembly linker |

## Well-Known IIDs

| IID | Interface |
|-----|-----------|
| `{00000001-0000-0000-C000-000000000046}` | IClassFactory |
| `{D332DB9E-B9B3-4125-8207-A14884F53216}` | ICLRMetaHost |
| `{E2190695-77B2-492E-8E14-C4B3A7FDD593}` | ICLRMetaHostPolicy |
| `{BD39D1D2-BA2F-486A-89B0-B4B0CB466891}` | ICLRRuntimeInfo |
| `{31BCFCE2-DAFB-11D2-9F81-00C04F79A0A3}` | IMetaDataDispenserEx |
| `{CB2F6722-AB3A-11D2-9C40-00C04FA30A3E}` | ICorRuntimeHost |
| `{07C4E752-3CBA-4A07-9943-B5F206382178}` | ICLRRuntimeHost |

## SEM_FAILCRITICALERRORS Values

The `SEM_FAILCRITICALERRORS is set to {value}` line reports the result of `SetErrorMode(0)` (which returns the previous mode). Common values:

| Value | Hex | Flags Set | FOD Allowed? |
|-------|-----|-----------|-------------|
| 0 | 0x0000 | None | **Yes** |
| 1 | 0x0001 | SEM_FAILCRITICALERRORS | No |
| 32769 | 0x8001 | SEM_FAILCRITICALERRORS + SEM_NOGPFAULTERRORBOX | No |
| 32768 | 0x8000 | SEM_NOGPFAULTERRORBOX only | **Yes** (only SEM_FAILCRITICALERRORS bit matters) |

Any nonzero value where bit 0 (SEM_FAILCRITICALERRORS = 0x0001) is set will suppress the FOD dialog.

## Nested / Re-Entrant Log Entries

Log entries within a single activation sequence can include the shim's own internal calls into its own APIs. This creates nested blocks that can be confusing if you don't expect them:

- A `DllGetClassObject` call internally triggers `ComputeVersionString`, which may call `FindLatestVersion` — all generating log lines within the same sequence
- When the FOD check runs ("Checking if feature-on-demand installation would help"), it **re-runs the entire version computation** — producing a second `Input values for ComputeVersionString` block. This is the shim asking "if .NET 3.5 were installed, would that resolve this request?"
- A `GetRequestedRuntime` call may internally trigger `GetFileVersion` and config parsing

The key to reading these is: watch for the major entry point lines (`FunctionCall:`, `MethodCall:`) to distinguish top-level activations from internal re-entrant calls.

## .NET 3.5 / v2.0.50727 Version Mapping

In the logs, all version strings refer to the **CLR runtime version**, not the .NET Framework marketing version:

| Runtime Version String | CLR Version | .NET Framework Versions |
|----------------------|-------------|------------------------|
| `v1.0.3705` | CLR 1.0 | .NET Framework 1.0 |
| `v1.1.4322` | CLR 1.1 | .NET Framework 1.1 |
| `v2.0.50727` | CLR 2.0 | .NET Framework 2.0, 3.0, 3.5 |
| `v4.0.30319` | CLR 4.0 | .NET Framework 4.0 through 4.8.x |

.NET 2.0, 3.0, and 3.5 all share CLR v2.0 — the "3.0" and "3.5" releases added libraries on top of the same runtime. When the shim resolves to `v2.0.50727` or FOD offers to install "NetFx3", it's all about CLR v2.0.

## Shim HRESULT Codes

These HRESULTs are returned by the shim to callers. They don't appear in the activation logs themselves (which use human-readable messages), but knowing them helps connect caller-side errors to activation log analysis:

| HRESULT | Symbol | Meaning |
|---------|--------|---------|
| `0x80131700` | `CLR_E_SHIM_RUNTIMELOAD` | Cannot find or load a suitable runtime. The most common shim error — corresponds to "ERROR: Unable to find a version of the runtime to use" in logs. |
| `0x80131701` | `CLR_E_SHIM_RUNTIMEEXPORT` | Found a runtime but failed to get a required export or interface. |
| `0x80131702` | `CLR_E_SHIM_INSTALLROOT` | .NET Framework install root missing or invalid in registry. |
| `0x80131703` | `CLR_E_SHIM_INSTALLCOMP` | A required installation component is missing. |
| `0x80131704` | `CLR_E_SHIM_LEGACYRUNTIMEALREADYBOUND` | A different runtime is already bound as the legacy runtime — a legacy API tried to bind to a conflicting version. |
| `0x80131705` | `CLR_E_SHIM_SHUTDOWNINPROGRESS` | The shim is shutting down. |
