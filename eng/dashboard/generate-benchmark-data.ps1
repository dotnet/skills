<#
.SYNOPSIS
    Converts skill-validator results into benchmark dashboard data.

.DESCRIPTION
    Reads the skill-validator results.json file (which contains all verdicts) and
    produces a per-component JSON file (<ComponentName>.json) compatible with the
    benchmark dashboard. If an existing JSON file is provided, the new data point
    is appended to the existing history.

.PARAMETER ResultsFile
    Path to the skill-validator results.json file.

.PARAMETER ComponentName
    Name of the component these results belong to. Used as the output filename.

.PARAMETER OutputDir
    Path to write the output files. Defaults to the directory containing ResultsFile.

.PARAMETER ExistingDataFile
    Optional path to an existing <ComponentName>.json file from gh-pages to append to.

.PARAMETER CommitJson
    Optional JSON string with commit info (id, message, author, timestamp, url).
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$ResultsFile,

    [Parameter(Mandatory)]
    [string]$ComponentName,

    [Parameter()]
    [string]$OutputDir,

    [Parameter()]
    [string]$ExistingDataFile,

    [Parameter()]
    [string]$CommitJson
)

$ErrorActionPreference = "Stop"

if (-not $OutputDir) {
    $OutputDir = Split-Path $ResultsFile -Parent
}

# Read skill-validator results
if (-not (Test-Path $ResultsFile)) {
    Write-Warning "Results file not found: $ResultsFile"
    exit 0
}

$results = Get-Content $ResultsFile -Raw | ConvertFrom-Json
$model = $results.model

if (-not $results.verdicts -or $results.verdicts.Count -eq 0) {
    Write-Warning "No verdicts found in $ResultsFile"
    exit 0
}

# Build bench arrays for this run
$qualityBenches = [System.Collections.Generic.List[object]]::new()
$efficiencyBenches = [System.Collections.Generic.List[object]]::new()

foreach ($verdict in $results.verdicts) {
    $skillName = $verdict.skillName

    # Check verdict-level activation state
    $verdictNotActivated = $false
    if ($verdict.skillNotActivated -eq $true) {
        $verdictNotActivated = $true
    }

    foreach ($scenario in $verdict.scenarios) {
        $testName = "$skillName/$($scenario.scenarioName)"

        # Check per-scenario activation state
        $scenarioNotActivated = $false
        if ($scenario.skillActivation -and -not $scenario.skillActivation.activated) {
            $scenarioNotActivated = $true
        }
        $notActivated = $verdictNotActivated -or $scenarioNotActivated

        # Check per-scenario timeout state
        $scenarioTimedOut = $false
        if ($scenario.timedOut -eq $true) {
            $scenarioTimedOut = $true
        }

        # Quality scores (from judge results, scale 0-5 mapped to 0-10 for dashboard)
        if ($null -ne $scenario.withSkill.judgeResult.overallScore) {
            $benchEntry = @{
                name  = "$testName - Skilled Quality"
                unit  = "Score (0-10)"
                value = [float]$scenario.withSkill.judgeResult.overallScore * 2
            }
            if ($notActivated) {
                $benchEntry.notActivated = $true
            }
            if ($scenarioTimedOut) {
                $benchEntry.timedOut = $true
            }
            $qualityBenches.Add($benchEntry)
        }
        if ($null -ne $scenario.baseline.judgeResult.overallScore) {
            $qualityBenches.Add(@{
                name  = "$testName - Vanilla Quality"
                unit  = "Score (0-10)"
                value = [float]$scenario.baseline.judgeResult.overallScore * 2
            })
        }

        # Efficiency metrics (from with-skill run)
        if ($null -ne $scenario.withSkill.metrics.wallTimeMs) {
            $effBenchEntry = @{
                name  = "$testName - Skilled Time"
                unit  = "seconds"
                value = [math]::Round([float]$scenario.withSkill.metrics.wallTimeMs / 1000, 1)
            }
            if ($notActivated) {
                $effBenchEntry.notActivated = $true
            }
            if ($scenarioTimedOut) {
                $effBenchEntry.timedOut = $true
            }
            $efficiencyBenches.Add($effBenchEntry)
        }
        if ($null -ne $scenario.withSkill.metrics.tokenEstimate) {
            $tokenBenchEntry = @{
                name  = "$testName - Skilled Tokens In"
                unit  = "tokens"
                value = [float]$scenario.withSkill.metrics.tokenEstimate
            }
            if ($notActivated) {
                $tokenBenchEntry.notActivated = $true
            }
            if ($scenarioTimedOut) {
                $tokenBenchEntry.timedOut = $true
            }
            $efficiencyBenches.Add($tokenBenchEntry)
        }
    }
}

# Build commit info
$commit = @{}
if ($CommitJson) {
    $commit = $CommitJson | ConvertFrom-Json -AsHashtable
} else {
    $commit = @{ id = "local"; message = "Local run"; timestamp = (Get-Date -Format "o") }
}

$now = [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds()

$qualityEntry = @{
    commit = $commit
    date   = $now
    tool   = "customBiggerIsBetter"
    model  = $model
    benches = $qualityBenches.ToArray()
}

$efficiencyEntry = @{
    commit = $commit
    date   = $now
    tool   = "customSmallerIsBetter"
    model  = $model
    benches = $efficiencyBenches.ToArray()
}

$qualityKey = "Quality"
$efficiencyKey = "Efficiency"

# Load existing data or create new structure
$benchmarkData = @{
    lastUpdate = $now
    repoUrl    = ""
    entries    = @{
        $qualityKey    = @()
        $efficiencyKey = @()
    }
}

if ($ExistingDataFile -and (Test-Path $ExistingDataFile)) {
    $existingContent = Get-Content $ExistingDataFile -Raw
    try {
        $benchmarkData = $existingContent | ConvertFrom-Json -AsHashtable
        $benchmarkData['lastUpdate'] = $now
    } catch {
        Write-Warning "Failed to parse existing data file, starting fresh: $_"
    }
}

# Append new entries
if (-not $benchmarkData['entries']) {
    $benchmarkData['entries'] = @{}
}
if (-not $benchmarkData['entries'][$qualityKey]) {
    $benchmarkData['entries'][$qualityKey] = @()
}
if (-not $benchmarkData['entries'][$efficiencyKey]) {
    $benchmarkData['entries'][$efficiencyKey] = @()
}

$benchmarkData['entries'][$qualityKey] += @($qualityEntry)
$benchmarkData['entries'][$efficiencyKey] += @($efficiencyEntry)

# Write <ComponentName>.json
New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
$dataJson = $benchmarkData | ConvertTo-Json -Depth 10
$dataJsonFile = Join-Path $OutputDir "$ComponentName.json"
$dataJson | Out-File -FilePath $dataJsonFile -Encoding utf8

Write-Host "[OK] Benchmark $ComponentName.json generated: $dataJsonFile"
Write-Host "   Quality entries: $($qualityBenches.Count)"
Write-Host "   Efficiency entries: $($efficiencyBenches.Count)"
Write-Host "   Total data points: $($benchmarkData['entries'][$qualityKey].Count)"
