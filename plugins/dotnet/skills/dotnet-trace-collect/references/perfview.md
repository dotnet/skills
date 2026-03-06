# PerfView

**Purpose**: ETW-based tracing on Windows. The richest diagnostic tool for .NET on Windows.

| Attribute | Value |
|-----------|-------|
| OS | Windows only |
| Runtime | .NET Framework ✅, Modern .NET ✅ |
| Admin required | Yes |
| Container | ✅ Windows containers (Hyper-V and process-isolation) |

## Installation

Download from [https://github.com/microsoft/perfview/releases](https://github.com/microsoft/perfview/releases). PerfView is a standalone `.exe` — no installation required.

## Common Commands

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

# Collect with networking providers (HTTP status codes, DNS, TLS, sockets)
PerfView /ThreadTime collect /BufferSizeMB:1024 /CircularMB:2048 /Providers:*System.Net.Http,*System.Net.NameResolution,*System.Net.Security,*System.Net.Sockets

# Collect with Kestrel/ASP.NET providers for slow inbound requests
PerfView /ThreadTime collect /BufferSizeMB:1024 /CircularMB:2048 /Providers:*Microsoft.AspNetCore.Hosting,*Microsoft-AspNetCore-Server-Kestrel

# Collect for a specific duration (seconds)
PerfView collect /MaxCollectSec:30 /BufferSizeMB:1024 /CircularMB:2048

# Collect from command line without GUI
PerfView /nogui collect /MaxCollectSec:30 /BufferSizeMB:1024 /CircularMB:2048
```

## Trigger Arguments for Long-Running Repros

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

## Windows Container Usage

PerfView works with both types of Windows containers. Most Windows containers (including Kubernetes on Windows) use **process-isolation** by default.

> **PerfView inside containers**: `PerfView.exe` does not run in many slimmed-down Windows container images. To run PerfView inside a container (needed for the merge step in process-isolation, or for direct collection in Hyper-V), build **PerfViewCollect** from [https://github.com/microsoft/perfview](https://github.com/microsoft/perfview) as a self-contained publish, then copy the output binaries into the container.

### Process-isolation containers (default)

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

### Hyper-V containers

Hyper-V containers are less common and are effectively lightweight VMs. Collect traces from **inside** the container the same way you would on a regular machine:

```powershell
# Inside the Hyper-V container
PerfView collect /MaxCollectSec:30 /BufferSizeMB:1024 /CircularMB:2048
```

## Trade-offs

- ✅ Richest diagnostic data available on Windows (ETW kernel + CLR providers)
- ✅ Works with both .NET Framework and modern .NET
- ✅ Powerful trigger system for capturing hard-to-reproduce issues
- ✅ Works with Windows containers (Hyper-V and process-isolation)
- ❌ Windows only
- ❌ Requires admin privileges
- ⚠️ **For .NET Framework without admin**: PerfView is the only trace tool for .NET Framework, and it requires admin. Without admin, there is **no way to investigate high CPU, slow requests, or excessive GC** on .NET Framework. Dumps can help with hangs and memory leaks (delegate to the `dump-collect` skill), but trace-based investigation requires admin access.
