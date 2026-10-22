<#
.SYNOPSIS
Compute CRAP = complexity^2 * (1 - line coverage)^3 + complexity.
.DESCRIPTION
Missing complexity or executable lines yield unknown, never complexity=1/coverage=0.
Legacy output retains OVERALL_*, TOTAL_METHODS, FLAGGED_METHODS and HOTSPOTS markers
only when all requested measurements are representable. -OutputFormat json-v1
emits one provenance-bearing JSON document. Diagnostics go to stderr.
Thresholds and hotspot ordering use raw scores; only displayed scores are rounded.
Exit codes: 0 complete, 2 invalid input, 3 incomplete/unrepresentable evidence.
#>
param(
    [Parameter(Mandatory)][string[]]$CoberturaPath,
    [ValidateRange(0,[int]::MaxValue)][int]$CrapThreshold = 30,
    [ValidateRange(1,[int]::MaxValue)][int]$TopN = 10,
    [ValidateSet('legacy', 'json-v1')][string]$OutputFormat = 'legacy'
)

try {
    . "$PSScriptRoot\CoverageData.ps1"
} catch {
    $diagnostic = @{ code = 'DEPENDENCY_UNAVAILABLE'; severity = 'error'; message = $_.Exception.Message; path = "$PSScriptRoot\CoverageData.ps1" }
    [Console]::Error.WriteLine("$($diagnostic.code): $($diagnostic.message)")
    if ($OutputFormat -eq 'json-v1') {
        @{
            schema_version = '1'; tool = 'Compute-CrapScores'; tool_version = '1.0'; status = 'error'
            scope = @{}; inputs = @(); diagnostics = @($diagnostic); data = @{}
        } | ConvertTo-Json -Depth 5
    }
    exit 2
}
$scope = @{ identity = 'reported-module-file-class-signature'; source_revision = $null; top = $TopN; crap_threshold = $CrapThreshold }
try {
    $coverage = Get-CoverageData $CoberturaPath
} catch {
    $diagnostics = @([pscustomobject]@{ code = 'INVALID_INPUT'; severity = 'error'; message = $_.Exception.Message })
    Write-CoverageDiagnostics $diagnostics
    if ($OutputFormat -eq 'json-v1') { Write-CoverageJson 'Compute-CrapScores' 'error' $scope @() $diagnostics @{} }
    exit 2
}
foreach ($method in $coverage.Methods) {
    $score = if ($null -ne $method.Complexity -and $method.LineCoverageStatus -eq 'exact') {
        [math]::Pow($method.Complexity, 2) * [math]::Pow(1 - $method.CoveredLines / [double]$method.TotalLines, 3) + $method.Complexity
    } else { $null }
    $method | Add-Member -NotePropertyName CrapScore -NotePropertyValue $score
}
$ranked = @($coverage.Methods | Where-Object { $null -ne $_.CrapScore } |
    Sort-Object -Property @{Expression='CrapScore';Descending=$true}, Class, Method, File, Signature)
$hotspots = @($ranked | Select-Object -First $TopN)
$unknown = @($coverage.Methods | Where-Object { $null -eq $_.CrapScore }).Count
$coverage.Summary.flagged_methods = @($ranked | Where-Object { $_.CrapScore -gt $CrapThreshold }).Count
$coverage.Summary.unknown_crap_methods = $unknown
foreach ($method in $ranked) {
    $method.CrapScore = [math]::Round($method.CrapScore, 2)
}
foreach ($metric in @('line', 'branch')) {
    if ($coverage.Summary["overall_${metric}_coverage_status"] -in @('unknown', 'bounded')) {
        Add-CoverageDiagnostic $coverage.Diagnostics 'AGGREGATE_UNKNOWN' "Overall $metric coverage cannot be derived from overlapping report counters." ''
    }
}
$partial = -not $coverage.Methods.Count -or $unknown -gt 0 -or
    $coverage.Summary.overall_line_coverage_status -in @('unknown', 'bounded') -or
    $coverage.Summary.overall_branch_coverage_status -in @('unknown', 'bounded')
Write-CoverageDiagnostics $coverage.Diagnostics
if ($OutputFormat -eq 'json-v1') {
    Write-CoverageJson 'Compute-CrapScores' $(if ($partial) { 'partial' } else { 'complete' }) $scope $coverage.Inputs $coverage.Diagnostics @{
        methods = $coverage.Methods; hotspots = $hotspots; summary = $coverage.Summary
    }
} elseif (-not $partial) {
    $line = if ($coverage.Summary.overall_line_coverage_status -eq 'not-applicable') { 0 } else { $coverage.Summary.overall_line_coverage }
    $branch = if ($coverage.Summary.overall_branch_coverage_status -eq 'not-applicable') { 0 } else { $coverage.Summary.overall_branch_coverage }
    Write-Output "OVERALL_LINE_COVERAGE:$line"
    Write-Output "OVERALL_BRANCH_COVERAGE:$branch"
    Write-Output "TOTAL_METHODS:$($coverage.Methods.Count)"
    Write-Output "FLAGGED_METHODS:$($coverage.Summary.flagged_methods)"
    $legacy = @($hotspots | Select-Object Class, Method, Signature, File, TotalLines, CoveredLines, LineCoverage, Complexity, CrapScore)
    Write-Output "HOTSPOTS:$(ConvertTo-Json -InputObject $legacy -Compress)"
}
if ($partial) { exit 3 }
exit 0
