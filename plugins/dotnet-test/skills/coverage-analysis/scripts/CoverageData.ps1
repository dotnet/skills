# Shared, report-identity-scoped Cobertura normalization. This does not attest
# that a report was collected from the current checkout.

function New-CoverageMap {
    return [System.Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
}

function Add-CoverageDiagnostic($Diagnostics, [string]$Code, [string]$Message, [string]$Path) {
    $Diagnostics.Add([pscustomobject]@{ code = $Code; severity = 'warning'; message = $Message; path = $Path })
}

function Read-CoverageInteger([string]$Text, [string]$Name, [long]$Minimum = 0) {
    $value = 0L
    if (-not [long]::TryParse($Text, [Globalization.NumberStyles]::Integer,
            [Globalization.CultureInfo]::InvariantCulture, [ref]$value) -or $value -lt $Minimum) {
        throw [FormatException]::new("Invalid $Name value '$Text'.")
    }
    return $value
}

function Add-CoverageLine($Map, $Node, [string]$Key, [string]$ReportId) {
    $number = Read-CoverageInteger $Node.GetAttribute('number') 'line number' 1
    $hits = Read-CoverageInteger $Node.GetAttribute('hits') 'line hits'
    $lineKey = "$Key`n$number"
    if (-not $Map.ContainsKey($lineKey)) {
        $Map[$lineKey] = @{
            Covered = $false
            NonBranch = $false
            ConflictingBranches = $false
            Branches = [System.Collections.Generic.List[object]]::new()
            Observations = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        }
    }
    $entry = $Map[$lineKey]
    $entry.Covered = $entry.Covered -or $hits -gt 0
    if (-not $entry.Observations.Add($ReportId)) { return }
    $branch = $Node.GetAttribute('branch')
    if ($branch -and $branch -notin @('true', 'false')) {
        throw [FormatException]::new("Invalid branch flag '$branch'.")
    }
    if ($branch -ne 'true') {
        $entry.NonBranch = $true
        if ($entry.Branches.Count) { $entry.ConflictingBranches = $true }
        return
    }
    if ($entry.NonBranch) { $entry.ConflictingBranches = $true }
    $raw = $Node.GetAttribute('condition-coverage')
    if ($raw -notmatch '^\s*[\d.]+%\s*\((\d+)/(\d+)\)\s*$') {
        $entry.Branches.Add(@{ Total = $null; Covered = $null; Conditions = $null })
        return
    }
    $covered = Read-CoverageInteger $Matches[1] 'covered branches'
    $total = Read-CoverageInteger $Matches[2] 'total branches' 1
    if ($covered -gt $total) { throw [FormatException]::new('Covered branches exceed total branches.') }

    # Cobertura jump conditions describe two outcomes, not which outcome a
    # 50% observation took. Keep that ambiguity even when condition ids match.
    $conditions = New-CoverageMap
    $nodes = @($Node.SelectNodes('conditions/condition'))
    $conditionSum = 0
    $usable = $nodes.Count -gt 0 -and $total -eq 2 * $nodes.Count
    foreach ($condition in $nodes) {
        $id = $condition.GetAttribute('number')
        $coverage = $condition.GetAttribute('coverage')
        if ($condition.GetAttribute('type') -ne 'jump' -or -not $id -or
            $conditions.ContainsKey($id) -or $coverage -notmatch '^(0|50|100)(?:\.0+)?%$') {
            $usable = $false
            continue
        }
        $count = [int]$Matches[1] / 50
        $conditions[$id] = $count
        $conditionSum += $count
    }
    if (-not $usable -or $conditionSum -ne $covered) { $conditions = $null }
    $entry.Branches.Add(@{ Total = $total; Covered = $covered; Conditions = $conditions })
}

function Get-CoverageMeasure($Lines) {
    $coveredLines = @($Lines.Values | Where-Object Covered).Count
    $linePercent = if ($Lines.Count) { [math]::Round(100.0 * $coveredLines / $Lines.Count, 1) } else { $null }
    $lower = 0L
    $upper = 0L
    $total = 0L
    $unknown = $Lines.Count -eq 0
    foreach ($line in $Lines.Values) {
        $observations = @($line.Branches)
        if (-not $observations.Count) { continue }
        if ($line.ConflictingBranches -or @($observations | Where-Object { $null -eq $_.Total }).Count) {
            $unknown = $true
            continue
        }
        $totals = @($observations | ForEach-Object { $_.Total } | Sort-Object -Unique)
        if ($totals.Count -ne 1 -or $null -eq $totals[0]) { $unknown = $true; continue }
        $capacity = $totals[0]
        $total += $capacity
        $useConditions = $true
        $conditionIds = $null
        foreach ($observation in $observations) {
            if ($null -eq $observation.Conditions) { $useConditions = $false; break }
            $ids = @($observation.Conditions.Keys | Sort-Object -CaseSensitive)
            $identity = ConvertTo-Json -InputObject $ids -Compress
            if ($null -ne $conditionIds -and $conditionIds -cne $identity) { $useConditions = $false; break }
            $conditionIds = $identity
        }
        if ($useConditions) {
            foreach ($id in $observations[0].Conditions.Keys) {
                $counts = @($observations | ForEach-Object { $_.Conditions[$id] })
                $lower += ($counts | Measure-Object -Maximum).Maximum
                $upper += [math]::Min(2, ($counts | Measure-Object -Sum).Sum)
            }
        } else {
            $counts = @($observations | ForEach-Object { $_.Covered })
            $lower += ($counts | Measure-Object -Maximum).Maximum
            $upper += [math]::Min($capacity, ($counts | Measure-Object -Sum).Sum)
        }
    }
    $branchStatus = if ($unknown) { 'unknown' } elseif (-not $total) { 'not-applicable' } elseif ($lower -eq $upper) { 'exact' } else { 'bounded' }
    return [pscustomobject]@{
        TotalLines = $Lines.Count
        CoveredLines = $coveredLines
        LineCoverage = $linePercent
        LineCoverageStatus = $(if ($Lines.Count) { 'exact' } else { 'unknown' })
        TotalBranches = $(if ($unknown) { $null } else { $total })
        CoveredBranches = $(if ($branchStatus -in @('exact', 'not-applicable')) { [long]$lower } else { $null })
        BranchCoverage = $(if ($branchStatus -eq 'exact') { [math]::Round(100.0 * $lower / $total, 1) } else { $null })
        BranchCoverageStatus = $branchStatus
        CoveredBranchesLower = $(if (-not $unknown) { [long]$lower } else { $null })
        CoveredBranchesUpper = $(if (-not $unknown) { [long]$upper } else { $null })
        BranchCoverageLower = $(if (-not $unknown -and $total) { [math]::Round(100.0 * $lower / $total, 1) } else { $null })
        BranchCoverageUpper = $(if (-not $unknown -and $total) { [math]::Round(100.0 * $upper / $total, 1) } else { $null })
    }
}

function Get-CoverageData([string[]]$Paths) {
    if (-not $Paths.Count) { throw [ArgumentException]::new('At least one Cobertura report is required.') }
    $methods = New-CoverageMap
    $classLines = New-CoverageMap
    $methodLines = New-CoverageMap
    $inputs = [System.Collections.Generic.List[object]]::new()
    $reports = [System.Collections.Generic.List[object]]::new()
    $diagnostics = [System.Collections.Generic.List[object]]::new()
    $seen = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $allClassesHaveLines = $true
    foreach ($path in ($Paths | Sort-Object -Unique -CaseSensitive)) {
        $resolved = (Get-Item -LiteralPath $path -ErrorAction Stop).FullName
        $bytes = [IO.File]::ReadAllBytes($resolved)
        $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()
        $inputs.Add([pscustomobject]@{ path = $resolved; sha256 = $hash })
        if (-not $seen.Add($hash)) { continue }
        $xml = [xml]::new()
        $xml.XmlResolver = $null
        $stream = [IO.MemoryStream]::new($bytes, $false)
        try { $xml.Load($stream) } finally { $stream.Dispose() }
        if ($xml.DocumentElement.Name -ne 'coverage') { throw [FormatException]::new("Not a Cobertura report: $path") }
        $coverageRoot = $xml.DocumentElement
        $report = @{ line = $null; branch = $null }
        foreach ($metric in @('line', 'branch')) {
            $prefix = if ($metric -eq 'line') { 'lines' } else { 'branches' }
            $valid = $coverageRoot.GetAttribute("$prefix-valid")
            $covered = $coverageRoot.GetAttribute("$prefix-covered")
            if ($valid -or $covered) {
                $denominator = Read-CoverageInteger $valid "$prefix-valid"
                $numerator = Read-CoverageInteger $covered "$prefix-covered"
                if ($numerator -gt $denominator) { throw [FormatException]::new("$prefix-covered exceeds $prefix-valid.") }
                $report[$metric] = @{ Total = $denominator; Covered = $numerator }
            }
        }
        $reports.Add($report)
        $reportMethods = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach ($class in $xml.SelectNodes('/coverage/packages/package/classes/class')) {
            $filename = $class.GetAttribute('filename').Replace('\', '/')
            $className = $class.GetAttribute('name')
            $module = $class.ParentNode.ParentNode.GetAttribute('name')
            if (-not $filename -or -not $className) { throw [FormatException]::new('Class name and filename are required.') }
            $sourceKey = ConvertTo-Json -InputObject @($module, $filename) -Compress
            $classNodes = @($class.SelectNodes('lines/line'))
            if (-not $classNodes.Count) { $allClassesHaveLines = $false }
            foreach ($line in $classNodes) { Add-CoverageLine $classLines $line $sourceKey $hash }
            foreach ($method in $class.SelectNodes('methods/method')) {
                $name = $method.GetAttribute('name')
                if (-not $name) { throw [FormatException]::new('Method name is required.') }
                $signature = $method.GetAttribute('signature')
                $key = ConvertTo-Json -InputObject @($module, $filename, $className, $name, $signature) -Compress
                if (-not $reportMethods.Add($key)) {
                    throw [FormatException]::new("Duplicate method identity for $className.$name; signatures or source mapping are ambiguous.")
                }
                if (-not $methods.ContainsKey($key)) {
                    $methods[$key] = @{
                        Module = $module; Class = $className; Method = $name; Signature = $signature; File = $filename
                        Lines = (New-CoverageMap)
                        Complexities = [System.Collections.Generic.HashSet[long]]::new()
                    }
                }
                $entry = $methods[$key]
                $complexity = $method.GetAttribute('complexity')
                if ($complexity) { [void]$entry.Complexities.Add((Read-CoverageInteger $complexity 'complexity' 1)) }
                foreach ($line in $method.SelectNodes('lines/line')) {
                    Add-CoverageLine $entry.Lines $line '' $hash
                    Add-CoverageLine $methodLines $line $sourceKey $hash
                }
            }
        }
    }
    $result = [System.Collections.Generic.List[object]]::new()
    foreach ($entry in $methods.Values) {
        $measure = Get-CoverageMeasure $entry.Lines
        $complexity = if ($entry.Complexities.Count -eq 1) { @($entry.Complexities)[0] } else { $null }
        $item = [ordered]@{
            Class = $entry.Class; Method = $entry.Method; Signature = $entry.Signature; File = $entry.File
            Module = $entry.Module; Complexity = $complexity
            ComplexityStatus = $(if ($null -ne $complexity) { 'exact' } else { 'unknown' })
        }
        foreach ($property in $measure.PSObject.Properties) { $item[$property.Name] = $property.Value }
        $item.UncoveredLines = $measure.TotalLines - $measure.CoveredLines
        if ($null -eq $complexity) {
            Add-CoverageDiagnostic $diagnostics 'COMPLEXITY_UNKNOWN' "Missing or conflicting complexity for $($entry.Class).$($entry.Method)." $entry.File
        }
        if ($measure.LineCoverageStatus -eq 'unknown') {
            Add-CoverageDiagnostic $diagnostics 'METHOD_LINES_UNKNOWN' "No executable-line evidence for $($entry.Class).$($entry.Method)." $entry.File
        }
        if ($measure.BranchCoverageStatus -in @('unknown', 'bounded')) {
            Add-CoverageDiagnostic $diagnostics 'BRANCH_UNION_UNCERTAIN' "Branch outcomes cannot be uniquely merged for $($entry.Class).$($entry.Method)." $entry.File
        }
        $result.Add([pscustomobject]$item)
    }
    $aggregate = Get-CoverageMeasure $classLines
    $aggregateScope = 'class-lines'
    if (-not $allClassesHaveLines -or -not $classLines.Count) {
        $aggregateScope = if ($reports.Count -eq 1) { 'report' } else { 'method-lines-only' }
        $aggregate = [pscustomobject]@{ LineCoverage = $null; LineCoverageStatus = 'unknown'; BranchCoverage = $null; BranchCoverageStatus = 'unknown' }
        if ($reports.Count -eq 1) {
            foreach ($metric in @('line', 'branch')) {
                $counter = $reports[0][$metric]
                $name = if ($metric -eq 'line') { 'LineCoverage' } else { 'BranchCoverage' }
                if ($null -ne $counter) {
                    $aggregate.$name = if ($counter.Total) { [math]::Round(100.0 * $counter.Covered / $counter.Total, 1) } else { $null }
                    $aggregate."${name}Status" = if ($counter.Total) { 'exact' } else { 'not-applicable' }
                }
            }
        }
    }
    if (-not $result.Count) {
        Add-CoverageDiagnostic $diagnostics 'METHODS_UNAVAILABLE' 'The report has no method-level coverage evidence.' ''
    }
    return [pscustomobject]@{
        Methods = @($result | Sort-Object -CaseSensitive Module, File, Class, Method, Signature)
        Inputs = @($inputs)
        Diagnostics = $diagnostics
        Summary = [ordered]@{
            overall_line_coverage = $aggregate.LineCoverage
            overall_line_coverage_status = $aggregate.LineCoverageStatus
            overall_branch_coverage = $aggregate.BranchCoverage
            overall_branch_coverage_status = $aggregate.BranchCoverageStatus
            coverage_scope = $aggregateScope
            method_line_coverage = (Get-CoverageMeasure $methodLines).LineCoverage
            total_methods = $result.Count
        }
    }
}

function Write-CoverageJson([string]$Tool, [string]$Status, $Scope, $Inputs, $Diagnostics, $Data) {
    [ordered]@{
        schema_version = '1'; tool = $Tool; tool_version = '1.0'; status = $Status
        scope = $Scope; inputs = @($Inputs); diagnostics = @($Diagnostics); data = $Data
    } | ConvertTo-Json -Depth 30
}

function Write-CoverageDiagnostics($Diagnostics) {
    foreach ($diagnostic in $Diagnostics) {
        [Console]::Error.WriteLine("$($diagnostic.code): $($diagnostic.message)")
    }
}
