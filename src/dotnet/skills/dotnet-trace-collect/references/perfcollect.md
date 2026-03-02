# perfcollect

**Purpose**: Wrapper script for `perf` and `LTTng` on Linux. Collects CPU profiles with native call stacks.

| Attribute | Value |
|-----------|-------|
| OS | Linux only |
| Runtime | Modern .NET (.NET Core 2.0+) |
| .NET Framework | ❌ Not supported |
| Admin required | Yes (root) |
| Container | Needs `SYS_ADMIN` / `--privileged` |

## Installation

```bash
# Download the script
curl -OL https://aka.ms/perfcollect
chmod +x perfcollect

# Install prerequisites (perf, LTTng)
sudo ./perfcollect install
```

## Common Commands

```bash
# Collect a trace (runs until Ctrl+C)
sudo ./perfcollect collect mytrace

# Collect for a specific duration
sudo timeout 30 ./perfcollect collect mytrace
```

## Container Usage

```bash
# Docker — run with privileged mode
docker run --privileged ...
```

## Kubernetes Usage

In Kubernetes, use a **diagnostics sidecar container** to avoid modifying your application image. The sidecar runs alongside your app, shares its process namespace and `/tmp` directory, and contains the diagnostic tools.

### Step 1: Add a privileged diagnostics sidecar

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

### Step 2: Enable shared process namespace

Set `shareProcessNamespace: true` in the pod spec so the sidecar can see and profile the app's processes:

```yaml
    spec:
      shareProcessNamespace: true
```

### Step 3: Share /tmp between containers

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

### Step 4: Set environment variables in the app container

Enable perf map generation and LTTng event logging in the application container:

```yaml
        env:
        - name: COMPlus_PerfMapEnabled
          value: "1"
        - name: COMPlus_EnableEventLog
          value: "1"
```

### Step 5: Collect the trace

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

### End-to-end Kubernetes deployment example

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

## Analyzing perfcollect Output

The output is a `*.trace.zip` file containing `perf.data.nl`. To analyze:

1. Copy the `.trace.zip` to a Windows machine
2. Open with PerfView — it can read perfcollect output and display flame graphs

## Trade-offs

- ✅ Captures native (unmanaged) call stacks — essential for diagnosing native interop or runtime issues
- ✅ Uses kernel-level `perf` for accurate CPU profiling
- ❌ Requires root/admin privileges
- ❌ Linux only
- ❌ In containers, requires `SYS_ADMIN` or `--privileged`
