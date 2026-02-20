---
name: nuget-restore-failures
description: "Diagnose and fix NuGet package restore failures in .NET projects. Only activate in MSBuild/.NET build contexts (see shared/domain-check.md for signals). Use when dotnet restore fails, packages can't be resolved, feed authentication fails, or version conflicts occur. Covers nuget.config issues, private feed auth, version conflicts, lock files, source mapping, and offline scenarios. DO NOT use for non-.NET package managers (npm, pip, Maven, etc.)."
---

# NuGet Restore Failures

## Feed Authentication Failures

### Symptoms

- `401 Unauthorized` or `403 Forbidden` during restore
- `Unable to load the service index for source`
- `Response status code does not indicate success: 401`
- Restore works locally but fails in CI

### Azure Artifacts

Install the credential provider:

```shell
# Windows
iex "& { $(irm https://aka.ms/install-artifacts-credprovider.ps1) }"

# Linux/macOS
sh -c "$(curl -fsSL https://aka.ms/install-artifacts-credprovider.sh)"
```

Add the feed with credentials:

```shell
dotnet nuget add source "https://pkgs.dev.azure.com/{org}/{project}/_packaging/{feed}/nuget/v3/index.json" \
  --name "AzureArtifacts" \
  --username "az" \
  --password "<PAT>" \
  --store-password-in-clear-text
```

For CI (Azure Pipelines), use the `NuGetAuthenticate@1` task before restore, or set the `VSS_NUGET_EXTERNAL_FEED_ENDPOINTS` environment variable:

```json
{"endpointCredentials": [{"endpoint":"https://pkgs.dev.azure.com/{org}/_packaging/{feed}/nuget/v3/index.json", "username":"az", "password":"<PAT>"}]}
```

### GitHub Packages

Requires a PAT with `read:packages` scope (and `write:packages` for publishing).

```shell
dotnet nuget add source "https://nuget.pkg.github.com/{OWNER}/index.json" \
  --name "GitHubPackages" \
  --username "{GITHUB_USERNAME}" \
  --password "{PAT}" \
  --store-password-in-clear-text
```

Or configure in `nuget.config`:

```xml
<packageSources>
  <add key="GitHubPackages" value="https://nuget.pkg.github.com/{OWNER}/index.json" />
</packageSources>
<packageSourceCredentials>
  <GitHubPackages>
    <add key="Username" value="{GITHUB_USERNAME}" />
    <add key="ClearTextPassword" value="{PAT}" />
  </GitHubPackages>
</packageSourceCredentials>
```

### Private / Self-Hosted Feeds

Configure credentials in `nuget.config` (user-level at `%APPDATA%\NuGet\NuGet.Config` or `~/.nuget/NuGet/NuGet.Config`):

```xml
<configuration>
  <packageSources>
    <add key="PrivateFeed" value="https://myserver/nuget/v3/index.json" />
  </packageSources>
  <packageSourceCredentials>
    <PrivateFeed>
      <add key="Username" value="user" />
      <add key="ClearTextPassword" value="secret" />
    </PrivateFeed>
  </packageSourceCredentials>
</configuration>
```

> **Important:** The key inside `<packageSourceCredentials>` must exactly match the key in `<packageSources>`. Spaces and special characters in source names must be replaced with `__x0020__` (for space) in the credential section — or just avoid spaces in source names.

### CI/CD Authentication Patterns

**Environment variable approach** — set `NUGET_CREDENTIALPROVIDER_SESSIONTOKENCACHE_ENABLED=true` and provide credentials via `VSS_NUGET_EXTERNAL_FEED_ENDPOINTS`.

**nuget.config transform** — use CI secrets to inject credentials at build time:

```shell
dotnet nuget update source "PrivateFeed" \
  --username "$FEED_USER" \
  --password "$FEED_PAT" \
  --store-password-in-clear-text \
  --configfile ./nuget.config
```

**GitHub Actions example:**

```yaml
- run: dotnet nuget add source "https://nuget.pkg.github.com/${{ github.repository_owner }}/index.json" --name github --username ${{ github.actor }} --password ${{ secrets.GITHUB_TOKEN }} --store-password-in-clear-text
- run: dotnet restore
```

---

## nuget.config Misconfiguration

### Config Hierarchy

NuGet merges config files from multiple levels (closest to project wins):

1. **Project-level** — `nuget.config` next to `.csproj` or `sln`
2. **Directory ancestors** — any `nuget.config` in parent directories up to the drive root
3. **User-level** — `%APPDATA%\NuGet\NuGet.Config` (Windows) or `~/.nuget/NuGet/NuGet.Config` (Linux/macOS)
4. **Machine-level** — `%ProgramFiles(x86)%\NuGet\Config\` (Windows)

Check effective config:

```shell
dotnet nuget list source
```

### The `<clear />` Element

`<clear />` removes all previously-defined sources (from higher-level configs). This is useful for controlling exactly which feeds are used, but can break restore if you forget to re-add `nuget.org`:

```xml
<packageSources>
  <clear />
  <!-- Only these sources will be used -->
  <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  <add key="PrivateFeed" value="https://myfeed/nuget/v3/index.json" />
</packageSources>
```

**Common mistake:** Adding `<clear />` in a project-level config without re-adding `nuget.org`, causing all public packages to fail resolution.

### Source URL Issues

- **HTTP vs HTTPS** — NuGet 6.3+ blocks HTTP sources by default (`allowInsecureConnections` must be set on the source if HTTP is required)
- **Trailing slashes** — Some servers require them, some break with them. Match the documented URL exactly.
- **v2 vs v3** — Use `/v3/index.json` endpoints. v2 endpoints (`/nuget`) may be deprecated or slower.
- **Typos** — A typo in the feed URL produces `Unable to load the service index`, which looks like an auth error but is a connectivity issue.

### Package Source Ordering

When no source mapping is configured, NuGet queries all configured sources and picks the best (highest version) match. Sources are NOT queried in order of priority — all sources are checked. Use `<packageSourceMapping>` to control which source provides which packages.

---

## Version Conflicts & Resolution

### NU1605: Package Downgrade Detected

Occurs when a transitive dependency pulls a higher version than what's directly referenced:

```
NU1605: Detected package downgrade: Newtonsoft.Json from 13.0.3 to 12.0.1.
```

**Fixes:**

1. Upgrade the direct `<PackageReference>` to the higher version:
   ```xml
   <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
   ```
2. Or suppress the warning (not recommended for production):
   ```xml
   <PropertyGroup>
     <NoWarn>$(NoWarn);NU1605</NoWarn>
   </PropertyGroup>
   ```

### Diamond Dependency Conflicts

Project → PackageA → SharedLib 2.0  
Project → PackageB → SharedLib 1.0

NuGet uses a **nearest-wins** rule: the version closest to the project root wins. If neither is directly referenced, the higher version typically wins. To force a specific version, add an explicit `<PackageReference>`:

```xml
<PackageReference Include="SharedLib" Version="2.0.0" />
```

### Floating Versions

`*` and range syntax (`1.*`, `[1.0, 2.0)`) allow flexible version resolution:

```xml
<PackageReference Include="MyLib" Version="1.*" />    <!-- latest 1.x -->
<PackageReference Include="MyLib" Version="*" />       <!-- latest of any version -->
```

**Risks:** Builds are non-reproducible. A new patch can break things silently. **Pin exact versions** for production projects, or use lock files.

### Central Package Management (CPM)

When `Directory.Packages.props` exists with `<ManagePackageVersions>true</ManagePackageVersions>`, version management changes:

- **`Directory.Packages.props`** defines versions with `<PackageVersion>`:
  ```xml
  <Project>
    <PropertyGroup>
      <ManagePackageVersions>true</ManagePackageVersions>
    </PropertyGroup>
    <ItemGroup>
      <PackageVersion Include="Newtonsoft.Json" Version="13.0.3" />
    </ItemGroup>
  </Project>
  ```

- **`.csproj` files** reference packages WITHOUT versions:
  ```xml
  <PackageReference Include="Newtonsoft.Json" />
  ```

**Common CPM errors:**

- `NU1008`: Project has a PackageReference with a Version when CPM is enabled — remove the `Version` attribute from the `.csproj`.
- Using `<PackageReference Update="...">` in `.csproj` to override a version — this is the correct CPM override mechanism. `Include` is for referencing; `Update` is for overriding the centrally-defined version.

```xml
<!-- In .csproj to override the central version -->
<PackageReference Update="Newtonsoft.Json" Version="13.0.1" />
```

---

## Lock Files (`packages.lock.json`)

### Enabling Lock Files

Add to your project or `Directory.Build.props`:

```xml
<PropertyGroup>
  <RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>
</PropertyGroup>
```

Run `dotnet restore` to generate `packages.lock.json`. **Commit this file** to source control.

### CI Enforcement with `RestoreLockedMode`

```xml
<PropertyGroup>
  <RestoreLockedMode Condition="'$(ContinuousIntegrationBuild)' == 'true'">true</RestoreLockedMode>
</PropertyGroup>
```

Or pass on the command line:

```shell
dotnet restore --locked-mode
```

In locked mode, restore fails if the lock file doesn't match the resolved dependency graph. This catches unintended dependency changes.

### NU1004: Lock File Inconsistent

```
NU1004: The packages lock file is inconsistent with the project dependencies.
```

**Causes:**
- A `<PackageReference>` was added, removed, or version-changed without regenerating the lock file
- Target framework was changed
- A transitive dependency published a new version (if using floating versions)

**Fix:** Regenerate the lock file locally and commit:

```shell
dotnet restore --force-evaluate
```

This re-evaluates all dependencies and updates `packages.lock.json`.

---

## Source Mapping Issues

### How `<packageSourceMapping>` Works

Source mapping restricts which sources can provide which packages, using namespace patterns:

```xml
<packageSourceMapping>
  <packageSource key="nuget.org">
    <package pattern="*" />
  </packageSource>
  <packageSource key="PrivateFeed">
    <package pattern="MyCompany.*" />
  </packageSource>
</packageSourceMapping>
```

With this config, `MyCompany.*` packages can come from either `nuget.org` or `PrivateFeed` (patterns are additive per package, not exclusive). But if you only map `MyCompany.*` to `PrivateFeed` and don't include a `*` wildcard on `nuget.org`, then no public packages will resolve.

### Debugging NU1100 with Source Mapping

```
NU1100: Unable to resolve 'SomePackage (>= 1.0.0)' for 'net8.0'.
```

When source mapping is active, NuGet only checks sources mapped to that package's namespace. If no pattern matches, the package is unresolvable.

**Diagnosis:**

```shell
dotnet restore --verbosity detailed
```

Look for `Source mapping` entries in the output to see which sources were consulted for each package.

**Fix:** Add a pattern for the missing package's namespace:

```xml
<packageSource key="nuget.org">
  <package pattern="SomePackage" />
  <!-- or use a wildcard: <package pattern="SomeCompany.*" /> -->
</packageSource>
```

### Pattern Guidelines

- `*` — matches all packages (use on your primary public feed)
- `MyCompany.*` — matches `MyCompany.Core`, `MyCompany.Utilities`, etc.
- `MyCompany.Utilities` — matches only that exact package ID
- Patterns are case-insensitive
- A package must match at least one pattern on at least one source, or restore fails

---

## Offline / Air-gapped Restore

### Using a Local Folder as a Source

```shell
dotnet restore --source "C:\local-packages"
```

Or configure permanently in `nuget.config`:

```xml
<packageSources>
  <clear />
  <add key="LocalFeed" value="C:\local-packages" />
</packageSources>
```

### Fallback Folders

Fallback folders are checked before downloading. Packages found there are used directly without copying to the global cache:

```xml
<fallbackPackageFolders>
  <add key="SharedCache" value="\\server\nuget-cache" />
</fallbackPackageFolders>
```

### Creating a Local Feed from Cache

NuGet caches downloaded packages in the global packages folder. Copy them to create an offline feed:

```shell
# Find cache location
dotnet nuget locals global-packages --list
# Output: global-packages: C:\Users\{user}\.nuget\packages

# Push packages to a local feed directory
dotnet nuget push "C:\Users\{user}\.nuget\packages\newtonsoft.json\13.0.3\*.nupkg" --source "C:\local-feed"
```

Or simply copy `.nupkg` files into a flat folder — NuGet supports folder-based feeds with no server required.

### NuGet Cache Locations

```shell
dotnet nuget locals all --list
```

Returns:

| Cache | Purpose |
|---|---|
| `http-cache` | Cached HTTP responses from feeds |
| `global-packages` | Extracted packages used during build |
| `temp` | Temporary staging during install |
| `plugins-cache` | Credential provider plugin cache |

Clear caches when troubleshooting stale package issues:

```shell
dotnet nuget locals all --clear        # clear everything
dotnet nuget locals http-cache --clear # clear only HTTP cache
```

---

## Diagnostic Steps

Follow this systematic troubleshooting flow when restore fails:

### 1. Verify Configured Sources

```shell
dotnet nuget list source
dotnet nuget list source --format detailed
```

Confirm all expected feeds are listed and enabled. Check for typos in URLs.

### 2. Check Cache State

```shell
dotnet nuget locals all --list
```

If you suspect stale cached data, clear the HTTP cache:

```shell
dotnet nuget locals http-cache --clear
```

### 3. Run Restore with Detailed Verbosity

```shell
dotnet restore --verbosity detailed
```

This shows:
- Which sources are queried for each package
- Source mapping decisions
- Authentication attempts and failures
- Version resolution logic

### 4. Check nuget.config Hierarchy

Look for `nuget.config` files at every level:

```shell
# From the project directory, check all ancestor directories
# Windows PowerShell:
Get-ChildItem -Path . -Filter nuget.config -Recurse
# Also check user-level:
Get-Item "$env:APPDATA\NuGet\NuGet.Config" -ErrorAction SilentlyContinue
```

A config file in an unexpected directory may add or `<clear />` sources.

### 5. Test Feed Connectivity

```shell
# Test if the feed is reachable
dotnet nuget list source --format detailed
# Try restoring a single known package
dotnet new classlib -o /tmp/test-restore && dotnet add /tmp/test-restore package Newtonsoft.Json
```

### 6. Verify Central Package Management (if used)

```shell
# Check if CPM is active
# Look for Directory.Packages.props in the directory hierarchy
Get-ChildItem -Path . -Filter Directory.Packages.props -Recurse
```

Confirm:
- `<ManagePackageVersions>true</ManagePackageVersions>` is set
- All referenced packages have a `<PackageVersion>` entry
- `.csproj` files don't specify `Version` on `<PackageReference>` (causes NU1008)

### 7. Binary Log Analysis

Generate a binary log during restore for deep analysis:

```shell
dotnet restore /bl:restore.binlog
```

Load the binlog and search for restore errors, feed interactions, and resolution decisions. Look for:
- `RestoreTask` entries showing which sources were queried
- Warning/error codes (NU1100, NU1605, NU1004, NU1008)
- Authentication handshake failures in HTTP traces
