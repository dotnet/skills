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
  .config/dotnet-tools.json
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

The `.csproj` is **not hand-written**. SKILL.md step 3 generates the shell with
`dotnet new mstest` (which owns the MSTest / Microsoft.Testing.Platform
references at their SDK-current versions) and then adds the eval + hosting
packages with `dotnet add package` **without hand-authored versions** (see the
version policy below). The listing here is an **illustrative expected result** —
the source of truth for the *eval + hosting* package **set** (not their exact
versions) — and the reconciliation target (TFM, data-file
`CopyToOutputDirectory`, and the agent `ProjectReference`) after `dotnet new`
runs. Do not paste it verbatim over the generated file; let the template own the
test-SDK lines and only add/adjust what is shown.

**The test-SDK line(s) vary by installed SDK — do not hand-maintain them.** On
.NET 10 (`dotnet new mstest`) the template emits a single MTP-native
`MSTest` metapackage (e.g. `4.0.1`) and **no** `Microsoft.NET.Test.Sdk`
reference; on older SDKs it emits `MSTest` 3.x **plus** `Microsoft.NET.Test.Sdk`.
The block below shows the .NET 10 single-package form. Whatever the template
produces is authoritative; the skill never rewrites it.

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
    <!-- Test SDK — owned by `dotnet new mstest`; versions track the installed
         SDK (on .NET 10, the MTP-native `MSTest` metapackage, no
         Microsoft.NET.Test.Sdk). Shown for reference; do not hand-edit. -->
    <PackageReference Include="MSTest" Version="4.0.1" />

    <!-- Eval + hosting packages are added by `dotnet add package` with NO
         hand-authored version (see the version policy below). The Version
         attributes are written by NuGet at scaffold time; the values here are
         only an illustrative snapshot, not a pin to maintain. -->

    <!-- MEAI Evaluation: GA (latest stable) -->
    <PackageReference Include="Microsoft.Extensions.AI" Version="10.7.0" />
    <PackageReference Include="Microsoft.Extensions.AI.Evaluation" Version="10.7.0" />
    <PackageReference Include="Microsoft.Extensions.AI.Evaluation.Quality" Version="10.7.0" />
    <PackageReference Include="Microsoft.Extensions.AI.Evaluation.Reporting" Version="10.7.0" />

    <!-- MEAI Evaluation: preview via `--prerelease` (NLP works without an API key) -->
    <PackageReference Include="Microsoft.Extensions.AI.Evaluation.NLP" Version="10.7.0-preview.1.26309.7" />

    <!-- Safety: only added (with `--prerelease`) when the user opts in -->
    <!-- <PackageReference Include="Microsoft.Extensions.AI.Evaluation.Safety" Version="10.7.0-preview.*" /> -->

    <!-- Hosting/config: latest stable resolves to >= 10.0.1 automatically,
         which is the floor that avoids the NU1605 downgrade (see below) -->
    <PackageReference Include="Microsoft.Extensions.Hosting" Version="10.0.9" />
    <PackageReference Include="Microsoft.Extensions.Configuration.Json" Version="10.0.9" />
    <PackageReference Include="Microsoft.Extensions.Configuration.UserSecrets" Version="10.0.9" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\{{AppName}}.Agent\{{AppName}}.Agent.csproj" />
  </ItemGroup>

  <ItemGroup>
    <None Update="Telemetry\inputs.json;Telemetry\prices.json;Quality\rubric.md;Quality\golden.json;Compare\matrix.json;quality.thresholds.json" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
</Project>
```

**Version policy (no hand-authored version literals):**

- **GA** packages — `Microsoft.Extensions.AI`, `Microsoft.Extensions.AI.Evaluation`,
  `.Quality`, `.Reporting` — are added with `dotnet add package <id>` (no
  `--version`), so they resolve to the **latest stable**.
- **Preview** packages — `.NLP` (opt-in-on) and `.Safety` (opt-in-off) — have no
  stable release yet, so they are added with `dotnet add package <id> --prerelease`
  (**latest prerelease**). No specific preview build is pinned.
- **Hosting/config** — `Microsoft.Extensions.Hosting` and
  `Microsoft.Extensions.Configuration.*` — are also added at latest stable. They
  must resolve to **>= 10.0.1** (not `10.0.0`) to satisfy the transitive
  constraint from `Microsoft.Agents.AI.Hosting`; a pin at `10.0.0` produces
  `NU1605`. Latest stable is always >= that floor, so no explicit pin is needed —
  this is a *floor*, not a hand-maintained version.

`dotnet add package` writes the concrete resolved version into the `.csproj`; the
skill never authors those numbers. The versions shown in the block above are an
illustrative snapshot from one scaffold run, not values to keep in sync.

## Tool manifest (`dotnet-tools.json`)

Do **not** hand-write this file with a pinned version. Generate it (unpinned):

```pwsh
dotnet new tool-manifest                                          # if none exists
dotnet tool install microsoft.extensions.ai.evaluation.console    # latest; provides `aieval`
```

`dotnet tool install` (no `--version`) records the current tool version in the
manifest. `dotnet new tool-manifest` places it per the SDK default (a
`.config/dotnet-tools.json`, or a repo-root `dotnet-tools.json` on newer SDKs);
`dotnet tool restore` finds it either way. The illustrative manifest below shows
the resulting shape:

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
