param(
    [Parameter(Mandatory=$true)]
    [string[]]$CoberturaPath,
    
    [Parameter(Mandatory=$false)]
    [int]$CoverageThreshold = 80,

    [Parameter(Mandatory=$false)]
    [int]$BranchThreshold = 70,
    
    [Parameter(Mandatory=$false)]
    [ValidateSet('uncovered', 'below-threshold', 'all')]
    [string]$Filter = 'all'
)

<#
.SYNOPSIS
Extract method-level coverage from Cobertura XML and output as JSON.

.DESCRIPTION
Parses one or more Cobertura code coverage XML files and extracts per-method coverage metrics:
- Method name and class
- Line coverage percentage
- Branch coverage percentage  
- Lines covered / total
- Branches covered / total
- Complexity (if available)

When multiple files are provided, line hits are merged across files so a line is counted
as covered if any test project covered it.

Filters by coverage status (uncovered, below threshold, or all).
Output is JSON for easy post-processing into tables, CSV, or other formats.

.PARAMETER CoberturaPath
Path(s) to Cobertura coverage.cobertura.xml file(s). Accepts multiple paths for multi-test-project merging.

.PARAMETER CoverageThreshold
Minimum acceptable line coverage percentage. Methods below this threshold are flagged (default: 80).

.PARAMETER BranchThreshold
Minimum acceptable branch coverage percentage for methods that contain branches (default: 70).

.PARAMETER Filter
Which methods to include:
  'uncovered' - methods with 0% coverage only
  'below-threshold' - methods with line coverage < CoverageThreshold OR branch coverage < BranchThreshold (for methods with branches)
  'all' - all methods (default)

.EXAMPLE
PS> & .\Extract-MethodCoverage.ps1 -CoberturaPath "coverage.cobertura.xml" -CoverageThreshold 80 -BranchThreshold 70 -Filter uncovered
Outputs a JSON array of uncovered methods.

.EXAMPLE
PS> & .\Extract-MethodCoverage.ps1 -CoberturaPath @("tests1/coverage.cobertura.xml","tests2/coverage.cobertura.xml")
Merges coverage from multiple test projects and outputs combined method-level metrics.

.OUTPUTS
Writes JSON array to stdout.
Sets exit code 0 on success, 2 on missing/invalid file.
#>

foreach ($p in $CoberturaPath) {
    if (-not (Test-Path $p)) {
        Write-Error "Cobertura file not found: $p"
        exit 2
    }
}

# Merge methods across all Cobertura files using a stable key (Class|Method|Signature|File).
# Line hits and branch data are accumulated so coverage reflects all test projects.
$methodMap = @{}

foreach ($p in $CoberturaPath) {
    try {
        [xml]$xml = Get-Content $p -Encoding UTF8 -ErrorAction Stop
    } catch {
        Write-Error "Failed to parse Cobertura XML: $_"
        exit 2
    }

    foreach ($package in $xml.coverage.packages.package) {
        foreach ($class in $package.classes.class) {
            $className = $class.name
            $classFilename = $class.filename
            
            foreach ($method in $class.methods.method) {
                $key = "$className|$($method.name)|$($method.signature)|$classFilename"
                
                if (-not $methodMap.ContainsKey($key)) {
                    $complexity = if ($null -ne $method.complexity) { [int]$method.complexity } else { 1 }
                    if ($complexity -lt 1) { $complexity = 1 }
                    $methodMap[$key] = @{
                        Class          = $className
                        Method         = $method.name
                        Signature      = $method.signature
                        File           = $classFilename
                        Complexity     = $complexity
                        LineHits       = @{}
                        BranchData     = @{}
                    }
                }

                # Accumulate line hits across files
                foreach ($line in $method.lines.line) {
                    $lineNo = $line.number
                    $hits   = [int]$line.hits
                    if ($methodMap[$key].LineHits.ContainsKey($lineNo)) {
                        $methodMap[$key].LineHits[$lineNo] += $hits
                    } else {
                        $methodMap[$key].LineHits[$lineNo] = $hits
                    }

                    # Accumulate branch data
                    if ($line.branch -eq 'true' -and $line.'condition-coverage') {
                        if ($line.'condition-coverage' -match '\((\d+)/(\d+)\)') {
                            $covered = [int]$Matches[1]
                            $total   = [int]$Matches[2]
                            if ($methodMap[$key].BranchData.ContainsKey($lineNo)) {
                                # Merge branch coverage across files by accumulating covered branches (capped at total)
                                $existingCovered = $methodMap[$key].BranchData[$lineNo].Covered
                                $existingTotal = $methodMap[$key].BranchData[$lineNo].Total
                                if ($existingTotal -ne $total) {
                                    Write-Warning ("Branch total mismatch for {0} at line {1}: {2} vs {3}" -f $key, $lineNo, $existingTotal, $total)
                                }
                                $mergedTotal = [Math]::Max($existingTotal, $total)
                                $mergedCovered = [Math]::Min($existingCovered + $covered, $mergedTotal)
                                $methodMap[$key].BranchData[$lineNo] = @{ Covered = $mergedCovered; Total = $mergedTotal }
                            } else {
                                $methodMap[$key].BranchData[$lineNo] = @{ Covered = $covered; Total = $total }
                            }
                        }
                    }
                }
            }
        }
    }
}

$methods = [System.Collections.Generic.List[PSCustomObject]]::new()

foreach ($entry in $methodMap.Values) {
    $totalLines = $entry.LineHits.Count
    $coveredLineCount = ($entry.LineHits.Values | Where-Object { $_ -gt 0 } | Measure-Object).Count
    $lineCoveragePercent = if ($totalLines -gt 0) { [math]::Round(($coveredLineCount / $totalLines) * 100, 1) } else { 0 }

    $branchesTotal = 0
    $branchesCovered = 0
    foreach ($bd in $entry.BranchData.Values) {
        $branchesCovered += $bd.Covered
        $branchesTotal   += $bd.Total
    }
    $branchCoveragePercent = if ($branchesTotal -gt 0) { [math]::Round(($branchesCovered / $branchesTotal) * 100, 1) } else { 0 }

    # Apply filter
    if ($Filter -eq 'uncovered' -and $lineCoveragePercent -gt 0) { continue }
    if ($Filter -eq 'below-threshold') {
        $lineOk = $lineCoveragePercent -ge $CoverageThreshold
        $branchOk = ($branchesTotal -eq 0) -or ($branchCoveragePercent -ge $BranchThreshold)
        if ($lineOk -and $branchOk) { continue }
    }
    
    $methods.Add([PSCustomObject]@{
        Class               = $entry.Class
        Method              = $entry.Method
        Signature           = $entry.Signature
        File                = $entry.File
        Complexity          = $entry.Complexity
        LineCoverage        = $lineCoveragePercent
        BranchCoverage      = $branchCoveragePercent
        CoveredLines        = $coveredLineCount
        TotalLines          = $totalLines
        UncoveredLines      = ($totalLines - $coveredLineCount)
        CoveredBranches     = $branchesCovered
        TotalBranches       = $branchesTotal
    })
}
# Sort by uncovered lines descending, then by line coverage ascending
$sorted = $methods | Sort-Object -Property @{Expression='UncoveredLines';Descending=$true}, @{Expression='LineCoverage';Descending=$false}, Class, Method

# Output as JSON (empty array guard for zero results)
if ($sorted.Count -eq 0) {
    Write-Output "[]"
} else {
    $json = @($sorted) | ConvertTo-Json
    Write-Output $json
}

# Summary
Write-Host "METHODS_FILTERED:$($methods.Count)" -ForegroundColor Green
$uncovered = $methods | Where-Object { $_.LineCoverage -eq 0 } | Measure-Object | Select-Object -ExpandProperty Count
Write-Host "UNCOVERED_METHODS:$uncovered" -ForegroundColor $(if ($uncovered -gt 0) { 'Yellow' } else { 'Green' })
exit 0
