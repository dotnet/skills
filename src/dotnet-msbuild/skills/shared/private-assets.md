# PrivateAssets for Analyzers and Build Tools

Analyzer and build-tool packages should always use `PrivateAssets="all"` to prevent them from flowing as transitive dependencies to consumers of your library.

```xml
<!-- BAD: Flows to consumers -->
<PackageReference Include="StyleCop.Analyzers" Version="1.2.0-beta.556" />
<PackageReference Include="Microsoft.SourceLink.GitHub" Version="8.0.0" />
<PackageReference Include="MinVer" Version="5.0.0" />

<!-- GOOD: Stays private -->
<PackageReference Include="StyleCop.Analyzers" Version="1.2.0-beta.556" PrivateAssets="all" />
<PackageReference Include="Microsoft.SourceLink.GitHub" Version="8.0.0" PrivateAssets="all" />
<PackageReference Include="MinVer" Version="5.0.0" PrivateAssets="all" />
```

**Packages that almost always need `PrivateAssets="all"`:**
- Roslyn analyzers (`*.Analyzers`, `*.CodeFixes`)
- Source generators
- SourceLink packages (`Microsoft.SourceLink.*`)
- Versioning tools (`MinVer`, `Nerdbank.GitVersioning`)
- Build-only tools (`Microsoft.DotNet.ApiCompat`, etc.)
