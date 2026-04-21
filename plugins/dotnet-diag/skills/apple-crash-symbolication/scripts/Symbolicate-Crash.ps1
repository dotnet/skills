<#
.SYNOPSIS
    Symbolicates .NET runtime frames in an Apple platform .ips crash log.

.DESCRIPTION
    Parses an Apple platform .ips crash log (JSON format, iOS 15+ / macOS 12+), extracts
    Mach-O UUIDs and frame addresses from the native backtrace, locates dSYM debug symbols
    from local SDK packs and NuGet cache, downloads missing symbols from the Microsoft
    symbol server, and runs atos to resolve each frame to function name, source file, and
    line number. Supports iOS, tvOS, Mac Catalyst, and macOS.

.PARAMETER CrashFile
    Path to the .ips crash log file.

.PARAMETER Atos
    Path to atos. Defaults to 'atos' (assumes Xcode Command Line Tools are installed).

.PARAMETER DsymSearchPaths
    Additional directories to search for dSYM bundles. Searched before SDK packs and
    NuGet cache.

.PARAMETER OutputFile
    Optional path to write the symbolicated backtrace. If omitted, writes to stdout.

.PARAMETER CrashingThreadOnly
    Limit symbolication to the faulting thread only.

.PARAMETER ParseOnly
    Parse the crash log and report libraries, UUIDs, and frame addresses without
    symbolicating. Useful when atos or dSYMs are not available.

.PARAMETER SkipVersionLookup
    Skip .NET runtime version identification.

.PARAMETER SymbolCacheDir
    Directory to cache downloaded symbol files. Defaults to a temp directory.

.PARAMETER SymbolServerUrl
    Base URL for the symbol server. Defaults to Microsoft's public server.

.PARAMETER SkipSymbolDownload
    Skip downloading symbols from the symbol server. Only use locally-found dSYMs.

.EXAMPLE
    pwsh Symbolicate-Crash.ps1 -CrashFile MyApp-2026-02-25.ips

.EXAMPLE
    pwsh Symbolicate-Crash.ps1 -CrashFile MyApp.ips -DsymSearchPaths ./build/dSYMs -OutputFile symbolicated.txt

.EXAMPLE
    pwsh Symbolicate-Crash.ps1 -CrashFile MyApp.ips -SymbolCacheDir ./symbol-cache
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$CrashFile,

    [Parameter()]
    [string]$Atos = 'atos',

    [Parameter()]
    [string[]]$DsymSearchPaths = @(),

    [Parameter()]
    [string]$OutputFile,

    [Parameter()]
    [switch]$CrashingThreadOnly,

    [Parameter()]
    [switch]$ParseOnly,

    [Parameter()]
    [switch]$SkipVersionLookup,

    [Parameter()]
    [string]$SymbolCacheDir,

    [Parameter()]
    [string]$SymbolServerUrl = 'https://msdl.microsoft.com/download/symbols',

    [Parameter()]
    [switch]$SkipSymbolDownload
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# .NET runtime libraries on Apple platforms (framework bundles omit .dylib extension)
$dotnetLibraries = @(
    'libcoreclr'
    'libmonosgen-2.0'
    'libSystem.Native'
    'libSystem.Globalization.Native'
    'libSystem.Security.Cryptography.Native.Apple'
    'libSystem.IO.Compression.Native'
    'libSystem.Net.Security.Native'
)

# All Apple platform RIDs to search for dSYMs and runtime packs
$appleRids = @(
    'ios-arm64'
    'iossimulator-arm64'
    'iossimulator-x64'
    'tvos-arm64'
    'tvossimulator-arm64'
    'tvossimulator-x64'
    'maccatalyst-arm64'
    'maccatalyst-x64'
    'osx-arm64'
    'osx-x64'
)

function Test-DotNetLibrary([string]$imageName) {
    foreach ($lib in $dotnetLibraries) {
        if ($imageName -like "*$lib*") { return $true }
    }
    return $false
}

# Normalize UUID for comparison: lowercase, no dashes
function Format-Uuid([string]$uuid) {
    return ($uuid -replace '-', '').ToLowerInvariant()
}

# Parse .ips crash log (two-part JSON: line 1 = metadata, lines 2+ = crash body)
function Read-IpsCrashLog([string]$path) {
    $lines = Get-Content $path -Raw
    $splitIndex = $lines.IndexOf("`n")
    if ($splitIndex -lt 0) {
        Write-Error "Invalid .ips file: expected multi-line JSON format"
        exit 1
    }

    $metadataJson = $lines.Substring(0, $splitIndex)
    $bodyJson = $lines.Substring($splitIndex + 1)

    try {
        $metadata = $metadataJson | ConvertFrom-Json
    }
    catch {
        Write-Error "Failed to parse .ips metadata (line 1): $_"
        exit 1
    }

    try {
        # Some .ips files contain case-variant duplicate keys (e.g. vmRegionInfo
        # and vmregioninfo) which ConvertFrom-Json rejects. Rename the lowercase
        # duplicate so parsing succeeds while preserving the camelCase variant.
        if ($bodyJson -match '"vmRegionInfo"\s*:' -and $bodyJson -match '"vmregioninfo"\s*:') {
            $bodyJson = $bodyJson -creplace '"vmregioninfo"\s*:', '"_vmregioninfo_dup":'
        }
        $body = $bodyJson | ConvertFrom-Json
    }
    catch {
        Write-Error "Failed to parse .ips crash body (lines 2+): $_"
        exit 1
    }

    return @{ Metadata = $metadata; Body = $body }
}

# Build a lookup table of images from usedImages[]
function Get-ImageTable($crashBody) {
    $images = @()
    $usedImages = $crashBody.usedImages
    if (-not $usedImages) { return $images }

    for ($i = 0; $i -lt $usedImages.Count; $i++) {
        $img = $usedImages[$i]
        $imgPath = if ($img.PSObject.Properties['path'] -and $img.path) { $img.path } else { $null }
        $imgName = if ($img.PSObject.Properties['name'] -and $img.name) { $img.name }
                   elseif ($imgPath) { [System.IO.Path]::GetFileName($imgPath) }
                   else { $null }
        # Skip sentinel/empty entries (e.g. null UUID, no name or path)
        if (-not $imgName) { continue }
        $images += [PSCustomObject]@{
            Index     = $i
            Name      = $imgName
            Path      = $imgPath
            Base      = [uint64]$img.base
            Uuid      = Format-Uuid $img.uuid
            Arch      = if ($img.arch) { $img.arch } else { 'arm64' }
            IsDotNet  = (Test-DotNetLibrary $imgName)
        }
    }
    return $images
}

# Extract frames from a thread, computing absolute addresses from imageOffset + base
function Get-ThreadFrames($thread, $images) {
    $frames = @()
    if (-not $thread.frames) { return $frames }

    $imageMap = @{}
    foreach ($img in $images) { $imageMap[$img.Index] = $img }

    foreach ($f in $thread.frames) {
        $imgIdx = [int]$f.imageIndex
        $offset = [uint64]$f.imageOffset
        $img = $imageMap[$imgIdx]

        if ($img) {
            $address = $img.Base + $offset
            $frames += [PSCustomObject]@{
                ImageIndex  = $imgIdx
                ImageName   = $img.Name
                ImagePath   = $img.Path
                ImageUuid   = $img.Uuid
                ImageArch   = $img.Arch
                LoadAddress = $img.Base
                Offset      = $offset
                Address     = $address
                AddressHex  = '0x{0:x}' -f $address
                IsDotNet    = $img.IsDotNet
            }
        }
    }
    return $frames
}

# Search for a dSYM matching a given UUID
function Find-Dsym([string]$uuid, [string]$libraryName, [string[]]$extraPaths) {
    # Build list of search directories
    $searchDirs = @()

    # 1. User-provided paths
    $searchDirs += $extraPaths

    # 2. SDK packs
    $dotnetRoot = if ($env:DOTNET_ROOT) { $env:DOTNET_ROOT }
                  elseif (Test-Path (Join-Path $HOME '.dotnet')) { Join-Path $HOME '.dotnet' }
                  else { $null }
    if ($dotnetRoot) {
        $packPatterns = @()
        foreach ($rid in $script:appleRids) {
            $packPatterns += "packs/Microsoft.NETCore.App.Runtime.$rid/*/runtimes/$rid/native"
            $packPatterns += "packs/Microsoft.NETCore.App.Runtime.Mono.$rid/*/runtimes/$rid/native"
        }
        foreach ($pattern in $packPatterns) {
            $searchDirs += @(Get-Item (Join-Path $dotnetRoot $pattern) -ErrorAction SilentlyContinue | Select-Object -ExpandProperty FullName)
        }
    }

    # 3. NuGet cache
    $nugetDir = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES }
                else { Join-Path $HOME '.nuget/packages' }
    $nugetPatterns = @()
    foreach ($rid in $script:appleRids) {
        $nugetPatterns += "microsoft.netcore.app.runtime.$rid/*/runtimes/$rid/native"
        $nugetPatterns += "microsoft.netcore.app.runtime.mono.$rid/*/runtimes/$rid/native"
    }
    foreach ($pattern in $nugetPatterns) {
        $searchDirs += @(Get-Item (Join-Path $nugetDir $pattern) -ErrorAction SilentlyContinue | Select-Object -ExpandProperty FullName)
    }

    # Search each directory for .dSYM bundles or bare DWARF files
    foreach ($dir in $searchDirs) {
        if (-not $dir -or -not (Test-Path $dir)) { continue }

        # Look for dSYM bundles matching the library name
        $dsymBundles = Get-ChildItem $dir -Filter '*.dSYM' -Directory -Recurse -ErrorAction SilentlyContinue
        foreach ($bundle in $dsymBundles) {
            if ($bundle.Name -notlike "*$libraryName*") { continue }

            $dwarfDir = Join-Path $bundle.FullName 'Contents/Resources/DWARF'
            if (-not (Test-Path $dwarfDir)) { continue }

            $dwarfFiles = Get-ChildItem $dwarfDir -File -ErrorAction SilentlyContinue
            foreach ($dwarfFile in $dwarfFiles) {
                $dsymUuid = Get-DsymUuid $dwarfFile.FullName
                if ($dsymUuid -and (Format-Uuid $dsymUuid) -eq $uuid) {
                    return $dwarfFile.FullName
                }
            }
        }

        # Also check for bare dylib/framework files (may have embedded DWARF)
        $candidates = Get-ChildItem $dir -Filter "$libraryName*" -File -Recurse -ErrorAction SilentlyContinue
        foreach ($candidate in $candidates) {
            $dsymUuid = Get-DsymUuid $candidate.FullName
            if ($dsymUuid -and (Format-Uuid $dsymUuid) -eq $uuid) {
                return $candidate.FullName
            }
        }
    }

    return $null
}

# Get UUID from a dSYM or Mach-O binary using dwarfdump
function Get-DsymUuid([string]$path) {
    try {
        $output = & dwarfdump --uuid $path 2>$null
        if ($output -match 'UUID:\s*([0-9A-Fa-f-]+)') {
            return $Matches[1]
        }
    }
    catch {
        Write-Verbose "dwarfdump failed for $path`: $_"
    }
    return $null
}

# Download DWARF symbols from the Microsoft symbol server using a Mach-O UUID
function Get-DebugSymbols([string]$uuid, [string]$cacheDir, [string]$serverUrl) {
    $dwarfFile = Join-Path $cacheDir "$uuid.dwarf"

    if (Test-Path $dwarfFile) {
        Write-Verbose "Using cached symbols for UUID $uuid"
        return $dwarfFile
    }

    $url = "$serverUrl/_.dwarf/mach-uuid-sym-$uuid/_.dwarf"
    Write-Verbose "Downloading symbols from $url"

    $savedProgressPreference = $ProgressPreference
    try {
        $ProgressPreference = 'SilentlyContinue'
        Invoke-WebRequest -Uri $url -OutFile $dwarfFile -TimeoutSec 120

        # Verify the download is a Mach-O file (64-bit LE magic: CF FA ED FE)
        $stream = [System.IO.File]::OpenRead($dwarfFile)
        try {
            $header = [byte[]]::new(4)
            $bytesRead = $stream.Read($header, 0, 4)
        }
        finally {
            $stream.Close()
        }
        if ($bytesRead -ge 4 -and $header[0] -eq 0xCF -and $header[1] -eq 0xFA -and $header[2] -eq 0xED -and $header[3] -eq 0xFE) {
            $size = (Get-Item $dwarfFile).Length
            Write-Verbose "Downloaded $([math]::Round($size / 1MB, 1)) MB symbols for UUID $uuid"
            return $dwarfFile
        }
        else {
            Write-Warning "Downloaded file for UUID $uuid is not a valid Mach-O file (symbols may not be published)"
            Remove-Item $dwarfFile -ErrorAction SilentlyContinue
            return $null
        }
    }
    catch {
        Write-Warning "Failed to download symbols for UUID $uuid`: $_"
        Remove-Item $dwarfFile -ErrorAction SilentlyContinue
        return $null
    }
    finally {
        $ProgressPreference = $savedProgressPreference
    }
}

# Convert a raw .dwarf file into a .dSYM bundle that atos can consume
function Convert-DwarfToDsym([string]$dwarfFile, [string]$libraryName, [string]$uuid, [string]$cacheDir) {
    # Sanitize library name to prevent path traversal
    $safeName = [System.IO.Path]::GetFileName($libraryName)
    $dsymBundle = Join-Path $cacheDir "$safeName-$uuid.dSYM"
    $dwarfDir = Join-Path $dsymBundle 'Contents/Resources/DWARF'
    $targetFile = Join-Path $dwarfDir $safeName

    if (Test-Path $targetFile) {
        # Verify that the cached dSYM matches the requested UUID
        $cachedUuid = Get-DsymUuid $targetFile
        if ($cachedUuid -and (Format-Uuid $cachedUuid) -eq $uuid) {
            Write-Verbose "Using cached dSYM bundle for $libraryName"
            return $targetFile
        }

        Write-Warning "Cached dSYM bundle for $libraryName has incorrect or unreadable UUID; recreating"
        try {
            Remove-Item $dsymBundle -Recurse -Force -ErrorAction SilentlyContinue
        }
        catch {
            Write-Warning "Failed to remove invalid cached dSYM bundle for ${libraryName}: $_"
        }
    }

    try {
        New-Item -ItemType Directory -Path $dwarfDir -Force | Out-Null
        Copy-Item -Path $dwarfFile -Destination $targetFile -Force

        # Verify UUID is readable from the converted bundle
        $verifyUuid = Get-DsymUuid $targetFile
        if (-not $verifyUuid) {
            Write-Warning "Converted dSYM for $libraryName failed UUID verification"
            Remove-Item $dsymBundle -Recurse -Force -ErrorAction SilentlyContinue
            return $null
        }

        Write-Verbose "Created dSYM bundle: $dsymBundle"
        return $targetFile
    }
    catch {
        Write-Warning "Failed to create dSYM bundle for $libraryName`: $_"
        Remove-Item $dsymBundle -Recurse -Force -ErrorAction SilentlyContinue
        return $null
    }
}

# Symbolicate a batch of addresses for one image using atos
function Resolve-Frames([string]$dsymPath, [string]$arch, [uint64]$loadAddress, [uint64[]]$addresses, [string]$atosPath) {
    $loadHex = '0x{0:x}' -f $loadAddress
    $addrArgs = $addresses | ForEach-Object { '0x{0:x}' -f $_ }

    try {
        $output = & $atosPath -arch $arch -o $dsymPath -l $loadHex @addrArgs 2>$null
        if ($LASTEXITCODE -ne 0 -or -not $output) { return @() }

        $results = [System.Collections.Generic.List[object]]::new()
        $lines = @($output)
        for ($i = 0; $i -lt [Math]::Min($lines.Count, $addresses.Count); $i++) {
            $line = $lines[$i].Trim()
            # atos returns "0xADDRESS" for unresolved frames
            if ($line -match '^0x[0-9a-fA-F]+$') {
                $results.Add($null)
            }
            else {
                # Parse: "functionName (in libraryName) (sourcefile:line)"
                $funcName = $line
                $source = $null
                if ($line -match '^(.+?)\s+\(in\s+.+?\)\s+\((.+)\)$') {
                    $funcName = $Matches[1]
                    $source = $Matches[2]
                    # Strip CI build agent path prefixes
                    $source = $source -replace '^/__w/\d+/s/', ''
                }
                elseif ($line -match '^(.+?)\s+\(in\s+.+?\)$') {
                    $funcName = $Matches[1]
                }
                $results.Add([PSCustomObject]@{
                    Function = $funcName
                    Source   = $source
                })
            }
        }
        return $results.ToArray()
    }
    catch {
        Write-Verbose "atos failed: $_"
        return @()
    }
}

# Extract .NET runtime version from a crash log image path.
# macOS paths: /usr/local/share/dotnet/shared/Microsoft.NETCore.App/10.0.4/libcoreclr.dylib
# host/fxr:    /usr/local/share/dotnet/host/fxr/10.0.4/libhostfxr.dylib
# Mono:        /usr/local/share/dotnet/shared/Microsoft.NETCore.App/10.0.4/libmonosgen-2.0.dylib
function Get-RuntimeVersionFromPath([string]$imagePath) {
    if (-not $imagePath) { return $null }

    # Pattern: .../shared/Microsoft.NETCore.App/<version>/... or .../host/fxr/<version>/...
    # Version pattern: digits.digits.digits with optional pre-release suffix
    $patterns = @(
        '(?:shared|packs)/Microsoft\.NETCore\.App(?:\.Runtime\.[^/]+)?/([0-9]+\.[0-9]+\.[0-9]+[^/]*)/'
        'host/fxr/([0-9]+\.[0-9]+\.[0-9]+[^/]*)/'
    )

    foreach ($pat in $patterns) {
        if ($imagePath -match $pat) {
            return $Matches[1]
        }
    }
    return $null
}

# Detect the platform RID from a crash log image path.
# e.g., .../runtimes/osx-arm64/native/... → osx-arm64
function Get-RidFromPath([string]$imagePath) {
    if (-not $imagePath) { return $null }

    # NuGet layout: .../runtimes/<rid>/native/...
    if ($imagePath -match 'runtimes/([a-z]+-[a-z0-9]+)/native/') {
        return $Matches[1]
    }

    # macOS shared framework — infer from OS name and arch in the crash metadata
    if ($imagePath -match '/usr/local/share/dotnet/' -or $imagePath -match '\.dotnet/') {
        return $null  # caller infers from crash metadata
    }
    return $null
}

# Try to identify the .NET runtime version by matching a UUID against locally-installed packs
function Find-RuntimeVersion([string]$uuid, [string]$libraryName) {
    $packNames = @()
    foreach ($rid in $script:appleRids) {
        if ($libraryName -like '*monosgen*') {
            $packNames += "Microsoft.NETCore.App.Runtime.Mono.$rid"
        }
        elseif ($libraryName -like '*coreclr*') {
            $packNames += "Microsoft.NETCore.App.Runtime.$rid"
        }
        else {
            $packNames += "Microsoft.NETCore.App.Runtime.Mono.$rid"
            $packNames += "Microsoft.NETCore.App.Runtime.$rid"
        }
    }

    $searchRoots = @()

    # SDK packs
    $dotnetRoot = if ($env:DOTNET_ROOT) { $env:DOTNET_ROOT }
                  elseif (Test-Path (Join-Path $HOME '.dotnet')) { Join-Path $HOME '.dotnet' }
                  else { $null }
    if ($dotnetRoot) {
        foreach ($pn in $packNames) {
            $p = Join-Path $dotnetRoot "packs/$pn"
            if (Test-Path $p) { $searchRoots += $p }
        }
    }

    # NuGet cache
    $nugetDir = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES }
                else { Join-Path $HOME '.nuget/packages' }
    foreach ($pn in $packNames) {
        $p = Join-Path $nugetDir $pn.ToLowerInvariant()
        if (Test-Path $p) { $searchRoots += $p }
    }

    foreach ($root in $searchRoots) {
        foreach ($versionDir in (Get-ChildItem $root -Directory -ErrorAction SilentlyContinue)) {
            # Find the native directory — each pack has one RID under runtimes/
            $nativeDir = $null
            $runtimesDir = Join-Path $versionDir.FullName 'runtimes'
            if (Test-Path $runtimesDir) {
                foreach ($ridDir in (Get-ChildItem $runtimesDir -Directory -ErrorAction SilentlyContinue)) {
                    $nd = Join-Path $ridDir.FullName 'native'
                    if (Test-Path $nd) { $nativeDir = $nd; break }
                }
            }
            if (-not $nativeDir) { continue }

            # Check dSYM bundles first, then bare binaries
            $candidates = @()
            $dsymBundles = Get-ChildItem $nativeDir -Filter '*.dSYM' -Directory -ErrorAction SilentlyContinue
            foreach ($bundle in $dsymBundles) {
                if ($bundle.Name -like "*$libraryName*") {
                    $dwarfDir = Join-Path $bundle.FullName 'Contents/Resources/DWARF'
                    $candidates += @(Get-ChildItem $dwarfDir -File -ErrorAction SilentlyContinue)
                }
            }
            $candidates += @(Get-ChildItem $nativeDir -Filter "$libraryName*" -File -ErrorAction SilentlyContinue)

            foreach ($candidate in $candidates) {
                $localUuid = Get-DsymUuid $candidate.FullName
                if (-not $localUuid) { continue }
                if ((Format-Uuid $localUuid) -ne $uuid) { continue }

                # Match — extract commit hash from .nuspec
                $version = $versionDir.Name
                $commit = $null
                $nuspec = Get-ChildItem $versionDir.FullName -Filter '*.nuspec' -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
                if ($nuspec) {
                    try {
                        $xml = [xml](Get-Content $nuspec.FullName -Raw)
                        $repoNode = $xml.package.metadata.repository
                        if ($repoNode -and $repoNode.commit) {
                            $commit = $repoNode.commit
                        }
                    }
                    catch {
                        Write-Verbose "Could not parse nuspec for version $version`: $_"
                    }
                }

                return [PSCustomObject]@{
                    Version  = $version
                    Commit   = $commit
                    PackPath = $versionDir.FullName
                }
            }
        }
    }

    return $null
}

# --- Main ---

if (-not (Test-Path $CrashFile)) {
    Write-Error "Crash file not found: $CrashFile"
    exit 1
}

# Detect file format: .ips JSON (iOS 15+) vs older .crash text
$firstLine = (Get-Content $CrashFile -TotalCount 1).Trim()
if (-not $firstLine.StartsWith('{')) {
    Write-Error "Unsupported crash log format. This script requires the .ips JSON format (iOS 15+). The file is not JSON — it may be a legacy .crash text format, an Android tombstone, or another non-.ips format."
    exit 1
}

# Parse .ips crash log
$crash = Read-IpsCrashLog $CrashFile
$metadata = $crash.Metadata
$body = $crash.Body

$appName = if ($metadata.app_name) { $metadata.app_name }
           elseif ($metadata.name) { $metadata.name }
           else { 'Unknown' }
$osVersion = if ($metadata.os_version) { $metadata.os_version } else { 'Unknown' }
Write-Host "Crash log: $appName on $osVersion" -ForegroundColor Cyan

# Check for Application Specific Information (often contains managed exception text)
$asi = if ($body.PSObject.Properties['asi']) { $body.asi } else { $null }
if ($asi) {
    Write-Host "`nApplication Specific Information:" -ForegroundColor Yellow
    # asi can be a hashtable/object or array — flatten to string
    $asiText = ($asi | ConvertTo-Json -Depth 5 -Compress)
    if ($asiText.Length -gt 500) { $asiText = $asiText.Substring(0, 500) + '...' }
    Write-Host "  $asiText"
}

# Exception info
$exc = $body.exception
if ($exc) {
    $excType = if ($exc.type) { $exc.type } else { '?' }
    $excSignal = if ($exc.signal) { $exc.signal } else { '?' }
    Write-Host "Exception: $excType / $excSignal" -ForegroundColor Yellow
}

# Build image table
$images = Get-ImageTable $body
$dotnetImages = @($images | Where-Object { $_.IsDotNet })
Write-Host "Found $($images.Count) binary images ($($dotnetImages.Count) .NET libraries)" -ForegroundColor Cyan

if ($dotnetImages.Count -eq 0) {
    Write-Warning "No .NET runtime libraries found in usedImages. Nothing to symbolicate."
    Write-Host "`nBinary images:" -ForegroundColor Yellow
    foreach ($img in $images) {
        Write-Host "  $($img.Name)  UUID: $($img.Uuid)"
    }
    exit 0
}

# Extract thread frames
$faultingThreadIdx = if ($null -ne $body.faultingThread) { [int]$body.faultingThread } else { 0 }
$threads = @()

if ($body.threads) {
    for ($t = 0; $t -lt $body.threads.Count; $t++) {
        if ($CrashingThreadOnly -and $t -ne $faultingThreadIdx) { continue }

        $threadData = $body.threads[$t]
        $threadName = if ($threadData.PSObject.Properties['name']) { $threadData.name } else { $null }
        $label = if ($t -eq $faultingThreadIdx) { "Thread $t (Crashed)" }
                 elseif ($threadName) { "Thread $t ($threadName)" }
                 else { "Thread $t" }

        $frames = @(Get-ThreadFrames $threadData $images)
        if ($frames.Count -gt 0) {
            $threads += [PSCustomObject]@{ Header = $label; Frames = @($frames) }
        }
    }
}

# Also check lastExceptionBacktrace if present
if ($body.PSObject.Properties['lastExceptionBacktrace'] -and $body.lastExceptionBacktrace -and -not $CrashingThreadOnly) {
    $lebtFrames = @(Get-ThreadFrames ([PSCustomObject]@{ frames = $body.lastExceptionBacktrace }) $images)
    if ($lebtFrames.Count -gt 0) {
        $threads = @([PSCustomObject]@{ Header = 'Last Exception Backtrace'; Frames = @($lebtFrames) }) + $threads
    }
}

$allFrames = @($threads | ForEach-Object { $_.Frames } | ForEach-Object { $_ })
$dotnetFrames = @($allFrames | Where-Object { $_.IsDotNet })

Write-Host "Found $($allFrames.Count) frames across $($threads.Count) thread(s) ($($dotnetFrames.Count) from .NET libraries)" -ForegroundColor Cyan

if ($dotnetFrames.Count -eq 0) {
    Write-Warning "No .NET runtime frames found in the backtrace. Nothing to symbolicate."
    exit 0
}

# Verify atos is available (before ParseOnly check so we can auto-fallback)
if (-not $ParseOnly) {
    $atosCmd = Get-Command $Atos -ErrorAction SilentlyContinue
    if (-not $atosCmd) {
        # Try xcrun atos
        $xcrunCmd = Get-Command xcrun -ErrorAction SilentlyContinue
        $xcrunAtos = if ($xcrunCmd) { & xcrun --find atos 2>$null } else { $null }
        if ($xcrunAtos -and (Test-Path $xcrunAtos)) {
            $Atos = $xcrunAtos
            $atosCmd = Get-Command $Atos -ErrorAction SilentlyContinue
        }
    }
    if (-not $atosCmd) {
        Write-Warning "atos not found (requires macOS with Xcode Command Line Tools). Falling back to parse-only output."
        $ParseOnly = $true
    } else {
        Write-Verbose "Using atos: $($atosCmd.Source)"
    }
}

# --- ParseOnly mode ---
if ($ParseOnly) {
    Write-Host "`n=== Crash Log Parse Report ===" -ForegroundColor Green
    Write-Host "App: $appName"
    Write-Host "OS: $osVersion"
    Write-Host "Threads: $($threads.Count)"
    Write-Host "Total frames: $($allFrames.Count)"
    Write-Host ".NET frames: $($dotnetFrames.Count)"

    Write-Host "`n--- .NET Libraries (with frames to symbolicate) ---"
    $libGroups = $dotnetFrames | Group-Object ImageName
    foreach ($g in $libGroups) {
        $sample = $g.Group | Select-Object -First 1
        $pathVersion = Get-RuntimeVersionFromPath $sample.ImagePath
        $versionTag = if ($pathVersion) { "  .NET $pathVersion" } else { '' }
        Write-Host "  $($g.Name)  UUID: $($sample.ImageUuid)  Arch: $($sample.ImageArch)  Load: 0x$($sample.LoadAddress.ToString('x'))  ($($g.Count) frame(s))$versionTag"
    }

    Write-Host "`n--- Frames to Symbolicate ---"
    foreach ($t in $threads) {
        if ($threads.Count -gt 1 -and $t.Header) {
            Write-Host "  [$($t.Header)]"
        }
        $frameIdx = 0
        foreach ($f in $t.Frames) {
            if ($f.IsDotNet) {
                Write-Host "    #$frameIdx  $($f.ImageName)  $($f.AddressHex)  (offset 0x$($f.Offset.ToString('x')))"
            }
            $frameIdx++
        }
    }

    Write-Host "`n--- atos Commands ---"
    foreach ($g in $libGroups) {
        $sample = $g.Group | Select-Object -First 1
        $addrs = ($g.Group | ForEach-Object { $_.AddressHex }) -join ' '
        Write-Host "  atos -arch $($sample.ImageArch) -o <path-to-$($g.Name).dSYM/Contents/Resources/DWARF/$($g.Name)> -l 0x$($sample.LoadAddress.ToString('x')) $addrs"
    }

    Write-Host "`n=== End Parse Report ==="
    exit 0
}

# Set up symbol cache directory
if (-not $SymbolCacheDir) {
    $SymbolCacheDir = Join-Path ([System.IO.Path]::GetTempPath()) 'dotnet-crash-symbols'
}
if (-not (Test-Path $SymbolCacheDir)) {
    New-Item -ItemType Directory -Path $SymbolCacheDir -Force | Out-Null
}
Write-Verbose "Symbol cache: $SymbolCacheDir"

# Search for dSYMs for each .NET library
$dsymMap = @{} # UUID -> dSYM DWARF path
$uniqueLibs = $dotnetFrames | Sort-Object ImageName, ImagePath, ImageUuid -Unique

Write-Host "Searching for dSYM debug symbols..." -ForegroundColor Cyan
foreach ($lib in $uniqueLibs) {
    Write-Host "  $($lib.ImageName) (UUID: $($lib.ImageUuid))" -ForegroundColor DarkGray
    $dsymPath = Find-Dsym $lib.ImageUuid $lib.ImageName $DsymSearchPaths
    if ($dsymPath) {
        $dsymMap[$lib.ImageUuid] = $dsymPath
        Write-Host "    Found: $dsymPath" -ForegroundColor Green
    }
    else {
        Write-Warning "    dSYM not found for $($lib.ImageName). UUID: $($lib.ImageUuid)"
    }
}

$foundCount = $dsymMap.Count
if ($foundCount -gt 0) {
    Write-Host "Found local dSYMs for $foundCount/$($uniqueLibs.Count) .NET library/libraries" -ForegroundColor Green
}

# Download symbols from the Microsoft symbol server for any libraries still missing
$missingAfterLocal = @($uniqueLibs | Where-Object { -not $dsymMap.ContainsKey($_.ImageUuid) })
if ($missingAfterLocal.Count -gt 0 -and -not $SkipSymbolDownload) {
    Write-Host "Downloading symbols for $($missingAfterLocal.Count) missing library/libraries from symbol server..." -ForegroundColor Cyan
    foreach ($lib in $missingAfterLocal) {
        $uuid = Format-Uuid $lib.ImageUuid
        Write-Host "  $($lib.ImageName) (UUID: $uuid)" -ForegroundColor DarkGray
        $dwarfFile = Get-DebugSymbols $uuid $SymbolCacheDir $SymbolServerUrl
        if ($dwarfFile) {
            $dsymPath = Convert-DwarfToDsym $dwarfFile $lib.ImageName $uuid $SymbolCacheDir
            if ($dsymPath) {
                # Verify the UUID matches what the crash expects
                $downloadedUuid = Get-DsymUuid $dsymPath
                if ($downloadedUuid -and (Format-Uuid $downloadedUuid) -eq $uuid) {
                    $dsymMap[$lib.ImageUuid] = $dsymPath
                    $size = [math]::Round((Get-Item $dwarfFile).Length / 1MB, 1)
                    Write-Host "    ✅ Downloaded and converted ($($size) MB)" -ForegroundColor Green
                }
                else {
                    Write-Warning "    UUID mismatch for $($lib.ImageName) — expected $uuid, got $(Format-Uuid $downloadedUuid)"
                    # Remove the .dSYM bundle and cached .dwarf to prevent repeated mismatch
                    try {
                        # Walk up from DWARF file to .dSYM bundle root
                        # Path is: <cache>/<name>.dSYM/Contents/Resources/DWARF/<name>
                        $dsymBundlePath = $null
                        $candidate = Split-Path -LiteralPath $dsymPath -Parent
                        while ($candidate -and $candidate -ne (Split-Path -LiteralPath $candidate -Parent)) {
                            if ($candidate -like '*.dSYM') {
                                $dsymBundlePath = $candidate
                                break
                            }
                            $candidate = Split-Path -LiteralPath $candidate -Parent
                        }

                        if ($dsymBundlePath -and (Test-Path -LiteralPath $dsymBundlePath)) {
                            Remove-Item -LiteralPath $dsymBundlePath -Recurse -Force -ErrorAction Stop
                            Write-Host "    Removed cached dSYM bundle at '$dsymBundlePath' due to UUID mismatch; it will be re-fetched on next run." -ForegroundColor DarkYellow
                        }
                        elseif (Test-Path -LiteralPath $dsymPath) {
                            Remove-Item -LiteralPath $dsymPath -Recurse -Force -ErrorAction Stop
                            Write-Host "    Removed cached dSYM at '$dsymPath' due to UUID mismatch; it will be re-fetched on next run." -ForegroundColor DarkYellow
                        }

                        # Also remove the cached .dwarf file so it won't be reused on next run
                        if ($dwarfFile -and (Test-Path -LiteralPath $dwarfFile)) {
                            Remove-Item -LiteralPath $dwarfFile -Force -ErrorAction Stop
                            Write-Host "    Removed cached DWARF file at '$dwarfFile' due to UUID mismatch." -ForegroundColor DarkYellow
                        }
                    }
                    catch {
                        Write-Warning "    Failed to remove cached mismatched dSYM/DWARF artifacts: $($_.Exception.Message)"
                    }
                }
            }
        }
    }
    $downloadedCount = $dsymMap.Count - $foundCount
    if ($downloadedCount -gt 0) {
        Write-Host "Downloaded symbols for $downloadedCount/$($missingAfterLocal.Count) library/libraries" -ForegroundColor Green
    }
}

$totalFound = $dsymMap.Count
if ($totalFound -eq 0) {
    Write-Warning "Could not locate or download dSYM for any .NET library. Outputting unsymbolicated backtrace."
}

# Version identification — try extracting from crash log image paths first (instant,
# works without local installs), then fall back to UUID matching against local packs.
$versionMap = @{} # UUID -> version info
if (-not $SkipVersionLookup) {
    Write-Host "Identifying .NET runtime version..." -ForegroundColor Cyan
    foreach ($lib in $uniqueLibs) {
        # Fast path: extract version directly from the image path in the crash log
        $pathVersion = Get-RuntimeVersionFromPath $lib.ImagePath
        if ($pathVersion) {
            $versionMap[$lib.ImageUuid] = [PSCustomObject]@{
                Version  = $pathVersion
                Commit   = $null
                PackPath = $null
                Source   = 'crash-path'
            }
            Write-Host "  $($lib.ImageName) → .NET $pathVersion (from crash log path)" -ForegroundColor Green
            continue
        }

        # Slow path: UUID match against locally-installed packs/NuGet cache
        $versionInfo = Find-RuntimeVersion $lib.ImageUuid $lib.ImageName
        if ($versionInfo) {
            $versionMap[$lib.ImageUuid] = $versionInfo
            $commitShort = if ($versionInfo.Commit) { " (commit $($versionInfo.Commit.Substring(0, [Math]::Min(12, $versionInfo.Commit.Length))))" } else { '' }
            Write-Host "  $($lib.ImageName) → .NET $($versionInfo.Version)$commitShort" -ForegroundColor Green
        }
    }
}

# If dSYMs are still missing after download, emit manual acquisition guidance as fallback
$missingDsymLibs = @($uniqueLibs | Where-Object { -not $dsymMap.ContainsKey($_.ImageUuid) })
if ($missingDsymLibs.Count -gt 0 -and $versionMap.Count -gt 0) {
    # Determine the version and RID for acquisition
    $anyVersion = ($versionMap.Values | Select-Object -First 1).Version
    $crashRid = $null
    foreach ($lib in $missingDsymLibs) {
        $rid = Get-RidFromPath $lib.ImagePath
        if ($rid) { $crashRid = $rid; break }
    }
    # Infer RID from crash metadata if not found in paths
    if (-not $crashRid) {
        $osVer = if ($metadata.os_version) { $metadata.os_version } else { '' }
        $cpuType = if ($body.cpuType) { $body.cpuType } else { '' }
        $archSuffix = if ($cpuType -eq 'ARM-64' -or $cpuType -eq 'arm64' -or $cpuType -eq 'arm64e') { 'arm64' } else { 'x64' }

        # Detect simulator vs device from image paths (CoreSimulator present = simulator)
        $isSimulator = $false
        if ($body.usedImages) {
            foreach ($img in $body.usedImages) {
                if ($img.path -like '*CoreSimulator*') { $isSimulator = $true; break }
            }
        }

        if ($osVer -match 'macOS|Mac OS') { $crashRid = "osx-$archSuffix" }
        elseif ($osVer -match 'iPhone OS|iOS') {
            $crashRid = if ($isSimulator) { "iossimulator-$archSuffix" } else { "ios-arm64" }
        }
        elseif ($osVer -match 'tvOS') {
            $crashRid = if ($isSimulator) { "tvossimulator-$archSuffix" } else { "tvos-arm64" }
        }
        elseif ($osVer -match 'Mac Catalyst') { $crashRid = "maccatalyst-$archSuffix" }
    }

    $missingNames = ($missingDsymLibs | ForEach-Object { $_.ImageName }) -join ', '
    Write-Host "`n⚠️  Still missing dSYMs for: $missingNames" -ForegroundColor Yellow
    if ($SkipSymbolDownload) {
        Write-Host "   (Symbol download was skipped — re-run without -SkipSymbolDownload to try automatic download)" -ForegroundColor Yellow
    }
    Write-Host "   Version detected: .NET $anyVersion" -ForegroundColor Yellow
    if ($crashRid) {
        $isOsx = $crashRid -like 'osx-*'
        $runtimePackBase = "Microsoft.NETCore.App.Runtime.$crashRid"
        Write-Host "   Manual acquisition fallback:" -ForegroundColor Yellow
        if ($isOsx) {
            Write-Host "     dotnet-symbol: curl -Lo runtime.nupkg https://www.nuget.org/api/v2/package/$runtimePackBase/$anyVersion && unzip -q runtime.nupkg -d runtime-extracted && dotnet-symbol --symbols -o symbols-out runtime-extracted/runtimes/$crashRid/native/*.dylib" -ForegroundColor DarkYellow
            Write-Host "     Then convert .dwarf to .dSYM: mkdir -p symbols-out/<lib>.dSYM/Contents/Resources/DWARF && cp symbols-out/<lib>.dwarf symbols-out/<lib>.dSYM/Contents/Resources/DWARF/<lib>" -ForegroundColor DarkYellow
            Write-Host "     Re-run with: -DsymSearchPaths ./symbols-out" -ForegroundColor DarkYellow
        }
        else {
            # iOS/tvOS/MacCatalyst: dSYM bundles ship in the main runtime package
            Write-Host "     curl -Lo runtime.nupkg https://www.nuget.org/api/v2/package/$runtimePackBase/$anyVersion && unzip -q runtime.nupkg -d runtime-extracted" -ForegroundColor DarkYellow
            Write-Host "     Re-run with: -DsymSearchPaths ./runtime-extracted/runtimes/$crashRid/native" -ForegroundColor DarkYellow
        }
    }
}

# Symbolicate frames, grouped by image for batch atos calls
Write-Host "`nSymbolicating backtrace..." -ForegroundColor Cyan
$resolveCache = @{} # "uuid:address" -> resolved result

# Pre-resolve: batch atos calls per image
foreach ($lib in $uniqueLibs) {
    if (-not $dsymMap.ContainsKey($lib.ImageUuid)) { continue }

    $libFrames = @($dotnetFrames | Where-Object { $_.ImageUuid -eq $lib.ImageUuid })
    $uniqueAddresses = @($libFrames | Select-Object -ExpandProperty Address -Unique)
    $sample = $libFrames | Select-Object -First 1

    $results = Resolve-Frames $dsymMap[$lib.ImageUuid] $sample.ImageArch $sample.LoadAddress $uniqueAddresses $Atos

    for ($i = 0; $i -lt [Math]::Min($results.Count, $uniqueAddresses.Count); $i++) {
        if ($results[$i]) {
            $key = "$($lib.ImageUuid):$($uniqueAddresses[$i])"
            $resolveCache[$key] = $results[$i]
        }
    }
}

# Format output
$output = @()
$resolvedCount = 0

foreach ($thread in $threads) {
    if ($threads.Count -gt 1 -and $thread.Header) {
        $output += ""
        $output += "--- $($thread.Header) ---"
    }

    $frameIdx = 0
    foreach ($frame in $thread.Frames) {
        $key = "$($frame.ImageUuid):$($frame.Address)"
        if ($frame.IsDotNet -and $resolveCache.ContainsKey($key)) {
            $resolved = $resolveCache[$key]
            $resolvedCount++
            $sourceInfo = if ($resolved.Source) {
                # Keep last 2-3 path segments
                $parts = $resolved.Source -split '/'
                if ($parts.Count -gt 3) { ($parts[-3..-1]) -join '/' }
                elseif ($parts.Count -gt 1) { ($parts[-2..-1]) -join '/' }
                else { $resolved.Source }
            } else { '' }
            $line = if ($sourceInfo) {
                "#{0:D2}  {1,-36} {2,-48} ({3})" -f $frameIdx, $frame.ImageName, $resolved.Function, $sourceInfo
            } else {
                "#{0:D2}  {1,-36} {2}" -f $frameIdx, $frame.ImageName, $resolved.Function
            }
            $output += $line
        }
        else {
            # Unresolved — show address and image info
            $line = "#{0:D2}  {1,-36} {2}  (offset 0x{3:x})" -f $frameIdx, $frame.ImageName, $frame.AddressHex, $frame.Offset
            $output += $line
        }
        $frameIdx++
    }
}

# Build final output
$header = @(
    "--- Symbolicated Backtrace ---"
    ""
)
$footer = @(
    ""
    "--- $resolvedCount of $($dotnetFrames.Count) .NET frame(s) symbolicated ---"
)

if ($versionMap.Count -gt 0) {
    $footer += ""
    $footer += "--- .NET Runtime Version ---"
    foreach ($uuid in $versionMap.Keys) {
        $vi = $versionMap[$uuid]
        $lib = ($uniqueLibs | Where-Object { $_.ImageUuid -eq $uuid } | Select-Object -First 1).ImageName
        $commitInfo = if ($vi.Commit) { "  Commit: https://github.com/dotnet/runtime/commit/$($vi.Commit)" } else { '' }
        $footer += "$lib → .NET $($vi.Version)"
        if ($commitInfo) { $footer += $commitInfo }
    }
}

$result = ($header + $output + $footer) -join "`n"

if ($OutputFile) {
    $result | Out-File -FilePath $OutputFile -Encoding utf8
    Write-Host "`nWrote symbolicated backtrace to $OutputFile" -ForegroundColor Green
}
else {
    Write-Host ""
    Write-Host $result
}
