## AP-16: Using `<Exec>` for String/Path Operations

**Smell**: `<Exec Command="echo $(Var) | sed ..." />` or `<Exec Command="powershell -c ..." />` for simple string manipulation.

**Why it's bad**: Shell-dependent, not cross-platform, slower than property functions, and the result is hard to capture back into MSBuild properties.

```xml
<!-- BAD -->
<Target Name="GetCleanVersion">
  <Exec Command="echo $(Version) | sed 's/-preview//'" ConsoleToMSBuildProperty="CleanVersion" />
</Target>

<!-- GOOD: Property function -->
<PropertyGroup>
  <CleanVersion>$(Version.Replace('-preview', ''))</CleanVersion>
  <HasPrerelease>$(Version.Contains('-'))</HasPrerelease>
  <LowerName>$(AssemblyName.ToLowerInvariant())</LowerName>
</PropertyGroup>

<!-- GOOD: Path operations -->
<PropertyGroup>
  <NormalizedOutput>$([MSBuild]::NormalizeDirectory($(OutputPath)))</NormalizedOutput>
  <ToolPath>$([System.IO.Path]::Combine($(MSBuildThisFileDirectory), 'tools', 'mytool.exe'))</ToolPath>
</PropertyGroup>
```

---

## AP-17: Mixing `Include` and `Update` for the Same Item Type in One ItemGroup

**Smell**: Same `<ItemGroup>` has both `<Compile Include="...">` and `<Compile Update="...">`.

**Why it's bad**: `Update` acts on items already in the set. If `Include` hasn't been processed yet (evaluation order), `Update` may not find the item. Separating them avoids subtle ordering bugs.

```xml
<!-- BAD -->
<ItemGroup>
  <Compile Include="Generated\Extra.cs" />
  <Compile Update="Generated\Extra.cs" CopyToOutputDirectory="Always" />
</ItemGroup>

<!-- GOOD -->
<ItemGroup>
  <Compile Include="Generated\Extra.cs" />
</ItemGroup>
<ItemGroup>
  <Compile Update="Generated\Extra.cs" CopyToOutputDirectory="Always" />
</ItemGroup>
```

---

## AP-18: Redundant `<ProjectReference>` to Transitively-Referenced Projects

**Smell**: A project references both `Core` and `Utils`, but `Core` already depends on `Utils`.

**Why it's bad**: Adds unnecessary coupling, makes the dependency graph harder to understand, and can cause ordering issues in large builds. MSBuild resolves transitive references automatically.

```xml
<!-- BAD -->
<ItemGroup>
  <ProjectReference Include="..\Core\Core.csproj" />
  <ProjectReference Include="..\Utils\Utils.csproj" />  <!-- Core already references Utils -->
</ItemGroup>

<!-- GOOD: Only direct dependencies -->
<ItemGroup>
  <ProjectReference Include="..\Core\Core.csproj" />
</ItemGroup>
```

**Caveat**: If you need to use types from `Utils` directly (not just transitively), the explicit reference is appropriate. But verify whether the direct dependency is actually needed.

---

## AP-19: Side Effects During Property Evaluation

**Smell**: Property functions that write files, make network calls, or modify state during `<PropertyGroup>` evaluation.

**Why it's bad**: Property evaluation happens during the evaluation phase, which can run multiple times (e.g., during design-time builds in Visual Studio). Side effects are unpredictable and can corrupt state.

```xml
<!-- BAD: File write during evaluation -->
<PropertyGroup>
  <Timestamp>$([System.IO.File]::WriteAllText('stamp.txt', 'built'))</Timestamp>
</PropertyGroup>

<!-- GOOD: Side effects belong in targets -->
<Target Name="WriteTimestamp" BeforeTargets="Build">
  <WriteLinesToFile File="stamp.txt" Lines="built" Overwrite="true" />
</Target>
```

---

## AP-20: Platform-Specific Exec Without OS Condition

**Smell**: `<Exec Command="chmod +x ..." />` or `<Exec Command="cmd /c ..." />` without an OS condition.

**Why it's bad**: Fails on the wrong platform. If the project is cross-platform, guard platform-specific commands.

```xml
<!-- BAD: Fails on Windows -->
<Target Name="MakeExecutable" AfterTargets="Build">
  <Exec Command="chmod +x $(OutputPath)mytool" />
</Target>

<!-- GOOD: OS-guarded -->
<Target Name="MakeExecutable" AfterTargets="Build"
        Condition="!$([MSBuild]::IsOSPlatform('Windows'))">
  <Exec Command="chmod +x $(OutputPath)mytool" />
</Target>
```

---

## AP-21: Property Conditioned on TargetFramework in .props Files

**Smell**: `<PropertyGroup Condition="'$(TargetFramework)' == '...'">` or `<Property Condition="'$(TargetFramework)' == '...'">` in `Directory.Build.props` or any `.props` file imported before the project body.

**Why it's bad**: `$(TargetFramework)` is NOT reliably available in `Directory.Build.props` or any `.props` file imported before the project body. It is only set that early for multi-targeting projects, which receive `TargetFramework` as a global property from the outer build. Single-targeting projects (using singular `<TargetFramework>`) set it in the project body, which is evaluated *after* `.props`. This means property conditions on `$(TargetFramework)` in `.props` files silently fail for single-targeting projects — the condition never matches because the property is empty. This applies to both `<PropertyGroup Condition="...">` and individual `<Property Condition="...">` elements.

For a detailed explanation of MSBuild's evaluation and execution phases, see [Build process overview](https://learn.microsoft.com/en-us/visualstudio/msbuild/build-process-overview).

```xml
<!-- BAD: In Directory.Build.props — TargetFramework may be empty here -->
<PropertyGroup Condition="'$(TargetFramework)' == 'net8.0'">
  <DefineConstants>$(DefineConstants);MY_FEATURE</DefineConstants>
</PropertyGroup>

<!-- ALSO BAD: Condition on the property itself has the same problem -->
<PropertyGroup>
  <DefineConstants Condition="'$(TargetFramework)' == 'net8.0'">$(DefineConstants);MY_FEATURE</DefineConstants>
</PropertyGroup>

<!-- GOOD: In Directory.Build.targets — TargetFramework is always available -->
<PropertyGroup Condition="'$(TargetFramework)' == 'net8.0'">
  <DefineConstants>$(DefineConstants);MY_FEATURE</DefineConstants>
</PropertyGroup>

<!-- ALSO GOOD: In the project file itself -->
<!-- MyProject.csproj -->
<PropertyGroup Condition="'$(TargetFramework)' == 'net8.0'">
  <DefineConstants>$(DefineConstants);MY_FEATURE</DefineConstants>
</PropertyGroup>
```

**⚠️ Item and Target conditions are NOT affected.** This restriction applies ONLY to property conditions (`<PropertyGroup Condition="...">` and `<Property Condition="...">`). Item conditions (`<ItemGroup Condition="...">`) and Target conditions in `.props` files are SAFE because items and targets evaluate after all properties (including those set in the project body) have been evaluated. This includes `PackageVersion` items in `Directory.Packages.props`, `PackageReference` items in `Directory.Build.props`, and any other item types.

**Do NOT flag the following patterns — they are correct:**

```xml
<!-- OK in Directory.Build.props — ItemGroup conditions evaluate late -->
<ItemGroup Condition="'$(TargetFramework)' == 'net472'">
  <PackageReference Include="System.Memory" />
</ItemGroup>

<!-- OK in Directory.Packages.props — PackageVersion items evaluate late -->
<ItemGroup Condition="'$(TargetFramework)' == 'net8.0'">
  <PackageVersion Include="Microsoft.AspNetCore.Mvc.Testing" Version="8.0.11" />
</ItemGroup>
<ItemGroup Condition="'$(TargetFramework)' == 'net9.0'">
  <PackageVersion Include="Microsoft.AspNetCore.Mvc.Testing" Version="9.0.0" />
</ItemGroup>

<!-- OK — Individual item conditions also evaluate late -->
<ItemGroup>
  <PackageReference Include="System.Memory" Condition="'$(TargetFramework)' == 'net472'" />
</ItemGroup>
```

---

## AP-22: Forking a Project Instance via `<MSBuild>` with Path-Neutral Global Properties

**Smell**: A target uses the `<MSBuild>` task to build or publish a project, passing extra `Properties` that don't change that project's output path. Two common shapes:

```xml
<!-- (a) the SAME project re-invokes itself (publish-on-build) -->
<MSBuild Projects="$(MSBuildProjectFullPath)" Targets="Publish" Properties="_IsPublishing=true" />

<!-- (b) project A invokes Build/Publish on ANOTHER project B it consumes
        (e.g. a test or layout project publishing a tool) -->
<MSBuild Projects="..\tool\tool.csproj" Targets="Publish" Properties="_IsPublishing=true" />
```

**Why it's bad**: An MSBuild project instance is identified by its path **plus its global properties**. Passing an extra global property creates a *distinct* instance of the target project — `(project, {_IsPublishing=true})` — that still resolves to the same `OutputPath`/`IntermediateOutputPath` as the instance the solution/graph already builds, `(project, {})`. That project is then built twice, and in a parallel/graph build the two instances can write the same files concurrently (PDBs, `*.sourcelink` and other NativeAOT intermediates, `project.assets.json`), producing `The process cannot access the file because it is being used by another process` or intermittent file-lock failures. This applies whether the offending `<MSBuild>` call is in the target project itself or in some other project in the same build. Use the `check-bin-obj-clash` skill to confirm two evaluations of that project differ only by a path-neutral property while sharing an output path.

```xml
<!-- BAD (a): forks a second instance (path + {_IsPublishing=true}) that shares this project's bin/obj -->
<Target Name="PublishOnBuild" AfterTargets="Build">
  <MSBuild Projects="$(MSBuildProjectFullPath)" Targets="Publish" Properties="_IsPublishing=true" />
</Target>
```

```xml
<!-- GOOD (a): set the flag as a normal (non-global) property and run the target in the SAME instance -->
<PropertyGroup>
  <!-- Capture whether the entry point already invoked publish (it sets _IsPublishing as a global prop). -->
  <_PublishWasInvokedDirectly Condition="'$(_IsPublishing)' == 'true'">true</_PublishWasInvokedDirectly>
  <_IsPublishing>true</_IsPublishing>
</PropertyGroup>

<Target Name="PublishOnBuild"
        AfterTargets="Build"
        DependsOnTargets="Publish"
        Condition="'$(_PublishWasInvokedDirectly)' != 'true'" />
```

For (a), the static property keeps everything in one instance (one output path, nothing to race); running `Publish` via `DependsOnTargets` (or `CallTarget`) reuses that instance instead of forking. The `_PublishWasInvokedDirectly` guard breaks the target cycle when publish is the entry point (e.g. `dotnet publish`, which sets `_IsPublishing=true` as a global property and would otherwise re-trigger `PublishOnBuild`).

```xml
<!-- BAD (b): A forks a publish instance of B that races B's own build in the graph -->
<MSBuild Projects="..\tool\tool.csproj" Targets="Publish" Properties="_IsPublishing=true" />

<!-- GOOD (b): make B publish as part of its OWN build (the (a) fix in tool.csproj), then have A
     just sequence B and consume B's already-produced publish output — never re-publish it. -->
<ItemGroup>
  <ProjectReference Include="..\tool\tool.csproj" ReferenceOutputAssembly="false" />
</ItemGroup>
<!-- A then reads tool's publish dir; it does not invoke Publish on tool. -->
```

For (b), the consumer must not fork the producer with path-neutral global properties. Let the producer publish itself (one instance), reference it only to sequence the build, and read its output.

**When extra global properties ARE fine**: only when the output path encodes the discriminator (`RuntimeIdentifier`, `TargetFramework`, `Configuration`, `Platform`) so each instance writes to a distinct directory. If you must invoke a project with a path-neutral property, give that build its own `BaseIntermediateOutputPath`/output path so it can't collide.

---

## AP-23: `SetTargetFramework` Metadata on a `ProjectReference` to a Non-Multi-Targeting Project

**Smell**: A `<ProjectReference>` carries `SetTargetFramework="TargetFramework=net8.0"` (or similar) metadata, the referenced project is **single-targeting** (uses singular `<TargetFramework>`, not `<TargetFrameworks>`), **and the injected TFM equals the TFM the project already targets**.

```xml
<!-- BAD: Tool.csproj single-targets net8.0 and we inject that SAME net8.0 — redundant AND harmful -->
<ItemGroup>
  <ProjectReference Include="..\Tool\Tool.csproj" SetTargetFramework="TargetFramework=net8.0" />
</ItemGroup>
```

**Why it's bad**: `SetTargetFramework` injects `TargetFramework` as a **global property** on the referenced project's build. That mechanism exists so a consumer can pick *one specific TFM* of a **multi-targeting** project — different TFM values produce different output paths, so each build is distinct and safe.

For a **single-targeting** project, injecting the TFM it **already targets** is **path-neutral**: the project already resolves to `bin\<config>\net8.0\` and `obj\<config>\net8.0\` on its own, so the extra global property doesn't change the output path — it only creates a *distinct* MSBuild project instance `(project, {TargetFramework=net8.0})`. Meanwhile the solution/graph builds that same project as `(project, {})` with no global properties. Both instances resolve to the **same** `OutputPath`/`IntermediateOutputPath`, so the project is **built twice** and the two instances write the same files (assemblies, PDBs, `project.assets.json`, etc.). Under a parallel build this is a classic bin/obj clash — `The process cannot access the file because it is being used by another process` or intermittent, retry-flaky failures. (Injecting a *different* TFM changes the output path and is a legitimate override — see below.)

Note the healthy contrast: the P2P protocol itself does **not** inject `TargetFramework` when it sees a non-multi-targeting reference — it correctly omits the global property. `SetTargetFramework` overrides that safe default and is what reintroduces the clash. Use the `check-bin-obj-clash` skill to confirm two evaluations of the referenced project differ only by a path-neutral `TargetFramework` global property while sharing an output path.

```xml
<!-- GOOD: single-targeting reference needs no SetTargetFramework — just reference it -->
<ItemGroup>
  <ProjectReference Include="..\Tool\Tool.csproj" />
</ItemGroup>
```

**When `SetTargetFramework` IS appropriate**:

1. **Multi-targeting reference** — the referenced project is multi-targeting (`<TargetFrameworks>`) and you deliberately need to consume a specific TFM. Each TFM has its own output path, so the forked instance doesn't collide.

2. **Deliberately overriding a single-targeting project's TFM to a *different* value** — you can use `SetTargetFramework` on a single-targeting reference to build it under a TFM *other than* the one it declares. This is only valid when the passed-in TFM **differs** from what the project single-targets: because the injected `TargetFramework` then changes the output path (`obj\<config>\<different-tfm>\`), the instance no longer collides with the `(project, {})` build. It is **only** the redundant case — passing the *same* TFM the project already targets (path-neutral) — that causes the clash.

**Related: referencing a framework-incompatible project.** Independently of the clash above, whenever the referencing and referenced projects target **incompatible frameworks** (e.g. a `.NETFramework` project referencing a `.NETCoreApp` project, or vice-versa) — **regardless of whether either side is single- or multi-targeting** — you must set both:
- `SkipGetTargetFrameworkProperties="true"` — bypass the P2P `GetTargetFrameworkProperties` negotiation, which would otherwise fail because the frameworks aren't compatible, and
- `ReferenceOutputAssembly="false"` — because an assembly built for an incompatible framework can't be consumed as a reference; you only want to trigger/sequence the build, not reference its output.

```xml
<!-- OK: .NETFramework project builds an incompatible .NETCoreApp tool without referencing its assembly -->
<ProjectReference Include="..\Tool\Tool.csproj"
                  SkipGetTargetFrameworkProperties="true"
                  ReferenceOutputAssembly="false" />
```

**⚠️ Prevent the referencing project's `TargetFramework` from leaking.** When `SkipGetTargetFrameworkProperties="true"` bypasses the negotiation, nothing stops the referencing project's own `TargetFramework` **global property** (present whenever the referencing project is being built for a specific TFM — e.g. it is multi-targeting) from flowing down into the referenced project. If it flows into a **single-targeting** referenced project, that project builds under the *wrong* TFM (and to a different, wrong output path). Guard against it one of two ways:
- set `SetTargetFramework="TargetFramework=<tfm>"` to explicitly pin the referenced build's TFM (also required for multi-targeting references), **or**
- for a single-targeting referenced project you want to build as-declared, set `UndefineProperties="TargetFramework"` to strip the inherited global property so the project uses its own `<TargetFramework>`.

```xml
<!-- OK: strip the referencing project's TargetFramework so the single-targeting tool builds as it declares -->
<ProjectReference Include="..\Tool\Tool.csproj"
                  SkipGetTargetFrameworkProperties="true"
                  UndefineProperties="TargetFramework"
                  ReferenceOutputAssembly="false" />
```

Add `SetTargetFramework` on top of these **only** if you also need to pin the referenced build to a specific TFM (a multi-targeting project, or a single-targeting project you're overriding to a *different* TFM per case 2 above). Use `SetTargetFramework` **or** `UndefineProperties="TargetFramework"`, not both — the former sets the property, the latter removes it.

---

## Quick-Reference Checklist

When reviewing an MSBuild file, scan for these in order:

| # | Check | Severity |
|---|-------|----------|
| AP-02 | Unquoted conditions | 🔴 Error-prone |
| AP-19 | Side effects in evaluation | 🔴 Dangerous |
| AP-21 | Property conditioned on TargetFramework in .props | 🔴 Silent failure |
| AP-22 | Forking a project instance via `<MSBuild>` with path-neutral global properties (self or cross-project) | 🔴 Race/duplicate build |
| AP-23 | `SetTargetFramework` re-injecting a single-targeting project's own TFM on a `ProjectReference` | 🔴 Race/duplicate build |
| AP-03 | Hardcoded absolute paths | 🔴 Broken on other machines |
| AP-06 | `<Reference>` with HintPath for NuGet | 🟡 Legacy |
| AP-07 | Missing `PrivateAssets="all"` on tools | 🟡 Leaks to consumers |
| AP-11 | Missing Inputs/Outputs on targets | 🟡 Perf regression |
| AP-13 | Import without Exists guard | 🟡 Fragile |
| AP-05 | Manual file listing in SDK-style | 🔵 Noise |
| AP-04 | Restating SDK defaults | 🔵 Noise |
| AP-08 | Copy-paste across csproj files | 🔵 Maintainability |
| AP-09 | Scattered package versions | 🔵 Version drift |
| AP-01 | `<Exec>` for built-in tasks | 🔵 Cross-platform |
| AP-14 | Backslashes in cross-platform paths | 🔵 Cross-platform |
| AP-10 | Monolithic targets | 🔵 Maintainability |
| AP-12 | Defaults in .targets instead of .props | 🔵 Ordering issue |
| AP-15 | Unconditional property override | 🔵 Confusing |
| AP-16 | `<Exec>` for string operations | 🔵 Preference |
| AP-17 | Mixed Include/Update in one ItemGroup | 🔵 Subtle bugs |
| AP-18 | Redundant transitive ProjectReferences | 🔵 Graph noise |
| AP-20 | Platform-specific Exec without guard | 🔵 Cross-platform |
