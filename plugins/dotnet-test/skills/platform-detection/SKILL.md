---
name: platform-detection
description: >-
  Identify a .NET project's test platform, framework, command mode, and
  SDK-style vs classic project system. Use only for "which test
  platform/framework?", "VSTest or MTP?", or "what runner does this project
  use?", including bridge settings, UseVSTest opt-outs, and incompatible or
  conflicting VSTest/MTP configuration. Resolves global.json, project,
  packages.config, Directory.Build.props, and Directory.Packages.props
  precedence for MSTest/xUnit/NUnit/TUnit. DO NOT USE when the user asks to
  run/filter tests or for commands, flags, TRX/dumps, or test-command/filter
  errors; use run-tests directly.
  Do not use for hot reload or migration.
license: MIT
---

# Test Platform and Framework Detection

Determine **which test platform** (VSTest or Microsoft.Testing.Platform) and **which test framework** (MSTest, xUnit, NUnit, TUnit) a project uses.

## Response contract

Honor the user's requested labels and order exactly, substituting the actual
classification for every placeholder. Start with the verdict: never put a
heading, scratch analysis, tool syntax, or an echoed template before it. Follow
with one concise evidence sentence naming the repository facts needed to justify
every requested classification. When `Framework` is requested, name the package
or project SDK that identifies it. Use a second sentence only for a conflict, an
incomplete configuration, or target-framework-specific differences.

`Platform` means the platform that actually executes tests: **VSTest** or
**MTP**. If conflicting or incomplete configuration prevents execution, report
it as unavailable rather than inventing a successful platform.

Never classify the executed platform from `global.json` `test.runner` alone.
That setting selects `dotnet test` command mode; even an explicit `VSTest`
value can bridge to an executable MTP application. Continue through the project
runner, bridge, and output-shape signals before writing `Platform:`.

Apply this scope gate before drafting the evidence:

| User asks for | Evidence to include | Omit |
|---------------|---------------------|------|
| Platform and framework | Final runner selector and its winning source; package or project SDK identifying the framework; when needed, the property that makes it executable | Command mode; common SDK facts; `OutputType` unless it is missing or conflicting |
| The single deciding signal | That runner-selection property, why its source wins, and why a competing package does not select or imply VSTest; still name the package or project SDK identifying a requested framework | Bridge, `OutputType`, SDK mode, and unrelated prerequisites when the configuration is complete |
| Platforms per target framework | Only the conditional final values that differ by target | Common properties and project-wide SDK commentary |
| Explicit opt-out | Final `UseVSTest` value and its winning source | Superseded defaults unless they create a conflict |
| `dotnet test` mode | The separate command-mode and executed-platform classifications | None of the requested axes |

If the requested labels omit `dotnet test mode`, do not state or explain command
mode anywhere in the response. An exact bridge property may still be decisive
platform evidence, but do not turn it into SDK or CLI-mode commentary.
When the user asks which single signal decides between an explicit runner
property and `Microsoft.NET.Test.Sdk`, the evidence sentence must say both that
the runner property selects MTP and that `Microsoft.NET.Test.Sdk` does not
select or imply VSTest.

When import precedence decides a property, state why the winning source wins
(for example, it is imported later or its condition applies), not merely that it
contains the final value or overrides another assignment.

Keep the explanation on the requested axis:

- An SDK version pin in `global.json` is context, not a platform selector. Claim
  that `global.json` selects VSTest or native MTP only when its `test.runner`
  setting actually does so.
- If `UseVSTest=true` is decisive, say that directly. Do not speculate about an
  absent `test.runner` or describe SDK pinning as an additional platform choice.
- If command mode was not requested, do not add it, even when it was needed
  internally to determine the executed platform.

When a classic-project request also asks for the command family, add a direct
line such as `Command family: MSBuild + vstest.console.exe`; do not turn it into
an optional alternative or add an unnecessary build qualifier.

For a file-backed request, enumerate the following configuration names once,
then read every relevant file that is present in one batched operation:
`global.json`, `.csproj`, `packages.config`, `Directory.Build.props`,
`Directory.Build.targets`, `Directory.Packages.props`, and explicit imported
`.props` / `.targets`. A setting absent from the project file may be defined by
an import, so never infer its final value from the `.csproj` alone. Do not search
the web or inspect unrelated files when repository configuration is sufficient.

Resolve properties in the actual MSBuild import order, not with a fixed
"project beats props" rule. For every applicable target framework:

1. Follow the import graph and conditions. A later applicable assignment wins.
   `Directory.Build.props` is normally imported before the project body, so an
   unconditional project assignment normally overrides it; later `.targets`
   can override the project again.
2. Record the final value and its winning source for `UseVSTest`, the framework
   runner selector, `TestingPlatformDotnetTestSupport`, and `OutputType`.
3. Treat `Directory.Packages.props` as version evidence unless it also contains
   relevant properties. Resolve package/SDK versions before applying
   version-dependent defaults.
4. Never infer a property from package presence. A package or SDK default counts
   only when that resolved version actually supplies it and no later assignment
   overrides it.

## Detecting the project system

Classify the project before selecting a CLI:

- Root `Sdk` attribute or `<Sdk>` declaration: SDK-style.
- `ToolsVersion`, `Microsoft.Common.props` / `Microsoft.CSharp.targets` imports,
  explicit `<Reference>` and `<Compile Include>` items: classic non-SDK.
- `packages.config`: classic NuGet dependency management.

Classic projects can still use VSTest-compatible adapters, but `dotnet test` is
not automatically a valid invocation. Preserve repository scripts/CI commands,
commonly MSBuild followed by `vstest.console.exe`. Mention `MSTest.exe` only
when repository configuration or documentation establishes that legacy runner.

## Detecting the test framework

Read the `.csproj`, adjacent `packages.config`, and
`Directory.Build.props` / `Directory.Packages.props` and look for:

| Package or SDK reference | Framework |
|--------------------------|-----------|
| `MSTest` metapackage, `<Project Sdk="MSTest.Sdk[/version]">`, or `<Sdk Name="MSTest.Sdk">` | MSTest |
| `MSTest.TestFramework` + `MSTest.TestAdapter` | MSTest (also valid for v3/v4) |
| `xunit`, `xunit.v3`, `xunit.v3.mtp-v1`, `xunit.v3.mtp-v2`, `xunit.v3.core.mtp-v1`, `xunit.v3.core.mtp-v2` | xUnit |
| `NUnit` + `NUnit3TestAdapter` | NUnit |
| `TUnit` | TUnit (MTP only) |

In classic projects, package IDs and versions may appear only in
`packages.config`, while the project contains assembly `<Reference>` elements
with `HintPath` values. Use both sources.

## Detecting the executed test platform

If the user explicitly requests `dotnet test` mode, read
[`references/command-mode.md`](references/command-mode.md) before answering.
Do not load that reference or mention command mode for a
platform/framework-only request.

For an SDK 8/9 request that explicitly asks about command mode and has no
effective bridge, state all three facts in one causal sentence: the runner makes
the project MTP-capable, `dotnet test` remains in VSTest mode, and the missing
bridge means VSTest actually executes the tests. Mention that native MTP command
mode starts with SDK 10 only when it helps explain that result.

When execution is permitted and neither the prompt nor `global.json` identifies
the SDK, run `dotnet --version` once. For read-only identification requests that
prohibit execution, do not probe the installed SDK; use repository facts and
state any necessary SDK assumption.

After resolving final property values, classify in this order:

1. Final `UseVSTest=true` selects VSTest. If `global.json` simultaneously
   selects native MTP command mode, report `Platform: unavailable` because the
   command mode and project opt-out conflict.
2. A native-MTP selection in `global.json` executes a compatible MTP
   application with final `OutputType=Exe` on MTP. A VSTest-only,
   library-output, or opted-out project is unavailable.
3. On SDK 8/9, an enabled MTP runner plus final
   `TestingPlatformDotnetTestSupport=true` plus final `OutputType=Exe` executes
   on MTP.
4. If the runner is enabled but the bridge is absent or false, a dual-capable
   MSTest, NUnit, or xUnit project remains on VSTest: the runner establishes MTP
   capability, but SDK 8/9 `dotnet test` cannot reach it and the VSTest adapter
   executes the tests instead. If the bridge is true but no runner is enabled,
   the project also remains on VSTest.
5. A runner and bridge with non-executable output is incomplete and unavailable.
   An MTP-only framework that cannot be reached by the selected SDK path is also
   unavailable, not VSTest.

Keep each signal's role exact:

- The runner property selects the test application.
- `TestingPlatformDotnetTestSupport=true` lets SDK 8/9 `dotnet test` reach that
  application.
- `OutputType=Exe` supplies the executable host shape. It does **not** select or
  enable MTP.

Do not confuse the `MSTest` metapackage with the `MSTest.Sdk` project SDK.
`PackageReference Include="MSTest"` plus `EnableMSTestRunner=true` enables the
MSTest MTP runner, but it does **not** implicitly set
`TestingPlatformDotnetTestSupport`.

MSTest.Sdk enables the MTP runner by default. Check its resolved version and
evaluated properties for bridge behavior: version 3.8 supplies
`TestingPlatformDotnetTestSupport` unless a later assignment overrides it,
while newer SDKs on .NET 10 may expect native MTP mode instead.
`<UseVSTest>true</UseVSTest>` opts back into VSTest.

| Signal | Meaning |
|--------|---------|
| `<Project Sdk="MSTest.Sdk...">` with no `UseVSTest` | MTP application; inspect the resolved SDK version and evaluated bridge property |
| `MSTest` metapackage + `<EnableMSTestRunner>true>` | MTP runner enabled; does not imply the VSTest-to-MTP bridge |
| `<UseMicrosoftTestingPlatformRunner>true` | Deciding xUnit runner-selection signal |
| `<EnableMSTestRunner>true>` / `<EnableNUnitRunner>true>` | Deciding MSTest/NUnit runner-selection signal |
| `TestingPlatformDotnetTestSupport=true` | Execution prerequisite for a VSTest-to-MTP bridge, not the runner-selection signal |
| `Microsoft.Testing.Platform` package | MTP-capable application; not decisive by itself |
| `TUnit` | MTP-only framework |
| Final evaluated `<OutputType>Exe</OutputType>` | Required executable host shape for package-based MTP applications |

`Microsoft.NET.Test.Sdk` alone is not decisive; it can remain for compatibility
in an MTP-enabled project. When an explicit override decides the result, name
the final override and its source, not the superseded default.
When a runner-selection property competes with `Microsoft.NET.Test.Sdk`, say
that the runner property selects MTP and `Microsoft.NET.Test.Sdk` does **not**
select or imply VSTest. It may remain as compatibility support, but that is
secondary. `TestingPlatformDotnetTestSupport=true` is a bridge prerequisite, not
the runner-selection signal; never say that this property alone enables the
bridge. For a request asking which single signal decides, stop there. Omit
bridge or host-shape prerequisites when the configuration is complete, and add
them only when needed to explain why the selected runner cannot execute.
Likewise, when an enabled runner is overridden or unreachable, state both axes
causally: the runner property makes the project MTP-capable, but the final
`UseVSTest`/bridge/output setting determines which platform actually executes.
Name the runner selector before using a package reference only to identify the
framework.

Use causal evidence, not a bag of signals. For example:

```text
Platform: MTP
Framework: NUnit

Directory.Build.props supplies final EnableNUnitRunner=true and
TestingPlatformDotnetTestSupport=true, so NUnit executes on MTP.
```

For an incompatible configuration, give one minimal alignment choice after the
verdict without modifying files: either select the project's configured
platform globally or remove the project opt-out to use the globally selected
platform.

### Conditional and per-target-framework properties

Evaluate runner and bridge properties for each target framework. If conditions
produce different executed platforms, report each target explicitly (for
example, `net8.0: VSTest`, `net9.0: MTP`) rather than collapsing the project to
one global platform.
