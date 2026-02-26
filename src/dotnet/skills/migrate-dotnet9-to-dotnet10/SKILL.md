---
name: migrate-dotnet9-to-dotnet10
description: >
  Migrate a .NET 9 project or solution to .NET 10 and resolve all breaking changes.
  USE FOR: upgrading TargetFramework from net9.0 to net10.0, fixing build errors and
  behavioral changes introduced in .NET 10 / C# 14 / ASP.NET Core 10 / EF Core 10,
  updating Dockerfiles for the Debian-to-Ubuntu container image change, resolving
  new obsoletion warnings (SYSLIB0058–SYSLIB0062), adapting to SDK and NuGet tooling
  changes, and migrating from System.Linq.Async to the built-in AsyncEnumerable.
  DO NOT USE FOR: major framework migrations (e.g., .NET Framework to .NET 10),
  upgrading from .NET 8 or earlier (address intermediate breaking changes first),
  greenfield projects that start on .NET 10, or purely cosmetic code modernization
  unrelated to the version upgrade.
  LOADS REFERENCES: core-libraries, sdk-msbuild, aspnet-core, efcore, cryptography,
  extensions-hosting, serialization-networking, winforms-wpf, containers-interop
  (loaded selectively based on project type).
---

# .NET 9 → .NET 10 Migration

Migrate a .NET 9 project or solution to .NET 10, systematically resolving all breaking changes. The outcome is a project targeting `net10.0` that builds cleanly, passes tests, and accounts for every behavioral, source-incompatible, and binary-incompatible change introduced in the .NET 10 release.

## When to Use

- Upgrading `TargetFramework` from `net9.0` to `net10.0`
- Resolving build errors or new warnings after updating the .NET 10 SDK
- Adapting to behavioral changes in .NET 10 runtime, ASP.NET Core 10, or EF Core 10
- Updating CI/CD pipelines, Dockerfiles, or deployment scripts for .NET 10
- Migrating from the community `System.Linq.Async` package to the built-in `System.Linq.AsyncEnumerable`

## When Not to Use

- The project already targets `net10.0` and builds cleanly — migration is done
- Upgrading from .NET 8 or earlier — address the .NET 8→9 breaking changes first
- Migrating from .NET Framework — that is a separate, larger effort
- Greenfield projects that start on .NET 10 (no migration needed)

## Inputs

| Input | Required | Description |
|-------|----------|-------------|
| Project or solution path | Yes | The `.csproj`, `.sln`, or `.slnx` entry point to migrate |
| Build command | No | How to build (e.g., `dotnet build`, a repo build script). Auto-detect if not provided |
| Test command | No | How to run tests (e.g., `dotnet test`). Auto-detect if not provided |
| Project type hints | No | Whether the project uses ASP.NET Core, EF Core, WinForms, WPF, containers, etc. Auto-detect from PackageReferences and SDK attributes if not provided |

## Workflow

> **Commit strategy:** Commit at each logical boundary — after updating the TFM (Step 2), after resolving build errors (Step 3), after addressing behavioral changes (Step 4), and after updating infrastructure (Step 5). This keeps each commit focused and reviewable.

### Step 1: Assess the project

1. Identify how the project is built and tested. Look for build scripts, `.sln`/`.slnx` files, or individual `.csproj` files.
2. Run `dotnet --version` to confirm the .NET 10 SDK is installed. If it is not, stop and inform the user.
3. Determine which technology areas the project uses by examining:
   - **SDK attribute**: `Microsoft.NET.Sdk.Web` → ASP.NET Core; `Microsoft.NET.Sdk.WindowsDesktop` with `<UseWPF>` or `<UseWindowsForms>` → WPF/WinForms
   - **PackageReferences**: `Microsoft.EntityFrameworkCore.*` → EF Core; `Microsoft.Data.Sqlite` → Sqlite; `Microsoft.Extensions.Hosting` → Generic Host / BackgroundService
   - **Dockerfile presence** → Container changes relevant
   - **P/Invoke or native interop usage** → Interop changes relevant
   - **`System.Linq.Async` package reference** → AsyncEnumerable migration needed
   - **`System.Text.Json` usage with polymorphism** → Serialization changes relevant
4. Record which reference documents are relevant (see the reference loading table in Step 3).
5. Do a **clean build** (`dotnet build --no-incremental` or delete `bin`/`obj`) on the current `net9.0` target to establish a clean baseline. Record any pre-existing warnings.

### Step 2: Update the Target Framework

1. In each `.csproj` (or `Directory.Build.props` if centralized), change:
   ```xml
   <TargetFramework>net9.0</TargetFramework>
   ```
   to:
   ```xml
   <TargetFramework>net10.0</TargetFramework>
   ```
   For multi-targeted projects, add `net10.0` to `<TargetFrameworks>` or replace `net9.0`.

2. Update all `Microsoft.Extensions.*`, `Microsoft.AspNetCore.*`, `Microsoft.EntityFrameworkCore.*`, and other Microsoft package references to their 10.0.x versions. If using Central Package Management (`Directory.Packages.props`), update versions there.

3. Run `dotnet restore`. Watch for:
   - **NU1510**: Direct references pruned by NuGet — the package may be included in the shared framework now. Remove the explicit `<PackageReference>` if so.
   - **PackageReference without a version now raises an error** — every `<PackageReference>` must have a `Version` (or use CPM).
   - **NuGet auditing of transitive packages** (`dotnet restore` now audits transitive deps) — review any new vulnerability warnings.

4. Run a clean build. Collect all errors and new warnings. These will be addressed in Step 3.

### Step 3: Resolve build errors and source-incompatible changes

Work through compilation errors and new warnings systematically. Load the appropriate reference documents based on the project type:

| If the project uses… | Load reference |
|-----------------------|----------------|
| Any .NET 10 project | `references/core-libraries.md` |
| Any .NET 10 project | `references/sdk-msbuild.md` |
| ASP.NET Core | `references/aspnet-core.md` |
| Entity Framework Core | `references/efcore.md` |
| Cryptography APIs | `references/cryptography.md` |
| Microsoft.Extensions.Hosting, BackgroundService, configuration | `references/extensions-hosting.md` |
| System.Text.Json, XmlSerializer, HttpClient, MailAddress, Uri | `references/serialization-networking.md` |
| Windows Forms or WPF | `references/winforms-wpf.md` |
| Docker containers, single-file apps, native interop | `references/containers-interop.md` |

**Common source-incompatible changes to check for:**

1. **`System.Linq.Async` conflicts** — Remove the `System.Linq.Async` package reference or upgrade to v7.0.0. If consumed transitively, add `<ExcludeAssets>compile</ExcludeAssets>`. Rename `SelectAwait` calls to `Select` where needed.

2. **New obsoletion warnings (SYSLIB0058–SYSLIB0062)**:
   - `SYSLIB0058`: Replace `SslStream.KeyExchangeAlgorithm`/`CipherAlgorithm`/`HashAlgorithm` with `NegotiatedCipherSuite`
   - `SYSLIB0059`: Replace `SystemEvents.EventsThreadShutdown` with `AppDomain.ProcessExit`
   - `SYSLIB0060`: Replace `Rfc2898DeriveBytes` constructors with `Rfc2898DeriveBytes.Pbkdf2`
   - `SYSLIB0061`: Replace `Queryable.MaxBy`/`MinBy` overloads taking `IComparer<TSource>` with ones taking `IComparer<TKey>`
   - `SYSLIB0062`: Replace `XsltSettings.EnableScript` usage

3. **C# 14 overload resolution with span parameters** — Expression trees containing `.Contains()` on arrays may now bind to `MemoryExtensions.Contains` instead of `Enumerable.Contains`. Fix by casting to `IEnumerable<T>` or using explicit static invocations.

4. **ASP.NET Core obsoletions** (if applicable):
   - `WebHostBuilder`, `IWebHost`, `WebHost` are obsolete — migrate to `Host.CreateDefaultBuilder` or `WebApplication.CreateBuilder`
   - `IActionContextAccessor` / `ActionContextAccessor` obsolete
   - `WithOpenApi` extension method deprecated
   - `IncludeOpenAPIAnalyzers` property deprecated
   - `IPNetwork` and `ForwardedHeadersOptions.KnownNetworks` obsolete
   - Razor runtime compilation is obsolete
   - `Microsoft.Extensions.ApiDescription.Client` package deprecated

5. **SDK changes**:
   - `dotnet new sln` now defaults to SLNX format — use `--format sln` if the old format is needed
   - Double quotes in file-level directives are disallowed
   - `dnx.ps1` removed from .NET SDK
   - `project.json` no longer supported in `dotnet restore`

6. **EF Core source changes** (if applicable) — See `references/efcore.md` for:
   - `ExecuteUpdateAsync` now accepts a regular lambda (expression tree construction code must be rewritten)
   - `IDiscriminatorPropertySetConvention` signature changed
   - `IRelationalCommandDiagnosticsLogger` methods add `logCommandText` parameter

7. **WinForms/WPF source changes** (if applicable):
   - Applications referencing both WPF and WinForms must disambiguate `MenuItem` and `ContextMenu` types
   - Renamed parameter in `HtmlElement.InsertAdjacentElement`
   - Empty `ColumnDefinitions` and `RowDefinitions` are disallowed in WPF

8. **Cryptography source changes** (if applicable):
   - `MLDsa` and `SlhDsa` members renamed from `SecretKey` to `PrivateKey`
   - `CoseSigner.Key` can now be null — check for null before use

Build again after each batch of fixes. Repeat until the build is clean.

### Step 4: Address behavioral changes

Behavioral changes do not cause build errors but may change runtime behavior. Review each applicable item and determine whether the previous behavior was relied upon.

**High-impact behavioral changes (check first):**

1. **SIGTERM signal handling removed** — The .NET runtime no longer registers default SIGTERM handlers. If you rely on `AppDomain.ProcessExit` or `AssemblyLoadContext.Unloading` being raised on SIGTERM:
   - ASP.NET Core and Generic Host apps are unaffected (they register their own handlers)
   - Console apps and containerized apps without Generic Host must register `PosixSignalRegistration.Create(PosixSignal.SIGTERM, _ => Environment.Exit(0))` explicitly

2. **BackgroundService.ExecuteAsync runs entirely on a background thread** — The synchronous portion before the first `await` no longer blocks startup. If startup ordering matters, move that code to `StartAsync` or the constructor, or implement `IHostedLifecycleService`.

3. **Configuration null values are now preserved** — JSON `null` values are no longer converted to empty strings. Properties initialized with non-default values will be overwritten with `null`. Review configuration binding code.

4. **Microsoft.Data.Sqlite DateTimeOffset changes** (all High impact):
   - `GetDateTimeOffset` without an offset now assumes UTC (previously assumed local)
   - Writing `DateTimeOffset` into REAL columns now converts to UTC first
   - `GetDateTime` with an offset now returns UTC with `DateTimeKind.Utc`
   - Mitigation: `AppContext.SetSwitch("Microsoft.Data.Sqlite.Pre10TimeZoneHandling", true)` as a temporary workaround

5. **EF Core parameterized collections** — `.Contains()` on collections now uses multiple scalar parameters instead of JSON/OPENJSON. May affect query performance for large collections. Mitigation: `UseParameterizedCollectionMode(ParameterTranslationMode.Parameter)` to revert.

6. **EF Core JSON data type on Azure SQL** — Azure SQL and compatibility level ≥170 now use the `json` data type instead of `nvarchar(max)`. A migration will be generated to alter existing columns. Mitigation: set compatibility level to 160 or use `HasColumnType("nvarchar(max)")` explicitly.

7. **System.Text.Json property name conflict validation** — Polymorphic types with properties conflicting with metadata names (`$type`, `$id`, `$ref`) now throw `InvalidOperationException`. Add `[JsonIgnore]` to conflicting properties.

**Other behavioral changes to review:**

- `BufferedStream.WriteByte` no longer implicitly flushes — add explicit `Flush()` calls if needed
- Default trace context propagator updated to W3C standard
- `DriveInfo.DriveFormat` returns actual Linux filesystem type names
- LDAP `DirectoryControl` parsing is more stringent
- Default .NET container images switched from Debian to Ubuntu (Debian images no longer shipped)
- Single-file apps no longer look for native libraries in executable directory by default
- `DllImportSearchPath.AssemblyDirectory` only searches the assembly directory
- `MailAddress` enforces validation for consecutive dots
- Streaming HTTP responses enabled by default in browser HTTP clients
- `Uri` length limits removed
- Cookie login redirects disabled for known API endpoints (ASP.NET Core)
- `XmlSerializer` no longer ignores `[Obsolete]` properties
- `dotnet restore` audits transitive packages
- `dotnet watch` logs to stderr instead of stdout
- `dotnet` CLI commands log non-command-relevant data to stderr
- Various NuGet behavioral changes (see `references/sdk-msbuild.md`)
- `StatusStrip` uses System RenderMode by default (WinForms)
- `TreeView` checkbox image truncation fix (WinForms)
- `DynamicResource` incorrect usage causes crash (WPF)

### Step 5: Update infrastructure

1. **Dockerfiles**: Update base images. Default tags now use Ubuntu instead of Debian. Debian images are no longer shipped for .NET 10.
   ```dockerfile
   # Before
   FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
   FROM mcr.microsoft.com/dotnet/aspnet:9.0
   # After
   FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
   FROM mcr.microsoft.com/dotnet/aspnet:10.0
   ```

2. **CI/CD pipelines**: Update SDK version references. If using `global.json`, update:
   ```json
   {
     "sdk": {
       "version": "10.0.100"
     }
   }
   ```

3. **Environment variables renamed**:
   - `DOTNET_OPENSSL_VERSION_OVERRIDE` replaces the old name
   - `DOTNET_ICU_VERSION_OVERRIDE` replaces the old name
   - `NUGET_ENABLE_ENHANCED_HTTP_RETRY` has been removed

4. **OpenSSL requirements**: OpenSSL 1.1.1 or later is now required on Unix. OpenSSL cryptographic primitives are no longer supported on macOS.

5. **Solution file format**: If `dotnet new sln` is used in scripts, note it now generates SLNX format. Pass `--format sln` if the old format is needed.

### Step 6: Verify

1. Run a full clean build: `dotnet build --no-incremental`
2. Run all tests: `dotnet test`
3. If the application is containerized, build and test the container image
4. Smoke-test the application, paying special attention to:
   - Signal handling / graceful shutdown behavior
   - Background services startup ordering
   - Configuration binding with null values
   - Date/time handling with Sqlite
   - JSON serialization with polymorphic types
   - EF Core queries using `.Contains()` on collections
5. Review the diff and ensure no unintended behavioral changes were introduced

## Reference Documents

The `references/` folder contains detailed breaking change information organized by technology area. Load only the references relevant to the project being migrated:

| Reference file | When to load |
|----------------|-------------|
| `references/core-libraries.md` | Always (applies to all .NET 10 projects) |
| `references/sdk-msbuild.md` | Always (SDK and build tooling changes) |
| `references/aspnet-core.md` | Project uses ASP.NET Core |
| `references/efcore.md` | Project uses Entity Framework Core or Microsoft.Data.Sqlite |
| `references/cryptography.md` | Project uses System.Security.Cryptography or X.509 certificates |
| `references/extensions-hosting.md` | Project uses Generic Host, BackgroundService, or Microsoft.Extensions.Configuration |
| `references/serialization-networking.md` | Project uses System.Text.Json, XmlSerializer, HttpClient, or networking APIs |
| `references/winforms-wpf.md` | Project uses Windows Forms or WPF |
| `references/containers-interop.md` | Project uses Docker containers, single-file publishing, or native interop (P/Invoke) |
