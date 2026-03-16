# Containers and Interop Breaking Changes (.NET 9)

These changes affect Docker containers, native interop, and CET (Control-flow Enforcement Technology).

## Containers

### Container images no longer install zlib

**Impact: Medium.** .NET 9 container images no longer install `zlib` because the .NET Runtime now includes a statically linked `zlib-ng`. If your app has a direct dependency on the `zlib` system package, install it manually:

```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:9.0
RUN apt-get update && apt-get install -y zlib1g
```

### .NET Monitor images simplified to version-only tags

**Impact: Low.** .NET Monitor container image tags have been simplified to version-only format. Update any container orchestration configuration that references specific tag formats.

## Interop

### CET supported by default

**Impact: Medium (binary incompatible).** `apphost` and `singlefilehost` are now compiled with the `/CETCOMPAT` flag, enabling Intel CET (Control-flow Enforcement Technology) hardware-enforced stack protection. This enhances security against ROP exploits but imposes restrictions on shared libraries:

- Libraries cannot set thread context to locations not on the shadow stack
- Libraries cannot use exception handlers that jump to unlisted continuation addresses
- Non-CET-compatible native libraries loaded via P/Invoke may cause process termination

**Mitigation:** If a native library is incompatible:
```xml
<!-- Opt out of CET in project file -->
<PropertyGroup>
  <CETCompat>false</CETCompat>
</PropertyGroup>
```

> **Warning:** Disabling CET removes hardware-enforced control-flow integrity, reducing protection against ROP (return-oriented programming) and JOP (jump-oriented programming) exploits. Only disable CET after confirming the specific native library is incompatible, and re-enable it once the library is updated. Prefer per-application opt-out via Windows Security / group policy over a project-wide setting when possible.
