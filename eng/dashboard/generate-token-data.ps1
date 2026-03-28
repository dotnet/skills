<#
.SYNOPSIS
    Generates dummy token usage data for the dashboard.

.DESCRIPTION
    Creates a token-usage.json file with realistic dummy data spanning 14 days,
    including both scheduled evaluation runs and PR-triggered runs across all
    plugins. Used for dashboard development and testing.
#>
param(
    [string]$OutputDir = (Join-Path $PSScriptRoot "data")
)

$ErrorActionPreference = "Stop"
New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

$plugins = [ordered]@{
    "dotnet"         = @("csharp-scripts", "dotnet-pinvoke", "nuget-trusted-publishing")
    "dotnet-ai"      = @("technology-selection")
    "dotnet-data"    = @("optimizing-ef-core-queries")
    "dotnet-diag"    = @("analyzing-dotnet-performance", "android-tombstone-symbolication", "dotnet-trace-collect", "dump-collect", "microbenchmarking")
    "dotnet-maui"    = @("dotnet-maui-doctor")
    "dotnet-msbuild" = @("binlog-failure-analysis", "build-parallelism", "build-perf-diagnostics", "incremental-build", "msbuild-antipatterns", "msbuild-modernization")
    "dotnet-test"    = @("migrate-vstest-to-mtp", "run-tests", "writing-mstest-tests")
    "dotnet-upgrade" = @("dotnet-aot-compat", "migrate-dotnet9-to-dotnet10", "migrate-nullable-references")
}

$random = [System.Random]::new(42) # Fixed seed for reproducibility

# Generate 14 days of data (March 3-16, 2026)
$baseDate = [DateTimeOffset]::new(2026, 3, 3, 8, 0, 0, [TimeSpan]::Zero)
$entries = [System.Collections.Generic.List[object]]::new()
$pluginNames = @($plugins.Keys)
$pluginIndex = 0

# --- Scheduled runs: cycle through plugins, 2-3 per day ---
for ($day = 0; $day -lt 14; $day++) {
    $dayDate = $baseDate.AddDays($day)
    $runsToday = $random.Next(2, 4)

    for ($r = 0; $r -lt $runsToday; $r++) {
        $plugin = $pluginNames[$pluginIndex % $pluginNames.Count]
        $pluginIndex++
        $skills = $plugins[$plugin]
        $runTime = $dayDate.AddHours($random.Next(0, 12)).AddMinutes($random.Next(0, 60))

        foreach ($skill in $skills) {
            $tokensIn = $random.Next(30000, 150000)
            $tokensOut = $random.Next(5000, 35000)
            # Cached read tokens are typically 30-70% of input tokens (prompt caching)
            $cacheRead = [math]::Round($tokensIn * ($random.Next(30, 71) / 100.0))
            # Cache write tokens are a small portion of non-cached input
            $cacheWrite = [math]::Round(($tokensIn - $cacheRead) * ($random.Next(5, 25) / 100.0))
            # Judge tokens: typically 5-20% of agent tokens
            $judgeIn = [math]::Round($tokensIn * ($random.Next(5, 21) / 100.0))
            $judgeOut = [math]::Round($tokensOut * ($random.Next(3, 15) / 100.0))
            $judgeCacheRead = [math]::Round($judgeIn * ($random.Next(20, 60) / 100.0))
            $judgeCacheWrite = [math]::Round(($judgeIn - $judgeCacheRead) * ($random.Next(5, 20) / 100.0))
            $entries.Add(@{
                date              = $runTime.ToUnixTimeMilliseconds()
                source            = "scheduled"
                plugin            = $plugin
                skill             = $skill
                tokensIn          = $tokensIn
                tokensOut         = $tokensOut
                cacheReadTokens   = $cacheRead
                cacheWriteTokens  = $cacheWrite
                totalTokens       = $tokensIn + $tokensOut
                judgeTokensIn     = $judgeIn
                judgeTokensOut    = $judgeOut
                judgeCacheRead    = $judgeCacheRead
                judgeCacheWrite   = $judgeCacheWrite
                judgeTotalTokens  = $judgeIn + $judgeOut
                model             = "gpt-4o"
            })
        }
    }
}

# --- PR runs: 6 PRs spread across the 14 days ---
$prConfigs = @(
    @{ day = 1;  plugins = @("dotnet");                    prNum = 1234; title = "Add P/Invoke marshallers for new API surface" }
    @{ day = 3;  plugins = @("dotnet-msbuild", "dotnet-test"); prNum = 1241; title = "Update MSBuild targets and test infrastructure" }
    @{ day = 5;  plugins = @("dotnet-upgrade");            prNum = 1248; title = "Fix nullable reference migration edge cases" }
    @{ day = 8;  plugins = @("dotnet-diag", "dotnet-data"); prNum = 1255; title = "Improve perf diagnostics and EF Core query analysis" }
    @{ day = 10; plugins = @("dotnet-msbuild");            prNum = 1260; title = "Add build-parallelism skill improvements" }
    @{ day = 12; plugins = @("dotnet", "dotnet-ai");       prNum = 1267; title = "Refactor AI technology selection logic" }
)

foreach ($pr in $prConfigs) {
    $dayDate = $baseDate.AddDays($pr.day)
    foreach ($plugin in $pr.plugins) {
        $skills = $plugins[$plugin]
        $runTime = $dayDate.AddHours($random.Next(14, 20)).AddMinutes($random.Next(0, 60))

        foreach ($skill in $skills) {
            $tokensIn = $random.Next(25000, 120000)
            $tokensOut = $random.Next(4000, 28000)
            # PR runs may have lower cache hit rates (less repetitive prompts)
            $cacheRead = [math]::Round($tokensIn * ($random.Next(15, 55) / 100.0))
            $cacheWrite = [math]::Round(($tokensIn - $cacheRead) * ($random.Next(10, 30) / 100.0))
            # Judge tokens: typically 5-20% of agent tokens
            $judgeIn = [math]::Round($tokensIn * ($random.Next(5, 21) / 100.0))
            $judgeOut = [math]::Round($tokensOut * ($random.Next(3, 15) / 100.0))
            $judgeCacheRead = [math]::Round($judgeIn * ($random.Next(15, 50) / 100.0))
            $judgeCacheWrite = [math]::Round(($judgeIn - $judgeCacheRead) * ($random.Next(5, 20) / 100.0))
            $entries.Add(@{
                date              = $runTime.ToUnixTimeMilliseconds()
                source            = "pr"
                prNumber          = $pr.prNum
                prTitle           = $pr.title
                plugin            = $plugin
                skill             = $skill
                tokensIn          = $tokensIn
                tokensOut         = $tokensOut
                cacheReadTokens   = $cacheRead
                cacheWriteTokens  = $cacheWrite
                totalTokens       = $tokensIn + $tokensOut
                judgeTokensIn     = $judgeIn
                judgeTokensOut    = $judgeOut
                judgeCacheRead    = $judgeCacheRead
                judgeCacheWrite   = $judgeCacheWrite
                judgeTotalTokens  = $judgeIn + $judgeOut
                model             = "gpt-4o"
            })
        }
    }
}

# Write token-usage.json
$output = @{ entries = @($entries) }
$outputJson = $output | ConvertTo-Json -Depth 5
$outputFile = Join-Path $OutputDir "token-usage.json"
$outputJson | Out-File -FilePath $outputFile -Encoding utf8

# Create an empty components.json if none exists (evaluations tab shows clean "no data")
$componentsFile = Join-Path $OutputDir "components.json"
if (-not (Test-Path $componentsFile)) {
    "[]" | Out-File -FilePath $componentsFile -Encoding utf8
}

Write-Host "[OK] Generated token-usage.json with $($entries.Count) entries"
Write-Host "   Output: $outputFile"
Write-Host "   Scheduled: $(($entries | Where-Object { $_.source -eq 'scheduled' }).Count)"
Write-Host "   PR: $(($entries | Where-Object { $_.source -eq 'pr' }).Count)"
