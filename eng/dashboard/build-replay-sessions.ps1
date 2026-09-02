<#
.SYNOPSIS
    Flattens native SDK session JSONL files from Vally experiment output into a
    structured directory and generates an AGENTVIZ-compatible manifest.json.

.DESCRIPTION
    Scans Vally's `executor-session-logs` trees inside each evaluation artifact.
    Each leaf directory holds a `metadata.json` (evalFilePath, variant,
    stimulusName, trialIndex, status, ...) alongside the captured `events.jsonl`.
    This script derives skill/scenario/role from that metadata, copies the
    events file with a meaningful name, and generates a manifest.json suitable
    for AGENTVIZ static manifest mode.

.PARAMETER ResultsDir
    Path to downloaded artifacts (contains vally-results-* directories, each
    holding a Vally run directory with an executor-session-logs tree).

.PARAMETER OutputDir
    Output directory for flattened sessions and manifest.

.PARAMETER Source
    "scheduled" or "pr" -- determines directory structure and tags.

.PARAMETER PrNumber
    PR number (when Source=pr). Ignored for scheduled runs.
#>
param(
    [Parameter(Mandatory)][string]$ResultsDir,
    [Parameter(Mandatory)][string]$OutputDir,
    [string]$Source = "scheduled",
    [int]$PrNumber = 0
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Variant -> role tag. Vally variants are declared in dotnet-skills.experiment.yaml:
#   baseline (skill-free control), skilled (one skill under test), plugin (whole plugin).
$roleMap = @{
    'baseline' = 'baseline'
    'skilled'  = 'isolated'
    'plugin'   = 'plugin'
}

# tests/<plugin>/<skill>/eval.yaml -> plugin=<plugin>, skill=<skill>
# Mirrors eng/vally-adapter/adapt.mjs evalIdentity().
function Get-EvalIdentity {
    param([string]$EvalFile)
    $norm = $EvalFile -replace '\\', '/'
    $dir = [System.IO.Path]::GetDirectoryName($norm) -replace '\\', '/'
    if (-not $dir) { return $null }
    $skill = [System.IO.Path]::GetFileName($dir)
    $parent = [System.IO.Path]::GetDirectoryName($dir) -replace '\\', '/'
    $plugin = if ($parent) { [System.IO.Path]::GetFileName($parent) } else { '' }
    if (-not $skill -or -not $plugin) { return $null }
    return [pscustomobject]@{ Skill = $skill; Plugin = $plugin }
}

# Determine date and subdirectory (use UTC for consistency with purge retention)
$dateTag = (Get-Date).ToUniversalTime().ToString('yyyy-MM-dd')
if ($Source -eq 'pr') {
    $subDir = "pr/$PrNumber"
} else {
    $subDir = "scheduled/$dateTag"
}

$sessionsOutDir = Join-Path $OutputDir "sessions/$subDir"
New-Item -ItemType Directory -Path $sessionsOutDir -Force | Out-Null

$manifestSessions = @()

Write-Host "Scanning $ResultsDir for Vally session logs..."
$allDirs = @(Get-ChildItem -Path $ResultsDir -Directory -ErrorAction SilentlyContinue)
Write-Host "  Total directories in ResultsDir: $($allDirs.Count)"
if ($allDirs.Count -gt 0) {
    Write-Host "  Directory names: $($allDirs.Name -join ', ')"
}

# Vally writes session logs under <runDir>/executor-session-logs/.../metadata.json
# with a sibling events.jsonl. Discover every metadata.json under any
# executor-session-logs tree, regardless of how deeply the artifacts are nested.
$metadataFiles = @(
    Get-ChildItem -Path $ResultsDir -Recurse -Filter 'metadata.json' -File -ErrorAction SilentlyContinue |
        Where-Object { ($_.FullName -replace '\\', '/') -match '/executor-session-logs/' }
)

Write-Host "  Found $($metadataFiles.Count) session metadata file(s)"

if ($metadataFiles.Count -eq 0) {
    Write-Warning "No executor-session-logs/metadata.json found in $ResultsDir"
    $manifest = @{ generated = (Get-Date -Format 'o'); sessions = @() }
    $manifestPath = Join-Path $OutputDir "manifest.json"
    $manifest | ConvertTo-Json -Depth 10 | Out-File -FilePath $manifestPath -Encoding utf8
    Write-Host "Wrote empty manifest to $manifestPath"
    exit 0
}

$usedNames = @{}

foreach ($metaFile in $metadataFiles) {
    try {
        $meta = Get-Content -Path $metaFile.FullName -Raw | ConvertFrom-Json
    }
    catch {
        Write-Warning "Failed to parse $($metaFile.FullName): $_"
        continue
    }

    # events.jsonl is written as a sibling of metadata.json on session finalize.
    $eventsPath = Join-Path $metaFile.Directory.FullName 'events.jsonl'
    if (-not (Test-Path $eventsPath)) {
        Write-Warning "No sibling events.jsonl for $($metaFile.FullName) (logSource=$($meta.PSObject.Properties['logSource'].Value)), skipping"
        continue
    }
    if ((Get-Item $eventsPath).Length -eq 0) {
        Write-Warning "Empty events.jsonl for $($metaFile.FullName), skipping"
        continue
    }

    $variant = if ($meta.PSObject.Properties['variant']) { [string]$meta.variant } else { '' }
    $stimulusName = if ($meta.PSObject.Properties['stimulusName']) { [string]$meta.stimulusName } else { '' }
    $trialIndex = if ($meta.PSObject.Properties['trialIndex'] -and $null -ne $meta.trialIndex) { [string]$meta.trialIndex } else { '0' }
    $evalFile = if ($meta.PSObject.Properties['evalFilePath'] -and $meta.evalFilePath) { [string]$meta.evalFilePath }
                elseif ($meta.PSObject.Properties['evalName'] -and $meta.evalName) { [string]$meta.evalName }
                else { '' }

    # Derive plugin/skill. Prefer evalFilePath (tests/<plugin>/<skill>/eval.yaml);
    # fall back to the executor-session-logs directory layout (.../<evalName>/<model>/<stimulus>/trial-N).
    $identity = if ($evalFile) { Get-EvalIdentity -EvalFile $evalFile } else { $null }
    if (-not $identity -and $evalFile) {
        $identity = Get-EvalIdentity -EvalFile ($evalFile.TrimEnd('/') + '/eval.yaml')
    }
    $pluginName = if ($identity) { $identity.Plugin } else { 'unknown' }
    $skillName = if ($identity) { $identity.Skill } else { if ($evalFile) { $evalFile } else { 'unknown' } }

    if (-not $stimulusName) {
        Write-Warning "No stimulusName in $($metaFile.FullName), using skill name '$skillName'"
        $stimulusName = $skillName
    }

    $roleTag = if ($variant -and $roleMap.ContainsKey($variant)) { $roleMap[$variant] }
               elseif ($variant) { $variant.ToLower() }
               else { 'unknown' }

    $safeScenario = ($stimulusName -replace '[^a-zA-Z0-9_-]', '-').ToLower()

    # Plugin subdirectory
    $pluginOutDir = Join-Path $sessionsOutDir $pluginName
    New-Item -ItemType Directory -Path $pluginOutDir -Force | Out-Null

    # Build output filename: <scenario>--<role>--run<N>.jsonl, de-duplicated on collision
    # (e.g. retried attempts that share a trial index).
    $baseName = "$safeScenario--$roleTag--run$trialIndex"
    $outFileName = "$baseName.jsonl"
    $collisionKey = "$pluginName/$outFileName"
    if ($usedNames.ContainsKey($collisionKey)) {
        $suffix = $usedNames[$collisionKey] + 1
        $usedNames[$collisionKey] = $suffix
        $outFileName = "$baseName-$suffix.jsonl"
    } else {
        $usedNames[$collisionKey] = 1
    }

    $outPath = Join-Path $pluginOutDir $outFileName
    Copy-Item -Path $eventsPath -Destination $outPath -Force

    $fileSize = (Get-Item $outPath).Length
    Write-Host "  Copied: $pluginName/$outFileName ($([math]::Round($fileSize / 1024, 1)) KB)"

    # Build manifest entry
    $relativeUrl = "sessions/$subDir/$pluginName/$outFileName"
    $leaf = [System.IO.Path]::GetFileNameWithoutExtension($outFileName)
    $displayName = "$pluginName / $stimulusName ($roleTag, run $trialIndex)"
    $id = "$subDir/$pluginName/$leaf"

    $tags = @($Source, $pluginName, $roleTag, $safeScenario)
    if ($Source -eq 'pr' -and $PrNumber -gt 0) {
        $tags += "pr-$PrNumber"
    }
    if ($Source -eq 'scheduled') {
        $tags += $dateTag
    }

    $mtime = try {
        [long]([DateTimeOffset]((Get-Item $eventsPath).LastWriteTimeUtc)).ToUnixTimeMilliseconds()
    } catch {
        [long]([DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds())
    }

    $manifestSessions += @{
        id    = $id
        name  = $displayName
        url   = $relativeUrl
        tags  = $tags
        mtime = $mtime
    }
}

# Write manifest.json
$manifest = @{
    generated = (Get-Date -Format 'o')
    sessions  = $manifestSessions
}

$manifestPath = Join-Path $OutputDir "manifest.json"
$manifest | ConvertTo-Json -Depth 10 | Out-File -FilePath $manifestPath -Encoding utf8

Write-Host "`nManifest written to $manifestPath with $($manifestSessions.Count) session(s)"
if ($manifestSessions.Count -eq 0) {
    Write-Warning "No sessions were extracted from any artifact. Check warnings above for details."
    Write-Warning "Metadata files scanned: $($metadataFiles.Count)"
}