---
name: target-authoring
description: "Write and repair custom MSBuild target structure. USE FOR: broken SDK target chains, hooks attached to Build that miss direct Compile/CoreCompile entry points, replacing instead of extending CompileDependsOn, query targets that misuse Outputs instead of Returns, dependency ordering, and missing FileWrites in an otherwise structural target defect. Covers DependsOnTargets, BeforeTargets, AfterTargets, Build/CoreBuild layering, and chain extension. NEVER INVOKE only to tune no-op timestamp-check performance when the target already has correct Inputs, Outputs, hooks, and clean tracking; use incremental-build. Exclude parallelism, broad review, and non-MSBuild systems."
license: MIT
---

# Custom Target Authoring Patterns

Canonical patterns from `Microsoft.Common.CurrentVersion.targets` in the MSBuild repository.

## The Three-Level Target Chain

Every major entry point (Build, Rebuild, Clean) delegates to a **property** listing its dependencies, which chains through Before → Core → After:

```xml
<PropertyGroup>
  <BuildDependsOn>
    BeforeBuild;
    CoreBuild;
    AfterBuild
  </BuildDependsOn>
</PropertyGroup>

<Target Name="Build"
    Condition=" '$(_InvalidConfigurationWarning)' != 'true' "
    DependsOnTargets="$(BuildDependsOn)"
    Returns="@(TargetPathWithTargetPlatformMoniker)" />

<!-- Empty extensibility targets — users override these -->
<Target Name="BeforeBuild" />
<Target Name="AfterBuild" />
```

`CoreBuild` delegates to `$(CoreBuildDependsOn)` and includes error handlers:

```xml
<Target Name="CoreBuild" DependsOnTargets="$(CoreBuildDependsOn)">
  <OnError ExecuteTargets="_TimeStampAfterCompile;PostBuildEvent"
      Condition="'$(RunPostBuildEvent)' == 'Always'" />
  <OnError ExecuteTargets="_CleanRecordFileWrites" />
</Target>
```

### Rules

- Delegate to a property (`DependsOnTargets="$(MyTargetDependsOn)"`), not hardcoded targets.
- `OnError` goes inside the orchestrating target to ensure cleanup runs even on failure.
- Empty Before/After targets are extensibility points. Users override them; SDKs never put logic in them.

## Chain Extension — Append, Never Overwrite

When adding a custom target to an existing chain, **append** to the `DependsOn` property:

```xml
<!-- GOOD: Append to existing chain -->
<PropertyGroup>
  <CompileDependsOn>$(CompileDependsOn);MyCodeGenTarget</CompileDependsOn>
</PropertyGroup>

<!-- BAD: Overwrites the entire chain, dropping SDK targets -->
<PropertyGroup>
  <CompileDependsOn>MyCodeGenTarget</CompileDependsOn>
</PropertyGroup>
```

## DependsOnTargets vs BeforeTargets vs AfterTargets

| Mechanism | Defined in | Best for |
|---|---|---|
| `DependsOnTargets` | The target that needs deps | Target explicitly requires others |
| `BeforeTargets` | The injecting target | Insert before a target you don't own |
| `AfterTargets` | The injecting target | Insert after a target you don't own |

Validation targets use `BeforeTargets` to intercept all entry points:

```xml
<Target Name="_CheckForInvalidConfigurationAndPlatform"
    BeforeTargets="$(BuildDependsOn);Build;$(RebuildDependsOn);Rebuild;$(CleanDependsOn);Clean">
</Target>
```

**Rules:**

- Use `DependsOnTargets` when your target needs specific prerequisites.
- Use `BeforeTargets`/`AfterTargets` when injecting into a pipeline you don't own.
- Prefer `BeforeTargets="CoreCompile"` over modifying `$(CompileDependsOn)` when you don't control the targets file.

## Returns vs Outputs

```xml
<!-- Build returns items for consumption by referencing projects -->
<Target Name="Build"
    DependsOnTargets="$(BuildDependsOn)"
    Returns="@(TargetPathWithTargetPlatformMoniker)" />

<!-- GetTargetPath is a lightweight query target -->
<Target Name="GetTargetPath" Returns="@(TargetPathWithTargetPlatformMoniker)" />
```

- **`Returns`** specifies what the MSBuild task receives when calling this project. Use for inter-project communication.
- **`Outputs`** on inner targets is for incrementality (timestamp checks). Use for up-to-date detection.
- Never mix the two purposes. Query targets (`GetTargetPath`, `GetTargetFrameworks`) should use `Returns`, not `Outputs`.

## Target Naming Conventions

| Pattern | Meaning | Example |
|---|---|---|
| `_PrefixedName` | Internal/private target | `_TimeStampBeforeCompile` |
| `CoreXxx` | The actual implementation | `CoreBuild`, `CoreCompile` |
| `BeforeXxx` / `AfterXxx` | Empty extensibility hooks | `BeforeBuild`, `AfterCompile` |
| `PrepareXxx` | Setup/validation phase | `PrepareForBuild` |
| `ResolveXxx` | Discovery/resolution phase | `ResolveReferences` |
| `GetXxx` | Lightweight query (no side effects) | `GetTargetPath` |

## Complete Custom Target Template

```xml
<!-- 1. Define the DependsOn chain for extensibility -->
<PropertyGroup>
  <MyFeatureDependsOn>
    _ValidateMyFeatureInputs;
    BeforeMyFeature;
    CoreMyFeature;
    AfterMyFeature
  </MyFeatureDependsOn>
</PropertyGroup>

<!-- 2. Outer target with Returns for inter-project communication -->
<Target Name="MyFeature"
    DependsOnTargets="$(MyFeatureDependsOn)"
    Returns="@(MyFeatureOutput)" />

<!-- 3. Empty extensibility points -->
<Target Name="BeforeMyFeature" />
<Target Name="AfterMyFeature" />

<!-- 4. Core implementation with Inputs/Outputs for incrementality -->
<Target Name="CoreMyFeature"
    Inputs="$(MSBuildAllProjects);@(MyFeatureInput)"
    Outputs="$(IntermediateOutputPath)myfeature.generated.cs">
  <Exec Command="my-tool.exe -o $(IntermediateOutputPath)myfeature.generated.cs" />
  <!-- 5. Register outputs for clean tracking -->
  <ItemGroup>
    <Compile Include="$(IntermediateOutputPath)myfeature.generated.cs" />
    <FileWrites Include="$(IntermediateOutputPath)myfeature.generated.cs" />
  </ItemGroup>
</Target>

<!-- 6. Validation target runs first in the dependency chain -->
<Target Name="_ValidateMyFeatureInputs">
  <Error Text="MyFeatureInput items are required."
         Condition="'@(MyFeatureInput)' == ''" />
</Target>
```

## Common Pitfalls

- **Overwriting `DependsOn` properties** drops SDK targets silently. Always include `$(ExistingProperty)` when appending.
- **Using `Outputs` on query targets** causes MSBuild to skip them when "up to date," returning stale data. Use `Returns`.
- **Defining targets in `.props`** means `BeforeTargets` on SDK targets have nothing to hook into yet. Move targets to `.targets`.
- **Forgetting `OnError`** in orchestrating targets means file tracking fails on build errors, breaking subsequent incremental builds.
