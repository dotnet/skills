# dotnet-counters

**Purpose**: Live monitoring of .NET runtime and application metrics.

| Attribute | Value |
|-----------|-------|
| OS | Windows, Linux, macOS |
| Runtime | Modern .NET (.NET Core 3.0+) |
| .NET Framework | ❌ Not supported |
| Admin required | No |
| Container | Works inside container; connect from outside via diagnostic port |

## Installation

```bash
dotnet tool install -g dotnet-counters
```

## Common Commands

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

## Container Usage

Inside a container, `dotnet-counters` works normally if installed in the image. From outside, connect via diagnostic port:

```bash
# Inside container
dotnet-counters monitor -p 1

# Outside via dotnet-monitor (when console access is not available — see dotnet-monitor reference)
```
