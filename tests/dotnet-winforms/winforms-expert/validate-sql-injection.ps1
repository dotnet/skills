$ErrorActionPreference = "Stop"
$interpolatedStringStartPattern = '(?:\${1,}"{3,}|(?:\$@?|@\$)")'

function Fail([string] $Message)
{
    Write-Error $Message
    exit 1
}

function Assert-Matches(
    [string] $Text,
    [string] $Pattern,
    [string] $Message)
{
    if ($Text -notmatch $Pattern)
    {
        Fail $Message
    }
}

function Assert-NotMatches(
    [string] $Text,
    [string] $Pattern,
    [string] $Message)
{
    if ($Text -match $Pattern)
    {
        Fail $Message
    }
}

function Test-DynamicSqlExpression([string] $Expression)
{
    return (
        $Expression -match $interpolatedStringStartPattern -or
        $Expression -match '(?i)\bString\.Concat\s*\(' -or
        $Expression -match '(?i)\bString\.Format\s*\(' -or
        $Expression -match '["''][^"'']*["'']\s*(?:\+|&)\s*\w+' -or
        $Expression -match '(?<!@)\b\w+\s*(?:\+|&)\s*["'']' -or
        $Expression -match '(?i)(?<!@)\b[A-Za-z_]\w*\s*(?:\+|&)\s*(?!@)[A-Za-z_]\w*\b'
    )
}

function Get-QueryInfo([string] $Source, [string] $Path)
{
    $directAssignment = [regex]::Match(
        $Source,
        '(?is)\b(?<command>\w+)\.CommandText\s*=\s*"{1,3}(?<sql>[^"]*\bSELECT\b[^"]*)"{1,3}'
    )
    if ($directAssignment.Success)
    {
        return [pscustomobject]@{
            Command = $directAssignment.Groups["command"].Value
            Sql = $directAssignment.Groups["sql"].Value
        }
    }

    $directConstructor = [regex]::Match(
        $Source,
        '(?is)\b(?:var|SqlCommand|Dim)\s+(?<command>\w+)[^=\r\n]*=\s*(?:new|New)\s+SqlCommand\s*\(\s*"{1,3}(?<sql>[^"]*\bSELECT\b[^"]*)"{1,3}'
    )
    if ($directConstructor.Success)
    {
        return [pscustomobject]@{
            Command = $directConstructor.Groups["command"].Value
            Sql = $directConstructor.Groups["sql"].Value
        }
    }

    $sqlVariable = [regex]::Match(
        $Source,
        '(?is)\b(?:var|string|String|Dim)\s+(?<variable>\w+)[^=\r\n]*=\s*"{1,3}(?<sql>[^"]*\bSELECT\b[^"]*)"{1,3}'
    )
    if ($sqlVariable.Success)
    {
        $escapedVariable = [regex]::Escape($sqlVariable.Groups["variable"].Value)
        $commandAssignment = [regex]::Match(
            $Source,
            "(?is)\b(?<command>\w+)\.CommandText\s*=\s*$escapedVariable\b"
        )
        if (-not $commandAssignment.Success)
        {
            $commandAssignment = [regex]::Match(
                $Source,
                "(?is)\b(?:var|SqlCommand|Dim)\s+(?<command>\w+)[^=\r\n]*=\s*(?:new|New)\s+SqlCommand\s*\(\s*$escapedVariable\b"
            )
        }

        if ($commandAssignment.Success)
        {
            return [pscustomobject]@{
                Command = $commandAssignment.Groups["command"].Value
                Sql = $sqlVariable.Groups["sql"].Value
            }
        }
    }

    Fail "No parameterizable SELECT command was found in $Path."
}

function Get-LocalExpression(
    [string] $Source,
    [string] $Name,
    [bool] $IsVisualBasic)
{
    $escapedName = [regex]::Escape($Name)
    $pattern = if ($IsVisualBasic)
    {
        "(?im)\bDim\s+$escapedName(?:\s+As\s+\w+)?\s*=\s*(?<expression>[^\r\n]+)"
    }
    else
    {
        "(?is)\b(?:var|string|String)\s+$escapedName\s*=\s*(?<expression>.*?);"
    }

    $match = [regex]::Match($Source, $pattern)
    if ($match.Success)
    {
        return $match.Groups["expression"].Value
    }

    return $null
}

function Test-ExpressionUsesInput(
    [string] $Source,
    [string] $Expression,
    [string] $InputName,
    [bool] $RequireContains,
    [bool] $SqlAddsWildcards,
    [bool] $IsVisualBasic,
    [System.Collections.Generic.HashSet[string]] $Visited)
{
    $escapedInput = [regex]::Escape($InputName)
    $hasTwoWildcards = [regex]::Matches($Expression, '%').Count -ge 2
    if ($Expression -match "(?i)\b$escapedInput\b")
    {
        return (
            -not $RequireContains -or
            $SqlAddsWildcards -or
            $hasTwoWildcards
        )
    }

    $identifiers = [regex]::Matches($Expression, '\b[A-Za-z_]\w*\b') |
        ForEach-Object { $_.Value } |
        Select-Object -Unique

    foreach ($identifier in $identifiers)
    {
        if (-not $Visited.Add($identifier))
        {
            continue
        }

        $localExpression = Get-LocalExpression $Source $identifier $IsVisualBasic
        if ($null -eq $localExpression)
        {
            continue
        }

        if (Test-ExpressionUsesInput `
            $Source `
            $localExpression `
            $InputName `
            $RequireContains `
            ($SqlAddsWildcards -or $hasTwoWildcards) `
            $IsVisualBasic `
            $Visited)
        {
            return $true
        }
    }

    return $false
}

function Get-ParameterValueExpressions(
    [string] $Source,
    [string] $CommandName,
    [string] $Placeholder,
    [bool] $IsVisualBasic)
{
    $escapedCommand = [regex]::Escape($CommandName)
    $parameterName = $Placeholder.TrimStart('@')
    $quotedName = '["'']@?' + [regex]::Escape($parameterName) + '["'']'
    $expressions = [System.Collections.Generic.List[string]]::new()

    $patterns = if ($IsVisualBasic)
    {
        @(
            "(?im)\b$escapedCommand\.Parameters\.AddWithValue\s*\(\s*$quotedName\s*,\s*(?<value>.+)\)\s*$",
            "(?im)\b$escapedCommand\.Parameters\.Add\s*\(\s*$quotedName\s*,.+\)\s*\.Value\s*=\s*(?<value>[^\r\n]+)"
        )
    }
    else
    {
        @(
            "(?is)\b$escapedCommand\.Parameters\.AddWithValue\s*\(\s*$quotedName\s*,\s*(?<value>.*?)\)\s*;",
            "(?is)\b$escapedCommand\.Parameters\.Add\s*\(\s*$quotedName\s*,.*?\)\s*\.Value\s*=\s*(?<value>.*?);"
        )
    }

    foreach ($pattern in $patterns)
    {
        foreach ($match in [regex]::Matches($Source, $pattern))
        {
            $expressions.Add($match.Groups["value"].Value)
        }
    }

    $addVariablePattern = if ($IsVisualBasic)
    {
        "(?im)\bDim\s+(?<variable>\w+)(?:\s+As\s+SqlParameter)?\s*=\s*$escapedCommand\.Parameters\.Add\s*\(\s*$quotedName(?:\s*,.+)?\)\s*$"
    }
    else
    {
        "(?is)\b(?:var|SqlParameter)\s+(?<variable>\w+)\s*=\s*$escapedCommand\.Parameters\.Add\s*\(\s*$quotedName(?:\s*,.*?)?\)\s*;"
    }
    foreach ($match in [regex]::Matches($Source, $addVariablePattern))
    {
        $variable = [regex]::Escape($match.Groups["variable"].Value)
        $valuePattern = if ($IsVisualBasic)
        {
            "(?im)\b$variable\.Value\s*=\s*(?<value>[^\r\n]+)"
        }
        else
        {
            "(?is)\b$variable\.Value\s*=\s*(?<value>.*?);"
        }
        foreach ($valueMatch in [regex]::Matches($Source, $valuePattern))
        {
            $expressions.Add($valueMatch.Groups["value"].Value)
        }
    }

    $newVariablePattern = if ($IsVisualBasic)
    {
        "(?im)\bDim\s+(?<variable>\w+)(?:\s+As\s+SqlParameter)?\s*=\s*New\s+SqlParameter\s*\(\s*$quotedName(?:\s*,.+)?\)(?:\s+With\s*\{(?<initializer>[^\r\n]+)\})?"
    }
    else
    {
        "(?is)\b(?:var|SqlParameter)\s+(?<variable>\w+)\s*=\s*new\s+SqlParameter\s*\(\s*$quotedName(?:\s*,.*?)?\)(?:\s*\{(?<initializer>.*?)\})?\s*;"
    }
    foreach ($match in [regex]::Matches($Source, $newVariablePattern))
    {
        $variableName = $match.Groups["variable"].Value
        $escapedVariable = [regex]::Escape($variableName)
        $isAdded =
            $Source -match "(?is)\b$escapedCommand\.Parameters\.Add\s*\(\s*$escapedVariable\s*\)" -or
            $Source -match "(?is)\b$escapedCommand\.Parameters\.AddRange\s*\([^;]*\b$escapedVariable\b"
        if (-not $isAdded)
        {
            continue
        }

        $initializer = $match.Groups["initializer"].Value
        $initializerValue = [regex]::Match(
            $initializer,
            '(?is)\.?Value\s*=\s*(?<value>[^,}]+)'
        )
        if ($initializerValue.Success)
        {
            $expressions.Add($initializerValue.Groups["value"].Value)
        }

        $valuePattern = if ($IsVisualBasic)
        {
            "(?im)\b$escapedVariable\.Value\s*=\s*(?<value>[^\r\n]+)"
        }
        else
        {
            "(?is)\b$escapedVariable\.Value\s*=\s*(?<value>.*?);"
        }
        foreach ($valueMatch in [regex]::Matches($Source, $valuePattern))
        {
            $expressions.Add($valueMatch.Groups["value"].Value)
        }
    }

    $directNewPattern = if ($IsVisualBasic)
    {
        "(?im)\b$escapedCommand\.Parameters\.Add\s*\(\s*New\s+SqlParameter\s*\(\s*$quotedName\s*,\s*(?<value>.+)\)\s*\)\s*$"
    }
    else
    {
        "(?is)\b$escapedCommand\.Parameters\.Add\s*\(\s*new\s+SqlParameter\s*\(\s*$quotedName\s*,\s*(?<value>.*?)\)\s*\)\s*;"
    }
    foreach ($match in [regex]::Matches($Source, $directNewPattern))
    {
        $expressions.Add($match.Groups["value"].Value)
    }

    return $expressions.ToArray()
}

function Assert-QueryParameterBinding(
    [string] $Source,
    [string] $Path,
    [string] $MethodName,
    [string] $InputName,
    [bool] $RequireContains,
    [bool] $IsVisualBasic)
{
    Assert-Matches $Source "(?i)\b$([regex]::Escape($MethodName))\s*\(" `
        "$MethodName was removed instead of repaired."

    $query = Get-QueryInfo $Source $Path
    $sql = $query.Sql
    $placeholders = @(
        [regex]::Matches($sql, '@[A-Za-z_]\w*') |
            ForEach-Object { $_.Value } |
            Select-Object -Unique
    )
    if ($placeholders.Count -eq 0)
    {
        Fail "The query in $Path no longer uses a parameter placeholder."
    }

    $inputIsBound = $false
    foreach ($placeholder in $placeholders)
    {
        $valueExpressions = @(
            Get-ParameterValueExpressions `
                $Source `
                $query.Command `
                $placeholder `
                $IsVisualBasic
        )
        if ($valueExpressions.Count -eq 0)
        {
            Fail "SQL placeholder '$placeholder' in $Path has no matching command parameter."
        }

        $sqlAddsWildcards =
            $RequireContains -and
            [regex]::Matches($sql, '%').Count -ge 2 -and
            $sql -match "(?i)$([regex]::Escape($placeholder))"

        foreach ($valueExpression in $valueExpressions)
        {
            $visited = [System.Collections.Generic.HashSet[string]]::new(
                [System.StringComparer]::OrdinalIgnoreCase
            )
            if (Test-ExpressionUsesInput `
                $Source `
                $valueExpression `
                $InputName `
                $RequireContains `
                $sqlAddsWildcards `
                $IsVisualBasic `
                $visited)
            {
                $inputIsBound = $true
                break
            }
        }
    }

    if (-not $inputIsBound)
    {
        $behavior = if ($RequireContains)
        {
            " with '%' wildcards preserving the original contains search"
        }
        else
        {
            ""
        }
        Fail "The input '$InputName' is not assigned to a matching SQL parameter$behavior in $Path."
    }
}

$sourceFiles = Get-ChildItem -Path . -Recurse -File |
    Where-Object {
        $_.Extension -in @(".cs", ".vb") -and
        $_.FullName -notmatch '[\\/](?:bin|obj)[\\/]'
    }

if ($sourceFiles.Count -eq 0)
{
    Fail "No C# or Visual Basic source files were found."
}

$allSource = ($sourceFiles | ForEach-Object {
    [IO.File]::ReadAllText($_.FullName)
}) -join "`n"

Assert-NotMatches $allSource "(?is)\b(?:var|String|string|Dim)\s+\w*(?:sql|query|commandText)\w*\s*=\s*$interpolatedStringStartPattern" `
    "Interpolated values are still used to construct SQL text."
Assert-NotMatches $allSource '(?is)\b(?:var|String|string|Dim)\s+\w*(?:sql|query|commandText)\w*\s*=.*?["''][^;]*["'']\s*(?:\+|&)\s*\w+' `
    "Concatenated values are still used to construct SQL text."
Assert-NotMatches $allSource '(?is)\bString\.Format\s*\(\s*["''][^"'']*(?:SELECT|INSERT|UPDATE|DELETE)' `
    "String.Format is still used to construct SQL text."
Assert-NotMatches $allSource "(?is)new\s+(?:SqlCommand|OleDbCommand|SqlDataAdapter|OleDbDataAdapter)\s*\(\s*$interpolatedStringStartPattern" `
    "An interpolated SQL command remains."
Assert-NotMatches $allSource '(?is)new\s+(?:SqlCommand|OleDbCommand|SqlDataAdapter|OleDbDataAdapter)\s*\([^;]*["'']\s*(?:\+|&)\s*\w+' `
    "A concatenated SQL command remains."
Assert-NotMatches $allSource '(?is)\bnew\s+(?:SqlCommand|OleDbCommand|SqlDataAdapter|OleDbDataAdapter)\s*\(\s*(?:System\.)?String\.(?:Format|Concat)\s*\(' `
    "String.Format or String.Concat is still used to construct a SQL command."

foreach ($sourceFile in $sourceFiles)
{
    $source = [IO.File]::ReadAllText($sourceFile.FullName)
    $commandTextAssignments = if ($sourceFile.Extension -eq ".cs")
    {
        [regex]::Matches(
            $source,
            '(?is)\.(?:CommandText|SelectCommand)\s*=\s*(?<expression>.*?);'
        )
    }
    else
    {
        [regex]::Matches(
            $source,
            '(?im)\.(?:CommandText|SelectCommand)\s*=\s*(?<expression>[^\r\n]+)'
        )
    }

    foreach ($assignment in $commandTextAssignments)
    {
        if (Test-DynamicSqlExpression $assignment.Groups["expression"].Value)
        {
            Fail "Dynamic SQL is assigned directly to CommandText or SelectCommand in $($sourceFile.FullName)."
        }
    }

    $assignments = if ($sourceFile.Extension -eq ".cs")
    {
        [regex]::Matches(
            $source,
            '(?is)\b(?:var|string|String)\s+(?<name>\w+)\s*=\s*(?<expression>.*?);'
        )
    }
    else
    {
        [regex]::Matches(
            $source,
            '(?im)\bDim\s+(?<name>\w+)(?:\s+As\s+String)?\s*=\s*(?<expression>[^\r\n]+)'
        )
    }

    foreach ($assignment in $assignments)
    {
        $name = $assignment.Groups["name"].Value
        $expression = $assignment.Groups["expression"].Value

        if (-not (Test-DynamicSqlExpression $expression))
        {
            continue
        }

        $escapedName = [regex]::Escape($name)
        $usedAsCommandText =
            $source -match "(?is)new\s+(?:SqlCommand|OleDbCommand|SqlDataAdapter|OleDbDataAdapter)\s*\(\s*$escapedName\b" -or
            $source -match "(?is)\.(?:CommandText|SelectCommand)\s*=\s*$escapedName\b"

        if ($usedAsCommandText)
        {
            Fail "Dynamic SQL variable '$name' remains in $($sourceFile.FullName)."
        }
    }
}

$mainForm = [IO.File]::ReadAllText("TimeTracking/FrmMain.cs")
$dayTracking = [IO.File]::ReadAllText("TimeTracking.Controls/DayTracking.cs")
$legacyQueries = [IO.File]::ReadAllText("TimeTracking.Legacy/LegacyEntryQueries.vb")

Assert-QueryParameterBinding `
    $mainForm `
    "TimeTracking/FrmMain.cs" `
    "CreateSearchCommand" `
    "userSearchText" `
    $true `
    $false
Assert-QueryParameterBinding `
    $dayTracking `
    "TimeTracking.Controls/DayTracking.cs" `
    "CreateEmployeeEntriesCommand" `
    "employeeId" `
    $false `
    $false
Assert-QueryParameterBinding `
    $legacyQueries `
    "TimeTracking.Legacy/LegacyEntryQueries.vb" `
    "CreateNotesCommand" `
    "notesFilter" `
    $false `
    $true

Write-Host "SQL command construction is parameterized across the TimeTracking solution."
