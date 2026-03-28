---
name: dotnet-trace-collect
description: Guide developers through capturing diagnostic artifacts to diagnose production .NET performance issues. Use when the user needs help choosing diagnostic tools, collecting performance data, or understanding tool trade-offs across different environments (Windows/Linux, .NET Framework/modern .NET, container/non-container).
---

# .NET Trace Collect

This skill helps developers diagnose production performance issues by recommending the right diagnostic tools for their environment, guiding data collection, and suggesting analysis approaches. It does not analyze code for anti-patterns or perform the analysis itself.

## When to Use

- A developer needs to investigate a production performance issue (high CPU, memory leak, slow requests, excessive GC, networking errors, etc.)
- Choosing the right diagnostic tool for a specific runtime, OS, or deployment topology
- Setting up and running diagnostic tool commands for data collection
- Understanding trade-offs between available tools (e.g. PerfView vs dotnet-trace)
- Collecting diagnostics from containerized or Kubernetes workloads

## When Not to Use

- Reviewing source code for performance anti-patterns (use a code review skill instead)
- Benchmarking during development (e.g. BenchmarkDotNet setup)
- Analyzing collected trace or dump files (this skill recommends tools for analysis, but does not perform it)

## Inputs

| Input | Required | Description |
|-------|----------|-------------|
| Symptom | Yes | What the developer is observing (high CPU, memory growth, slow requests, hangs, excessive GC, HTTP 5xx errors, networking timeouts, connection failures, assembly loading failures, etc.) |
| Runtime | Yes | .NET Framework or modern .NET (and version, especially whether .NET 10+) |
| OS | Yes | Windows or Linux |
| Deployment | Yes | Non-container, container, or Kubernetes |
| Admin privileges | Recommended | Whether the developer has admin/root access on the target machine |
| Repro characteristics | Recommended | Whether the issue is easy to reproduce or requires a long time to manifest |

## Workflow

### Step 1: Understand the environment

Determine or ask the developer to clarify:

1. **Symptom**: What they are observing (high CPU, memory leak, slow requests, hangs, excessive GC, HTTP 5xx errors, networking timeouts, connection failures, assembly loading failures, etc.)
2. **Runtime**: .NET Framework or modern .NET? If modern .NET, which version? (Especially whether .NET 10 or later.)
3. **OS**: Windows or Linux?
4. **Deployment**: Running directly on the host, in a container, or in Kubernetes?
5. **Admin privileges**: Do they have admin/root access on the target machine or container?
6. **Repro characteristics**: Does the issue reproduce quickly, or does it take a long time to manifest?
7. **Workload context**: Determine or ask the user if you are running in the context of the workload (i.e., on the same machine or connected to the same environment where the issue is occurring). If so, you can run diagnostic commands directly on their behalf. If not, provide the commands as guidance for the user to run themselves.

Use this information to select the right tool in Step 2.

### Step 2: Recommend diagnostic tools

Select tools based on the environment using the priority rules below. Once a tool is selected, load the corresponding reference file for detailed command-line usage.

#### Tool reference lookup

| Environment | Reference file(s) |
|-------------|-------------------|
| Windows + modern .NET + admin | `references/perfview.md` |
| Windows + modern .NET, no admin | `references/dotnet-trace-collect.md` |
| Windows + .NET Framework | `references/perfview.md` |
| Linux + .NET 10+ + root | `references/dotnet-trace-collect-linux.md` |
| Linux + pre-.NET 10 | `references/dotnet-trace-collect.md` |
| Linux + native stacks needed | `references/perfcollect.md` |
| Container/K8s (console access) | `references/dotnet-trace-collect.md` (or `dotnet-trace-collect-linux.md`) |
| Container/K8s (no console) | `references/dotnet-monitor.md` |

#### Quick decision matrix (first-pass triage)

| Environment | Preferred tool | Fallback / Notes |
|-------------|----------------|------------------|
| Windows + modern .NET + admin | PerfView | If admin is unavailable, use `dotnet-trace` |
| Windows + .NET Framework + admin | PerfView | Without admin, there is no trace fallback; for hangs/memory leaks, provide dump commands directly (`procdump -ma` or Task Manager) since `dump-collect` does not support .NET Framework |
| Linux + .NET 10+ + root | `dotnet-trace collect-linux` | Use `dotnet-trace` if root or kernel prerequisites are not met |
| Linux + pre-.NET 10 | `dotnet-trace` | Add `perfcollect` when native stacks are needed (requires root) |
| Linux container/Kubernetes | Console tools if in workload context; `dotnet-monitor` if no console access | See Linux Container / Kubernetes section for details |

#### Windows (non-container, modern .NET)

1. **PerfView** (preferred) — produces richer ETW-based data; requires admin privileges. For **slow requests**, add `/ThreadTime` to capture thread-level wait and block detail.
2. **`dotnet-trace`** — fallback when admin privileges are not available.
3. For **long-running repros**: use PerfView with a `/StopOn` trigger that fires on the **symptom you want to capture** (e.g., `/StopOnPerfCounter`, `/StopOnGCEvent`, `/StopOnException`) and a circular buffer (`/CircularMB` + `/BufferSizeMB`). **Critical: the stop trigger must fire on the interesting event, not the recovery.** The circular buffer continuously overwrites old data, so if you trigger on recovery, the buffer may have already overwritten the interesting behavior by the time collection stops. Only add `/StartOn` if the start event is known to precede the stop event. For **slow requests**, do not include a stop trigger by default — let the user design one based on their specific scenario.

#### Windows containers

1. **PerfView** — most Windows containers (including Kubernetes on Windows) use process-isolation by default. Collect from the host with `/EnableEventsInContainers`. After collection, you have two options:
   - **Analyze locally while the container is still running** — PerfView can reach into the live container to resolve symbols, so you can open the trace immediately on the host machine.
   - **Analyze off-machine** — before the container shuts down, copy the `.etl.zip` into the container and run `PerfViewCollect merge /ImageIDsOnly` inside it to embed symbol information. Then copy the merged trace out. Without this merge step, symbols for binaries inside the container will be unresolvable on other machines.

   For the less common Hyper-V containers, collect inside the container directly. See [references/perfview.md](references/perfview.md) for detailed commands.
2. **`dotnet-monitor`**, **`dotnet-trace`** — inside the container if the tools are installed in the image. For dumps, invoke the **`dump-collect`** skill.

#### Windows (.NET Framework)

1. **PerfView** — the primary diagnostic tool for .NET Framework on Windows. Requires admin.
2. Same trigger guidance for long repros: use `/StopOn` triggers that fire on the symptom (e.g., `/StopOnPerfCounter`, `/StopOnGCEvent`, `/StopOnException`) with `/CircularMB` + `/BufferSizeMB`.
3. **Without admin**: PerfView requires admin, and there are no alternative trace tools for .NET Framework. Process dumps can still be captured without admin — provide dump commands directly (e.g., `procdump -ma <PID>` or Task Manager) since the `dump-collect` skill does not support .NET Framework. Dumps can help diagnose hangs and memory leaks. However, for **high CPU**, **slow requests**, and **excessive GC**, there is no way to investigate on .NET Framework without admin access. Advise the user to obtain admin privileges.

#### Linux (non-container, .NET 10+)

1. **`dotnet-trace collect-linux`** (preferred) — uses `perf_events` for richer traces including native call stacks and kernel events. Captures machine-wide by default (no PID required). Requires root and kernel >= 6.4.
2. **`dotnet-trace`** — fallback when root privileges are not available or kernel requirements are not met. Managed stacks only.

#### Linux (non-container, pre-.NET 10)

1. **`dotnet-trace`** (preferred) — managed trace collection; no admin required.
2. **`perfcollect`** — when **native call stacks** are needed (requires admin/root).

#### Linux Container / Kubernetes

**If running in the context of the workload** (i.e., you have console access to the container), prefer console-based tools. These are easier to set up than `dotnet-monitor`, which requires authentication configuration and sidecar deployment:

1. **`dotnet-trace collect-linux`** (.NET 10+ with root) — produces the richest traces including native call stacks and kernel events.
2. **`dotnet-trace`** — inside the container if the tool is installed in the image. For dumps, invoke the **`dump-collect`** skill.
3. **`perfcollect`** — inside the container when native stacks are needed on pre-.NET 10 (requires `SYS_ADMIN` / `--privileged`).

**If not running in the workload context** (no console access), or if `dotnet-monitor` is already deployed:

1. **`dotnet-monitor`** — designed for containers; runs as a sidecar. No tools needed in the app container. Easiest option when console access is not available.

#### Memory dumps

When dumps are needed (memory leaks, hangs), **do not provide dump collection commands directly** for modern .NET — invoke the **`dump-collect`** skill instead. The `dump-collect` skill only supports modern .NET (.NET Core 3.0+). For **.NET Framework**, provide dump collection guidance directly (e.g., `procdump -ma <PID>` or Task Manager). This skill focuses on trace collection only.

#### Memory leaks

- **Capture two dumps** as memory is increasing (e.g., one early, one after significant growth). Invoke the **`dump-collect`** skill for dump collection — do not provide dump commands directly. Diff the dumps in PerfView to see which objects have increased — this is the most effective way to identify what is leaking.
- **Without admin privileges**: Two process dumps can give a sense of what's growing on the heap, but may not be enough to identify the root cause. If dumps aren't sufficient, reproduce the issue in an environment where admin privileges are available to collect richer data (traces).
- **Modern .NET on Linux (pre-.NET 10)**: Recommend two dump captures (invoke `dump-collect` skill) for heap diff, plus `dotnet-trace` while memory is growing (for allocation tracking). No trigger needed — capture during the growth period. Both together give the best picture.
- **Modern .NET 10+ on Linux with admin**: Recommend two dump captures (invoke `dump-collect` skill) for heap diff, plus `dotnet-trace collect-linux` while memory is growing (richer data including native stacks). No trigger needed.
- **.NET Framework**: Recommend two dumps plus a PerfView trace while memory is growing to see what is being allocated. The `dump-collect` skill does not support .NET Framework, so provide dump commands directly (e.g., `procdump -ma <PID>` or right-click → Create Dump File in Task Manager). No trigger is needed — just capture the trace during the growth period. Do not wait for an `OutOfMemoryException`.

#### Excessive GC

Excessive GC requires a **trace** to analyze GC events, pause times, and allocation patterns — a dump is not sufficient.

- **Windows (PerfView)**: Use `PerfView collect /GCCollectOnly` to capture GC events.
- **Linux (dotnet-trace)**: Use `dotnet-trace collect -p <PID> --profile gc-verbose`.
- **Linux .NET 10+ with root**: Use `dotnet-trace collect-linux --profile gc-verbose` for richer data with native stacks.
- **Containers**: `dotnet-monitor` can capture GC traces via its REST API (`/trace?profile=gc-verbose`).

#### Slow Requests

Slow requests require a **thread time trace** to see where threads are spending time — waiting on locks, I/O, external calls, etc. Use larger buffers since thread time traces generate more data. For ASP.NET Core applications, also enable `Microsoft.AspNetCore.Hosting` and `Microsoft-AspNetCore-Server-Kestrel` providers to get server-side request lifecycle timing (when requests arrive, how long they take to process).

- **Windows (PerfView)**: Use `PerfView /ThreadTime collect /BufferSizeMB:1024 /CircularMB:2048`. The `/ThreadTime` argument adds thread-level wait and block detail. For ASP.NET Core, add Kestrel providers: `PerfView /ThreadTime collect /BufferSizeMB:1024 /CircularMB:2048 /Providers:*Microsoft.AspNetCore.Hosting,*Microsoft-AspNetCore-Server-Kestrel`. Do not include a stop trigger by default — let the user design one based on their specific scenario.
- **Linux (dotnet-trace)**: `dotnet-trace` captures thread time data by default — no special arguments needed. Use `dotnet-trace collect -p <PID>`. For ASP.NET Core, add Kestrel providers: `dotnet-trace collect -p <PID> --providers Microsoft.AspNetCore.Hosting,Microsoft-AspNetCore-Server-Kestrel`.
- **Linux .NET 10+ with root**: Use `dotnet-trace collect-linux --profile thread-time` for richer data with native stacks. For ASP.NET Core, add: `--providers Microsoft.AspNetCore.Hosting,Microsoft-AspNetCore-Server-Kestrel`.
- **Containers**: `dotnet-monitor` can capture traces via its REST API (`/trace?pid=<PID>&durationSeconds=30`).

#### Hangs

1. **Start with a trace** to understand what threads are doing. Use the appropriate trace tool for the environment (PerfView with `/ThreadTime` on Windows, `dotnet-trace` on Linux, `dotnet-trace collect-linux --profile thread-time` on .NET 10+ Linux with root). The trace can reveal:
   - **Livelocks** (threads spinning without forward progress) — threads appear busy but the application makes no progress.
   - **Thread starvation** — the ThreadPool is exhausted and queued work items are not being processed. This can look like a deadlock but has a different root cause.
   - **Whether there is any forward progress at all** — if some threads are making progress, the issue may be a bottleneck rather than a true hang.
2. **If the trace does not explain the hang**, the issue may be a **true deadlock** (threads waiting on each other in a cycle). In this case, invoke the **`dump-collect`** skill to collect a process dump — do not provide dump commands directly.
3. **Analyze the dump with a debugger** to inspect thread stacks and identify the lock cycle:
   - **Windows**: Visual Studio or WinDbg with the SOS debugger extension.
   - **Linux**: `lldb` with the SOS debugger extension.

#### Networking Issues

Networking issues (HTTP 5xx errors from downstream services, request timeouts, connection failures, DNS resolution failures, TLS handshake failures, connection pool exhaustion) require **both** a thread-time trace and networking event providers. The thread-time trace shows where threads are blocked (slow downstream calls, thread starvation), while the networking events show the request lifecycle — which requests failed, what status codes came back, how long DNS resolution and TLS handshakes took, and how long requests waited for a connection from the pool.

For **.NET Framework**, `PerfView /ThreadTime` already collects the relevant networking events (from the `System.Net` ETW provider) — no additional providers are needed.

For **modern .NET**, you must explicitly enable the `System.Net.*` EventSource providers:

| Provider | What it covers |
|----------|---------------|
| `System.Net.Http` | HttpClient/SocketsHttpHandler — request lifecycle, HTTP status codes, connection pool |
| `System.Net.NameResolution` | DNS lookups (start/stop, duration) |
| `System.Net.Security` | TLS/SSL handshakes (SslStream) |
| `System.Net.Sockets` | Low-level socket connect/disconnect |

Key events from `System.Net.Http`: `RequestStart` (scheme, host, port, path), `RequestStop` (statusCode — `-1` if no response was received), `RequestFailed` (exception message for timeouts, connection refused, etc.), `RequestLeftQueue` (time waiting for a connection from the pool — indicates connection pool exhaustion), `ConnectionEstablished`, `ConnectionClosed`.

Collect a thread-time trace with networking providers enabled (modern .NET only — .NET Framework needs only `PerfView /ThreadTime`):

- **Windows (PerfView)**: Use `PerfView /ThreadTime collect /BufferSizeMB:1024 /CircularMB:2048 /Providers:*System.Net.Http,*System.Net.NameResolution,*System.Net.Security,*System.Net.Sockets`. For .NET Framework, omit the `/Providers` flag — `/ThreadTime` already includes the networking events. The thread-time trace shows where threads are blocked while the networking events show what requests are failing and why.
- **Linux (dotnet-trace)**: `dotnet-trace` captures thread time data by default, but specifying `--providers` overrides the defaults so you must also include `--profile`: `dotnet-trace collect -p <PID> --profile dotnet-common,dotnet-sampled-thread-time --providers System.Net.Http,System.Net.NameResolution,System.Net.Security,System.Net.Sockets`.
- **Linux .NET 10+ with root**: Use `dotnet-trace collect-linux --profile dotnet-common,cpu-sampling,thread-time --providers System.Net.Http,System.Net.NameResolution,System.Net.Security,System.Net.Sockets`.
- **Containers**: `dotnet-monitor` can capture traces with custom providers via its REST API.

#### Assembly Loading Issues

For modern .NET, assembly loading issues (`FileNotFoundException`, `FileLoadException`, `ReflectionTypeLoadException`, version conflicts, duplicate assembly loads across AssemblyLoadContexts) require collecting **assembly loader binder events** from the `Microsoft-Windows-DotNETRuntime` provider with the Loader keyword (`0x4`). These events trace every step of the runtime's assembly resolution algorithm — which paths were probed, which AssemblyLoadContext handled the load, whether the load succeeded or failed, and why. For .NET Framework, the same provider and keyword work for ETW-based collection; additionally, the Fusion Log Viewer (`fuslogvw.exe`) can diagnose assembly binding failures without requiring a trace.

The provider specification is `Microsoft-Windows-DotNETRuntime:0x4:4` (provider name, AssemblyLoader keyword, Informational verbosity).

- **Windows (PerfView)**: A default PerfView trace already includes binder events - simply run `PerfView collect` with no extra providers. For a smaller trace file, use `PerfView collect /ClrEvents:Default-Profile`, which removes the most verbose default events while keeping the events necessary for diagnosing assembly loading issues.
- **Linux / cross-platform (dotnet-trace)**: Use `dotnet-trace collect --clrevents assemblyloader -- <path-to-built-exe>` to launch and trace the process, or `dotnet-trace collect --clrevents assemblyloader -p <PID>` to attach to a running process.
- **Linux .NET 10+ with root**: Use `dotnet-trace collect-linux --clrevents assemblyloader`.
- **Containers**: `dotnet-monitor` can capture traces with the loader provider via its REST API.

For short-lived processes that fail on startup (common with assembly loading issues), prefer the `dotnet-trace` launch form (`-- <path-to-built-exe>`) over attaching by PID, since the process may exit before you can attach.

Explain the trade-offs when recommending a tool. For example:
- PerfView gives richer data but needs admin; runs on Windows including Windows containers.
- `dotnet-trace` works cross-platform without admin but captures less system-level detail.
- `perfcollect` captures native call stacks but needs admin/root.
- `dotnet-monitor` is the best option for containers/K8s when console access is not available, but requires sidecar deployment and authentication configuration.

### Step 3: Guide data collection

Provide the specific commands for the recommended tool. Load the appropriate reference file from the [tool reference lookup](#tool-reference-lookup) table for detailed command-line examples.

Key guidance to include:

1. **Installation**: How to install the tool if it is not already available (e.g. `dotnet tool install -g dotnet-trace`). **When recommending multiple tools, provide installation and usage instructions for each one** — do not mention a tool without showing how to install and use it.
2. **PID discovery (required before any `-p <PID>` command)**: Verify the target process first (for example: `dotnet-trace ps`, `curl <monitor-endpoint>/processes`, or `ps` inside a container). If the app is expected to be PID 1 in a container, still verify before collecting.
3. **Collection command**: The exact command to run, including relevant providers, output format, and duration.
4. **Container considerations**:
   - Collecting from **inside** the container: ensure the tool is installed in the image or use `kubectl cp` to copy it in.
   - Collecting from **outside** the container: use `dotnet-monitor` as a sidecar with a shared diagnostic port (Unix domain socket in `/tmp`).
   - Kubernetes: `dotnet-monitor` as a sidecar container, or `kubectl debug` for ephemeral debug containers.
5. **Long-running repros** (Windows/PerfView): show how to use trigger arguments and circular buffer settings.
6. **Output location**: Where the collected file will be saved and how to copy it off the target for analysis.
7. **Artifact handoff checklist**: Include runtime version, OS/kernel, container image tag or build SHA, PID/process name, UTC collection start/end timestamps, exact command used, and final artifact path when handing traces to someone else for analysis.

### Step 4: Recommend analysis approach

After data is collected, recommend the appropriate tool for analysis. Do **not** perform the analysis — just point the developer to the right tool and documentation.

| Collected Data | Analysis Tool | Notes |
|----------------|---------------|-------|
| `.nettrace` file | PerfView (Windows), Speedscope (web) | PerfView gives the richest view on Windows |
| `.etl` / `.etl.zip` file | PerfView | ETW traces from PerfView or perfcollect |
| `perf.data.nl` from perfcollect | PerfView (Windows) | Copy the file to a Windows machine and open with PerfView |

## Validation

- [ ] The recommended tool is compatible with the developer's runtime, OS, and deployment topology
- [ ] The collection command runs without errors
- [ ] The output file is generated in the expected location
- [ ] The developer knows which analysis tool to use for the collected data

## Common Pitfalls

| Pitfall | Solution |
|---------|----------|
| Using `dotnet-trace` on .NET Framework | `dotnet-trace` only works with modern .NET (.NET Core 3.0+). Use PerfView for .NET Framework. |
| PerfView without admin privileges | PerfView requires admin for ETW tracing. Fall back to `dotnet-trace` if admin is not available. |
| `perfcollect` in container without `SYS_ADMIN` | Containers drop `SYS_ADMIN` by default. Run with `--privileged` or add `SYS_ADMIN` capability, or fall back to `dotnet-trace`. |
| Huge trace files from long repros | On Windows, use PerfView `/StopOn` triggers that fire on the symptom you want to capture (e.g., `/StopOnPerfCounter`, `/StopOnGCEvent`, `/StopOnException`) with `/CircularMB` and `/BufferSizeMB`. **Never trigger on recovery** — the circular buffer continuously overwrites old data, so the interesting behavior may be lost by the time collection stops. |
| Diagnostic port not accessible in container | Mount `/tmp` as a shared volume between the app container and `dotnet-monitor` sidecar for the diagnostic Unix domain socket. |
| Forgetting to install tools in container image | Add `dotnet tool install` to your Dockerfile, or use `dotnet-monitor` as a sidecar to avoid modifying the app image. |
| Exposing `dotnet-monitor` with `--no-auth` in production | Keep auth enabled, bind to localhost, and use `kubectl port-forward` for access. Use `--no-auth` only for short-lived isolated debugging. |
| Collecting only CPU/thread-time trace for networking issues | CPU and thread-time traces alone do not show HTTP status codes, DNS timing, or connection pool behavior. Add the networking providers (`System.Net.Http`, `System.Net.NameResolution`, `System.Net.Security`, `System.Net.Sockets`) alongside the thread-time trace. |
| Enabling all networking providers when only one is needed | Each networking provider adds overhead. If the issue is clearly HTTP-level (5xx status codes), `System.Net.Http` alone may be sufficient. Add DNS, TLS, and socket providers when the root cause is unclear. |
