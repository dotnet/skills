---
name: migrate-dotnet10-to-dotnet11
description: >
  Migrate a .NET 10 project or solution to .NET 11 and resolve all breaking changes.
  This is a MIGRATION skill — use it when upgrading from .NET 10 to .NET 11,
  NOT for writing new programs.
  USE FOR: upgrading TargetFramework from net10.0 to net11.0, fixing build errors
  after updating the .NET 11 SDK, resolving source-breaking and behavioral changes
  in .NET 11 runtime, C# 15 compiler, ASP.NET Core 11, and EF Core 11, adapting to updated minimum
  hardware requirements (x86-64-v2, Arm64 LSE), and updating CI/CD pipelines and
  Dockerfiles for .NET 11.
  DO NOT USE FOR: .NET Framework migrations, upgrading from .NET 9 or earlier,
  greenfield .NET 11 projects, or cosmetic modernization unrelated to the upgrade.
  Covers the common breaking changes documented for the .NET 11 GA release (SDK 11.0.100).
license: MIT
---

# .NET 10 → .NET 11 Migration

Migrate a .NET 10 project or solution to .NET 11, systematically resolving all breaking changes. The outcome is a project targeting `net11.0` that builds cleanly, passes tests, and accounts for every behavioral, source-incompatible, and binary-incompatible change introduced in .NET 11.

> **Note:** This skill covers the common breaking changes documented for the .NET 11 GA release, not every Learn entry (for example, .NET MAUI changes are not included). Also review new build warnings and run the application's tests.

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

> **Answer directly from the loaded reference documents for information about .NET 11 breaking changes.** You may inspect the local repository (project/solution files, source code, configuration, build/test scripts) as needed to determine which changes apply. Do not fetch web pages or other external sources for breaking change information — the loaded references are the authoritative source. Focus on identifying which breaking changes apply and providing concrete fixes.
>
> **Commit strategy:** Commit at each logical boundary — after updating the TFM (Step 2), after resolving build errors (Step 3), after addressing behavioral changes (Step 4), and after updating infrastructure (Step 5). This keeps each commit focused and reviewable.

### Step 1: Assess the project

1. Identify how the project is built and tested. Look for build scripts, `.sln`/`.slnx` files, or individual `.csproj` files.
2. Run `dotnet --version` to confirm the .NET 11 SDK is installed. If it is not, stop and inform the user.
3. Determine which technology areas the project uses by examining:
   - **SDK attribute**: `Microsoft.NET.Sdk.Web` → ASP.NET Core; `Microsoft.NET.Sdk.WindowsDesktop` with `<UseWPF>` or `<UseWindowsForms>` → WPF/WinForms
   - **PackageReferences**: `Microsoft.EntityFrameworkCore.*` → EF Core; `Microsoft.EntityFrameworkCore.Cosmos` → Cosmos DB provider
   - **Dockerfile presence** → Container changes relevant
   - **Cryptography API usage** → DSA on macOS affected; AIA cert download changes relevant
   - **Compression API usage** → DeflateStream/GZipStream/ZipArchive changes relevant
   - **TAR API usage** → Header checksum validation and HardLink entry changes relevant
   - **`NamedPipeClientStream` usage with `SafePipeHandle`** → SYSLIB0063 constructor obsoletion relevant
   - **`BackgroundService` usage** → Unhandled exceptions now stop the host
   - **`Microsoft.OpenApi` direct usage** → v3 API breaking changes in ASP.NET Core OpenAPI
   - **EF Core SQL Server with Entra ID auth** → SqlClient 7.0 auth dependency changes
   - **NativeAOT native libraries on Unix** → Output filename prefix changed
   - **`double`/`float` to `decimal` conversions or `Math.Round(value, digits)`** (money, quantities, rates) → Conversion and rounding results changed at run time
   - **Explicit `PackageReference` to one of the nine `Microsoft.Extensions` packages now in the shared framework** (listed in the core libraries reference) → NU1510
   - **Blazor forms (`EditForm`, `EditContext`)** → `EditContext.Validate()` obsolete; hidden `.validation-message` placeholders in static SSR
   - **Third-party Blazor component libraries** → Check for .NET 11 builds; removed obsolete Blazor APIs break older binaries at run time
   - **Form POST endpoints** (Blazor SSR, Minimal APIs binding forms, MVC with antiforgery), especially when removing `app.UseAntiforgery()` or accepting cross-origin form posts → Automatic CSRF protection
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
| `references/csharp-compiler-dotnet10to11.md` | Always (C# 15 compiler breaking changes) |
| `references/core-libraries-dotnet10to11.md` | Always (applies to all .NET 11 projects) |
| `references/sdk-msbuild-dotnet10to11.md` | Always (SDK and build tooling changes) |
| `references/aspnetcore-dotnet10to11.md` | Project uses ASP.NET Core (Blazor, forms and antiforgery, OpenAPI, Kestrel) |
| `references/efcore-dotnet10to11.md` | Project uses Entity Framework Core |
| `references/cryptography-dotnet10to11.md` | Project uses cryptography APIs, mTLS, HttpClient metrics, or targets macOS |
| `references/runtime-jit-dotnet10to11.md` | Deploying to older hardware, embedded devices, or using NativeAOT |

**Check these first.** They are the most common new warnings in .NET 11 and fail builds that use `TreatWarningsAsErrors`:

- **`EditContext.Validate()` is obsolete (CS0618):** Replace it with `await editContext.ValidateAsync()` and make the callers async; the change spreads upward through form components. A `Task<bool>` is never `false`, so rewrite checks like `editContext?.Validate() is false` rather than only swapping the method name. See `references/aspnetcore-dotnet10to11.md`.
- **Blazor analyzers BL0011 to BL0016:** New warnings, enabled by default. BL0012 ignores other callers of the flagged method, so check every caller before removing a `StateHasChanged()` call. See the ASP.NET Core reference for BL0016 guard rules.
- **NU1510:** Explicit `PackageReference` items for the nine `Microsoft.Extensions` packages that moved into the shared framework (such as `Microsoft.Extensions.Logging.Abstractions` and `Microsoft.Extensions.Options`). Remove them in projects that target only `net11.0`.
- **Removed obsolete Blazor and MVC APIs:** `WebEventCallbackFactoryEventArgsExtensions`, `RemoteBrowserFileStreamOptions`, `SetCompatibilityVersion`, and others no longer exist. Use the replacements in the ASP.NET Core reference.
- **New obsoletions SYSLIB0064, SYSLIB0065, and SYSLIB0066:** `RSACryptoServiceProvider.Encrypt/Decrypt(byte[], bool)`, the `AsnEncodedData.RawData` setter, and `IStartupValidator`.
- **Test projects using `Newtonsoft.Json`:** `Microsoft.NET.Test.Sdk` no longer brings it in transitively. Add an explicit `PackageReference`.

Then work through the remaining build errors systematically. Common patterns:

1. **C# 15 Span collection expression safe-context** — Collection expressions of `Span<T>`/`ReadOnlySpan<T>` type now have `declaration-block` safe-context. Code assigning span collection expressions to variables in outer scopes will error. Use array type or move the expression to the correct scope.

2. **`ref readonly` delegates/local functions need `InAttribute`** — If synthesizing delegates from `ref readonly`-returning methods or using `ref readonly` local functions, ensure `System.Runtime.InteropServices.InAttribute` is available.

3. **`nameof(this.)` in attributes** — Remove `this.` qualifier; use `nameof(P)` instead of `nameof(this.P)`.

4. **`with()` in collection expressions (C# 15)** — `with(...)` is now treated as constructor arguments, not a method call. Use `@with(...)` to call a method named `with`.

5. **Dynamic `&&`/`||` with interface operand** — Interface types as left operand of `&&`/`||` with `dynamic` right operand now errors at compile time. Cast to concrete type or `dynamic`.

6. **EF Core Cosmos sync I/O removal** — `ToList()`, `SaveChanges()`, etc. on Cosmos provider always throw. Convert to async equivalents.

7. **SYSLIB0063: `NamedPipeClientStream` `isConnected` parameter obsoleted** — The constructor overload taking `bool isConnected` is obsoleted. Remove the `isConnected` argument and use the new 3-parameter constructor. Projects with `TreatWarningsAsErrors` will fail to build.

8. **`when` switch-expression-arm parsing** — `(X.Y) when` is now parsed as a constant pattern with a `when` clause instead of a cast expression, which can cause existing code to fail to compile or change meaning. Review switch expressions using `when` and adjust syntax as needed.

9. **Microsoft.OpenApi v3 breaking changes** — `Microsoft.AspNetCore.OpenApi` now depends on `Microsoft.OpenApi` 3.x. Code using `Microsoft.OpenApi` types directly (`OpenApiDocument`, `OpenApiSchema`, etc.) will have compile errors. Follow the v3 upgrade guide.

10. **EF Core Design package no longer transitive** — `Microsoft.EntityFrameworkCore.Tools` and `.Tasks` no longer depend on `.Design`. Add an explicit `PackageReference` if needed.

11. **EFOptimizeContext MSBuild property removed** — Replace with `<EFScaffoldModelStage>` and `<EFPrecompileQueriesStage>`.

### Step 4: Address behavioral changes

These changes compile successfully but alter runtime behavior. Review each one and determine impact.

**Check these first.** They produce no build warning and only show at run time:

- **Decimal and BigInteger floating-point conversions are correctly rounded:** `(decimal)1.23` (a `double`) was `1.23` in .NET 10 and is `1.229999999999999982236431606` in .NET 11. Search for `(decimal)` casts, `Convert.ToDecimal(`, and `new decimal(` on `double`/`float` inputs, especially in money code and decimal-place validation. Use `m` literals in code and test data, or round to 15 significant digits where the old result is required. Review `Math.Round(value, digits)` in the same pass: it now returns correctly rounded results. See `references/core-libraries-dotnet10to11.md`.
- **Third-party Blazor component libraries:** libraries compiled against an earlier ASP.NET Core that still call removed obsolete Blazor APIs throw `TypeLoadException` or `MissingMethodException` when they render. Upgrade them to .NET 11 versions and run UI or render tests.
- **Hidden `.validation-message` placeholders (Blazor static SSR):** a valid field now renders an empty `.validation-message` element with the `hidden` attribute. Scripts, CSS, and UI tests that treat its presence as "invalid" must use `.validation-message:not([hidden])`.

1. **DeflateStream/GZipStream empty payload** — Now writes headers and footers even for empty payloads. If your code checks for zero-length output, update the check.

2. **MemoryStream maximum capacity** — Maximum capacity updated and exception behavior changed. Review code that creates large MemoryStreams or relies on specific exception types.

3. **TAR header checksum validation** — TAR-reading APIs now verify checksums. Corrupted or hand-crafted TAR files may now fail to read.

4. **ZipArchive.CreateAsync eager loading** — `ZipArchive.CreateAsync` eagerly loads entries. May affect memory usage for large archives.

5. **Environment.TickCount consistency** — Made consistent with Windows timeout behavior. Code relying on specific tick count behavior may need adjustment.

6. **DSA removed from macOS** — DSA cryptographic operations throw on macOS. Use a different algorithm (RSA, ECDSA).

7. **Japanese Calendar minimum date** — Minimum supported date corrected. Code using very early Japanese Calendar dates may be affected.

8. **Minimum hardware requirements** — x86/x64 baseline moved to `x86-64-v2`; Windows Arm64 requires `LSE`. Verify deployment targets meet requirements.

9. **Mono launch target for .NET Framework** — No longer set automatically. If using Mono for .NET Framework apps on Linux, specify explicitly.

10. **Unhandled BackgroundService exceptions stop the host** — Exceptions from `ExecuteAsync()` now propagate and crash the host. Add try/catch in background services that should not bring down the application.

11. **ZipArchive CRC32 validation** — ZIP reads now validate CRC32 checksums. Corrupt or truncated archives that previously succeeded will now throw `InvalidDataException`.

12. **TarWriter emits HardLink entries** — Hard-linked files are now written as `HardLink` entries instead of duplicated data. Consumers of .NET-produced tar archives must handle `HardLink` entries.

13. **AIA certificate downloads disabled** — Server-side client-certificate validation no longer downloads intermediate CAs via AIA by default. Pre-install the full chain or have clients send intermediates.

14. **Blazor Virtualize OverscanCount default changed** — Default `OverscanCount` changed from 3 to 15. Set explicitly if performance-sensitive.

15. **Microsoft.Data.SqlClient 7.0 — Entra ID auth separated** — Azure/Entra ID authentication dependencies removed from the core SqlClient package. Add `Microsoft.Data.SqlClient.Extensions.Azure` if using Entra ID auth.

16. **SqlVector&lt;T&gt; excluded from SELECT** — Vector properties are no longer auto-loaded. Use explicit projections to include vector values.

17. **SQLitePCLRaw encryption bundles removed** — `bundle_e_sqlcipher` and other encryption bundle packages removed in SQLitePCLRaw 3.0.

18. **NativeAOT Unix native library `lib` prefix** — Output filenames now include `lib` prefix on Linux/macOS (e.g., `libMyLib.so`).

19. **Automatic cross-origin CSRF protection (ASP.NET Core):** `WebApplication.CreateBuilder` adds middleware that checks `Sec-Fetch-Site` and `Origin` on form-handling endpoints. Apps that run `app.UseAntiforgery()` keep their .NET 10 behavior. Without token validation (for example after removing `UseAntiforgery()`), cross-origin form posts return `400 Bad Request`; allow legitimate callers with a CORS policy that calls `.AllowCredentials()`.

20. **Also review** (details in the references): saturating floating-point to small integer casts, `IHost.RunAsync` failing when a `BackgroundService` fails, empty strings in chained configuration, DI disposal `AggregateException`, Unix named mutex scope, OpenAPI multiple response types per status code, stricter Kestrel HTTP/2 and HTTP/3 header rules, OpenTelemetry HTTP tags, the Linux AIA fetch limit, and NuGet restore no longer using higher versions from the global packages folder.

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

2. **CI/CD pipelines**: Update SDK version references. If using `global.json`, update the `sdk.version` in your existing file while preserving other keys (such as `rollForward` and test configuration):
   ```diff
    {
      "sdk": {
   -    "version": "10.0.100",
   -    "rollForward": "latestFeature"
   +    "version": "11.0.100",
   +    "rollForward": "latestFeature"
      },
      "otherSettings": {
        "...": "..."
      }
    }
   ```

3. **Hardware deployment targets**: Verify all deployment targets meet the updated minimum hardware requirements (x86-64-v2 for x86/x64, LSE for Windows Arm64).

4. **Shared libraries also used by .NET Framework apps**: When multi-targeted libraries move their `Microsoft.Extensions.*` or `System.*` package references to 11.0.x, .NET Framework apps that maintain `bindingRedirect` entries by hand must redirect to the new assembly versions. Prefer automatic binding redirects; a build with no MSB3276 or MSB3277 warnings is a useful check. See https://learn.microsoft.com/en-us/dotnet/framework/configure-apps/redirect-assembly-versions

### Step 6: Verify

1. Run a full clean build: `dotnet build --no-incremental`
2. Run all tests: `dotnet test`
3. If the application is containerized, build and test the container image
4. Smoke-test the application, paying special attention to:
   - Compression behavior with empty streams
   - TAR file reading (checksum validation and HardLink entries)
   - EF Core Cosmos DB operations (must be async)
   - DSA usage on macOS
   - Memory-intensive MemoryStream usage
   - Span collection expression assignments
   - BackgroundService exception handling
   - mTLS / client certificate chain validation
   - EF Core SQL Server with Entra ID authentication
   - NativeAOT output filenames on Unix
   - Money and quantity calculations that convert `double` to `decimal` or use `Math.Round(value, digits)`
   - Form POSTs (cross-origin posts, and requests through a reverse proxy) if token-based antiforgery is not used
   - Blazor forms validation (`ValidateAsync`), validation message styling and scripts, and third-party component libraries
5. Review the diff and ensure no unintended behavioral changes were introduced

## Reference Documents

The `references/` folder contains detailed breaking change information organized by technology area. Load only the references relevant to the project being migrated:

| Reference file | When to load |
|----------------|-------------|
| `references/csharp-compiler-dotnet10to11.md` | Always (C# 15 compiler breaking changes) |
| `references/core-libraries-dotnet10to11.md` | Always (applies to all .NET 11 projects) |
| `references/sdk-msbuild-dotnet10to11.md` | Always (SDK and build tooling changes) |
| `references/aspnetcore-dotnet10to11.md` | Project uses ASP.NET Core (Blazor, forms and antiforgery, OpenAPI, Kestrel) |
| `references/efcore-dotnet10to11.md` | Project uses Entity Framework Core |
| `references/cryptography-dotnet10to11.md` | Project uses cryptography APIs, mTLS, HttpClient metrics, or targets macOS |
| `references/runtime-jit-dotnet10to11.md` | Deploying to older hardware, embedded devices, or using NativeAOT |
