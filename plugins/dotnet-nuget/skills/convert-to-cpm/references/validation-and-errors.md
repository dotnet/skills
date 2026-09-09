# Validation and Common Errors

## Diagnose a failed validation batch

The main workflow already ran clean, restore, and build. Do not repeat them merely to diagnose the same failure. First inspect the relevant error lines and determine whether the failure is caused by CPM edits.

For multi-target framework projects (those with `<TargetFrameworks>` containing multiple TFMs), verify restore works for each framework. If restoration errors are framework-specific, the solution may require conditional `<PackageVersion>` entries or `VersionOverride` for specific projects.

## NuGet error codes

| Error | Meaning | Fix |
|-------|---------|-----|
| **NU1008** | A `PackageReference` still has a `Version` attribute when CPM is enabled | Remove the `Version` attribute or convert to `VersionOverride` |
| **NU1010** | A `PackageReference` has no corresponding `PackageVersion` entry | Add the missing `<PackageVersion>` entry to `Directory.Packages.props` |
| **NU1507** | Multiple package sources without package source mapping | Configure [package source mapping](https://learn.microsoft.com/nuget/consume-packages/package-source-mapping) |

Keep full build output out of the conversation. On success, report a concise status. On failure, inspect only the relevant error lines or a short tail before making a targeted correction.

Only after one CPM-specific correction should you rerun the failed final validation batch from the main workflow. Do not start a separate open-ended validation sequence.

Do not run tests for a version-neutral conversion unless the user explicitly requested them. When the main workflow runs one scoped test pass because resolved versions changed, treat a failure separately unless the evidence clearly ties it to CPM package resolution; avoid unrelated dependency or test-host debugging.

Only CPM-related restore/build errors justify an automatic correction and retry. Do not install SDKs, change `global.json` or roll-forward policy, invoke SDK-internal DLLs, kill processes, or debug file locks/package sources as part of this skill. Report those environmental blockers with the failed command and required user action.

## Common pitfalls

| Pitfall | Solution |
|---------|----------|
| `Directory.Packages.props` not picked up | Ensure it is in the project directory or an ancestor directory. Only the closest one is evaluated |
| Multiple `Directory.Packages.props` files conflict | Use `Import` to chain files, or consolidate into one. Only the nearest file is evaluated per project |
| Version properties in `.props` files cause build errors | Decide whether to inline the version or keep the property. See [msbuild-property-handling.md](msbuild-property-handling.md) |
| Conditional PackageReference loses its condition | Move the condition to the `PackageVersion` entry in `Directory.Packages.props`, or use `VersionOverride` in the project |
| `packages.config` projects are in scope | These must first be [migrated to PackageReference](https://learn.microsoft.com/nuget/consume-packages/migrate-packages-config-to-package-reference) before CPM conversion |
| Global tools or CLI tool references affected | `DotNetCliToolReference` items are deprecated and not managed by CPM. They can be ignored |
