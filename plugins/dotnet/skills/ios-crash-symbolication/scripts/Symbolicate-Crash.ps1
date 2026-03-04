<#
.SYNOPSIS
    Symbolicates .NET runtime frames in an iOS .ips crash log.

.DESCRIPTION
    Parses an iOS .ips crash log (JSON format, iOS 15+), extracts Mach-O UUIDs and
    frame addresses from the native backtrace, locates dSYM debug symbols from local
    SDK packs and NuGet cache, and runs atos to resolve each frame to function name,
    source file, and line number.

.PARAMETER CrashFile
    Path to the iOS .ips crash log file.

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

.EXAMPLE
    pwsh Symbolicate-Crash.ps1 -CrashFile MyApp-2026-02-25.ips

.EXAMPLE
    pwsh Symbolicate-Crash.ps1 -CrashFile MyApp.ips -DsymSearchPaths ./build/dSYMs -OutputFile symbolicated.txt
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
    [switch]$SkipVersionLookup
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# .NET runtime libraries on iOS (framework bundles omit .dylib extension)
$dotnetLibraries = @(
    'libcoreclr'
    'libmonosgen-2.0'
    'libSystem.Native'
    'libSystem.Globalization.Native'
    'libSystem.Security.Cryptography.Native.Apple'
    'libSystem.IO.Compression.Native'
    'libSystem.Net.Security.Native'
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

# Parse iOS .ips crash log (two-part JSON: line 1 = metadata, lines 2+ = crash body)
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
        $images += [PSCustomObject]@{
            Index     = $i
            Name      = $img.name
            Base      = [uint64]('0x' + ($img.base -replace '^0x', ''))
            Uuid      = Format-Uuid $img.uuid
            Arch      = if ($img.arch) { $img.arch } else { 'arm64' }
            IsDotNet  = (Test-DotNetLibrary $img.name)
        }
    }
    return $images
}

# Extract frames from a thread, computing absolute addresses from imageOffset + base
function Get-ThreadFrames($thread, $images) {
    $frames = @()
    if (-not $thread.frames) { return $frames }

    foreach ($f in $thread.frames) {
        $imgIdx = [int]$f.imageIndex
        $offset = [uint64]$f.imageOffset
        $img = $images | Where-Object { $_.Index -eq $imgIdx } | Select-Object -First 1

        if ($img) {
            $address = $img.Base + $offset
            $frames += [PSCustomObject]@{
                ImageIndex  = $imgIdx
                ImageName   = $img.Name
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
        $packPatterns = @(
            'packs/Microsoft.NETCore.App.Runtime.ios-arm64/*/runtimes/ios-arm64/native'
            'packs/Microsoft.NETCore.App.Runtime.Mono.ios-arm64/*/runtimes/ios-arm64/native'
        )
        foreach ($pattern in $packPatterns) {
            $searchDirs += @(Get-Item (Join-Path $dotnetRoot $pattern) -ErrorAction SilentlyContinue | Select-Object -ExpandProperty FullName)
        }
    }

    # 3. NuGet cache
    $nugetDir = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES }
                else { Join-Path $HOME '.nuget/packages' }
    $nugetPatterns = @(
        'microsoft.netcore.app.runtime.ios-arm64/*/runtimes/ios-arm64/native'
        'microsoft.netcore.app.runtime.mono.ios-arm64/*/runtimes/ios-arm64/native'
    )
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

# Symbolicate a batch of addresses for one image using atos
function Resolve-Frames([string]$dsymPath, [string]$arch, [uint64]$loadAddress, [uint64[]]$addresses, [string]$atosPath) {
    $loadHex = '0x{0:x}' -f $loadAddress
    $addrArgs = $addresses | ForEach-Object { '0x{0:x}' -f $_ }

    try {
        $output = & $atosPath -arch $arch -o $dsymPath -l $loadHex @addrArgs 2>$null
        if ($LASTEXITCODE -ne 0 -or -not $output) { return @() }

        $results = @()
        $lines = @($output)
        for ($i = 0; $i -lt [Math]::Min($lines.Count, $addresses.Count); $i++) {
            $line = $lines[$i].Trim()
            # atos returns "0xADDRESS" for unresolved frames
            if ($line -match '^0x[0-9a-fA-F]+$') {
                $results += $null
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
                $results += [PSCustomObject]@{
                    Function = $funcName
                    Source   = $source
                }
            }
        }
        return $results
    }
    catch {
        Write-Verbose "atos failed: $_"
        return @()
    }
}

# Try to identify the .NET runtime version by matching a UUID against locally-installed packs
function Find-RuntimeVersion([string]$uuid, [string]$libraryName) {
    $packNames = @()
    if ($libraryName -like '*monosgen*') {
        $packNames += 'Microsoft.NETCore.App.Runtime.Mono.ios-arm64'
    }
    elseif ($libraryName -like '*coreclr*') {
        $packNames += 'Microsoft.NETCore.App.Runtime.ios-arm64'
    }
    else {
        $packNames += 'Microsoft.NETCore.App.Runtime.Mono.ios-arm64'
        $packNames += 'Microsoft.NETCore.App.Runtime.ios-arm64'
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
            # Find the native library or dSYM under this version
            $nativeDir = Join-Path $versionDir.FullName "runtimes/ios-arm64/native"
            if (-not (Test-Path $nativeDir)) { continue }

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
    Write-Error "Unsupported crash log format. This script requires the .ips JSON format (iOS 15+). The file appears to be in the older text-based .crash format."
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
Write-Host "Crash log: $appName on iOS $osVersion" -ForegroundColor Cyan

# Check for Application Specific Information (often contains managed exception text)
$asi = $body.asi
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
        $label = if ($t -eq $faultingThreadIdx) { "Thread $t (Crashed)" }
                 elseif ($threadData.name) { "Thread $t ($($threadData.name))" }
                 else { "Thread $t" }

        $frames = Get-ThreadFrames $threadData $images
        if ($frames.Count -gt 0) {
            $threads += [PSCustomObject]@{ Header = $label; Frames = @($frames) }
        }
    }
}

# Also check lastExceptionBacktrace if present
if ($body.lastExceptionBacktrace -and -not $CrashingThreadOnly) {
    $lebtFrames = Get-ThreadFrames ([PSCustomObject]@{ frames = $body.lastExceptionBacktrace }) $images
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

# --- ParseOnly mode ---
if ($ParseOnly) {
    Write-Host "`n=== Crash Log Parse Report ===" -ForegroundColor Green
    Write-Host "App: $appName"
    Write-Host "OS: iOS $osVersion"
    Write-Host "Threads: $($threads.Count)"
    Write-Host "Total frames: $($allFrames.Count)"
    Write-Host ".NET frames: $($dotnetFrames.Count)"

    Write-Host "`n--- .NET Libraries ---"
    $libGroups = $dotnetFrames | Group-Object ImageName
    foreach ($g in $libGroups) {
        $sample = $g.Group | Select-Object -First 1
        Write-Host "  $($g.Name)  UUID: $($sample.ImageUuid)  Arch: $($sample.ImageArch)  Load: 0x$($sample.LoadAddress.ToString('x'))  ($($g.Count) frame(s))"
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

# Verify atos is available
$atosCmd = Get-Command $Atos -ErrorAction SilentlyContinue
if (-not $atosCmd) {
    # Try xcrun atos
    $xcrunAtos = & xcrun --find atos 2>$null
    if ($xcrunAtos -and (Test-Path $xcrunAtos)) {
        $Atos = $xcrunAtos
        $atosCmd = Get-Command $Atos -ErrorAction SilentlyContinue
    }
}
if (-not $atosCmd) {
    Write-Error "atos not found. Install Xcode Command Line Tools (xcode-select --install) or pass -Atos <path>."
    exit 1
}
Write-Verbose "Using atos: $($atosCmd.Source)"

# Search for dSYMs for each .NET library
$dsymMap = @{} # UUID -> dSYM DWARF path
$uniqueLibs = $dotnetFrames | Select-Object ImageName, ImageUuid -Unique

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
if ($foundCount -eq 0) {
    Write-Warning "Could not locate dSYM for any .NET library. Outputting unsymbolicated backtrace."
}
else {
    Write-Host "Found dSYMs for $foundCount/$($uniqueLibs.Count) .NET library/libraries" -ForegroundColor Green
}

# Version identification
$versionMap = @{} # UUID -> version info
if (-not $SkipVersionLookup) {
    Write-Host "Identifying .NET runtime version..." -ForegroundColor Cyan
    foreach ($lib in $uniqueLibs) {
        $versionInfo = Find-RuntimeVersion $lib.ImageUuid $lib.ImageName
        if ($versionInfo) {
            $versionMap[$lib.ImageUuid] = $versionInfo
            $commitShort = if ($versionInfo.Commit) { " (commit $($versionInfo.Commit.Substring(0, [Math]::Min(12, $versionInfo.Commit.Length))))" } else { '' }
            Write-Host "  $($lib.ImageName) → .NET $($versionInfo.Version)$commitShort" -ForegroundColor Green
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
