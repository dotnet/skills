# Container Crash Dump Collection

| Need | Tool | Runtime | Requires |
|------|------|---------|----------|
| Automatic dump on crash (CoreCLR) | `DOTNET_DbgEnableMiniDump` env vars + `createdump` | CoreCLR | `SYS_PTRACE`, volume mount |
| Automatic dump on crash (NativeAOT, preferred) | `--ulimit core=-1` + `core_pattern` | NativeAOT | `SYS_PTRACE`, volume mount |
| Automatic dump on crash (NativeAOT, alternative) | `createdump` + `DOTNET_DbgEnableMiniDump` env vars | NativeAOT | `SYS_PTRACE`, volume mount |
| On-demand from running container | `dotnet-dump collect` (CoreCLR), `gcore` (NativeAOT) | Per-runtime | `SYS_PTRACE` |
| Copy dump out of container | `docker cp` / `kubectl cp` | Both | — |

Collecting crash dumps from .NET applications running in Docker or Kubernetes containers requires additional configuration for capabilities, storage, and environment variables.

> **Note:** The `%e`, `%p`, `%h`, `%t` format specifiers in `DOTNET_DbgMiniDumpName` require .NET 7+. On .NET 6, use a literal path instead.
>
> **Dump types** for `DOTNET_DbgMiniDumpType`: 1=Mini, 2=Heap, 3=Triage, 4=Full. Use 4 for maximum diagnostic value. NativeAOT and CoreCLR single-file apps only support full dumps (type 4).

## Docker

### Required Capability

The `SYS_PTRACE` capability is required for `dotnet-dump collect`, `createdump`, and `gcore` to attach to processes:

```bash
# docker run
docker run --cap-add=SYS_PTRACE -v /tmp/dumps:/dumps myapp

# docker compose
```

```yaml
# docker-compose.yml
services:
  myapp:
    image: myapp
    cap_add:
      - SYS_PTRACE
    volumes:
      - ./dumps:/dumps
    environment:
      # CoreCLR automatic crash dumps
      DOTNET_DbgEnableMiniDump: "1"
      DOTNET_DbgMiniDumpType: "4"
      DOTNET_DbgMiniDumpName: "/dumps/%e_%p_%t.dmp"
      DOTNET_EnableCrashReport: "1"
```

### CoreCLR in Docker

Add environment variables to enable automatic crash dumps. Either in `docker run`:

```bash
docker run --cap-add=SYS_PTRACE \
  -v /tmp/dumps:/dumps \
  -e DOTNET_DbgEnableMiniDump=1 \
  -e DOTNET_DbgMiniDumpType=4 \
  -e DOTNET_DbgMiniDumpName="/dumps/%e_%p_%t.dmp" \
  -e DOTNET_EnableCrashReport=1 \
  myapp
```

Or in the Dockerfile (baked into the image):

```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:10.0

# Configure crash dump collection
ENV DOTNET_DbgEnableMiniDump=1
ENV DOTNET_DbgMiniDumpType=4
ENV DOTNET_DbgMiniDumpName="/dumps/%e_%p_%t.dmp"
ENV DOTNET_EnableCrashReport=1

# Create dump directory
RUN mkdir -p /dumps

COPY --from=build /app .
ENTRYPOINT ["dotnet", "myapp.dll"]
```

### NativeAOT in Docker

Since NativeAOT only supports full dumps, OS-level core dump mechanisms are the simplest approach — no extra tooling to copy or configure.

**Preferred: OS-level core dumps:**

```bash
docker run --cap-add=SYS_PTRACE \
  --ulimit core=-1 \
  -v /tmp/dumps:/dumps \
  myapp
```

**Note:** You may also need to set the core pattern inside the container. If the host's `core_pattern` pipes to `systemd-coredump`, container core dumps may not be written where expected. Override at runtime:

```bash
docker run --cap-add=SYS_PTRACE \
  --ulimit core=-1 \
  --privileged \
  -v /tmp/dumps:/dumps \
  myapp sh -c 'echo "/dumps/core.%e.%p" > /proc/sys/kernel/core_pattern && exec ./myapp'
```

⚠️ `--privileged` is needed to write to `/proc/sys/kernel/core_pattern`. For production, prefer configuring the core pattern on the host instead.

**Alternative: Use createdump with DOTNET_DbgEnableMiniDump** (same env vars as CoreCLR). Copy `createdump` next to the app binary in the image:

```dockerfile
FROM mcr.microsoft.com/dotnet/runtime-deps:10.0

# Copy createdump from the runtime (match your .NET version)
COPY --from=mcr.microsoft.com/dotnet/runtime:10.0 /usr/share/dotnet/shared/Microsoft.NETCore.App/ /tmp/runtime/
RUN find /tmp/runtime -name createdump -exec cp {} /app/ \; && rm -rf /tmp/runtime/

# Configure crash dump collection
ENV DOTNET_DbgEnableMiniDump=1
ENV DOTNET_DbgMiniDumpType=4
ENV DOTNET_DbgMiniDumpName="/dumps/%e_%p_%t.dmp"

RUN mkdir -p /dumps

COPY --from=build /app .
ENTRYPOINT ["./myapp"]
```

For .NET 11+, use `DOTNET_DbgCreateDumpToolPath` instead of co-locating:

```dockerfile
ENV DOTNET_DbgCreateDumpToolPath=/opt/tools/createdump
ENV DOTNET_DbgEnableMiniDump=1
ENV DOTNET_DbgMiniDumpType=4
ENV DOTNET_DbgMiniDumpName="/dumps/%e_%p_%t.dmp"
```

Run with:
```bash
docker run --cap-add=SYS_PTRACE \
  -v /tmp/dumps:/dumps \
  myapp
```

### On-Demand Collection in Docker

```bash
# Find the container and process
docker exec <container> dotnet-dump ps         # CoreCLR
docker exec <container> sh -c 'ps aux | grep myapp'    # NativeAOT

# Collect dump (CoreCLR)
docker exec <container> dotnet-dump collect -p <pid> --output /dumps/myapp.dmp

# Collect dump (NativeAOT — requires gdb/gcore in the image)
docker exec <container> gcore -o /dumps/myapp <pid>

# Copy dump out of container (alternative to volume mount)
docker cp <container>:/dumps/myapp.dmp ./myapp.dmp
```

## Kubernetes

### Pod Spec for CoreCLR

```yaml
apiVersion: v1
kind: Pod
metadata:
  name: myapp
spec:
  containers:
    - name: myapp
      image: myapp:latest
      securityContext:
        capabilities:
          add: ["SYS_PTRACE"]
      env:
        - name: DOTNET_DbgEnableMiniDump
          value: "1"
        - name: DOTNET_DbgMiniDumpType
          value: "4"
        - name: DOTNET_DbgMiniDumpName
          value: "/dumps/%e_%p_%t.dmp"
        - name: DOTNET_EnableCrashReport
          value: "1"
      volumeMounts:
        - name: dumps
          mountPath: /dumps
  volumes:
    - name: dumps
      emptyDir:
        sizeLimit: 5Gi    # Adjust based on expected dump size
```

### Pod Spec for NativeAOT

**Preferred: OS-level core dumps** (simplest — no extra tooling needed):

```yaml
apiVersion: v1
kind: Pod
metadata:
  name: myapp-nativeaot
spec:
  containers:
    - name: myapp
      image: myapp:latest
      securityContext:
        capabilities:
          add: ["SYS_PTRACE"]
      command: ["/bin/sh", "-c"]
      args: ["ulimit -c unlimited && exec ./myapp"]
      volumeMounts:
        - name: dumps
          mountPath: /dumps
  volumes:
    - name: dumps
      emptyDir:
        sizeLimit: 10Gi
```

**Alternative: Use createdump** (bundled in the image next to the app, or via `DOTNET_DbgCreateDumpToolPath` on .NET 11+):

```yaml
apiVersion: v1
kind: Pod
metadata:
  name: myapp-nativeaot
spec:
  containers:
    - name: myapp
      image: myapp:latest
      securityContext:
        capabilities:
          add: ["SYS_PTRACE"]
      env:
        - name: DOTNET_DbgEnableMiniDump
          value: "1"
        - name: DOTNET_DbgMiniDumpType
          value: "4"
        - name: DOTNET_DbgMiniDumpName
          value: "/dumps/%e_%p_%t.dmp"
        # .NET 11+ only — uncomment if createdump is not next to the binary:
        # - name: DOTNET_DbgCreateDumpToolPath
        #   value: "/opt/tools/createdump"
      volumeMounts:
        - name: dumps
          mountPath: /dumps
  volumes:
    - name: dumps
      emptyDir:
        sizeLimit: 5Gi
```

### Using a Persistent Volume for Dumps

For production, use a PersistentVolumeClaim instead of `emptyDir` so dumps survive pod restarts:

```yaml
volumes:
  - name: dumps
    persistentVolumeClaim:
      claimName: dump-storage

---
apiVersion: v1
kind: PersistentVolumeClaim
metadata:
  name: dump-storage
spec:
  accessModes: ["ReadWriteOnce"]
  resources:
    requests:
      storage: 20Gi
```

### On-Demand Collection in Kubernetes

```bash
# Find the pod and process
kubectl exec <pod> -- dotnet-dump ps           # CoreCLR
kubectl exec <pod> -- sh -c 'ps aux | grep myapp'      # NativeAOT

# Collect dump
kubectl exec <pod> -- dotnet-dump collect -p <pid> --output /dumps/myapp.dmp

# Copy dump out of the pod
kubectl cp <pod>:/dumps/myapp.dmp ./myapp.dmp
```

### Deployment-Level Configuration

To apply dump collection to all pods in a Deployment:

```yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: myapp
spec:
  template:
    spec:
      containers:
        - name: myapp
          image: myapp:latest
          securityContext:
            capabilities:
              add: ["SYS_PTRACE"]
          env:
            - name: DOTNET_DbgEnableMiniDump
              value: "1"
            - name: DOTNET_DbgMiniDumpType
              value: "4"
            - name: DOTNET_DbgMiniDumpName
              value: "/dumps/%e_%p_%t.dmp"
          volumeMounts:
            - name: dumps
              mountPath: /dumps
      volumes:
        - name: dumps
          emptyDir:
            sizeLimit: 5Gi
```

## Verification

**Docker:**

```bash
# Check env vars inside the container
docker exec <container> env | grep DOTNET_Dbg

# Check dump directory exists and is writable
docker exec <container> ls -la /dumps/

# Check createdump is available (NativeAOT)
docker exec <container> ls -la /app/createdump 2>/dev/null || echo "createdump not co-located"

# After a crash, check for dump files
docker exec <container> sh -c 'ls -la /dumps/*.dmp' 2>/dev/null
# Or from the host via volume mount:
ls -la /tmp/dumps/*.dmp
```

**Kubernetes:**

```bash
# Check env vars
kubectl exec <pod> -- env | grep DOTNET_Dbg

# Check dump directory
kubectl exec <pod> -- ls -la /dumps/

# After a crash, list dumps
kubectl exec <pod> -- sh -c 'ls -la /dumps/*.dmp' 2>/dev/null

# Copy dumps out for inspection
kubectl cp <pod>:/dumps/ ./dumps/
```

## Container Considerations

### Non-Root Users

If your container runs as a non-root user, ensure the dump directory is writable:

```dockerfile
RUN mkdir -p /dumps && chown -R app:app /dumps
USER app
```

For Kubernetes, use an init container or `securityContext.fsGroup`:

```yaml
securityContext:
  runAsNonRoot: true
  runAsUser: 1000
  fsGroup: 1000    # matches the app user's group
```

When using `runAsNonRoot: true`, the dump directory must be writable by the non-root user. Use `fsGroup` to grant group write access to the mounted volume, or set permissions in the Dockerfile.

### Alpine / musl-Based Images

`createdump`, `dotnet-dump`, and CoreCLR automatic crash dumps all work on the supported .NET Alpine images. No special configuration is needed beyond the standard setup described above.

### SELinux / AppArmor

On hosts with SELinux or AppArmor, `SYS_PTRACE` alone may not be sufficient:

- **SELinux**: The container may need `--security-opt label=disable` or an appropriate SELinux policy allowing ptrace
- **AppArmor**: Use `--security-opt apparmor=unconfined` for debugging, or create a custom profile that allows ptrace

⚠️ Disabling security modules is acceptable for debugging but should not be used in production.

## Troubleshooting

| Problem | Cause | Fix |
|---------|-------|-----|
| `dotnet-dump collect` fails with "permission denied" | Missing `SYS_PTRACE` capability | Add `SYS_PTRACE` to securityContext / cap_add |
| Dump file not created on crash | Dump directory doesn't exist | Ensure `/dumps` directory exists in the image or init container |
| Core dump goes to wrong location | Host `core_pattern` overrides container | Configure `core_pattern` on the host or use `--privileged` |
| Dump file is 0 bytes | Disk space exhausted | Increase `emptyDir.sizeLimit` or PVC size |
| `createdump` not found in container | Minimal runtime image | For CoreCLR: use `dotnet-dump collect` instead. For NativeAOT: copy `createdump` from the runtime image (see NativeAOT in Docker section) or use the full SDK image |
