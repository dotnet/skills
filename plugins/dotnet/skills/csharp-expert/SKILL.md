---
name: csharp-expert
description: >-
  Route C# and .NET requests to the exact installed specialist or smallest dotnet/skills
  marketplace plugin. USE FOR: "which specialist should own this" or "how do I add the skill"
  requests involving ASP.NET Core endpoints, Blazor, MAUI binding, Windows Forms specialist
  selection or installation, EF Core queries, test or framework migration, runtime
  CPU/allocation evidence, file-based C#, editor/compiler defects, a surviving MSBuild
  `.binlog`, or a plugin missing from `/skills`; also use for C# semantics
  when no narrower specialist exists. DO NOT USE FOR: requests that already name the exact
  installed specialist to invoke, or work unrelated to C# or .NET.
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
4. If the best skill is available and the user asked to perform the downstream work, invoke it with
   the `skill` tool as the first external action; do not emit a routing explanation before the
   invocation and do not merely recommend the skill. For selection or preparation-only requests,
   name the installed specialist and stop without invoking it.
5. If the best skill is missing, identify its plugin in
   `references/dotnet-skills-marketplace.md`, then decide whether the current task can still be
   completed safely with repository tools and general .NET knowledge.
6. Use multiple skills only when the request has distinct phases with different owners.
7. If no narrower marketplace skill owns the request, continue with the C# fallback workflow.

Routing is not task completion. A missing specialist changes the confidence and preferred workflow;
it does not automatically justify stopping. Continue in the same turn when the task can be completed
and validated without the specialist. Stop for installation only when the missing capability is
actually required to proceed safely or the user asked specifically to install or load it.

Use these fast paths before general repository exploration:

- **Marketplace selection only:** when the prompt already states the framework, lifecycle, host, and
  required behavior, read only the bundled marketplace reference. Do not inspect the fixture,
  repository, GitHub, or plugin source. Name the exact skill and plugin, explain the decisive mapping,
  give host-correct acquisition steps, and stop.
- **Only surviving diagnostic artifact:** inspect that artifact first with the narrowest available
  query. For a supplied `.binlog`, do not glob, list, or search unrelated workspace files before
  extracting its recorded error, property, target, and path evidence.

Choose the operating mode from the user's requested outcome:

| User asks for | Required behavior |
|---|---|
| Implement, fix, diagnose, migrate, or create | Invoke the installed specialist, or complete a safe local fallback. If a narrower specialist exists but is unavailable, report its optional plugin afterward unless the user prohibited installation advice. |
| Identify, choose, install, prepare, or load the right marketplace capability | Inspect enough solution evidence to choose the owner, name it whether installed or missing, give acquisition steps only when needed, and stop without invoking the specialist, editing files, or generating the requested application artifact. |
| Recover a plugin already installed but absent from `/skills` | Refresh discovery first; do not reinstall or update on the first response. |

Choose one owner per phase. Do not expose internal routing ceremony or turn the answer into a menu.

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

When a deployed .NET process needs CPU and allocation evidence and no observability vendor is named,
prefer the vendor-neutral .NET runtime diagnostics route. Do not substitute an APM-vendor agent for
raw process evidence merely because it can also report performance data. In a selection answer,
state that runtime trace collection gathers deployed-process evidence before a hot method is known,
whereas source optimization starts from code or an already identified hot path.

## Step 2: Detect the Solution Type

Inspect only likely manifests and nearby owning files. Prefer a solution/project file and the file
named by the prompt over broad repository searches.

Use LSP navigation when available to trace a prompt-named symbol or file to its owning project and
nearby callers. Use LSP diagnostics as early evidence, but do not treat them as a substitute for the
specialist's required build or runtime validation.

When the prompt names a C# source file with an editor/compiler defect and LSP is available, request
diagnostics before running a build or broad search. Use the diagnostic location and code to scope
the edit, request diagnostics again after the edit, then run the narrowest build or test that proves
the fix.

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

1. If its skill appears in the runtime's available-skill catalog, invoke it immediately only when
   the operating mode requires downstream implementation. For selection or preparation-only mode,
   name the installed skill and stop after the requested plan or availability guidance.
2. If it does not appear, open `references/dotnet-skills-marketplace.md` and map the capability or
   skill name to the owning marketplace plugin.
3. Recommend the smallest plugin that contains the needed skill. Do not install every .NET plugin.
4. Follow the missing-skill workflow below. Do not claim that an unavailable skill was loaded, and
   do not stop if a safe, verifiable local fallback can still complete the request.

When the bundled reference contains a maintained marketplace capability, recommend that capability.
Do not ask the user to author a repository-local agent or skill as a substitute. For migrations,
state the source-to-target lifecycle and parameterization mappings that make the chosen capability
fit, not only its name.

For marketplace-planning requests, name both the narrow skill and its plugin. Use project evidence
to disambiguate framework nouns, but do not perform the downstream implementation the user asked to
prepare for.

For migration selection, quote concrete source-to-target syntax from the bundled reference: include
at least one lifecycle mapping and one parameterization mapping instead of saying only that those
behaviors are supported.

Use the bundled marketplace reference as the authoritative lookup. Do not search GitHub, inspect
unrelated plugin source, or enumerate alternatives after the prompt and one nearby manifest already
identify the owner. If the prompt already names the source and target lifecycle or an unambiguous
artifact constraint, do not inspect files merely to reconfirm it. A selection request that states
the framework, lifecycle, and required behavior needs no repository search: read only the bundled
reference, choose the owner, and answer. Do not inspect the local skill source, marketplace checkout,
or fixture merely to prove that a named capability exists.

Answer in four compact parts:

1. Exact skill name and plugin; never substitute a generic capability label when the bundled
   reference contains an exact route.
2. One sentence matching the decisive behavior or artifact evidence. Use the user's concrete
   mechanism: N+1/database round trips for repeated EF related-data queries; deployed-process
   CPU/allocation collection before a known hot path for runtime tracing; source and target TFM
   plus compatibility work for upgrades.
3. Host-correct install steps.
4. Restart/discovery verification, when the host requires it.

For a selection answer, completeness beats extra exploration. Read the bundled reference once,
then answer. Do not call host help, search the web, or inspect plugin source to reconfirm commands
already present in the reference.

Do not add a `Route:` header in marketplace-planning mode; lead with the capability and plugin.
Do not mention this skill's step numbers, fallback labels, routing contract, or internal selection
process in the user-facing answer.

## Step 4: Obtain a Missing Skill

For GitHub Copilot CLI or Claude Code, give these exact commands with the selected plugin substituted:

```text
/plugin marketplace add dotnet/skills
/plugin install <plugin>@dotnet-agent-skills
```

When installation is the next step, require:

```text
Restart the host, run `/skills` to confirm the specialist is available, and rerun the request.
```

When the task can proceed without the specialist:

1. State the missing specialist and reduced coverage in one concise sentence.
2. Complete the requested work now using the repository, standard .NET tooling, and the fallback
   rules that match the task.
3. Validate the result as narrowly as possible.
4. Put optional installation guidance after the result. Do not ask whether to proceed, defer the
   implementation, or make the user repeat the request.

If the user explicitly says not to recommend or discuss installation, omit the missing-plugin
sentence and all acquisition guidance. Complete and validate the safe fallback with the capabilities
available in the current run.

This reduced-coverage path may still perform framework, migration, diagnostics, or tooling work.
Preserve the selected domain's invariants and report specialist-specific checks that were unavailable.

Rules:

- The marketplace name is `dotnet-agent-skills`; the source repository is `dotnet/skills`.
- The install target is the plugin name, not the individual skill name.
- If the marketplace is already registered, the add command may report that fact; continue with the
  install command.
- Slash commands are host actions. Present them exactly; do not run shell commands that pretend to
  install a Copilot or Claude plugin.
- Do not pretend to continue with the unavailable specialist workflow. Use an explicit local
  fallback when the task remains safely achievable.
- If the plugin is installed but the skill is absent, ask the user to restart and check `/skills`
  before recommending a reinstall.
- For Codex CLI, VS Code, Cursor, or individual-skill installation, use the host-specific commands in
  `references/dotnet-skills-marketplace.md`.
- If installation is impossible, declined, or not necessary for the immediate task, state the
  reduced coverage and use the safest local fallback when it can still satisfy the request.
- Never trade away implementation or validation merely to produce installation instructions.

### Installed but not discovered

When the user says the plugin is already installed but its skills are absent:

1. Trust the stated installed state unless repository evidence directly contradicts it.
2. Start by acknowledging that the plugin is installed and discovery is stale.
3. Tell the user to restart or reload the host, then run `/skills`.
4. Name the expected skill so discovery can be verified.
5. Stop there on the first response. Do not emit marketplace-add, install, update, shell-level
   plugin-management, `/skills reload`, or invented explicit-invocation commands.

Only after the user reports that restart plus `/skills` still fails should the next response move to
host-specific update or reinstall diagnostics.

## Step 5: Compose Skills Deliberately

Use a sequence only when phases are independently owned. Examples:

- Scaffold a project, then author a framework-specific component.
- Collect a trace, then analyze the captured performance evidence.
- Upgrade a target framework, then address a separately requested AOT compatibility phase.
- Detect the test platform, then run tests with the correct filter syntax.

Do not chain skills that duplicate each other, load an entire plugin "just in case", or use a
generic skill before a specialist that already owns the request. After a specialist is loaded,
follow its workflow and boundaries.

If one or more phase specialists are missing but ordinary `dotnet` commands and repository edits
can complete the phases, execute the phases in order and report the optional plugins afterward.
Do not defer an entire multi-phase request solely because the ideal plugin set is unavailable.

## Step 6: C# Language Fallback

Use this only when no narrower available skill matches and the load-bearing problem is C# language
or runtime semantics.

1. Reproduce the exact compiler diagnostic, failing test, exception, or incorrect behavior when
   source is available. If the defect is fully specified but no repository was provided, answer
   with the concrete minimal code pattern instead of refusing to help.
2. Inspect the owning project for TFM, language version, nullable policy, analyzers, and existing
   tests.
3. Preserve public signatures, serialization shape, ownership, cancellation, disposal, and
   multi-target behavior unless the request explicitly changes them.
4. Implement the smallest complete fix through the affected call path.
5. Check LSP diagnostics when available, then build the narrowest affected project and run focused
   tests or the executable path that proves the original symptom is gone.

For a marketplace-planning request whose correct route is this fallback, say: `No additional
marketplace plugin is required; the loaded csharp-expert skill owns this C# semantic fix.` Do not
claim that no skill or specialist is involved.

Do not raise the SDK, TFM, language version, package versions, or analyzer settings merely to make a
local C# edit compile. Do not edit generated files. Do not use broad casts, null-forgiving
operators, catch-all handlers, or fire-and-forget work to hide evidence.

When a framework type provides an ownership-preserving overload such as `leaveOpen: true`, give that
canonical fix only. Never suggest intentionally leaking or skipping disposal of a disposable
wrapper as an alternative. Preserve the example's observable behavior: do not add null coalescing,
change a nullable return to a non-null value, alter access modifiers, or invent unrelated error
handling merely to make a conceptual snippet look more complete. If an example directly returns
`StreamReader.ReadLine()`, use a nullable `string?` return in nullable-aware C#; do not show
`string` while claiming that the existing null-on-end-of-stream behavior is preserved.

## Boundaries and Failure Handling

- If the best specialist is unavailable, provide its exact `dotnet/skills` plugin installation
  command. Continue immediately with an explicit reduced-coverage fallback whenever standard tools
  can still complete and validate the task.
- If repository evidence contradicts the prompt, state the mismatch and route from the evidence
  that owns the requested file or behavior.
- If the user explicitly requests analysis only, route to the correct analysis skill but do not
  edit.
- If the user supplies the only surviving diagnostic artifact, analyze that artifact directly.
  Routing must not add installation attempts, unrelated checkout searches, or marketplace ceremony
  before the evidence is read.
- For an artifact-backed failure, propose only the smallest repair supported by the recorded
  evidence. Do not add alternate configuration or relocation advice unless the artifact indicates
  that the configured path is wrong rather than the required input being absent.
- Describe a missing artifact at its exact configured relative path. Do not call a nested path such
  as `schemas/prod.json` the repository root, and explicitly rule out build or CI configuration
  changes when the recorded command already proves the configured path.
- If a loaded specialist reports that its prerequisites are absent, return here, reclassify using
  that evidence, and choose one different route. Do not bounce repeatedly between skills.
- Non-.NET work is out of scope; leave this skill dormant rather than forcing a .NET interpretation.

## Observable Completion Criteria

- One evidence-backed owner is selected for each distinct phase and invoked when available.
- Missing specialists map to the smallest correct plugin and host-specific acquisition path.
- Safe local work continues despite a missing specialist; preparation-only requests stop before edits.
- Pure C# work stays local and preserves behavior; implementation work receives focused validation.
