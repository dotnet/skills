---
name: crank-benchmarking
description: Run reproducible ASP.NET benchmarks using Microsoft.Crank with dotnet-trace collection. Covers local and multi-machine setups for before/after comparisons and profiling under load.
metadata:
  author: adityam
  version: "1.0"
---

# Crank Benchmarking

Microsoft.Crank orchestrates ASP.NET benchmarks — it deploys your app, runs a load generator, optionally collects traces, and returns results. Use it when you need reproducible measurements that go beyond BenchmarkDotNet micro-benchmarks (real HTTP load, multi-machine, trace collection during the run).

## Setup

```bash
# Controller (runs on your dev machine, orchestrates everything)
dotnet tool install -g Microsoft.Crank.Controller

# Agent (runs on each machine that hosts the app or load generator)
dotnet tool install -g Microsoft.Crank.Agent
```

For local-only benchmarks, install both on the same machine. For multi-machine setups, install the agent on each server.

### Starting the Agent

Each machine that runs a workload needs a crank agent:
```bash
crank-agent --url http://*:5010
```
Leave this running. The controller connects to agents to deploy and run jobs.

## Local Benchmarking (Single Machine)

The simplest setup — app and load generator on the same machine.

```yaml
# my-benchmark.yml
imports:
  - https://raw.githubusercontent.com/dotnet/crank/main/src/Microsoft.Crank.Jobs.Wrk/wrk.yml

jobs:
  server:
    source:
      localFolder: .
      project: src/MyApp/MyApp.csproj
    readyStateText: "Now listening on"

scenarios:
  my-api:
    application:
      job: server
    load:
      job: wrk
      variables:
        connections: 256
        threads: 32
        duration: 15
        path: /api/endpoint

profiles:
  local:
    variables:
      serverAddress: localhost
    jobs:
      application:
        endpoints:
          - http://localhost:5010
      load:
        endpoints:
          - http://localhost:5010
```

```bash
# Start a local agent first
crank-agent --url http://*:5010 &

# Run the benchmark
crank --config my-benchmark.yml --scenario my-api --profile local
```

`readyStateText` must match what your app prints on startup — crank waits for this exact string before starting the load generator.

### Using a Git Repository Instead of Local Source

```yaml
jobs:
  server:
    source:
      repository: https://github.com/your-org/your-repo.git
      branchOrCommit: main
      project: src/MyApp/MyApp.csproj
    readyStateText: "Now listening on"
```

This lets you compare branches:
```bash
crank --config my-benchmark.yml --scenario my-api --profile local \
  --application.source.branchOrCommit main --description "baseline"

crank --config my-benchmark.yml --scenario my-api --profile local \
  --application.source.branchOrCommit my-optimization --description "optimized"
```

## Multi-Machine Setup

For realistic benchmarks, run the app and load generator on separate machines to avoid competing for CPU.

```yaml
profiles:
  two-machine:
    variables:
      serverAddress: 10.0.0.10    # IP of server machine
    jobs:
      application:
        endpoints:
          - http://10.0.0.10:5010   # agent on server machine
      load:
        endpoints:
          - http://10.0.0.20:5010   # agent on load machine
```

Start `crank-agent` on both machines, then run from your dev machine:
```bash
crank --config my-benchmark.yml --scenario my-api --profile two-machine
```

## Collecting Traces During Benchmarks

This is the key capability — profiling under realistic load.

```bash
crank --config my-benchmark.yml --scenario my-api --profile local \
  --application.dotnetTrace true \
  --application.dotnetTraceProviders "Microsoft-Windows-DotNETRuntime:0x1:5,Microsoft-DotNET-SampleProfiler" \
  --application.options.traceOutput /tmp/bench-trace.nettrace
```

The trace downloads to your local machine automatically after the run.

### Provider strings for common goals

```bash
# CPU only (small trace, fast)
"Microsoft-DotNET-SampleProfiler"

# GC + CPU (most common)
"Microsoft-Windows-DotNETRuntime:0x1:5,Microsoft-DotNET-SampleProfiler"

# Full (GC + JIT + CPU) — larger traces
"Microsoft-Windows-DotNETRuntime:0x4000000011:5,Microsoft-DotNET-SampleProfiler"

# Allocations + GC (can produce very large traces at high throughput)
"Microsoft-Windows-DotNETRuntime:0x200001:5"
```

See the [`dotnet-trace-collection`](../dotnet-trace-collection/SKILL.md) skill for the full provider keyword reference and analysis commands. For systematic optimization methodology, see the [`perf-investigation`](../perf-investigation/SKILL.md) skill.

## Useful Overrides

```bash
# Connection count
--load.variables.connections 100

# Duration (seconds)
--load.variables.duration 15

# Specific .NET SDK
--application.sdkVersion 9.0.100

# Framework target
--application.framework net9.0

# Environment variables
--application.environmentVariables DOTNET_gcServer=1

# Custom application arguments
--application.arguments "--urls http://*:5000"
```

## Load Generators

Crank supports multiple load generators. Import the one you need:

```yaml
# wrk (HTTP/1.1, high throughput)
imports:
  - https://raw.githubusercontent.com/dotnet/crank/main/src/Microsoft.Crank.Jobs.Wrk/wrk.yml

# bombardier (HTTP/1.1 and HTTP/2)
imports:
  - https://raw.githubusercontent.com/dotnet/crank/main/src/Microsoft.Crank.Jobs.Bombardier/bombardier.yml

# h2load (HTTP/2 focused)
imports:
  - https://raw.githubusercontent.com/dotnet/crank/main/src/Microsoft.Crank.Jobs.H2Load/h2load.yml
```

## Gotchas

| Gotcha | Details |
|--------|---------|
| **First run is warmup** | JIT compilation + cold caches. Always discard. Compare run 2 vs run 2 |
| **`readyStateText` must match exactly** | If your app prints "Now listening on: http://..." but you wrote "Application started", crank hangs forever waiting |
| **Agent must be running** | `crank-agent` must be started on each machine before running the controller. If you get connection refused, the agent isn't running |
| **Local benchmarks are noisy** | App and load generator compete for CPU on the same machine. Results have higher variance than multi-machine. Run 3+ iterations |
| **Absolute numbers vary across machines** | Compare relative % improvements, not raw RPS. Your laptop and a perf lab machine will show different absolute numbers |
| **Allocation traces can be huge** | At high throughput, `0x200001` produces 100MB+ traces in seconds. Limit duration or use GC-only (`0x1`) for initial analysis |
| **Connection resets ≠ server error** | Load tools report connection resets when the server closes connections. This is expected for some scenarios (e.g., bad request handling) — check server logs before assuming a bug |
| **Port conflicts** | If you get "address already in use", a previous crank run may not have cleaned up. Find and kill the old process: `lsof -i :5000 -t` |
