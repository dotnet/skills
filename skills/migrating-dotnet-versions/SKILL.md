---
name: migrating-dotnet-versions
description: Migrate .NET projects between major framework versions, including .NET Framework to .NET 8+, or between modern .NET versions. Use when upgrading target frameworks, resolving API breaking changes, or modernizing legacy .NET codebases.
---

# Migrating .NET Versions

## When to Use

- Upgrading a project's target framework (e.g., .NET 6 → .NET 8, .NET 8 → .NET 10)
- Migrating from .NET Framework to modern .NET
- Resolving breaking changes after a framework version bump
- Auditing a solution for migration readiness

## When Not to Use

- The user wants to create a new project from scratch (no migration needed)
- The change is purely a NuGet package update with no framework change
- The user is migrating between non-.NET platforms

## Inputs

| Input | Required | Description |
|-------|----------|-------------|
| Solution or project path | Yes | The `.sln` or `.csproj` to migrate |
| Target framework version | Yes | The .NET version to migrate to (e.g., `net8.0`, `net10.0`) |
| Source framework version | No | Auto-detected from project files if not provided |

## Workflow

### Step 1: Assess the current state

```bash
dotnet --list-sdks
```

Verify the target SDK is installed. Then scan project files:

```bash
grep -r "<TargetFramework" --include="*.csproj" .
```

Record the current framework versions for all projects in the solution.

### Step 2: Check migration readiness

For .NET Framework → modern .NET migrations, use the upgrade assistant:

```bash
dotnet tool install -g upgrade-assistant
upgrade-assistant analyze <solution-path>
```

For modern .NET → modern .NET migrations (e.g., .NET 6 → .NET 8), review the breaking changes list:

- Check https://learn.microsoft.com/en-us/dotnet/core/compatibility/ for the target version
- Focus on breaking changes in categories: Core .NET, ASP.NET Core, EF Core, Windows Forms, WPF

### Step 3: Update the target framework

Edit each `.csproj` file:

```xml
<!-- Before -->
<TargetFramework>net6.0</TargetFramework>

<!-- After -->
<TargetFramework>net8.0</TargetFramework>
```

For multi-targeting:

```xml
<TargetFrameworks>net8.0;net6.0</TargetFrameworks>
```

### Step 4: Update NuGet packages

```bash
dotnet restore
```

Update packages to versions compatible with the target framework:

```bash
dotnet list package --outdated
```

Update each outdated package. For major version bumps, check the package's release notes for breaking changes.

### Step 5: Build and fix compilation errors

```bash
dotnet build
```

Common categories of build errors after migration:

| Error Category | Typical Fix |
|---|---|
| Obsolete API usage | Replace with recommended alternative from the warning message |
| Removed API | Find replacement in the migration guide |
| Namespace changes | Update `using` directives |
| Default behavior changes | Explicitly set the previous behavior in config if needed |

Fix errors iteratively: build → fix → build again until clean.

### Step 6: Run tests

```bash
dotnet test
```

Pay attention to:

- Tests that fail due to behavioral changes in the new runtime
- Tests that rely on specific runtime version checks
- Integration tests that depend on framework-specific APIs

### Step 7: Validate runtime behavior

Run the application and verify:

- [ ] Application starts without errors
- [ ] Core functionality works as expected
- [ ] Performance is comparable or improved
- [ ] No new runtime warnings in logs

## .NET Framework to Modern .NET: Key Differences

| Area | .NET Framework | Modern .NET |
|---|---|---|
| Config | `web.config` / `app.config` | `appsettings.json` + `IConfiguration` |
| DI | Various (Unity, Autofac, etc.) | Built-in `Microsoft.Extensions.DependencyInjection` |
| Hosting | IIS-dependent | Kestrel + any reverse proxy |
| Startup | `Global.asax` | `Program.cs` / minimal hosting |
| Logging | `System.Diagnostics.Trace` | `Microsoft.Extensions.Logging` |

## Validation

- [ ] All projects target the new framework version
- [ ] `dotnet restore` completes without errors
- [ ] `dotnet build` completes without errors
- [ ] `dotnet test` passes (regressions documented if any)
- [ ] Application runs and core scenarios work

## Common Pitfalls

| Pitfall | Solution |
|---------|----------|
| Missing SDK version | Install from https://dotnet.microsoft.com/download |
| `global.json` pins old SDK | Update or remove the `global.json` SDK version |
| Transitive package conflicts | Use `dotnet nuget why` or check `Directory.Packages.props` for central package management |
| Windows-only APIs on Linux | Guard with `OperatingSystem.IsWindows()` or use compatibility packages |
| EF Core migration conflicts | Regenerate migrations if model snapshot is incompatible |
