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
    [switch]$ParseOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

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

# Parse tombstone backtrace frames, grouped by thread
# Returns an array of thread objects, each with Header and Frames properties
function Get-BacktraceFrames([string[]]$lines, [bool]$firstThreadOnly) {
    $threads = @()
    $currentFrames = @()
    $currentHeader = 'Crashing thread'
    $inBacktrace = $false

    foreach ($line in $lines) {
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
        if ($line -match '^\s*pid:\s*\d+,\s*tid:\s*(\d+),\s*name:\s*(.+)') {
            if ($currentFrames.Count -gt 0) {
                $threads += [PSCustomObject]@{ Header = $currentHeader; Frames = @($currentFrames) }
                if ($firstThreadOnly) { return @($threads) }
                $currentFrames = @()
                $inBacktrace = $false
            }
            # Keep 'Crashing thread' label for the first thread (before any separator)
            if ($threads.Count -gt 0 -or $currentHeader -ne 'Crashing thread') {
                $currentHeader = "Thread $($Matches[1]) ($($Matches[2].Trim()))"
            }
            continue
        }

        if ($line -match '^\s*backtrace:') {
            $inBacktrace = $true
            continue
        }

        if ($inBacktrace -and $line -match '^\s*#(\d+)\s+pc\s+(0x)?([0-9a-fA-F]+)\s+(\S+)(.*)$') {
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

# Symbolicate a single PC offset using llvm-symbolizer
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

$result = ($header + $output + $footer) -join "`n"

if ($OutputFile) {
    $result | Out-File -FilePath $OutputFile -Encoding utf8
    Write-Host "`nWrote symbolicated backtrace to $OutputFile" -ForegroundColor Green
}
else {
    Write-Host ""
    Write-Host $result
}
