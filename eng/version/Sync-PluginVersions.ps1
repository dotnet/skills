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
        exactly one height-bearing commit on top of the merge base. A PR that edits only version.json
        without changing the base (e.g. a pathFilters tweak) changes no height-bearing file, so the
        height is unchanged (=> <base>.heightAtBase) and no spurious bump is predicted.

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
if ($PredictSquashMerge -and -not $HeadCommit) {
    throw '-PredictSquashMerge requires -HeadCommit (the PR head) so the bump is scoped to the plugins the PR actually changed; without it the script would predict a +1 patch for every plugin.'
}
if ($HeadCommit -and -not $BaseCommit) {
    throw '-HeadCommit requires -BaseCommit (the diff is BaseCommit..HeadCommit).'
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
$pluginsRoot = Join-Path $repoRoot 'plugins'

# Every git command below uses repo-root-relative paths (e.g. "plugins/<name>"), so pin the working
# directory. Invoked from any other directory, git show/diff/log would otherwise operate on whatever
# repository owns the caller's cwd and silently compute the wrong height.
Set-Location -LiteralPath $repoRoot

# The files excluded from NBGV git height for every plugin: the stamped manifests (which are
# outputs, not inputs) plus version.json itself. plugin.json and .codex-plugin/plugin.json exist
# for every plugin; .claude-plugin/plugin.json is optional (only plugins that need an inline Claude
# manifest, e.g. dotnet-msbuild's binlog MCP server, carry one). Excluding a manifest a given plugin
# doesn't have is a harmless no-op for that plugin, and keeping it in the universal list means any
# plugin that later adds a .claude-plugin manifest is already height-neutral (won't self-bump when
# stamped). This is the single source of truth, mirrored by each plugin's version.json `pathFilters`,
# by Test-HeightBearingChange, and by the canonical-filter guard in the main loop. Keeping one list
# avoids the pieces drifting apart.
$HeightExcludedFiles = @('plugin.json', '.codex-plugin/plugin.json', '.claude-plugin/plugin.json', 'version.json')

# The canonical `pathFilters` array every plugin's version.json must contain: include the whole
# plugin subtree ('.') minus the height-excluded files above.
function Get-CanonicalFilters {
    @('.') + ($HeightExcludedFiles | ForEach-Object { ":!$_" })
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

# Whether the BaseCommit..HeadCommit diff touches any *height-bearing* file for a plugin — a
# file NBGV counts toward git height. The git pathspec excludes mirror the plugin's canonical
# version.json pathFilters (built from $HeightExcludedFiles), so a version.json-only edit that
# leaves the base unchanged is correctly treated as height-neutral. The main loop guarantees the
# pathFilters are canonical, so these excludes always match what NBGV actually computes.
function Test-HeightBearingChange {
    param([string] $Plugin, [string] $From, [string] $To)
    $excludes = $HeightExcludedFiles | ForEach-Object { ":(exclude)plugins/$Plugin/$_" }
    $touched = git diff --name-only --diff-filter=ACMRD $From $To -- "plugins/$Plugin" @excludes
    return [bool]$touched
}

# Plugins whose version-affecting files changed between two commits. The stamped manifests
# (plugin.json, .codex-plugin/plugin.json, and the optional .claude-plugin/plugin.json) are output,
# not input, so they're excluded; everything else under the plugin counts — including version.json,
# since a base bump (0.1 -> 0.2) with no other change must still be detected so /version-bump can
# stamp the reset patch.
# Used by /version-bump to scope -PredictSquashMerge to exactly the plugins the PR touched.
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
# PR actually touched (required so -PredictSquashMerge only bumps those); otherwise every plugin
# that has a version.json (the weekly backstop reconciles them all on main).
if ($HeadCommit) {
    $Plugins = @(Get-ChangedPlugins -From $BaseCommit -To $HeadCommit)
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
    # Optional third manifest: some clients (e.g. Claude Code) need an inline plugin.json rather than
    # the reference form the Codex manifest uses, so a plugin may carry .claude-plugin/plugin.json.
    # Only plugins that need it have one (dotnet-msbuild today), so it's stamped when present rather
    # than required.
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
    # The optional Claude manifest, read only when the plugin carries one. Track presence separately
    # from the version value: keying "is it present?" off $currentClaude would conflate an absent file
    # with a present-but-malformed one (missing/empty "version"). By gating on $hasClaudeManifest, a
    # present manifest is always considered and stamped — and a missing/empty "version" fails loudly
    # (the strict-mode read or Set-ManifestVersion throws) rather than being silently skipped.
    $hasClaudeManifest = Test-Path $claudeManifest
    $currentClaude = if ($hasClaudeManifest) {
        (Get-Content $claudeManifest -Raw | ConvertFrom-Json).version
    } else { $null }

    # The version.json base must be major.minor (e.g. "0.1"); a malformed or 3-part base
    # (e.g. "0.1.0") would otherwise pass through the non-predict path because NBGV normalizes
    # it into a 3-part SimpleVersion that satisfies the computed-value guard below, silently
    # producing a wrong/fixed version. Validate it here so both paths fail loudly instead.
    $base = Get-VersionBase -Plugin $name
    if ($base -notmatch '^\d+\.\d+$') {
        throw "version.json base '$base' for plugin '$name' must be major.minor (e.g. 0.1) — check plugins/$name/version.json"
    }

    # pathFilters must stay exactly canonical. The predict-mode height math assumes version.json
    # and the two stamped manifests are excluded from NBGV height (so a version.json-only edit is
    # height-neutral) and that the filter set is identical at the merge base and head. A hand-edited
    # pathFilters would silently break that assumption — the post-merge NBGV height would diverge
    # from the prediction — so reject anything but the generated canonical set in both paths.
    $canonicalFilters = @(Get-CanonicalFilters | Sort-Object)
    $filtersProp = (Get-Content (Join-Path $pluginsRoot $name 'version.json') -Raw |
        ConvertFrom-Json).PSObject.Properties['pathFilters']
    $actualFilters = if ($filtersProp) { @($filtersProp.Value | Sort-Object) } else { @() }
    if (($actualFilters -join "`n") -ne ($canonicalFilters -join "`n")) {
        throw "version.json pathFilters for plugin '$name' must be the canonical set [$((Get-CanonicalFilters) -join ', ')] — check plugins/$name/version.json"
    }

    if ($PredictSquashMerge) {
        $oldBase = Get-VersionBase -Plugin $name -Commit $BaseCommit
        if (-not $oldBase -or $oldBase -ne $base) {
            # Base bumped in this PR, or brand-new plugin: the squashed commit becomes the
            # version-origin commit, so NBGV resets the patch to 0.
            $computed = "$base.0"
        }
        else {
            $heightAtBase = [int](Get-NbgvInfo -PluginDir $pluginDir -Commit $BaseCommit).VersionHeight
            # The squash adds a height-bearing commit only if the PR actually changed a height-bearing
            # file. -PredictSquashMerge requires -HeadCommit (guarded above), so the +1 is always scoped
            # to that. A version.json-only edit that doesn't touch the base (e.g. a pathFilters/$schema/
            # whitespace tweak) excludes itself from NBGV height, so it adds none — predicting +1 there
            # would over-bump, and the weekly sync would later compute the true (lower) height and
            # correct it downward: a visible version regression.
            $bumps = [int](Test-HeightBearingChange -Plugin $name -From $BaseCommit -To $HeadCommit)
            $computed = "$base.$($heightAtBase + $bumps)"
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

    # Both required manifests are guaranteed to exist (guarded above); the Claude manifest is optional.
    # A mismatch in any present manifest — vs. the computed version — means the plugin drifted and
    # needs a rewrite. Gate the Claude check on presence (not on its version value) so a present-but-
    # malformed manifest still forces a rewrite and fails loudly at stamp time.
    $changed = ($computed -ne $current) -or ($computed -ne $currentCodex) -or
               ($hasClaudeManifest -and $computed -ne $currentClaude)

    if ($OnlyChanged -and -not $changed) { continue }

    if ($Write -and $changed) {
        [void](Set-ManifestVersion -Path $manifest -Version $computed)
        [void](Set-ManifestVersion -Path $codexManifest -Version $computed)
        # Stamp the optional Claude manifest only when the plugin actually has one.
        if ($hasClaudeManifest) {
            [void](Set-ManifestVersion -Path $claudeManifest -Version $computed)
        }
    }

    $results.Add([ordered]@{
        plugin   = $name
        current  = $current
        computed = $computed
        changed  = $changed
    })
}

$results | ConvertTo-Json -AsArray -Compress
