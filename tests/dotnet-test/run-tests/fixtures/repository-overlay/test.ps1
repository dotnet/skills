param(
    [ValidateSet("Unit")]
    [string] $Suite,
    [switch] $NoRestore
)

$arguments = @("test", "TestProject.csproj", "--filter", "TestCategory=Unit")
if ($NoRestore) {
    $arguments += "--no-restore"
}

dotnet @arguments
exit $LASTEXITCODE
