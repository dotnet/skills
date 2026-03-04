<#
.SYNOPSIS
    Converts skill-validator results into benchmark dashboard data.

.DESCRIPTION
    Reads the skill-validator results.json file (which contains all verdicts) and
    produces a per-plugin JSON file (<PluginName>.json) compatible with the
    benchmark dashboard. If an existing JSON file is provided, the new data point
    is appended to the existing history.

.PARAMETER ResultsFile
    Path to the skill-validator results.json file.

.PARAMETER PluginName
    Name of the plugin these results belong to. Used as the output filename.

.PARAMETER OutputDir
    Path to write the output files. Defaults to the directory containing ResultsFile.

.PARAMETER ExistingDataFile
    Optional path to an existing <PluginName>.json file from gh-pages to append to.

.PARAMETER CommitJson
    Optional JSON string with commit info (id, message, author, timestamp, url).
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$ResultsFile,

    [Parameter(Mandatory)]
    [string]$PluginName,

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

    foreach ($scenario in $verdict.scenarios) {
        $testName = "$skillName/$($scenario.scenarioName)"

        # Check per-scenario activation state (verdict-level skillNotActivated is a
        # roll-up across all scenarios and must NOT be used here — each datapoint
        # should reflect only its own scenario's activation result).
        $notActivated = $false
        if ($scenario.skillActivation -and -not $scenario.skillActivation.activated) {
            # Only flag as not-activated if activation was expected (expect_activation defaults to true)
            $expectActivation = $true
            if ($scenario.PSObject.Properties['expectActivation'] -and $scenario.expectActivation -eq $false) {
                $expectActivation = $false
            }
            if ($expectActivation) {
                $notActivated = $true
            }
        }

        # Check per-scenario timeout state
        $scenarioTimedOut = $false
        if ($scenario.timedOut -eq $true) {
            $scenarioTimedOut = $true
        }

        # Check overfitting state — use per-scenario assessment when available.
        # The verdict-level overfittingResult is a skill-wide aggregate; applying it
        # to every scenario would misrepresent scenarios that are fine.  We check
        # rubric and assertion assessments for this scenario and fall back to the
        # verdict-level result only when no per-scenario data exists.
        $overfittingSeverity = $null
        $overfittingScore = $null
        if ($verdict.overfittingResult -and $verdict.overfittingResult.severity -in @("Moderate", "High")) {
            $scenarioName = $scenario.scenarioName
            # Determine whether the overfittingResult carries per-scenario
            # breakdowns (rubricAssessments / assertionAssessments arrays).
            # When breakdowns exist we use them; when they don't (older schema)
            # we fall back to the verdict-level flag for every scenario.
            $hasBreakdowns = $verdict.overfittingResult.PSObject.Properties['rubricAssessments'] -or
                             $verdict.overfittingResult.PSObject.Properties['assertionAssessments']

            if ($hasBreakdowns) {
                $rubrics    = $verdict.overfittingResult.rubricAssessments    | Where-Object { $_.scenario -eq $scenarioName }
                $assertions = $verdict.overfittingResult.assertionAssessments | Where-Object { $_.scenario -eq $scenarioName }
                # Rubric classifications: outcome | technique | vocabulary  — flag non-outcome.
                # Assertion classifications: broad | narrow               — flag narrow.
                $scenarioHasIssues = ($rubrics    | Where-Object { $_.classification -ne "outcome" }) -or
                                     ($assertions | Where-Object { $_.classification -eq "narrow" })
                if ($scenarioHasIssues) {
                    $overfittingSeverity = $verdict.overfittingResult.severity.ToLower()
                    $overfittingScore = $verdict.overfittingResult.score
                }
                # else: breakdowns exist but this scenario has no issues — leave unflagged
            } else {
                # No per-scenario breakdown available (older schema); fall back to verdict-level
                $overfittingSeverity = $verdict.overfittingResult.severity.ToLower()
                $overfittingScore = $verdict.overfittingResult.score
            }
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
            if ($overfittingSeverity) {
                $benchEntry.overfitting = $overfittingSeverity
                $benchEntry.overfittingScore = $overfittingScore
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
            if ($overfittingSeverity) {
                $effBenchEntry.overfitting = $overfittingSeverity
                $effBenchEntry.overfittingScore = $overfittingScore
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
            if ($overfittingSeverity) {
                $tokenBenchEntry.overfitting = $overfittingSeverity
                $tokenBenchEntry.overfittingScore = $overfittingScore
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

# Write <PluginName>.json
New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
$dataJson = $benchmarkData | ConvertTo-Json -Depth 10
$dataJsonFile = Join-Path $OutputDir "$PluginName.json"
$dataJson | Out-File -FilePath $dataJsonFile -Encoding utf8

Write-Host "[OK] Benchmark $PluginName.json generated: $dataJsonFile"
Write-Host "   Quality entries: $($qualityBenches.Count)"
Write-Host "   Efficiency entries: $($efficiencyBenches.Count)"
Write-Host "   Total data points: $($benchmarkData['entries'][$qualityKey].Count)"
