# dotnet-monitor

**Purpose**: REST API-based diagnostics collection, designed for container and Kubernetes environments.

| Attribute | Value |
|-----------|-------|
| OS | Windows, Linux, macOS |
| Runtime | Modern .NET (.NET Core 3.1+) |
| .NET Framework | ❌ Not supported |
| Admin required | No |
| Container | ✅ Designed for containers/K8s — runs as a sidecar |

## Installation

```bash
# As a global tool
dotnet tool install -g dotnet-monitor

# As a container image (for K8s sidecar)
# mcr.microsoft.com/dotnet/monitor
```

## Common Commands

```bash
# Production-safe default: keep auth enabled (default) and bind to loopback
dotnet-monitor collect --urls http://127.0.0.1:52323 --metricUrls http://127.0.0.1:52325

# Dev-only shortcut in isolated environments (avoid in production)
dotnet-monitor collect --urls http://127.0.0.1:52323 --no-auth
```

## Security Guidance (Production)

- Do **not** run `dotnet-monitor` with `--no-auth` on production workloads.
- Bind to localhost (`127.0.0.1`) and access through `kubectl port-forward` (or another local-only tunnel).
- If remote access is required, keep auth enabled and restrict network exposure to trusted operators.

## REST API Endpoints

```bash
# Access via local tunnel (recommended in Kubernetes)
kubectl port-forward pod/<pod-name> 52323:52323

# List processes (auth enabled by default)
curl -H "Authorization: Bearer <monitor-token>" http://localhost:52323/processes

# Collect a trace
curl -H "Authorization: Bearer <monitor-token>" -o trace.nettrace http://localhost:52323/trace?pid=<PID>&durationSeconds=30

# Collect GC trace
curl -H "Authorization: Bearer <monitor-token>" -o trace.nettrace "http://localhost:52323/trace?pid=<PID>&profile=gc-verbose&durationSeconds=30"

# Collect trace with networking providers (HTTP status codes, DNS, TLS, sockets)
curl -H "Authorization: Bearer <monitor-token>" -o trace.nettrace "http://localhost:52323/trace?pid=<PID>&durationSeconds=30&providers=System.Net.Http,System.Net.NameResolution,System.Net.Security,System.Net.Sockets"

# Get live metrics
curl -H "Authorization: Bearer <monitor-token>" http://localhost:52323/livemetrics?pid=<PID>
```

## Kubernetes Sidecar Setup

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

## Trade-offs

- ✅ Purpose-built for containers and Kubernetes
- ✅ No tools needed inside the app container
- ✅ REST API is easy to automate and integrate
- ❌ Requires sidecar setup and shared volume
- ❌ Additional resource overhead from sidecar container
- ❌ Requires authentication setup (API key or `--no-auth` flag)
- ⚠️ **When running in the workload context** (console access to the container), prefer console-based tools (`dotnet-trace`, `dotnet-trace collect-linux`) to avoid authentication setup. Use `dotnet-monitor` when console access is not available or when it is already deployed.
