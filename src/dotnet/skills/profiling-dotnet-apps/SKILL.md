---
name: profiling-dotnet-apps
description: Profile .NET application performance using dotnet-counters, dotnet-trace, and dotnet-dump. Use when diagnosing CPU spikes, memory growth, high GC pressure, slow requests, or thread contention in .NET applications.
---

# Profiling .NET Applications

## When to Use

- Investigating high CPU usage, memory growth, or slow response times
- Collecting performance traces for offline analysis
- Monitoring live runtime counters during load tests or production issues

## When Not to Use

- The user wants to profile a non-.NET application
- The issue is a compile-time or build error (use `analyzing-build-errors` instead)
- The user needs to debug functional correctness, not performance

## Inputs

| Input | Required | Description |
|-------|----------|-------------|
| Target app or process | Yes | A running .NET process ID, or a project to launch |
| Symptom description | No | What the user is observing (high CPU, memory growth, etc.) |

## Workflow

### Step 1: Verify diagnostic tools are available

```bash
dotnet tool list -g | grep dotnet-counters
dotnet tool list -g | grep dotnet-trace
dotnet tool list -g | grep dotnet-dump
```

If any are missing, install them:

```bash
dotnet tool install -g dotnet-counters
dotnet tool install -g dotnet-trace
dotnet tool install -g dotnet-dump
```

### Step 2: Identify the target process

```bash
dotnet-counters ps
```

This lists all running .NET processes with their PIDs. If the user provides a project instead of a PID, launch it first and note the PID.

### Step 3: Choose the profiling approach based on the symptom

**High CPU** → go to [CPU profiling](#cpu-profiling)
**Memory growth / OOM** → go to [Memory profiling](#memory-profiling)
**General slowness / latency** → go to [Request tracing](#request-tracing)
**Unknown** → start with [Live counters](#live-counters)

### Live counters

Monitor key metrics in real time. Good starting point when the symptom is vague.

```bash
dotnet-counters monitor --process-id <PID> --counters System.Runtime,Microsoft.AspNetCore.Hosting
```

Key counters to watch:

| Counter | Healthy Range | Concern |
|---------|--------------|---------|
| `cpu-usage` | < 70% | Sustained > 85% indicates CPU-bound work |
| `gc-heap-size` | Stable | Steady growth suggests a memory leak |
| `gen-2-gc-count` | Low, infrequent | Frequent Gen 2 GCs indicate memory pressure |
| `threadpool-queue-length` | < 10 | High values suggest thread pool starvation |
| `exception-count` | Low | Sudden spikes need investigation |

Press `q` to stop. Export to CSV for longer monitoring:

```bash
dotnet-counters collect --process-id <PID> --format csv --output counters.csv
```

### CPU profiling

Collect a CPU trace:

```bash
dotnet-trace collect --process-id <PID> --profile cpu-sampling --duration 00:00:30 --output cpu-trace.nettrace
```

Convert to SpeedScope format for visualization:

```bash
dotnet-trace convert cpu-trace.nettrace --format speedscope --output cpu-trace.speedscope.json
```

Open `cpu-trace.speedscope.json` at https://www.speedscope.app/ or analyze in Visual Studio / PerfView.

### Memory profiling

Capture a heap dump:

```bash
dotnet-dump collect --process-id <PID> --output heap.dmp
```

Analyze the dump:

```bash
dotnet-dump analyze heap.dmp
```

Inside the analyzer, run these commands in sequence:

```
dumpheap -stat
```

This shows object counts and sizes sorted by total size. Look for unexpectedly large counts or sizes. Then inspect the top types:

```
dumpheap -type <FullTypeName>
```

To find what roots are keeping objects alive:

```
gcroot <object-address>
```

### Request tracing

For ASP.NET Core applications, collect a trace with HTTP events:

```bash
dotnet-trace collect --process-id <PID> --providers Microsoft-AspNetCore-Server-Kestrel,Microsoft.AspNetCore.Hosting --duration 00:00:30
```

### Step 4: Interpret results and recommend next steps

After collecting data, summarize:

1. The top CPU consumers or largest heap objects
2. Any anomalies in counter trends
3. Specific code paths or types to investigate
4. Concrete optimization suggestions (e.g., caching, pooling, async fixes)

## Validation

- [ ] Diagnostic tools are installed and functional
- [ ] `dotnet-counters ps` lists the target process
- [ ] Profiling data was collected without errors
- [ ] Output files exist and are non-empty
- [ ] Analysis produced actionable findings

## Common Pitfalls

| Pitfall | Solution |
|---------|----------|
| Tools not installed globally | Use `dotnet tool install -g <tool>` |
| "No process found" from dotnet-counters | Verify the process is .NET (not native) and still running |
| Trace file too large | Reduce `--duration` or use specific `--providers` |
| PerfView not available on Linux/macOS | Use SpeedScope (web-based) or `dotnet-trace convert` |
| Collecting dumps in production | Dumps freeze the process briefly; warn the user about impact |
