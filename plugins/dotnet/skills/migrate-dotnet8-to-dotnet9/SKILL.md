---
name: migrate-dotnet8-to-dotnet9
description: >
  Migrate a .NET 8 project or solution to .NET 9 and resolve all breaking changes.
  USE FOR: upgrading TargetFramework from net8.0 to net9.0, fixing build errors,
  compilation errors, and new warnings after updating the .NET 9 SDK, resolving
  behavioral changes introduced in .NET 9 / C# 13 / ASP.NET Core 9 / EF Core 9,
  replacing BinaryFormatter usage (now always throws), resolving new obsoletion
  warnings (SYSLIB0054–SYSLIB0057), adapting to params span overload resolution
  changes, fixing C# 13 compiler breaking changes (InlineArray on records,
  iterator safe context, collection expression overloads), updating HttpClientFactory
  code for the SocketsHttpHandler default, and resolving EF Core 9 breaking changes
  (pending model changes exception, Cosmos DB discriminator/$type rename).
  DO NOT USE FOR: major framework migrations (e.g., .NET Framework to .NET 9),
  upgrading from .NET 7 or earlier (address intermediate breaking changes first),
  greenfield projects that start on .NET 9, or purely cosmetic code modernization
  unrelated to the version upgrade.
  LOADS REFERENCES: csharp-compiler, core-libraries, sdk-msbuild (always loaded),
  aspnet-core, efcore, cryptography, serialization-networking, winforms-wpf,
  containers-interop, deployment-runtime (loaded selectively based on project type).
---

# .NET 8 → .NET 9 Migration

Migrate a .NET 8 project or solution to .NET 9, systematically resolving all breaking changes. The outcome is a project targeting `net9.0` that builds cleanly, passes tests, and accounts for every behavioral, source-incompatible, and binary-incompatible change introduced in the .NET 9 release.

## When to Use

- Upgrading `TargetFramework` from `net8.0` to `net9.0`
- Resolving build errors or new warnings after updating the .NET 9 SDK
- Adapting to behavioral changes in .NET 9 runtime, ASP.NET Core 9, or EF Core 9
- Replacing `BinaryFormatter` usage (now always throws at runtime)
- Updating CI/CD pipelines, Dockerfiles, or deployment scripts for .NET 9

## When Not to Use

- The project already targets `net9.0` and builds cleanly — migration is done
- Upgrading from .NET 7 or earlier — address the prior version breaking changes first
- Migrating from .NET Framework — that is a separate, larger effort
- Greenfield projects that start on .NET 9 (no migration needed)

## Inputs

| Input | Required | Description |
|-------|----------|-------------|
| Project or solution path | Yes | The `.csproj`, `.sln`, or `.slnx` entry point to migrate |
| Build command | No | How to build (e.g., `dotnet build`, a repo build script). Auto-detect if not provided |
| Test command | No | How to run tests (e.g., `dotnet test`). Auto-detect if not provided |
| Project type hints | No | Whether the project uses ASP.NET Core, EF Core, WinForms, WPF, containers, etc. Auto-detect from PackageReferences and SDK attributes if not provided |

## Workflow

> **Answer directly from the loaded reference documents.** Do not search the filesystem or fetch web pages for breaking change information — the references contain the authoritative details. Focus on identifying which breaking changes apply and providing concrete fixes.
>
> **Commit strategy:** Commit at each logical boundary — after updating the TFM (Step 2), after resolving build errors (Step 3), after addressing behavioral changes (Step 4), and after updating infrastructure (Step 5). This keeps each commit focused and reviewable.

### Step 1: Assess the project

1. Identify how the project is built and tested. Look for build scripts, `.sln`/`.slnx` files, or individual `.csproj` files.
2. Run `dotnet --version` to confirm the .NET 9 SDK is installed. If it is not, stop and inform the user.
3. Determine which technology areas the project uses by examining:
   - **SDK attribute**: `Microsoft.NET.Sdk.Web` → ASP.NET Core; `Microsoft.NET.Sdk.WindowsDesktop` with `<UseWPF>` or `<UseWindowsForms>` → WPF/WinForms
   - **PackageReferences**: `Microsoft.EntityFrameworkCore.*` → EF Core; `Microsoft.Extensions.Http` → HttpClientFactory
   - **Dockerfile presence** → Container changes relevant
   - **P/Invoke or native interop usage** → Interop changes relevant
   - **`BinaryFormatter` usage** → Serialization migration needed
   - **`System.Text.Json` usage** → Serialization changes relevant
   - **X509Certificate constructors** → Cryptography changes relevant
4. Record which reference documents are relevant (see the reference loading table in Step 3).
5. Do a **clean build** (`dotnet build --no-incremental` or delete `bin`/`obj`) on the current `net8.0` target to establish a clean baseline. Record any pre-existing warnings.

### Step 2: Update the Target Framework

1. In each `.csproj` (or `Directory.Build.props` if centralized), change:
   ```xml
   <TargetFramework>net8.0</TargetFramework>
   ```
   to:
   ```xml
   <TargetFramework>net9.0</TargetFramework>
   ```
   For multi-targeted projects, add `net9.0` to `<TargetFrameworks>` or replace `net8.0`.

2. Update all `Microsoft.Extensions.*`, `Microsoft.AspNetCore.*`, `Microsoft.EntityFrameworkCore.*`, and other Microsoft package references to their 9.0.x versions. If using Central Package Management (`Directory.Packages.props`), update versions there.

3. Run `dotnet restore`. Watch for:
   - **Version requirements**: .NET 9 SDK requires Visual Studio 17.12+ to target `net9.0` (17.11 for `net8.0` and earlier).
   - **New warnings for .NET Standard 1.x** and **.NET 7 targets** — consider updating or removing outdated target frameworks.

4. Run a clean build. Collect all errors and new warnings. These will be addressed in Step 3.

### Step 3: Resolve build errors and source-incompatible changes

Work through compilation errors and new warnings systematically. Load the appropriate reference documents based on the project type:

| If the project uses… | Load reference |
|-----------------------|----------------|
| Any .NET 9 project | `references/csharp-compiler.md` |
| Any .NET 9 project | `references/core-libraries.md` |
| Any .NET 9 project | `references/sdk-msbuild.md` |
| ASP.NET Core | `references/aspnet-core.md` |
| Entity Framework Core | `references/efcore.md` |
| Cryptography APIs | `references/cryptography.md` |
| System.Text.Json, HttpClient, networking | `references/serialization-networking.md` |
| Windows Forms or WPF | `references/winforms-wpf.md` |
| Docker containers, native interop | `references/containers-interop.md` |
| Runtime configuration, deployment | `references/deployment-runtime.md` |

**Common source-incompatible changes to check for:**

1. **`params` span overload resolution** — New `params ReadOnlySpan<T>` overloads on `String.Join`, `String.Concat`, `Path.Combine`, `Task.WhenAll`, and many more now bind preferentially over the `params T[]` overloads. Code calling these methods inside `Expression` lambdas will fail with CS8640/CS9226. Fix by passing an explicit array: `string.Join("", new string[] { x, y })`. See `references/core-libraries.md`.

2. **`StringValues` ambiguous overload** — The `params Span<T>` feature creates ambiguity with `StringValues` implicit operators on methods like `String.Concat`, `String.Join`, `Path.Combine`. Fix by explicitly casting arguments. See `references/core-libraries.md`.

3. **New obsoletion warnings (SYSLIB0054–SYSLIB0057)**:
   - `SYSLIB0054`: Replace `Thread.VolatileRead`/`VolatileWrite` with `Volatile.Read`/`Volatile.Write`
   - `SYSLIB0055`: Replace signed `AdvSimd.ShiftRightLogicalRoundedNarrowingSaturate*` with unsigned overloads
   - `SYSLIB0056`: Replace `Assembly.LoadFrom` with `AssemblyHashAlgorithm` parameter with overloads without it
   - `SYSLIB0057`: Replace `X509Certificate2`/`X509Certificate` binary/file constructors with `X509CertificateLoader` methods

4. **C# 13 `InlineArray` on record structs** — `[InlineArray]` attribute on `record struct` types is now disallowed (CS9259). Change to a regular `struct`. See `references/csharp-compiler.md`.

5. **C# 13 iterator safe context** — Iterators now introduce a safe context in C# 13. Local functions inside iterators that used unsafe code inherited from an outer `unsafe` class will now error. Add `unsafe` modifier to the local function. See `references/csharp-compiler.md`.

6. **C# 13 collection expression overload resolution** — Empty collection expressions (`[]`) no longer use span vs non-span to tiebreak overloads. Exact element type is now preferred. See `references/csharp-compiler.md`.

7. **`String.Trim(params ReadOnlySpan<char>)` removed** — The `Trim`/`TrimStart`/`TrimEnd` overloads accepting `ReadOnlySpan<char>` were removed in .NET 9 GA after being added in previews. Code explicitly passing `ReadOnlySpan<char>` must switch to `char[]`. See `references/core-libraries.md`.

8. **`BinaryFormatter` always throws** — If the project uses `BinaryFormatter`, **stop and inform the user** — this is a major decision. See `references/serialization-networking.md`.

9. **`HttpListenerRequest.UserAgent` is nullable** — The property is now `string?`. Add null checks. See `references/serialization-networking.md`.

10. **Windows Forms nullability annotation changes** — Some WinForms API parameters changed from nullable to non-nullable. Update call sites. See `references/winforms-wpf.md`.

11. **Windows Forms security analyzers (WFO1000)** — New analyzers produce errors for properties without explicit serialization configuration. See `references/winforms-wpf.md`.

Build again after each batch of fixes. Repeat until the build is clean.

### Step 4: Address behavioral changes

Behavioral changes do not cause build errors but may change runtime behavior. Review each applicable item and determine whether the previous behavior was relied upon.

**High-impact behavioral changes (check first):**

1. **BinaryFormatter always throws at runtime** — No runtime switch to re-enable. If BinaryFormatter usage is detected, **stop and ask the user** which replacement to use. See `references/serialization-networking.md` for options.

2. **Floating-point to integer conversions are now saturating** — On x86/x64, conversions from `float`/`double` to integer types now saturate instead of wrapping. `NaN` converts to `0`. Values exceeding the target type range clamp to `MinValue`/`MaxValue`. If you relied on the previous wrapping behavior, use `ConvertToIntegerNative<TInteger>`.

3. **EF Core: Pending model changes exception** — `Migrate()`/`MigrateAsync()` now throws if the model has pending changes compared to the last migration. Add a new migration before calling `Migrate()`, or suppress with `ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))`.

4. **EF Core: Explicit transaction exception** — `Migrate()` inside a user transaction now throws. Remove the external transaction and `ExecutionStrategy` wrapper, or suppress with `ConfigureWarnings(w => w.Ignore(RelationalEventId.MigrationsUserTransactionWarning))`.

5. **HttpClientFactory uses `SocketsHttpHandler` by default** — Code that casts the primary handler to `HttpClientHandler` will get `InvalidCastException`. Check for both handler types or explicitly configure a primary handler.

6. **HttpClientFactory header redaction by default** — All header values in `Trace`-level logs are now redacted by default. Call `RedactLoggedHeaders` to explicitly specify which headers to redact.

7. **Environment variables take precedence over runtimeconfig.json** — Runtime configuration settings (e.g., `DOTNET_gcServer`) from environment variables now override `runtimeconfig.json` values instead of the reverse.

8. **ASP.NET Core `ValidateOnBuild`/`ValidateScopes` in development** — `HostBuilder` now enables DI validation in the development environment by default. Fix any DI registration issues or disable with `UseDefaultServiceProvider`.

**Other behavioral changes to review:**

- `BigInteger` now has a maximum length of `(2^31) - 1` bits
- `JsonDocument` deserialization of JSON `null` now returns non-null `JsonDocument` with `JsonValueKind.Null` instead of C# `null`
- `System.Text.Json` metadata reader now unescapes metadata property names
- `ZipArchiveEntry` names/comments now respect the UTF-8 flag
- `FromKeyedServicesAttribute` no longer injects non-keyed service fallback
- `IncrementingPollingCounter` initial callback is now asynchronous
- `InMemoryDirectoryInfo` prepends rootDir to files
- `RuntimeHelpers.GetSubArray` returns a different type
- Container images no longer install zlib
- `PictureBox` raises `HttpRequestException` instead of `WebException`
- `StatusStrip` uses a different default renderer
- `IMsoComponent` support is opt-in
- `SafeEvpPKeyHandle.DuplicateHandle` up-refs the handle
- Intel CET is now enabled by default (may break incompatible native libraries)
- `HttpClient` metrics report `server.port` unconditionally
- URI query strings redacted in HttpClient EventSource events and IHttpClientFactory logs
- `dotnet watch` is incompatible with Hot Reload for old frameworks
- WPF `GetXmlNamespaceMaps` returns `Hashtable` instead of `String`

### Step 5: Update infrastructure

1. **Dockerfiles**: Update base images. Note that .NET 9 container images no longer install zlib. If your app depends on zlib, add `RUN apt-get update && apt-get install -y zlib1g` to your Dockerfile.
   ```dockerfile
   # Before
   FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
   FROM mcr.microsoft.com/dotnet/aspnet:8.0
   # After
   FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
   FROM mcr.microsoft.com/dotnet/aspnet:9.0
   ```

2. **CI/CD pipelines**: Update SDK version references. If using `global.json`, update:
   ```json
   {
     "sdk": {
       "version": "9.0.100"
     }
   }
   ```

3. **Visual Studio version**: .NET 9 SDK requires VS 17.12+ to target `net9.0`. VS 17.11 can only target `net8.0` and earlier.

4. **Terminal Logger**: `dotnet build` now uses Terminal Logger by default in interactive terminals. CI scripts that parse MSBuild console output may need `--tl:off` or `MSBUILDTERMINALLOGGER=off`.

5. **`dotnet workload` output**: Output format has changed. Update any scripts that parse workload command output.

6. **.NET Monitor images**: Tags simplified to version-only (affects container orchestration referencing specific tags).

### Step 6: Verify

1. Run a full clean build: `dotnet build --no-incremental`
2. Run all tests: `dotnet test`
3. If the application is containerized, build and test the container image
4. Smoke-test the application, paying special attention to:
   - BinaryFormatter usage (will throw at runtime)
   - Floating-point to integer conversion behavior
   - EF Core migration application
   - HttpClientFactory handler casting and logging
   - DI validation in development environment
   - Runtime configuration settings (environment variable precedence)
5. Review the diff and ensure no unintended behavioral changes were introduced

## Reference Documents

The `references/` folder contains detailed breaking change information organized by technology area. Load only the references relevant to the project being migrated:

| Reference file | When to load |
|----------------|-------------|
| `references/csharp-compiler.md` | Always (C# 13 compiler breaking changes — InlineArray on records, iterator safe context, collection expression overloads) |
| `references/core-libraries.md` | Always (applies to all .NET 9 projects) |
| `references/sdk-msbuild.md` | Always (SDK and build tooling changes) |
| `references/aspnet-core.md` | Project uses ASP.NET Core |
| `references/efcore.md` | Project uses Entity Framework Core |
| `references/cryptography.md` | Project uses System.Security.Cryptography or X.509 certificates |
| `references/serialization-networking.md` | Project uses BinaryFormatter, System.Text.Json, HttpClient, or networking APIs |
| `references/winforms-wpf.md` | Project uses Windows Forms or WPF |
| `references/containers-interop.md` | Project uses Docker containers or native interop (P/Invoke) |
| `references/deployment-runtime.md` | Project uses runtime configuration, deployment, or has floating-point to integer conversions |
