# NativeAOT Crash Dump Collection

| Need | Tool | Platforms |
|------|------|-----------|
| Automatic dump on crash (preferred) | OS-level core dumps (`ulimit`, `core_pattern`, WER) | All |
| Automatic dump on crash (alternative) | `createdump` + `DOTNET_DbgEnableMiniDump` env vars | All |
| On-demand from running process | `gcore` (Linux), `lldb` (macOS), `procdump` (Windows) | Per-platform |

NativeAOT applications are native executables. Since NativeAOT only supports full dumps, OS-level core dump mechanisms are the simplest approach — no extra tooling to copy or configure.

> **Note:** NativeAOT only supports full dumps. `DOTNET_DbgMiniDumpType` must be set to `4` (Full).

## OS-Level Core Dumps (Preferred)

### Linux

**Option A: Direct core dumps (simplest)**

```bash
# Enable core dumps for the current shell session
ulimit -c unlimited

# Set the core dump output pattern (system-wide, requires root)
echo '/tmp/dumps/core.%e.%p.%t' | sudo tee /proc/sys/kernel/core_pattern

# Make persistent across reboots — add to /etc/sysctl.conf:
# kernel.core_pattern = /tmp/dumps/core.%e.%p.%t

# Ensure dump directory exists
mkdir -p /tmp/dumps
```

> **Note:** `ulimit` only applies to processes started in the current shell session. For an already-running process, use on-demand collection with `gcore` (see On-Demand Dump Collection below).

**Format specifiers for `core_pattern`:**
| Spec | Meaning |
|------|---------|
| `%p` | PID |
| `%e` | Executable name (first 15 chars) |
| `%t` | Unix timestamp |
| `%h` | Hostname |
| `%u` | UID |

**Option B: systemd-coredump (systemd systems)**

Many Linux distributions pipe core dumps to `systemd-coredump` by default. Check:

```bash
cat /proc/sys/kernel/core_pattern
# If it shows: |/usr/lib/systemd/systemd-coredump ...
# Then systemd-coredump is already handling dumps.
```

Using `coredumpctl`:

```bash
# List collected dumps
coredumpctl list

# Show details of the most recent dump
coredumpctl info

# Export a dump to a file
coredumpctl dump -o /tmp/dumps/myapp.core

# Filter by executable name
coredumpctl list myapp
coredumpctl dump myapp -o /tmp/dumps/myapp.core
```

Configure systemd-coredump storage in `/etc/systemd/coredump.conf`:
```ini
[Coredump]
Storage=external
MaxUse=2G
ProcessSizeMax=8G
```

### On-Demand Dump Collection

```bash
# Using gcore (from gdb package)
gcore -o /tmp/dumps/myapp <pid>
```

## macOS

### Automatic Crash Dumps

```bash
# Enable core dumps for the current shell session
ulimit -c unlimited

# Core dumps go to /cores/core.<pid>
# Ensure the directory exists and is writable
sudo mkdir -p /cores
sudo chmod 1777 /cores

# Verify the setting
ulimit -c  # Should print "unlimited"
```

**Notes:**
- macOS also generates `.crash` reports in `~/Library/Logs/DiagnosticReports/` automatically — these are text-based crash logs, not full memory dumps.
- On Apple Silicon, core dumps may require SIP (System Integrity Protection) adjustments for certain processes.

### Retrieving Dumps from an Already-Crashed Process

If the app has already crashed and core dumps were enabled (`ulimit -c unlimited` was set):

```bash
# Check /cores/ for core dumps
ls -la /cores/core.*

# Check macOS crash reports (always generated, even without ulimit)
ls -la ~/Library/Logs/DiagnosticReports/*.crash
# Or on newer macOS:
ls -la ~/Library/Logs/DiagnosticReports/*.ips
```

If core dumps were **not** enabled before the crash, the core dump is lost. The `.crash`/`.ips` report in `DiagnosticReports` is the only artifact — it contains the stack trace and crash reason but not full memory.

### On-Demand Dump Collection

```bash
# Using lldb (ships with Xcode command-line tools)
lldb -p <pid> -o "process save-core /tmp/dumps/myapp.core" -o "quit"

# Using gcore if gdb is installed (via Homebrew)
gcore -o /tmp/dumps/myapp <pid>
```

## Windows

### Automatic Crash Dumps (Windows Error Reporting)

WER is a Windows OS-level mechanism — it uses its own `DumpType` values (0=Custom, 1=Mini, 2=Full) which are separate from the `DOTNET_DbgMiniDumpType` environment variable. WER works for any process, including NativeAOT apps without `createdump`.

Configure via the registry. Run in an **elevated PowerShell**:

```powershell
# Enable local dumps for a specific application
$appName = "myapp.exe"
$dumpPath = "C:\dumps"
$regPath = "HKLM:\SOFTWARE\Microsoft\Windows\Windows Error Reporting\LocalDumps\$appName"

New-Item -Path $regPath -Force
New-ItemProperty -Path $regPath -Name "DumpFolder" -Value $dumpPath -PropertyType ExpandString -Force
New-ItemProperty -Path $regPath -Name "DumpCount" -Value 10 -PropertyType DWord -Force
New-ItemProperty -Path $regPath -Name "DumpType" -Value 2 -PropertyType DWord -Force
# DumpType: 0=Custom, 1=Mini, 2=Full

# Ensure dump directory exists
New-Item -Path $dumpPath -ItemType Directory -Force
```

**To enable for ALL applications** (not just one), use the parent key:
```powershell
$regPath = "HKLM:\SOFTWARE\Microsoft\Windows\Windows Error Reporting\LocalDumps"
New-Item -Path $regPath -Force
New-ItemProperty -Path $regPath -Name "DumpFolder" -Value "C:\dumps" -PropertyType ExpandString -Force
New-ItemProperty -Path $regPath -Name "DumpType" -Value 2 -PropertyType DWord -Force
```

### On-Demand Dump Collection

**Using procdump (recommended — download from Sysinternals):**

```powershell
# Download procdump (one-time)
Invoke-WebRequest -Uri "https://download.sysinternals.com/files/Procdump.zip" -OutFile "$env:TEMP\Procdump.zip"
Expand-Archive "$env:TEMP\Procdump.zip" -DestinationPath "$env:TEMP\Procdump" -Force

# Capture a full dump
& "$env:TEMP\Procdump\procdump.exe" -ma <pid> C:\dumps\myapp.dmp

# Capture on crash (waits for an unhandled exception)
& "$env:TEMP\Procdump\procdump.exe" -ma -e -w <processname> C:\dumps\myapp.dmp
```

**Using Task Manager:**
1. Open Task Manager → Details tab
2. Right-click the process → "Create memory dump file"
3. Note the output path shown in the dialog

## Alternative: Using createdump with DOTNET_DbgEnableMiniDump

NativeAOT apps also support the `DOTNET_DbgEnableMiniDump` environment variables if `createdump` is available. This gives you the same env-var-based workflow as CoreCLR, but requires copying `createdump` to the right location.

If `createdump` is placed **next to the application binary**:

```bash
export DOTNET_DbgEnableMiniDump=1
export DOTNET_DbgMiniDumpType=4
export DOTNET_DbgMiniDumpName="/tmp/dumps/%e_%p_%t.dmp"
export DOTNET_EnableCrashReport=1
```

> **Note:** The `%e`, `%p`, `%h`, `%t` format specifiers in `DOTNET_DbgMiniDumpName` require .NET 7+. On .NET 6, use a literal path instead.

**Setup:** Copy `createdump` from the .NET runtime into the same directory as your published NativeAOT binary:

```bash
# Linux
cp /usr/share/dotnet/shared/Microsoft.NETCore.App/<version>/createdump ./publish/

# macOS
cp /usr/local/share/dotnet/shared/Microsoft.NETCore.App/<version>/createdump ./publish/

# Windows (PowerShell)
Copy-Item "C:\Program Files\dotnet\shared\Microsoft.NETCore.App\<version>\createdump.exe" .\publish\
```

**If the .NET runtime is not installed** (fully self-contained deployment), extract `createdump` from the runtime Docker image or NuGet package:

```bash
# Extract from Docker runtime image
docker run --rm -v "$(pwd)/publish:/out" mcr.microsoft.com/dotnet/runtime:10.0 \
  sh -c 'cp /usr/share/dotnet/shared/Microsoft.NETCore.App/*/createdump /out/'
```

### .NET 11+: DOTNET_DbgCreateDumpToolPath

Starting with .NET 11, you can point to `createdump` at any location instead of requiring it next to the binary:

```bash
export DOTNET_DbgCreateDumpToolPath=/opt/tools/createdump
export DOTNET_DbgEnableMiniDump=1
export DOTNET_DbgMiniDumpType=4
export DOTNET_DbgMiniDumpName="/tmp/dumps/%e_%p_%t.dmp"
```

This is especially useful in containers where you can install `createdump` once in a shared location.

## Verification

### When using OS-level core dumps

```bash
# Linux — check core_pattern and ulimit
cat /proc/sys/kernel/core_pattern
ulimit -c

# macOS — check ulimit and /cores/
ulimit -c
ls -la /cores/

# Linux (systemd) — check for recent dumps
coredumpctl list --no-pager | tail -5
```

```powershell
# Windows — check WER registry
Get-ChildItem "HKLM:\SOFTWARE\Microsoft\Windows\Windows Error Reporting\LocalDumps" -ErrorAction SilentlyContinue
Get-ChildItem "C:\dumps" -ErrorAction SilentlyContinue
```

### When using createdump + DOTNET_DbgEnableMiniDump

```bash
# Verify env vars are set
env | grep DOTNET_Dbg
env | grep DOTNET_EnableCrashReport

# Verify createdump is next to the binary (or at DOTNET_DbgCreateDumpToolPath)
ls -la ./createdump        # co-located
# or: ls -la $DOTNET_DbgCreateDumpToolPath   # .NET 11+

# Verify dump directory exists
ls -la /tmp/dumps/
```
