#!/usr/bin/env pwsh
#requires -Version 7
[CmdletBinding()]
param(
    [switch] $KeepTestRepositories
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$sourceScript = Join-Path $PSScriptRoot 'Sync-PluginVersions.ps1'
$testRoot = Join-Path ([IO.Path]::GetTempPath()) "skills-version-tests-$([guid]::NewGuid().ToString('N'))"
$script:assertions = 0

function Invoke-Git {
    param(
        [string] $Repository,
        [Parameter(ValueFromRemainingArguments)]
        [string[]] $Arguments
    )
    $output = git -C $Repository @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "git $($Arguments -join ' ') failed in '$Repository'."
    }
    return $output
}

function Assert-Equal {
    param($Expected, $Actual, [string] $Message)
    $script:assertions++
    if ($Expected -ne $Actual) {
        throw "$Message Expected '$Expected', got '$Actual'."
    }
}

function Set-JsonVersion {
    param([string] $Path, [string] $Version)
    $text = [IO.File]::ReadAllText($Path)
    $updated = [regex]::Replace($text, '("version"\s*:\s*")[^"]+(")', "`${1}$Version`${2}", 1)
    [IO.File]::WriteAllText($Path, $updated)
}

function New-TestRepository {
    param([string] $Name, [switch] $LegacyHistory)
    $repo = Join-Path $testRoot $Name
    [void](New-Item -ItemType Directory -Path (Join-Path $repo 'eng/version') -Force)
    [void](New-Item -ItemType Directory -Path (Join-Path $repo 'plugins/sample/.codex-plugin') -Force)
    [void](New-Item -ItemType Directory -Path (Join-Path $repo 'plugins/sample/.claude-plugin') -Force)
    [void](New-Item -ItemType Directory -Path (Join-Path $repo 'plugins/sample/skills/example') -Force)
    $testScript = Join-Path $repo 'eng/version/Sync-PluginVersions.ps1'
    Copy-Item -LiteralPath $sourceScript -Destination $testScript
    if ($LegacyHistory) {
        $text = [IO.File]::ReadAllText($testScript)
        [IO.File]::WriteAllText($testScript, $text.Replace('release-checkpoint-validation-v1', 'legacy-checkpoint-versioning'))
    }

    @'
{
  "$schema": "https://raw.githubusercontent.com/dotnet/Nerdbank.GitVersioning/main/src/NerdBank.GitVersioning/version.schema.json",
  "version": "0.1",
  "pathFilters": [
    ".",
    ":!plugin.json",
    ":!.codex-plugin/plugin.json",
    ":!.claude-plugin/plugin.json",
    ":!version.json"
  ]
}
'@ | Set-Content -LiteralPath (Join-Path $repo 'plugins/sample/version.json')

    @'
{
  "name": "sample",
  "version": "0.1.4",
  "description": "Test plugin.",
  "skills": ["./skills/"]
}
'@ | Set-Content -LiteralPath (Join-Path $repo 'plugins/sample/plugin.json')
    Copy-Item -LiteralPath (Join-Path $repo 'plugins/sample/plugin.json') `
        -Destination (Join-Path $repo 'plugins/sample/.codex-plugin/plugin.json')
    Copy-Item -LiteralPath (Join-Path $repo 'plugins/sample/plugin.json') `
        -Destination (Join-Path $repo 'plugins/sample/.claude-plugin/plugin.json')
    'initial content' | Set-Content -LiteralPath (Join-Path $repo 'plugins/sample/skills/example/SKILL.md')

    [void](Invoke-Git $repo init -b main)
    [void](Invoke-Git $repo config user.name 'Version Test')
    [void](Invoke-Git $repo config user.email 'version-test@example.com')
    [void](Invoke-Git $repo add .)
    [void](Invoke-Git $repo commit -m 'Initial release checkpoint')
    if ($LegacyHistory) {
        Copy-Item -LiteralPath $sourceScript -Destination $testScript -Force
    }
    return $repo
}

function Add-PluginContent {
    param([string] $Repository, [string] $RelativePath, [string] $Content, [string] $Message)
    $path = Join-Path $Repository "plugins/sample/$RelativePath"
    [void](New-Item -ItemType Directory -Path (Split-Path $path -Parent) -Force)
    $Content | Set-Content -LiteralPath $path
    [void](Invoke-Git $Repository add "plugins/sample/$RelativePath")
    [void](Invoke-Git $Repository commit -m $Message)
}

function Set-ManifestVersions {
    param([string] $Repository, [string] $Version)
    Set-JsonVersion -Path (Join-Path $Repository 'plugins/sample/plugin.json') -Version $Version
    Set-JsonVersion -Path (Join-Path $Repository 'plugins/sample/.codex-plugin/plugin.json') -Version $Version
    Set-JsonVersion -Path (Join-Path $Repository 'plugins/sample/.claude-plugin/plugin.json') -Version $Version
}

function Commit-ManifestVersions {
    param([string] $Repository, [string] $Version)
    Set-ManifestVersions -Repository $Repository -Version $Version
    [void](Invoke-Git $Repository add plugins/sample/plugin.json plugins/sample/.codex-plugin/plugin.json plugins/sample/.claude-plugin/plugin.json)
    [void](Invoke-Git $Repository commit -m "Stamp sample $Version")
}

function Invoke-Sync {
    param(
        [string] $Repository,
        [string] $BaseCommit,
        [string] $HeadCommit,
        [switch] $PredictMerge,
        [switch] $Write
    )
    $parameters = @{ OnlyChanged = $true }
    if ($BaseCommit) { $parameters.BaseCommit = $BaseCommit }
    if ($HeadCommit) { $parameters.HeadCommit = $HeadCommit }
    if ($PredictMerge) { $parameters.PredictMerge = $true }
    if ($Write) { $parameters.Write = $true }

    Push-Location $Repository
    try {
        $json = & (Join-Path $Repository 'eng/version/Sync-PluginVersions.ps1') @parameters
        return @($json | ConvertFrom-Json)
    }
    finally {
        Pop-Location
    }
}

function Test-BatchedChangesAdvanceOnce {
    $repo = New-TestRepository 'batched-changes'
    Add-PluginContent $repo 'skills/example/SKILL.md' 'change one' 'First content change'
    Add-PluginContent $repo 'skills/second/SKILL.md' 'change two' 'Second content change'

    $report = @(Invoke-Sync $repo)
    Assert-Equal 1 $report.Count 'Batched changes must produce one release.'
    Assert-Equal '0.1.5' $report[0].computed 'Batched changes must advance one patch.'
    Assert-Equal 2 @($report[0].commits).Count 'Both first-parent changes must be attributed.'

    [void](Invoke-Sync $repo -Write)
    [void](Invoke-Git $repo add plugins/sample/plugin.json plugins/sample/.codex-plugin/plugin.json plugins/sample/.claude-plugin/plugin.json)
    [void](Invoke-Git $repo commit -m 'Publish batched changes')
    Assert-Equal 0 @(Invoke-Sync $repo).Count 'Published content must not drift.'
}

function Test-GraphOnlyMergeDoesNotAdvance {
    $repo = New-TestRepository 'graph-only-merge'
    [void](Invoke-Git $repo branch stale)
    Add-PluginContent $repo 'skills/example/SKILL.md' 'published content' 'Change plugin content'
    Commit-ManifestVersions $repo '0.1.5'

    [void](Invoke-Git $repo switch stale)
    [void](Invoke-Git $repo merge main --no-ff -m 'Merge main into stale branch')
    'branch-only note' | Set-Content -LiteralPath (Join-Path $repo 'branch-note.txt')
    [void](Invoke-Git $repo add branch-note.txt)
    [void](Invoke-Git $repo commit -m 'Add branch-only note')
    [void](Invoke-Git $repo switch main)
    [void](Invoke-Git $repo merge stale --no-ff -m 'Merge stale branch')

    Assert-Equal 0 @(Invoke-Sync $repo).Count 'A graph-only merge must not advance the plugin.'
}

function Test-RevertToCheckpointDoesNotAdvance {
    $repo = New-TestRepository 'revert'
    Add-PluginContent $repo 'skills/example/SKILL.md' 'temporary content' 'Temporary plugin change'
    Add-PluginContent $repo 'skills/example/SKILL.md' 'initial content' 'Revert plugin change'
    Assert-Equal 0 @(Invoke-Sync $repo).Count 'Content restored to the checkpoint must not advance.'
}

function Test-BaseResetStartsAtZero {
    $repo = New-TestRepository 'base-reset'
    Set-JsonVersion -Path (Join-Path $repo 'plugins/sample/version.json') -Version '0.2'
    [void](Invoke-Git $repo add plugins/sample/version.json)
    [void](Invoke-Git $repo commit -m 'Start sample 0.2 release line')

    $report = @(Invoke-Sync $repo)
    Assert-Equal 1 $report.Count 'A release-base change must drift.'
    Assert-Equal '0.2.0' $report[0].computed 'A release-base change must reset patch to zero.'
    Assert-Equal $true $report[0].baseChanged 'A release-base change must be identified.'
}

function Test-MajorBaseResetStartsAtZero {
    $repo = New-TestRepository 'major-base-reset'
    Set-JsonVersion -Path (Join-Path $repo 'plugins/sample/version.json') -Version '1.0'
    [void](Invoke-Git $repo add plugins/sample/version.json)
    [void](Invoke-Git $repo commit -m 'Start sample 1.0 release line')

    $report = @(Invoke-Sync $repo)
    Assert-Equal 1 $report.Count 'A major release-base change must drift.'
    Assert-Equal '1.0.0' $report[0].computed 'A major release-base change must reset patch to zero.'
    Assert-Equal $true $report[0].baseChanged 'A major release-base change must be identified.'
}

function Test-BaseResetRejectsManualPatch {
    $repo = New-TestRepository 'base-reset-manual-patch'
    Set-JsonVersion -Path (Join-Path $repo 'plugins/sample/version.json') -Version '1.0'
    Set-ManifestVersions -Repository $repo -Version '1.0.5'
    [void](Invoke-Git $repo add plugins/sample/version.json plugins/sample/plugin.json plugins/sample/.codex-plugin/plugin.json plugins/sample/.claude-plugin/plugin.json)
    [void](Invoke-Git $repo commit -m 'Start sample 1.0 with an invalid patch')

    $report = @(Invoke-Sync $repo)
    Assert-Equal 1 $report.Count 'A base change with a manual patch must drift.'
    Assert-Equal '1.0.0' $report[0].computed 'A base change must calculate patch zero.'
    Assert-Equal $true $report[0].baseChanged 'The corrected base change must be identified.'
}

function Test-PredictionUsesCurrentMain {
    $repo = New-TestRepository 'current-main'
    [void](Invoke-Git $repo switch -c feature)
    Add-PluginContent $repo 'skills/feature/SKILL.md' 'feature content' 'Feature plugin change'
    $featureHead = Invoke-Git $repo rev-parse HEAD

    [void](Invoke-Git $repo switch main)
    Add-PluginContent $repo 'skills/main/SKILL.md' 'main content' 'Pending main plugin change'
    $currentMain = Invoke-Git $repo rev-parse HEAD
    [void](Invoke-Git $repo switch feature)

    $report = @(Invoke-Sync $repo -BaseCommit $currentMain -HeadCommit $featureHead -PredictMerge)
    Assert-Equal '0.1.6' $report[0].computed 'Prediction must include pending main and PR releases.'
}

function Test-MainOnlyBranchMergeDoesNotPredict {
    $repo = New-TestRepository 'main-only-merge'
    [void](Invoke-Git $repo branch feature)
    Add-PluginContent $repo 'skills/example/SKILL.md' 'published main content' 'Main plugin change'
    Commit-ManifestVersions $repo '0.1.5'
    $currentMain = Invoke-Git $repo rev-parse HEAD

    [void](Invoke-Git $repo switch feature)
    [void](Invoke-Git $repo merge main --no-ff -m 'Merge main into feature')
    $featureHead = Invoke-Git $repo rev-parse HEAD

    Assert-Equal 0 @(Invoke-Sync $repo -BaseCommit $currentMain -HeadCommit $featureHead -PredictMerge).Count `
        'A branch that only merges main must not predict a release.'
}

function Test-MatchingMainBaseDoesNotReset {
    $repo = New-TestRepository 'matching-main-base'
    [void](Invoke-Git $repo switch -c feature)
    Set-JsonVersion -Path (Join-Path $repo 'plugins/sample/version.json') -Version '0.2'
    Add-PluginContent $repo 'skills/feature/SKILL.md' 'feature content' 'Feature on 0.2'
    [void](Invoke-Git $repo add plugins/sample/version.json)
    [void](Invoke-Git $repo commit --amend --no-edit)
    $featureHead = Invoke-Git $repo rev-parse HEAD

    [void](Invoke-Git $repo switch main)
    Set-JsonVersion -Path (Join-Path $repo 'plugins/sample/version.json') -Version '0.2'
    Set-ManifestVersions -Repository $repo -Version '0.2.0'
    [void](Invoke-Git $repo add plugins/sample/version.json plugins/sample/plugin.json plugins/sample/.codex-plugin/plugin.json plugins/sample/.claude-plugin/plugin.json)
    [void](Invoke-Git $repo commit -m 'Publish main 0.2 release')
    $currentMain = Invoke-Git $repo rev-parse HEAD
    [void](Invoke-Git $repo switch feature)

    $report = @(Invoke-Sync $repo -BaseCommit $currentMain -HeadCommit $featureHead -PredictMerge)
    Assert-Equal '0.2.1' $report[0].computed 'A matching base already on main must not reset to 0.2.0.'
}

function Test-ConcurrentPredictionsReconcile {
    $repo = New-TestRepository 'concurrent'
    $initialMain = Invoke-Git $repo rev-parse HEAD

    [void](Invoke-Git $repo switch -c feature-a)
    Add-PluginContent $repo 'skills/a/SKILL.md' 'feature a' 'Feature A content'
    $headA = Invoke-Git $repo rev-parse HEAD
    $reportA = @(Invoke-Sync $repo -BaseCommit $initialMain -HeadCommit $headA -PredictMerge -Write)
    Assert-Equal '0.1.5' $reportA[0].computed 'Feature A prediction must be 0.1.5.'
    [void](Invoke-Git $repo add plugins/sample/plugin.json plugins/sample/.codex-plugin/plugin.json plugins/sample/.claude-plugin/plugin.json)
    [void](Invoke-Git $repo commit -m 'Stamp feature A')

    [void](Invoke-Git $repo switch main)
    [void](Invoke-Git $repo switch -c feature-b)
    Add-PluginContent $repo 'skills/b/SKILL.md' 'feature b' 'Feature B content'
    $headB = Invoke-Git $repo rev-parse HEAD
    $reportB = @(Invoke-Sync $repo -BaseCommit $initialMain -HeadCommit $headB -PredictMerge -Write)
    Assert-Equal '0.1.5' $reportB[0].computed 'Feature B prediction must also be 0.1.5.'
    [void](Invoke-Git $repo add plugins/sample/plugin.json plugins/sample/.codex-plugin/plugin.json plugins/sample/.claude-plugin/plugin.json)
    [void](Invoke-Git $repo commit -m 'Stamp feature B')

    [void](Invoke-Git $repo switch main)
    [void](Invoke-Git $repo merge feature-a --no-ff -m 'Merge feature A')
    [void](Invoke-Git $repo merge feature-b --no-ff -m 'Merge feature B')

    $report = @(Invoke-Sync $repo)
    Assert-Equal '0.1.6' $report[0].computed 'The second concurrent change must reconcile to 0.1.6.'
}

function Test-ManualManifestInflationIsRejected {
    $repo = New-TestRepository 'manual-inflation'
    Commit-ManifestVersions $repo '0.1.99'

    $report = @(Invoke-Sync $repo)
    Assert-Equal 1 $report.Count 'A manual patch inflation must drift.'
    Assert-Equal '0.1.4' $report[0].computed 'A manual patch inflation must restore the calculated version.'

    Add-PluginContent $repo 'skills/example/SKILL.md' 'content after inflation' 'Content after inflation'
    $report = @(Invoke-Sync $repo)
    Assert-Equal '0.1.5' $report[0].computed 'Content after a manual inflation must use the valid checkpoint.'
}

function Test-ManualManifestDowngradeIsRejected {
    $repo = New-TestRepository 'manual-downgrade'
    Commit-ManifestVersions $repo '0.1.1'

    $report = @(Invoke-Sync $repo)
    Assert-Equal 1 $report.Count 'A manual patch downgrade must drift.'
    Assert-Equal '0.1.4' $report[0].computed 'A manual patch downgrade must restore the calculated version.'

    Add-PluginContent $repo 'skills/example/SKILL.md' 'content after downgrade' 'Content after downgrade'
    $report = @(Invoke-Sync $repo)
    Assert-Equal '0.1.5' $report[0].computed 'Content after a manual downgrade must use the valid checkpoint.'
}

function Test-LegacyHistoryRemainsTrusted {
    $repo = New-TestRepository 'legacy-history' -LegacyHistory
    Commit-ManifestVersions $repo '0.1.99'

    Assert-Equal 0 @(Invoke-Sync $repo).Count `
        'Manifest transitions before checkpoint validation must remain a trusted migration baseline.'
}

function Test-MissingClaudeManifestIsRepaired {
    $repo = New-TestRepository 'missing-claude-manifest'
    $claudeManifest = Join-Path $repo 'plugins/sample/.claude-plugin/plugin.json'
    Remove-Item -LiteralPath $claudeManifest

    $report = @(Invoke-Sync $repo)
    Assert-Equal 1 $report.Count 'A missing Claude manifest must be reported as drift.'

    [void](Invoke-Sync $repo -Write)
    Assert-Equal $true (Test-Path $claudeManifest) 'Version sync must recreate a missing Claude manifest.'
    Assert-Equal $true ([string]::Equals(
        [IO.File]::ReadAllText((Join-Path $repo 'plugins/sample/plugin.json')),
        [IO.File]::ReadAllText($claudeManifest),
        [StringComparison]::Ordinal)) `
        'The recreated Claude manifest must exactly match the root manifest.'
}

function Test-ClaudeManifestCaseDriftIsRepaired {
    $repo = New-TestRepository 'claude-manifest-case-drift'
    $rootManifest = Join-Path $repo 'plugins/sample/plugin.json'
    $claudeManifest = Join-Path $repo 'plugins/sample/.claude-plugin/plugin.json'
    $content = [IO.File]::ReadAllText($claudeManifest).Replace('Test plugin.', 'test plugin.')
    [IO.File]::WriteAllText($claudeManifest, $content)

    $report = @(Invoke-Sync $repo)
    Assert-Equal 1 $report.Count 'Case-only Claude manifest drift must be reported.'

    [void](Invoke-Sync $repo -Write)
    Assert-Equal $true ([string]::Equals(
        [IO.File]::ReadAllText($rootManifest),
        [IO.File]::ReadAllText($claudeManifest),
        [StringComparison]::Ordinal)) `
        'Version sync must repair case-only Claude manifest drift.'
}

function Test-RepositoryClaudeManifests {
    $repository = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
    $pluginDirectories = Get-ChildItem (Join-Path $repository 'plugins') -Directory |
        Where-Object { Test-Path (Join-Path $_.FullName 'plugin.json') }

    foreach ($pluginDirectory in $pluginDirectories) {
        $rootManifest = Join-Path $pluginDirectory.FullName 'plugin.json'
        $claudeManifest = Join-Path $pluginDirectory.FullName '.claude-plugin/plugin.json'
        Assert-Equal $true (Test-Path $claudeManifest) `
            "Plugin '$($pluginDirectory.Name)' must carry .claude-plugin/plugin.json."
        Assert-Equal $true ([string]::Equals(
            [IO.File]::ReadAllText($rootManifest),
            [IO.File]::ReadAllText($claudeManifest),
            [StringComparison]::Ordinal)) `
            "Plugin '$($pluginDirectory.Name)' must keep its Claude manifest synchronized with plugin.json."
    }
}

[void](New-Item -ItemType Directory -Path $testRoot)
try {
    Test-BatchedChangesAdvanceOnce
    Test-GraphOnlyMergeDoesNotAdvance
    Test-RevertToCheckpointDoesNotAdvance
    Test-BaseResetStartsAtZero
    Test-MajorBaseResetStartsAtZero
    Test-BaseResetRejectsManualPatch
    Test-PredictionUsesCurrentMain
    Test-MainOnlyBranchMergeDoesNotPredict
    Test-MatchingMainBaseDoesNotReset
    Test-ConcurrentPredictionsReconcile
    Test-ManualManifestInflationIsRejected
    Test-ManualManifestDowngradeIsRejected
    Test-LegacyHistoryRemainsTrusted
    Test-MissingClaudeManifestIsRepaired
    Test-ClaudeManifestCaseDriftIsRepaired
    Test-RepositoryClaudeManifests
    Write-Host "Passed $script:assertions plugin-version assertions."
}
finally {
    if ($KeepTestRepositories) {
        Write-Host "Kept test repositories at $testRoot"
    }
    else {
        Remove-Item -LiteralPath $testRoot -Recurse -Force
    }
}
