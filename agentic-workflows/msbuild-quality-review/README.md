# MSBuild Quality Review

Installs a reusable, advisory pull request reviewer for MSBuild authoring changes. The
workflow reviews changed project files and build infrastructure for correctness,
incremental-build behavior, item and property semantics, target ordering, NuGet build
extension authoring, maintainability, and credible performance regressions.

The workflow is read-only. It does not build pull request code, modify files, create
issues, or open pull requests. Each run emits at most one `COMMENT` review and reports at
most 10 findings. It uses `pull_request_target` so GitHub executes the workflow and
generated lock from the trusted base branch; pull request content is read only through
the restricted GitHub tools and is never checked out or executed.

## Install

From the root of the consuming repository:

```powershell
gh aw add-wizard dotnet/skills/agentic-workflows/msbuild-quality-review@main
```

Pin production installations to a release tag or commit SHA instead of `main`.

The package installs:

- `.github/workflows/msbuild-quality-review.md`
- `.github/workflows/msbuild-quality-review-shared.md`
- `.github/agents/msbuild-quality-reviewer.agent.md`
- the generated `.github/workflows/msbuild-quality-review.lock.yml`

## Configuration

The installer prompts for the optional
`MSBUILD_QUALITY_REVIEW_EXCLUDED_PATHS` repository variable. Use it to add
repository-specific fixture, generated, or intentionally invalid paths that should not be
reviewed. Separate patterns with newlines, commas, or semicolons:

```text
src/CompatibilityTests/InvalidProjects/**
eng/test-assets/**
samples/intentional-failures/**
```

The reviewer always excludes generated output and common fixture paths:

- `**/bin/**`, `**/obj/**`, `**/artifacts/**`
- `**/fixtures/**`, `**/testdata/**`, `**/test-data/**`
- `**/baselines/**`, `**/snapshots/**`
- `**/samples/broken/**`, `**/invalid/**`

The trigger covers changed `.csproj`, `.fsproj`, `.vbproj`, `.proj`, `.projitems`,
`.props`, `.targets`, `.tasks`, `.overridetasks`, `.nuspec`, `Directory.Build.*`, and
`Directory.Packages.*` files. The reviewer only reports findings supported by changed
lines or directly related changed-file evidence; it does not turn a PR review into a
repository-wide audit.

## Validation after customization

Run:

```powershell
gh aw compile msbuild-quality-review --strict --validate
```

Commit both the installed Markdown source and generated `.lock.yml`. Future package
updates can be pulled with `gh aw update msbuild-quality-review`; the updater uses a
three-way merge to preserve local changes. Run the consuming repository's actionlint
gate against the generated lock file before merging.

## Offline decision evaluation

From a `dotnet/skills` checkout:

```powershell
dotnet run --project eng/skill-validator/src/SkillValidator.csproj -- evaluate `
  agentic-workflows/msbuild-quality-review/aw.yml `
  --tests-dir tests/agentic-workflows --runs 1 --verdict-warn-only
```

The ten scenarios stage deterministic diffs, full source, related imports and
packaging maps, exclusions, and simulated current PR reads. The native prompt
lane loads the packaged prompt/import bodies and reviewer, stages runtime
resources at their installed `.github` paths, and grades one proposed review
or justified no-op in `result.json`. Coverage includes extension chains,
default items, generated output isolation, valid packed imports, F# source
order, scope/exclusions, missing evidence, and revision freshness.

This is read-only offline decision evidence: no PR code is executed and no
GitHub review is published. See
[`tests/agentic-workflows`](../../tests/agentic-workflows/README.md) for the
result contract and deterministic grader regression command. Results are
labelled `skillKind=workflow`, `evaluationLane=workflow-prompt-sdk`.
The lane is **not gh-aw Actions E2E**. Only main/imported Markdown bodies are
composed as prompts; frontmatter jobs/steps are not executed. Every case's
`workflow-context.json` supplies the trusted base SHA and exclusion expression
values as strings, separately from later simulated PR reads.

## Provenance

This package generalizes the MSBuild quality workflow, shared configuration, and reviewer
guidance from
[`microsoft/testfx@eb6aa01e`](https://github.com/microsoft/testfx/tree/eb6aa01e21637fa15f0000a1f041e263a6601108/.github)
for pull request review across repositories. The bundled reviewer also incorporates
portable guidance from the `dotnet-msbuild` target-authoring, incremental-build,
item-management, property-patterns, extension-points, and anti-pattern skills. It does
not depend on those skills being installed in the consuming repository.
