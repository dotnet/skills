<#
.SYNOPSIS
Extract Cobertura method coverage without inventing missing measurements.
.DESCRIPTION
Legacy output is a JSON array; diagnostics and summaries go to stderr.
Use -OutputFormat json-v1 for provenance, null/unknown metrics and branch bounds.
Merging is scoped to reported module/file/class/signature identities, not an
attestation of source revision. Duplicate report bytes are idempotent.
Exit codes: 0 complete, 2 invalid input, 3 incomplete/unrepresentable evidence.
#>
param(
    [Parameter(Mandatory=$true)][string[]]$CoberturaPath,
    [ValidateRange(0,100)][int]$CoverageThreshold = 80,
    [ValidateRange(0,100)][int]$BranchThreshold = 70,
    [ValidateSet('uncovered', 'below-threshold', 'all')][string]$Filter = 'all',
    [ValidateSet('legacy', 'json-v1')][string]$OutputFormat = 'legacy'
)

try {
    . "$PSScriptRoot\CoverageData.ps1"
} catch {
    $diagnostic = @{ code = 'DEPENDENCY_UNAVAILABLE'; severity = 'error'; message = $_.Exception.Message; path = "$PSScriptRoot\CoverageData.ps1" }
    [Console]::Error.WriteLine("$($diagnostic.code): $($diagnostic.message)")
    if ($OutputFormat -eq 'json-v1') {
        @{
            schema_version = '1'; tool = 'Extract-MethodCoverage'; tool_version = '1.0'; status = 'error'
            scope = @{}; inputs = @(); diagnostics = @($diagnostic); data = @{}
        } | ConvertTo-Json -Depth 5
    }
    exit 2
}
$scope = @{ identity = 'reported-module-file-class-signature'; source_revision = $null; filter = $Filter }
try {
    $coverage = Get-CoverageData $CoberturaPath
} catch {
    $diagnostics = @([pscustomobject]@{ code = 'INVALID_INPUT'; severity = 'error'; message = $_.Exception.Message })
    Write-CoverageDiagnostics $diagnostics
    if ($OutputFormat -eq 'json-v1') { Write-CoverageJson 'Extract-MethodCoverage' 'error' $scope @() $diagnostics @{} }
    exit 2
}
$partial = -not $coverage.Methods.Count -or @($coverage.Methods | Where-Object {
    $_.LineCoverageStatus -eq 'unknown' -or $_.BranchCoverageStatus -in @('unknown', 'bounded') -or $_.ComplexityStatus -eq 'unknown'
}).Count -gt 0
$methods = @($coverage.Methods | Where-Object {
    if ($Filter -eq 'all') { return $true }
    if ($_.LineCoverageStatus -eq 'unknown' -or $_.BranchCoverageStatus -in @('unknown', 'bounded')) { return $true }
    if ($Filter -eq 'uncovered') { return $_.LineCoverage -eq 0 }
    return (100.0 * $_.CoveredLines / $_.TotalLines) -lt $CoverageThreshold -or
        ($_.TotalBranches -gt 0 -and (100.0 * $_.CoveredBranches / $_.TotalBranches) -lt $BranchThreshold)
} | Sort-Object -Property @{Expression='UncoveredLines';Descending=$true}, @{Expression='LineCoverage';Descending=$false}, Class, Method, File, Signature)
Write-CoverageDiagnostics $coverage.Diagnostics
if ($OutputFormat -eq 'json-v1') {
    Write-CoverageJson 'Extract-MethodCoverage' $(if ($partial) { 'partial' } else { 'complete' }) $scope $coverage.Inputs $coverage.Diagnostics @{
        methods = $methods; summary = $coverage.Summary
    }
} elseif (-not $partial) {
    $legacy = @($methods | Select-Object Class, Method, Signature, File, Complexity, LineCoverage,
        @{Name='BranchCoverage';Expression={if ($_.BranchCoverageStatus -eq 'not-applicable') { 0 } else { $_.BranchCoverage }}},
        CoveredLines, TotalLines, UncoveredLines, CoveredBranches, TotalBranches)
    ConvertTo-Json -InputObject $legacy -Depth 5
    [Console]::Error.WriteLine("METHODS_FILTERED:$($methods.Count)")
    [Console]::Error.WriteLine("UNCOVERED_METHODS:$(@($methods | Where-Object { $_.LineCoverageStatus -eq 'exact' -and $_.LineCoverage -eq 0 }).Count)")
}
if ($partial) { exit 3 }
exit 0
