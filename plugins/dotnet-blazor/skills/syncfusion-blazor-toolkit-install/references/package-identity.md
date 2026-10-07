# Package Identity Reference

## Syncfusion.Blazor.Toolkit vs. commercial Syncfusion.Blazor*

### The open-source Toolkit: Syncfusion.Blazor.Toolkit

- **NuGet package name**: `Syncfusion.Blazor.Toolkit` (verified at version 1.0.2)
- **License**: MIT
- **Target frameworks**: `net8.0`, `net9.0`, `net10.0`
- **Scope**: Open-source Blazor toolkit for the current published component set
- **Repository**: https://github.com/syncfusion/blazor-toolkit
- **Components included**: Refer to the official Syncfusion Blazor Toolkit documentation for the authoritative and current component list. This skill is not a component reference.
- **No license key required**: Toolkit is free to use without registration

### Commercial Syncfusion.Blazor* Packages

- **Package name(s)**: `Syncfusion.Blazor`, `Syncfusion.Blazor.Core`, `Syncfusion.Blazor.Buttons`, `Syncfusion.Blazor.Calendars`, `Syncfusion.Blazor.Charts`, `Syncfusion.Blazor.Grids`, `Syncfusion.Blazor.Inputs`, etc.
- **License**: Commercial (requires valid license key for production)
- **Scope**: Full-featured Syncfusion component library
- **Requires**: License registration through `Syncfusion.Licensing.SyncfusionLicenseProvider.RegisterLicense(...)`. There is no `AddSyncfusionLicense()` API.
- **Not the same as Toolkit**: Commercial packages are separate products with different APIs and licensing
- **Do not remove them blindly**: If any page still uses a commercial component, keep that package and its `RegisterLicense` call. Remove them only when Toolkit is replacing every commercial component in the app.

## Install the Toolkit (and ONLY the Toolkit for this skill)

```bash
# Correct: Install Toolkit
dotnet add package Syncfusion.Blazor.Toolkit

# Wrong: Do NOT install commercial packages for Toolkit tasks
dotnet add package Syncfusion.Blazor
dotnet add package Syncfusion.Blazor.Core
dotnet add package Syncfusion.Blazor.Buttons
```

## Verify Installation

Check the `.csproj` file:

```xml
<!-- Correct for Toolkit -->
<PackageReference Include="Syncfusion.Blazor.Toolkit" Version="x.y.z" />
<!-- Example only: replace with the latest stable version from NuGet (check nuget.org) -->

<!-- Wrong for Toolkit (these are commercial) -->
<!-- <PackageReference Include="Syncfusion.Blazor" Version="26.1.35" /> -->
<!-- <PackageReference Include="Syncfusion.Blazor.Buttons" Version="26.1.35" /> -->
```
