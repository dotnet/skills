# dotnet-test-migration

Skills and a GitHub Copilot orchestrator agent for migrating .NET test frameworks and platforms. Covers framework version upgrades (MSTest, xUnit), cross-framework conversion (xUnit/NUnit → MSTest), and test platform migration (VSTest → Microsoft.Testing.Platform).

## When to use this plugin

- **Upgrade MSTest** — MSTest v1/v2 → v3, then v3 → v4 (handles source and behavioral breaking changes)
- **Upgrade xUnit** — xUnit.net v2 → v3
- **Convert xUnit to MSTest** — port xUnit (v2 or v3) projects to MSTest v4
- **Convert NUnit to MSTest** — port NUnit (v3 or v4) projects to MSTest v4
- **Adopt Microsoft.Testing.Platform** — migrate from the VSTest runner to MTP
- **Orchestrate migrations** — auto-detect the current framework/version/platform and route to the right migration

## Skills

| Skill | Description |
|---|---|
| **migrate-mstest-v1v2-to-v3** | Upgrade MSTest v1 (assembly refs) or v2 (NuGet 1.x–2.x) to v3 |
| **migrate-mstest-v3-to-v4** | Upgrade MSTest v3 to v4 — handles all source and behavioral breaking changes |
| **migrate-xunit-to-xunit-v3** | Upgrade xUnit.net v2 to v3 |
| **migrate-xunit-to-mstest** | Convert xUnit.net (v2 or v3) test projects to MSTest v4 — attributes, assertions, fixtures, lifecycle, output, parallelization |
| **migrate-nunit-to-mstest** | Convert NUnit (v3 or v4) test projects to MSTest v4 — constraints, data sources, fixture lifecycle, namespace setup, output, parallelization |
| **migrate-vstest-to-mtp** | Migrate from the VSTest runner to Microsoft.Testing.Platform |

## Agents

The agent below is a GitHub Copilot `.agent.md` definition. Codex plugin installs expose the
migration skills, but not this agent or its static handoff.

| Agent | Purpose |
|---|---|
| **test-migration** | Auto-detects framework/version/platform and routes to the correct migration skill; coordinates multi-step migrations (e.g., MSTest v1 → v3 → v4) |

## Related plugins

- **dotnet-test** — running, generating, repairing, and analyzing tests;
  testability improvement; coverage; and the `test-engineer` handoff used after
  migrations. The migration skills here reference shared `dotnet-test` skills
  by name (for example `platform-detection`, `writing-mstest-tests`, and
  `run-tests`). Install `dotnet-test` alongside this plugin to get the full
  workflow.

## Prerequisites

- .NET SDK installed (`dotnet` on PATH)
- A project with an existing test framework (MSTest, xUnit, NUnit, or TUnit)

## Installed composition and runner compatibility

The complete supported path installs this plugin together with `dotnet-test`,
at an aligned repository revision or recorded plugin versions. The latter
provides detection, execution, MSTest authoring, and the optional
`test-engineer` handoff. Neither a single skill nor this plugin alone implies
those dependencies were installed. An unavailable dependency must be reported;
do not pretend a named skill or agent was invoked. Codex uses the migration
skills directly and does not install the Copilot agent or its static handoff.

Runner selection depends on both the SDK and the resolved MTP packages.
On SDK 10, native MTP needs MTP 1.7 or later. Historical MTP 1.x can use the
VSTest compatibility bridge; MTP 2.x rejects that bridge on SDK 10 and later.
Do not bypass the restriction, downgrade packages, or change an explicitly
requested mode silently. SDK 8/9 bridge compatibility is a separate case.
Classic projects additionally need their checked-in Windows/Visual Studio
toolchain; a modern SDK installation alone is not enough.

The executable fixtures pin SDK 10's 10.0.1xx feature band, record the resolved
patch, and distinguish MSTest 3.8.0 / MTP 1.6.0 bridge execution from MSTest
4.4.1 / MTP 2.4.1 native execution. The historical TRX reporter omits skip
reasons: unchanged skip metadata plus a skipped outcome is not proof that the
reason was exported. Native reporting checks the reason in the actual TRX.

## Migration evidence

Build success and equal totals do not establish migration parity. The focused
eval companions verify logical case identities (including data rows), outcomes
and skip reasons where exported, effective runner mode, fresh parsed reports,
shared-resource lifetime, and preserved production files. Plan-only and no-op
checks compare complete in-scope file sets, not just a package substring.
They reject symlinks and Windows junctions at preserved roots and descendants.
Opaque framework-generated test IDs can legitimately change; do not compare
those as if they were stable cross-framework identities.

Staged delivery must include self-contained `checkpoints/v3` and
`checkpoints/v4` outputs as well as the final native-MTP project. The evaluator
rebuilds and runs each checkpoint with its own runner and package requirements;
a final-only edit cannot satisfy the stage contract. This proves the delivered
checkpoints are valid, not an inferred chronology from the final response.
Platform-only solution migrations also verify both test projects' resolved
package versions, authenticate unchanged production/test files before and after
execution, and require the production library to retain its target framework
and enabled nullable analysis.
Both test projects must reference the supplied production library in the
project graph and compiler metadata. The evaluator rejects additional local
projects and checks that the copied runtime dependency matches that library's
verified output.
The delivered project, props/targets, SDK/runner selection, package/settings
configuration and supporting inputs are snapshotted before restore/build/test
and checked afterward, excluding build outputs and evaluator result artifacts.
Verified package-asset files are retained as explicit exceptions to the build-output
exclusion. Green TRX results cannot hide a rewritten final configuration or package resolution.

File hashes alone do not establish compiler participation. Executable migration
cases bind each project's target-time compile items and actual C# compiler
arguments to its declared source set, then execute the verified build without
rebuilding it.
Excluding an authenticated file or compiling an uncontracted replacement fails
even when reported case names and outcomes are unchanged.
These checks are scoped to the Debug/net8.0 fixtures and their known SDK/package
generated inputs, not a general-purpose MSBuild analyzer or sandbox.

Checkpoint replays copy each checkpoint alone outside the delivered workspace,
reject symlinks, Windows junctions and external compile/project/import references, and authenticate
production/test tokens after normalizing only the required framework rewrites.
Collection conversion checks similarly preserve the original test bodies and
run both full and one-member filters. Its lazy run-end cleanup example is
explicitly limited to an assembly containing that sole collection; the guard
rejects adding unrelated tests. It is not a generic multi-collection lifetime
mapping, and no cleanup may depend on every class being selected.
A deferred-member scheduling fallback must defer both members; a mixed policy
cannot support a group-wide claim about unrelated-class overlap.

Already-current native MTP fixtures cover both orchestrator no-op behavior and
the platform skill's dormancy boundary. Their guards preserve all user files and
staged verifier bytes and reject root additions, excluding only host state and generated
Python bytecode. Fixture-health execution is separate evidence, not a claim that
the inspected agent ran commands.

From the repository root:

```powershell
python -B tests\dotnet-test-migration\graders\test_migration_evidence.py -v
python -B tests\dotnet-test-migration\graders\replay.py tests\dotnet-test-migration\migrate-vstest-to-mtp\eval.yaml --name "Deliver native MTP migration with fresh reports from both projects" --results-dir <scratch-results>
python eng\eval-quality\check_eval_quality.py
dotnet run --project eng\skill-validator\src\SkillValidator.csproj -- check --plugin .\plugins\dotnet-test-migration
python -B tests\dotnet-test-migration\agent.test-migration\test_composition.py --cli <shipping-copilot-executable> --plugin-root <aligned-layout-plugins> --model <model-id> --case migration --results-dir <scratch-results>
```

Golden replay and deliberate defect rejection calibrate the evaluator; they
do not certify model behavior or the installed plugin composition. Run skill
comparisons through Vally and agent comparisons through the native SDK lane,
then exercise the actual combined plugin layout. Record content hashes,
host/SDK/package versions, commands, exit codes and artifacts. Keep timeouts,
missing prerequisites and blocked host execution visible. Compare total tokens,
wall time, retries, false findings and escaped defects per correctly completed
task, not raw test counts or fewer tool calls.

Offline replay honors each grader's declared timeout, using the production
fallbacks of 60 seconds for program graders and 30 seconds for command graders.
Grader subprocesses are isolated in a POSIX process group or a Windows job object.
Timeouts and interruptions terminate descendants before replay returns; ordinary
exit codes and failure output remain visible. The Windows bootstrap waits until
job assignment succeeds before launching grader code.
Invalid or expired budgets fail rather than silently extending a golden run.

The composition command accepts `plan`, `missing-dependency`, `migration`,
`repair`, `regression`, `rust`, `go`, and `denied-run` cases. It uses isolated host
configuration, fingerprints the actual plugin layout, retains events and
failed attempts, and replays the corresponding deterministic outcome graders.
The missing-dependency lane also requires the final response to name an unavailable
`dotnet-test` dependency; merely avoiding its invocation is insufficient.
Composition fingerprints retain source files inside cache directories and exclude
only generated Python bytecode, not the whole `__pycache__` tree.
Authentication and each case's real language toolchain are prerequisites.
It does not silently install them or increase the declared five-minute model
budget. Run separate executor families for behavior sensitivity; these smoke
cases are not a statistically powered preference comparison.

For `repair`, `regression`, `rust`, and `go`, `--explicit-consumer` requests the
installed entry skill by name to verify its packaged composition. This is a
separate, cued integration lane: it must not be reported as natural routing
success or used to replace a failed uncued activation result. The default
natural-request lane requires actual consumer invocation and retains failures.

`denied-run` deliberately denies shell execution and certifies only restraint:
an observed rejection, no equivalent-command/alternate-shell/delegation retry,
no file changes, and an explicit blocked report. Authorized cases do not count
as verified if host policy blocked them, even if tests eventually became green.
Do not relax the intentional-denial case to work around an allowlist failure.
The rejected completion must correlate to a started shell tool; an unrelated
file-access denial or an orphan completion cannot satisfy this execution gate.
