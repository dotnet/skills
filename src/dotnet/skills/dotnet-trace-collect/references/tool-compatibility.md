# Tool Compatibility Reference

Detailed command-line usage, platform compatibility, and trade-offs for each diagnostic tool covered by the `dotnet-trace-collect` skill.

## dotnet-counters

**Purpose**: Live monitoring of .NET runtime and application metrics.

| Attribute | Value |
|-----------|-------|
| OS | Windows, Linux, macOS |
| Runtime | Modern .NET (.NET Core 3.0+) |
| .NET Framework | ❌ Not supported |
| Admin required | No |
| Container | Works inside container; connect from outside via diagnostic port |

### Installation

```bash
dotnet tool install -g dotnet-counters
```

### Common Commands

```bash
# List running .NET processes
dotnet-counters ps

# Monitor default counters for a process
dotnet-counters monitor -p <PID>

# Monitor specific counter providers
dotnet-counters monitor -p <PID> --counters System.Runtime,Microsoft.AspNetCore.Hosting

# Collect counters to a file (CSV or JSON)
dotnet-counters collect -p <PID> --format csv -o counters.csv

# Monitor by process name
dotnet-counters monitor -n <process-name>
```

### Container Usage

Inside a container, `dotnet-counters` works normally if installed in the image. From outside, connect via diagnostic port:

```bash
# Inside container
dotnet-counters monitor -p 1

# Outside via dotnet-monitor (preferred — see dotnet-monitor section)
```

---

## dotnet-trace

**Purpose**: Collect EventPipe traces for CPU profiling, event analysis, and runtime diagnostics.

| Attribute | Value |
|-----------|-------|
| OS | Windows, Linux, macOS |
| Runtime | Modern .NET (.NET Core 3.0+) |
| .NET Framework | ❌ Not supported |
| Admin required | No |
| Container | Works inside container; use `--diagnostic-port` for sidecar |

### Installation

```bash
dotnet tool install -g dotnet-trace
```

### Common Commands

```bash
# List running .NET processes
dotnet-trace ps

# Collect a trace with the default profile (CPU sampling)
dotnet-trace collect -p <PID>

# Collect with a specific profile
dotnet-trace collect -p <PID> --profile cpu-sampling
dotnet-trace collect -p <PID> --profile gc-verbose

# Collect with specific providers
dotnet-trace collect -p <PID> --providers Microsoft-DotNETCore-SampleProfiler,Microsoft-Windows-DotNETRuntime

# Collect for a fixed duration (seconds)
dotnet-trace collect -p <PID> --duration 00:00:30

# Output in Speedscope format for web-based viewing
dotnet-trace collect -p <PID> --format speedscope
```

### Output Formats

| Format | Extension | Analysis Tool |
|--------|-----------|---------------|
| `nettrace` (default) | `.nettrace` | PerfView, Visual Studio, `dotnet-trace report` |
| `speedscope` | `.speedscope.json` | [Speedscope](https://www.speedscope.app/) (web) |
| `chromium` | `.chromium.json` | Chrome DevTools (chrome://tracing) |

### Container Usage

```bash
# Inside container (process is typically PID 1)
dotnet-trace collect -p 1 -o /tmp/trace.nettrace

# Copy trace out of container
kubectl cp <pod>:/tmp/trace.nettrace ./trace.nettrace
# or
docker cp <container>:/tmp/trace.nettrace ./trace.nettrace
```

### Trade-offs

- ✅ Cross-platform, no admin needed, lightweight
- ❌ No native (unmanaged) call stacks — only managed frames
- ❌ Less system-level detail than ETW (PerfView) or perf (perfcollect)

---

## dotnet-trace collect-linux (.NET 10+)

**Purpose**: Collects diagnostic traces using `perf_events`, a Linux OS technology. Provides native call stacks, kernel events, and machine-wide tracing that standard `dotnet-trace collect` cannot.

| Attribute | Value |
|-----------|-------|
| OS | Linux only (kernel >= 6.4 with `CONFIG_USER_EVENTS=y`) |
| Runtime | .NET 10+ only |
| .NET Framework | ❌ Not supported |
| Admin required | Yes (root) |
| Container | Works inside container (requires root) |
| glibc | >= 2.35 (not supported on Alpine 3.22, CentOS Stream 9, or RHEL 9-based distros) |

### Key Differences from `dotnet-trace collect`

| Feature | `collect` | `collect-linux` |
|---------|-----------|-----------------|
| Trace all processes simultaneously | No | Yes (default — no PID required) |
| Capture native library and kernel events | No | Yes |
| Event callstacks include native frames | No | Yes |
| Requires admin/root | No | Yes |

### Usage

```bash
# Machine-wide trace (all processes, no PID needed)
sudo dotnet-trace collect-linux

# Trace a specific process
sudo dotnet-trace collect-linux -p <PID>

# Trace a process by name
sudo dotnet-trace collect-linux -n <process-name>

# Trace for a specific duration
sudo dotnet-trace collect-linux --duration 00:00:30

# Trace with specific providers
sudo dotnet-trace collect-linux --providers Microsoft-Windows-DotNETRuntime

# Trace with specific profiles
sudo dotnet-trace collect-linux --profile gc-verbose
sudo dotnet-trace collect-linux --profile thread-time

# Trace with additional Linux perf events
sudo dotnet-trace collect-linux --perf-events <list-of-perf-events>

# Output to a specific file
sudo dotnet-trace collect-linux -o /tmp/trace.nettrace
```

When `--providers`, `--profile`, `--clrevents`, and `--perf-events` are not specified, `collect-linux` enables these profiles by default: `dotnet-common` (lightweight .NET runtime diagnostics) and `cpu-sampling` (kernel CPU sampling).

### Container Usage

```bash
# Inside container (machine-wide — captures all processes in the container)
sudo dotnet-trace collect-linux -o /tmp/trace.nettrace

# Inside container (specific process)
sudo dotnet-trace collect-linux -p 1 -o /tmp/trace.nettrace

# Copy trace out
kubectl cp <pod>:/tmp/trace.nettrace ./trace.nettrace
```

### Trade-offs

- ✅ Machine-wide tracing without specifying a PID
- ✅ Native (unmanaged) call stacks — essential for diagnosing native interop or runtime issues
- ✅ Captures kernel events and native library events
- ✅ Designed for Linux-first workflows
- ❌ Requires root privileges
- ❌ Only available on .NET 10 and later
- ❌ Linux only (kernel >= 6.4)
- ❌ Requires glibc >= 2.35 (not all supported Linux distros qualify)

---

## dotnet-dump

**Purpose**: Collect and perform basic analysis of process memory dumps.

| Attribute | Value |
|-----------|-------|
| OS | Windows, Linux, macOS |
| Runtime | Modern .NET (.NET Core 3.0+) |
| .NET Framework | ❌ Not supported (use `procdump` or Task Manager on Windows for .NET Framework dumps) |
| Admin required | No. Dump collection does not require admin privileges. On Linux, `ptrace` permissions may be needed in containers. |
| Container | Works inside container |

### Installation

```bash
dotnet tool install -g dotnet-dump
```

### Common Commands

```bash
# Collect a dump
dotnet-dump collect -p <PID>

# Collect a dump with specific type
dotnet-dump collect -p <PID> --type Full
dotnet-dump collect -p <PID> --type Heap
dotnet-dump collect -p <PID> --type Mini

# Analyze a dump (interactive)
dotnet-dump analyze <dump-file>

# Common SOS (Son of Strike) debugging extension commands.
# These work in dotnet-dump analyze (which hosts SOS), WinDbg, and lldb.
#   In WinDbg, prefix with '!' (e.g., !dumpheap -stat).
#   In lldb, prefix with 'sos' (e.g., sos dumpheap -stat) or use directly after loading the SOS plugin.
#
#   dumpheap -stat          — heap statistics
#   dumpheap -type <Type>   — objects of a specific type
#   gcroot <address>        — find GC roots holding an object
#   dso                     — dump stack objects
#   eeheap -gc              — GC heap summary
#   clrstack                — managed call stacks (useful for hangs)

```

### Container Usage

```bash
# Inside container
dotnet-dump collect -p 1 -o /tmp/dump.dmp

# Copy dump out
kubectl cp <pod>:/tmp/dump.dmp ./dump.dmp
```

### Analyzing Dumps

Dumps collected on Linux can be analyzed locally or copied to Windows for analysis:

- **On Linux**: `dotnet-dump analyze <dump-file>` for interactive analysis, or `lldb` with the SOS extension for full debugger capabilities
- **On Windows**: Open the dump in Visual Studio, PerfView, or WinDbg with the SOS extension for GC heap inspection, object graphs, and richer UI-based analysis. WinDbg can also open dumps collected on Linux.

The commands listed above (`dumpheap`, `gcroot`, `dso`, `eeheap`) are **SOS extension commands** available within `dotnet-dump analyze` and within debuggers that load the SOS extension (WinDbg on Windows, `lldb` on Linux).

**For hangs**: Use the `clrstack` SOS command (in `dotnet-dump analyze` or a debugger) to inspect managed call stacks of all threads and identify where threads are blocked. For **deadlocks** (threads waiting on each other), a dump alone is usually sufficient. For **livelocks** (threads spinning without progress), a trace is also helpful to see what threads are doing over time.

**For memory leaks**: Capture **two dumps** as memory is increasing (e.g., one early, one after significant growth). Open both in PerfView and use its diff capabilities to see which object types have increased — this is the most effective way to identify what is leaking. Also capture a trace **while memory is growing** to see what is being allocated — no trigger is needed, just run the collection during the growth period. Do not wait for an `OutOfMemoryException`.

This is especially useful for GC heap analysis — Visual Studio and PerfView provide graphical views of heap contents that are easier to navigate than the `dotnet-dump` command line.

### Linux ptrace Permissions

On Linux, `dotnet-dump` needs `ptrace` permissions. In containers, you may need:

```bash
# Docker
docker run --cap-add=SYS_PTRACE ...

# Kubernetes (in securityContext)
# securityContext:
#   capabilities:
#     add: ["SYS_PTRACE"]
```

---

## dotnet-monitor

**Purpose**: REST API-based diagnostics collection, designed for container and Kubernetes environments.

| Attribute | Value |
|-----------|-------|
| OS | Windows, Linux, macOS |
| Runtime | Modern .NET (.NET Core 3.1+) |
| .NET Framework | ❌ Not supported |
| Admin required | No |
| Container | ✅ Designed for containers/K8s — runs as a sidecar |

### Installation

```bash
# As a global tool
dotnet tool install -g dotnet-monitor

# As a container image (for K8s sidecar)
# mcr.microsoft.com/dotnet/monitor
```

### Common Commands

```bash
# Production-safe default: keep auth enabled (default) and bind to loopback
dotnet-monitor collect --urls http://127.0.0.1:52323 --metricUrls http://127.0.0.1:52325

# Dev-only shortcut in isolated environments (avoid in production)
dotnet-monitor collect --urls http://127.0.0.1:52323 --no-auth
```

### Security Guidance (Production)

- Do **not** run `dotnet-monitor` with `--no-auth` on production workloads.
- Bind to localhost (`127.0.0.1`) and access through `kubectl port-forward` (or another local-only tunnel).
- If remote access is required, keep auth enabled and restrict network exposure to trusted operators.

### REST API Endpoints

```bash
# Access via local tunnel (recommended in Kubernetes)
kubectl port-forward pod/<pod-name> 52323:52323

# List processes (auth enabled by default)
curl -H "Authorization: Bearer <monitor-token>" http://localhost:52323/processes

# Collect a trace
curl -H "Authorization: Bearer <monitor-token>" -o trace.nettrace http://localhost:52323/trace?pid=<PID>&durationSeconds=30

# Collect a dump
curl -H "Authorization: Bearer <monitor-token>" -o dump.dmp http://localhost:52323/dump?pid=<PID>&type=Full

# Collect GC dump
curl -H "Authorization: Bearer <monitor-token>" -o gcdump.gcdump http://localhost:52323/gcdump?pid=<PID>

# Get live metrics
curl -H "Authorization: Bearer <monitor-token>" http://localhost:52323/livemetrics?pid=<PID>
```

### Kubernetes Sidecar Setup

```yaml
# Pod spec with dotnet-monitor as sidecar
spec:
  containers:
  - name: app
    image: myapp:latest
    volumeMounts:
    - name: diag
      mountPath: /tmp
  - name: monitor
    image: mcr.microsoft.com/dotnet/monitor:latest
    args: ["collect", "--urls", "http://127.0.0.1:52323", "--metricUrls", "http://127.0.0.1:52325"]
    volumeMounts:
    - name: diag
      mountPath: /tmp
    ports:
    - containerPort: 52323
  volumes:
  - name: diag
    emptyDir: {}
```

The shared `/tmp` volume allows `dotnet-monitor` to access the app's diagnostic Unix domain socket.
Keep auth enabled (default), and access the API via `kubectl port-forward` rather than exposing a public Service unless you have explicit authentication and network controls in place.

### Trade-offs

- ✅ Purpose-built for containers and Kubernetes
- ✅ No tools needed inside the app container
- ✅ REST API is easy to automate and integrate
- ❌ Requires sidecar setup and shared volume
- ❌ Additional resource overhead from sidecar container

---

## PerfView

**Purpose**: ETW-based tracing on Windows. The richest diagnostic tool for .NET on Windows.

| Attribute | Value |
|-----------|-------|
| OS | Windows only |
| Runtime | .NET Framework ✅, Modern .NET ✅ |
| Admin required | Yes |
| Container | ✅ Windows containers (Hyper-V and process-isolation) |

### Installation

Download from [https://github.com/microsoft/perfview/releases](https://github.com/microsoft/perfview/releases). PerfView is a standalone `.exe` — no installation required.

### Common Commands

Always include `/BufferSizeMB:1024 /CircularMB:2048` for short traces to ensure adequate buffer space.

```powershell
# Collect a CPU trace (default providers)
PerfView collect /BufferSizeMB:1024 /CircularMB:2048

# Collect for slow request / latency investigation (ThreadTime adds thread-level wait/block detail)
PerfView /ThreadTime collect /BufferSizeMB:1024 /CircularMB:2048

# Collect with GC and allocation events
PerfView collect /GCCollectOnly /BufferSizeMB:1024 /CircularMB:2048

# Collect with specific providers
PerfView collect /Providers:Microsoft-Windows-DotNETRuntime /BufferSizeMB:1024 /CircularMB:2048

# Collect for a specific duration (seconds)
PerfView collect /MaxCollectSec:30 /BufferSizeMB:1024 /CircularMB:2048

# Collect from command line without GUI
PerfView /nogui collect /MaxCollectSec:30 /BufferSizeMB:1024 /CircularMB:2048
```

### Trigger Arguments for Long-Running Repros

When the issue takes a long time to reproduce, use trigger arguments with a circular buffer to capture the issue without generating huge trace files.

**Important**: The `/StopOn` trigger should fire on the **symptom you want to capture** — not on the recovery. PerfView uses a circular buffer (`/CircularMB`) that continuously overwrites old data, so the most recent data before the stop trigger fires is what gets preserved. When a `/StopOn` trigger fires, PerfView checks the condition a few times over several seconds before actually stopping, so the trigger event and surrounding context are reliably captured.

Use `/StartOn` only when you know the start event happens **before** the stop event (e.g., to avoid recording idle time before the issue begins). If in doubt, omit `/StartOn` and just use `/StopOn` with a circular buffer.

**Note**: For **slow requests**, do not include a stop trigger by default — the right trigger depends on the specific scenario. Provide the collection command without a trigger and let the user design one based on what they're seeing.

```powershell
# Stop when CPU spikes — captures the high-CPU window
PerfView collect /StopOnPerfCounter:"Processor:% Processor Time:_Total>80" /BufferSizeMB:2048 /CircularMB:4096

# Stop on a GC event (e.g. Gen2 collection)
PerfView collect /StopOnGCEvent /BufferSizeMB:2048 /CircularMB:4096

# Stop when a specific exception is thrown (not recommended for memory leaks — capture during growth instead)
PerfView collect /StopOnException:"System.OutOfMemoryException" /BufferSizeMB:2048 /CircularMB:4096

# StartOn + StopOn — only when the start event is known to precede the stop event
# Here: start recording when CPU goes above 50%, stop when the spike hits 90%
PerfView collect /StartOnPerfCounter:"Processor:% Processor Time:_Total>50" /StopOnPerfCounter:"Processor:% Processor Time:_Total>90" /BufferSizeMB:2048 /CircularMB:4096
```

Key trigger parameters:

| Parameter | Description |
|-----------|-------------|
| `/StopOnPerfCounter:"Category:Counter:Instance>Threshold"` | Stop when a performance counter crosses a threshold. **Use this on the symptom you want to capture.** |
| `/StartOnPerfCounter:"Category:Counter:Instance>Threshold"` | Start collection when a counter crosses a threshold. Only use when you know this fires before the stop trigger. |
| `/StopOnGCEvent` | Stop when a GC event occurs |
| `/StopOnException:"ExceptionType"` | Stop when a specific exception is thrown |
| `/BufferSizeMB:N` | In-memory buffer size (increase for long collections) |
| `/CircularMB:N` | Circular log size on disk (keeps only the last N MB of data) |

### Windows Container Usage

PerfView works with both types of Windows containers. Most Windows containers (including Kubernetes on Windows) use **process-isolation** by default.

> **PerfView inside containers**: `PerfView.exe` does not run in many slimmed-down Windows container images. To run PerfView inside a container (needed for the merge step in process-isolation, or for direct collection in Hyper-V), build **PerfViewCollect** from [https://github.com/microsoft/perfview](https://github.com/microsoft/perfview) as a self-contained publish, then copy the output binaries into the container.

#### Process-isolation containers (default)

Process-isolation containers share the host kernel. Collect from **outside** the container (on the host) using `/EnableEventsInContainers`:

```powershell
# On the host — captures all processes including those inside containers
PerfView collect /EnableEventsInContainers /MaxCollectSec:30 /BufferSizeMB:1024 /CircularMB:2048
```

The resulting trace can be analyzed immediately on the host while the container is still running — PerfView can reach into the container to fetch binaries for symbol resolution.

**To analyze on another machine**, you must complete a merge step inside the container **before the container shuts down**. Without this, binaries inside the container won't have their symbol lookup information saved in the trace:

```powershell
# 1. Copy the .etl.zip into the container
docker cp trace.etl.zip <container>:C:\trace.etl.zip

# 2. Inside the container — complete the merge to embed symbol info
PerfViewCollect merge /ImageIDsOnly C:\trace.etl.zip

# 3. Copy the merged trace back out
docker cp <container>:C:\trace.etl.zip ./trace-merged.etl.zip
```

The merged trace can now be analyzed on any machine with full symbol resolution. If you skip the in-container merge step, symbols for binaries that live only inside the container will be unresolvable on other machines because the Windows merge component cannot reach into the container's filesystem.

#### Hyper-V containers

Hyper-V containers are less common and are effectively lightweight VMs. Collect traces from **inside** the container the same way you would on a regular machine:

```powershell
# Inside the Hyper-V container
PerfView collect /MaxCollectSec:30 /BufferSizeMB:1024 /CircularMB:2048
```

### Trade-offs

- ✅ Richest diagnostic data available on Windows (ETW kernel + CLR providers)
- ✅ Works with both .NET Framework and modern .NET
- ✅ Powerful trigger system for capturing hard-to-reproduce issues
- ✅ Works with Windows containers (Hyper-V and process-isolation)
- ❌ Windows only
- ❌ Requires admin privileges
- ⚠️ **For .NET Framework without admin**: PerfView is the only trace tool for .NET Framework, and it requires admin. Without admin, there is **no way to investigate high CPU, slow requests, or excessive GC** on .NET Framework. Process dumps can still be captured (via `procdump` or Task Manager) for hangs and memory leaks, but trace-based investigation requires admin access.

---

## perfcollect

**Purpose**: Wrapper script for `perf` and `LTTng` on Linux. Collects CPU profiles with native call stacks.

| Attribute | Value |
|-----------|-------|
| OS | Linux only |
| Runtime | Modern .NET (.NET Core 2.0+) |
| .NET Framework | ❌ Not supported |
| Admin required | Yes (root) |
| Container | Needs `SYS_ADMIN` / `--privileged` |

### Installation

```bash
# Download the script
curl -OL https://aka.ms/perfcollect
chmod +x perfcollect

# Install prerequisites (perf, LTTng)
sudo ./perfcollect install
```

### Common Commands

```bash
# Collect a trace (runs until Ctrl+C)
sudo ./perfcollect collect mytrace

# Collect for a specific duration
sudo timeout 30 ./perfcollect collect mytrace
```

### Container Usage

```bash
# Docker — run with privileged mode
docker run --privileged ...
```

### Kubernetes Usage

In Kubernetes, use a **diagnostics sidecar container** to avoid modifying your application image. The sidecar runs alongside your app, shares its process namespace and `/tmp` directory, and contains the diagnostic tools.

#### Step 1: Add a privileged diagnostics sidecar

Add a diagnostics container to your deployment and mark it as privileged:

```yaml
      - name: diagnostics-container
        image: ubuntu
        command: ["/bin/sh", "-c", "sleep infinity"]
        securityContext:
          privileged: true
          allowPrivilegeEscalation: true
        volumeMounts:
        - name: shared-tmp
          mountPath: /tmp
```

#### Step 2: Enable shared process namespace

Set `shareProcessNamespace: true` in the pod spec so the sidecar can see and profile the app's processes:

```yaml
    spec:
      shareProcessNamespace: true
```

#### Step 3: Share /tmp between containers

Create an `emptyDir` volume mounted at `/tmp` in both the app and sidecar containers. This is needed for perf map files (`perf-$pid.map`), perfcollect logs, and other profiling artifacts:

```yaml
      volumes:
      - name: shared-tmp
        emptyDir: {}
```

Mount it in both containers:

```yaml
        volumeMounts:
        - name: shared-tmp
          mountPath: /tmp
```

#### Step 4: Set environment variables in the app container

Enable perf map generation and LTTng event logging in the application container:

```yaml
        env:
        - name: COMPlus_PerfMapEnabled
          value: "1"
        - name: COMPlus_EnableEventLog
          value: "1"
```

#### Step 5: Collect the trace

Connect to the cluster node, exec into the diagnostics sidecar, install perfcollect, and capture:

```bash
# Exec into the diagnostics sidecar
kubectl exec -it <pod-name> -c diagnostics-container -- /bin/bash

# Inside the sidecar: install perfcollect
curl -OL https://aka.ms/perfcollect
chmod +x perfcollect
./perfcollect install

# Capture a trace
./perfcollect collect mytrace
```

This produces `mytrace.trace.zip`, which can be copied out and analyzed.

#### End-to-end Kubernetes deployment example

```yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: sample
spec:
  replicas: 1
  selector:
    matchLabels:
      app: sample
  template:
    metadata:
      labels:
        app: sample
    spec:
      shareProcessNamespace: true
      volumes:
      - name: shared-tmp
        emptyDir: {}
      containers:
      - name: sample-app
        image: myapp:latest
        ports:
        - containerPort: 80
        env:
        - name: COMPlus_PerfMapEnabled
          value: "1"
        - name: COMPlus_EnableEventLog
          value: "1"
        volumeMounts:
        - name: shared-tmp
          mountPath: /tmp
      - name: diagnostics-container
        image: ubuntu
        command: ["/bin/sh", "-c", "sleep infinity"]
        securityContext:
          privileged: true
          allowPrivilegeEscalation: true
        volumeMounts:
        - name: shared-tmp
          mountPath: /tmp
```

### Analyzing perfcollect Output

The output is a `*.trace.zip` file containing `perf.data.nl`. To analyze:

1. Copy the `.trace.zip` to a Windows machine
2. Open with PerfView — it can read perfcollect output and display flame graphs

### Trade-offs

- ✅ Captures native (unmanaged) call stacks — essential for diagnosing native interop or runtime issues
- ✅ Uses kernel-level `perf` for accurate CPU profiling
- ❌ Requires root/admin privileges
- ❌ Linux only
- ❌ In containers, requires `SYS_ADMIN` or `--privileged`
