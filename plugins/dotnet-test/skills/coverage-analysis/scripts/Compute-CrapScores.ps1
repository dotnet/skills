# Compute-CrapScores.ps1
#
# Reads a Cobertura XML coverage file and calculates CRAP scores per method.
# Uses Alberto Savoia's original CRAP formula:
#   CRAP(m) = comp(m)^2 * (1 - cov(m))^3 + comp(m)
#
# Usage:
#   .\Compute-CrapScores.ps1 -CoberturaPath <path1>,<path2>,... [-CrapThreshold <int>] [-TopN <int>]
#
# Outputs:
#   - OVERALL_LINE_COVERAGE:<n.n>   (aggregate line coverage across input files, as percent)
#   - OVERALL_BRANCH_COVERAGE:<n.n> (aggregate branch coverage across input files, as percent)
#   - TOTAL_METHODS:<n>
#   - FLAGGED_METHODS:<n>
#   - HOTSPOTS:<json> (top N by CRAP score)

param(
    [Parameter(Mandatory)][string[]]$CoberturaPath,
    [int]$CrapThreshold = 30,
    [int]$TopN = 10
)

# Merge methods across all Cobertura files using a stable key (Class|Method|Signature|File).
# Line hits are accumulated so a line is counted as covered if any input coverage file covered it.
$methodMap = @{}
$overallLineRate = 0.0
$overallBranchRate = 0.0
$totalLinesCovered = 0
$totalLinesValid = 0
$totalBranchesCovered = 0
$totalBranchesValid = 0
$fallbackLineRates = [System.Collections.Generic.List[double]]::new()
$fallbackBranchRates = [System.Collections.Generic.List[double]]::new()

foreach ($filePath in $CoberturaPath) {
    if (-not (Test-Path $filePath)) {
        Write-Error "Cobertura file not found: $filePath"
        exit 2
    }

    try {
        [xml]$cobertura = Get-Content $filePath -Encoding UTF8 -ErrorAction Stop
    } catch {
        Write-Error "Failed to parse Cobertura XML: $filePath. $_"
        exit 2
    }

    # Prefer aggregate numerator/denominator attributes when present.
    if ($null -ne $cobertura.coverage.'lines-covered' -and $null -ne $cobertura.coverage.'lines-valid') {
        $totalLinesCovered += [double]$cobertura.coverage.'lines-covered'
        $totalLinesValid += [double]$cobertura.coverage.'lines-valid'
    } elseif ($cobertura.coverage.'line-rate') {
        $fallbackLineRates.Add([double]$cobertura.coverage.'line-rate')
    }
    if ($null -ne $cobertura.coverage.'branches-covered' -and $null -ne $cobertura.coverage.'branches-valid') {
        $totalBranchesCovered += [double]$cobertura.coverage.'branches-covered'
        $totalBranchesValid += [double]$cobertura.coverage.'branches-valid'
    } elseif ($cobertura.coverage.'branch-rate') {
        $fallbackBranchRates.Add([double]$cobertura.coverage.'branch-rate')
    }

    foreach ($package in $cobertura.coverage.packages.package) {
        foreach ($class in $package.classes.class) {
            $className = $class.name
            $fileName  = $class.filename

            foreach ($method in $class.methods.method) {
                $key = "$className|$($method.name)|$($method.signature)|$fileName"

                # Cyclomatic complexity is stored as an XML attribute in Cobertura format
                $complexity = if ($null -ne $method.complexity) { [int]$method.complexity } else { 1 }
                if ($complexity -lt 1) { $complexity = 1 }

                if (-not $methodMap.ContainsKey($key)) {
                    $methodMap[$key] = @{
                        Class      = $className
                        Method     = $method.name
                        Signature  = $method.signature
                        File       = $fileName
                        Complexity = $complexity
                        LineHits   = @{}
                    }
                }

                # Accumulate hit counts per line number across files
                foreach ($line in $method.lines.line) {
                    $lineNo = $line.number
                    $hits   = [int]$line.hits
                    if ($methodMap[$key].LineHits.ContainsKey($lineNo)) {
                        $methodMap[$key].LineHits[$lineNo] += $hits
                    } else {
                        $methodMap[$key].LineHits[$lineNo] = $hits
                    }
                }
            }
        }
    }
}

$results = [System.Collections.Generic.List[PSCustomObject]]::new()

foreach ($entry in $methodMap.Values) {
    $totalLines   = $entry.LineHits.Count
    $coveredLines = ($entry.LineHits.Values | Where-Object { $_ -gt 0 } | Measure-Object).Count
    $lineCoverage = if ($totalLines -gt 0) { $coveredLines / $totalLines } else { 0.0 }

    $complexity = $entry.Complexity

    # Alberto Savoia's CRAP formula: comp^2 * (1 - cov)^3 + comp
    # The cubic exponent on (1-cov) sharply penalizes low coverage:
    # at 0% coverage the risk multiplier is 1.0; at 50% it drops to 0.125.
    # Higher scores = more complex AND less covered = riskier to change
    $uncovered = 1.0 - $lineCoverage
    $crapScore = [Math]::Round(($complexity * $complexity * [Math]::Pow($uncovered, 3)) + $complexity, 2)

    $results.Add([PSCustomObject]@{
        Class        = $entry.Class
        Method       = $entry.Method
        Signature    = $entry.Signature
        File         = $entry.File
        TotalLines   = $totalLines
        CoveredLines = $coveredLines
        LineCoverage = [Math]::Round($lineCoverage * 100, 1)
        Complexity   = $complexity
        CrapScore    = $crapScore
    })
}

$hotspots = $results | Sort-Object CrapScore -Descending | Select-Object -First $TopN
$flagged  = $results | Where-Object { $_.CrapScore -gt $CrapThreshold }

if ($totalLinesValid -gt 0) {
    $overallLineRate = $totalLinesCovered / $totalLinesValid
} else {
    # Fallback approximation when Cobertura aggregate counters and per-file rates are unavailable.
    # This uses merged method line totals and may under/over-estimate if Cobertura
    # includes executable lines outside method nodes.
    $mergedTotalLines = ($results | Measure-Object -Property TotalLines -Sum).Sum
    $mergedCoveredLines = ($results | Measure-Object -Property CoveredLines -Sum).Sum
    if ($mergedTotalLines -gt 0) {
        $overallLineRate = [double]$mergedCoveredLines / [double]$mergedTotalLines
    } elseif ($fallbackLineRates.Count -gt 0) {
        $overallLineRate = ($fallbackLineRates | Measure-Object -Average).Average
    } else {
        $overallLineRate = 0.0
    }
}

if ($totalBranchesValid -gt 0) {
    $overallBranchRate = $totalBranchesCovered / $totalBranchesValid
} elseif ($fallbackBranchRates.Count -gt 0) {
    $overallBranchRate = ($fallbackBranchRates | Measure-Object -Average).Average
} else {
    $overallBranchRate = 0.0
}

Write-Host "OVERALL_LINE_COVERAGE:$([Math]::Round($overallLineRate * 100, 1))"
Write-Host "OVERALL_BRANCH_COVERAGE:$([Math]::Round($overallBranchRate * 100, 1))"
Write-Host "TOTAL_METHODS:$($results.Count)"
Write-Host "FLAGGED_METHODS:$($flagged.Count)"
if ($hotspots) {
    Write-Output "HOTSPOTS:$(@($hotspots) | ConvertTo-Json -Compress)"
} else {
    Write-Output "HOTSPOTS:[]"
}
