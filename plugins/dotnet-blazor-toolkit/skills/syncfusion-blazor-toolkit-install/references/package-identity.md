# Install Syncfusion Blazor Toolkit

## Package Identity Reference

### Syncfusion.Blazor.Toolkit vs. Commercial Syncfusion.Blazor*

### The Open-Source Toolkit: Syncfusion.Blazor.Toolkit

- **NuGet package name**: `Syncfusion.Blazor.Toolkit`
- **License**: MIT
- **Scope**: Open-source Blazor toolkit for the current published component set
- **Repository**: Official Syncfusion GitHub repository
- **Components included**: Refer to the official Syncfusion Blazor Toolkit documentation for the authoritative and current component list. This skill is not a component reference.
- **No license key required**: Toolkit is free to use without registration

### Commercial Syncfusion.Blazor* Packages

- **Package name(s)**: `Syncfusion.Blazor`, `Syncfusion.Blazor.Core`, `Syncfusion.Blazor.Buttons`, `Syncfusion.Blazor.Calendars`, `Syncfusion.Blazor.Charts`, `Syncfusion.Blazor.Grids`, `Syncfusion.Blazor.Inputs`, etc.
- **License**: Commercial (requires valid license key for production)
- **Scope**: Full-featured Syncfusion component library
- **Requires**: License-key registration via `AddSyncfusionLicense()` (or similar)
- **Not the same as Toolkit**: Commercial packages are separate products with different APIs and licensing

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
