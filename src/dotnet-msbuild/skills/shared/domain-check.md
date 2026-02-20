# MSBuild Domain Relevance Check

All MSBuild skills and agents in this plugin **must** verify domain relevance before proceeding. This document defines the signals to check and the confidence thresholds.

## Purpose

Before activating any MSBuild-specific skill, verify that the current user context is actually related to MSBuild or .NET builds. This avoids false activation on unrelated build systems.

## When MSBuild Skills Are Relevant

Check for these positive signals. Any single signal from the **High confidence** category is sufficient to activate MSBuild skills.

### 1. Project File Presence

The workspace contains any of these files:

- `.csproj`, `.vbproj`, `.fsproj`, `.esproj`, `.wapproj`, `.proj`
- `.props`, `.targets` (MSBuild property/target imports)
- `.sln`, `.slnx` (solution files)
- `Directory.Build.props`, `Directory.Build.targets` (auto-imported MSBuild files)
- `Directory.Packages.props` (central package management)
- `global.json` (.NET SDK version pinning)

### 2. CLI Command Context

The user ran or is discussing any of these commands:

- `dotnet build`
- `dotnet test`
- `dotnet pack`
- `dotnet publish`
- `dotnet restore`
- `dotnet run`
- `dotnet new`
- `msbuild` or `msbuild.exe`

### 3. Error Code Prefixes

The user's message or console output contains error/warning codes matching these patterns:

- `CS####` — C# compiler errors/warnings
- `MSB####` — MSBuild engine errors/warnings
- `NU####` — NuGet errors/warnings
- `NETSDK####` — .NET SDK errors/warnings
- `BC####` — Visual Basic compiler errors/warnings
- `FS####` — F# compiler errors/warnings

### 4. Build Artifacts

The workspace contains MSBuild-specific build artifacts:

- `.binlog` files (MSBuild binary logs)
- `bin/` or `obj/` directories containing MSBuild marker files:
  - `project.assets.json`
  - `*.AssemblyReference.cache`
  - `*.csproj.nuget.g.props` / `*.csproj.nuget.g.targets`

### 5. NuGet Artifacts

The workspace contains NuGet configuration or output files:

- `nuget.config`
- `packages.config`
- `*.nupkg`
- `.nuget/` directory

### 6. MSBuild XML Content

The user shares or references XML containing MSBuild elements:

- `<Project>` root element
- `<PropertyGroup>`, `<ItemGroup>`, `<Target>`
- `<PackageReference>`, `<ProjectReference>`
- `Sdk="Microsoft.NET.Sdk"` or other `Sdk=` attributes
- `<Import Project="..."/>` statements

## When MSBuild Skills Are NOT Relevant

If **only** these signals are present and none of the positive signals above exist, do **not** activate MSBuild skills:

- **Node.js/JavaScript**: `package.json`, `node_modules/`, npm/yarn/pnpm errors, `webpack.config.js`
- **Make/CMake**: `Makefile`, `CMakeLists.txt`, `configure.ac`, `autoconf`
- **Java/JVM**: `build.gradle`, `pom.xml`, Maven/Gradle errors, `build.sbt`
- **Rust**: `Cargo.toml`, `rustc` errors, `cargo build` failures
- **Go**: `go.mod`, `go.sum`, Go compiler errors
- **Python**: `pyproject.toml`, `setup.py`, `requirements.txt`, pip errors
- **Generic phrases**: "build failed", "compilation error", or "test failed" without any .NET/MSBuild indicators

## Confidence Assessment

Use this mental model to decide whether to activate MSBuild skills:

### High Confidence — Activate Immediately

Any one of these is sufficient:

- MSBuild error codes present (`CS####`, `MSB####`, `NU####`, `NETSDK####`, `BC####`, `FS####`)
- `.csproj`, `.vbproj`, `.fsproj`, or `.sln` files present in the workspace
- User explicitly ran a `dotnet` CLI or `msbuild` command
- `.binlog` file referenced or present
- XML content with `Sdk="Microsoft.NET.Sdk"` or `<PackageReference>`

### Medium Confidence — Investigate Further

These signals suggest MSBuild relevance but require confirmation:

- "Build failed" message alongside some .NET indicators (e.g., mention of NuGet, .NET, or C#)
- XML content that looks like MSBuild but lacks definitive markers (e.g., `<Project>` without `Sdk=`)
- `bin/` or `obj/` directories present without confirming their contents
- User mentions "Visual Studio" or "solution" without specifying the build system

**Action:** Glob for `**/*.csproj`, `**/*.sln`, or `**/global.json` to confirm before committing to MSBuild-specific analysis.

### Low Confidence — Do Not Activate

- No .NET indicators anywhere in the context
- A different build system's artifacts are present (`package.json`, `Makefile`, `Cargo.toml`, etc.)
- Generic build terminology without .NET-specific context

**Action:** Do not activate MSBuild skills. If the user explicitly asks for MSBuild help, ask for clarification or scan the workspace first.

## How Other Skills Should Reference This

All MSBuild-specific skills (e.g., `binlog-failure-analysis`, `check-bin-obj-clash`, `binlog-generation`) should mentally verify domain relevance before deep-diving into MSBuild-specific troubleshooting.

**Before activating an MSBuild skill:**

1. Check whether any high-confidence signal is present in the user's message, console output, or recent context.
2. If only medium-confidence signals exist, run a quick workspace scan:
   ```
   glob for **/*.csproj, **/*.sln, **/global.json
   ```
3. If no positive signals are found, do not proceed with MSBuild-specific analysis. Respond generically or ask the user to clarify their build system.

**This check should be fast and lightweight.** Do not invoke heavy tools (e.g., `load_binlog`) until domain relevance is confirmed. The goal is a quick pass/fail gate, not a deep investigation.
