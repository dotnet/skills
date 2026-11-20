AddAnalyzer: a repo tool run by CI before the build.

  dotnet run --project tools/AddAnalyzer -- <project.csproj>

It edits <project.csproj> in place and adds
  <PackageReference Include="Microsoft.CodeAnalysis.NetAnalyzers" Version="10.0.100" PrivateAssets="all" />
when the project does not already reference it. The tool does not restore the
project passed as its argument; dotnet run implicitly restores AddAnalyzer itself.
