---
name: migrate-dotnet10-to-dotnet11
description: >
  Migrate a .NET 10 project or solution to .NET 11 and resolve all breaking changes.
  USE FOR: upgrading TargetFramework from net10.0 to net11.0, fixing build errors,
  compilation errors, and new warnings after updating the .NET 11 SDK, resolving
  behavioral changes introduced in .NET 11 / C# 15 / ASP.NET Core 11 / EF Core 11,
  adapting to updated minimum hardware requirements (x86-64-v2, Arm64 LSE),
  fixing C# 15 compiler breaking changes (Span collection expression safe-context,
  ref readonly delegate/local function InAttribute, with() parsing, nameof(this.)
  in attributes), resolving EF Core Cosmos DB sync I/O removal, adapting to
  obsoleted APIs (SYSLIB0063 NamedPipeClientStream isConnected parameter), and
  adapting to core library behavioral changes (DeflateStream/GZipStream empty payloads,
  MemoryStream capacity, TAR checksum validation, ZipArchive.CreateAsync).
  DO NOT USE FOR: major framework migrations (e.g., .NET Framework to .NET 11),
  upgrading from .NET 9 or earlier (address intermediate breaking changes first),
  greenfield projects that start on .NET 11, or purely cosmetic code modernization
  unrelated to the version upgrade.
  LOADS REFERENCES: csharp-compiler, core-libraries, sdk-msbuild (always loaded),
  efcore, cryptography, runtime-jit (loaded selectively based on project type).
  NOTE: .NET 11 is in preview. This skill covers breaking changes through Preview 1.
  Additional breaking changes are expected in later previews.
---

# .NET 10 → .NET 11 Migration

Migrate a .NET 10 project or solution to .NET 11, systematically resolving all breaking changes. The outcome is a project targeting `net11.0` that builds cleanly, passes tests, and accounts for every behavioral, source-incompatible, and binary-incompatible change introduced in .NET 11.

> **Note:** .NET 11 is currently in preview. This skill covers breaking changes documented through Preview 1. It will be updated as additional previews ship.

## When to Use

- Upgrading `TargetFramework` from `net10.0` to `net11.0`
- Resolving build errors or new warnings after updating the .NET 11 SDK
- Adapting to behavioral changes in .NET 11 runtime, ASP.NET Core 11, or EF Core 11
- Updating CI/CD pipelines, Dockerfiles, or deployment scripts for .NET 11
- Fixing C# 15 compiler breaking changes after SDK upgrade

## When Not to Use

- The project already targets `net11.0` and builds cleanly — migration is done
- Upgrading from .NET 9 or earlier — address the .NET 9→10 breaking changes first
- Migrating from .NET Framework — that is a separate, larger effort
- Greenfield projects that start on .NET 11 (no migration needed)

## Inputs

| Input | Required | Description |
|-------|----------|-------------|
| Project or solution path | Yes | The `.csproj`, `.sln`, or `.slnx` entry point to migrate |
| Build command | No | How to build (e.g., `dotnet build`, a repo build script). Auto-detect if not provided |
| Test command | No | How to run tests (e.g., `dotnet test`). Auto-detect if not provided |
| Project type hints | No | Whether the project uses ASP.NET Core, EF Core, Cosmos DB, etc. Auto-detect from PackageReferences and SDK attributes if not provided |

## Workflow

> **Answer directly from the loaded reference documents.** Do not search the filesystem or fetch web pages for breaking change information — the references contain the authoritative details. Focus on identifying which breaking changes apply and providing concrete fixes.
>
> **Commit strategy:** Commit at each logical boundary — after updating the TFM (Step 2), after resolving build errors (Step 3), after addressing behavioral changes (Step 4), and after updating infrastructure (Step 5). This keeps each commit focused and reviewable.

### Step 1: Assess the project

1. Identify how the project is built and tested. Look for build scripts, `.sln`/`.slnx` files, or individual `.csproj` files.
2. Run `dotnet --version` to confirm the .NET 11 SDK is installed. If it is not, stop and inform the user.
3. Determine which technology areas the project uses by examining:
   - **SDK attribute**: `Microsoft.NET.Sdk.Web` → ASP.NET Core; `Microsoft.NET.Sdk.WindowsDesktop` with `<UseWPF>` or `<UseWindowsForms>` → WPF/WinForms
   - **PackageReferences**: `Microsoft.EntityFrameworkCore.*` → EF Core; `Microsoft.EntityFrameworkCore.Cosmos` → Cosmos DB provider
   - **Dockerfile presence** → Container changes relevant
   - **Cryptography API usage** → DSA on macOS affected
   - **Compression API usage** → DeflateStream/GZipStream/ZipArchive changes relevant
   - **TAR API usage** → Header checksum validation change relevant
   - **`NamedPipeClientStream` usage with `SafePipeHandle`** → SYSLIB0063 constructor obsoletion relevant
4. Record which reference documents are relevant (see the reference loading table in Step 3).
5. Do a **clean build** (`dotnet build --no-incremental` or delete `bin`/`obj`) on the current `net10.0` target to establish a clean baseline. Record any pre-existing warnings.

### Step 2: Update the Target Framework

1. In each `.csproj` (or `Directory.Build.props` if centralized), change:
   ```xml
   <TargetFramework>net10.0</TargetFramework>
   ```
   to:
   ```xml
   <TargetFramework>net11.0</TargetFramework>
   ```
   For multi-targeted projects, add `net11.0` to `<TargetFrameworks>` or replace `net10.0`.

2. Update all `Microsoft.Extensions.*`, `Microsoft.AspNetCore.*`, `Microsoft.EntityFrameworkCore.*`, and other Microsoft package references to their 11.0.x versions. If using Central Package Management (`Directory.Packages.props`), update versions there.

3. Run `dotnet restore`. Fix any restore errors before continuing.

4. Run `dotnet build`. Capture all errors and warnings — these will be addressed in Step 3.

### Step 3: Fix source-breaking and compilation changes

Load reference documents based on the project's technology areas:

| Reference file | When to load |
|----------------|-------------|
| `references/csharp-compiler.md` | Always (C# 15 compiler breaking changes) |
| `references/core-libraries.md` | Always (applies to all .NET 11 projects) |
| `references/sdk-msbuild.md` | Always (SDK and build tooling changes) |
| `references/efcore.md` | Project uses Entity Framework Core (especially Cosmos DB provider) |
| `references/cryptography.md` | Project uses cryptography APIs or targets macOS |
| `references/runtime-jit.md` | Deploying to older hardware or embedded devices |

Work through each build error systematically. Common patterns:

1. **C# 15 Span collection expression safe-context** — Collection expressions of `Span<T>`/`ReadOnlySpan<T>` type now have `declaration-block` safe-context. Code assigning span collection expressions to variables in outer scopes will error. Use array type or move the expression to the correct scope.

2. **`ref readonly` delegates/local functions need `InAttribute`** — If synthesizing delegates from `ref readonly`-returning methods or using `ref readonly` local functions, ensure `System.Runtime.InteropServices.InAttribute` is available.

3. **`nameof(this.)` in attributes** — Remove `this.` qualifier; use `nameof(P)` instead of `nameof(this.P)`.

4. **`with()` in collection expressions (C# 15)** — `with(...)` is now treated as constructor arguments, not a method call. Use `@with(...)` to call a method named `with`.

5. **Dynamic `&&`/`||` with interface operand** — Interface types as left operand of `&&`/`||` with `dynamic` right operand now errors at compile time. Cast to concrete type or `dynamic`.

6. **EF Core Cosmos sync I/O removal** — `ToList()`, `SaveChanges()`, etc. on Cosmos provider always throw. Convert to async equivalents.

7. **SYSLIB0063: `NamedPipeClientStream` `isConnected` parameter obsoleted** — The constructor overload taking `bool isConnected` is obsoleted. Remove the `isConnected` argument and use the new 3-parameter constructor. Projects with `TreatWarningsAsErrors` will fail to build.

### Step 4: Address behavioral changes

These changes compile successfully but alter runtime behavior. Review each one and determine impact:

1. **DeflateStream/GZipStream empty payload** — Now writes headers and footers even for empty payloads. If your code checks for zero-length output, update the check.

2. **MemoryStream maximum capacity** — Maximum capacity updated and exception behavior changed. Review code that creates large MemoryStreams or relies on specific exception types.

3. **TAR header checksum validation** — TAR-reading APIs now verify checksums. Corrupted or hand-crafted TAR files may now fail to read.

4. **ZipArchive.CreateAsync eager loading** — `ZipArchive.CreateAsync` eagerly loads entries. May affect memory usage for large archives.

5. **Environment.TickCount consistency** — Made consistent with Windows timeout behavior. Code relying on specific tick count behavior may need adjustment.

6. **DSA removed from macOS** — DSA cryptographic operations throw on macOS. Use a different algorithm (RSA, ECDSA).

7. **Japanese Calendar minimum date** — Minimum supported date corrected. Code using very early Japanese Calendar dates may be affected.

8. **Minimum hardware requirements** — x86/x64 baseline moved to `x86-64-v2`; Windows Arm64 requires `LSE`. Verify deployment targets meet requirements.

9. **Mono launch target for .NET Framework** — No longer set automatically. If using Mono for .NET Framework apps on Linux, specify explicitly.

10. **`with()` switch-expression-arm parsing** — `(X.Y) when` now parsed as constant pattern + when clause instead of cast.

### Step 5: Update infrastructure

1. **Dockerfiles**: Update base images from 10.0 to 11.0:
   ```dockerfile
   # Before
   FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
   FROM mcr.microsoft.com/dotnet/aspnet:10.0
   # After
   FROM mcr.microsoft.com/dotnet/sdk:11.0 AS build
   FROM mcr.microsoft.com/dotnet/aspnet:11.0
   ```

2. **CI/CD pipelines**: Update SDK version references. If using `global.json`, update:
   ```json
   {
     "sdk": {
       "version": "11.0.100-preview.1"
     }
   }
   ```

3. **Hardware deployment targets**: Verify all deployment targets meet the updated minimum hardware requirements (x86-64-v2 for x86/x64, LSE for Windows Arm64).

### Step 6: Verify

1. Run a full clean build: `dotnet build --no-incremental`
2. Run all tests: `dotnet test`
3. If the application is containerized, build and test the container image
4. Smoke-test the application, paying special attention to:
   - Compression behavior with empty streams
   - TAR file reading
   - EF Core Cosmos DB operations (must be async)
   - DSA usage on macOS
   - Memory-intensive MemoryStream usage
   - Span collection expression assignments
5. Review the diff and ensure no unintended behavioral changes were introduced

## Reference Documents

The `references/` folder contains detailed breaking change information organized by technology area. Load only the references relevant to the project being migrated:

| Reference file | When to load |
|----------------|-------------|
| `references/csharp-compiler.md` | Always (C# 15 compiler breaking changes) |
| `references/core-libraries.md` | Always (applies to all .NET 11 projects) |
| `references/sdk-msbuild.md` | Always (SDK and build tooling changes) |
| `references/efcore.md` | Project uses Entity Framework Core (especially Cosmos DB provider) |
| `references/cryptography.md` | Project uses cryptography APIs or targets macOS |
| `references/runtime-jit.md` | Deploying to older hardware or embedded devices |
