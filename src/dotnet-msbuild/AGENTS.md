# MSBuild & .NET Build Instructions

<!-- Copy this file to your repository root as AGENTS.md for cross-agent support (Copilot, Claude Code, etc.) -->

## Build System

This repository uses MSBuild / .NET SDK for building. Key commands:
- Build: `dotnet build`
- Test: `dotnet test`
- Pack: `dotnet pack`
- Restore: `dotnet restore`

## Build Failure Handling

When a build fails:
1. Always generate a binary log: `dotnet build /bl:N.binlog` (increment N for each attempt)
2. Analyze the binlog before attempting fixes — don't guess from console output alone
3. Check for common error patterns: CS (C# compiler), MSB (MSBuild), NU (NuGet), NETSDK (SDK) prefixes
4. After fixing, rebuild to verify the fix works

## MSBuild Project File Conventions

- Use SDK-style projects (`<Project Sdk="Microsoft.NET.Sdk">`)
- Use `<PackageReference>` for NuGet packages and `<ProjectReference>` for local dependencies (avoid `<Reference>` with HintPath, though `<Reference>` is still valid for .NET Framework GAC assemblies like `WindowsBase`)
- Centralize common settings in `Directory.Build.props`
- Centralize package versions in `Directory.Packages.props` (Central Package Management)
- Use PascalCase for custom MSBuild properties
- Add `Inputs` and `Outputs` to custom targets for incremental build support
- Prefer property functions over `<Exec>` for simple operations
- Use `$(MSBuildThisFileDirectory)` over hardcoded paths

## Performance

- Always build with `-m` (parallel) in CI
- Separate restore from build: `dotnet restore` then `dotnet build --no-restore`
- When diagnosing slow builds, generate a binlog and analyze with binlog tools

## NuGet

- Configure package sources in `nuget.config` at the repo root
- Use `packages.lock.json` for reproducible restores in CI
- Clear caches when troubleshooting: `dotnet nuget locals all --clear`
