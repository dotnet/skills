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
        $Expression -match '\w+\s*(?:\+|&)\s*["'']'
    )
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

$mainForm = [IO.File]::ReadAllText("TimeTracking/TimeTracking/FrmMain.cs")
$dayTracking = [IO.File]::ReadAllText("TimeTracking/TimeTracking.Controls/DayTracking.cs")
$legacyQueries = [IO.File]::ReadAllText("TimeTracking/TimeTracking.Legacy/LegacyEntryQueries.vb")

Assert-Matches $mainForm '\bCreateSearchCommand\s*\(' `
    "The user-search command path was removed instead of repaired."
Assert-Matches $mainForm '(?is)\bSELECT\b.*?\bFROM\s+TimeEntries\b.*?@\w+' `
    "The user-search SQL no longer uses a parameter placeholder."
Assert-Matches $mainForm '(?is)\.Parameters\.(?:Add|AddWithValue|AddRange)\s*\([^;]*userSearchText' `
    "The user-controlled search text is not bound as a command parameter."

Assert-Matches $dayTracking '\bCreateEmployeeEntriesCommand\s*\(' `
    "The employee-entry command path was removed instead of repaired."
Assert-Matches $dayTracking '(?is)\bSELECT\b.*?\bFROM\s+TimeEntries\b.*?@\w+' `
    "The employee-entry SQL no longer uses a parameter placeholder."
Assert-Matches $dayTracking '(?is)\.Parameters\.(?:Add|AddWithValue|AddRange)\s*\([^;]*employeeId' `
    "The employee ID is not bound as a command parameter."

Assert-Matches $legacyQueries '(?i)\bCreateNotesCommand\s*\(' `
    "The VB.NET notes-query command path was removed instead of repaired."
Assert-Matches $legacyQueries '(?is)\bSELECT\b.*?\bFROM\s+TimeEntries\b.*?@\w+' `
    "The VB.NET notes-query SQL no longer uses a parameter placeholder."
Assert-Matches $legacyQueries '(?is)\.Parameters\.(?:Add|AddWithValue|AddRange)\s*\([^\r\n]*notesFilter' `
    "The VB.NET notes filter is not bound as a command parameter."

Write-Host "SQL command construction is parameterized across the TimeTracking solution."
