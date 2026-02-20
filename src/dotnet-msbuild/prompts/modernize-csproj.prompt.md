---
description: "Modernize and clean up MSBuild project files"
---

# Modernize Project Files

Help me modernize and clean up the MSBuild project files in this repository.

## Steps

1. Scan for all MSBuild files: `*.csproj`, `*.vbproj`, `*.fsproj`, `*.props`, `*.targets`
2. Check for `Directory.Build.props`, `Directory.Build.targets`, `Directory.Packages.props`
3. For each project file, assess:
   - Is it SDK-style or legacy? (Legacy needs migration)
   - Are there unnecessary explicit file includes?
   - Is there an `AssemblyInfo.cs` that should be project properties?
   - Are there `packages.config` files? (Migrate to PackageReference)
   - Are there hardcoded paths?
   - Are there properties duplicated across projects?
4. Create `Directory.Build.props` if it doesn't exist, consolidate common properties
5. Consider Central Package Management (`Directory.Packages.props`)
6. Apply modernization changes and verify with a build
7. Produce a summary of changes made

## What to Look For

- Properties repeated in multiple .csproj files → centralize in Directory.Build.props
- Legacy format projects → migrate to SDK-style
- `<Reference>` tags for NuGet packages → convert to `<PackageReference>` (keep `<Reference>` only for .NET Framework GAC assemblies)
- Custom targets without Inputs/Outputs → add for incremental build
- Scattered package versions → centralize with Directory.Packages.props
