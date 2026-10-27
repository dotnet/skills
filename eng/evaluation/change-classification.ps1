function Test-JsonPropertiesUnique {
  param([System.Text.Json.JsonElement]$Element)

  if ($Element.ValueKind -eq [System.Text.Json.JsonValueKind]::Object) {
    $names = [System.Collections.Generic.HashSet[string]]::new(
      [System.StringComparer]::Ordinal)
    foreach ($property in $Element.EnumerateObject()) {
      if (-not $names.Add($property.Name)) {
        return $false
      }
      if (-not (Test-JsonPropertiesUnique -Element $property.Value)) {
        return $false
      }
    }
  } elseif ($Element.ValueKind -eq [System.Text.Json.JsonValueKind]::Array) {
    foreach ($item in $Element.EnumerateArray()) {
      if (-not (Test-JsonPropertiesUnique -Element $item)) {
        return $false
      }
    }
  }

  return $true
}

function Get-ManifestWithoutVersion {
  param(
    [string]$Content,
    [string]$Label = "plugin manifest"
  )

  $document = $null
  try {
    $options = [System.Text.Json.JsonDocumentOptions]::new()
    $options.AllowTrailingCommas = $false
    $options.CommentHandling = [System.Text.Json.JsonCommentHandling]::Disallow
    $document = [System.Text.Json.JsonDocument]::Parse($Content, $options)
    $root = $document.RootElement

    if ($root.ValueKind -ne [System.Text.Json.JsonValueKind]::Object) {
      throw "the JSON root must be an object"
    }
    if (-not (Test-JsonPropertiesUnique -Element $root)) {
      throw "duplicate JSON property names are not supported"
    }

    $versionProperties = @($root.EnumerateObject() |
      Where-Object { $_.Name -ceq "version" })
    if ($versionProperties.Count -ne 1) {
      throw "the top-level 'version' property must occur exactly once"
    }

    $versionProperty = $versionProperties[0]
    if ($versionProperty.Value.ValueKind -ne
        [System.Text.Json.JsonValueKind]::String) {
      throw "the top-level 'version' property must be a string"
    }

    $version = $versionProperty.Value.GetString()
    if ([string]::IsNullOrWhiteSpace($version) -or
        $version -notmatch '^\d+\.\d+\.\d+$') {
      throw "the top-level 'version' property must be major.minor.patch"
    }

    $manifest = $Content | ConvertFrom-Json -AsHashtable -Depth 100
    [void]$manifest.Remove("version")
    return $manifest | ConvertTo-Json -Compress -Depth 100
  } catch {
    Write-Warning "Treating $Label as a behavior change: $($_.Exception.Message)"
    return $null
  } finally {
    if ($null -ne $document) {
      $document.Dispose()
    }
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

      $baseManifest = Get-ManifestWithoutVersion `
        -Content ($baseLines -join "`n") -Label "${BaseCommit}:$path"
      $headManifest = Get-ManifestWithoutVersion `
        -Content ($headLines -join "`n") -Label "${HeadCommit}:$path"
      if ($null -eq $baseManifest -or $null -eq $headManifest -or
          $baseManifest -cne $headManifest) {
        return $plugin
      }
    } |
    Where-Object { $_ } |
    Sort-Object -Unique)
}
