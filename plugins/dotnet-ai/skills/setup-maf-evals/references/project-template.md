# Project template (MSTest shape)

The scaffold creates `<App>.Evals.Tests` as a **MSTest** project. This
matches the
[upstream Learn doc tutorial](https://learn.microsoft.com/en-us/dotnet/ai/evaluation/evaluate-with-reporting)
and the [dotnet/ai-samples evaluation unit tests](https://github.com/dotnet/ai-samples/blob/main/src/microsoft-extensions-ai-evaluation/api/),
which is the canonical pattern for `Microsoft.Extensions.AI.Evaluation`.

A console-runner shape is available behind an explicit
`--shape console` flag for users who can't take an MSTest dependency,
but is no longer the default — every CI system understands `dotnet test`
out of the box, and Test Explorer integration is automatic.

## File tree

```
<App>.Evals.Tests/
  <App>.Evals.Tests.csproj
  dotnet-tools.json
  GlobalUsings.cs
  Reporting/
    ReportingConfig.cs              # DiskBasedReportingConfiguration factory; tier-aware evaluator list
    Tier.cs                         # EvalTier enum + EvalEnv reader
    AievalReport.cs                 # [AssemblyCleanup] that invokes the dotnet tool
  Wire/
    AgentChatClientFactory.cs       # auto-generated from IChatClient detection
    StubChatClient.cs               # used when EVAL_USE_REAL_AGENT is unset
  Telemetry/
    TelemetryTests.cs
    inputs.json
    prices.json
  Quality/
    QualityTests.cs
    rubric.md
    golden.json
  Compare/
    CompareTests.cs
    matrix.json
  Safety/                           # only if user opted in
    SafetyTests.cs
  quality.thresholds.json
.github/
  workflows/
    evals.yml                       # optional, opt-in
```

`.gitignore` additions (idempotent):

```
# setup-maf-evals
.copilot/perf-reports/evals/
<App>.Evals.Tests/_store/
```

## `.csproj` template

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsPackable>false</IsPackable>
    <RootNamespace>{{AppName}}.Evals.Tests</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <!-- MSTest -->
    <PackageReference Include="MSTest" Version="3.6.4" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />

    <!-- MEAI Evaluation: GA -->
    <PackageReference Include="Microsoft.Extensions.AI" Version="10.7.0" />
    <PackageReference Include="Microsoft.Extensions.AI.Evaluation" Version="10.7.0" />
    <PackageReference Include="Microsoft.Extensions.AI.Evaluation.Quality" Version="10.7.0" />
    <PackageReference Include="Microsoft.Extensions.AI.Evaluation.Reporting" Version="10.7.0" />

    <!-- MEAI Evaluation: preview (still useful — NLP works without an API key) -->
    <PackageReference Include="Microsoft.Extensions.AI.Evaluation.NLP" Version="10.7.0-preview.1.26309.5" />

    <!-- Safety: only added when user opts in -->
    <!-- <PackageReference Include="Microsoft.Extensions.AI.Evaluation.Safety" Version="10.7.0-preview.1.26309.5" /> -->

    <!-- Hosting (matches existing app pkgs to avoid NU1605) -->
    <PackageReference Include="Microsoft.Extensions.Hosting" Version="10.0.1" />
    <PackageReference Include="Microsoft.Extensions.Configuration.Json" Version="10.0.1" />
    <PackageReference Include="Microsoft.Extensions.Configuration.UserSecrets" Version="10.0.1" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\{{AppName}}.Agent\{{AppName}}.Agent.csproj" />
  </ItemGroup>

  <ItemGroup>
    <None Update="Telemetry\inputs.json;Telemetry\prices.json;Quality\rubric.md;Quality\golden.json;Compare\matrix.json;quality.thresholds.json" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
</Project>
```

**Why these versions:**

- `Microsoft.Extensions.AI.Evaluation.{Reporting,Quality,Console}` are GA at `10.7.0`.
- `Microsoft.Extensions.AI.Evaluation.{NLP,Safety}` are still preview at `10.7.0-preview.1.26309.5`. NLP is opt-in-on; Safety is opt-in-off.
- `Microsoft.Extensions.Hosting` and `Microsoft.Extensions.Configuration.*` must be `10.0.1` (not `10.0.0`) to satisfy the transitive constraint from `Microsoft.Agents.AI.Hosting`. Pinning `10.0.0` produces `NU1605`.

## `dotnet-tools.json`

```json
{
  "version": 1,
  "isRoot": true,
  "tools": {
    "microsoft.extensions.ai.evaluation.console": {
      "version": "10.7.0",
      "commands": ["aieval"],
      "rollForward": false
    }
  }
}
```

After scaffold: `dotnet tool restore` (the skill runs this automatically).

## `GlobalUsings.cs`

```csharp
global using Microsoft.Extensions.AI;
global using Microsoft.Extensions.AI.Evaluation;
global using Microsoft.Extensions.AI.Evaluation.Reporting;
global using Microsoft.Extensions.AI.Evaluation.Reporting.Storage;
global using Microsoft.Extensions.AI.Evaluation.NLP;
global using Microsoft.Extensions.AI.Evaluation.Quality;
global using Microsoft.Extensions.Configuration;       // AddUserSecrets in AgentChatClientFactory
global using Microsoft.Extensions.DependencyInjection; // GetRequiredService in AgentChatClientFactory
global using Microsoft.Extensions.Hosting;             // Host.CreateApplicationBuilder
global using Microsoft.VisualStudio.TestTools.UnitTesting;
```
