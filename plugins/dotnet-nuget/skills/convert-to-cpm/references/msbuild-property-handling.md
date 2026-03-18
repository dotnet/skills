# MSBuild Property Handling

This covers how to handle MSBuild properties that define package versions (e.g., `Version="$(DIVersion)"` or `Version="$(BlobsVersion)"`) during CPM conversion.

## Import order guidance

If keeping a property reference in `Directory.Packages.props` (e.g., `Version="$(PackageAVersion)"`), the property must be defined in a file that MSBuild evaluates before `Directory.Packages.props`. Properties in `Directory.Build.props` satisfy this requirement because MSBuild imports `Directory.Build.props` before `Directory.Packages.props`.

## Part 1: Make property decisions

For each `PackageReference` that used an MSBuild property for its version:

### 1.1. Check if the property is used elsewhere

Search all project files, `.props`, and `.targets` files in scope for references to the property name:

```bash
# Unix/macOS
grep -r '$(PropertyName)' --include='*.csproj' --include='*.props' --include='*.targets' .

# Windows (PowerShell)
Get-ChildItem -Recurse -Include *.csproj,*.props,*.targets | Select-String '$(PropertyName)'
```

If it appears only in `PackageReference` version attributes, it is safe to remove after inlining.

### 1.2. Property only used for versioning (in scope)

If the property is defined in a file within scope (e.g., `Directory.Build.props`), ask the user whether to:

- **Inline**: Replace the property usage with a literal version in `Directory.Packages.props` and remove the property definition (deferred to step 9)
- **Keep**: Reference the property from `Directory.Packages.props` (e.g., `<PackageVersion Include="PackageA" Version="$(PackageAVersion)" />`)

### 1.3. Property used for other purposes

If the property is used beyond package versioning, do not remove it. Use the property's resolved value in `Directory.Packages.props` and inform the user.

### 1.4. Property defined outside scope

If the property is defined outside the conversion scope (e.g., in parent repository build infrastructure), flag it to the user and skip that package. Add a comment in `Directory.Packages.props`:

```xml
<!-- PackageA: version managed externally via $(PackageAVersion) in [file path] -->
```

## Part 2: Clean up obsolete properties

After restore and build succeed (step 8), remove property definitions that the user chose to inline. Before removing any property, verify it has zero remaining references outside its own definition:

```bash
# Unix/macOS
grep -r '$(PropertyName)' --include='*.csproj' --include='*.props' --include='*.targets' .

# Windows (PowerShell)
Get-ChildItem -Recurse -Include *.csproj,*.props,*.targets | Select-String '$(PropertyName)'
```

Only remove a property if it has zero remaining references outside its own definition. Preserve all non-versioning properties in the same file (e.g., `OutputPath`, `LangVersion`).
