param(
    [ValidateSet("Unit")]
    [string] $Suite = "Unit",
    [switch] $NoRestore
)

$arguments = @("test", "TestProject.csproj", "--filter", "TestCategory=$Suite")
if ($NoRestore) {
    $arguments += "--no-restore"
}

dotnet @arguments
exit $LASTEXITCODE
