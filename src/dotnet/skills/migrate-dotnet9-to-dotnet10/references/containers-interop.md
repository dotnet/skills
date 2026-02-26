# Containers, Interop, and Deployment Breaking Changes (.NET 10)

These changes affect Docker containers, single-file apps, native interop (P/Invoke), and deployment scenarios.

## Containers

### Default .NET images use Ubuntu (Debian images discontinued)

**Impact: Medium.** Default .NET container image tags now reference Ubuntu 24.04 (Noble Numbat) instead of Debian. Debian-based images are no longer provided for .NET 10.

```dockerfile
# .NET 10 images — all Ubuntu-based
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
FROM mcr.microsoft.com/dotnet/aspnet:10.0
FROM mcr.microsoft.com/dotnet/runtime:10.0

# Explicit Ubuntu tag (same as default)
FROM mcr.microsoft.com/dotnet/sdk:10.0-noble
```

**What to check:**
- Dockerfile `RUN apt-get` commands — Ubuntu and Debian share `apt` but may differ in available packages and versions
- Native library dependencies that were Debian-specific
- Custom base image layers that assumed Debian
- CI/CD scripts that referenced Debian-specific image tags

**If you need Debian:** Create custom images following [Microsoft's guide for installing .NET in a Dockerfile](https://github.com/dotnet/dotnet-docker/blob/main/documentation/scenarios/installing-dotnet.md).

## Interop

### Single-file apps no longer look for native libraries in executable directory

In single-file apps, the application directory is no longer automatically added to `NATIVE_DLL_SEARCH_DIRECTORIES`. The directory is only searched when `DllImportSearchPath.AssemblyDirectory` is included in the search paths (which is the default for P/Invokes without explicit search paths).

**Breaking scenario:** P/Invokes with `[DefaultDllImportSearchPaths]` that explicitly exclude `AssemblyDirectory`:
```csharp
// This no longer finds "lib" in the app directory:
[DllImport("lib")]
[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
static extern void Method();

// Fix: Add AssemblyDirectory to the search paths:
[DllImport("lib")]
[DefaultDllImportSearchPaths(DllImportSearchPath.System32 | DllImportSearchPath.AssemblyDirectory)]
static extern void Method();
```

For NativeAOT on non-Windows, the `rpath` is no longer set to the application directory. Add explicit linker arguments if needed.

### DllImportSearchPath.AssemblyDirectory only searches the assembly directory

`DllImportSearchPath.AssemblyDirectory` now strictly searches only the assembly directory, not additional directories that were previously included.

### Casting IDispatchEx COM object to IReflect fails

Casting a COM object that implements `IDispatchEx` to `IReflect` now fails. Use `dynamic` or explicit COM interop interfaces instead.

## Other Changes

- **Globalization**: Environment variable renamed to `DOTNET_ICU_VERSION_OVERRIDE`
- **Reflection**: `[DynamicallyAccessedMembers]` annotations on `IReflect.InvokeMember`, `Type.FindMembers` are more restrictive (new trim warnings possible)
- **Reflection**: `Type.MakeGenericSignatureType` arguments validated more strictly
- **VS Code**: `dotnet.acquire` API no longer always downloads latest — pin specific versions
