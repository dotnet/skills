# MSBuild Property Handling

This covers how to handle MSBuild properties that define package versions (for example, `Version="$(PackageVersionProperty)"`) during CPM conversion.

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

- **Inline**: Replace the property usage with a literal version in `Directory.Packages.props` and remove the property definition before final validation, after verifying no references remain
- **Keep**: Reference the property from `Directory.Packages.props` (e.g., `<PackageVersion Include="PackageA" Version="$(PackageAVersion)" />`)

### 1.3. Property used for other purposes

If the property is used beyond package versioning, do not remove it. Use the property's resolved value in `Directory.Packages.props` and inform the user.

### 1.4. Property defined outside scope

If the property is defined outside the conversion scope (e.g., in parent repository build infrastructure), stop before editing that package. Ask the user to choose one safe option:

1. Expand the conversion scope to include the defining file.
2. Use the resolved literal value in `Directory.Packages.props` and leave the external property unchanged.
3. Keep the property reference in `Directory.Packages.props` only after confirming its definition is evaluated before that file.

Do not skip the central `PackageVersion` and continue: after CPM is enabled that would leave the project with either `NU1008` or `NU1010`.

## Part 2: Clean up obsolete properties

After updating all package references and before the final restore/build, remove property definitions that the user chose to inline. Match the XML element structurally rather than depending on a particular newline style. Before removing any property, verify it has zero remaining references outside its own definition:

```bash
# Unix/macOS
grep -r '$(PropertyName)' --include='*.csproj' --include='*.props' --include='*.targets' .

# Windows (PowerShell)
Get-ChildItem -Recurse -Include *.csproj,*.props,*.targets | Select-String '$(PropertyName)'
```

Only remove a property if it has zero remaining references outside its own definition. Preserve all non-versioning properties in the same file (e.g., `OutputPath`, `LangVersion`). Then run two distinct checks:

- Search for `$(PropertyName)` to confirm no uses remain.
- Search for the XML element name (for example, `<PropertyName>`) to confirm the obsolete definition itself is gone.

Both checks must pass before final validation.
