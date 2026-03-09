<#
.SYNOPSIS
    Symbolicates .NET runtime frames in an Android tombstone file.

.DESCRIPTION
    Parses an Android tombstone file, extracts backtrace frames from .NET runtime
    libraries (libmonosgen-2.0.so, libcoreclr.so, etc.), downloads debug symbols
    from the Microsoft symbol server using the ELF BuildId, and runs llvm-symbolizer
    to resolve each frame to function name, source file, and line number.

.PARAMETER TombstoneFile
    Path to the Android tombstone text file.

.PARAMETER LlvmSymbolizer
    Path to llvm-symbolizer. Defaults to 'llvm-symbolizer' (assumes it is on PATH).

.PARAMETER SymbolCacheDir
    Directory to cache downloaded debug symbol files. Defaults to a temp directory.

.PARAMETER OutputFile
    Optional path to write the symbolicated backtrace. If omitted, writes to stdout.

.PARAMETER SymbolServerUrl
    Base URL for the symbol server. Defaults to Microsoft's public server.

.EXAMPLE
    pwsh Symbolicate-Tombstone.ps1 -TombstoneFile tombstone_01.txt

.EXAMPLE
    pwsh Symbolicate-Tombstone.ps1 -TombstoneFile tombstone_01.txt -LlvmSymbolizer /path/to/llvm-symbolizer -OutputFile symbolicated.txt
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$TombstoneFile,

    [Parameter()]
    [string]$LlvmSymbolizer = 'llvm-symbolizer',

    [Parameter()]
    [string]$SymbolCacheDir,

    [Parameter()]
    [string]$OutputFile,

    [Parameter()]
    [string]$SymbolServerUrl = 'https://msdl.microsoft.com/download/symbols',

    [Parameter()]
    [switch]$CrashingThreadOnly,

    [Parameter()]
    [switch]$ParseOnly,

    [Parameter()]
    [switch]$SkipVersionLookup
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Required for NuGet package inspection in Find-RuntimeVersionOnline
Add-Type -AssemblyName System.IO.Compression -ErrorAction SilentlyContinue

# .NET runtime libraries we know how to symbolicate
$dotnetLibraries = @(
    'libmonosgen-2.0.so'
    'libcoreclr.so'
    'libSystem.Native.so'
    'libSystem.Globalization.Native.so'
    'libSystem.Security.Cryptography.Native.OpenSsl.so'
    'libSystem.IO.Compression.Native.so'
    'libSystem.Net.Security.Native.so'
)

function Test-DotNetLibrary([string]$libraryName) {
    foreach ($lib in $dotnetLibraries) {
        if ($libraryName -like "*$lib*") { return $true }
    }
    return $false
}

# Strip logcat prefixes from a line.
# Supports common logcat formats:
#   threadtime (default): "05-06 11:27:48.795  2931  2931 F DEBUG   : text"
#   time:                 "05-06 11:27:48.795 F/DEBUG( 2931): text"
#   brief:                "F/DEBUG( 2931): text"
# Returns the content after the prefix, or the original line if no prefix found.
function Remove-LogcatPrefix([string]$line) {
    # threadtime: MM-DD HH:MM:SS.mmm  PID  TID PRIO TAG  : content
    if ($line -match '^\d{2}-\d{2}\s+\d{2}:\d{2}:\d{2}\.\d+\s+\d+\s+\d+\s+[A-Z]\s+.*?:\s*(.*)$') {
        return $Matches[1]
    }
    # time: MM-DD HH:MM:SS.mmm PRIO/TAG(PID): content
    if ($line -match '^\d{2}-\d{2}\s+\d{2}:\d{2}:\d{2}\.\d+\s+[A-Z]/[^\(]+\(\s*\d+\):\s*(.*)$') {
        return $Matches[1]
    }
    # brief: PRIO/TAG(PID): content
    if ($line -match '^[A-Z]/[^\(]+\(\s*\d+\):\s*(.*)$') {
        return $Matches[1]
    }
    return $line
}

# Parse tombstone backtrace frames, grouped by thread
# Returns an array of thread objects, each with Header and Frames properties
function Get-BacktraceFrames([string[]]$lines, [bool]$firstThreadOnly) {
    $threads = @()
    $currentFrames = @()
    $currentHeader = 'Crashing thread'
    $inBacktrace = $false

    foreach ($rawLine in $lines) {
        # Strip logcat prefix if present (CI systems often capture tombstones via logcat)
        $line = Remove-LogcatPrefix $rawLine
        # Thread separator — save current thread and start a new one
        if ($line -match '^---\s+---\s+---') {
            if ($currentFrames.Count -gt 0) {
                $threads += [PSCustomObject]@{ Header = $currentHeader; Frames = @($currentFrames) }
                if ($firstThreadOnly) { return @($threads) }
                $currentFrames = @()
            }
            $inBacktrace = $false
            $currentHeader = $null
            continue
        }

        # Thread header line — flush any accumulated frames as a new thread
        # Standard tombstone format: "pid: NNN, tid: NNN, name: ..."
        # Debuggerd short-form: "name (native):tid=NNN systid=NNN"
        if ($line -match '^\s*pid:\s*\d+,\s*tid:\s*(\d+),\s*name:\s*(.+)' -or
            $line -match '^\s*(\S.*?)\s*\(native\)\s*:\s*tid=(\d+)') {
            if ($currentFrames.Count -gt 0) {
                $threads += [PSCustomObject]@{ Header = $currentHeader; Frames = @($currentFrames) }
                if ($firstThreadOnly) { return @($threads) }
                $currentFrames = @()
                $inBacktrace = $false
            }
            # Extract thread id and name (capture groups are in different order for the two patterns)
            if ($line -match '^\s*pid:\s*\d+,\s*tid:\s*(\d+),\s*name:\s*(.+)') {
                $threadId = $Matches[1]; $threadName = $Matches[2].Trim()
            } else {
                $line -match '^\s*(\S.*?)\s*\(native\)\s*:\s*tid=(\d+)' | Out-Null
                $threadId = $Matches[2]; $threadName = $Matches[1].Trim()
            }
            # Keep 'Crashing thread' label for the first thread (before any separator)
            if ($threads.Count -gt 0 -or $currentHeader -ne 'Crashing thread') {
                $currentHeader = "Thread $threadId ($threadName)"
            }
            continue
        }

        if ($line -match '^\s*backtrace:') {
            $inBacktrace = $true
            continue
        }

        if ($line -match '^\s*#(\d+)\s+pc\s+(0x)?([0-9a-fA-F]+)\s+(\S+)(.*)$') {
            $inBacktrace = $true
            $frameNum = $Matches[1]
            $pcOffset = '0x' + $Matches[3]
            $libraryPath = $Matches[4]
            $remainder = $Matches[5]

            $buildId = $null
            if ($remainder -match '\(BuildId:\s*([0-9a-fA-F]+)\)') {
                $buildId = $Matches[1].ToLowerInvariant()
            }

            $existingSymbol = $null
            if ($remainder -match '^\s*\(([^)]+)\)') {
                $sym = $Matches[1]
                if ($sym -notmatch '^BuildId:') {
                    $existingSymbol = $sym
                }
            }

            $libraryName = Split-Path $libraryPath -Leaf

            $currentFrames += [PSCustomObject]@{
                FrameNumber    = [int]$frameNum
                PcOffset       = $pcOffset
                LibraryPath    = $libraryPath
                LibraryName    = $libraryName
                BuildId        = $buildId
                ExistingSymbol = $existingSymbol
                IsDotNet       = (Test-DotNetLibrary $libraryName)
                OriginalLine   = $line.Trim()
            }
        }
        elseif ($inBacktrace -and $line -match '^\s*#\d+\s+pc\s+') {
            # Looks like a frame but didn't fully parse — warn about truncation/malformation
            Write-Warning "Skipping malformed frame line: $($line.Trim())"
        }
        elseif ($inBacktrace -and $line -notmatch '^\s*#\d+' -and $line.Trim() -ne '') {
            # End of this backtrace section
            $inBacktrace = $false
        }
    }

    # Save the last thread
    if ($currentFrames.Count -gt 0) {
        $threads += [PSCustomObject]@{ Header = $currentHeader; Frames = @($currentFrames) }
    }

    return @($threads)
}

# Download debug symbols from the Microsoft symbol server
function Get-DebugSymbols([string]$buildId, [string]$cacheDir, [string]$serverUrl) {
    $debugFile = Join-Path $cacheDir "$buildId.debug"

    if (Test-Path $debugFile) {
        Write-Verbose "Using cached symbols for BuildId $buildId"
        return $debugFile
    }

    $url = "$serverUrl/_.debug/elf-buildid-sym-$buildId/_.debug"
    Write-Verbose "Downloading symbols from $url"

    $savedProgressPreference = $ProgressPreference
    try {
        $ProgressPreference = 'SilentlyContinue'
        Invoke-WebRequest -Uri $url -OutFile $debugFile -UseBasicParsing -TimeoutSec 120

        # Verify the download is an ELF file (starts with 0x7f ELF)
        $stream = [System.IO.File]::OpenRead($debugFile)
        try {
            $header = [byte[]]::new(4)
            $bytesRead = $stream.Read($header, 0, 4)
        }
        finally {
            $stream.Close()
        }
        if ($bytesRead -ge 4 -and $header[0] -eq 0x7f -and $header[1] -eq 0x45 -and $header[2] -eq 0x4c -and $header[3] -eq 0x46) {
            $size = (Get-Item $debugFile).Length
            Write-Verbose "Downloaded $([math]::Round($size / 1MB, 1)) MB debug symbols for BuildId $buildId"
            return $debugFile
        }
        else {
            Write-Warning "Downloaded file for BuildId $buildId is not a valid ELF file (symbols may not be published)"
            Remove-Item $debugFile -ErrorAction SilentlyContinue
            return $null
        }
    }
    catch {
        Write-Warning "Failed to download symbols for BuildId $buildId`: $_"
        Remove-Item $debugFile -ErrorAction SilentlyContinue
        return $null
    }
    finally {
        $ProgressPreference = $savedProgressPreference
    }
}

# Try to identify the .NET runtime version and commit hash by matching a BuildId against
# locally-installed runtime packs (SDK workloads) and the NuGet package cache.
function Find-RuntimeVersion([string]$buildId, [string]$libraryName, [string]$llvmReadelf) {
    # Map library name to candidate runtime pack names
    $packNames = @()
    if ($libraryName -like '*monosgen*') {
        $packNames += 'Microsoft.NETCore.App.Runtime.Mono.android-{0}'
    }
    elseif ($libraryName -like '*coreclr*') {
        $packNames += 'Microsoft.NETCore.App.Runtime.android-{0}'
    }
    else {
        # libSystem.*.so can be in either pack
        $packNames += 'Microsoft.NETCore.App.Runtime.Mono.android-{0}'
        $packNames += 'Microsoft.NETCore.App.Runtime.android-{0}'
    }

    $rids = @('arm64', 'arm', 'x64', 'x86')
    $expandedPackNames = foreach ($tmpl in $packNames) { foreach ($rid in $rids) { $tmpl -f $rid } }

    # Build list of directories to search
    $searchRoots = @()

    # 1. SDK packs folder (workload installs — most likely for MAUI apps)
    $dotnetRoot = if ($env:DOTNET_ROOT) { $env:DOTNET_ROOT }
                  elseif (Test-Path (Join-Path $HOME '.dotnet')) { Join-Path $HOME '.dotnet' }
                  else { $null }
    if ($dotnetRoot) {
        foreach ($pn in $expandedPackNames) {
            $p = Join-Path $dotnetRoot "packs/$pn"
            if (Test-Path $p) { $searchRoots += $p }
        }
    }

    # 2. NuGet packages cache
    $nugetDir = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES }
                else { Join-Path $HOME '.nuget/packages' }
    foreach ($pn in $expandedPackNames) {
        $p = Join-Path $nugetDir $pn.ToLowerInvariant()
        if (Test-Path $p) { $searchRoots += $p }
    }

    foreach ($root in $searchRoots) {
        foreach ($versionDir in (Get-ChildItem $root -Directory -ErrorAction SilentlyContinue)) {
            # Find the native library under this version directory
            $soFile = Get-ChildItem $versionDir.FullName -Recurse -Filter $libraryName -ErrorAction SilentlyContinue | Select-Object -First 1
            if (-not $soFile) { continue }

            # Read BuildId from the local binary
            $localBuildId = $null
            if ($llvmReadelf) {
                $readelfOut = (& $llvmReadelf --notes $soFile.FullName 2>$null) -join "`n"
                if ($readelfOut -match 'Build ID:\s*([0-9a-fA-F]+)') {
                    $localBuildId = $Matches[1].ToLowerInvariant()
                }
            }
            if (-not $localBuildId) { continue }
            if ($localBuildId -ne $buildId) { continue }

            # Match found — extract commit hash from .nuspec
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

    return $null
}

# Fallback: query NuGet.org to identify the .NET version when the runtime pack is not installed locally.
# Downloads candidate nupkg files and checks the BuildId of the contained native library.
function Find-RuntimeVersionOnline([string]$buildId, [string]$libraryName, [string]$llvmReadelf, [string]$cacheDir) {
    # Determine which NuGet package IDs and RIDs to try
    $packNames = @()
    if ($libraryName -like '*monosgen*') {
        $packNames += 'Microsoft.NETCore.App.Runtime.Mono.android-{0}'
    }
    elseif ($libraryName -like '*coreclr*') {
        $packNames += 'Microsoft.NETCore.App.Runtime.android-{0}'
    }
    else {
        $packNames += 'Microsoft.NETCore.App.Runtime.Mono.android-{0}'
        $packNames += 'Microsoft.NETCore.App.Runtime.android-{0}'
    }

    # Try arm64 first (most common), then others
    $rids = @('arm64', 'arm', 'x64', 'x86')
    $packageIds = foreach ($tmpl in $packNames) { foreach ($rid in $rids) { ($tmpl -f $rid).ToLowerInvariant() } }

    $nugetBase = 'https://api.nuget.org/v3-flatcontainer'
    $savedProgressPreference = $ProgressPreference

    try {
        $ProgressPreference = 'SilentlyContinue'

        foreach ($pkgId in $packageIds) {
            # List available versions
            $indexUrl = "$nugetBase/$pkgId/index.json"
            try {
                $indexJson = Invoke-RestMethod -Uri $indexUrl -TimeoutSec 15 -UseBasicParsing
            }
            catch {
                Write-Verbose "Could not fetch version index for $pkgId`: $_"
                continue
            }

            # Only check stable release versions (no previews/RCs), newest first
            $versions = @($indexJson.versions | Where-Object { $_ -notmatch '-' }) | Sort-Object { [version]($_ -replace '[^0-9.]', '') } -Descending

            if ($versions.Count -eq 0) { continue }
            Write-Host "  Checking $($versions.Count) release versions of $pkgId on NuGet.org..." -ForegroundColor DarkGray

            foreach ($ver in $versions) {
                $nupkgUrl = "$nugetBase/$pkgId/$ver/$pkgId.$ver.nupkg"
                $nupkgFile = Join-Path $cacheDir "$pkgId.$ver.nupkg"
                $extractDir = Join-Path $cacheDir "$pkgId.$ver"

                try {
                    # Download the nupkg (zip file)
                    if (-not (Test-Path $nupkgFile)) {
                        Invoke-WebRequest -Uri $nupkgUrl -OutFile $nupkgFile -UseBasicParsing -TimeoutSec 120
                    }

                    # Extract only the native library
                    if (-not (Test-Path $extractDir)) {
                        New-Item -ItemType Directory -Path $extractDir -Force | Out-Null
                    }
                    $zip = [System.IO.Compression.ZipFile]::OpenRead($nupkgFile)
                    try {
                        $entry = $zip.Entries | Where-Object { $_.Name -eq $libraryName } | Select-Object -First 1
                        if (-not $entry) {
                            Remove-Item $nupkgFile -ErrorAction SilentlyContinue
                            continue
                        }
                        $soPath = Join-Path $extractDir $libraryName
                        if (-not (Test-Path $soPath)) {
                            [System.IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $soPath, $true)
                        }
                    }
                    finally {
                        $zip.Dispose()
                    }

                    # Check BuildId
                    $readelfOut = (& $llvmReadelf --notes $soPath 2>$null) -join "`n"
                    if ($readelfOut -match 'Build ID:\s*([0-9a-fA-F]+)') {
                        $localBuildId = $Matches[1].ToLowerInvariant()
                        if ($localBuildId -eq $buildId) {
                            # Match — extract commit from .nuspec inside the nupkg
                            $commit = $null
                            $zip2 = [System.IO.Compression.ZipFile]::OpenRead($nupkgFile)
                            try {
                                $nuspecEntry = $zip2.Entries | Where-Object { $_.FullName -like '*.nuspec' } | Select-Object -First 1
                                if ($nuspecEntry) {
                                    $reader = [System.IO.StreamReader]::new($nuspecEntry.Open())
                                    try {
                                        $xml = [xml]$reader.ReadToEnd()
                                        $repoNode = $xml.package.metadata.repository
                                        if ($repoNode -and $repoNode.commit) {
                                            $commit = $repoNode.commit
                                        }
                                    }
                                    finally { $reader.Dispose() }
                                }
                            }
                            finally { $zip2.Dispose() }

                            # Clean up downloaded packages (keep only the match)
                            Remove-Item $extractDir -Recurse -Force -ErrorAction SilentlyContinue
                            Remove-Item $nupkgFile -ErrorAction SilentlyContinue

                            return [PSCustomObject]@{
                                Version  = $ver
                                Commit   = $commit
                                PackPath = $null
                            }
                        }
                    }

                    # No match — clean up this version
                    Remove-Item $extractDir -Recurse -Force -ErrorAction SilentlyContinue
                    Remove-Item $nupkgFile -ErrorAction SilentlyContinue
                }
                catch {
                    Write-Verbose "Failed to check version $ver`: $_"
                    Remove-Item $nupkgFile -ErrorAction SilentlyContinue
                    Remove-Item $extractDir -Recurse -Force -ErrorAction SilentlyContinue
                }
            }
        }
    }
    finally {
        $ProgressPreference = $savedProgressPreference
    }

    return $null
}
function Resolve-Frame([string]$debugFile, [string]$pcOffset, [string]$symbolizerPath) {
    try {
        $output = & $symbolizerPath "--obj=$debugFile" -f -C $pcOffset 2>$null
        if ($LASTEXITCODE -ne 0 -or -not $output) { return $null }

        $lines = $output -split "`n" | Where-Object { $_.Trim() -ne '' }
        if ($lines.Count -ge 2) {
            $functionName = $lines[0].Trim()
            $sourceLocation = $lines[1].Trim()

            if ($functionName -eq '??' -and $sourceLocation -eq '??:0') { return $null }

            # Strip common CI build agent path prefixes (Azure DevOps hosted agents)
            $sourceLocation = $sourceLocation -replace '^/__w/\d+/s/', ''

            return [PSCustomObject]@{
                Function = $functionName
                Source   = $sourceLocation
            }
        }
    }
    catch {
        Write-Verbose "llvm-symbolizer failed for offset $pcOffset`: $_"
    }

    return $null
}

# --- Main ---

if (-not (Test-Path $TombstoneFile)) {
    Write-Error "Tombstone file not found: $TombstoneFile"
    exit 1
}

# Set up symbol cache directory
if (-not $SymbolCacheDir) {
    $SymbolCacheDir = Join-Path ([System.IO.Path]::GetTempPath()) 'dotnet-tombstone-symbols'
}
if (-not (Test-Path $SymbolCacheDir)) {
    New-Item -ItemType Directory -Path $SymbolCacheDir -Force | Out-Null
}
Write-Verbose "Symbol cache: $SymbolCacheDir"

# Read and parse tombstone
$tombstoneLines = Get-Content $TombstoneFile
$threads = Get-BacktraceFrames $tombstoneLines $CrashingThreadOnly

$allFrames = @($threads | ForEach-Object { $_.Frames } | ForEach-Object { $_ })
if ($allFrames.Count -eq 0) {
    Write-Error "No backtrace frames found in $TombstoneFile"
    exit 1
}

$dotnetFrames = @($allFrames | Where-Object { $_.IsDotNet })
Write-Host "Found $($allFrames.Count) backtrace frames across $($threads.Count) thread(s) ($($dotnetFrames.Count) from .NET libraries)" -ForegroundColor Cyan

if ($dotnetFrames.Count -eq 0) {
    Write-Warning "No .NET runtime frames found in the backtrace. Nothing to symbolicate."
    Write-Host "`nBacktrace frames found:" -ForegroundColor Yellow
    foreach ($t in $threads) {
        if ($t.Header) { Write-Host "  $($t.Header)" }
        foreach ($f in $t.Frames) { Write-Host "    $($f.OriginalLine)" }
    }
    exit 0
}

# --- ParseOnly mode: report what was found without downloading or symbolicating ---
if ($ParseOnly) {
    Write-Host "`n=== Tombstone Parse Report ===" -ForegroundColor Green
    Write-Host "Threads: $($threads.Count)"
    Write-Host "Total frames: $($allFrames.Count)"
    Write-Host ".NET frames: $($dotnetFrames.Count)"

    $uniqueBuildIds = @($dotnetFrames | Where-Object { $_.BuildId } | Select-Object -ExpandProperty BuildId -Unique)
    $framesWithoutBuildId = @($dotnetFrames | Where-Object { -not $_.BuildId })

    Write-Host "`n--- .NET Libraries ---"
    $libGroups = $dotnetFrames | Group-Object LibraryName
    foreach ($g in $libGroups) {
        $bidFrame = $g.Group | Where-Object { $_.BuildId } | Select-Object -First 1
        if ($bidFrame) {
            Write-Host "  $($g.Name)  BuildId: $($bidFrame.BuildId)  ($($g.Count) frame(s))"
            Write-Host "    Symbol URL: $SymbolServerUrl/_.debug/elf-buildid-sym-$($bidFrame.BuildId)/_.debug"
        }
        else {
            Write-Host "  $($g.Name)  (no BuildId)  ($($g.Count) frame(s))"
        }
    }

    if ($framesWithoutBuildId.Count -gt 0) {
        Write-Host "`n--- Frames Without BuildId ---"
        foreach ($f in $framesWithoutBuildId) {
            Write-Host "  #$($f.FrameNumber)  $($f.LibraryName)  pc $($f.PcOffset)"
        }
    }

    Write-Host "`n--- Frames to Symbolicate ---"
    foreach ($t in $threads) {
        if ($threads.Count -gt 1 -and $t.Header) {
            Write-Host "  [$($t.Header)]"
        }
        foreach ($f in $t.Frames) {
            if ($f.IsDotNet) {
                $marker = if ($f.BuildId) { "✓" } else { "✗ (no BuildId)" }
                Write-Host "    #$($f.FrameNumber)  $($f.LibraryName)  pc $($f.PcOffset)  $marker"
            }
        }
    }

    Write-Host "`n=== End Parse Report ==="
    exit 0
}

# Collect unique BuildIds and download symbols
$buildIdMap = @{} # BuildId -> debug file path
$uniqueBuildIds = @($dotnetFrames | Where-Object { $_.BuildId } | Select-Object -ExpandProperty BuildId -Unique)
$framesWithoutBuildId = @($dotnetFrames | Where-Object { -not $_.BuildId })

if ($framesWithoutBuildId.Count -gt 0) {
    Write-Warning "$($framesWithoutBuildId.Count) .NET frame(s) have no BuildId metadata — these cannot be symbolicated via the symbol server."
}

if ($uniqueBuildIds.Count -eq 0) {
    Write-Warning "No BuildIds found on any .NET frame. Outputting unsymbolicated backtrace."
}
else {
    # Verify llvm-symbolizer is available (only needed when we have symbols to resolve)
    $symbolizerCmd = Get-Command $LlvmSymbolizer -ErrorAction SilentlyContinue

    # If not on PATH, try common NDK locations
    if (-not $symbolizerCmd) {
        $ndkPaths = @()
        if ($env:ANDROID_NDK_ROOT) {
            $ndkPaths += Join-Path $env:ANDROID_NDK_ROOT 'toolchains/llvm/prebuilt/*/bin/llvm-symbolizer'
        }
        if ($env:ANDROID_HOME) {
            $ndkPaths += Join-Path $env:ANDROID_HOME 'ndk/*/toolchains/llvm/prebuilt/*/bin/llvm-symbolizer'
        }
        foreach ($pattern in $ndkPaths) {
            $found = Get-Item $pattern -ErrorAction SilentlyContinue | Select-Object -First 1
            if ($found) {
                $LlvmSymbolizer = $found.FullName
                $symbolizerCmd = Get-Command $LlvmSymbolizer -ErrorAction SilentlyContinue
                break
            }
        }
    }

    if (-not $symbolizerCmd) {
        Write-Error "llvm-symbolizer not found. Set ANDROID_NDK_ROOT, add it to PATH, or pass -LlvmSymbolizer."
        exit 1
    }
    Write-Verbose "Using llvm-symbolizer: $($symbolizerCmd.Source)"

    Write-Host "Downloading debug symbols for $($uniqueBuildIds.Count) unique .NET BuildId(s)..." -ForegroundColor Cyan
    foreach ($bid in $uniqueBuildIds) {
        $lib = ($dotnetFrames | Where-Object { $_.BuildId -eq $bid } | Select-Object -First 1).LibraryName
        Write-Host "  $lib (BuildId: $bid)" -ForegroundColor DarkGray
        $debugFile = Get-DebugSymbols $bid $SymbolCacheDir $SymbolServerUrl
        if ($debugFile) {
            $buildIdMap[$bid] = $debugFile
        }
    }

    $downloadedCount = $buildIdMap.Count
    if ($downloadedCount -eq 0) {
        Write-Warning "Could not download debug symbols for any .NET library. Outputting unsymbolicated backtrace."
    }
    else {
        Write-Host "Successfully downloaded symbols for $downloadedCount/$($uniqueBuildIds.Count) BuildId(s)" -ForegroundColor Green
    }

    # Try to identify .NET runtime version from locally-installed packs
    $llvmReadelf = Join-Path (Split-Path $symbolizerCmd.Source) 'llvm-readelf'
    if (-not (Test-Path $llvmReadelf)) {
        $llvmReadelf = (Get-Command 'llvm-readelf' -ErrorAction SilentlyContinue)?.Source
    }
    $versionMap = @{} # BuildId -> version info
    if (-not $SkipVersionLookup -and $llvmReadelf) {
        Write-Host "Identifying .NET runtime version..." -ForegroundColor Cyan
        foreach ($bid in $uniqueBuildIds) {
            $lib = ($dotnetFrames | Where-Object { $_.BuildId -eq $bid } | Select-Object -First 1).LibraryName
            $versionInfo = Find-RuntimeVersion $bid $lib $llvmReadelf
            if (-not $versionInfo) {
                # Fallback: search NuGet.org
                Write-Host "  Not found locally — searching NuGet.org..." -ForegroundColor DarkGray
                $versionInfo = Find-RuntimeVersionOnline $bid $lib $llvmReadelf $SymbolCacheDir
            }
            if ($versionInfo) {
                $versionMap[$bid] = $versionInfo
                $commitShort = if ($versionInfo.Commit) { " (commit $($versionInfo.Commit.Substring(0, [Math]::Min(12, $versionInfo.Commit.Length))))" } else { '' }
                Write-Host "  $lib → .NET $($versionInfo.Version)$commitShort" -ForegroundColor Green
            }
        }
    }
}

# Symbolicate each frame, grouped by thread
Write-Host "`nSymbolicating backtrace..." -ForegroundColor Cyan
$output = @()
$resolvedCount = 0

foreach ($thread in $threads) {
    if ($threads.Count -gt 1 -and $thread.Header) {
        $output += ""
        $output += "--- $($thread.Header) ---"
    }

    foreach ($frame in $thread.Frames) {
        if ($frame.IsDotNet -and $frame.BuildId -and $buildIdMap.ContainsKey($frame.BuildId)) {
            $resolved = Resolve-Frame $buildIdMap[$frame.BuildId] $frame.PcOffset $LlvmSymbolizer
            if ($resolved) {
                $resolvedCount++
                # Keep last 2-3 path segments for context (e.g., "mono/metadata/icall.c:6244")
                $sourceParts = $resolved.Source -split '/'
                if ($sourceParts.Count -gt 3) {
                    $sourceShort = ($sourceParts[-3..-1]) -join '/'
                }
                elseif ($sourceParts.Count -gt 1) {
                    $sourceShort = ($sourceParts[-2..-1]) -join '/'
                }
                else {
                    $sourceShort = $resolved.Source
                }
                $line = "#{0:D2}  {1,-24} {2,-48} ({3})" -f $frame.FrameNumber, $frame.LibraryName, $resolved.Function, $sourceShort
                $output += $line
                continue
            }
        }

        # Fallback: preserve original detail for triage (BuildId, path, symbol)
        if ($frame.ExistingSymbol) {
            $line = "#{0:D2}  {1,-24} {2}" -f $frame.FrameNumber, $frame.LibraryName, $frame.ExistingSymbol
        }
        elseif ($frame.BuildId) {
            $line = "#{0:D2}  {1,-24} pc {2}  (BuildId: {3})" -f $frame.FrameNumber, $frame.LibraryName, $frame.PcOffset, $frame.BuildId
        }
        else {
            $line = "#{0:D2}  {1,-24} pc {2}  {3}" -f $frame.FrameNumber, $frame.LibraryName, $frame.PcOffset, $frame.LibraryPath
        }
        $output += $line
    }
}

# Output results
$header = @(
    "--- Symbolicated Backtrace ---"
    ""
)
$footer = @(
    ""
    "--- $resolvedCount of $($dotnetFrames.Count) .NET frame(s) symbolicated ---"
)

# Append runtime version info if identified
if ($versionMap.Count -gt 0) {
    $footer += ""
    $footer += "--- .NET Runtime Version ---"
    foreach ($bid in $versionMap.Keys) {
        $vi = $versionMap[$bid]
        $lib = ($dotnetFrames | Where-Object { $_.BuildId -eq $bid } | Select-Object -First 1).LibraryName
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
