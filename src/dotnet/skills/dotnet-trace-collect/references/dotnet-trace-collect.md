# dotnet-trace collect

**Purpose**: Collect EventPipe traces for CPU profiling, event analysis, and runtime diagnostics.

| Attribute | Value |
|-----------|-------|
| OS | Windows, Linux, macOS |
| Runtime | Modern .NET (.NET Core 3.0+) |
| .NET Framework | ❌ Not supported |
| Admin required | No |
| Container | Works inside container; use `--diagnostic-port` for sidecar |

## Installation

```bash
dotnet tool install -g dotnet-trace
```

## Common Commands

```bash
# List running .NET processes
dotnet-trace ps

# Collect a trace with the default profile (CPU sampling)
dotnet-trace collect -p <PID>

# Collect with a specific profile
dotnet-trace collect -p <PID> --profile dotnet-sampled-thread-time
dotnet-trace collect -p <PID> --profile gc-verbose

# Collect with specific providers
dotnet-trace collect -p <PID> --providers Microsoft-DotNETCore-SampleProfiler,Microsoft-Windows-DotNETRuntime

# Collect for a fixed duration (time span in hh:mm:ss format)
dotnet-trace collect -p <PID> --duration 00:00:30

# Output in Speedscope format for web-based viewing
dotnet-trace collect -p <PID> --format speedscope
```

## Output Formats

| Format | Extension | Analysis Tool |
|--------|-----------|---------------|
| `nettrace` (default) | `.nettrace` | PerfView, Visual Studio, `dotnet-trace report` |
| `speedscope` | `.speedscope.json` | [Speedscope](https://www.speedscope.app/) (web) |
| `chromium` | `.chromium.json` | Chrome DevTools (chrome://tracing) |

## Container Usage

```bash
# Inside container (process is typically PID 1)
dotnet-trace collect -p 1 -o /tmp/trace.nettrace

# Copy trace out of container
kubectl cp <pod>:/tmp/trace.nettrace ./trace.nettrace
# or
docker cp <container>:/tmp/trace.nettrace ./trace.nettrace
```

## Trade-offs

- ✅ Cross-platform, no admin needed, lightweight
- ❌ No native (unmanaged) call stacks — only managed frames
- ❌ Less system-level detail than ETW (PerfView) or perf (perfcollect)
