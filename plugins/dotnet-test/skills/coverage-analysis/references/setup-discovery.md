# Coverage Analysis — setup and discovery

Read this file only when the user did not supply usable coverage evidence and
the request requires workspace discovery or fresh collection. Do not read or run
these probes for a supplied excerpt or valid Cobertura path.

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

# Project-system classification controls whether the automatic dotnet/provider path is safe.
$classicTestProjects = @($testProjects | Where-Object {
    $text = Get-Content $_.FullName -Raw
    $hasSdk = $text -match '<Project[^>]+\bSdk\s*=' -or $text -match '<Sdk\b'
    $hasPackagesConfig = Test-Path (Join-Path $_.DirectoryName "packages.config")
    $hasClassicSignals = $text -match '\bToolsVersion\s*=' -or
        $text -match 'Microsoft\.(Common\.props|CSharp\.targets)' -or
        $text -match '<Compile\s+Include='
    $hasPackagesConfig -or (-not $hasSdk -and $hasClassicSignals)
})
Write-Host "CLASSIC_TEST_PROJECTS:$($classicTestProjects.Count)"
$classicTestProjects | ForEach-Object { Write-Host "CLASSIC_TEST_PROJECT:$($_.FullName)" }
$sdkTestProjects = @($testProjects | Where-Object {
    $classicTestProjects.FullName -notcontains $_.FullName
})
Write-Host "SDK_TEST_PROJECTS:$($sdkTestProjects.Count)"
$sdkTestProjects | ForEach-Object { Write-Host "SDK_TEST_PROJECT:$($_.FullName)" }

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

- If `ENTRY_TYPE:NotFound` and SDK-style test projects were found → use the test projects directly as `dotnet test` entry points.
- If `ENTRY_TYPE:NotFound` and classic test projects were found → use only the repository's documented coverage command; do not infer `dotnet test`.
- If `ENTRY_TYPE:NotFound` and no test projects found → stop: `No .sln or test projects found under <path>. Provide the path to your .NET solution or project.`
- If `TEST_PROJECTS:0` and `EXISTING_COBERTURA_COUNT` > 0 (Step 2b) → continue with existing Cobertura XML analysis (no `dotnet test` run).
- If `TEST_PROJECTS:0` and `EXISTING_COBERTURA_COUNT` == 0 → stop: `No test projects found (expected projects with 'Test' or 'Spec' in the name), and no existing Cobertura XML was provided. Add a test project or provide a Cobertura file path.`
- If `CLASSIC_TEST_PROJECTS` is nonzero and no existing Cobertura XML is found,
  search scripts/CI/docs for a repository-owned coverage command. Use it if it
  emits Cobertura.
- If classic projects are the only test projects and no repository command
  exists, stop: `Classic non-SDK or packages.config test project detected. The
  automatic SDK-style coverage-provider path would modify this project
  incorrectly. Run the repository's supported coverage workflow and provide its
  Cobertura XML.`
  This is a hard stop: do not create or run a temporary SDK project against the
  classic source, because its coverage would belong to the substitute assembly,
  not the requested test project.
- In a mixed solution, run automatic collection only for `SDK_TEST_PROJECTS`.
  Never run the solution entry point if it would include classic projects.
  Clearly label the result partial until repository-owned Cobertura data for the
  classic projects is also available.

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

- If `EXISTING_COBERTURA_COUNT` > 0 → skip fresh collection and analyze these paths.
- If `EXISTING_COBERTURA_COUNT` == 0 and all test projects are SDK-style →
  invoke `run-tests` to select the repository-compatible platform/provider
  command and collect Cobertura.
- If `EXISTING_COBERTURA_COUNT` == 0 and only classic/packages.config projects
  exist → use a repository-owned coverage command that emits Cobertura;
  otherwise stop with the message above.
- If `EXISTING_COBERTURA_COUNT` == 0 and both classic and SDK-style projects
  exist → ask `run-tests` to collect only for `SDK_TEST_PROJECTS` and mark the
  result partial until classic-project Cobertura is available.

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
