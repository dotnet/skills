function Get-WorkflowEntries {
  param(
    [string]$contentRoot = ".",
    [string[]]$selectedPackages = @()
  )
  $root = [IO.Path]::GetFullPath((Join-Path $contentRoot "agentic-workflows"))
  $rootExists = Test-Path $root -PathType Container
  if (-not $rootExists -and $selectedPackages.Count -eq 0) { return @() }
  if ($rootExists -and (Test-PathHasReparsePoint -allowedRoot $root -path $root)) {
    throw "Workflow collection contains a symbolic link or reparse point"
  }
  $packages = @()
  if ($rootExists) {
    $packages = @(Get-ChildItem -LiteralPath $root -Directory | Sort-Object Name)
  }
  if ($packages.Count -eq 0 -and $selectedPackages.Count -eq 0) {
    throw "No workflow packages found"
  }
  if ($selectedPackages.Count -gt 0) {
    foreach ($name in $selectedPackages) {
      if ($name -notmatch '\A[A-Za-z0-9_-]+\z') {
        throw "Invalid workflow package name '$name'"
      }
      if ($name -notin $packages.Name) {
        $testsPackage = Join-Path $contentRoot "tests" "agentic-workflows" $name
        if (Test-Path $testsPackage -PathType Container) {
          throw "Workflow package '$name' has no aw.yml"
        }
        throw "Unknown workflow package '$name'"
      }
    }
    $packages = @($packages | Where-Object { $_.Name -in $selectedPackages })
  }
  foreach ($package in $packages) {
    $name = $package.Name
    $manifest = Join-Path $package.FullName "aw.yml"
    if (-not (Test-Path $manifest -PathType Leaf)) {
      throw "Workflow package '$name' has no aw.yml"
    }
    $eval = Join-Path $contentRoot "tests" "agentic-workflows" $name "eval.yaml"
    if (-not (Test-Path $eval -PathType Leaf)) {
      throw "Workflow package '$name' has no eval.yaml"
    }
    foreach ($path in @($manifest, $eval)) {
      if (Test-PathHasReparsePoint -allowedRoot ([IO.Path]::GetFullPath($contentRoot)) -path $path) {
        throw "Workflow evaluation path contains a symbolic link or reparse point: $path"
      }
    }
    @{
      name = "agentic-workflows--$name"
      plugin = "agentic-workflows"
      target_kind = "workflow"
      skills_path = ""
      agents_path = ""
      package_path = "agentic-workflows/$name/aw.yml"
      eval_path = "tests/agentic-workflows/$name/eval.yaml"
    }
  }
}
