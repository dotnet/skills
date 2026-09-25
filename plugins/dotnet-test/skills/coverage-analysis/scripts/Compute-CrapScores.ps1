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
$methodMap = [System.Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
$overallLineHits = [System.Collections.Generic.Dictionary[string, int]]::new([StringComparer]::Ordinal)
$overallBranchData = [System.Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
$overallLineRate = 0.0
$overallBranchRate = 0.0
$unlocatedLinesCovered = 0.0
$unlocatedLinesValid = 0.0
$unlocatedBranchesCovered = 0.0
$unlocatedBranchesValid = 0.0
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

    $reportHasLineData = $false
    $reportHasBranchData = $false

    foreach ($package in $cobertura.coverage.packages.package) {
        foreach ($class in $package.classes.class) {
            $className = $class.name
            $fileName  = $class.filename

            $classLines = @($class.lines.line | Where-Object { $null -ne $_ })
            if ($classLines.Count -eq 0) {
                $classLines = @($class.methods.method | ForEach-Object { $_.lines.line })
            }
            foreach ($line in $classLines) {
                $lineNo = $line.number
                $lineKey = "$fileName|$lineNo"
                $hits = [int]$line.hits
                $reportHasLineData = $true
                if ($overallLineHits.ContainsKey($lineKey)) {
                    $overallLineHits[$lineKey] = [Math]::Max($overallLineHits[$lineKey], $hits)
                } else {
                    $overallLineHits[$lineKey] = $hits
                }

                if (($line.branch -eq 'true') -and $line.'condition-coverage' -and ($line.'condition-coverage' -match '\((\d+)/(\d+)\)')) {
                    $covered = [int]$Matches[1]
                    $total = [int]$Matches[2]
                    $reportHasBranchData = $true
                    if ($overallBranchData.ContainsKey($lineKey)) {
                        $existingCovered = $overallBranchData[$lineKey].Covered
                        $existingTotal = $overallBranchData[$lineKey].Total
                        if ($existingTotal -ne $total) {
                            Write-Warning ("Branch total mismatch for {0} at line {1}: {2} vs {3}" -f $fileName, $lineNo, $existingTotal, $total)
                        }
                        $mergedTotal = [Math]::Max($existingTotal, $total)
                        # Cobertura does not identify which outcomes were covered, so summing
                        # overlapping reports could count the same branch more than once.
                        $mergedCovered = [Math]::Min([Math]::Max($existingCovered, $covered), $mergedTotal)
                        $overallBranchData[$lineKey] = @{ Covered = $mergedCovered; Total = $mergedTotal }
                    } else {
                        $overallBranchData[$lineKey] = @{ Covered = $covered; Total = $total }
                    }
                }
            }

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

    if (-not $reportHasLineData) {
        if ($null -ne $cobertura.coverage.'lines-covered' -and $null -ne $cobertura.coverage.'lines-valid') {
            $unlocatedLinesCovered += [double]$cobertura.coverage.'lines-covered'
            $unlocatedLinesValid += [double]$cobertura.coverage.'lines-valid'
        } elseif ($cobertura.coverage.'line-rate') {
            $fallbackLineRates.Add([double]$cobertura.coverage.'line-rate')
        }
    }
    if (-not $reportHasBranchData) {
        if ($null -ne $cobertura.coverage.'branches-covered' -and $null -ne $cobertura.coverage.'branches-valid') {
            $unlocatedBranchesCovered += [double]$cobertura.coverage.'branches-covered'
            $unlocatedBranchesValid += [double]$cobertura.coverage.'branches-valid'
        } elseif ($cobertura.coverage.'branch-rate') {
            $fallbackBranchRates.Add([double]$cobertura.coverage.'branch-rate')
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

$overallCoveredLines = $unlocatedLinesCovered
$overallTotalLines = $unlocatedLinesValid
if ($overallLineHits.Count -gt 0) {
    $overallCoveredLines += ($overallLineHits.Values | Where-Object { $_ -gt 0 } | Measure-Object).Count
    $overallTotalLines += $overallLineHits.Count
}
if ($overallTotalLines -gt 0) {
    $overallLineRate = [double]$overallCoveredLines / [double]$overallTotalLines
} elseif ($fallbackLineRates.Count -gt 0) {
    $overallLineRate = ($fallbackLineRates | Measure-Object -Average).Average
} else {
    $overallLineRate = 0.0
}

$overallCoveredBranches = $unlocatedBranchesCovered
$overallTotalBranches = $unlocatedBranchesValid
if ($overallBranchData.Count -gt 0) {
    foreach ($branch in $overallBranchData.Values) {
        $overallCoveredBranches += $branch.Covered
        $overallTotalBranches += $branch.Total
    }
}
if ($overallTotalBranches -gt 0) {
    $overallBranchRate = [double]$overallCoveredBranches / [double]$overallTotalBranches
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
