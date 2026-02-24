# CoreCLR Crash Dump Collection

| Need | Tool | Platforms |
|------|------|-----------|
| Automatic dump on crash | `DOTNET_DbgEnableMiniDump` env vars | All |
| On-demand from running process | `dotnet-dump collect` (recommended) | All |
| On-demand without installing tools | `createdump` (ships with runtime) | All |
| On-demand via OS tools | `gcore` (Linux) | Linux |

## Automatic Crash Dumps (All Platforms)

CoreCLR has built-in crash dump support via environment variables. Set these before launching the app:

```bash
# Enable crash dumps (required)
export DOTNET_DbgEnableMiniDump=1

# Dump type: 1=Mini, 2=Heap, 3=Triage, 4=Full
# Use 4 (Full) for maximum diagnostic value, 1 (Mini) for smaller files
export DOTNET_DbgMiniDumpType=4

# Output path — supports format specifiers (.NET 7+):
#   %p = PID, %e = process name, %h = hostname, %t = timestamp
export DOTNET_DbgMiniDumpName=/tmp/dumps/%e_%p_%t.dmp

# Optional: generate a JSON crash report alongside the dump
export DOTNET_EnableCrashReport=1

# Optional: diagnostics if dump creation itself fails
export DOTNET_CreateDumpDiagnostics=1
```

**On Windows (PowerShell):**
```powershell
$env:DOTNET_DbgEnableMiniDump = "1"
$env:DOTNET_DbgMiniDumpType = "4"
$env:DOTNET_DbgMiniDumpName = "C:\dumps\%e_%p_%t.dmp"
$env:DOTNET_EnableCrashReport = "1"
```

### Dump Type Reference

| Value | Type | Size | Use When |
|-------|------|------|----------|
| 1 | Mini | Small | Stack traces only, minimal disk usage |
| 2 | Heap | Large | Need to inspect managed heap objects |
| 3 | Triage | Small | Quick triage, limited data |
| 4 | Full | Largest | Full process memory, maximum diagnostic value |

### Important Notes

- The dump output directory must exist before the crash — CoreCLR will not create it.
- Format specifiers (`%p`, `%e`, `%h`, `%t`) require .NET 7+. On .NET 6, use a literal path.
- The legacy `COMPlus_` prefix (e.g., `COMPlus_DbgEnableMiniDump`) still works but `DOTNET_` is preferred for .NET 6+.
- Single-file published apps only support full dumps (`DOTNET_DbgMiniDumpType=4`), same as NativeAOT.

## On-Demand Dump Collection

### Using dotnet-dump (Recommended)

```bash
# Install (one-time)
dotnet tool install -g dotnet-dump

# List .NET processes
dotnet-dump ps

# Collect a dump from a running process
dotnet-dump collect -p <pid>

# Specify dump type and output path
dotnet-dump collect -p <pid> --type Full --output /tmp/dumps/myapp.dmp
```

**Supported `--type` values:** `Full`, `Heap`, `Mini`

### Using createdump

`createdump` ships with the .NET runtime and works without installing additional tools.

```bash
# Find createdump location
CREATEDUMP=$(find /usr/share/dotnet/shared/Microsoft.NETCore.App/ -name createdump | sort -V | tail -1)

# Collect a dump
$CREATEDUMP <pid>

# With options
$CREATEDUMP --full <pid>              # Full dump
$CREATEDUMP --withheap <pid>          # Heap dump (default)
$CREATEDUMP --normal <pid>            # Mini dump
$CREATEDUMP --triage <pid>            # Triage dump
$CREATEDUMP -f /tmp/dump.dmp <pid>    # Specify output path
```

**On Windows**, `createdump.exe` is in the runtime directory:
```powershell
$createdump = Get-ChildItem "C:\Program Files\dotnet\shared\Microsoft.NETCore.App" -Recurse -Filter "createdump.exe" | Sort-Object FullName | Select-Object -Last 1
& $createdump.FullName <pid>
```

**On macOS**, `createdump` is in the runtime directory:
```bash
CREATEDUMP=$(find /usr/local/share/dotnet/shared/Microsoft.NETCore.App/ -name createdump | sort -V | tail -1)
$CREATEDUMP <pid>
```

### Using gcore (Linux Only)

```bash
# Requires gdb installed
gcore -o /tmp/dumps/myapp <pid>
# Produces /tmp/dumps/myapp.<pid>
```

## Verification

After enabling crash dumps, verify the configuration:

```bash
# Check env vars are set
env | grep DOTNET_Dbg
env | grep DOTNET_EnableCrashReport

# Ensure dump directory exists and is writable
ls -la /tmp/dumps/

# After a crash, check for dump files
ls -la /tmp/dumps/*.dmp
```
