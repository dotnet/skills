---
name: item-management
description: "Patterns for managing MSBuild item groups: Include/Remove/Update semantics, item metadata, batching with %(Metadata), transforms, per-item filtering, and cross-product batching pitfalls. Only activate in MSBuild/.NET build context. USE FOR: diagnosing and fixing item group anti-patterns in .csproj files, reviewing item management for correctness, fixing CS2002 duplicate file warnings from SDK globbing, fixing targets that run more times than expected due to cross-product batching, fixing Include vs Update misuse on SDK-globbed items, fixing FileWrites registration for generated file clean support, moving generated files to IntermediateOutputPath. DO NOT USE FOR: target chain architecture (use target-authoring), property patterns (use property-patterns), incrementality (use incremental-build), general anti-patterns (use msbuild-antipatterns), non-MSBuild build systems."
license: MIT
---

# MSBuild Item Management Patterns

Canonical patterns for working with item groups, from `Microsoft.Common.CurrentVersion.targets`.

## Include / Remove / Update — Three Operations

| Operation | Purpose | When to use |
|---|---|---|
| `Include` | Add new items to the group | Creating items with identity + metadata |
| `Remove` | Remove items matching a pattern | Excluding files or clearing a group |
| `Update` | Modify metadata on existing items | Adding/changing metadata without re-adding |

### Include — Add Items

```xml
<ItemGroup>
  <Compile Include="Generated\*.cs">
    <AutoGen>true</AutoGen>
  </Compile>
</ItemGroup>
```

### Remove — Subtract Items

```xml
<ItemGroup>
  <!-- Remove specific items -->
  <Reference Remove="$(AdditionalExplicitAssemblyReferences)" />

  <!-- Set subtraction: prior minus current -->
  <_CleanOrphanFileWrites Include="@(_CleanPriorFileWrites)"
      Exclude="@(_CleanCurrentFileWrites)" />

  <!-- Clear an entire group -->
  <_Temporary Remove="@(_Temporary)" />
</ItemGroup>
```

### Update — Modify Existing Items

```xml
<ItemGroup>
  <EmbeddedResource Update="@(EmbeddedResource)"
      Condition="'%(NuGetPackageId)' == 'Microsoft.CodeAnalysis.Collections'">
    <GenerateSource>true</GenerateSource>
    <ClassName>Microsoft.CodeAnalysis.Collections.SR</ClassName>
  </EmbeddedResource>
</ItemGroup>
```

`Update` does not add items — it only modifies items already in the group.

## Item Batching — %(Metadata)

When `%(Metadata)` appears in target attributes or task parameters, MSBuild **batches** execution per unique metadata value.

### Target-level batching (Outputs)

```xml
<Target Name="GenerateSatelliteAssemblies"
    Inputs="$(MSBuildAllProjects);@(_SatelliteAssemblyResourceInputs)"
    Outputs="$(IntermediateOutputPath)%(Culture)\$(TargetName).resources.dll">
  <!-- Runs once per unique Culture value -->
</Target>
```

### Task-level batching

```xml
<Copy SourceFiles="@(_SourceItems)"
    DestinationFiles="@(_SourceItems->'$(OutDir)%(TargetPath)')">
</Copy>
```

### Per-item filtering with Condition

```xml
<ItemGroup>
  <_ResxOutput Include="@(EmbeddedResource->'%(OutputResource)')"
      Condition="'%(EmbeddedResource.WithCulture)' == 'false'" />
</ItemGroup>
```

### Batching rules

- `%(Metadata)` in `Condition` or `Outputs` → target batches per unique value.
- `%(Metadata)` in task parameters → task batches per unique value.
- **Do not mix `%()` from different item groups** in the same expression — this causes a cross-product (see Common Pitfalls).

## Item Transforms — @(Item->'expression')

Transforms create new item lists by applying an expression to each item:

```xml
<!-- Transform file paths to destinations -->
<Copy SourceFiles="@(IntermediateAssembly)"
    DestinationFiles="@(IntermediateAssembly->'$(OutDir)%(Filename)%(Extension)')"/>

<!-- Transform with separator for display -->
<Message Text="Files: @(Compile->'%(Filename)', ', ')" />
```

## Exclude Pattern — Set Subtraction on Include

```xml
<ItemGroup>
  <Compile Include="**\*.cs" Exclude="Generated\**;Tests\**" />
</ItemGroup>
```

`Exclude` only works on `Include` — it cannot be used with `Update` or `Remove`.

## Conditional Item Inclusion

```xml
<!-- Condition on ItemGroup — all or nothing -->
<ItemGroup Condition="'$(NetCoreBuild)' == 'true'">
  <PackageReference Include="System.IO.Pipelines" />
</ItemGroup>

<!-- Condition on individual items -->
<ItemGroup>
  <PackageReference Include="System.IO.Pipelines"
      Condition="'$(NetCoreBuild)' == 'true'" />
</ItemGroup>
```

## PrivateAssets on Tool/Analyzer Packages

```xml
<ItemGroup>
  <PackageReference Include="Microsoft.CodeAnalysis.NetAnalyzers" PrivateAssets="all" />
  <PackageReference Include="StyleCop.Analyzers" PrivateAssets="all" />
</ItemGroup>
```

## Common Pitfalls

### Cross-product batching

Referencing `%(Metadata)` from two different item groups creates O(N×M) executions:

```xml
<!-- BAD: Cross-product of @(Source) × @(Config) -->
<Exec Command="process %(Source.Identity) with %(Config.Identity)" />

<!-- GOOD: Reference one group via batching, the other via property -->
<Exec Command="process %(Source.Identity) with $(ConfigFile)" />
```

### Generated files in source tree

Write to `$(IntermediateOutputPath)` (obj/), not the source directory. Source-tree generation pollutes version control and can cause duplicate compilation via globs.

### Missing FileWrites

Every file created during a target must be added to `@(FileWrites)` for `dotnet clean` support.
