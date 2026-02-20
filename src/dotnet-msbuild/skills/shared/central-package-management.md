# Central Package Management (CPM)

Central Package Management provides a single source of truth for all NuGet package versions across a multi-project repository.

**Enable CPM in `Directory.Packages.props` at the repo root:**

```xml
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
  </PropertyGroup>

  <ItemGroup>
    <PackageVersion Include="Newtonsoft.Json" Version="13.0.3" />
    <PackageVersion Include="xunit" Version="2.9.0" />
  </ItemGroup>

  <ItemGroup>
    <!-- GlobalPackageReference applies to ALL projects — great for analyzers -->
    <GlobalPackageReference Include="StyleCop.Analyzers" Version="1.2.0-beta.556" />
  </ItemGroup>
</Project>
```

**Project files reference packages without versions:**

```xml
<!-- In a .csproj file -->
<ItemGroup>
  <PackageReference Include="Newtonsoft.Json" />
</ItemGroup>
```

**Override a version for a specific project when needed:**

```xml
<PackageReference Include="Newtonsoft.Json" VersionOverride="14.0.0-beta1" />
```

**Benefits:**

- Single source of truth for all package versions
- Easier bulk updates across the entire repo
- Prevents version drift between projects
- `GlobalPackageReference` ensures analyzers apply everywhere without per-project configuration
