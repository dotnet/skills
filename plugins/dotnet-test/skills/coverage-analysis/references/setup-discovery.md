# Coverage Analysis — setup and discovery

Read this file at the start of Phase 1. It carries the discovery, output-directory and existing-report probes referenced by `SKILL.md`.

## Step 1: Locate the solution or project

Given the user's path (default: current directory), find the entry point:

```powershell
$root = "<user-provided-path-or-current-directory>"

# Prefer solution file; fall back to project file
$sln = Get-ChildItem -Path $root -Filter "*.sln" -Recurse -Depth 2 -ErrorAction SilentlyContinue |
    Select-Object -First 1
if ($sln) {
    Write-Host "ENTRY_TYPE:Solution"; Write-Host "ENTRY:$($sln.FullName)"
} else {
    $project = Get-ChildItem -Path $root -Filter "*.csproj" -Recurse -Depth 2 -ErrorAction SilentlyContinue |
        Select-Object -First 1
    if ($project) {
        Write-Host "ENTRY_TYPE:Project"; Write-Host "ENTRY:$($project.FullName)"
    } else {
        Write-Host "ENTRY_TYPE:NotFound"
    }
}

# Test projects: search path first, then git root, then parent
$searchRoots = @($root)
$gitRoot = (git -C $root rev-parse --show-toplevel 2>$null)
if ($gitRoot) { $gitRoot = [System.IO.Path]::GetFullPath($gitRoot) }
if ($gitRoot -and $gitRoot -ne $root) { $searchRoots += $gitRoot }
$parentPath = Split-Path $root -Parent
if ($parentPath -and $parentPath -ne $root -and $parentPath -ne $gitRoot) { $searchRoots += $parentPath }

$testProjects = @()
foreach ($sr in $searchRoots) {
    # Primary: match by .csproj content (test framework references)
    $testProjects = @(Get-ChildItem -Path $sr -Filter "*.csproj" -Recurse -Depth 5 -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch '([/\\]obj[/\\]|[/\\]bin[/\\])' } |
        Where-Object { (Select-String -Path $_.FullName -Pattern 'Microsoft\.NET\.Test\.Sdk|xunit|nunit|MSTest\.TestAdapter|"MSTest"|MSTest\.TestFramework|TUnit' -Quiet) })
    if ($testProjects.Count -gt 0) {
        if ($sr -ne $root) { Write-Host "SEARCHED:$sr" }
        break
    }
}

# Fallback: match by file name convention
if ($testProjects.Count -eq 0) {
    foreach ($sr in $searchRoots) {
        $testProjects = @(Get-ChildItem -Path $sr -Filter "*.csproj" -Recurse -Depth 5 -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -match '(?i)(test|spec)' })
        if ($testProjects.Count -gt 0) {
            if ($sr -ne $root) { Write-Host "SEARCHED:$sr" }
            break
        }
    }
}
Write-Host "TEST_PROJECTS:$($testProjects.Count)"
$testProjects | ForEach-Object { Write-Host "TEST_PROJECT:$($_.FullName)" }

# Resolve the test output root (where coverage-analysis artifacts will be written)
if ($testProjects.Count -eq 0) {
    if ($gitRoot) {
        $testOutputRoot = $gitRoot
    } else {
        $testOutputRoot = $root
    }
} elseif ($testProjects.Count -eq 1) {
    $testOutputRoot = $testProjects[0].DirectoryName
} else {
    # Multiple test projects — find their deepest common parent directory
    $dirs = $testProjects | ForEach-Object { $_.DirectoryName }
    $common = $dirs[0]
    foreach ($d in $dirs[1..($dirs.Count-1)]) {
        $sep = [System.IO.Path]::DirectorySeparatorChar
        while (-not $d.StartsWith("$common$sep", [System.StringComparison]::OrdinalIgnoreCase) -and $d -ne $common) {
            $prevCommon = $common
            $common = Split-Path $common -Parent
            # Terminate if we can no longer move up (at filesystem root or no parent)
            if ([string]::IsNullOrEmpty($common) -or $common -eq $prevCommon) {
                $common = $null
                break
            }
        }
    }
    if ([string]::IsNullOrEmpty($common)) {
        # Fallback when no common parent directory exists (e.g., projects on different drives)
        if ($gitRoot) {
            $testOutputRoot = $gitRoot
        } else {
            $testOutputRoot = $root
        }
    } else {
        $testOutputRoot = $common
    }
}
Write-Host "TEST_OUTPUT_ROOT:$testOutputRoot"
```

- If `ENTRY_TYPE:NotFound` and test projects were found → use the test projects directly as entry points (run `dotnet test` on each test `.csproj`).
- If `ENTRY_TYPE:NotFound` and no test projects found → stop: `No .sln or test projects found under <path>. Provide the path to your .NET solution or project.`
- If `TEST_PROJECTS:0` and `EXISTING_COBERTURA_COUNT` > 0 (Step 2b) → continue with existing Cobertura XML analysis (no `dotnet test` run).
- If `TEST_PROJECTS:0` and `EXISTING_COBERTURA_COUNT` == 0 → stop: `No test projects found (expected projects with 'Test' or 'Spec' in the name), and no existing Cobertura XML was provided. Add a test project or provide a Cobertura file path.`

## Step 2: Create the output directory

```powershell
$coverageDir = Join-Path $testOutputRoot "TestResults" "coverage-analysis"
if (Test-Path $coverageDir) { Remove-Item $coverageDir -Recurse -Force }
New-Item -ItemType Directory -Path $coverageDir -Force | Out-Null
Write-Host "COVERAGE_DIR:$coverageDir"
```

This step only manages the `TestResults/coverage-analysis/` subdirectory (skill-owned outputs). It must never delete user-supplied Cobertura files — those live one level up at `TestResults/coverage.cobertura.xml` (or wherever the user pointed). If the user provided a path that *is* `TestResults/coverage-analysis/...`, copy the file aside before this step recreates the directory.

## Step 2b: Discover or accept existing Cobertura XML (required for the existing-data path)

If the user supplied a Cobertura XML path explicitly, use it. Otherwise probe well-known locations and any path the user mentioned:

```powershell
# 1. Honor a user-supplied path first (highest priority)
$coberturaFiles = @()
if ($userSuppliedCoberturaPath -and (Test-Path $userSuppliedCoberturaPath)) {
    $coberturaFiles = @(Get-Item $userSuppliedCoberturaPath)
}

# 2. Otherwise scan TestResults/ at the repo/test root for any *.cobertura.xml
if ($coberturaFiles.Count -eq 0) {
    $searchPaths = @(
        (Join-Path $testOutputRoot "TestResults"),
        (Join-Path $root "TestResults")
    ) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -Unique
    foreach ($sp in $searchPaths) {
        $found = @(Get-ChildItem -Path $sp -Filter "*.cobertura.xml" -Recurse -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -notmatch '[/\\]coverage-analysis[/\\]raw[/\\]' })
        if ($found.Count -gt 0) { $coberturaFiles = $found; break }
    }
}

Write-Host "EXISTING_COBERTURA_COUNT:$($coberturaFiles.Count)"
$coberturaFiles | ForEach-Object { Write-Host "EXISTING_COBERTURA:$($_.FullName)" }
```

- If `EXISTING_COBERTURA_COUNT` > 0 → **skip Phase 2 entirely** and pass these paths to the Phase 3 scripts.
- If `EXISTING_COBERTURA_COUNT` == 0 → run Phase 2 to generate fresh coverage; the file paths to feed Phase 3 will be discovered from `<COVERAGE_DIR>/raw/` after `dotnet test`.

## Step 2c: Recommend ignoring `TestResults/`

```powershell
$pattern = "**/TestResults/"
$gitRoot = (git -C $testOutputRoot rev-parse --show-toplevel 2>$null)
if ($gitRoot) { $gitRoot = [System.IO.Path]::GetFullPath($gitRoot) }
if ($gitRoot) {
    $gitignorePath = Join-Path $gitRoot ".gitignore"
    $alreadyIgnored = $false
    if (Test-Path $gitignorePath) {
        $alreadyIgnored = (Select-String -Path $gitignorePath -Pattern '^\s*(\*\*/)?TestResults/?\s*$' -Quiet)
    }
    if ($alreadyIgnored) {
        Write-Host "GITIGNORE_RECOMMENDATION:already-present"
    } else {
        Write-Host "GITIGNORE_RECOMMENDATION:$pattern"
    }
} else {
    Write-Host "GITIGNORE_RECOMMENDATION:$pattern"
}
```
