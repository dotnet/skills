param(
    [Parameter(Position = 0)]
    [string] $RequestPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

[Console]::Error.WriteLine(
    'The repository must replace verification.command with its trusted build/test hook.'
)
exit 2
