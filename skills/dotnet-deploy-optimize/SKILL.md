---
name: dotnet-deploy-optimize
description: Optimizes .NET 8+ application deployments by analyzing publish configuration, trimming, Native AOT, Docker images, CI/CD pipelines, environment configuration, and health checks. Use when preparing a .NET app for production deployment or improving an existing deployment pipeline.
---

# .NET Deployment Optimization

Analyze and optimize a .NET 8+ application's deployment pipeline to produce smaller, faster, and more reliable production artifacts. The skill walks through publish modes, trimming, Native AOT, container optimization, CI/CD tuning, configuration management, and health-check readiness.

## When to Use

- Preparing a .NET 8+ application for its first production deployment
- Reducing published application size or cold-start latency
- Optimizing Docker images for a .NET service
- Improving CI/CD build times for .NET projects
- Adding health checks or readiness probes to a .NET service
- Reviewing an existing deployment pipeline for inefficiencies

## When Not to Use

- The project targets .NET Framework (not .NET Core/.NET 8+)
- The goal is application-level performance profiling (CPU, memory, hot-path optimization)
- The project is a library or NuGet package (no deployment artifact)

## Inputs

| Input | Required | Description |
|-------|----------|-------------|
| Project or solution path | Yes | Path to the `.csproj`, `.fsproj`, or `.sln` file |
| Target environment | No | Description of where the app will run (e.g., Linux container, Windows IIS, cloud PaaS) |
| Current Dockerfile | No | Existing Dockerfile, if one exists |
| CI/CD pipeline config | No | Existing pipeline file (e.g., GitHub Actions YAML, Azure Pipelines YAML) |

## Workflow

### Step 1: Assess the current project configuration

1. Read the project file(s) and identify the target framework, output type, and any existing publish settings.
2. Check for a `Properties/launchSettings.json`, `appsettings.json`, and `appsettings.*.json` files.
3. Note any existing `PublishTrimmed`, `PublishAot`, `PublishSingleFile`, `ReadyToRun`, or `SelfContained` properties.
4. Identify the application type using these signals:
   - **Web API / MVC / Razor Pages**: `<Project Sdk="Microsoft.NET.Sdk.Web">` with no Blazor packages
   - **Blazor**: `Sdk="Microsoft.NET.Sdk.Web"` plus `Microsoft.AspNetCore.Components` packages
   - **gRPC**: `Sdk="Microsoft.NET.Sdk.Web"` plus `Grpc.AspNetCore` package reference
   - **Worker Service**: `Sdk="Microsoft.NET.Sdk.Worker"`, or `Sdk="Microsoft.NET.Sdk"` with a `BackgroundService` or `IHostedService` implementation and a reference to `Microsoft.Extensions.Hosting`
   - **Console**: `Sdk="Microsoft.NET.Sdk"` with `<OutputType>Exe</OutputType>`
   - **WinForms / WPF**: `<UseWindowsForms>true</UseWindowsForms>` or `<UseWPF>true</UseWPF>`

### Step 2: Recommend a publish mode

Evaluate and recommend the most appropriate publish mode based on the application type:

| Mode | When to use |
|------|-------------|
| Framework-dependent | Target already has the .NET runtime installed; smallest artifact |
| Self-contained | Target may not have the runtime; trade size for portability |
| Single-file | Self-contained apps that benefit from a single executable |
| ReadyToRun (R2R) | Self-contained apps where faster startup is needed but full AOT is not feasible; pre-compiles IL to native code while keeping JIT as a fallback |
| Native AOT | Console or API apps needing minimal startup time and memory; no reflection-heavy libraries |

Provide the recommended `dotnet publish` command with appropriate flags:

```bash
# Example: self-contained single-file publish for Linux
dotnet publish -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true
```

Or equivalently, set the properties in the project file:

```xml
<!-- Example: project file properties for single-file self-contained -->
<PropertyGroup>
  <PublishSingleFile>true</PublishSingleFile>
  <SelfContained>true</SelfContained>
  <RuntimeIdentifier>linux-x64</RuntimeIdentifier>
</PropertyGroup>
```

### Step 2b: Check publish setting compatibility

Before combining publish properties, verify the combination is valid. The following table lists settings that conflict or are redundant when used together:

| Setting A | Setting B | Outcome | Explanation |
|-----------|-----------|---------|-------------|
| `PublishAot` | `PublishSingleFile` | **Conflict** | AOT produces a single native binary by default; `PublishSingleFile` is for IL-based apps and is ignored with AOT |
| `PublishAot` | `ReadyToRun` | **Conflict** | ReadyToRun pre-compiles IL to native via crossgen; AOT replaces the IL pipeline entirely, making R2R meaningless |
| `PublishAot` | `SelfContained=false` | **Conflict** | AOT output is always self-contained; setting `SelfContained` to false is contradictory and will cause a build error |
| `PublishAot` | `PublishTrimmed` | **Redundant** | AOT implies trimming; setting `PublishTrimmed` explicitly is unnecessary (but not harmful) |
| `PublishSingleFile` | `SelfContained=false` | **Not recommended** | Framework-dependent single-file bundles are supported but rarely useful; the host still requires the shared runtime |
| `ReadyToRun` | `PublishTrimmed` | **Caution** | Supported but may increase size because R2R adds native code on top of IL; trimming savings can be offset by R2R overhead |
| `ReadyToRun` | `SelfContained=false` | **Conflict** | ReadyToRun pre-compiles app IL to native code and requires the runtime to be bundled; framework-dependent apps cannot use R2R |
| `PublishTrimmed` | `SelfContained=false` | **Conflict** | Trimming requires self-contained deployment; framework-dependent apps cannot be trimmed |

When reviewing a project file, flag any of these combinations and recommend removing the conflicting or redundant property.

### Step 3: Apply trimming and tree-shaking

> **Skip this step** if Native AOT was chosen in Step 2 — AOT applies trimming automatically.

1. Check if the application is trim-compatible by scanning for known trim-incompatible patterns:
   - Heavy use of `System.Reflection` and using string representations of types, strongly typed reflection is not problematic
   - Dynamic assembly loading
   - `System.Text.Json` source generators not configured
   - COM interop
   - Check trim-compatibility of dependencies, including from nuget
2. If compatible, offer to assist to enable trimming and fix errors:

```xml
<PropertyGroup>
  <PublishTrimmed>true</PublishTrimmed>
</PropertyGroup>
```

3. Suggest adding `[DynamicallyAccessedMembers]` or `[RequiresUnreferencedCode]` annotations where needed.
4. Recommend running `dotnet publish` with trimming and reviewing warnings.

### Step 4: Evaluate Native AOT

1. Check if the application is AOT-compatible:
   - No `dynamic` keyword usage
   - No unbounded reflection
   - All serialization uses source generators
   - No runtime code generation (e.g., `System.Reflection.Emit`)
   - Check AOT-compatibility of dependencies, including from nuget
2. If compatible, recommend enabling AOT and offer to fix any errors:

```xml
<PropertyGroup>
  <PublishAot>true</PublishAot>
</PropertyGroup>
```

3. Note trade-offs: longer build time, platform-specific output, no JIT. For long-running services (Worker Services, daemons), the JIT with tiered compilation can optimize hot paths over time — AOT eliminates this benefit. Consider ReadyToRun as a middle ground for long-running workloads.
4. If not compatible, document the blockers and suggest alternatives (trimming, ReadyToRun).

### Step 5: Optimize Docker images

> **Skip this step** if the application is not deployed as a container and container deployment is not planned.

If a Dockerfile exists or container deployment is planned:

1. Recommend a multi-stage build pattern:

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY *.csproj ./
RUN dotnet restore
COPY . .
RUN dotnet publish -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "<AssemblyName>.dll"]  # Replace <AssemblyName> with the actual project assembly name
```

2. Recommend using `aspnet` (not `sdk`) as the runtime base image.
3. Suggest using Alpine-based images (`8.0-alpine`) when glibc dependencies allow, for smaller images.
4. Recommend a `.dockerignore` file that excludes `bin/`, `obj/`, `.git/`, and other non-essential files.
5. Suggest layer ordering: restore before copy to maximize cache hits.
6. For AOT apps, use the `runtime-deps` base image instead.

### Step 6: Optimize CI/CD pipeline

> **Skip this step** if no CI/CD pipeline configuration file exists in the repository.

Review the pipeline configuration and recommend:

1. **Dependency caching**: Cache NuGet packages across builds.

```yaml
# GitHub Actions example
- uses: actions/cache@v4
  with:
    path: ~/.nuget/packages
    key: nuget-${{ hashFiles('**/*.csproj') }}
```

2. **Parallelism**: Run tests and builds in parallel jobs when possible.
3. **Incremental builds**: Avoid unnecessary full restores; use `--no-restore` after a cached restore step.
4. **Artifact size**: Publish only the deployment artifact, not the full build output.
5. **Build configuration**: Ensure `Release` configuration is used for deployed artifacts.

### Step 7: Review configuration and environment management

> **Skip this step** if the application has no configuration files (`appsettings.json`, environment variables, or similar).

1. Verify that no `appsettings*.json` file contains secrets (check the base `appsettings.json`, `appsettings.Production.json`, and any other environment-specific files).
2. Recommend environment variables or a secret manager for sensitive values.
3. Confirm that configuration binding uses the Options pattern (`IOptions<T>`).
4. Suggest using `DOTNET_ENVIRONMENT` or `ASPNETCORE_ENVIRONMENT` to control configuration layering.
5. Check that connection strings and API keys are not hardcoded.

### Step 8: Add health checks and probes

> **Skip this step** for console applications and CLI tools that do not run as long-lived services.

For web applications and worker services:

**For web applications** (Web API, MVC, Blazor, gRPC):

1. Add the health checks middleware:

```csharp
builder.Services.AddHealthChecks()
    .AddCheck("self", () => HealthCheckResult.Healthy());

app.MapHealthChecks("/healthz");
```

2. Recommend separate endpoints for liveness (`/healthz`) and readiness (`/ready`).
3. Add dependency health checks for databases, caches, and external services:

```csharp
builder.Services.AddHealthChecks()
    .AddNpgSql(connectionString)       // PostgreSQL
    .AddRedis(redisConnectionString);  // Redis
```

4. Document how orchestrators (Kubernetes, Docker Compose) should configure probes pointing at these endpoints.

**For Worker Services** (no HTTP endpoints by default):

1. Option A — Add a minimal Kestrel endpoint for health checks:

```csharp
builder.Services.AddHealthChecks()
    .AddCheck("self", () => HealthCheckResult.Healthy());

builder.WebHost.UseKestrel(o => o.ListenAnyIP(8080));
var app = builder.Build();
app.MapHealthChecks("/healthz");
```

2. Option B — Use a health check publisher to write status to a file or external system that orchestrators can monitor:

```csharp
builder.Services.AddHealthChecks()
    .AddCheck("self", () => HealthCheckResult.Healthy());
builder.Services.Configure<HealthCheckPublisherOptions>(options =>
{
    options.Period = TimeSpan.FromSeconds(30);
});
builder.Services.AddSingleton<IHealthCheckPublisher, FileHealthCheckPublisher>();
```

3. Option C — Use a TCP health check listener for simple liveness probes without adding HTTP overhead.

### Step 9: Summarize recommendations

Produce a summary table of all recommendations:

| Area | Current state | Recommendation | Impact |
|------|--------------|----------------|--------|
| Publish mode | (detected) | (recommended) | (size/startup) |
| Publish compatibility | (conflicts found / none) | (resolution) | (correctness) |
| Trimming | (enabled/disabled) | (recommendation) | (size reduction) |
| AOT | (enabled/disabled) | (recommendation) | (startup/size) |
| Docker | (exists/missing) | (recommendation) | (image size) |
| CI/CD | (detected) | (recommendation) | (build time) |
| Config | (reviewed) | (recommendation) | (security) |
| Health checks | (present/missing) | (recommendation) | (reliability) |

## Validation

- [ ] `dotnet publish -c Release` completes without errors after changes
- [ ] Published output size is smaller than or equal to the baseline (if trimming or AOT was applied)
- [ ] Docker image builds successfully (if Dockerfile was modified)
- [ ] Health check endpoints return HTTP 200 (if health checks were added)
- [ ] No secrets are present in configuration files committed to source control
- [ ] CI/CD pipeline runs successfully with caching enabled
- [ ] Application starts and responds to requests in the target environment

## Common Pitfalls

| Pitfall | Solution |
|---------|----------|
| Enabling trimming without testing | Run the full test suite after trimming; fix trim warnings before deploying |
| AOT with reflection-heavy libraries | Use source generators for serialization; avoid libraries that rely on unbounded reflection |
| Using `sdk` as Docker runtime image | Switch to `aspnet` or `runtime-deps` to reduce image size by hundreds of MB |
| Caching the wrong NuGet path | Verify the NuGet cache path for your CI runner OS (`~/.nuget/packages` on Linux, `%USERPROFILE%\.nuget\packages` on Windows) |
| Hardcoded secrets in appsettings | Move secrets to environment variables, user secrets (dev), or a vault (production) |
| Missing `.dockerignore` | Add one to prevent `bin/`, `obj/`, and `.git/` from bloating the Docker build context |
| Health checks without dependency checks | Add checks for databases, caches, and external APIs to get meaningful readiness signals |
| Combining incompatible publish settings | Check the compatibility matrix in Step 2b; remove conflicting or redundant properties before publishing |
| Single-file publish without `IncludeNativeLibrariesForSelfExtract` | Native libraries may not bundle correctly; add the property if needed |
