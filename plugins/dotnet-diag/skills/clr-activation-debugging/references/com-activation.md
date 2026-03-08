# COM Activation Through the CLR Shim — Reference

## How COM Objects End Up in mscoree.dll

When a COM object is implemented in managed code (or is a CLR-internal component like diasymreader), its `InprocServer32` registry entry points to `mscoree.dll`. When `CoCreateInstance` is called for such a CLSID, the OS loads mscoree.dll and calls its `DllGetClassObject`.

The shim then must determine:
1. Which CLR version should host this COM object
2. Whether the CLR is already loaded in this process
3. Whether to use legacy or modern activation

## CLSID Registry Layout

```
HKCR\CLSID\{guid}
│   (Default) = "Friendly Name"
│   MasterCLSID = "{other-guid}"    (optional, for redirected CLSIDs)
│
└── InprocServer32
    │   (Default) = "C:\Windows\System32\mscoree.dll"
    │   ThreadingModel = "Both"
    │
    ├── 2.0.50727                    (version subkey — CLR v2 registration)
    │   │   (Default) = "2.0.50727"
    │   │   Assembly = "FullAssemblyName"       (managed interop)
    │   │   Class = "Namespace.ClassName"       (managed interop)
    │   │   RuntimeVersion = "v2.0.50727"       (which CLR to load)
    │   └── ImplementedInThisVersion = ""       (native CLR component, not interop)
    │
    └── 4.0.30319                    (version subkey — CLR v4 registration)
        │   (Default) = "4.0.30319"
        │   Assembly = "FullAssemblyName"
        │   Class = "Namespace.ClassName"
        │   RuntimeVersion = "v4.0.30319"
        └── ImplementedInThisVersion = ""
```

### Key Registry Values

**`(Default)` under InprocServer32**: Must be `mscoree.dll` (or full path) for the shim to handle it.

**Version subkeys** (e.g., `2.0.50727`, `4.0.30319`): Each subkey represents a CLR version that registered this CLSID. The shim uses these to determine which runtime version(s) can host the object.

**`RuntimeVersion`**: The CLR version to load for this object. Read from the highest applicable version subkey.

**`Assembly` and `Class`**: For managed COM interop — the managed assembly and type that implement the COM object. If present, the CLR loads this assembly and creates the managed type.

**`ImplementedInThisVersion`**: When this value exists (even if empty), it marks the COM class as a **native CLR component** — something implemented inside the runtime itself (like the metadata APIs, symbol readers, etc.), not a managed interop object. The shim handles these differently from managed COM.

**`SupportedRuntimeVersions`** (directly under InprocServer32): Semicolon-delimited list of runtime versions. Acts like a `<supportedRuntime>` list in a config file. Example: `v2.0.50727;v4.0.30319`.

**`MasterCLSID`**: If present on the CLSID key, redirects to another CLSID for version lookup purposes. Used for COM objects that have been superseded.

## The DllGetClassObject Decision Flow

```
DllGetClassObject(rclsid, riid, ppv)
│
├─ 1. Is this an AppX process with a disallowed CLSID? → E_NOTIMPL
│
├─ 2. Is this CLSID cached in g_pCVMList?
│     └─ Yes → Call cached DllGetClassObject directly → done
│
├─ 3. Try LoadUnmanagedCOMObject() — handles non-CLR objects
│     └─ Success → done (no CLR involved)
│
├─ 4. Classify the CLSID:
│     ├─ IsClrHostedLegacyComObject()? → Legacy COM object
│     ├─ IsCLSIDImplementedInFrameworkAssembly()? → Framework assembly COM
│     └─ Neither → Modern managed COM
│
├─ 5. Determine activation path:
│     │
│     ├─ MODERN PATH (not legacy, not framework):
│     │   └─ GetClassObjectForManagedType()
│     │       ├─ Uses ICLRMetaHostPolicy with METAHOST_POLICY_HIGHCOMPAT
│     │       ├─ IsLegacyBind: 0, IsCapped: 0
│     │       └─ Config file + version subkeys guide runtime selection
│     │
│     └─ LEGACY PATH (legacy or framework COM):
│         └─ RequestRuntimeDll()
│             ├─ Sets IsLegacyBind: 1, IsCapped: 1
│             ├─ Sets fLatestVersion: TRUE (find latest within cap)
│             ├─ If g_pLegacyAPIRuntimeInfo already set → use it directly
│             └─ Otherwise → ComputeVersionString → FindLatestVersion (capped)
│
└─ 6. Load the CLR, get class factory, return object
```

## Why Native Build Tools Trigger COM Activation

Native tools like `link.exe`, `mt.exe`, `CL.exe` are not .NET applications, but they may use COM objects that are implemented inside the CLR:

- **Diasymreader** (`{E5CB7A31-7512-11D2-89CE-0080C792E5D8}`): Debug symbol writer/reader, used by tools that produce or consume PDB files
- **SymBinder** (`{0A29FF9E-7F9C-4437-8B11-F424491E3931}`): Symbol binder for debug information
- **ALink** (`{B79B0ACD-F5CD-409B-B5A5-A16244610B92}`): Assembly linker

These COM objects have `InprocServer32 = mscoree.dll` and go through the full shim activation path. Because they're activated via `DllGetClassObject` (not via a managed EXE launch), they take the **legacy COM path** with `IsCapped: 1`.

## The Legacy Runtime Binding Order Problem

The order of activations within a process determines behavior:

### Scenario A: Primary load first, COM activation second (common for link.exe, CL.exe)

```
1. ClrCreateInstance → ICLRMetaHostPolicy::GetRequestedRuntime
   → Config says supportedRuntime v4.0 → Loads v4.0 → Success
   → g_pLegacyAPIRuntimeInfo may or may not be set yet

2. DllGetClassObject for diasymreader
   → Legacy bind, IsCapped: 1
   → If g_pLegacyAPIRuntimeInfo is set to v4.0 → uses v4.0 → OK
   → If NOT set → FindLatestVersion (capped to v2.0) → fails on machines without 3.5
```

### Scenario B: COM activation is the ONLY activation (common for mt.exe)

```
1. DllGetClassObject for diasymreader
   → Legacy bind, IsCapped: 1
   → No legacy runtime bound yet
   → No config file
   → FindLatestVersion (capped to v2.0) → (null) → ERROR → FOD
```

### Scenario C: Legacy hosting API sets the runtime first

```
1. CorBindToRuntimeEx(v4.0) → binds g_pLegacyAPIRuntimeInfo to v4.0
2. DllGetClassObject for anything → legacy bind → uses g_pLegacyAPIRuntimeInfo (v4.0) → OK
```

## Diagnosing CLSID Issues

### Check the CLSID registration

```powershell
$clsid = '{E5CB7A31-7512-11D2-89CE-0080C792E5D8}'

# Main CLSID entry
Get-ItemProperty "Registry::HKCR\CLSID\$clsid" -ErrorAction SilentlyContinue

# InprocServer32 (should be mscoree.dll)
Get-ItemProperty "Registry::HKCR\CLSID\$clsid\InprocServer32" -ErrorAction SilentlyContinue

# Version subkeys — what runtimes registered this CLSID?
Get-ChildItem "Registry::HKCR\CLSID\$clsid\InprocServer32" -ErrorAction SilentlyContinue |
    ForEach-Object {
        Write-Output "`n--- $($_.PSChildName) ---"
        Get-ItemProperty "Registry::$($_.Name)" -ErrorAction SilentlyContinue
    }
```

### What to look for

| Observation | Meaning |
|-------------|---------|
| Only a `4.0.30319` subkey exists, no `2.0.50727` | .NET 3.5 was never installed (no v2 registration). Capped legacy binds will fail. |
| Both `2.0.50727` and `4.0.30319` subkeys exist | Both runtimes have registered this CLSID. Capped binds can use v2, uncapped can use v4. |
| `ImplementedInThisVersion` is present | This is a native CLR component (not managed interop). |
| `Assembly` and `Class` are present | This is a managed COM interop registration. |
| `SupportedRuntimeVersions` exists | Acts as an inline supportedRuntime list (semicolon-delimited). |
| `InprocServer32\(Default)` is NOT mscoree.dll | This CLSID is not CLR-hosted — shim is not involved. |

### Registration corruption from build races

If a build process registers/unregisters COM objects (e.g., via `regasm.exe`), concurrent registrations can corrupt CLSID entries:

- A version subkey may be deleted or partially written
- `Assembly`/`Class` values may be missing or point to the wrong type
- `RuntimeVersion` may be incorrect

Check for partial registrations by verifying that all expected values exist under each version subkey.
