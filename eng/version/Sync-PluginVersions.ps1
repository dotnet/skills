#!/usr/bin/env pwsh
#requires -Version 7
<#
.SYNOPSIS
    Computes and (optionally) materializes per-plugin release versions.

.DESCRIPTION
    dotnet/skills is consumed directly from the repository (no published mirror), so every
    plugin's version must be written into the checked-in manifests. Each plugins/<name>
    directory carries a version.json that declares its major.minor release base and the
    files that are excluded from version-bearing content.

    The checked-in manifest version is a release checkpoint. If a plugin's effective
    content on main differs from the content at its latest first-parent checkpoint, its
    patch advances once. This deliberately ignores second-parent history: merging a stale
    branch can make old commits newly reachable without changing the plugin on main, and
    raw DAG height would incorrectly treat that graph-only event as a new release.

    The computed version (e.g. "0.1.4") is materialized into every manifest a plugin ships:
        plugins/<name>/plugin.json
        plugins/<name>/.codex-plugin/plugin.json
        plugins/<name>/.claude-plugin/plugin.json

    This one script backs both versioning entry points:
      * version-bump-command.yml     -> -BaseCommit <currentMain> -HeadCommit <prHead> -PredictMerge -OnlyChanged -Write    (admin /version-bump)
      * weekly-version-sync.yml      -> -OnlyChanged -Write                                                                (backstop, on main HEAD)

.PARAMETER BaseCommit
    Commit-ish that represents the authoritative main state. In -PredictMerge mode this
    is the current base-branch SHA, not the PR's potentially stale merge base. Without
    -PredictMerge it defaults to HEAD (used by the weekly backstop on main).

.PARAMETER HeadCommit
    The PR head commit. When given, the script finds its merge base with BaseCommit and derives
    the plugin set from that merge-base..HeadCommit diff. Requires -BaseCommit.

.PARAMETER PredictMerge
    Predict the version a plugin will have on main AFTER this PR merges. Requires -BaseCommit
    (current main) and -HeadCommit. The prediction handles three cases:
      * the PR bumps the plugin's version.json base (0.1 -> 0.2)  => <newBase>.0;
      * the plugin is newly added (no version.json at the merge base) => <newBase>.0;
      * otherwise, the authoritative pending version on current main advances once when the PR
        has a net content change. The result is independent of squash, merge, or rebase topology.

.PARAMETER OnlyChanged
    Emit/stamp only plugins whose computed version differs from the value currently in
    plugin.json (i.e. plugins that actually drifted).

.PARAMETER Write
    Materialize the computed version into every manifest and synchronize the Claude manifest with
    the root manifest. Without it the script is read-only.

.OUTPUTS
    A JSON array on stdout: [{ "plugin", "current", "computed", "changed" }, ...].
#>
[CmdletBinding()]
param(
    [string]   $BaseCommit,
    [string]   $HeadCommit,
    [switch]   $PredictMerge,
    [switch]   $OnlyChanged,
    [switch]   $Write
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ($PredictMerge -and -not $BaseCommit) {
    throw '-PredictMerge requires -BaseCommit (the current base-branch SHA).'
}
if ($PredictMerge -and -not $HeadCommit) {
    throw '-PredictMerge requires -HeadCommit (the PR head) so the bump is scoped to the plugins the PR actually changed; without it the script would predict a +1 patch for every plugin.'
}
if ($HeadCommit -and -not $BaseCommit) {
    throw '-HeadCommit requires -BaseCommit (the diff is BaseCommit..HeadCommit).'
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
$pluginsRoot = Join-Path $repoRoot 'plugins'
$authoritativeVersionCache = @{}
$checkpointValidationMarker = 'release-checkpoint-validation-v1'
$checkpointValidationStartCache = @{}

# Every git command below uses repo-root-relative paths (e.g. "plugins/<name>"), so pin the working
# directory. Invoked from any other directory, git show/diff/log would otherwise operate on whatever
# repository owns the caller's cwd and silently compute the wrong height.
Set-Location -LiteralPath $repoRoot

# The files excluded from release-bearing content for every plugin: the stamped manifests (which
# are outputs, not inputs) plus version.json itself. This is the single source of truth, mirrored
# by each plugin's version.json `pathFilters`, by Test-EffectiveContentChange, and by the
# canonical-filter guard in the main loop.
$ContentExcludedFiles = @('plugin.json', '.codex-plugin/plugin.json', '.claude-plugin/plugin.json', 'version.json')

# The canonical `pathFilters` array every plugin's version.json must contain: include the whole
# plugin subtree ('.') minus the content-excluded files above.
function Get-CanonicalFilters {
    @('.') + ($ContentExcludedFiles | ForEach-Object { ":!$_" })
}

# Replace only the "version" value so the rest of the manifest stays byte-identical
# (avoids reflow/key-reorder noise that a full ConvertTo-Json round-trip would cause).
# Uses a MatchEvaluator (not a replacement string) so a '$' in the version can never be
# re-expanded as a regex substitution (e.g. "$1") and corrupt the JSON.
function Set-ManifestVersion {
    param([string] $Path, [string] $Version)
    if (-not (Test-Path $Path)) { return $false }
    $text = [IO.File]::ReadAllText($Path)
    $pattern = '("version"\s*:\s*")[^"]*(")'
    $rx = [regex]::new($pattern)
    if (-not $rx.IsMatch($text)) {
        throw "No `"version`" field found in $Path"
    }
    $evaluator = [System.Text.RegularExpressions.MatchEvaluator]({
        param($m) $m.Groups[1].Value + $Version + $m.Groups[2].Value
    }.GetNewClosure())
    $updated = $rx.Replace($text, $evaluator, 1)
    if ($updated -ne $text) {
        [IO.File]::WriteAllText($Path, $updated)
        return $true
    }
    return $false
}

# The version.json `version` base (major.minor) for a plugin, read either from the working
# tree (current) or from a historical commit. Returns $null if the file is absent there.
function Get-VersionBase {
    param([string] $Plugin, [string] $Commit)
    if ($Commit) {
        $raw = git show "${Commit}:plugins/$Plugin/version.json" 2>$null
        if ($LASTEXITCODE -ne 0 -or -not $raw) { return $null }
        return ($raw | ConvertFrom-Json).version
    }
    return (Get-Content (Join-Path $pluginsRoot $Plugin 'version.json') -Raw | ConvertFrom-Json).version
}

# Whether the BaseCommit..HeadCommit diff touches effective plugin content. The git pathspec
# excludes mirror the plugin's canonical version.json pathFilters, so a version.json-only edit
# that leaves the base unchanged is release-neutral.
function Test-EffectiveContentChange {
    param([string] $Plugin, [string] $From, [string] $To)
    $excludes = $ContentExcludedFiles | ForEach-Object { ":(exclude)plugins/$Plugin/$_" }
    $touched = git diff --name-only --diff-filter=ACMRD $From $To -- "plugins/$Plugin" @excludes
    if ($LASTEXITCODE -ne 0) {
        throw "Could not compare effective content for plugin '$Plugin' from '$From' to '$To'."
    }
    return [bool]$touched
}

function Get-ManifestVersionAtCommit {
    param([string] $Plugin, [string] $Commit)
    $raw = git show "${Commit}:plugins/$Plugin/plugin.json" 2>$null
    if ($LASTEXITCODE -ne 0 -or -not $raw) { return $null }
    $version = ($raw | ConvertFrom-Json).version
    if ($version -notmatch '^\d+\.\d+\.\d+$') {
        throw "Manifest version '$version' for plugin '$Plugin' at commit '$Commit' is not a valid major.minor.patch version."
    }
    return [string]$version
}

# Existing history used NBGV and can contain legitimate patch jumps. Find the first-parent commit
# that introduced transition validation, so older manifest changes remain a trusted migration baseline.
function Get-CheckpointValidationStart {
    param([string] $Commit)
    if ($checkpointValidationStartCache.ContainsKey($Commit)) {
        return $checkpointValidationStartCache[$Commit]
    }

    $matches = @(git log --first-parent -S $checkpointValidationMarker --format='%H' $Commit -- eng/version/Sync-PluginVersions.ps1)
    if ($LASTEXITCODE -ne 0) {
        throw "Could not locate the checkpoint-validation boundary at '$Commit'."
    }
    $start = [string]($matches | Select-Object -Last 1)
    $checkpointValidationStartCache[$Commit] = $start
    return $start
}

# Find the latest valid version transition on the target's first-parent history. A manifest edit is
# a checkpoint only when it matches the version computed from its first parent and its own effective
# content change. This prevents arbitrary patch edits from becoming permanent version authority.
# The first commit that establishes version.json remains a bootstrap checkpoint so existing history
# can retain a non-zero patch. Second-parent history is ignored to avoid duplicate stale-branch changes.
function Get-ReleaseCheckpoint {
    param([string] $Plugin, [string] $Commit)
    $manifestPath = "plugins/$Plugin/plugin.json"
    $versionPath = "plugins/$Plugin/version.json"
    $validationStart = Get-CheckpointValidationStart -Commit $Commit
    $candidates = @(git rev-list --first-parent --full-history $Commit -- $manifestPath $versionPath)
    if ($LASTEXITCODE -ne 0) {
        throw "Could not read first-parent manifest history for plugin '$Plugin' at commit '$Commit'."
    }

    foreach ($candidate in $candidates) {
        $version = Get-ManifestVersionAtCommit -Plugin $Plugin -Commit $candidate
        if (-not $version) { continue }

        $commitAndParents = @((git rev-list --parents -n 1 $candidate) -split ' ')
        if ($LASTEXITCODE -ne 0) {
            throw "Could not read parents for checkpoint candidate '$candidate'."
        }
        $firstParent = if ($commitAndParents.Count -gt 1) { $commitAndParents[1] } else { $null }
        $parentVersion = if ($firstParent) {
            Get-ManifestVersionAtCommit -Plugin $Plugin -Commit $firstParent
        } else { $null }
        $releaseBase = Get-VersionBase -Plugin $Plugin -Commit $candidate
        $parentBase = if ($firstParent) {
            Get-VersionBase -Plugin $Plugin -Commit $firstParent
        } else { $null }

        $isTransition = -not $parentVersion -or $version -ne $parentVersion -or
            -not $parentBase -or $releaseBase -ne $parentBase
        if (-not $isTransition) { continue }

        $requiresValidation = $false
        if ($validationStart) {
            git merge-base --is-ancestor $validationStart $candidate
            $requiresValidation = $LASTEXITCODE -eq 0
        }

        # Trust the legacy migration baseline and the first commit that establishes a plugin.
        if (-not $requiresValidation -or -not $firstParent -or -not $parentVersion -or -not $parentBase) {
            return [pscustomobject]@{
                Commit = $candidate
                Version = $version
            }
        }

        if ($releaseBase -ne $parentBase) {
            $expectedVersion = "$releaseBase.0"
        }
        else {
            $parentAuthority = Get-AuthoritativeVersion -Plugin $Plugin -Commit $firstParent
            if ($parentAuthority.Version -notmatch '^(\d+\.\d+)\.(\d+)$') {
                throw "Authoritative parent version '$($parentAuthority.Version)' for plugin '$Plugin' at '$firstParent' is invalid."
            }
            $parentAuthorityBase = $Matches[1]
            $parentAuthorityPatch = [int]$Matches[2]
            if ($parentAuthorityBase -ne $releaseBase) {
                throw "Authoritative parent base '$parentAuthorityBase' for plugin '$Plugin' does not match release base '$releaseBase' at '$candidate'."
            }
            $candidateContentChanged = Test-EffectiveContentChange -Plugin $Plugin -From $firstParent -To $candidate
            $expectedVersion = "$releaseBase.$($parentAuthorityPatch + [int]$candidateContentChanged)"
        }

        if ($version -eq $expectedVersion) {
            return [pscustomobject]@{
                Commit = $candidate
                Version = $version
            }
        }
    }

    throw "Could not find a release checkpoint for plugin '$Plugin' at commit '$Commit'."
}

# Return first-parent main commits that changed effective plugin content after a checkpoint.
# These commits explain a pending release without descending into duplicate second-parent history.
function Get-FirstParentContentChanges {
    param([string] $Plugin, [string] $FromCommit, [string] $ToCommit)
    if ($FromCommit -eq $ToCommit) { return @() }

    git merge-base --is-ancestor $FromCommit $ToCommit
    if ($LASTEXITCODE -ne 0) {
        throw "Release checkpoint '$FromCommit' for plugin '$Plugin' is not on the first-parent target history ending at '$ToCommit'."
    }

    $changes = [System.Collections.Generic.List[object]]::new()
    $commits = @(git rev-list --first-parent --reverse "$FromCommit..$ToCommit")
    if ($LASTEXITCODE -ne 0) {
        throw "Could not read first-parent content history for plugin '$Plugin' from '$FromCommit' to '$ToCommit'."
    }

    foreach ($commit in $commits) {
        $firstParent = git rev-parse "$commit^1"
        if ($LASTEXITCODE -ne 0 -or -not $firstParent) {
            throw "Could not read the first parent of commit '$commit'."
        }
        if (Test-EffectiveContentChange -Plugin $Plugin -From $firstParent -To $commit) {
            $changes.Add([ordered]@{
                commit = $commit
                shortCommit = $commit.Substring(0, 8)
                subject = [string](git show -s --format='%s' $commit)
            })
        }
    }

    return @($changes)
}

function Get-AuthoritativeVersion {
    param([string] $Plugin, [string] $Commit)
    $cacheKey = "$Plugin`n$Commit"
    if ($authoritativeVersionCache.ContainsKey($cacheKey)) {
        return $authoritativeVersionCache[$cacheKey]
    }

    $releaseBase = Get-VersionBase -Plugin $Plugin -Commit $Commit
    if ($releaseBase -notmatch '^\d+\.\d+$') {
        throw "version.json base '$releaseBase' for plugin '$Plugin' at '$Commit' must be major.minor (e.g. 0.1)."
    }

    $checkpoint = Get-ReleaseCheckpoint -Plugin $Plugin -Commit $Commit
    if ($checkpoint.Version -notmatch '^(\d+\.\d+)\.(\d+)$') {
        throw "Checkpoint version '$($checkpoint.Version)' for plugin '$Plugin' is invalid."
    }
    $checkpointBase = $Matches[1]
    $checkpointPatch = [int]$Matches[2]

    if ($checkpointBase -ne $releaseBase) {
        $result = [pscustomobject]@{
            Version = "$releaseBase.0"
            Checkpoint = $checkpoint.Commit
            BaseChanged = $true
            Commits = @()
        }
    }
    else {
        $contentChanged = Test-EffectiveContentChange -Plugin $Plugin -From $checkpoint.Commit -To $Commit
        $changes = if ($contentChanged) {
            @(Get-FirstParentContentChanges -Plugin $Plugin -FromCommit $checkpoint.Commit -ToCommit $Commit)
        } else { @() }

        $result = [pscustomobject]@{
            Version = "$releaseBase.$($checkpointPatch + [int]$contentChanged)"
            Checkpoint = $checkpoint.Commit
            BaseChanged = $false
            Commits = $changes
        }
    }

    $authoritativeVersionCache[$cacheKey] = $result
    return $result
}

# Plugins whose version-affecting files changed between two commits. The stamped manifests
# (plugin.json, .codex-plugin/plugin.json, and .claude-plugin/plugin.json) are output,
# not input, so they're excluded; everything else under the plugin counts — including version.json,
# since a base bump (0.1 -> 0.2) with no other change must still be detected so /version-bump can
# stamp the reset patch.
# Used by /version-bump to scope -PredictMerge to exactly the plugins the PR touched.
function Get-ChangedPlugins {
    param([string] $From, [string] $To)
    git diff --name-only --diff-filter=ACMRD $From $To |
        Where-Object {
            $_ -match '^plugins/[^/]+/' -and
            $_ -notmatch '^plugins/[^/]+/plugin\.json$' -and
            $_ -notmatch '^plugins/[^/]+/\.codex-plugin/plugin\.json$' -and
            $_ -notmatch '^plugins/[^/]+/\.claude-plugin/plugin\.json$'
        } |
        ForEach-Object { ($_ -split '/')[1] } |
        Sort-Object -Unique
}

# Resolve the plugin set: an explicit PR diff (BaseCommit..HeadCommit) scopes to the plugins the
# PR actually touched (required so -PredictMerge only bumps those); otherwise every plugin
# that has a version.json (the weekly backstop reconciles them all on main).
if (-not $BaseCommit) {
    $BaseCommit = [string](git rev-parse HEAD)
    if ($LASTEXITCODE -ne 0 -or -not $BaseCommit) {
        throw 'Could not resolve HEAD for authoritative version computation.'
    }
}

$mergeBase = $null
if ($HeadCommit) {
    $mergeBase = [string](git merge-base $BaseCommit $HeadCommit)
    if ($LASTEXITCODE -ne 0 -or -not $mergeBase) {
        throw "Could not determine a merge base between base '$BaseCommit' and head '$HeadCommit'."
    }
    $Plugins = @(Get-ChangedPlugins -From $mergeBase -To $HeadCommit)
}
else {
    # Weekly backstop: reconcile every real plugin. Enumerate by plugin.json (the marker of a
    # shipped plugin) rather than version.json, and fail fast if any plugin is missing version.json,
    # so a plugin can never be silently dropped from automated versioning.
    $Plugins = Get-ChildItem -Path $pluginsRoot -Directory |
        Where-Object { Test-Path (Join-Path $_.FullName 'plugin.json') } |
        Select-Object -ExpandProperty Name |
        Sort-Object
    foreach ($p in $Plugins) {
        if (-not (Test-Path (Join-Path $pluginsRoot $p 'version.json'))) {
            throw "plugins/$p ships a plugin.json but has no version.json — every plugin must define a version base. Add plugins/$p/version.json (base = its current major.minor, e.g. 0.2)."
        }
    }
}

$results = [System.Collections.Generic.List[object]]::new()

foreach ($name in $Plugins) {
    $pluginDir = Join-Path $pluginsRoot $name
    $manifest = Join-Path $pluginDir 'plugin.json'
    $codexManifest = Join-Path $pluginDir '.codex-plugin' 'plugin.json'
    # Claude Code needs an inline plugin.json. The root manifest is authoritative because the two
    # formats are identical; -Write creates or replaces the Claude copy when it is missing or stale.
    $claudeManifest = Join-Path $pluginDir '.claude-plugin' 'plugin.json'

    # A shipped plugin (one with a plugin.json) must define a version base; fail loudly rather than
    # silently skipping it — symmetric with the weekly enumerate guard above, so /version-bump can't
    # let a plugin.json-without-version.json reach main. A dir with no plugin.json (a deleted plugin,
    # or a non-plugin helper dir surfaced by the diff) is genuinely not versioned, so skip it.
    if (-not (Test-Path (Join-Path $pluginDir 'version.json'))) {
        if (Test-Path $manifest) {
            throw "plugins/$name ships a plugin.json but has no version.json — every plugin must define a version base. Add plugins/$name/version.json (base = its current major.minor, e.g. 0.2)."
        }
        continue
    }

    $current = (Get-Content $manifest -Raw | ConvertFrom-Json).version
    # Both manifests must exist: the version is duplicated across plugin.json and the Codex-facing
    # .codex-plugin/plugin.json, and every consumer reads one of them. If the Codex manifest were
    # missing we'd silently stamp only plugin.json and ship mismatched versions across clients, so
    # fail fast instead.
    if (-not (Test-Path $codexManifest)) {
        throw "plugins/$name is missing .codex-plugin/plugin.json — the version must be stamped into both manifests. Add plugins/$name/.codex-plugin/plugin.json."
    }
    # Read the Codex manifest too so we detect (and repair) the case where the two manifests have
    # drifted apart — e.g. a hand-edit updated one but not the other.
    $currentCodex = (Get-Content $codexManifest -Raw | ConvertFrom-Json).version
    $rootManifestContent = [IO.File]::ReadAllText($manifest)
    $claudeManifestContent = if (Test-Path $claudeManifest) {
        [IO.File]::ReadAllText($claudeManifest)
    } else { $null }

    # The version.json base must be major.minor (e.g. "0.1"). Validate it before either
    # computation path can stamp a malformed release.
    $base = Get-VersionBase -Plugin $name
    if ($base -notmatch '^\d+\.\d+$') {
        throw "version.json base '$base' for plugin '$name' must be major.minor (e.g. 0.1) — check plugins/$name/version.json"
    }

    # pathFilters must stay exactly canonical because they document the same release-neutral files
    # that this script excludes from content comparisons. Reject drift so the declared versioning
    # contract and the implementation cannot silently disagree.
    $canonicalFilters = @(Get-CanonicalFilters | Sort-Object)
    $filtersProp = (Get-Content (Join-Path $pluginsRoot $name 'version.json') -Raw |
        ConvertFrom-Json).PSObject.Properties['pathFilters']
    $actualFilters = if ($filtersProp) { @($filtersProp.Value | Sort-Object) } else { @() }
    if (($actualFilters -join "`n") -ne ($canonicalFilters -join "`n")) {
        throw "version.json pathFilters for plugin '$name' must be the canonical set [$((Get-CanonicalFilters) -join ', ')] — check plugins/$name/version.json"
    }

    if ($PredictMerge) {
        $oldBase = Get-VersionBase -Plugin $name -Commit $mergeBase
        $mainBase = Get-VersionBase -Plugin $name -Commit $BaseCommit
        $branchChangesBase = -not $oldBase -or $oldBase -ne $base
        if ($branchChangesBase -and $mainBase -and $mainBase -ne $oldBase) {
            if ($mainBase -ne $base) {
                throw "Plugin '$name' changes its version base to '$base', but current main independently changed it to '$mainBase'. Resolve the base conflict before stamping."
            }
            # Main already established the same release line. Build on its checkpoint instead of
            # resetting that line when this stale branch merges.
            $branchChangesBase = $false
        }

        if ($branchChangesBase) {
            # A deliberate base bump or a new plugin starts a new release line at patch 0.
            $computed = "$base.0"
            $authority = $null
        }
        else {
            $authority = Get-AuthoritativeVersion -Plugin $name -Commit $BaseCommit
            if ($authority.Version -notmatch '^(\d+\.\d+)\.(\d+)$') {
                throw "Authoritative version '$($authority.Version)' for plugin '$name' is invalid."
            }
            $authoritativeBase = $Matches[1]
            $authoritativePatch = [int]$Matches[2]
            $contentChange = [int](Test-EffectiveContentChange -Plugin $name -From $mergeBase -To $HeadCommit)
            $computed = "$authoritativeBase.$($authoritativePatch + $contentChange)"
        }
    }
    else {
        $authority = Get-AuthoritativeVersion -Plugin $name -Commit $BaseCommit
        $computed = $authority.Version
    }

    # Guard against malformed source data before writing to any manifest.
    if ($computed -notmatch '^\d+\.\d+\.\d+$') {
        throw "Computed version '$computed' for plugin '$name' is not a valid major.minor.patch — check plugins/$name/version.json"
    }

    # The Claude manifest must be an exact copy of the root manifest. Treat a missing or stale copy
    # as drift so both the weekly sync and /version-bump can repair older branches automatically.
    $changed = ($computed -ne $current) -or ($computed -ne $currentCodex) -or
               ($claudeManifestContent -cne $rootManifestContent)

    if ($OnlyChanged -and -not $changed) { continue }

    if ($Write -and $changed) {
        [void](Set-ManifestVersion -Path $manifest -Version $computed)
        [void](Set-ManifestVersion -Path $codexManifest -Version $computed)
        [void](New-Item -ItemType Directory -Path (Split-Path $claudeManifest -Parent) -Force)
        [IO.File]::WriteAllText($claudeManifest, [IO.File]::ReadAllText($manifest))
    }

    $results.Add([ordered]@{
        plugin      = $name
        current     = $current
        computed    = $computed
        changed     = $changed
        checkpoint  = if ($authority) { $authority.Checkpoint } else { $null }
        baseChanged = if ($authority) { $authority.BaseChanged } else { $true }
        commits     = if ($authority) { @($authority.Commits) } else { @() }
    })
}

$results | ConvertTo-Json -AsArray -Compress -Depth 5
