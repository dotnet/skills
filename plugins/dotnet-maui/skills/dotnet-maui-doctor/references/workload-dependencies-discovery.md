# Project-aware workload dependency discovery

Use this only when exact package/version discovery is needed. Prefer the
effective installed metadata; live NuGet lookup is a fallback, not an obligatory
scan of the latest SDK.

## Select the effective context first

From the project directory, read the applicable `global.json`, target frameworks
and imported settings; inspect `dotnet --version`, `dotnet --info`,
`dotnet workload list`. For workload-set-capable SDKs also inspect
`dotnet workload --version` and `dotnet workload config --update-mode`.

- Respect SDK roll-forward/preview policy and `sdk.workloadVersion`.
- In loose-manifest mode, `<band>-manifests.<hash>` is **not** a downloadable
  workload-set version. Use the effective installed manifests shown by the CLI.
- An installed workload's manifest version can differ from the SDK version.
  Do not use advertising manifests as evidence of installed requirements.
- For a new setup with no pins, choose a supported SDK and compatible set
  deliberately. The release index can inform that choice, but must not override
  an existing project. Query only the relevant channel.

SDK feature bands round the SDK patch component down to a multiple of 100:
`10.0.205` → `10.0.200`, **not** `10.0.100`. Preview identities need their
documented package suffix; do not strip prerelease information.

## NuGet fallback

1. Resolve the selected workload set (if one exists), not the first/latest
   search result. Newer SDKs may expose
   `dotnet workload search version <selected-version>`; inspect `--help` before
   relying on JSON/options. This command is not available uniformly on old SDKs.
2. A workload-set NuGet package is `Microsoft.NET.Workloads.<set-feature-band>`.
   Its NuGet version is not the CLI set-version string. For example CLI
   `10.0.102` corresponds to NuGet `10.102.0` under
   `Microsoft.NET.Workloads.10.0.100`. Verify the exact correspondence in the
   package README/metadata. Do not generalize a string replacement to arbitrary
   prereleases or infer a set package's band from an Android manifest's band.
3. Download that exact package and extract
   `data/microsoft.net.workloads.workloadset.json`.
4. Each manifest entry is `"manifestVersion/manifestFeatureBand"`. Match workload
   IDs case-insensitively: published set keys can be `Microsoft.NET.Sdk.Android`.
   **Use both fields of that entry**, even if its band differs from the SDK or
   the set package. For Android `35.0.50/9.0.100`, the package is
   `Microsoft.NET.Sdk.Android.Manifest-9.0.100` version `35.0.50`, even when
   the selected SDK/set band is `10.0.100`.
5. Extract `data/WorkloadDependencies.json`, then the matching workload key.
   Read `jdk.version` / `recommendedVersion`, `androidsdk.packages`, and Apple
   `xcode.version` where present. Treat absent metadata as unavailable, not empty
   requirements. Use version-specific official docs/project-aware installation
   instead of silently substituting newer requirements.

### Bash: resolving an already-selected Android set entry

Prerequisites: `curl`, `jq`, `unzip`. Do not install prerequisites during diagnosis.
`WORKLOAD_SET_JSON` is the exact extracted set, `MANIFEST_ARCHIVE` is a
user-approved cache/output path (downloads write files).

```bash
entry=$(jq -er 'to_entries | map(select(.key | ascii_downcase ==
  "microsoft.net.sdk.android")) | select(length == 1) |
  .[0].value | select(type == "string")' "$WORKLOAD_SET_JSON") || exit 1
IFS=/ read -r manifest_version manifest_band extra <<< "$entry"
if [ -z "$manifest_version" ] || [ -z "$manifest_band" ] || [[ "$entry" == */*/* ]]; then
  printf '%s\n' "Invalid Android manifest entry: $entry" >&2
  exit 1
fi
package_id="microsoft.net.sdk.android.manifest-$manifest_band"
url="https://api.nuget.org/v3-flatcontainer/$package_id/$manifest_version/$package_id.$manifest_version.nupkg"
curl --fail --show-error --location "$url" -o "$MANIFEST_ARCHIVE" || exit 1
unzip -p "$MANIFEST_ARCHIVE" data/WorkloadDependencies.json |
  jq -e '."microsoft.net.sdk.android" // error("Android dependency key missing")' || exit 1
```

### PowerShell: the same entry, no SDK-band substitution

```powershell
$set = Get-Content -Raw $WorkloadSetJson | ConvertFrom-Json
$parts = $set.'microsoft.net.sdk.android' -split '/'
if ($parts.Count -ne 2 -or !$parts[0] -or !$parts[1]) {
    throw 'Invalid Android manifest entry'
}
$manifestVersion, $manifestBand = $parts
$packageId = "microsoft.net.sdk.android.manifest-$manifestBand".ToLowerInvariant()
$url = "https://api.nuget.org/v3-flatcontainer/$packageId/$manifestVersion/$packageId.$manifestVersion.nupkg"
Invoke-WebRequest $url -OutFile $ManifestArchive -ErrorAction Stop
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [System.IO.Compression.ZipFile]::OpenRead($ManifestArchive)
try {
    $entry = $zip.GetEntry('data/WorkloadDependencies.json')
    if (!$entry) { throw 'Dependency metadata unavailable' }
    $reader = [System.IO.StreamReader]::new($entry.Open())
    try { $deps = $reader.ReadToEnd() | ConvertFrom-Json }
    finally { $reader.Dispose() }
    if (!$deps.'microsoft.net.sdk.android') { throw 'Android dependency key missing' }
    $deps.'microsoft.net.sdk.android'
} finally { $zip.Dispose() }
```

NuGet flat-container URLs require lowercase package IDs and normalized lowercase
versions. Preserve prerelease identity when normalizing. Record selected SDK,
set/mode, manifest version/band and source so CI discovery can be reproduced.

## Interpret requirements, not just inventory

Range brackets are inclusive, parentheses exclusive:
`[17.0,22.0)` means at least 17.0, below 22.0. A recommended version is a
recommendation, not the sole valid value in that range.

Published `androidsdk.packages` can contain **objects**, not just strings:
`sdkPackage.id` is the package ID, `optional` can be the string `"true"`/`"false"`,
and IDs for system images can be host-keyed maps. Select required packages for
build-only CI; include optional items only for the requested task/host.
Do not invent `apiLevel`, `buildToolsVersion` or `cmdLineToolsVersion` fields if
they are absent: derive them from the actual package IDs when needed.

`androidsdk.packages` describes workload dependencies, not necessarily every
project-specific API/package. Inspect the evaluated compile API/Android TFM:
an explicit higher compile API requires its matching `platforms;android-XX`,
even if the workload baseline lists an older platform. Minimum/target runtime
SDK policy is not the compile API, and build-tools need not have the same version
number as the platform. Do not assume a Gradle-style fallback to any installed API. The
authorized `InstallAndroidDependencies` target handles project-aware installation.
For build-only CI avoid emulator images unless actually requested.

Sources:
- [Workload sets and package/version mapping](https://learn.microsoft.com/dotnet/core/tools/dotnet-workload-sets)
- [Workload search](https://learn.microsoft.com/dotnet/core/tools/dotnet-workload-search)
- [Android dependencies](https://learn.microsoft.com/dotnet/android/getting-started/installation/dependencies)
