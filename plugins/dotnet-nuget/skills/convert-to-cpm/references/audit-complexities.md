# Audit Complexities

Audit only the resolved scope. Use the baseline package snapshot plus one targeted search across in-scope project, `.props`, and `.targets` files; do not explore unrelated repository files. Read the sections below only for complexities that were actually detected.

## 1. Version set via MSBuild property

If a `PackageReference` uses a property for its version (e.g., `Version="$(SomePackageVersion)"`), trace the property definition. If the property is defined in a `Directory.Build.props`, `.props` import, or the project file itself, note it for the user. These require manual decisions about whether to replace the property with a literal version in `Directory.Packages.props` or to keep the property and use it within `Directory.Packages.props`.

See [msbuild-property-handling.md](msbuild-property-handling.md) for decision workflow.

## 2. Conditional PackageReference items

If a `PackageReference` is inside a conditional `<ItemGroup>` (e.g., `Condition="'$(TargetFramework)' == 'net8.0'"`), the version must still be centralized. The `PackageVersion` entry in `Directory.Packages.props` can use the same condition, or the project can use `VersionOverride` if the condition is project-specific.

## 3. Same package with different versions

If the same package ID appears with different versions across projects, this is a **version conflict** that requires a user decision before proceeding. CPM requires a single `<PackageVersion>` per package (unless `VersionOverride` is used), so conflicts must be resolved.

For each conflict, present a focused summary showing:

- The package name and all distinct versions found
- Which projects use each version, so the user can see the scope of the disagreement
- Whether the difference is major, minor, or patch — this signals the level of risk
- Any known security advisories on the lower versions (cross-reference with `dotnet package list --vulnerable` if available)

Then present the resolution options with their trade-offs:

1. **Align to the highest version** — simplest path; all projects get the latest. Risk: a major version bump may introduce breaking API changes in projects that were on an older version.
2. **Align to the lowest version** — conservative; avoids pulling in untested changes. Risk: projects already on higher versions would be downgraded, which may break them or regress security fixes.
3. **Use `VersionOverride`** — projects that need a different version keep it via `VersionOverride` in their project file. The central `<PackageVersion>` holds the default. This preserves the status quo but partially undermines centralization for that package.

Do not upgrade any package beyond the highest version already in use across the scope — this avoids introducing version incompatibilities or breaking changes that are unrelated to the CPM conversion itself. Instead, note any known advisories or upgrade opportunities as follow-up items in the post-conversion report for the user to address after the conversion is complete.

If the user already supplied a conflict strategy, treat that as the decision and do not ask again. Otherwise ask the user to choose for each conflict and stop before editing. After each decision, record which projects will see a version change and which will stay the same.

- **Major version difference**: Emphasize the risk of breaking API changes. Recommend `VersionOverride` unless the user is prepared to validate all affected projects.
- **Minor or patch difference**: Prefer the highest version but highlight any security advisories. Note that patch-level alignment is usually safe.
- **One version is vulnerable**: Note the advisory in the post-conversion report as a follow-up item. Do not upgrade the version as part of the CPM conversion.

## 4. Known security advisories

If the user requested security information, a known advisory must be verified, or conflict resolution crosses a major package version, prefer one scoped vulnerability query after the baseline restore:

- SDK 10+: `dotnet package list --project <scope> --vulnerable --include-transitive --format json --no-restore`
- SDK 7.0.200–9.x: `dotnet list <scope> package --vulnerable --include-transitive --format json --no-restore`

Extract only package ID, resolved version, advisory severity/URL, and affected projects into the audit summary. Preserve the full output as an optional artifact if useful, but do not read it repeatedly. Do not also run deprecated or outdated scans by default. Do not upgrade beyond the highest version already in scope; record advisory remediation as a follow-up item instead.

## 5. Packages without a Version attribute

These may already be managed by CPM from a parent directory or may be using a default version. Verify whether a `Directory.Packages.props` in an ancestor directory already provides the version.

## 6. PackageReference in imported .props/.targets files

Scan for `<Import>` elements in project files and `Directory.Build.props` to discover shared `.props` or `.targets` files that may contain `PackageReference` items. Search those imported files for package references — they need the same treatment but modifying shared build files has broader impact. Flag these to the user.

## 7. VersionOverride already in use

If any project already uses `VersionOverride`, note it — this suggests partial CPM adoption may already be in progress.
