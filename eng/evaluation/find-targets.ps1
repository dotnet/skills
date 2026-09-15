$entries = @()
$plugins = @()
. (Join-Path $PWD "eng/evaluation/path-safety.ps1")

# Build matrix entries for a full-plugin evaluation, sharding skills
# that have eval specs by the optional `executionShard:` top-level tag
# in tests/<plugin>/<skill>/eval.yaml. Untagged evals fall into a
# synthetic "default" bucket. Plugins with all skills in one bucket
# produce a single entry (unchanged behavior); plugins with multiple
# buckets fan out into one matrix entry per shard so each shard
# runs on a fresh GitHub-hosted runner. This is the mitigation for
# MCP-heavy plugins like dotnet-msbuild, where cumulative runner
# resource usage across ~90 min of continuous MCP traffic
# consistently triggered host-level termination.
function Get-PluginShardEntries {
  param(
    [string]$plugin,
    [string]$contentRoot = ".",
    [string[]]$selectedSkills = @()
  )
  $skillsDir = "plugins/$plugin/skills"
  $skillsRoot = Join-Path $contentRoot $skillsDir
  $singleEntry = @{
    name = $plugin
    plugin = $plugin
    target_kind = "skill"
    skills_path = $skillsDir
    agents_path = ""
    eval_path = ""
  }
  if (-not (Test-Path $skillsRoot)) {
    return @()
  }
  $skillDirs = @(Get-ChildItem -Path $skillsRoot -Directory -ErrorAction SilentlyContinue |
    Sort-Object Name | Select-Object -ExpandProperty Name)
  if ($selectedSkills.Count -gt 0) {
    $selected = @{}
    foreach ($skill in $selectedSkills) { $selected[$skill] = $true }
    $skillDirs = @($skillDirs | Where-Object { $selected.ContainsKey($_) })
  }
  if ($skillDirs.Count -eq 0) {
    return @()
  }
  $shardGroups = @{}
  $evalSkills = @()
  foreach ($skill in $skillDirs) {
    $evalPath = Join-Path $contentRoot "tests" $plugin $skill "eval.yaml"
    if (-not (Test-Path $evalPath)) { continue }
    $evalSkills += $skill
    $shard = "default"
    # Allow optional leading whitespace so an accidentally indented
    # executionShard: key still groups correctly (rather than silently
    # collapsing back to the default bucket).
    $m = Select-String -Path $evalPath -Pattern '^\s*executionShard:\s*[\x27"]?([\w.\-]+)' -List
    if ($m) { $shard = $m.Matches[0].Groups[1].Value }
    if (-not $shardGroups.ContainsKey($shard)) { $shardGroups[$shard] = @() }
    $shardGroups[$shard] += $skill
  }
  if ($shardGroups.Count -eq 0) { return @() }
  if ($shardGroups.Count -le 1) {
    if ($selectedSkills.Count -gt 0) {
      $paths = ($evalSkills | ForEach-Object { "$skillsDir/$_" }) -join ' '
      $name = if ($evalSkills.Count -eq 1) { "$plugin--$($evalSkills[0])" } else { $plugin }
      return @(@{
        name = $name
        plugin = $plugin
        target_kind = "skill"
        skills_path = $paths
        agents_path = ""
        eval_path = ""
      })
    }
    return @($singleEntry)
  }
  return @($shardGroups.Keys | Sort-Object | ForEach-Object {
    $shardName = $_
    $paths = ($shardGroups[$shardName] | ForEach-Object { "$skillsDir/$_" }) -join ' '
    @{
      name = "$plugin--shard-$shardName"
      plugin = $plugin
      target_kind = "skill"
      skills_path = $paths
      agents_path = ""
      eval_path = ""
    }
  })
}

function Resolve-AgentEvalPath {
  param(
    [string]$plugin,
    [string]$agent,
    [string]$contentRoot = "."
  )
  $testsRoot = Join-Path $contentRoot "tests" $plugin
  $candidates = @(
    (Join-Path $testsRoot "agent.$agent" "eval.yaml"),
    (Join-Path $testsRoot $agent "eval.yaml")
  )
  if (Test-Path $testsRoot) {
    foreach ($subDir in Get-ChildItem -Path $testsRoot -Directory -ErrorAction SilentlyContinue) {
      $candidates += Join-Path $subDir.FullName "agent.$agent" "eval.yaml"
      $candidates += Join-Path $subDir.FullName $agent "eval.yaml"
    }
  }
  foreach ($candidate in $candidates) {
    if (Test-Path $candidate) {
      return [IO.Path]::GetRelativePath(
        [IO.Path]::GetFullPath($contentRoot),
        [IO.Path]::GetFullPath($candidate)
      ).Replace('\', '/')
    }
  }
  return $null
}

function Get-PluginAgentEntries {
  param(
    [string]$plugin,
    [string]$contentRoot = ".",
    [string[]]$selectedAgents = @()
  )
  $pluginRoot = [IO.Path]::GetFullPath((Join-Path $contentRoot "plugins" $plugin))
  $manifestPath = Join-Path $pluginRoot "plugin.json"
  if (-not (Test-Path $manifestPath)) { return @() }
  try {
    $manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json
  } catch {
    Write-Warning "Skipping agent discovery for '$plugin': malformed plugin.json"
    return @()
  }
  $declaredPaths = @($manifest.agents | Where-Object { -not [string]::IsNullOrWhiteSpace("$_") })
  if ($declaredPaths.Count -eq 0) { $declaredPaths = @("./agents/") }

  $agentFiles = @()
  foreach ($declaredPath in $declaredPaths) {
    $candidate = [IO.Path]::GetFullPath((Join-Path $pluginRoot "$declaredPath"))
    $pluginPrefix = $pluginRoot.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if (-not $candidate.StartsWith($pluginPrefix, [StringComparison]::Ordinal)) {
      Write-Warning "Skipping agent path outside plugin '$plugin': $declaredPath"
      continue
    }
    if (-not (Test-Path $candidate)) { continue }
    if (Test-PathHasReparsePoint -allowedRoot $pluginRoot -path $candidate) {
      throw "Agent path '$declaredPath' contains a symbolic link or reparse point"
    }
    if (Test-Path $candidate -PathType Container) {
      $agentFiles += Get-ChildItem -Path $candidate -Filter "*.agent.md" -File -ErrorAction SilentlyContinue
    } elseif ((Test-Path $candidate -PathType Leaf) -and $candidate.EndsWith(".agent.md", [StringComparison]::OrdinalIgnoreCase)) {
      $agentFiles += Get-Item $candidate
    }
  }

  $selected = @{}
  foreach ($agent in $selectedAgents) { $selected[$agent] = $true }
  return @($agentFiles |
    Sort-Object FullName -Unique |
    Sort-Object Name |
    ForEach-Object {
      $agent = $_.Name -replace '\.agent\.md$', ''
      if (Test-PathHasReparsePoint -allowedRoot $pluginRoot -path $_.FullName) {
        throw "Agent file '$($_.FullName)' contains a symbolic link or reparse point"
      }
      $evalPath = Resolve-AgentEvalPath -plugin $plugin -agent $agent -contentRoot $contentRoot
      if ($evalPath -and
          ($selectedAgents.Count -eq 0 -or $selected.ContainsKey($agent))) {
        $agentPath = [IO.Path]::GetRelativePath(
          [IO.Path]::GetFullPath($contentRoot),
          [IO.Path]::GetFullPath($_.FullName)
        ).Replace('\', '/')
        @{
          name = "$plugin--agent.$agent"
          plugin = $plugin
          target_kind = "agent"
          skills_path = ""
          agents_path = $agentPath
          eval_path = $evalPath
        }
      }
    })
}

if ("$env:GATE_PR_NUMBER" -ne "") {
  # Single-PR run (/evaluate comment, /evaluate review, or the
  # evaluate-now label): detect individual changed skills against the
  # exact commit the gate bound this run to. Never re-resolve the live
  # branch head here; always use the gate-bound SHA.
  $base = "$env:GATE_BASE_SHA"
  $head = "$env:GATE_HEAD_SHA"

  # Use a worktree so Test-Path checks are against the bound commit's
  # content (present in the local repo via the refs/pull fetch above).
  # Fail closed: if the bound commit cannot be checked out (e.g. it is
  # no longer present in the repo), stop here rather than continue and
  # report success for a commit we did not actually evaluate.
  git worktree add /tmp/pr-content "$env:GATE_HEAD_SHA"
  if ($LASTEXITCODE -ne 0) {
    throw "Bound commit $env:GATE_HEAD_SHA could not be checked out (it may no longer be present). Failing closed."
  }
  $contentRoot = "/tmp/pr-content"

  $mergeBase = git merge-base $base $head
  $changedFiles = git diff --name-only --diff-filter=ACMR $mergeBase $head

  # Check if any changed files are in infrastructure paths. The native
  # custom-agent lane executes eng/skill-validator/src, so those source
  # changes are evaluation infrastructure changes.
  $hasInfraChanges = $changedFiles |
    Where-Object {
      ($_ -match '^eng/vally-adapter/') -or
      ($_ -match '^eng/evaluation/(?:find-targets|path-safety)\.ps1$') -or
      ($_ -match '^eng/skill-validator/src/') -or
      ($_ -match '^dotnet-skills\.experiment\.yaml$') -or
      $_ -match '^\.github/workflows/(evaluation|evaluation-run)\.yml$'
    } |
    Select-Object -First 1

  # Also check for skill, agent, and test changes so we don't lose them.
  $hasSkillChanges = $changedFiles |
    Where-Object { $_ -match '^(?:plugins/[^/]+/plugin\.json$|plugins/[^/]+/skills/[^/]+/|plugins/[^/]+/(?:[^/]+/)*[^/]+\.agent\.md$|tests/[^/]+/[^/]+/)' } |
    Select-Object -First 1

  if ($hasInfraChanges -and -not $hasSkillChanges) {
    echo "is_infra=true" >> $env:GITHUB_OUTPUT
    # Infra-only: evaluate a small random subset of plugins to keep
    # the smoke-test fast while still catching regressions.
    $allPlugins = @(Get-ChildItem -Path (Join-Path $contentRoot "plugins") -Directory -ErrorAction SilentlyContinue |
      Where-Object {
        (Test-Path (Join-Path $_.FullName "plugin.json")) -and
        (Test-Path (Join-Path $contentRoot "tests" $_.Name))
      } |
      Select-Object -ExpandProperty Name)
    $plugins = @($allPlugins | Get-Random -Count ([Math]::Min(2, $allPlugins.Count)))
    Write-Host "Infrastructure changes detected, evaluating random subset: $($plugins -join ', ')"
    $entries = @($plugins | ForEach-Object {
      Get-PluginShardEntries -plugin $_ -contentRoot $contentRoot
      Get-PluginAgentEntries -plugin $_ -contentRoot $contentRoot
    })
  } else {
    # Extract unique plugin/skill pairs from changed skill sources and
    # non-agent eval directories.
    $changedPairs = @($changedFiles |
      Where-Object {
        $_ -match '^plugins/([^/]+)/skills/([^/]+)/' -or
        ($_ -match '^tests/([^/]+)/([^/]+)/' -and $Matches[2] -notlike 'agent.*')
      } |
      ForEach-Object {
        if ($_ -match '^plugins/([^/]+)/skills/([^/]+)/') {
          "$($Matches[1])/$($Matches[2])"
        } elseif ($_ -match '^tests/([^/]+)/([^/]+)/') {
          "$($Matches[1])/$($Matches[2])"
        }
      } |
      Sort-Object -Unique)

    $changedAgentSourcePlugins = @($changedFiles |
      ForEach-Object {
        if ($_ -match '^plugins/([^/]+)/(?:[^/]+/)*[^/]+\.agent\.md$') {
          $Matches[1]
        }
      } |
      Where-Object { $_ } |
      Sort-Object -Unique)
    $changedSkillSourcePlugins = @($changedFiles |
      ForEach-Object {
        if ($_ -match '^plugins/([^/]+)/skills/[^/]+/') {
          $Matches[1]
        }
      } |
      Where-Object { $_ } |
      Sort-Object -Unique)
    $changedManifestPlugins = @($changedFiles |
      ForEach-Object {
        if ($_ -match '^plugins/([^/]+)/plugin\.json$') {
          $Matches[1]
        }
      } |
      Where-Object { $_ } |
      Sort-Object -Unique)
    $changedTestPlugins = @($changedFiles |
      ForEach-Object {
        if ($_ -match '^tests/([^/]+)/') { $Matches[1] }
      } |
      Where-Object { $_ } |
      Sort-Object -Unique)

    # Filter to skills that have a SKILL.md and a tests directory,
    # then group the changed skills by plugin and apply the same
    # executionShard bucketing used by schedule/full-plugin runs.
    $changedSkillsByPlugin = @{}
    $changedPairs | ForEach-Object {
      $parts = $_ -split '/'
      $plugin = $parts[0]
      $skill = $parts[1]
      $skillMd = Join-Path $contentRoot "plugins" $plugin "skills" $skill "SKILL.md"
      $testsDir = Join-Path $contentRoot "tests" $plugin
      if ((Test-Path $skillMd) -and (Test-Path $testsDir)) {
        if (-not $changedSkillsByPlugin.ContainsKey($plugin)) { $changedSkillsByPlugin[$plugin] = @() }
        $changedSkillsByPlugin[$plugin] += $skill
      }
    }
    $entries = @($changedSkillsByPlugin.Keys |
      Where-Object { $_ -notin $changedManifestPlugins } |
      Sort-Object |
      ForEach-Object {
      $plugin = $_
      Get-PluginShardEntries -plugin $plugin -contentRoot $contentRoot -selectedSkills ($changedSkillsByPlugin[$plugin] | Sort-Object -Unique)
    })
    $entries += @($changedManifestPlugins | ForEach-Object {
      Get-PluginShardEntries -plugin $_ -contentRoot $contentRoot
    })

    $agentPlugins = @(
      $changedAgentSourcePlugins + $changedSkillSourcePlugins + $changedManifestPlugins + $changedTestPlugins |
        Sort-Object -Unique)
    $entries += @($agentPlugins | ForEach-Object {
      $plugin = $_
      # Any agent source can be a declared dependency of another evaluated
      # agent. Skills and shared test fixtures can also be dependencies, and
      # manifest changes alter the production registration surface. Rerun
      # every agent eval in an affected plugin rather than miss an indirect edge.
      Get-PluginAgentEntries -plugin $plugin -contentRoot $contentRoot
    })
  }

  git worktree remove /tmp/pr-content --force 2>$null
} else {
  # Schedule and workflow_dispatch: evaluate full plugins.
  # Schedule covers everything; workflow_dispatch can optionally
  # narrow to a single plugin via the `plugin` input for ad-hoc
  # smoke tests without committing to main.
  # Exclude experimental plugins from scheduled runs — they are
  # evaluated only on-demand via /evaluate on PRs.
  $excludeFromSchedule = @('dotnet-experimental')
  $dispatchPlugin = "$env:INPUT_PLUGIN"
  if ("$env:EVAL_EVENT_NAME" -eq "workflow_dispatch" -and $dispatchPlugin) {
    # Restrict to a simple directory-name shape before any path
    # construction, so values like '../foo' or 'foo/bar' cannot
    # traverse outside plugins/ or tests/.
    if ($dispatchPlugin -notmatch '^[a-zA-Z0-9._-]+$') {
      throw "workflow_dispatch input plugin='$dispatchPlugin' must match ^[a-zA-Z0-9._-]+$ (single directory name, no path separators)"
    }
    if (-not (Test-Path (Join-Path "plugins" $dispatchPlugin "plugin.json")) -or
        -not (Test-Path (Join-Path "tests" $dispatchPlugin))) {
      throw "workflow_dispatch input plugin='$dispatchPlugin' is not a valid plugin (must have plugin.json and tests/<name>)"
    }
    $plugins = @($dispatchPlugin)
    Write-Host "workflow_dispatch: evaluating only $dispatchPlugin"
  } else {
    $plugins = @(Get-ChildItem -Path "plugins" -Directory |
      Where-Object {
        (Test-Path (Join-Path $_.FullName "plugin.json")) -and
        (Test-Path (Join-Path "tests" $_.Name)) -and
        ($_.Name -notin $excludeFromSchedule)
      } |
      Select-Object -ExpandProperty Name)
  }
  $entries = @($plugins | ForEach-Object {
    Get-PluginShardEntries -plugin $_
    Get-PluginAgentEntries -plugin $_
  })
}
# Only plugins represented by a non-empty eval entry are downstream
# publication targets.
$plugins = @($entries | ForEach-Object { $_.plugin } | Sort-Object -Unique)

# ---- Cross-family model dimension (IMPACT-ANALYSIS.md §10) ----------
# Resolve a matrix profile, then expand each plugin/shard entry across
# the selected executor models, attaching a cross-family primary judge
# (judge is never the same MODEL as the executor, and cross-family
# where possible) plus, for the scheduled dual-judge cadence, an
# optional within-family second judge. Every trigger resolves to a
# cross-family profile: the two DEFAULT models are the floor (every PR
# gate and every scheduled day a heavier tier does not occupy), and
# heavier profiles are opt-in via a workflow_dispatch matrix_profile,
# a /evaluate flag on a PR, or a scheduled cadence day.
#
# Event bodies arrive via env (never inline interpolation) and are only
# regex-matched here, never executed — no command-injection surface.
$matrixProfile = 'default'
$dualJudge = $false
$evt = "$env:EVAL_EVENT_NAME"
$evalBody = @("$env:EVAL_COMMENT_BODY", "$env:EVAL_REVIEW_BODY") |
  Where-Object { $_ -match '(^|\s)/evaluate(\s|$)' } | Select-Object -First 1
if ($evt -eq 'workflow_dispatch') {
  # Manual dispatch: an explicit profile wins; anything else (empty
  # input, a PR-gate auto-dispatch, or a whole-repo sweep) falls through
  # to the two DEFAULT cross-family models set above.
  $mp = "$env:MATRIX_PROFILE_INPUT"
  if ($mp -and $mp -in @('default','mid','opus48','full','newer')) {
    $matrixProfile = $mp
  }
} elseif ($evalBody) {
  # PR gate: the DEFAULT models run on every authorized /evaluate. Flags
  # opt into heavier coverage: --mid (cheap models), --opus48 (opus-4.8),
  # --newer (frontier), --full (the expensive super-set). --full is
  # intentionally under-advertised; warn when it is used so it stays rare.
  # Anchor each flag on token boundaries ((?:^|\s) before, (?=\s|$)
  # after) so a malformed near-miss like --fuller, --opus480, or
  # --midnight does NOT silently select an expensive profile — it falls
  # through to the default. \s boundaries also match newlines, so a flag
  # anywhere in a multi-line review body is still recognized.
  if ($evalBody -match '(?:^|\s)--full(?:-matrix)?(?=\s|$)') {
    $matrixProfile = 'full'
    Write-Host "::warning::/evaluate --full requested: runs defaults + haiku + mai + gpt-5.3-codex + opus-4.8. This is expensive — exercise sparingly."
  }
  elseif ($evalBody -match '(?:^|\s)--newer(?=\s|$)') { $matrixProfile = 'newer' }
  elseif ($evalBody -match '(?:^|\s)--opus48(?=\s|$)') { $matrixProfile = 'opus48' }
  elseif ($evalBody -match '(?:^|\s)--mid(?=\s|$)') { $matrixProfile = 'mid' }
  else { $matrixProfile = 'default' }
} elseif ($evt -in @('pull_request','pull_request_target')) {
  # Same-repo / fork PR events. The heavy evaluation only runs once a
  # maintainer authorizes it through the gate, but when it does the PR
  # gate defaults to the two DEFAULT models (cross-family).
  $matrixProfile = 'default'
} elseif ($evt -eq 'schedule') {
  # Deterministic weekly cadence, keyed off the exact cron string that
  # triggered this run (github.event.schedule). Reading the triggering
  # cron — not the wall clock — means a re-run of a scheduled run always
  # resolves the SAME profile it did originally (the old Get-Date logic
  # picked a different profile when re-run on another day).
  #
  #   Mon/Wed/Fri -> default  (the 2 default models; the daily floor)
  #   Tue/Sat     -> mid      (cheap models, lower-cadence signal)
  #   Thu         -> opus48   (opus-4.8 alone; no defaults that day)
  #   Sun         -> newer    (frontier models alone; no defaults)
  #
  # Heavy tiers (opus48, newer) deliberately REPLACE the defaults on their
  # day rather than co-run them, to cap spend on the expensive models.
  $sched = "$env:EVAL_SCHEDULE"
  switch ($sched) {
    '0 7 * * 1,3,5' { $matrixProfile = 'default' }
    '0 7 * * 2,6'   { $matrixProfile = 'mid' }
    '0 7 * * 4'     { $matrixProfile = 'opus48' }
    '0 7 * * 0'     { $matrixProfile = 'newer' }
    default         { $matrixProfile = 'default' }
  }
  # Judge-comparison experiment: run the optional second judge on every
  # scheduled run for the comparison window, so claude-opus-4.8 (primary)
  # and claude-haiku-4.5 (judge2) score the same GPT-executor transcripts
  # and can be compared before switching the primary judge to the cheaper model.
  $dualJudge = $true
}

if ($matrixProfile -in @('default','mid','opus48','full','newer')) {
  # §10.2 model tiers. Cost-optimized cadence: the two DEFAULT models
  # ({sonnet-5, gpt-5.6-luna}) carry every PR and every scheduled day that
  # no heavier tier occupies. The cheap MID models run at a lower cadence
  # for periodic signal. The expensive OPUS48 and frontier NEWER tiers run
  # once a week and DO NOT co-run the defaults (they replace them that
  # day). FULL is the opt-in PR super-set (defaults + mid + opus48); it is
  # expensive and deliberately under-advertised — use sparingly.
  $profileModels = @{
    'default' = @('claude-sonnet-5', 'gpt-5.6-luna')
    'mid'     = @('claude-haiku-4.5', 'mai-code-1.1-flash', 'gpt-5.3-codex')
    'opus48'  = @('claude-opus-4.8')
    'full'    = @('claude-sonnet-5', 'gpt-5.6-luna', 'claude-haiku-4.5', 'mai-code-1.1-flash', 'gpt-5.3-codex', 'claude-opus-4.8')
    'newer'   = @('gpt-5.6-sol', 'claude-opus-5', 'claude-sonnet-5')
  }
  # §10.5 judge routing. Primary = cross-family (never the same model, and
  # a different family in every case here). judge2 is the OPTIONAL second
  # judge used only on the scheduled dual-judge cadence.
  #   - Non-GPT executor legs (Claude/MAI) are judged by gpt-5.6-terra with
  #     NO second judge (judge2 = '').
  #   - GPT executor legs are judged primarily by claude-opus-4.8 and carry
  #     judge2 = claude-haiku-4.5, so the same transcripts are scored by
  #     both judges on the scheduled dual-judge cadence and can be compared.
  $judgeRoutes = @{
    'claude-opus-4.8'         = @{ judge = 'gpt-5.6-terra'; judge2 = '' }
    'gpt-5.6-luna'            = @{ judge = 'claude-opus-4.8'; judge2 = 'claude-haiku-4.5' }
    'claude-haiku-4.5'        = @{ judge = 'gpt-5.6-terra'; judge2 = '' }
    'mai-code-1.1-flash'       = @{ judge = 'gpt-5.6-terra'; judge2 = '' }
    'gpt-5.3-codex'           = @{ judge = 'claude-opus-4.8'; judge2 = 'claude-haiku-4.5' }
    'gpt-5.6-sol'             = @{ judge = 'claude-opus-4.8'; judge2 = 'claude-haiku-4.5' }
    'claude-sonnet-5'         = @{ judge = 'gpt-5.6-terra'; judge2 = '' }
    'claude-opus-5'           = @{ judge = 'gpt-5.6-terra'; judge2 = '' }
  }
  $models = $profileModels[$matrixProfile]
  $expanded = @()
  foreach ($e in $entries) {
    foreach ($m in $models) {
      $route = $judgeRoutes[$m]
      if (-not $route) { throw "No judge route defined for model '$m'" }
      $j2 = if ($dualJudge) { "$($route.judge2)" } else { '' }
      $expanded += @{
        name        = "$($e.name)--$m"
        plugin      = $e.plugin
        target_kind = if ($e.target_kind) { $e.target_kind } else { "skill" }
        skills_path = $e.skills_path
        agents_path = $e.agents_path
        eval_path   = $e.eval_path
        model       = $m
        judge       = $route.judge
        judge2      = if ($e.target_kind -eq "agent") { "" } else { $j2 }
      }
    }
  }
  $entries = $expanded
  Write-Host "Cross-family profile '$matrixProfile' (dualJudge=$dualJudge): expanded to $($entries.Count) entries across $($models.Count) model(s)"
}

# Validate every entry against a strict allowlist before it enters
# the matrix consumed by the vally evaluation. A lone-dot
# component ('.') matches the charset and holds no '..', but it is a
# path-normalization token, so reject it for names and path segments.
$namePattern = '\A[A-Za-z0-9._-]+\z'
$pathPattern = '\Aplugins/[A-Za-z0-9._-]+/skills(/[A-Za-z0-9._-]+)?\z'
$agentPathPattern = '\Aplugins/[A-Za-z0-9._-]+/(?:[A-Za-z0-9._-]+/)*[A-Za-z0-9._-]+\.agent\.md\z'
$evalPathPattern = '\Atests/[A-Za-z0-9._-]+/(?:[A-Za-z0-9._-]+/)+eval\.yaml\z'
foreach ($e in $entries) {
  if ("$($e.plugin)" -notmatch $namePattern -or "$($e.plugin)" -match '\.\.' -or "$($e.plugin)" -eq '.') {
    throw "Refusing unsafe matrix entry: plugin '$($e.plugin)' must match $namePattern, not be '.', and not contain '..'"
  }
  if ("$($e.name)" -notmatch $namePattern -or "$($e.name)" -match '\.\.' -or "$($e.name)" -eq '.') {
    throw "Refusing unsafe matrix entry: name '$($e.name)' must match $namePattern, not be '.', and not contain '..'"
  }
  if ("$($e.target_kind)" -notin @('skill', 'agent')) {
    throw "Refusing unsafe matrix entry: target_kind '$($e.target_kind)' must be 'skill' or 'agent'"
  }
  # model/judge are always present; judge2 is optional (empty unless the
  # scheduled dual-judge cadence sets it). All flow into CLI args in the
  # runner, so hold them to the same strict allowlist as names.
  foreach ($mk in @('model', 'judge', 'judge2')) {
    $mv = "$($e[$mk])"
    if ($mv -ne '' -and ($mv -notmatch $namePattern -or $mv -match '\.\.' -or $mv -eq '.')) {
      throw "Refusing unsafe matrix entry: $mk '$mv' for entry '$($e.name)' must match $namePattern, not be '.', and not contain '..'"
    }
  }
  # skills_path is a space-separated list of one or more path segments.
  # An empty or whitespace-only value must hard-fail: it is later used
  # to build CLI arguments, so a blank entry is a configuration error,
  # not a no-op. Filter on the trimmed value so whitespace-only
  # segments count as empty (matching the bash validation step).
  $spSegments = @("$($e.skills_path)" -split ' ' | Where-Object { $_.Trim() })
  # Each segment must also belong to THIS entry's plugin: $pathPattern
  # only checks the generic shape, so bind the prefix to $e.plugin via
  # ordinal string comparison. This blocks a mismatched entry such as
  # plugin=foo + skills_path=plugins/bar/skills, which downstream steps
  # assume shares the entry's plugin prefix.
  $skillsPrefix = "plugins/$($e.plugin)/skills"
  foreach ($sp in $spSegments) {
    if ($sp -notmatch $pathPattern -or $sp -match '\.\.') {
      throw "Refusing unsafe matrix entry: skills_path segment '$sp' must match $pathPattern and not contain '..'"
    }
    # Reject a lone-dot path component ('plugins/./skills' or '.../skills/.').
    if ($sp -match '/\./' -or $sp -match '/\.$') {
      throw "Refusing unsafe matrix entry: skills_path segment '$sp' contains a '.' path component"
    }
    if ($sp -cne $skillsPrefix -and -not $sp.StartsWith("$skillsPrefix/", [System.StringComparison]::Ordinal)) {
      throw "Refusing unsafe matrix entry: skills_path segment '$sp' does not belong to plugin '$($e.plugin)'"
    }
  }
  $agentSegments = @("$($e.agents_path)" -split ' ' | Where-Object { $_.Trim() })
  foreach ($ap in $agentSegments) {
    if ($ap -notmatch $agentPathPattern -or $ap -match '\.\.') {
      throw "Refusing unsafe matrix entry: agents_path segment '$ap' must match $agentPathPattern and not contain '..'"
    }
    if (-not $ap.StartsWith("plugins/$($e.plugin)/", [System.StringComparison]::Ordinal)) {
      throw "Refusing unsafe matrix entry: agents_path segment '$ap' does not belong to plugin '$($e.plugin)'"
    }
  }
  $evalPath = "$($e.eval_path)"
  if ($evalPath -and ($evalPath -notmatch $evalPathPattern -or $evalPath -match '\.\.')) {
    throw "Refusing unsafe matrix entry: eval_path '$evalPath' must match $evalPathPattern and not contain '..'"
  }
  if ($evalPath -and -not $evalPath.StartsWith("tests/$($e.plugin)/", [System.StringComparison]::Ordinal)) {
    throw "Refusing unsafe matrix entry: eval_path '$evalPath' does not belong to plugin '$($e.plugin)'"
  }
  if ($e.target_kind -eq 'skill' -and $spSegments.Count -eq 0) {
    throw "Refusing unsafe matrix entry: skill target '$($e.name)' has no skills_path"
  }
  if ($e.target_kind -eq 'agent' -and $agentSegments.Count -eq 0) {
    throw "Refusing unsafe matrix entry: agent target '$($e.name)' has no agents_path"
  }
  if ($e.target_kind -eq 'agent' -and -not $evalPath) {
    throw "Refusing unsafe matrix entry: agent target '$($e.name)' has no eval_path"
  }
}

# Output entries for evaluate matrix
if (-not $entries -or $entries.Count -eq 0) {
  Write-Host "No entries to evaluate"
  echo "entries=[]" >> $env:GITHUB_OUTPUT
  echo "has_entries=false" >> $env:GITHUB_OUTPUT
} else {
  $json = $entries | ConvertTo-Json -Compress -AsArray
  Write-Host "Entries to evaluate: $json"
  echo "entries=$json" >> $env:GITHUB_OUTPUT
  echo "has_entries=true" >> $env:GITHUB_OUTPUT
}

# Output plugins for publish jobs
if (-not $plugins -or $plugins.Count -eq 0) {
  echo "plugins=[]" >> $env:GITHUB_OUTPUT
  echo "has_plugins=false" >> $env:GITHUB_OUTPUT
} else {
  $cjson = $plugins | ConvertTo-Json -Compress -AsArray
  echo "plugins=$cjson" >> $env:GITHUB_OUTPUT
  echo "has_plugins=true" >> $env:GITHUB_OUTPUT
}
