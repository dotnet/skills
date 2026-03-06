# Workload Dependencies Discovery

This reference describes how to discover authoritative version requirements from NuGet APIs. All JDK, Android SDK, and Xcode requirements come from WorkloadDependencies.json - never hardcode versions.

## Workload Aliases

| Alias | Full ID |
|-------|---------|
| ios | microsoft.net.sdk.ios |
| android | microsoft.net.sdk.android |
| maccatalyst | microsoft.net.sdk.maccatalyst |
| macos | microsoft.net.sdk.macos |
| tvos | microsoft.net.sdk.tvos |
| maui | microsoft.net.sdk.maui |

---

## Discovery Process

### Step 1: Get Latest SDK Version

**Bash:**
```bash
curl -s "https://dotnetcli.blob.core.windows.net/dotnet/release-metadata/releases-index.json" | \
  jq '.["releases-index"][] | select(.["channel-version"]=="{MAJOR}.0")'
```

**PowerShell:**
```powershell
$releases = Invoke-RestMethod "https://dotnetcli.blob.core.windows.net/dotnet/release-metadata/releases-index.json"
$releases.'releases-index' | Where-Object { $_.'channel-version' -eq '{MAJOR}.0' }
```

Response fields:
| Field | Description |
|-------|-------------|
| `channel-version` | Major.minor (e.g., "10.0") |
| `latest-sdk` | Current stable SDK version |
| `support-phase` | "active", "maintenance", "eol" |

Extract `latest-sdk` and derive SDK band:
- `10.0.102` → band `10.0.100` (hundreds digit)
- `10.0.205` → band `10.0.200`

### Step 2: Find Workload Set Version

Use the `dotnet workload search version` command to discover the latest workload set version:

```bash
dotnet workload search version --format json --take 1
# Returns: [{"workloadVersion":"10.0.103"}]
```

```powershell
dotnet workload search version --format json --take 1 | ConvertFrom-Json
```

The returned `workloadVersion` is the CLI version to use with `--version` flag.

To convert this to the NuGet package version (needed for Steps 3-4):
- CLI `10.0.102` → NuGet `10.102.0` (remove middle `.0.`, combine)
- The NuGet package is: `Microsoft.NET.Workloads.{band}` where band = CLI version (e.g., `Microsoft.NET.Workloads.10.0.100`)

### Step 3: Download Workload Set Manifest

**Bash:**
```bash
curl -o workloadset.nupkg "https://api.nuget.org/v3-flatcontainer/microsoft.net.workloads.{band}/{version}/microsoft.net.workloads.{band}.{version}.nupkg"
unzip -p workloadset.nupkg data/microsoft.net.workloads.workloadset.json
```

**PowerShell:**
```powershell
Invoke-WebRequest "https://api.nuget.org/v3-flatcontainer/microsoft.net.workloads.{band}/{version}/microsoft.net.workloads.{band}.{version}.nupkg" -OutFile workloadset.nupkg
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [System.IO.Compression.ZipFile]::OpenRead("workloadset.nupkg")
$entry = $zip.Entries | Where-Object { $_.FullName -eq "data/microsoft.net.workloads.workloadset.json" }
$reader = [System.IO.StreamReader]::new($entry.Open())
$reader.ReadToEnd() | ConvertFrom-Json
$reader.Dispose(); $zip.Dispose()
```

Contents format: `"{workload_id}": "{manifestVersion}/{sdkBand}"`

Example:
```json
{
  "microsoft.net.sdk.android": "35.0.50/9.0.100",
  "microsoft.net.sdk.ios": "26.2.10191/10.0.100",
  "microsoft.net.sdk.maui": "10.0.10/10.0.100"
}
```

### Step 4: Download Workload Manifest

Build package id: `{WorkloadId}.Manifest-{sdkBand}`

Examples:
- `Microsoft.NET.Sdk.iOS.Manifest-10.0.100`
- `Microsoft.NET.Sdk.Android.Manifest-9.0.100`

**Bash:**
```bash
curl -o manifest.nupkg "https://api.nuget.org/v3-flatcontainer/{packageid}/{version}/{packageid}.{version}.nupkg"
unzip -p manifest.nupkg data/WorkloadDependencies.json
```

**PowerShell:**
```powershell
Invoke-WebRequest "https://api.nuget.org/v3-flatcontainer/{packageid}/{version}/{packageid}.{version}.nupkg" -OutFile manifest.nupkg
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [System.IO.Compression.ZipFile]::OpenRead("manifest.nupkg")
$entry = $zip.Entries | Where-Object { $_.FullName -eq "data/WorkloadDependencies.json" }
$reader = [System.IO.StreamReader]::new($entry.Open())
$reader.ReadToEnd() | ConvertFrom-Json
$reader.Dispose(); $zip.Dispose()
```

### Step 5: Parse WorkloadDependencies.json

**Android workload** (`microsoft.net.sdk.android`):
```json
{
  "microsoft.net.sdk.android": {
    "jdk": {
      "version": "[17.0,22.0)",
      "recommendedVersion": "21.0.8"
    },
    "androidsdk": {
      "packages": ["build-tools;35.0.0", "platform-tools", "platforms;android-35", "cmdline-tools;13.0"],
      "apiLevel": "35",
      "buildToolsVersion": "35.0.0",
      "cmdLineToolsVersion": "13.0"
    }
  }
}
```

**iOS workload** (`microsoft.net.sdk.ios`):
```json
{
  "microsoft.net.sdk.ios": {
    "xcode": {
      "version": "[26.2,)",
      "recommendedVersion": "26.2"
    },
    "sdk": {
      "version": "26.2"
    }
  }
}
```

### Version Range Notation

| Notation | Meaning |
|----------|---------|
| `[17.0,22.0)` | >= 17.0 AND < 22.0 |
| `[26.2,)` | >= 26.2 (no upper bound) |

Brackets: `[` = inclusive, `(` = exclusive

---

## Complete Example

**Goal**: Find requirements for .NET 10

### Bash

```bash
# Step 1: Get SDK info
curl -s "https://dotnetcli.blob.core.windows.net/dotnet/release-metadata/releases-index.json" | \
  jq '.["releases-index"][] | select(.["channel-version"]=="10.0") | .["latest-sdk"]'
# Result: "10.0.102" → band "10.0.100"

# Step 2: Get latest workload set version
dotnet workload search version --format json --take 1
# Result: [{"workloadVersion":"10.0.102"}]
# NuGet version: 10.102.0

# Step 3: Download workload set manifest
curl -so workloadset.nupkg "https://api.nuget.org/v3-flatcontainer/microsoft.net.workloads.10.0.100/10.102.0/microsoft.net.workloads.10.0.100.10.102.0.nupkg"
unzip -p workloadset.nupkg data/microsoft.net.workloads.workloadset.json | jq '."microsoft.net.sdk.android"'
# Result: "35.0.50/9.0.100"

# Step 4: Download Android manifest
curl -so android.nupkg "https://api.nuget.org/v3-flatcontainer/microsoft.net.sdk.android.manifest-9.0.100/35.0.50/microsoft.net.sdk.android.manifest-9.0.100.35.0.50.nupkg"
unzip -p android.nupkg data/WorkloadDependencies.json | jq '.["microsoft.net.sdk.android"]'
```

### PowerShell

```powershell
# Step 1: Get SDK info
$releases = Invoke-RestMethod "https://dotnetcli.blob.core.windows.net/dotnet/release-metadata/releases-index.json"
$sdkInfo = $releases.'releases-index' | Where-Object { $_.'channel-version' -eq '10.0' }
$latestSdk = $sdkInfo.'latest-sdk'
# Result: "10.0.102" → band "10.0.100"

# Step 2: Get latest workload set version
$workloadVersion = (dotnet workload search version --format json --take 1 | ConvertFrom-Json).workloadVersion
# Result: "10.0.102"
# NuGet version: 10.102.0

# Step 3: Download workload set manifest and extract
Invoke-WebRequest "https://api.nuget.org/v3-flatcontainer/microsoft.net.workloads.10.0.100/10.102.0/microsoft.net.workloads.10.0.100.10.102.0.nupkg" -OutFile workloadset.nupkg
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [System.IO.Compression.ZipFile]::OpenRead("workloadset.nupkg")
$entry = $zip.Entries | Where-Object { $_.FullName -eq "data/microsoft.net.workloads.workloadset.json" }
$reader = [System.IO.StreamReader]::new($entry.Open())
$manifest = $reader.ReadToEnd() | ConvertFrom-Json
$reader.Dispose(); $zip.Dispose()
$manifest.'microsoft.net.sdk.android'
# Result: "35.0.50/9.0.100"

# Step 4: Download Android manifest and extract WorkloadDependencies
Invoke-WebRequest "https://api.nuget.org/v3-flatcontainer/microsoft.net.sdk.android.manifest-9.0.100/35.0.50/microsoft.net.sdk.android.manifest-9.0.100.35.0.50.nupkg" -OutFile android.nupkg
$zip = [System.IO.Compression.ZipFile]::OpenRead("android.nupkg")
$entry = $zip.Entries | Where-Object { $_.FullName -eq "data/WorkloadDependencies.json" }
$reader = [System.IO.StreamReader]::new($entry.Open())
$reader.ReadToEnd() | ConvertFrom-Json
$reader.Dispose(); $zip.Dispose()
```

**Result**: Authoritative JDK, Android SDK, and Xcode requirements from live NuGet data.

---

## NuGet API Reference

| Operation | Endpoint |
|-----------|----------|
| .NET releases | `https://dotnetcli.blob.core.windows.net/dotnet/release-metadata/releases-index.json` |
| NuGet service index | `https://api.nuget.org/v3/index.json` |
| Download package | `https://api.nuget.org/v3-flatcontainer/{id}/{version}/{id}.{version}.nupkg` |

**Workload version discovery**: Use `dotnet workload search version --format json --take 1` instead of querying NuGet search APIs directly. The NuGet download URLs are still needed for Steps 3-4 (manifest extraction).

**Important**: Package IDs must be lowercase in download URLs.

---

## Best Practices

- **ALWAYS** fetch live data from NuGet APIs
- **NEVER** hardcode version requirements
- **ALWAYS** include SDK band with manifest versions
- Show exact URLs used for transparency
