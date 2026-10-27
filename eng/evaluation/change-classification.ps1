function Get-ManifestWithoutVersion {
  param([string]$Content)

  try {
    $manifest = $Content | ConvertFrom-Json -AsHashtable
    [void]$manifest.Remove("version")
    return $manifest | ConvertTo-Json -Compress -Depth 100
  } catch {
    return $null
  }
}

function Get-BehaviorChangedPluginManifests {
  param(
    [string[]]$ChangedFiles,
    [string]$BaseCommit,
    [string]$HeadCommit
  )

  return @($ChangedFiles |
    ForEach-Object {
      if ($_ -notmatch '^plugins/([^/]+)/plugin\.json$') {
        return
      }

      $plugin = $Matches[1]
      $path = $_
      $baseLines = @(git show "${BaseCommit}:$path" 2>$null)
      $baseExit = $LASTEXITCODE
      $headLines = @(git show "${HeadCommit}:$path" 2>$null)
      $headExit = $LASTEXITCODE

      if ($baseExit -ne 0 -or $headExit -ne 0) {
        return $plugin
      }

      $baseManifest = Get-ManifestWithoutVersion -Content ($baseLines -join "`n")
      $headManifest = Get-ManifestWithoutVersion -Content ($headLines -join "`n")
      if ($null -eq $baseManifest -or $null -eq $headManifest -or
          $baseManifest -cne $headManifest) {
        return $plugin
      }
    } |
    Where-Object { $_ } |
    Sort-Object -Unique)
}
