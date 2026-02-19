<#
.SYNOPSIS
    Parses Copilot CLI stats from the output of a -p (programmatic) mode run.

.DESCRIPTION
    Extracts metrics like premium requests, API time, session time, code changes,
    model used, and token counts from Copilot CLI output.

.PARAMETER Output
    The raw text output from Copilot CLI.

.OUTPUTS
    Hashtable with parsed metrics.
#>
function Parse-CopilotStats {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$Output
    )

    $stats = @{
        PremiumRequests = $null
        ApiTimeSeconds  = $null
        TotalTimeSeconds = $null
        LinesAdded      = $null
        LinesRemoved    = $null
        Model           = $null
        TokensIn        = $null
        TokensOut       = $null
        TokensCached    = $null
    }

    if ($Output -match "Total usage est:\s+(\d+) Premium request") {
        $stats.PremiumRequests = [int]$Matches[1]
    }
    if ($Output -match "API time spent:\s+(\d+)h\s+(\d+)m\s+([\d.]+)s") {
        $stats.ApiTimeSeconds = [int]$Matches[1] * 3600 + [int]$Matches[2] * 60 + [math]::Round([float]$Matches[3])
    } elseif ($Output -match "API time spent:\s+(\d+)m\s+([\d.]+)s") {
        $stats.ApiTimeSeconds = [int]$Matches[1] * 60 + [math]::Round([float]$Matches[2])
    } elseif ($Output -match "API time spent:\s+([\d.]+)s") {
        $stats.ApiTimeSeconds = [math]::Round([float]$Matches[1])
    }
    if ($Output -match "Total session time:\s+(\d+)h\s+(\d+)m\s+([\d.]+)s") {
        $stats.TotalTimeSeconds = [int]$Matches[1] * 3600 + [int]$Matches[2] * 60 + [math]::Round([float]$Matches[3])
    } elseif ($Output -match "Total session time:\s+(\d+)m\s+([\d.]+)s") {
        $stats.TotalTimeSeconds = [int]$Matches[1] * 60 + [math]::Round([float]$Matches[2])
    } elseif ($Output -match "Total session time:\s+([\d.]+)s") {
        $stats.TotalTimeSeconds = [math]::Round([float]$Matches[1])
    }
    if ($Output -match "Total code changes:\s+\+(\d+) -(\d+)") {
        $stats.LinesAdded = [int]$Matches[1]
        $stats.LinesRemoved = [int]$Matches[2]
    }

    # Model and tokens from breakdown section
    # Examples:
    #   claude-opus-4.6         97.1k in, 370 out, 71.1k cached (Est. 6 Premium requests)
    #   claude-opus-4.6         118.1k in, 3.5k out, 90.8k cached (Est. 3 Premium requests)
    #   claude-opus-4.6         1.2M in, 45.3k out, 900k cached (Est. 10 Premium requests)
    if ($Output -match "(?m)^\s*([\w\-\.]+)\s+([\d.,]+)([kM])? in,\s+([\d.,]+)([kM])? out,\s+([\d.,]+)([kM])? cached") {
        $stats.Model = $Matches[1]
        $stats.TokensIn = [math]::Round([float]($Matches[2] -replace ',', '') * $(if ($Matches[3] -eq 'M') { 1000000 } elseif ($Matches[3] -eq 'k') { 1000 } else { 1 }))
        $stats.TokensOut = [math]::Round([float]($Matches[4] -replace ',', '') * $(if ($Matches[5] -eq 'M') { 1000000 } elseif ($Matches[5] -eq 'k') { 1000 } else { 1 }))
        $stats.TokensCached = [math]::Round([float]($Matches[6] -replace ',', '') * $(if ($Matches[7] -eq 'M') { 1000000 } elseif ($Matches[7] -eq 'k') { 1000 } else { 1 }))
    }

    return $stats
}

# This script is designed to be dot-sourced by other scripts.
# For standalone testing, use:
#   . ./parse-copilot-stats.ps1
#   Parse-CopilotStats -Output "your copilot output here" | ConvertTo-Json
