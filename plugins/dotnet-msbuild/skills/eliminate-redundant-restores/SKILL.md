---
name: eliminate-redundant-restores
description: "Remove repeated NuGet restores from multi-step .NET builds safely. USE FOR: build/test/publish steps that each restore, Directory.Build.rsp with -restore, safe restore suppression, NETSDK1004/NETSDK1047 after --no-restore. DO NOT USE FOR: slow first restore, only one restore, version or lock errors, unranked build slowness (use build-perf-baseline)."
license: MIT
---

# Eliminate Redundant Restores

A no-op restore still evaluates every project and checks restore state, so each extra restore in a multi-step build costs seconds to tens of seconds in a large repo. The cure is easy to get wrong: `--no-restore` on a step that no earlier restore actually covered turns a slow build into a failed one. This skill finds which invocations restore, proves which are already covered, and changes only those.

## When to Use

- A pipeline or script runs several `dotnet`/`msbuild` steps and the log shows restore output in more than one of them
- A restore step exists, yet later steps still print `Determining projects to restore...`
- A repository `Directory.Build.rsp` or the active MSBuild installation's `MSBuild.rsp` contains `-restore`
- Someone asks where `--no-restore` or `-restore:false` can safely be added
- `NETSDK1004` or `NETSDK1047` appeared after `--no-restore` was added

## When Not to Use

- The first restore is slow (feed latency, missing package cache): that is caching, not redundancy
- There is only one restore across all invocations: nothing to deduplicate
- Restore errors about versions, lock files or auditing: fix the restore itself
- Unranked "why is my build slow": start with `build-perf-baseline`, then `build-perf-diagnostics`

## Inputs

Discover repository inputs in the working directory; do not ask the user for paths.

- CI definitions (`*.yml`, `.github/workflows`, `azure-pipelines*.yml`, `Jenkinsfile`) and build scripts (`build.cmd`, `build.sh`, `build.ps1`, `eng/`)
- `Directory.Build.rsp`, `Directory.Build.props`/`.targets`, `NuGet.config`
- Auto-loaded `MSBuild.rsp` beside the active MSBuild executable, or response files passed explicitly with `@file`. A repository-local `MSBuild.rsp` is not auto-loaded
- Restore evidence if present: step logs or a binlog

## Workflow

### Step 1: Confirm restore is repeated and worth removing

- Log evidence: `Determining projects to restore...` or `All projects are up-to-date for restore.` once per invocation.
- Binlog evidence: restore evaluations carry the global properties `MSBuildRestoreSessionId` and `ExcludeRestorePackageImports=true`; the Target Performance Summary lists `Restore` with N calls.
- Quantify each no-op restore. If there is one restore, or the repeated ones total about a second, report "nothing worth removing" and stop without editing.

### Step 2: Inventory every invocation in execution order

| Invocation | Restores? |
|---|---|
| `dotnet build`, `test`, `publish`, `pack` | Implicitly, unless `--no-restore`; also check inherited rsp flags |
| `dotnet run` | Implicitly before building; `--no-restore` can still inherit rsp restore on older SDKs. With `--no-build`, no build or restore runs |
| `dotnet test`, `publish`, `pack` with `--no-build` | No implicit restore, but an rsp `-restore` can still enable restore; add `--no-restore -restore:false` |
| `dotnet msbuild`, `msbuild.exe`, CI MSBuild tasks | With `-restore` or `-t:Restore`, including flags inherited from repository `Directory.Build.rsp` or installation-level `MSBuild.rsp`, even when a step looks build-only |
| `dotnet restore`, `nuget restore`, `msbuild -t:Restore` | Explicit restore |
| CI build task with a "restore packages" option | Check its default |

Starting with .NET SDK 9.0.1xx, `--no-restore` forwards `-restore:false` to override rsp flags; older SDKs can still inherit `-restore` despite skipping the CLI's implicit restore. Whenever an rsp enables restore, use the version-independent `--no-restore -restore:false` with `dotnet build`, `test`, `publish` or `pack`, even with `--no-build`. `dotnet msbuild` and `msbuild.exe` take `-restore:false`, not `--no-restore`.

`dotnet run` does not expose the MSBuild `-restore:false` override. With an rsp on older SDKs, first build using `dotnet build --no-restore -restore:false`, then run using `dotnet run --no-build` with the same project, configuration, framework and runtime inputs (or execute the built DLL directly). Passing `-restore:false` to `dotnet run` is not a restore-suppression strategy.

### Step 3: Decide per invocation

Keep the first restore. Skip a later one only when all four checks pass:

| Check | If it fails |
|---|---|
| **Same project set.** A restore covers its entry project or solution and that project's ProjectReference closure. It does not cover a project in another solution or `.slnf`, a tool built from another path, or a project an orchestrating `.proj` builds through the `MSBuild` task (`-restore` on the orchestrator leaves those unrestored and they fail with NETSDK1004) | Keep restore on that step, or add the project to the explicit restore step |
| **Same restore inputs.** `-r`/`--runtime`, RID-producing `-a`/`--arch`, `--os`, `--ucr`/`--use-current-runtime`, `TargetFramework(s)`, `RuntimeIdentifier(s)`, `PublishAot`, package sources (`--source`, `--configfile`, `RestoreSources`), `RestorePackagesPath`, and any `-p:` value a PackageReference condition reads. Architecture/OS options combine with the SDK's current RID; `--use-current-runtime` selects it. Missing RID targets fail with NETSDK1047 (`Assets file ... doesn't have a target for 'net10.0/linux-x64'`) | Match the effective RID in the explicit restore (for example `publish --os linux --arch x64` requires `restore -r linux-x64`), or keep restore on the later step. A generic restore does not cover these aliases merely because there is no `-r`. Do not require `SelfContained=true` on restore solely because publish is self-contained: RID-only restore covers ordinary SDK projects. Match `Configuration` or `SelfContained` only if repository conditions change package inputs |
| **Same available restore state.** Earlier `project.assets.json` files must exist where the later invocation looks for them. Match `--artifacts-path`, `UseArtifactsOutput`/`ArtifactsPath`, and `MSBuildProjectExtensionsPath`/`BaseIntermediateOutputPath`; another job or workspace does not automatically share these files | Align the explicit restore's output layout with build/test, or retain restore in the later workspace. For `build --artifacts-path artifacts --no-restore`, use `restore --artifacts-path artifacts` first; ordinary `obj/` assets do not cover that layout |
| **Nothing changes packages in between.** A step that generates or edits a project, runs `dotnet add package`, rewrites `Directory.Packages.props` or `NuGet.config` invalidates the earlier restore | Move the explicit restore after that step |

### Step 4: Apply the smallest change

- One explicit restore carrying every restore input, then `--no-restore`, `-restore:false` or `--no-build` on later steps.
- Several `dotnet test` steps over one solution: build once, then `dotnet test --no-build` on each. Add `--no-restore -restore:false` if an rsp enables restore, including on .NET 8 and earlier. `--no-restore` alone still rebuilds.
- Keep self-contained publish settings. A `publish --no-build` still needs a prior build with matching configuration, framework, RID and self-contained output; restore compatibility does not prove that build outputs exist.
- Scope the change to CI and scripts. Leave `-restore` in `Directory.Build.rsp`, because developers rely on it; pass `-restore:false` per invocation instead.
- Never remove the first restore. If it is a `--locked-mode` gate, keep it there.
- Do not bundle static graph restore or package caching into this change.

```bash
# One restore carries every input later steps use
dotnet restore MyApp.sln -r linux-x64
# Explicit overrides also work when an rsp enables restore on older SDKs
dotnet build MyApp.sln -c Release --no-restore -restore:false
dotnet test MyApp.sln -c Release --no-build --no-restore -restore:false
dotnet publish src/Api -c Release -r linux-x64 --no-restore -restore:false
# msbuild invocations need the explicit switch if an rsp file adds -restore
dotnet msbuild Packaging.proj -t:Pack -restore:false
```

### Step 5: Validate and report truthfully

1. Run the changed pipeline or script end to end in a clean temporary copy (no `obj/`, no `project.assets.json`). A project that was never restored only fails there. YAML syntax or flag-presence checks do not validate restore coverage or test execution. If you cannot run it, say "not validated" and list the steps and projects to confirm.
2. Success evidence: every later step passed; `Restore` calls (binlog) or `Determining projects to restore` lines fell from N to the expected count; for a time claim, before/after medians of at least three runs.
3. Report a restore failure (NETSDK1004, NETSDK1047) separately from a build failure, and never claim success after either.

## Output

Explain which steps restored, which earlier restore covers them, what changed, and what remains unvalidated. State whether redundant restores were removed or no change was needed, keeping the required first restore. Use prose or a findings table as appropriate; no fixed table headings or verdict syntax are required.

## Common Pitfalls

| Pitfall | Solution |
|---|---|
| Adding `--no-restore` to every command | Apply the Step 3 checks per step |
| Missing that an rsp adds `-restore` to "build-only" msbuild steps | Search for `*.rsp` before concluding a step does not restore |
| Deleting `-restore` from `Directory.Build.rsp` | Override per CI invocation; the rsp serves developers |
| Orchestrator `.proj` restored, children not | Restore each child explicitly, or keep restore on the step that builds it |
| Publishing with `-r`, architecture/OS options, or `--use-current-runtime` after a generic restore | Restore with the same effective runtime identifier, including SDK-host defaults used by the aliases |
| A generator step edits PackageReferences after restore | Restore after the generator |
