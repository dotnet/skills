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
# As a .NET global tool (requires .NET SDK)
dotnet tool install -g dotnet-trace

# Direct download via aka.ms (no SDK required — useful in containers)
# Linux x64
curl -JL https://aka.ms/dotnet-trace/linux-x64 -o dotnet-trace
chmod +x dotnet-trace

# Linux Arm64
curl -JL https://aka.ms/dotnet-trace/linux-arm64 -o dotnet-trace
chmod +x dotnet-trace

# Linux musl x64 (Alpine)
curl -JL https://aka.ms/dotnet-trace/linux-musl-x64 -o dotnet-trace
chmod +x dotnet-trace

# Windows x64
curl -JL https://aka.ms/dotnet-trace/win-x64 -o dotnet-trace.exe
```

## Common Commands

```bash
# List running .NET processes
dotnet-trace ps

# Collect a trace with the default profiles (dotnet-common and dotnet-sampled-thread-time)
dotnet-trace collect -p <PID>

# Collect with a specific profile
dotnet-trace collect -p <PID> --profile dotnet-sampled-thread-time
dotnet-trace collect -p <PID> --profile gc-verbose

# Collect with specific providers
dotnet-trace collect -p <PID> --providers Microsoft-DotNETCore-SampleProfiler,Microsoft-Windows-DotNETRuntime

# Collect with networking providers (HTTP status codes, DNS, TLS, sockets)
dotnet-trace collect -p <PID> --providers System.Net.Http,System.Net.NameResolution,System.Net.Security,System.Net.Sockets

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
