# Coverage Analysis — setup and discovery

Read this file only when the user did not supply usable coverage evidence and
the request requires workspace discovery or fresh collection. Do not read or run
these probes for a supplied excerpt or valid Cobertura path.

## Step 1: Locate the solution or project

Given the user's path (default: current directory), find the entry point.
If the user or the repository's documented coverage command selects a specific
`.sln`, `.slnx`, `.slnf`, or `.csproj`, pass that exact file as `$root`; do not
replace it with a discovered alternative. Directory discovery stops on multiple
solutions or, when no solution exists, multiple projects. Request an explicit
entry point rather than selecting the first.

```powershell
$root = "<user-or-repository-selected-file-or-directory>"
$requested = Get-Item -LiteralPath $root -ErrorAction Stop
$entry = $null
if (-not $requested.PSIsContainer) {
    if ($requested.Extension -notin @('.sln', '.slnx', '.slnf', '.csproj')) {
        throw "Unsupported entry point: $($requested.FullName)"
    }
    $entry = $requested
    $root = $requested.DirectoryName
} else {
    $root = $requested.FullName
    $candidates = @(Get-ChildItem -LiteralPath $root -File -Recurse -Depth 2 -ErrorAction Stop |
        Where-Object { $_.Extension -in @('.sln', '.slnx') -and $_.FullName -notmatch '[/\\](obj|bin)[/\\]' })
    if ($candidates.Count -eq 0) {
        $candidates = @(Get-ChildItem -LiteralPath $root -Filter "*.csproj" -File -Recurse -Depth 2 -ErrorAction Stop |
            Where-Object { $_.FullName -notmatch '[/\\](obj|bin)[/\\]' })
    }
    if ($candidates.Count -gt 1) {
        throw "Ambiguous entry point under $root. Specify one .sln, .slnx, .slnf, or .csproj: $($candidates.FullName -join ', ')"
    }
    if ($candidates.Count -eq 1) { $entry = $candidates[0] }
}
if ($entry) {
    $entryType = if ($entry.Extension -eq '.csproj') { 'Project' } else { 'Solution' }
    Write-Host "ENTRY_TYPE:$entryType"; Write-Host "ENTRY:$($entry.FullName)"
} else {
    Write-Host "ENTRY_TYPE:NotFound"
}

# Test projects: search the requested directory, then its containing Git root only.
$searchRoots = @($root)
$gitOutput = @(git -C $root rev-parse --show-toplevel 2>&1)
if ($LASTEXITCODE -eq 0) {
    $gitRoot = [string]$gitOutput[0]
} else {
    $gitError = $gitOutput | Out-String
    if ($gitError -notmatch 'not a git repository \(or any of the parent directories\): \.git') {
        throw "Git root lookup failed: $gitError"
    }
    $gitRoot = $null
}
if ($gitRoot) { $gitRoot = [System.IO.Path]::GetFullPath($gitRoot) }
if ($gitRoot -and $gitRoot -ne $root) {
    $sep = [System.IO.Path]::DirectorySeparatorChar
    $prefix = $gitRoot.TrimEnd($sep) + $sep
    if (-not $root.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Git root is not a containing directory: $gitRoot"
    }
    $searchRoots += $gitRoot
}

function Get-SolutionProjects($solution) {
    $text = Get-Content -LiteralPath $solution.FullName -Raw -ErrorAction Stop
    if ($solution.Extension -eq '.slnx') {
        $xml = [xml]$text
        if ($xml.DocumentElement.Name -ne 'Solution') { throw "Invalid solution: $($solution.FullName)" }
        $paths = @($xml.SelectNodes('//Project[@Path]') | ForEach-Object { $_.Path })
    } elseif ($solution.Extension -eq '.sln') {
        if ($text -notmatch 'Microsoft Visual Studio Solution File, Format Version') {
            throw "Invalid solution: $($solution.FullName)"
        }
        $paths = @([regex]::Matches($text, '(?m)^Project\("[^"]+"\)\s*=\s*"[^"]*",\s*"([^"]+)",') |
            ForEach-Object { $_.Groups[1].Value } |
            Where-Object { [System.IO.Path]::GetExtension($_) -eq '.csproj' })
    } else {
        throw "Unsupported underlying solution: $($solution.FullName)"
    }
    foreach ($path in $paths) {
        if ([System.IO.Path]::GetExtension($path) -eq '.csproj') {
            $relative = $path.Replace('\', [System.IO.Path]::DirectorySeparatorChar)
            [System.IO.Path]::GetFullPath((Join-Path $solution.DirectoryName $relative))
        }
    }
}

# Scope before reading/classifying projects: solution membership, filter membership,
# or the single project that the selected test command actually targets.
if ($entry) {
    if ($entry.Extension -eq '.csproj') {
        $projectPaths = @($entry.FullName)
    } elseif ($entry.Extension -eq '.slnf') {
        $filter = Get-Content -LiteralPath $entry.FullName -Raw -ErrorAction Stop | ConvertFrom-Json -ErrorAction Stop
        if (-not $filter.solution.path -or $null -eq $filter.solution.projects) {
            throw "Invalid solution filter: $($entry.FullName)"
        }
        $solutionPath = $filter.solution.path.Replace('\', [System.IO.Path]::DirectorySeparatorChar)
        $solution = Get-Item -LiteralPath (Join-Path $entry.DirectoryName $solutionPath) -ErrorAction Stop
        $members = @(Get-SolutionProjects $solution)
        $projectPaths = @($filter.solution.projects | ForEach-Object {
            $relative = $_.Replace('\', [System.IO.Path]::DirectorySeparatorChar)
            $project = Get-Item -LiteralPath (Join-Path $solution.DirectoryName $relative) -ErrorAction Stop
            if ($members -notcontains $project.FullName) { throw "Filtered project is not in the solution: $($project.FullName)" }
            $project.FullName
        })
    } else {
        $projectPaths = @(Get-SolutionProjects $entry)
    }
    $projects = @($projectPaths | Select-Object -Unique | ForEach-Object {
        Get-Item -LiteralPath $_ -ErrorAction Stop
    })
    $testProjects = @($projects | Where-Object {
        (Select-String -LiteralPath $_.FullName -Pattern 'Microsoft\.NET\.Test\.Sdk|xunit|nunit|MSTest\.TestAdapter|"MSTest"|MSTest\.TestFramework|TUnit' -Quiet -ErrorAction Stop) -or
        $_.Name -match '(?i)(test|spec)'
    })
} else {
    $testProjects = @()
    foreach ($sr in $searchRoots) {
        $projects = @(Get-ChildItem -LiteralPath $sr -Filter "*.csproj" -File -Recurse -Depth 5 -ErrorAction Stop |
            Where-Object { $_.FullName -notmatch '[/\\](obj|bin)[/\\]' })
        $testProjects = @($projects | Where-Object {
            Select-String -LiteralPath $_.FullName -Pattern 'Microsoft\.NET\.Test\.Sdk|xunit|nunit|MSTest\.TestAdapter|"MSTest"|MSTest\.TestFramework|TUnit' -Quiet -ErrorAction Stop
        })
        if ($testProjects.Count -eq 0) {
            $testProjects = @($projects | Where-Object { $_.Name -match '(?i)(test|spec)' })
        }
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
    $text = Get-Content -LiteralPath $_.FullName -Raw -ErrorAction Stop
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
- If `ENTRY_TYPE:NotFound` and no test projects found → stop: `No .sln, .slnx, or test projects found under <path>. Provide the path to your .NET solution, filter, or project.`
- A missing/unreadable requested path, failed lookup, or ambiguous entry point is a discovery blocker, not measured zero coverage or evidence of an empty suite. Do not continue collection after a discovery error.
- Discovery is an inventory, not permission to expand collection scope. Pass the
  exact selected entry point to `run-tests` and collect only its requested
  project graph. Do not replace a selected `.csproj` or `.slnf` with every test
  project found under the containing Git root. Apply the classic-project safety
  rules below within that selected graph.
- Selected-entry arrays contain only the projects targeted by that entry:
  solution members, filter members, or one explicit project. Transitive build
  dependencies are not additional test-run targets. For a generated or
  nonstandard entry graph that these formats cannot represent, stop and use
  the repository's evaluated target inventory rather than scanning siblings.
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
if ($userSuppliedCoberturaPath) {
    $coberturaFiles = @(Get-Item -LiteralPath $userSuppliedCoberturaPath -ErrorAction Stop)
}

# 2. Otherwise scan TestResults/ at the repo/test root for any *.cobertura.xml
if ($coberturaFiles.Count -eq 0) {
    $searchPaths = @(
        (Join-Path $testOutputRoot "TestResults"),
        (Join-Path $root "TestResults")
    ) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -Unique
    foreach ($sp in $searchPaths) {
        $found = @(Get-ChildItem -LiteralPath $sp -Filter "*.cobertura.xml" -Recurse -ErrorAction Stop |
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
