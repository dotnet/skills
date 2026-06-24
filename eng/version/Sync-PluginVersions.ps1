#!/usr/bin/env pwsh
#requires -Version 7
<#
.SYNOPSIS
    Computes and (optionally) materializes per-plugin versions using Nerdbank.GitVersioning (NBGV).

.DESCRIPTION
    dotnet/skills is consumed directly from the repository (no published mirror), so every
    plugin's version must be written into the checked-in manifests. Each plugins/<name>
    directory carries a version.json whose pathFilters scope NBGV's git height to that one
    subtree, giving every plugin an independent patch number. version.json also excludes
    itself and the two stamped manifests, so adopting it (and stamping it) never inflates
    a plugin's height.

    The computed version (e.g. "0.1.4") is materialized into BOTH manifests a plugin ships:
        plugins/<name>/plugin.json
        plugins/<name>/.codex-plugin/plugin.json

    This one script backs both versioning entry points:
      * version-bump-command.yml     -> -BaseCommit <mergeBase> -HeadCommit <prHead> -PredictSquashMerge -OnlyChanged -Write    (admin /version-bump)
      * weekly-version-sync.yml      -> -OnlyChanged -Write                                                                      (backstop, on main HEAD)

.PARAMETER BaseCommit
    Commit-ish at which to read each plugin's NBGV height. In -PredictSquashMerge mode this is
    the PR's merge base (the main commit the squash will land on). Without -PredictSquashMerge it
    defaults to the current HEAD (used by the weekly backstop running on main).

.PARAMETER HeadCommit
    The PR head commit. When given, the plugin set is derived from the BaseCommit..HeadCommit diff
    (only plugins whose height-bearing files changed), so -PredictSquashMerge bumps exactly the
    plugins the PR touched. Requires -BaseCommit.

.PARAMETER PredictSquashMerge
    Predict the version a plugin will have on main AFTER this PR squash-merges, instead of reading
    the current height. Requires -BaseCommit (the merge base). The prediction handles three cases:
      * the PR bumps the plugin's version.json base (0.1 -> 0.2)  => <newBase>.0 (squash is the
        base-change commit, so NBGV resets the patch to 0);
      * the plugin is newly added (no version.json at the merge base) => <newBase>.0;
      * otherwise (ordinary content change) => <base>.(heightAtBase + 1), because the squash adds
        exactly one height-bearing commit on top of the merge base.

.PARAMETER OnlyChanged
    Emit/stamp only plugins whose computed version differs from the value currently in
    plugin.json (i.e. plugins that actually drifted).

.PARAMETER Write
    Materialize the computed version into both manifests. Without it the script is read-only.

.OUTPUTS
    A JSON array on stdout: [{ "plugin", "current", "computed", "changed" }, ...].
#>
[CmdletBinding()]
param(
    [string]   $BaseCommit,
    [string]   $HeadCommit,
    [switch]   $PredictSquashMerge,
    [switch]   $OnlyChanged,
    [switch]   $Write
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ($PredictSquashMerge -and -not $BaseCommit) {
    throw '-PredictSquashMerge requires -BaseCommit (the PR merge base).'
}
if ($HeadCommit -and -not $BaseCommit) {
    throw '-HeadCommit requires -BaseCommit (the diff is BaseCommit..HeadCommit).'
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
$pluginsRoot = Join-Path $repoRoot 'plugins'

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

function Get-NbgvInfo {
    param([string] $PluginDir, [string] $Commit)
    $nbgvArgs = @('nbgv', 'get-version', '-p', $PluginDir, '-f', 'json')
    if ($Commit) { $nbgvArgs += $Commit }
    # Capture stderr separately so a failure surfaces the real NBGV/dotnet error
    # in CI logs, while stdout stays clean JSON for ConvertFrom-Json.
    $errFile = New-TemporaryFile
    try {
        $json = & dotnet @nbgvArgs 2>$errFile.FullName
        if ($LASTEXITCODE -ne 0 -or -not $json) {
            $stderr = (Get-Content $errFile.FullName -Raw).Trim()
            throw "nbgv get-version failed for $PluginDir (commit '$Commit'): $stderr"
        }
    } finally {
        Remove-Item $errFile.FullName -ErrorAction SilentlyContinue
    }
    return ($json | ConvertFrom-Json)
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

# Plugins whose version-affecting files changed between two commits. The two stamped manifests
# (plugin.json, .codex-plugin/plugin.json) are output, not input, so they're excluded; everything
# else under the plugin counts — including version.json, since a base bump (0.1 -> 0.2) with no
# other change must still be detected so /version-bump can stamp the reset patch.
# Used by /version-bump to scope -PredictSquashMerge to exactly the plugins the PR touched.
function Get-ChangedPlugins {
    param([string] $From, [string] $To)
    git diff --name-only --diff-filter=ACMRD $From $To |
        Where-Object {
            $_ -match '^plugins/[^/]+/' -and
            $_ -notmatch '^plugins/[^/]+/plugin\.json$' -and
            $_ -notmatch '^plugins/[^/]+/\.codex-plugin/plugin\.json$'
        } |
        ForEach-Object { ($_ -split '/')[1] } |
        Sort-Object -Unique
}

# Resolve the plugin set: an explicit PR diff (BaseCommit..HeadCommit) scopes to the plugins the
# PR actually touched (required so -PredictSquashMerge only bumps those); otherwise every plugin
# that has a version.json (the weekly backstop reconciles them all on main).
if ($HeadCommit) {
    $Plugins = @(Get-ChangedPlugins -From $BaseCommit -To $HeadCommit)
}
else {
    $Plugins = Get-ChildItem -Path $pluginsRoot -Directory |
        Where-Object { Test-Path (Join-Path $_.FullName 'version.json') } |
        Select-Object -ExpandProperty Name |
        Sort-Object
}

$results = [System.Collections.Generic.List[object]]::new()

foreach ($name in $Plugins) {
    $pluginDir = Join-Path $pluginsRoot $name
    $manifest = Join-Path $pluginDir 'plugin.json'
    $codexManifest = Join-Path $pluginDir '.codex-plugin' 'plugin.json'

    if (-not (Test-Path (Join-Path $pluginDir 'version.json'))) { continue }

    $current = (Get-Content $manifest -Raw | ConvertFrom-Json).version
    # Also read the Codex-facing manifest so we can detect (and repair) the case where
    # the two manifests have drifted apart — e.g. a hand-edit updated one but not the other.
    $currentCodex = if (Test-Path $codexManifest) {
        (Get-Content $codexManifest -Raw | ConvertFrom-Json).version
    } else { $null }

    if ($PredictSquashMerge) {
        $newBase = Get-VersionBase -Plugin $name
        $oldBase = Get-VersionBase -Plugin $name -Commit $BaseCommit
        if (-not $oldBase -or $oldBase -ne $newBase) {
            # Base bumped in this PR, or brand-new plugin: the squashed commit becomes the
            # version-origin commit, so NBGV resets the patch to 0.
            $computed = "$newBase.0"
        }
        else {
            $heightAtBase = (Get-NbgvInfo -PluginDir $pluginDir -Commit $BaseCommit).VersionHeight
            $computed = "$newBase.$([int]$heightAtBase + 1)"
        }
    }
    else {
        $computed = (Get-NbgvInfo -PluginDir $pluginDir -Commit $BaseCommit).SimpleVersion
    }

    # Guard against a malformed version.json base (e.g. "0.2$1" or "1.x"): a bad value
    # would otherwise be written verbatim into the manifests. NBGV versions are always
    # numeric major.minor.patch, so anything else means the source data is wrong.
    if ($computed -notmatch '^\d+\.\d+\.\d+$') {
        throw "Computed version '$computed' for plugin '$name' is not a valid major.minor.patch — check plugins/$name/version.json"
    }

    $changed = ($computed -ne $current) -or
               ($null -ne $currentCodex -and $computed -ne $currentCodex)

    if ($OnlyChanged -and -not $changed) { continue }

    if ($Write -and $changed) {
        [void](Set-ManifestVersion -Path $manifest -Version $computed)
        [void](Set-ManifestVersion -Path $codexManifest -Version $computed)
    }

    $results.Add([ordered]@{
        plugin   = $name
        current  = $current
        computed = $computed
        changed  = $changed
    })
}

$results | ConvertTo-Json -AsArray -Compress
