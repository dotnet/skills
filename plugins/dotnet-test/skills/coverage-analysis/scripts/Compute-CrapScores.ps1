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
#   - Hotspot rows (top N by CRAP score) as a JSON array to stdout (HOTSPOTS:<json>)
#   - Summary counts as TOTAL_METHODS:<n> and FLAGGED_METHODS:<n>

param(
    [Parameter(Mandatory)][string[]]$CoberturaPath,
    [int]$CrapThreshold = 30,
    [int]$TopN = 10
)

# Merge methods across all Cobertura files using a stable key (Class|Method|Signature|File).
# Line hits are accumulated so a line is counted as covered if any test project covered it.
$methodMap = @{}

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

Write-Host "TOTAL_METHODS:$($results.Count)"
Write-Host "FLAGGED_METHODS:$($flagged.Count)"
if ($hotspots) {
    Write-Output "HOTSPOTS:$(@($hotspots) | ConvertTo-Json -Compress)"
} else {
    Write-Output "HOTSPOTS:[]"
}
