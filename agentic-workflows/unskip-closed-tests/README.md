# Unskip Closed Tests

Installs a conservative agentic workflow that re-enables .NET tests only when
their source identity, GitHub tracking state, source revision, edit, and actual
test execution are all proven by trusted deterministic code.

The workflow was generalized from
[`microsoft/testfx`'s `unskip-closed-tests.md`](https://github.com/microsoft/testfx/blob/b6fd123ab550ccaa8522bc20efaad96f13eff283/.github/workflows/unskip-closed-tests.md)
at commit `b6fd123ab550ccaa8522bc20efaad96f13eff283`.
The original prompt-only search-and-edit flow is deliberately replaced by a
source-bound manifest, a read-only planner, and a custom safe-output job.

## Install

From the consuming repository:

```powershell
gh aw add-wizard dotnet/skills/agentic-workflows/unskip-closed-tests@main
```

Pin production installations to a release tag or commit SHA instead of
`main`. The package installs:

- `.github/workflows/unskip-closed-tests.md`
- `.github/workflows/unskip-closed-tests-prepare.md`
- `.github/workflows/unskip-closed-tests-shared.md`
- `.github/workflows/unskip-closed-tests.config.json`
- `.github/workflows/unskip-closed-tests-verify.ps1`
- `.github/workflows/unskip-closed-tests-tool/*`
- `.github/agents/unskip-closed-tests.agent.md`
- generated `.github/workflows/unskip-closed-tests.lock.yml`

After customizing the repository-owned verification hook, compile and commit
both the Markdown source and generated lock:

```powershell
gh aw compile unskip-closed-tests --strict
```

## Producer-consumer lifecycle

1. A deterministic collector checks out the exact scheduled/manual revision,
   parses configured C# roots with Roslyn, and inventories concrete `Ignore`
   attributes.
2. Every site is bound to repository-relative path, commit and Git blob
   identity, exact source span and digest, syntax-derived declaration owner,
   test FQN set, and canonical GitHub references.
3. The collector resolves each reference. Issues qualify only when closed with
   `state_reason: completed`; pull requests qualify only when merged.
   Inaccessible, unknown, malformed, open, or not-planned items fail closed.
4. If there are no eligible sites, the agent job is skipped and no pull request
   is opened.
5. A read-only agent may select or defer only manifest candidate IDs. It cannot
   edit files or supply source/remote identity.
6. A read-only custom safe-output job checks out the same commit, re-inventories
   source, re-resolves GitHub state, validates the exact proposal and source
   spans, applies surgical removals, and invokes the repository's trusted
   verification hook once per candidate. It re-verifies the exact final retained
   set until stable and uploads only raw request/TRX evidence plus the original
   agent output.
7. The fresh publication job reruns the trusted authorizer over that raw
   evidence after the consumer process boundary has ended. A candidate is
   retained only when every intended FQN actually executed and passed with no
   extra/mismatched, failed, skipped, or not-executed result.
8. The same fresh write-scoped publication job then validates the trusted
   authorization and deterministically reconstructs the exact Ignore removals
   without executing consumer code.
   Immediately before publication, it requires the default branch still points
   at the analyzed commit and no prior workflow PR is open. It opens at most one
   draft PR and reads it back to verify the trusted body marker.

Exit code alone never proves execution. Missing or malformed results, zero
selected tests, all-skipped results, or an intended/result FQN mismatch revert
that candidate.

## Source and identity contract

The manifest schema version is `"1"`. Its root records the repository, source
commit, Git object format, configuration digest, manifest digest, and
candidates.

Each candidate records:

- `candidate_id`: exact site identity, including the verified source site;
- `stable_owner_id`: line-movement-stable repository/path/declaration identity;
- `path`, `blob_oid`, `source_sha256`, exact `attribute_span`, and
  `attribute_text_sha256`;
- a syntax-derived owner containing the actual namespace/type ancestry,
  declaration identity, method signature, and affected test FQNs;
- canonical issue or pull-request references plus deterministic eligibility;
- an eligibility decision and explicit deferrals.

`stable_owner_id` never depends on line numbers or repeated attribute text.
`candidate_id` additionally binds one exact attribute occurrence. Repeated
text at distinct owners therefore cannot collapse into one history/dedup key.

Method owners come only from Roslyn syntax ancestors. Class-level ignores are
eligible only when every directly affected test is enumerated. Nested, partial,
inherited, duplicate, or otherwise incomplete class evidence is deferred.

## Consumer configuration and verification hook

Edit `.github/workflows/unskip-closed-tests.config.json` after installation:

- `source_roots`: repository-relative directories to scan;
- `excluded_globs`: fixture or otherwise forbidden paths;
- `generated_globs`: generated paths that must never become candidates;
- `ignore_attribute_names`: framework-qualified Ignore attribute type names;
- `test_attribute_names`: framework-qualified test method attribute type names;
- `attribute_aliases`: explicit trusted syntax-to-type mappings for unqualified
  forms such as `Ignore` and `TestMethod`;
- `verification.command`: a trusted argv array for the repository hook;
- `verification.timeout_seconds`: per-candidate hook timeout.

The installed command runs a PowerShell fail-closed placeholder that returns
nonzero, so all proposed candidates are reverted and no PR is opened until the
consumer replaces it.

For each candidate, the helper writes a version-1 request JSON and invokes the
configured argv exactly, appending the absolute request path as its final
argument. The request contains immutable
candidate/source identity and one entry per intended test:

```json
{
  "schema_version": "1",
  "repository": "owner/repo",
  "source_commit": "0123456789abcdef...",
  "candidate": {
    "candidate_id": "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"
  },
  "tests": [
    {
      "fqn": "Namespace.Type.TestMethod",
      "source_path": "tests/TypeTests.cs",
      "result_file": "/trusted/temp/results/test-1.trx"
    }
  ]
}
```

The hook owns repository-specific project discovery, setup, build, and test
commands. It must run every exact FQN and write TRX to each exact
`result_file`. VSTest can use `--logger trx`; Microsoft.Testing.Platform
consumers should enable its TRX report extension and select the requested FQN.
The helper, not the hook or agent, decides whether the evidence qualifies.

TestFX should customize the source roots/exclusions and provide a hook that
maps source/test FQNs to its projects, performs any package/bootstrap setup,
runs its chosen VSTest or MTP command, and writes the requested TRX files.

## Helper exit semantics

| Code | Meaning |
| --- | --- |
| `0` | Command succeeded; for `apply`, at least one verified edit remains. |
| `10` | Clean no-op; no verified candidate remains and the worktree is clean. |
| `20` | Stale or invalid source, manifest, proposal, GitHub evidence, path, symlink, generated input, or other contract violation. |
| `30` | Helper, infrastructure, hook, or result-protocol failure; no PR is authorized. |

## Offline decision evaluation

From a `dotnet/skills` checkout:

```powershell
dotnet run --project eng/skill-validator/src/SkillValidator.csproj -- evaluate `
  agentic-workflows/unskip-closed-tests/aw.yml `
  --tests-dir tests/agentic-workflows --runs 1 --verdict-warn-only
```

The eleven scenarios provide committed source-bound manifest/source snapshots,
trusted context, and simulated verification observations. The native prompt
lane loads the packaged prompt/import bodies and planner and stages all
manifest-declared runtime resources at installed `.github` paths. It grades
selection/deferral proposals in `result.json`, including completed issues,
merged PRs, ambiguous context, ownership/class boundaries, multiple modules,
stale/incompatible manifests, and zero-execution evidence.

`action: "patch"` is only a candidate-selection proposal: no source is edited
and no PR is authorized or published. This native evaluation is **not full
helper execution**. It does not run inventory/apply/authorize/materialize,
consumer hooks, or real TRX validation. Package helper tests remain separate
execution/protocol coverage. See
[`tests/agentic-workflows`](../../tests/agentic-workflows/README.md) for the
shared result contract and deterministic grader regression command. Results
are labelled `skillKind=workflow`, `evaluationLane=workflow-prompt-sdk`.
The lane is **not gh-aw Actions E2E**. Only main/imported Markdown bodies are
composed as prompts; frontmatter jobs/steps are not executed. Each case stages
flat expression context and trusted manifest/runtime-variable snapshots.

## Local package staging

`gh aw add` accepts local workflow files but not a local package directory with
all manifest resources. From a `dotnet/skills` checkout, stage the package into
a disposable consumer checkout deterministically:

```powershell
python eng/agentic-workflows/stage_agentic_workflow_package.py `
  agentic-workflows/unskip-closed-tests/aw.yml `
  C:\path\to\consumer

Set-Location C:\path\to\consumer
gh aw compile unskip-closed-tests --strict
```

The staging command applies the same include-to-destination mapping as CI
package validation and refuses absolute paths, traversal, symlinks, missing
files, and conflicting destinations.
