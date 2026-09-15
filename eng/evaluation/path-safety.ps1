function Test-PathHasReparsePoint {
  param(
    [Parameter(Mandatory = $true)]
    [string]$AllowedRoot,
    [Parameter(Mandatory = $true)]
    [string]$Path
  )

  $root = [IO.Path]::TrimEndingDirectorySeparator(
    [IO.Path]::GetFullPath($AllowedRoot))
  $fullPath = [IO.Path]::GetFullPath($Path)
  try {
    if (([IO.File]::GetAttributes($root) -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
      return $true
    }
  } catch {
    return $true
  }

  $relative = [IO.Path]::GetRelativePath($root, $fullPath)
  if ($relative -eq ".") { return $false }
  if ([IO.Path]::IsPathRooted($relative) -or
      $relative -eq ".." -or
      $relative.StartsWith(".." + [IO.Path]::DirectorySeparatorChar) -or
      $relative.StartsWith(".." + [IO.Path]::AltDirectorySeparatorChar)) {
    return $true
  }

  $current = $root
  foreach ($segment in $relative.Split(
    @([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar),
    [StringSplitOptions]::RemoveEmptyEntries)) {
    $current = Join-Path $current $segment
    try {
      if (([IO.File]::GetAttributes($current) -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        return $true
      }
    } catch {
      return $true
    }
  }
  return $false
}
