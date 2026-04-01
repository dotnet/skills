<#
.SYNOPSIS
    Merges new session data with existing data, purging sessions older than the
    retention window, and regenerates the manifest.json.

.DESCRIPTION
    Copies sessions from NewDir into OutputDir, then copies sessions from
    ExistingDir that are within the retention window. Sessions older than
    RetentionDays are discarded. A new manifest.json is generated covering
    the merged set.

.PARAMETER ExistingDir
    Path to existing session data from the dashboard-session-data branch.

.PARAMETER NewDir
    Path to new session data from the current pipeline run.

.PARAMETER OutputDir
    Path to write the merged output (can be the same as ExistingDir).

.PARAMETER RetentionDays
    Number of days to keep sessions. Sessions older than this are purged.
#>
param(
    [Parameter(Mandatory)][string]$ExistingDir,
    [Parameter(Mandatory)][string]$NewDir,
    [Parameter(Mandatory)][string]$OutputDir,
    [int]$RetentionDays = 7
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$cutoffDate = (Get-Date).ToUniversalTime().AddDays(-$RetentionDays)

# Use a temp directory if OutputDir == ExistingDir to avoid read/write conflicts
$resolvedOutputDir  = Resolve-Path $OutputDir  -ErrorAction SilentlyContinue
$resolvedExistingDir = Resolve-Path $ExistingDir -ErrorAction SilentlyContinue
$useTempDir = $false
if ($resolvedOutputDir -and $resolvedExistingDir) {
    $useTempDir = [IO.Path]::GetFullPath($resolvedOutputDir) -eq [IO.Path]::GetFullPath($resolvedExistingDir)
}
if ($useTempDir) {
    $workDir = Join-Path ([System.IO.Path]::GetTempPath()) "purge-sessions-$(Get-Random)"
    New-Item -ItemType Directory -Path $workDir -Force | Out-Null
} else {
    $workDir = $OutputDir
    New-Item -ItemType Directory -Path $workDir -Force | Out-Null
}

$sessionsWorkDir = Join-Path $workDir "sessions"
New-Item -ItemType Directory -Path $sessionsWorkDir -Force | Out-Null

$keptCount = 0
$purgedCount = 0

# Step 1: Copy all new sessions
$newSessionsDir = Join-Path $NewDir "sessions"
if (Test-Path $newSessionsDir) {
    $newFiles = Get-ChildItem -Path $newSessionsDir -Recurse -File -ErrorAction SilentlyContinue
    foreach ($file in $newFiles) {
        $relativePath = $file.FullName.Substring($newSessionsDir.Length).TrimStart([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
        $destPath = Join-Path $sessionsWorkDir $relativePath
        $destDir = Split-Path $destPath -Parent
        New-Item -ItemType Directory -Path $destDir -Force | Out-Null
        Copy-Item -Path $file.FullName -Destination $destPath -Force
        $keptCount++
    }
    Write-Host "Copied $keptCount new session file(s)"
}

# Step 2: Copy existing sessions within retention window
$existingSessionsDir = Join-Path $ExistingDir "sessions"
if (Test-Path $existingSessionsDir) {
    # For scheduled runs, the path structure is sessions/scheduled/<date>/...
    # For PR runs, the path structure is sessions/pr/<number>/...
    # We check dated directories for retention, PR dirs are always kept within window (use file mtime)

    $existingFiles = Get-ChildItem -Path $existingSessionsDir -Recurse -File -ErrorAction SilentlyContinue
    foreach ($file in $existingFiles) {
        $relativePath = $file.FullName.Substring($existingSessionsDir.Length).TrimStart([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
        $destPath = Join-Path $sessionsWorkDir $relativePath

        # Skip if already copied from new data
        if (Test-Path $destPath) { continue }

        # Check retention: try to extract date from path (scheduled/YYYY-MM-DD/...)
        $isExpired = $false
        if ($relativePath -match 'scheduled[/\\](\d{4}-\d{2}-\d{2})[/\\]') {
            $dirDate = [DateTime]::ParseExact($Matches[1], 'yyyy-MM-dd', $null)
            if ($dirDate.Date -lt $cutoffDate.Date) {
                $isExpired = $true
            }
        } elseif ($file.LastWriteTimeUtc -lt $cutoffDate) {
            # For PR sessions without date in path, use file modification time
            $isExpired = $true
        }

        if ($isExpired) {
            $purgedCount++
            continue
        }

        $destDir = Split-Path $destPath -Parent
        New-Item -ItemType Directory -Path $destDir -Force | Out-Null
        Copy-Item -Path $file.FullName -Destination $destPath -Force
        $keptCount++
    }
}

Write-Host "Retained $keptCount total session file(s), purged $purgedCount expired file(s)"

# Step 3: Read new manifest entries and merge with existing ones within retention
$allSessions = @()

# Load new manifest entries
$newManifestPath = Join-Path $NewDir "manifest.json"
if (Test-Path $newManifestPath) {
    $newManifest = Get-Content $newManifestPath -Raw | ConvertFrom-Json
    if ($newManifest.sessions) {
        $allSessions += @($newManifest.sessions)
    }
}

# Load existing manifest entries (filter by retention)
$existingManifestPath = Join-Path $ExistingDir "manifest.json"
if (Test-Path $existingManifestPath) {
    $existingManifest = Get-Content $existingManifestPath -Raw | ConvertFrom-Json
    if ($existingManifest.sessions) {
        # Precompute IDs from sessions already in $allSessions (new manifest)
        $existingIds = [System.Collections.Generic.HashSet[string]]::new(
            [string[]]@($allSessions | ForEach-Object { $_.id })
        )

        foreach ($session in $existingManifest.sessions) {
            # Skip if already in new manifest (by id)
            if ($existingIds.Contains($session.id)) { continue }

            # Check retention via mtime
            if ($session.mtime) {
                $sessionDate = [DateTimeOffset]::FromUnixTimeMilliseconds($session.mtime).UtcDateTime
                if ($sessionDate -lt $cutoffDate) { continue }
            }

            # Verify the session file still exists in the merged output
            $sessionFilePath = Join-Path $sessionsWorkDir ($session.url -replace '^sessions/', '')
            if (-not (Test-Path $sessionFilePath)) { continue }

            $allSessions += $session
        }
    }
}

# Step 4: Write merged manifest
# Derive generated timestamp from newest session mtime to avoid gratuitous commits
# when sessions haven't changed.
$newestMtime = ($allSessions | Where-Object { $_.mtime } | ForEach-Object { $_.mtime } | Measure-Object -Maximum).Maximum
$generatedTs = if ($newestMtime) {
    [DateTimeOffset]::FromUnixTimeMilliseconds($newestMtime).ToString('o')
} else {
    (Get-Date).ToUniversalTime().ToString('o')
}
$mergedManifest = @{
    generated = $generatedTs
    sessions  = @($allSessions)
}

$manifestOutPath = Join-Path $workDir "manifest.json"
$mergedManifest | ConvertTo-Json -Depth 10 | Out-File -FilePath $manifestOutPath -Encoding utf8

Write-Host "Merged manifest: $($allSessions.Count) session(s) written to $manifestOutPath"

# Step 5: If using temp dir, move results to OutputDir
if ($useTempDir) {
    # Clean existing data
    if (Test-Path (Join-Path $OutputDir "sessions")) {
        Remove-Item -Path (Join-Path $OutputDir "sessions") -Recurse -Force -ErrorAction SilentlyContinue
    }
    if (Test-Path (Join-Path $OutputDir "manifest.json")) {
        Remove-Item -Path (Join-Path $OutputDir "manifest.json") -Force -ErrorAction SilentlyContinue
    }

    # Copy merged data
    New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
    Copy-Item -Path (Join-Path $workDir "sessions") -Destination $OutputDir -Recurse -Force
    Copy-Item -Path (Join-Path $workDir "manifest.json") -Destination $OutputDir -Force

    # Cleanup temp
    Remove-Item -Path $workDir -Recurse -Force -ErrorAction SilentlyContinue
}
