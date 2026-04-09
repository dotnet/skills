# Validation and Common Errors

## Restore validation

Always validate from a clean state to ensure full package resolution, not incremental cache:

```bash
dotnet clean
dotnet restore
```

For multi-target framework projects (those with `<TargetFrameworks>` containing multiple TFMs), verify restore works for each framework. If restoration errors are framework-specific, the solution may require conditional `<PackageVersion>` entries or `VersionOverride` for specific projects.

## NuGet error codes

| Error | Meaning | Fix |
|-------|---------|-----|
| **NU1008** | A `PackageReference` still has a `Version` attribute when CPM is enabled | Remove the `Version` attribute or convert to `VersionOverride` |
| **NU1010** | A `PackageReference` has no corresponding `PackageVersion` entry | Add the missing `<PackageVersion>` entry to `Directory.Packages.props` |
| **NU1507** | Multiple package sources without package source mapping | Configure [package source mapping](https://learn.microsoft.com/nuget/consume-packages/package-source-mapping) |

## Build validation

If `dotnet restore` succeeds, also run `dotnet build` to verify:

```bash
dotnet build
```

## Common pitfalls

| Pitfall | Solution |
|---------|----------|
| `Directory.Packages.props` not picked up | Ensure it is in the project directory or an ancestor directory. Only the closest one is evaluated |
| Multiple `Directory.Packages.props` files conflict | Use `Import` to chain files, or consolidate into one. Only the nearest file is evaluated per project |
| Version properties in `.props` files cause build errors | Decide whether to inline the version or keep the property. See [msbuild-property-handling.md](msbuild-property-handling.md) |
| Conditional PackageReference loses its condition | Move the condition to the `PackageVersion` entry in `Directory.Packages.props`, or use `VersionOverride` in the project |
| `packages.config` projects are in scope | These must first be [migrated to PackageReference](https://learn.microsoft.com/nuget/consume-packages/migrate-packages-config-to-package-reference) before CPM conversion |
| Global tools or CLI tool references affected | `DotNetCliToolReference` items are deprecated and not managed by CPM. They can be ignored |
