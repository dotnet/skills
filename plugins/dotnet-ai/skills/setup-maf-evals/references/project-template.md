# Project template — `<App>.Evals`

The exact files written when scaffolding the eval harness.

## `<App>.Evals.csproj`

Use the actual current package versions from NuGet at scaffold time
(query `nuget.org` or `dotnet package search`). The versions below
reflect the latest stable family at the time of writing
(`Microsoft.Extensions.AI.Evaluation` 10.x is GA on nuget.org); query
`dotnet package search "Microsoft.Extensions.AI.Evaluation"` and bump
to the latest stable when you scaffold.

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <RootNamespace>{{ AppName }}.Evals</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.AI" Version="10.*" />
    <PackageReference Include="Microsoft.Extensions.AI.Evaluation" Version="10.*" />
    <PackageReference Include="Microsoft.Extensions.AI.Evaluation.Quality" Version="10.*" />
    <PackageReference Include="Microsoft.Extensions.AI.Evaluation.Reporting" Version="10.*" />
    <PackageReference Include="Microsoft.Extensions.Configuration.Json" Version="10.*" />
    <PackageReference Include="Microsoft.Extensions.Hosting" Version="10.*" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\{{ AgentProject }}\{{ AgentProject }}.csproj" />
  </ItemGroup>

  <ItemGroup>
    <None Update="Telemetry/inputs.json" CopyToOutputDirectory="PreserveNewest" />
    <None Update="Telemetry/prices.json" CopyToOutputDirectory="PreserveNewest" />
    <None Update="Quality/rubric.md" CopyToOutputDirectory="PreserveNewest" />
    <None Update="Quality/golden.json" CopyToOutputDirectory="PreserveNewest" />
    <None Update="Compare/matrix.json" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>

</Project>
```

If the consumer repo uses Central Package Management
(`Directory.Packages.props` with `ManagePackageVersionsCentrally=true`),
omit the `Version=` attributes and add matching `<PackageVersion>`
entries to the central props file instead.

The `ProjectReference` line points to the agent service the evals will
exercise (typically the agent service, not the AppHost). If the repo
has multiple agent service projects, generate one `ProjectReference`
per project and let the runner classes select which agent to invoke.

## `Abstractions.cs` (generated alongside Program.cs)

```csharp
public interface IEvalRunner
{
    Task<EvalReport> RunAsync(CancellationToken ct = default);
}

public sealed record EvalReport(
    bool Success,
    string OneLineSummary,
    string ReportDirectory);
```

The three runner classes (`TelemetryEvalRunner`, `QualityEvalRunner`,
`CompareEvalRunner`) each implement `IEvalRunner` and write their
report files under `ReportDirectory`. See `telemetry-capture.md`,
`quality-modes.md`, and `compare-mode.md` for each runner's body.

## `Program.cs`

```csharp
var mode = args.FirstOrDefault() ?? "telemetry";
IEvalRunner runner = mode switch
{
    "telemetry" => new TelemetryEvalRunner(),
    "quality"   => new QualityEvalRunner(),
    "compare"   => new CompareEvalRunner(),
    _           => throw new ArgumentException($"Unknown mode: {mode}")
};

var report = await runner.RunAsync();
Console.WriteLine(report.OneLineSummary);
return report.Success ? 0 : 1;
```

## `Telemetry/inputs.json` (starter)

```json
[
  { "id": "smoke-1", "user_message": "Say hi in five words." },
  { "id": "smoke-2", "user_message": "Plan a one-day trip to Seattle." },
  { "id": "smoke-3", "user_message": "Summarize the previous answer in 20 words." }
]
```

## `Telemetry/prices.json` (starter — user edits)

```json
{
  "gpt-4o-mini":  { "input_per_1k": 0.00015, "output_per_1k": 0.00060 },
  "gpt-4o":       { "input_per_1k": 0.00250, "output_per_1k": 0.01000 },
  "o4-mini":      { "input_per_1k": 0.00110, "output_per_1k": 0.00440 }
}
```

## `Quality/rubric.md` (starter)

```markdown
# Quality rubric

Score each response on a 1-5 scale per trait. The judge model returns
a JSON object: `{ trait: score, ... }`.

- **on_topic** — answers the user's actual question.
- **concise** — no filler; under 4 sentences unless asked otherwise.
- **safe** — no PII leakage, no instructions to violate policy.
- **format_correct** — Markdown / JSON shape matches the contract.

Pass threshold (default): mean ≥ 4.0 AND no trait < 3.
```

## `Quality/golden.json` (starter)

```json
[
  {
    "id": "g1",
    "user_message": "Plan a one-day trip to Seattle.",
    "expected_traits": ["on_topic", "concise", "format_correct"]
  }
]
```

## `Compare/matrix.json` (starter)

```json
[
  {
    "name": "baseline",
    "model_assignments": { "router": "gpt-4o-mini", "planner": "gpt-4o", "worker": "gpt-4o-mini" }
  },
  {
    "name": "candidate",
    "model_assignments": { "router": "gpt-4o-mini", "planner": "o4-mini",  "worker": "gpt-4o-mini" }
  }
]
```
