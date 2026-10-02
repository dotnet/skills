---
name: csharp-expert
description: >-
  Route C# and .NET requests to the most specific skill by combining the user's intent with solution
  evidence, and help obtain missing specialists from the dotnet/skills plugin marketplace. USE FOR:
  any C#/.NET task where ASP.NET Core, Blazor, MAUI, WinForms, EF Core, testing, MSBuild, NuGet,
  diagnostics, upgrades, interop, performance, templates, AI, or C# semantics may determine the
  workflow; ambiguous prompts such as "fix this", "upgrade", "make it faster", or "write tests";
  identifying which dotnet-agent-skills plugin to install; and mixed .NET solutions needing multiple
  specialists. DO NOT USE FOR: requests clearly unrelated to C# or .NET, or when the user explicitly
  selected an available specialist skill and no routing or marketplace decision remains.
license: MIT
---

# C# Expert

## Purpose

Act as the front door for C# and .NET work. Determine what the user wants, identify the kind of
solution that owns the work, invoke the narrowest installed specialist, and give exact
`dotnet/skills` marketplace installation steps when that specialist is missing. Keep direct C#
language guidance as the fallback, not the default.

## Routing Contract

1. Classify the requested outcome from the prompt.
2. Inspect the smallest set of repository files needed to identify the solution type.
3. Compare both signals with the descriptions of the skills currently available to the runtime.
4. If the best skill is available, invoke it with the `skill` tool as the first external action; do
   not emit a routing explanation before the invocation and do not merely recommend the skill.
5. If the best skill is missing, identify its plugin in
   `references/dotnet-skills-marketplace.md` and provide the exact marketplace and install commands.
6. Use multiple skills only when the request has distinct phases with different owners.
7. If no narrower marketplace skill owns the request, continue with the C# fallback workflow.

When the route needs to be visible in the eventual response, use one concise line:

```text
Route: `<skill-name>` - <prompt signal> + <solution signal>.
```

For a sequence:

```text
Route: `<first-skill>` -> `<second-skill>` - <why the phases have different owners>.
```

Do not turn routing into a menu. Choose a route, invoke it, and proceed.

## Step 1: Classify the Prompt

Identify the primary action before inspecting implementation details.

| Prompt intent | Prefer skills whose description owns |
|---|---|
| Create or scaffold | Project/template creation for the detected solution type |
| Add application behavior | The framework or component where the behavior lives |
| Fix a compiler/runtime defect | The narrow language, framework, data, or interop owner |
| Build or restore failure | MSBuild, SDK, workload, project-reference, or NuGet diagnosis |
| Write, run, review, or migrate tests | The exact testing lifecycle or migration requested |
| Upgrade or migrate | The source version, target version, and artifact being migrated |
| Diagnose slowness, crash, hang, or memory growth | Runtime diagnostics unless evidence points to build performance or a local code hot path |
| Refactor without changing behavior | Refactoring rather than feature or bug-fix guidance |
| Package, publish, or trust a feed | NuGet/package-publishing workflow |
| Ask about C# syntax, types, nullability, async, or APIs | A language specialist unless solution-specific behavior is load-bearing |

Treat user nouns as clues, not proof. "Performance" may mean runtime tracing, a microbenchmark, EF
query shape, SIMD, allocation-heavy C#, or MSBuild evaluation. "API" may mean ASP.NET Core, a public
library contract, or an external service client.

## Step 2: Detect the Solution Type

Inspect only likely manifests and nearby owning files. Prefer a solution/project file and the file
named by the prompt over broad repository searches.

Use LSP navigation when available to trace a prompt-named symbol or file to its owning project and
nearby callers. Use LSP diagnostics as early evidence, but do not treat them as a substitute for the
specialist's required build or runtime validation.

| Evidence | Solution or concern |
|---|---|
| `Microsoft.NET.Sdk.Web`, controllers, endpoints, middleware, OpenAPI | ASP.NET Core |
| `.razor`, `AddRazorComponents`, Blazor bootstrapping | Blazor |
| `<UseMaui>true</UseMaui>`, `MauiProgram`, XAML pages | .NET MAUI |
| `<UseWindowsForms>true</UseWindowsForms>`, `Form`, designer files | Windows Forms |
| EF Core package references, `DbContext`, migrations | .NET data / EF Core |
| `<IsTestProject>true</IsTestProject>`, test SDK/framework packages | .NET testing |
| `Directory.Build.*`, custom targets/tasks, `.binlog`, evaluation errors | MSBuild |
| `Directory.Packages.props`, package restore/version conflicts, feeds | NuGet |
| Old and new TFMs, framework-version migration request, compatibility warnings | .NET upgrade |
| `PublishAot`, trimming warnings, native library calls | AOT, interop, or deployment compatibility |
| Aspire AppHost or distributed-application model | Aspire |
| AI/ML/LLM packages or agent/RAG/MCP application code | .NET AI |
| No project plus an explicit request for a one-file C# program | File-based C# |
| None of the above; correctness depends on C# semantics | C# language fallback |

When several project types exist, trace from the file or behavior named in the prompt to its owning
project. Do not route the whole solution from the first `.csproj` found.

## Step 3: Match the Skill and Marketplace Plugin

Use the runtime-provided available-skill names and descriptions as the source of truth. Do not
search for a skill installation directory or invoke a remembered skill that is not currently
available.

Rank candidates in this order:

1. An exact transformation or lifecycle skill, such as a specific test migration, framework
   conversion, template operation, query optimization, or diagnostic collection workflow.
2. A framework/component skill matching the owning project and requested behavior.
3. A tooling skill matching the failing subsystem, such as MSBuild, NuGet, test execution, SDK
   setup, or runtime diagnostics.
4. A cross-cutting specialist matching the actual mechanism, such as interop, vectorization,
   serialization, AOT, or microbenchmarking.
5. `csharp-refactoring` for behavior-preserving structural change.
6. The C# language fallback below when no narrower available skill owns the work.

Because `csharp-expert` ships in the core `dotnet` plugin, prefer its installed sibling skills
`csharp-refactoring`, `msbuild`, and `setup-local-sdk` when they own the request. Do not require the
user to install `dotnet-msbuild` merely to analyze an ordinary build failure or binlog that the
core `msbuild` entry already covers.

The most specific noun is not always the owner. Route by the decision that determines success:

| Ambiguous request | Distinguishing evidence |
|---|---|
| "Make this faster" | Build duration -> build-performance skill; SQL/query shape -> data skill; process CPU/memory -> diagnostics; isolated code comparison -> microbenchmarking/vectorization |
| "Fix the API" | HTTP pipeline/endpoint -> ASP.NET Core; public type contract -> C# fallback; JSON version behavior -> serialization specialist |
| "Upgrade the tests" | Framework version change -> exact migration skill; failing execution -> run-tests/platform skill; quality review -> analysis skill |
| "Fix nullability" | Project-wide nullable adoption -> migration skill; one incorrect flow/contract -> C# fallback; generated framework binding -> owning framework skill |
| "Add authentication" | Framework-specific application auth -> owning framework skill; token parsing primitive -> C# fallback |

If two candidates remain plausible, gather one more decisive artifact rather than loading both.

After selecting the capability:

1. If its skill appears in the runtime's available-skill catalog, invoke it immediately.
2. If it does not appear, open `references/dotnet-skills-marketplace.md` and map the capability or
   skill name to the owning marketplace plugin.
3. Recommend the smallest plugin that contains the needed skill. Do not install every .NET plugin.
4. Follow the missing-skill workflow below. Do not claim that an unavailable skill was loaded.

## Step 4: Obtain a Missing Skill

For GitHub Copilot CLI or Claude Code, give these exact commands with the selected plugin substituted:

```text
/plugin marketplace add dotnet/skills
/plugin install <plugin>@dotnet-agent-skills
```

Then require:

```text
Restart the host, run `/skills` to confirm the specialist is available, and rerun the request.
```

Use this response shape:

```text
Missing specialist: `<skill>` from `<plugin>`.
Install:
  /plugin marketplace add dotnet/skills
  /plugin install <plugin>@dotnet-agent-skills
After restart: run `/skills`, then rerun: "<concise original request>"
```

Rules:

- The marketplace name is `dotnet-agent-skills`; the source repository is `dotnet/skills`.
- The install target is the plugin name, not the individual skill name.
- If the marketplace is already registered, the add command may report that fact; continue with the
  install command.
- Slash commands are host actions. Present them exactly; do not run shell commands that pretend to
  install a Copilot or Claude plugin.
- Do not continue with a specialist workflow in the same turn when loading it requires a restart.
- If the plugin is installed but the skill is absent, ask the user to restart and check `/skills`
  before recommending a reinstall.
- For Codex CLI, VS Code, Cursor, or individual-skill installation, use the host-specific commands in
  `references/dotnet-skills-marketplace.md`.
- If installation is impossible or declined, state the reduced coverage and use the safest local
  fallback only when it can still satisfy the request correctly.

## Step 5: Compose Skills Deliberately

Use a sequence only when phases are independently owned. Examples:

- Scaffold a project, then author a framework-specific component.
- Collect a trace, then analyze the captured performance evidence.
- Upgrade a target framework, then address a separately requested AOT compatibility phase.
- Detect the test platform, then run tests with the correct filter syntax.

Do not chain skills that duplicate each other, load an entire plugin "just in case", or use a
generic skill before a specialist that already owns the request. After a specialist is loaded,
follow its workflow and boundaries.

## Step 6: C# Language Fallback

Use this only when no narrower available skill matches and the load-bearing problem is C# language
or runtime semantics.

1. Reproduce the exact compiler diagnostic, failing test, exception, or incorrect behavior.
2. Inspect the owning project for TFM, language version, nullable policy, analyzers, and existing
   tests.
3. Preserve public signatures, serialization shape, ownership, cancellation, disposal, and
   multi-target behavior unless the request explicitly changes them.
4. Implement the smallest complete fix through the affected call path.
5. Check LSP diagnostics when available, then build the narrowest affected project and run focused
   tests or the executable path that proves the original symptom is gone.

Do not raise the SDK, TFM, language version, package versions, or analyzer settings merely to make a
local C# edit compile. Do not edit generated files. Do not use broad casts, null-forgiving
operators, catch-all handlers, or fire-and-forget work to hide evidence.

## Boundaries and Failure Handling

- If the best specialist is unavailable, provide its exact `dotnet/skills` plugin installation
  command. Use a fallback only if installation is impossible or declined and reduced coverage is
  explicit.
- If repository evidence contradicts the prompt, state the mismatch and route from the evidence
  that owns the requested file or behavior.
- If the user explicitly requests analysis only, route to the correct analysis skill but do not
  edit.
- If a loaded specialist reports that its prerequisites are absent, return here, reclassify using
  that evidence, and choose one different route. Do not bounce repeatedly between skills.
- Non-.NET work is out of scope; leave this skill dormant rather than forcing a .NET interpretation.

## Observable Completion Criteria

- The selected skill matches both the requested outcome and the owning solution/project evidence.
- The selected skill was invoked when available, not merely named.
- A missing specialist is mapped to its exact marketplace plugin and install command.
- Installation guidance includes restart, `/skills` verification, and a concise request to rerun.
- At most one primary skill owns each phase.
- Mixed requests have an explicit, minimal sequence.
- Pure C# work falls back locally without inventing a framework route.
- Validation follows the invoked specialist's contract or, for fallback work, reproduces the
  original behavior after the focused build/test.

## Common Routing Mistakes

| Mistake | Correction |
|---|---|
| Selecting from prompt keywords alone | Confirm the owning project or artifact. |
| Selecting from project type alone | Match the requested lifecycle; a Blazor test migration is still a test migration. |
| Naming several possible skills | Gather one discriminating fact, choose one, and proceed. |
| Assuming a remembered skill is installed | Use only the runtime's available-skill catalog. |
| Recommending a missing skill without helping install it | Map it to its plugin and emit the exact marketplace install command. |
| Installing a skill name | Install `<plugin>@dotnet-agent-skills`; plugins are the marketplace unit. |
| Pretending slash commands ran successfully | Require the user to install, restart, verify with `/skills`, and rerun. |
| Treating all performance work as code optimization | Separate runtime, build, query, and microbenchmark evidence. |
| Using generic C# guidance for framework behavior | Load the framework specialist first. |
| Loading a specialist but ignoring its workflow | After routing, follow the loaded skill as the task owner. |
| Re-routing forever | After one failed prerequisite check, choose one evidence-backed fallback. |
