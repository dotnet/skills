<#
.SYNOPSIS
    Generates a markdown summary of evaluation results.

.DESCRIPTION
    Reads evaluation results from the results directory and produces a markdown
    summary table suitable for GitHub Job Summary or PR comment.

.PARAMETER ResultsDir
    Path to the results directory for this run.

.PARAMETER ComponentName
    Name of the component being evaluated. Included in the summary header.

.PARAMETER GitHubRunUrl
    Optional URL to the GitHub Actions workflow run for linking in the footer.

.PARAMETER ArtifactsUrl
    Optional URL to the artifacts download page for linking in the footer.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$ResultsDir,

    [Parameter()]
    [string]$ComponentName,

    [Parameter()]
    [string]$GitHubRunUrl,

    [Parameter()]
    [string]$ArtifactsUrl,

    [Parameter(Mandatory)]
    [string]$RunId
)

$ErrorActionPreference = "Stop"

#region Helper Functions

function Get-QualityDelta {
    param([float]$Vanilla, [float]$Skilled)
    $delta = $Skilled - $Vanilla
    if ($delta -ge 3) { return "+$delta (much better)" }
    if ($delta -ge 2) { return "+$delta (better)" }
    if ($delta -ge 1) { return "+$delta (slightly better)" }
    if ($delta -gt 0) { return "+$delta (marginally better)" }
    if ($delta -eq 0) { return "0 (same)" }
    if ($delta -gt -1) { return "$delta (marginally worse)" }
    if ($delta -gt -2) { return "$delta (slightly worse)" }
    if ($delta -gt -3) { return "$delta (worse)" }
    return "$delta (much worse)"
}

function Get-QualityDeltaEmoji {
    param([float]$Delta)
    if ($Delta -ge 3) { return "+++" }
    if ($Delta -ge 2) { return "++" }
    if ($Delta -ge 1) { return "+" }
    if ($Delta -gt 0) { return "~+" }
    if ($Delta -eq 0) { return "=" }
    if ($Delta -gt -1) { return "~-" }
    if ($Delta -gt -2) { return "-" }
    if ($Delta -gt -3) { return "--" }
    return "---"
}

function Get-PercentDelta {
    param([int]$Vanilla, [int]$Skilled)
    if ($null -eq $Vanilla -or $null -eq $Skilled -or $Vanilla -eq 0) {
        return "N/A"
    }
    $ratio = ($Skilled / $Vanilla) * 100
    $pct = [math]::Round($ratio - 100)
    if ($pct -le 0) { return "${pct}%" }
    return "+${pct}%"
}

#endregion

function Get-Winner {
    param(
        [float]$VanillaScore,
        [float]$SkilledScore,
        [int]$VanillaTime = 0,
        [int]$SkilledTime = 0,
        [int]$VanillaTokens = 0,
        [int]$SkilledTokens = 0
    )
    # Quality is the primary criterion
    if ($SkilledScore -gt $VanillaScore) { return "Skilled" }
    if ($VanillaScore -gt $SkilledScore) { return "Vanilla" }

    # Quality tied - use efficiency (time + tokens) as tiebreaker
    # Score: lower time/tokens = better. Count wins per metric.
    $skilledWins = 0
    $vanillaWins = 0

    if ($VanillaTime -gt 0 -and $SkilledTime -gt 0) {
        if ($SkilledTime -lt $VanillaTime) { $skilledWins++ }
        elseif ($VanillaTime -lt $SkilledTime) { $vanillaWins++ }
    }
    if ($VanillaTokens -gt 0 -and $SkilledTokens -gt 0) {
        if ($SkilledTokens -lt $VanillaTokens) { $skilledWins++ }
        elseif ($VanillaTokens -lt $SkilledTokens) { $vanillaWins++ }
    }

    if ($skilledWins -gt $vanillaWins) { return "Skilled" }
    if ($vanillaWins -gt $skilledWins) { return "Vanilla" }
    return "Tie"
}

function Format-TokenCount {
    param($Value)
    if ($null -eq $Value -or $Value -eq 0) { return "N/A" }
    if ([int]$Value -ge 1000000) {
        return "$([math]::Round([int]$Value / 1000000, 1))M"
    }
    if ([int]$Value -ge 1000) {
        return "$([math]::Round([int]$Value / 1000, 1))k"
    }
    return "$Value"
}

#region Main Logic

Write-Host ""
Write-Host ("=" * 60)
Write-Host "[SUMMARY] Generating Summary"
Write-Host ("=" * 60)

# Find all test results
$testDirs = Get-ChildItem -Path $ResultsDir -Directory -ErrorAction SilentlyContinue
if (-not $testDirs) {
    Write-Warning "No test results found in $ResultsDir"
    $testDirs = @()
}

# Load all test data in a single pass
$testData = @{}
foreach ($testDir in $testDirs) {
    $testName = $testDir.Name
    $evalFile = Join-Path $testDir.FullName $RunId "evaluation.json"
    $vanillaStatsFile = Join-Path $testDir.FullName $RunId "vanilla-stats.json"
    $skilledStatsFile = Join-Path $testDir.FullName $RunId "skilled-stats.json"
    $activationsFile = Join-Path $testDir.FullName $RunId "skilled-activations.json"

    $data = @{
        Name = $testName
        VanillaEval = $null
        SkilledEval = $null
        VanillaStats = $null
        SkilledStats = $null
        Activations = $null
    }

    if (Test-Path $evalFile) {
        $evalData = Get-Content $evalFile -Raw | ConvertFrom-Json
        $data.VanillaEval = $evalData.evaluations.vanilla
        $data.SkilledEval = $evalData.evaluations.skilled
    }
    if (Test-Path $vanillaStatsFile) {
        $data.VanillaStats = Get-Content $vanillaStatsFile -Raw | ConvertFrom-Json
    }
    if (Test-Path $skilledStatsFile) {
        $data.SkilledStats = Get-Content $skilledStatsFile -Raw | ConvertFrom-Json
    }
    if (Test-Path $activationsFile) {
        $data.Activations = Get-Content $activationsFile -Raw | ConvertFrom-Json
    }

    $testData[$testName] = $data
}

$summaryLines = New-Object System.Collections.Generic.List[string]

# Header
$headerSuffix = if ($ComponentName) { " — $ComponentName" } else { "" }
$summaryLines.Add("## Copilot Skills Evaluation Results$headerSuffix")
$summaryLines.Add("")
$summaryLines.Add("**Run Date**: $(Get-Date -Format 'yyyy-MM-dd HH:mm UTC')")
$summaryLines.Add("**Tests Run**: $($testDirs.Count)")
$summaryLines.Add("")

# Summary table
$summaryLines.Add("### Summary")
$summaryLines.Add("")
$summaryLines.Add("| Test | Quality (0-10) | Checklist | Time | Tokens (in) | Skills | Winner |")
$summaryLines.Add("|----------|----------------|-----------|------|-------------|--------|--------|")

$overallVanilla = 0.0
$overallSkilled = 0.0
$testCount = 0

foreach ($testDir in $testDirs) {
    $d = $testData[$testDir.Name]
    $vanillaEval = $d.VanillaEval
    $skilledEval = $d.SkilledEval
    $vanillaStats = $d.VanillaStats
    $skilledStats = $d.SkilledStats

    # Quality delta
    $qualityDelta = "N/A"
    if ($vanillaEval -and $vanillaEval.score) {
        $overallVanilla += [float]$vanillaEval.score
    }
    if ($skilledEval -and $skilledEval.score) {
        $overallSkilled += [float]$skilledEval.score
    }
    if ($vanillaEval -and $vanillaEval.score -and $skilledEval -and $skilledEval.score) {
        $delta = [float]$skilledEval.score - [float]$vanillaEval.score
        $deltaEmoji = Get-QualityDeltaEmoji -Delta $delta
        $qualityDelta = "$deltaEmoji $delta"
        $testCount++
    }

    # Checklist delta
    $checklistDelta = "N/A"
    $vCheck = if ($vanillaEval -and $null -ne $vanillaEval.checklist_score -and $null -ne $vanillaEval.checklist_max) { "$($vanillaEval.checklist_score)/$($vanillaEval.checklist_max)" } else { $null }
    $sCheck = if ($skilledEval -and $null -ne $skilledEval.checklist_score -and $null -ne $skilledEval.checklist_max) { "$($skilledEval.checklist_score)/$($skilledEval.checklist_max)" } else { $null }
    if ($vCheck -and $sCheck) {
        $clDelta = [float]$skilledEval.checklist_score - [float]$vanillaEval.checklist_score
        $clEmoji = Get-QualityDeltaEmoji -Delta $clDelta
        $checklistDelta = "$clEmoji $clDelta ($vCheck vs $sCheck)"
    } elseif ($sCheck) {
        $checklistDelta = "$sCheck (skilled only)"
    } elseif ($vCheck) {
        $checklistDelta = "$vCheck (vanilla only)"
    }

    # Time delta
    $timeDelta = "N/A"
    if ($vanillaStats -and $vanillaStats.TotalTimeSeconds -and $skilledStats -and $skilledStats.TotalTimeSeconds) {
        $timeDelta = Get-PercentDelta -Vanilla ([int]$vanillaStats.TotalTimeSeconds) -Skilled ([int]$skilledStats.TotalTimeSeconds)
    }

    # Token delta
    $tokenDelta = "N/A"
    if ($vanillaStats -and $skilledStats -and $vanillaStats.TokensIn -and $skilledStats.TokensIn) {
        $tokenDelta = Get-PercentDelta -Vanilla ([int]$vanillaStats.TokensIn) -Skilled ([int]$skilledStats.TokensIn)
    }

    # Winner
    $winner = "N/A"
    if ($vanillaEval -and $skilledEval -and $vanillaEval.score -and $skilledEval.score) {
        $winnerParams = @{
            VanillaScore = [float]$vanillaEval.score
            SkilledScore = [float]$skilledEval.score
        }
        if ($vanillaStats -and $skilledStats) {
            if ($vanillaStats.TotalTimeSeconds -and $skilledStats.TotalTimeSeconds) {
                $winnerParams.VanillaTime = [int]$vanillaStats.TotalTimeSeconds
                $winnerParams.SkilledTime = [int]$skilledStats.TotalTimeSeconds
            }
            if ($vanillaStats.TokensIn -and $skilledStats.TokensIn) {
                $winnerParams.VanillaTokens = [int]$vanillaStats.TokensIn
                $winnerParams.SkilledTokens = [int]$skilledStats.TokensIn
            }
        }
        $winner = Get-Winner @winnerParams
    }

    # Skills activation
    $skillsSummary = "-"
    if ($d.Activations) {
        if ($d.Activations.Activated) {
            $parts = @()
            if ($d.Activations.Skills -and $d.Activations.Skills.Count -gt 0) {
                $parts += $d.Activations.Skills
            }
            if ($d.Activations.Agents -and $d.Activations.Agents.Count -gt 0) {
                $parts += $d.Activations.Agents
            }
            $skillsSummary = $parts -join ', '
        } else {
            $skillsSummary = ":warning: NONE"
        }
    }

    $summaryLines.Add("| $($d.Name) | $qualityDelta | $checklistDelta | $timeDelta | $tokenDelta | $skillsSummary | $winner |")
}

$summaryLines.Add("")

# Overall result
if ($testCount -gt 0) {
    $avgVanilla = [math]::Round($overallVanilla / $testCount, 1)
    $avgSkilled = [math]::Round($overallSkilled / $testCount, 1)

    if ($avgSkilled -gt $avgVanilla) {
        $summaryLines.Add("### Overall Result: **Skills Improved Response Quality**")
    } elseif ($avgSkilled -eq $avgVanilla) {
        $summaryLines.Add("### Overall Result: **No Significant Difference**")
    } else {
        $summaryLines.Add("### Overall Result: **Skills Degraded Response Quality**")
    }
    $summaryLines.Add("")
    $summaryLines.Add("**Average Scores**: Vanilla $avgVanilla/10 | Skilled $avgSkilled/10")
} else {
    $summaryLines.Add("### Overall Result: **No tests evaluated**")
}

$summaryLines.Add("")

# Per-test comparison tables
$summaryLines.Add("### Test Details")
$summaryLines.Add("")

foreach ($testDir in $testDirs) {
    $d = $testData[$testDir.Name]
    $vanillaEval = $d.VanillaEval
    $skilledEval = $d.SkilledEval
    $vanillaStats = $d.VanillaStats
    $skilledStats = $d.SkilledStats

    $summaryLines.Add("#### $($d.Name)")
    $summaryLines.Add("")
    $summaryLines.Add("| Metric | Vanilla | Skilled | Delta |")
    $summaryLines.Add("|--------|---------|---------|-------|")

    # Quality row
    $vScore = if ($vanillaEval -and $vanillaEval.score) { "$($vanillaEval.score)/10" } else { "N/A" }
    $sScore = if ($skilledEval -and $skilledEval.score) { "$($skilledEval.score)/10" } else { "N/A" }
    $qDelta = "N/A"
    if ($vanillaEval -and $vanillaEval.score -and $skilledEval -and $skilledEval.score) {
        $dd = [float]$skilledEval.score - [float]$vanillaEval.score
        $emoji = Get-QualityDeltaEmoji -Delta $dd
        $qDelta = "$emoji $dd"
    }
    $summaryLines.Add("| Quality | $vScore | $sScore | $qDelta |")

    # Time row
    $vTime = if ($vanillaStats -and $vanillaStats.TotalTimeSeconds) { "$($vanillaStats.TotalTimeSeconds)s" } else { "N/A" }
    $sTime = if ($skilledStats -and $skilledStats.TotalTimeSeconds) { "$($skilledStats.TotalTimeSeconds)s" } else { "N/A" }
    $tDelta = "N/A"
    if ($vanillaStats -and $skilledStats -and $vanillaStats.TotalTimeSeconds -and $skilledStats.TotalTimeSeconds) {
        $tDelta = Get-PercentDelta -Vanilla ([int]$vanillaStats.TotalTimeSeconds) -Skilled ([int]$skilledStats.TotalTimeSeconds)
    }
    $summaryLines.Add("| Time | $vTime | $sTime | $tDelta |")

    # Tokens row
    $vTokens = "N/A"
    $sTokens = "N/A"
    $tkDelta = "N/A"
    if ($vanillaStats -and $vanillaStats.TokensIn) {
        $vIn = Format-TokenCount $vanillaStats.TokensIn
        $vOut = Format-TokenCount $vanillaStats.TokensOut
        $vTokens = "$vIn / $vOut"
    }
    if ($skilledStats -and $skilledStats.TokensIn) {
        $sIn = Format-TokenCount $skilledStats.TokensIn
        $sOut = Format-TokenCount $skilledStats.TokensOut
        $sTokens = "$sIn / $sOut"
    }
    if ($vanillaStats -and $skilledStats -and $vanillaStats.TokensIn -and $skilledStats.TokensIn) {
        $tkDelta = Get-PercentDelta -Vanilla ([int]$vanillaStats.TokensIn) -Skilled ([int]$skilledStats.TokensIn)
    }
    $summaryLines.Add("| Tokens (in/out) | $vTokens | $sTokens | $tkDelta |")

    $summaryLines.Add("")

    # Model verification
    if ($vanillaStats -and $skilledStats -and $vanillaStats.Model -and $skilledStats.Model) {
        if ($vanillaStats.Model -eq $skilledStats.Model) {
            $summaryLines.Add("**Model**: $($vanillaStats.Model) (consistent)")
        } else {
            $summaryLines.Add("**WARNING - Model Mismatch**: Vanilla=$($vanillaStats.Model), Skilled=$($skilledStats.Model)")
        }
        $summaryLines.Add("")
    }

    # Skill activation info
    if ($d.Activations) {
        if ($d.Activations.Activated) {
            $activationParts = @()
            if ($d.Activations.Skills -and $d.Activations.Skills.Count -gt 0) {
                $activationParts += "Skills: $($d.Activations.Skills -join ', ')"
            }
            if ($d.Activations.Agents -and $d.Activations.Agents.Count -gt 0) {
                $activationParts += "Agents: $($d.Activations.Agents -join ', ')"
            }
            $summaryLines.Add("**Skills Activated**: $($activationParts -join ' | ')")
        } else {
            $summaryLines.Add("**Skills Activated**: :warning: **NONE** — skills were installed but not invoked")
        }
        $summaryLines.Add("")
    }

    # Evaluation details in collapsible section
    if ($vanillaEval -or $skilledEval) {
        $summaryLines.Add("<details>")
        $summaryLines.Add("<summary>Evaluation Details</summary>")
        $summaryLines.Add("")

        foreach ($runType in @("vanilla", "skilled")) {
            $eval = if ($runType -eq "vanilla") { $vanillaEval } else { $skilledEval }
            if ($eval) {
                $label = $runType.Substring(0,1).ToUpper() + $runType.Substring(1)
                $checklistInfo = ""
                if ($null -ne $eval.checklist_score -and $null -ne $eval.checklist_max) {
                    $checklistInfo = " | Checklist $($eval.checklist_score)/$($eval.checklist_max)"
                }
                $summaryLines.Add("**$label** ($($eval.score)/10): Accuracy $($eval.accuracy)/10, Completeness $($eval.completeness)/10, Actionability $($eval.actionability)/10, Clarity $($eval.clarity)/10$checklistInfo")
                if ($eval.reasoning) {
                    $summaryLines.Add("> $($eval.reasoning)")
                }
                $summaryLines.Add("")
            }
        }

        $summaryLines.Add("</details>")
        $summaryLines.Add("")
    }
}

# Footer
$summaryLines.Add("---")
$footerParts = @("*Generated by Copilot Skills Evaluation Pipeline")
if ($GitHubRunUrl) {
    $footerParts += " | [View workflow run]($GitHubRunUrl)"
}
if ($ArtifactsUrl) {
    $footerParts += " | [Download artifacts]($ArtifactsUrl)"
}
$footerParts += "*"
$summaryLines.Add($footerParts -join "")

# Ensure results directory exists before writing summary
if (-not (Test-Path $ResultsDir)) {
    New-Item -ItemType Directory -Force -Path $ResultsDir | Out-Null
}

# Write summary
$summaryContent = $summaryLines -join "`n"
$summaryFile = Join-Path $ResultsDir "summary.md"
$summaryContent | Out-File -FilePath $summaryFile -Encoding utf8

Write-Host ""
Write-Host "[OK] Summary generated: $summaryFile"
Write-Host ""
Write-Host "--- Summary Preview ---"
Write-Host $summaryContent
Write-Host "--- End Preview ---"

#endregion
