# Project template — `<App>.Evals`

The exact files written when scaffolding the eval harness.

## `<App>.Evals.csproj`

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
    <PackageReference Include="Microsoft.Extensions.AI" />
    <PackageReference Include="Microsoft.Extensions.AI.Evaluation" />
    <PackageReference Include="Microsoft.Extensions.AI.Evaluation.Quality" />
    <PackageReference Include="Microsoft.Extensions.AI.Evaluation.Reporting" />
    <PackageReference Include="Microsoft.Extensions.Configuration.Json" />
    <PackageReference Include="Microsoft.Extensions.Hosting" />
  </ItemGroup>

  <ItemGroup>
    <None Update="Telemetry/inputs.json"; CopyToOutputDirectory="PreserveNewest" />
    <None Update="Telemetry/prices.json"; CopyToOutputDirectory="PreserveNewest" />
    <None Update="Quality/rubric.md"; CopyToOutputDirectory="PreserveNewest" />
    <None Update="Quality/golden.json"; CopyToOutputDirectory="PreserveNewest" />
    <None Update="Compare/matrix.json"; CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>

</Project>
```

## `Program.cs`

```csharp
var mode = args.FirstOrDefault() ?? "telemetry";
var runner = mode switch
{
    "telemetry" => (IEvalRunner)new TelemetryEvalRunner(),
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
