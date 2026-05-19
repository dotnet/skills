## Summary

Adds four new skills to the `dotnet-msbuild` plugin covering MSBuild target/props authoring best practices derived from analysis of the MSBuild repo's own `.targets` and `.props` files.

Closes #668

## New Skills

### 1. `target-authoring`
Three-level target chain pattern (Before/Core/After), DependsOn chain extension, DependsOnTargets vs BeforeTargets/AfterTargets guidance, Returns vs Outputs, incremental build with Inputs/Outputs, naming conventions, and a complete target template.

### 2. `property-patterns`
Conditional defaults, semicolon-delimited composition, path normalization, MSBuild string functions, TFM condition helpers, guard properties, feature gating, and fallback chains.

### 3. `item-management`
Include/Remove/Update semantics, batching (single-axis vs cross-product pitfall), item transforms, Exclude patterns, conditional item inclusion, PrivateAssets/ExcludeAssets metadata, and FileWrites registration for generated files.

### 4. `extension-points`
CustomBefore/CustomAfter hooks, wildcard import directories with alphabetic ordering, import gating with control properties, NuGet package build extension layout (build/buildTransitive), Directory.Build discovery and multi-level hierarchy, and the import guard pattern.

## Tests

Each skill includes:
- eval.yaml with scenarios, assertions, and rubric
- Anti-pattern fixture files (.csproj, Directory.Build.props/targets) for the eval to review

## Checklist
- [x] Each skill has YAML frontmatter (name, description, license: MIT)
- [x] All skills under 500 lines
- [x] eval.yaml tests provided for each skill
- [x] CODEOWNERS already covers /plugins/dotnet-msbuild/ and /tests/dotnet-msbuild/
- [x] Issue opened first (#668)
