# dotnet-trace collect-linux (.NET 10+)

**Purpose**: Collects diagnostic traces using `perf_events`, a Linux OS technology. Provides native call stacks, kernel events, and machine-wide tracing that standard `dotnet-trace collect` cannot.

| Attribute | Value |
|-----------|-------|
| OS | Linux only (kernel >= 6.4 with `CONFIG_USER_EVENTS=y`) |
| Runtime | .NET 10+ only |
| .NET Framework | ❌ Not supported |
| Admin required | Yes (root) |
| Container | Works inside container (requires root) |
| glibc | >= 2.35 (not supported on Alpine 3.22, CentOS Stream 9, or RHEL 9-based distros) |

## Key Differences from `dotnet-trace collect`

| Feature | `collect` | `collect-linux` |
|---------|-----------|-----------------|
| Trace all processes simultaneously | No | Yes (default — no PID required) |
| Capture native library and kernel events | No | Yes |
| Event callstacks include native frames | No | Yes |
| Requires admin/root | No | Yes |

## Usage

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

# Trace with networking providers (HTTP status codes, DNS, TLS, sockets)
sudo dotnet-trace collect-linux --providers System.Net.Http,System.Net.NameResolution,System.Net.Security,System.Net.Sockets

# Trace with networking providers and thread-time profile
sudo dotnet-trace collect-linux --profile thread-time --providers System.Net.Http,System.Net.NameResolution,System.Net.Security,System.Net.Sockets

# Trace with specific profiles
sudo dotnet-trace collect-linux --profile gc-verbose
sudo dotnet-trace collect-linux --profile thread-time

# Trace with additional Linux perf events
sudo dotnet-trace collect-linux --perf-events <list-of-perf-events>

# Output to a specific file
sudo dotnet-trace collect-linux -o /tmp/trace.nettrace
```

When `--providers`, `--profile`, `--clrevents`, and `--perf-events` are not specified, `collect-linux` enables these profiles by default: `dotnet-common` (lightweight .NET runtime diagnostics) and `cpu-sampling` (kernel CPU sampling).

## Container Usage

```bash
# Inside container (machine-wide — captures all processes in the container)
sudo dotnet-trace collect-linux -o /tmp/trace.nettrace

# Inside container (specific process)
sudo dotnet-trace collect-linux -p 1 -o /tmp/trace.nettrace

# Copy trace out
kubectl cp <pod>:/tmp/trace.nettrace ./trace.nettrace
```

## Trade-offs

- ✅ Machine-wide tracing without specifying a PID
- ✅ Native (unmanaged) call stacks — essential for diagnosing native interop or runtime issues
- ✅ Captures kernel events and native library events
- ✅ Designed for Linux-first workflows
- ❌ Requires root privileges
- ❌ Only available on .NET 10 and later
- ❌ Linux only (kernel >= 6.4)
- ❌ Requires glibc >= 2.35 (not all supported Linux distros qualify)
- ❌ Native code symbols must be present on disk next to the binary (or in a standard location) during trace capture for symbol resolution
- ❌ Unable to trace cross-namespace processes
