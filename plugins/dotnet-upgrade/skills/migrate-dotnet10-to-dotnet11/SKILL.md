---
name: migrate-dotnet10-to-dotnet11
description: >
  Migrate a .NET 10 project or solution to .NET 11 and resolve all breaking changes.
  This is a MIGRATION skill — use it when upgrading from .NET 10 to .NET 11,
  NOT for writing new programs.
  USE FOR: upgrading TargetFramework from net10.0 to net11.0, fixing build errors
  after updating the .NET 11 SDK, resolving source-breaking and behavioral changes
  in .NET 11 runtime, C# 15 compiler, ASP.NET Core 11, and EF Core 11, adapting to
  updated minimum hardware requirements (x86-64-v2, Arm64 LSE), and updating CI/CD
  pipelines and Dockerfiles for .NET 11.
  DO NOT USE FOR: .NET Framework migrations, upgrading from .NET 9 or earlier,
  greenfield .NET 11 projects, or cosmetic modernization unrelated to the upgrade.
  Covers the common breaking changes documented for the .NET 11 GA release
  (SDK 11.0.100).
license: MIT
---

# .NET 10 → .NET 11 Migration

Migrate a .NET 10 project or solution to .NET 11. The outcome is a project targeting `net11.0` that builds cleanly, passes tests, and accounts for every breaking change that applies to it.

> **Note:** This skill covers the common breaking changes documented for the .NET 11 GA release, not every Learn entry. For Android and Apple projects it covers platform and runtime changes, not .NET MAUI UI changes. Also review new build warnings and run the application's tests.

## When to Use

Upgrading a `net10.0` project to `net11.0`, or fixing the build errors, new warnings, and run-time changes that appear after the upgrade.

## When Not to Use

- The project already targets `net11.0` and builds cleanly: the migration is done
- The project targets .NET 9 or earlier: address the .NET 9→10 breaking changes first
- .NET Framework migrations and projects that start on .NET 11

## Inputs

Discover the solution or project files, the build and test commands, and the technology areas from the repository. Ask the user only for what the repository does not show.

## Workflow

> **Answer directly from the loaded reference documents for information about .NET 11 breaking changes.** You may inspect the local repository (project/solution files, source code, configuration, build/test scripts) as needed to determine which changes apply. Do not fetch web pages or other external sources for breaking change information — the loaded references are the authoritative source. Focus on identifying which breaking changes apply and providing concrete fixes.
>
> **Commit strategy:** Commit at each logical boundary — after updating the TFM (Step 2), after resolving build errors (Step 3), after addressing behavioral changes (Step 4), and after updating infrastructure (Step 5). This keeps each commit focused and reviewable.

### Step 1: Assess the project

1. Identify how the project is built and tested. Look for build scripts, `.sln`/`.slnx` files, or individual `.csproj` files.
2. Run `dotnet --version` to confirm the .NET 11 SDK is installed. If it is not, stop and inform the user.
3. Determine which references to load, using the table in Step 3. Check the SDK attribute (`Microsoft.NET.Sdk.Web`), `PackageReference` items (`Microsoft.EntityFrameworkCore.*` and its Cosmos, SQL Server, and SQLite providers), target frameworks (Android, iOS, tvOS, Mac Catalyst), Dockerfiles, NativeAOT, and the hardware the app is deployed to.
4. Search the code for the usages behind the run-time changes in Step 4, which no build warning reveals: `double`/`float` to `decimal` conversions and `Math.Round(value, digits)`, Blazor forms and third-party Blazor component libraries, form POST endpoints, EF Core against existing Cosmos or SQL Server data, compression, ZIP, and TAR APIs, `BackgroundService`, and client-certificate validation.
5. Do a **clean build** (`dotnet build --no-incremental` or delete `bin`/`obj`) on the current `net10.0` target to establish a clean baseline. Record any pre-existing warnings.

### Step 2: Update the Target Framework

1. In each `.csproj` (or `Directory.Build.props` if centralized), change `<TargetFramework>net10.0</TargetFramework>` to `<TargetFramework>net11.0</TargetFramework>`. For multi-targeted projects, add `net11.0` to `<TargetFrameworks>` or replace `net10.0`.

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
| `references/cryptography-dotnet10to11.md` | Project uses cryptography APIs, mTLS or X.509 chain building, HttpClient metrics, or targets macOS |
| `references/mobile-dotnet10to11.md` | Project targets Android, iOS, tvOS, or Mac Catalyst (including .NET MAUI) |
| `references/runtime-jit-dotnet10to11.md` | Deploying to older hardware, embedded devices, using NativeAOT, or casting floating-point values to small integer types |

**Check these first.** They are the most common new warnings in .NET 11 and fail builds that use `TreatWarningsAsErrors`:

- **`EditContext.Validate()` is obsolete (CS0618):** Replace it with `await editContext.ValidateAsync()` and make the callers async; the change spreads upward through form components. A `Task<bool>` is never `false`, so rewrite checks like `editContext?.Validate() is false` rather than only swapping the method name. See `references/aspnetcore-dotnet10to11.md`.
- **Blazor analyzers BL0011 to BL0016:** New warnings, enabled by default. BL0012 ignores other callers of the flagged method, so check every caller before removing a `StateHasChanged()` call. See the ASP.NET Core reference for BL0016 guard rules.
- **NU1510:** Explicit `PackageReference` items for the nine `Microsoft.Extensions` packages that moved into the shared framework (such as `Microsoft.Extensions.Logging.Abstractions` and `Microsoft.Extensions.Options`). Remove them in projects that target only `net11.0`.
- **Removed obsolete Blazor and MVC APIs:** `WebEventCallbackFactoryEventArgsExtensions`, `RemoteBrowserFileStreamOptions`, `SetCompatibilityVersion`, and others no longer exist. Use the replacements in the ASP.NET Core reference.
- **New obsoletions SYSLIB0064, SYSLIB0065, and SYSLIB0066:** `RSACryptoServiceProvider.Encrypt/Decrypt(byte[], bool)`, the `AsnEncodedData.RawData` setter, and `IStartupValidator`.
- **Test projects using `Newtonsoft.Json`:** `Microsoft.NET.Test.Sdk` no longer brings it in transitively. Add an explicit `PackageReference`.
- **Android and Apple projects:** remove `UseMonoRuntime` (`NETSDK1242`) and Mono AOT properties. An Android minimum below API 24 fails (`XA4216`); raising it drops devices, so confirm with the user.

Then work through the remaining build errors systematically. Common patterns:

1. **C# 15 Span collection expression safe-context** — Collection expressions of `Span<T>`/`ReadOnlySpan<T>` type now have `declaration-block` safe-context. Code assigning span collection expressions to variables in outer scopes will error. Use array type or move the expression to the correct scope.

2. **`ref readonly` delegates/local functions need `InAttribute`** — If synthesizing delegates from `ref readonly`-returning methods or using `ref readonly` local functions, ensure `System.Runtime.InteropServices.InAttribute` is available.

3. **`nameof(this.)` in attributes** — Remove `this.` qualifier; use `nameof(P)` instead of `nameof(this.P)`.

4. **`with()` in collection expressions (C# 15)** — `with(...)` is now treated as constructor arguments, not a method call. Use `@with(...)` to call a method named `with`. Types named `closed` or `union` also break in declarations (CS9380, CS1519, CS9370); escape them as `@closed` and `@union`.

5. **Dynamic `&&`/`||` with interface operand** — Interface types as left operand of `&&`/`||` with `dynamic` right operand now errors at compile time. Cast to concrete type or `dynamic`.

6. **SYSLIB0063: `NamedPipeClientStream` `isConnected` parameter obsoleted** — The constructor overload taking `bool isConnected` is obsoleted. Remove the `isConnected` argument and use the new 3-parameter constructor. Projects with `TreatWarningsAsErrors` will fail to build.

7. **`when` switch-expression-arm parsing** — `(X.Y) when` is now parsed as a constant pattern with a `when` clause instead of a cast expression, which can cause existing code to fail to compile or change meaning. Review switch expressions using `when` and adjust syntax as needed.

8. **Microsoft.OpenApi v3 breaking changes** — `Microsoft.AspNetCore.OpenApi` now depends on `Microsoft.OpenApi` 3.x. Code using `Microsoft.OpenApi` types directly (`OpenApiDocument`, `OpenApiSchema`, etc.) will have compile errors. Follow the v3 upgrade guide.

9. **EF Core build changes:** add `Microsoft.EntityFrameworkCore.Design` explicitly if the build relied on the Tools package, and replace `EFOptimizeContext` with `<EFScaffoldModelStage>` and `<EFPrecompileQueriesStage>`.

### Step 4: Address behavioral changes

These changes compile successfully but alter runtime behavior. Review each one and determine impact.

**Check these first.** They produce no build warning and only show at run time:

- **Decimal and BigInteger floating-point conversions are correctly rounded:** `(decimal)1.23` (a `double`) was `1.23` in .NET 10 and is `1.229999999999999982236431606` in .NET 11. Search for `(decimal)` casts, `Convert.ToDecimal(`, and `new decimal(` on `double`/`float` inputs, especially in money code and decimal-place validation. Use `m` literals in code and test data, or, where the old result is required, round through `"G15"` for a `double` source and `"G7"` for a `float` source. Review `Math.Round(value, digits)` in the same pass: it now returns correctly rounded results. See `references/core-libraries-dotnet10to11.md`.
- **Third-party Blazor component libraries:** libraries compiled against an earlier ASP.NET Core that still call removed obsolete Blazor APIs throw `TypeLoadException` or `MissingMethodException` when they render. Upgrade them to .NET 11 versions and run UI or render tests.
- **EF Core against existing data:** Cosmos synchronous calls (`ToList()`, `SaveChanges()`) always throw, so convert them to async. Cosmos also no longer preserves unmapped JSON properties (a load and save deletes them) or escapes `/ \ ? #` in ids. SQL Server defaults to compatibility level 160 (`LEAST`/`GREATEST` fail on SQL Server 2019; use `UseCompatibilityLevel(150)`).
- **OpenAPI documents default to 3.2:** set `OpenApiVersion` to `OpenApi3_1` if consumers require 3.1.
- **Hidden `.validation-message` placeholders (Blazor static SSR):** a valid field now renders an empty `.validation-message` element with the `hidden` attribute. Scripts, CSS, and UI tests that treat its presence as "invalid" must use `.validation-message:not([hidden])`.

1. **DeflateStream/GZipStream empty payload** — Now writes headers and footers even for empty payloads. Update checks for zero-length output.

2. **MemoryStream maximum capacity** — Maximum capacity updated and exception behavior changed. Review code that creates large MemoryStreams or relies on specific exception types.

3. **TAR header checksum validation** — TAR-reading APIs now verify checksums. Corrupted or hand-crafted TAR files may now fail to read.

4. **ZipArchive.CreateAsync eager loading** — `ZipArchive.CreateAsync` eagerly loads entries. May affect memory usage for large archives.

5. **DSA removed from macOS** — DSA cryptographic operations throw on macOS. Use a different algorithm (RSA, ECDSA).

6. **Minimum hardware requirements** — x86/x64 baseline moved to `x86-64-v2`; Windows Arm64 requires `LSE`. Verify deployment targets meet requirements.

7. **Unhandled BackgroundService exceptions stop the host** — Exceptions from `ExecuteAsync()` now stop the host; catch them where the app should keep running.

8. **ZipArchive CRC32 validation** — ZIP reads validate CRC32 checksums; corrupt or truncated archives throw `InvalidDataException`.

9. **TarWriter emits HardLink entries** — Hard-linked files are written as `HardLink` entries instead of duplicated data; consumers must handle them.

10. **AIA certificate downloads disabled** — Server-side client-certificate validation no longer downloads intermediate CAs via AIA by default. Pre-install the full chain or have clients send intermediates.

11. **Blazor Virtualize OverscanCount default changed** — Default `OverscanCount` changed from 3 to 15. Set explicitly if performance-sensitive. `QuickGrid` now keeps paging and sorting in the URL; several grids on one page need distinct `QueryParameterNameOptions`.

12. **EF Core provider packages:** Entra ID auth needs `Microsoft.Data.SqlClient.Extensions.Azure`, `SqlVector<T>` is no longer loaded by default, and SQLitePCLRaw bundles such as `bundle_e_sqlcipher` are unmaintained.

13. **NativeAOT Unix native library `lib` prefix** — Output filenames now include `lib` prefix on Linux/macOS (e.g., `libMyLib.so`).

14. **Automatic cross-origin CSRF protection (ASP.NET Core):** `WebApplication.CreateBuilder` adds middleware that checks `Sec-Fetch-Site` and `Origin` on form-handling endpoints. Apps that run `app.UseAntiforgery()` keep their .NET 10 behavior. Without token validation (for example after removing `UseAntiforgery()`), cross-origin form posts return `400 Bad Request`; allow legitimate callers with a CORS policy that calls `.AllowCredentials()`.

15. **Also review** (details in the references): saturating floating-point to small integer casts, `IHost.RunAsync` failing when a `BackgroundService` fails, empty strings in chained configuration, DI disposal `AggregateException`, Unix named mutex scope, OpenAPI multiple response types per status code, stricter Kestrel HTTP/2 and HTTP/3 header rules, OpenTelemetry HTTP tags, the Linux AIA fetch limit, NuGet restore no longer using higher versions from the global packages folder, `Environment.TickCount` timing, the Japanese Calendar minimum date, and the Mono launch target for .NET Framework apps.

### Step 5: Update infrastructure

1. **Dockerfiles**: Update base image tags from 10.0 to 11.0:
   ```dockerfile
   FROM mcr.microsoft.com/dotnet/sdk:11.0 AS build
   FROM mcr.microsoft.com/dotnet/aspnet:11.0
   ```

2. **CI/CD pipelines**: Update SDK version references. If using `global.json`, change `sdk.version` to `11.0.100` and preserve the other keys (such as `rollForward` and test configuration).

3. **Shared libraries also used by .NET Framework apps**: When multi-targeted libraries move their `Microsoft.Extensions.*` or `System.*` package references to 11.0.x, .NET Framework apps that maintain `bindingRedirect` entries by hand must redirect to the new assembly versions. Prefer automatic binding redirects; a build with no MSB3276 or MSB3277 warnings is a useful check. See https://learn.microsoft.com/en-us/dotnet/framework/configure-apps/redirect-assembly-versions

### Step 6: Verify

1. Run a full clean build: `dotnet build --no-incremental`
2. Run all tests: `dotnet test`
3. If the application is containerized, build and test the container image
4. Smoke-test the application, paying special attention to the technology areas identified in Step 1 and every Step 4 change that applied
5. Review the diff and ensure no unintended behavioral changes were introduced

## Reference Documents

The `references/` folder contains detailed breaking change information organized by technology area. Load only the references relevant to the project, using the table in Step 3.
