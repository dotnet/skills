---
name: create-skill-test
description: Scaffolds eval.yaml evaluation specs for skills, custom agents, and redistributable gh-aw workflow packages in the dotnet/skills repository. Use when creating skill or workflow-package tests, writing evaluation stimuli, defining graders and rubrics, sizing an eval for statistical power, or setting up test fixture files. Handles the Vally eval.yaml schema, fixture organization, and overfitting avoidance. Do not use for running or debugging existing evals (use improve-skill-quality) nor for skills authoring (use create-skill).
---

# Create Skill Test

Scaffold an evaluation spec (`eval.yaml`) for a skill, agent, or workflow package so it conforms to the Vally schema,
passes `skill-validator check` and `check_eval_quality.py`, is powerful enough to return a verdict,
and does not overfit to the skill's own wording.

## When to Use

- Creating a new `eval.yaml` for a skill, agent, or workflow package
- Adding stimuli to an existing eval
- Sizing an eval so the pass gate can actually be reached
- Setting up or repairing fixture files alongside an eval
- Reviewing whether rubric items and graders risk overfitting

## When Not to Use

- Diagnosing a failing or regressed eval — use `improve-skill-quality`
- Modifying the skill-validator or the evaluation workflows
- Creating or editing `SKILL.md` files — use `create-skill`

## Inputs

| Input | Required | Description |
|-------|----------|-------------|
| Skill or agent name | Yes | Must exist under `plugins/<plugin>/skills/` or `plugins/<plugin>/agents/` |
| Plugin name | Yes | e.g. `dotnet-msbuild` |
| Skill content | Yes | Read it — you cannot write non-overfitted rubric items without it |
| Scenario hypothesis | Yes | State the expected improvement for a preference case, or the invariant protected by a guard |
| Failure modes to discriminate | Recommended | Each becomes one distinct stimulus |

## Workflow

### Step 1: Prove the eval belongs to the target

Before writing YAML, state what the stimulus proves. A preference stimulus is necessary when the
target should improve the answer, action, restraint, or validation result compared with the same
model without the target. A non-voting activation contract or no-op guard is necessary when it
protects a meaningful invariant, even if correct behavior is baseline-equivalent.

Do not add a stimulus when it measures:

- generic knowledge the base model already has;
- path recall for a skill that only points to reference files;
- output volume rather than correctness;
- a renamed or lightly reworded copy of an existing case; or
- a `disable-model-invocation: true` reference skill in isolation.

Map each proposed case to **capability**, **risk**, and **customer journey** tags. Preference cases
must add distinct voting value. Activation contracts and no-op guards may share a capability when
they protect a separate routing or preservation invariant. If two cases have the same inputs,
expected outcome, failure mode, and grader path, keep the stronger one. The five-stimulus floor
never justifies padding.

Then locate the target and test directory:

```text
tests/<plugin>/<skill-name>/eval.yaml          # skills
tests/<plugin>/agent.<agent-name>/eval.yaml    # agents (the agent. prefix disambiguates)
tests/agentic-workflows/<package>/eval.yaml   # redistributable gh-aw packages
```

Verify the target exists at `plugins/<plugin>/skills/<skill-name>/SKILL.md` or
`plugins/<plugin>/agents/<agent-name>.agent.md`, and read it.

**Agent evals use the native SDK agent lane.** Vally 0.14 cannot register custom
agents, so `agent.*` specs do not run through the skill experiment. The
evaluation workflow discovers them separately, runs the target agent through
`skill-validator evaluate`, and adapts that evidence into the same
schema-versioned result and dashboard pipeline. The distinct-stimulus floor
applies to both skill and agent evals.

**Be careful with a skill that sets `disable-model-invocation: true`.** The model cannot invoke it,
so the skill is absent from the model-facing skilled arm and any direct eval compares two identical
arms. Answer-content graders do not create a difference between those arms. The honest coverage for
such skills is dependency-level — through the outcome evals of the skills that load them, and through
the plugin arm. For example, `filter-syntax` is covered by the filtered-command scenarios in
`tests/dotnet-test/run-tests/eval.yaml`.

### Step 2: Write the spec skeleton

The spec is Vally format. Every eval in this repo uses `stimuli:` and `graders:`; `scenarios:` and
`assertions:` are a pre-Vally format that no longer loads.

```yaml
name: <skill-name>
description: Evaluates the <plugin>/<skill-name> skill
type: capability
defaults:
  timeout: 5m
  runs: 1
stimuli:
  - name: <what the agent must accomplish>
    prompt: <natural developer request>
    tags:
      capability: <distinct-capability>
      risk: <failure-being-prevented>
      journey: <customer-task>
    environment:
      files:
        - src: fixtures/<case>/Project.csproj
          dest: Project.csproj
    graders:
      - type: output-matches
        config:
          pattern: (root cause|underlying issue)
      - type: exit-success
      - type: prompt
    rubric:
      - <outcome the agent should have reached>
```

> **Use `defaults:` only.** `config:` is a deprecated alias that the repository gate rejects. Vally
> warns when the alias appears alone and throws when a spec declares both keys. Replace `config:`
> with one `defaults:` block and preserve its settings.

### Step 3: Size the eval for power before writing content

The gate gives each distinct stimulus one vote. Repeated runs for one stimulus collapse to one
majority-direction vote and remain available as reliability evidence.

1. **Distinct stimuli ≥ 5**, else the verdict is `underpowered` — never a pass, never a regression.
2. **p ≤ 0.05 on an exact one-sided sign test over *discordant* (non-tie) stimulus votes.** Ties are not
   discarded; they hold the discordant count down.

| discordant stimulus votes | records that pass | p |
|---:|---|---:|
| ≤ 4 | none | ≥ 0.0625 |
| 5–7 | zero losses only (5W/0L) | 0.031 |
| 8 | one loss survivable (7W/1L) | 0.035 |

At exactly 5 stimuli, one tie is fatal because it leaves 4 discordant votes. At 6 stimuli one tie
is survivable; at 7, up to two are. A loss is not. Five is an **eligibility floor**, not adequate
power. For example, 80% power needs 8 discordant votes only for a true 90% conditional win rate;
it needs 18 at 80%, 37 at 70%, and 158 at 60%. Size for the effect and tie rate you need to detect.

Use `runs` for reliability, not task breadth. Vally recommends 3 runs in CI and 5–10 nightly for
pass rate, pass@k, pass^k, and flakiness. Extra runs never clear the five-stimulus floor.

Do not set `runs` in `dotnet-skills.experiment.yaml`; experiment overrides overwrite every eval's
own value rather than defaulting it.

### Step 4: Write stimuli

- **Name** describes *what* is tested, not *how*.
- **Prompt** is a natural developer request. Never mention the skill, the agent, or its vocabulary —
  cued prompts inflate the overfit score and bias the baseline.
- Each stimulus should discriminate a **different** property of the skill. Five stimuli covering one
  property give arithmetic, not evidence.
- Give every capability stimulus non-empty `capability`, `risk`, and `journey` tags. Use stable
  lowercase kebab-case values. A useful portfolio crosses distinct rows or columns in that matrix;
  it does not repeat one journey with cosmetic wording changes.
- Give every stimulus a stable, unique `name`. Vally pairs comparison trajectories by
  `(stimulus name, trial index)`; duplicate names make slot identity ambiguous.
- Include a boundary / no-op stimulus for any skill that migrates or rewrites code, proving it
  leaves already-correct input alone.
- Add a dormancy stimulus for each real routing boundary. No-op proves restraint on an on-target,
  already-correct input; dormancy proves the target stays inactive on an off-target request.

### Step 5: Configure the environment

```yaml
environment:
  files:
    - src: fixtures/broken-build/App.csproj      # path relative to eval.yaml
      dest: App.csproj                           # path in the agent's working directory
    - src: fixtures/broken-build                 # a directory
      dest: .
  commands:
    - dotnet build -bl || exit 0                 # guard intentional failures
```

**Do not set `environment.skills` in a skill eval.** The experiment declares
`vary: /environment/skills` and supplies the value itself — `[]` for the baseline arm and
`plugins/<plugin>/skills/<skill>` for the skilled arm — so anything the eval declares is replaced,
in every arm. It cannot add a skill to one arm only. `environment.skills` is meaningful in an
`agent.*` eval; the native agent lane loads those entries only in the isolated
target run, while the plugin run loads the production plugin's complete skill
surface. Copy the shape from an existing agent eval such as
`tests/dotnet-test/agent.test-quality-auditor/eval.yaml` rather than reproducing a remembered form —
the specs in this repo are not consistent about how they spell those entries.

Fixture rules — each one has already cost a real result:

- **Every referenced fixture must be tracked by git.** `.gitignore` (e.g. `coverage*.xml`) has
  silently swallowed a committed fixture: the eval passed locally and failed at setup in CI. Verify
  with `git ls-files`, not by looking at the working tree.
- **Every fixture must behave as its stimulus assumes.** A fixture meant to be healthy must build; a
  fixture meant to be broken must fail for the exact reason the stimulus is about, and no other.
  Judges penalize agents for unrelated "pre-existing build issues" that the fixture author
  introduced.
- **Every fixture must reproduce the bug its stimulus is named for.** If it does not, the baseline
  scores well and the skill has nothing to add.
- **Coverage fixtures must be internally consistent.** A Cobertura report whose declared
  `line-rate`, summary totals (`lines-covered`/`lines-valid`), and `<line>` elements disagree lets
  the two arms read different truths, and the loss is the fixture's fault. Update any rubric item or
  prompt that quotes a figure in the same change.
- **Do not wire duplicate fixtures** to raise `n`; rename leftovers add trials without evidence.
- A setup command that is *expected* to fail while still producing its artifact must be guarded
  (`|| exit 0`), or vally drops the trial.
- A cleanup command that strips sources must skip directories containing `SKILL.md` — the staged
  skill lives there, and deleting it aborts only the skilled arm.
- **Preserve the complete file set.** When the prompt limits edits or requires source/test
  preservation, snapshot or compare every in-scope file, not one representative file. A grader that
  checks only the main output can miss deletion, truncation, or edits to sibling files.

### Step 6: Write graders

Graders are hard pass/fail checks evaluated on every arm.

| Type | Required config | Purpose |
|------|-----------------|---------|
| `output-matches` / `output-not-matches` | `pattern` | Regex over agent output |
| `output-contains` / `output-not-contains` | `substring` | Literal text in output |
| `file-exists` / `file-not-exists` | `path` | Glob against the work directory |
| `file-contains` / `file-not-contains` | `path`, `value` | Content of a produced file |
| `run-command` | `command` (plus optional `expected_exit_code`, `timeout`, `stdout_matches`) | Verify produced code actually builds/runs |
| `exit-success` | — | Agent produced non-empty output |
| `prompt` | — | Runs the LLM judge against the `rubric` |

Rules:

- A grader whose `config` is absent or missing its required key parses fine and **enforces nothing**.
  The usual cause is an indentation slip during an edit; `check_eval_quality.py` blocks it.
- Prefer broad patterns that several valid approaches satisfy:
  `(root cause|primary error|underlying issue)`.
- **If the skill mandates an output shape, assert on it.** A skill required to emit a decisive
  `Recommendation:` line can silently stop doing so while the eval still passes.
- Use `file-not-contains` / `file-not-exists` to prove the agent avoided an incorrect action.

Define the deterministic contract before writing the prompt grader:

1. **Golden acceptance:** materialize the fixture and apply the `golden_patch`, if any. The golden
   workspace and final `golden_trajectory` response must pass every deterministic grader that
   applies to them.
2. **Mutation rejection:** make one realistic defect that the eval exists to catch, such as a
   missing file, zero discovered tests, an out-of-scope edit, or a changed semantic value. The
   relevant deterministic grader must fail.
3. **Complete-state check:** cover all files and artifacts named by the request. Do not accept a
   partial artifact because one positive substring exists.

Use `golden_patch` for replayable workspace state and `golden_trajectory` for the expected final
response. A narrated edit, build, or test is not proof: completion claims need a patch or a
`run-command` grader that replays the evidence.

### Step 7: Write rubric items

Rubric items are judged pairwise (baseline vs. skilled). The overfitting judge classifies each item:

| Classification | Description | Goal |
|---------------|-------------|------|
| **outcome** | Whether the agent reached a correct result — WHAT, not HOW | Target this |
| **technique** | Whether the agent used a skill-specific procedure | Minimize |
| **vocabulary** | Whether the agent used the skill's terminology | Avoid |

1. Test outcomes, not methods: "Identified the root cause of the build failure", not "Replayed the
   binlog using `dotnet build /flp`".
2. Accept any valid approach.
3. Never reference the skill by name, and never reuse `SKILL.md` phrasing.
4. Never reward using the skill — the harness reports activation separately, so a rubric item that
   does this measures nothing and inflates the overfit score.
5. Do not test knowledge the model already has; it adds no delta.
6. Keep each item independently evaluable.
7. Do not reward raw volume (test count, report length); judges will compare it when both arms act.

**Good:**

```yaml
rubric:
  - Correctly identified the missing NuGet package as the root cause of the build failure
  - Recognized that downstream failures cascaded from that root cause
  - Suggested a concrete fix that resolves it
```

**Overfitted:**

```yaml
rubric:
  - Replayed the binary log using 'dotnet build /flp:v=diag'   # technique
  - Measured cold, warm, and no-op build scenarios             # vocabulary
  - Used the template-comparison skill                         # rewards activation
```

### Step 8: Add constraints sparingly

```yaml
constraints:
  expect_tools: [bash]
  reject_tools: [edit, create]
  reject_skills: [some-skill]
```

- `expect_tools: [bash]` on an **advisory** question forces a restore or build and converts an
  answer into a timeout with no quality benefit. Only require tools when the task genuinely needs
  them.
- `reject_tools` is the right way to keep a read-only stimulus read-only.

### Step 9: Add dormancy guards

A dormancy guard proves the skill stays dormant on an off-target request that superficially matches
it. Add one per real "when not to use" boundary: wrong input format, out-of-scope request,
incompatible project type, wrong framework version, prerequisite absent.

```yaml
  - name: Decline dump analysis request
    prompt: |
      I already have a .dmp crash dump from my .NET app. Can you help me
      analyze it to find the root cause of the crash?
    expect_activation: false
    graders:
      - type: output-matches
        config:
          pattern: (out of scope|not cover|does not|cannot|only.*collect)
      - type: prompt
    rubric:
      - Stated that dump analysis is out of scope
      - Did not open or analyze the dump file
      - Did not install analysis tools such as dotnet-dump analyze, lldb, or windbg
      - Suggested the correct alternative
```

> **Never combine `expect_activation: false` with `constraints.reject_skills`.** That forces the
> skilled arm to run skill-free, so the harness cannot observe whether the target skill hijacks the
> request. The comparison remains visible as report-only evidence but does not vote in preference;
> unexpected isolated activation blocks a pass. `expect_activation: false` **alone** is the repo
> convention.

### Workflow-package scenarios

For a package target, verify `agentic-workflows/<package>/aw.yml`, then read its
entry workflow, local imports, and bundled agents. The native SDK lane evaluates
their real prompt bodies and installed resources against offline fixtures.
Specify collector outputs, revision/tracking evidence, and service responses as
fixture inputs; propose terminal actions in `result.json` rather than pretending
to publish through live GitHub or safe-output tools. Assert the structured result
with deterministic graders. Do not place expected answers in agent-readable
fixtures or staged grader scripts; pass expected values through grader argv.

Prompt expressions are rendered from a flat `workflow-context.json` fixture,
whose keys are exact trimmed expressions and values are strings. Missing context
fails setup. A workflow that correctly chooses noop is still expected-active
decision evidence, not `expect_activation: false` routing evidence. Include
normal, partial, stale, incompatible, missing-evidence, and multi-module cases
where applicable. Keep compilation, helper execution, and actual consumer
publication tests separate: this lane is labeled `workflow-prompt-sdk`, not
end-to-end Actions execution.

```powershell
dotnet run --project eng/skill-validator/src/SkillValidator.csproj -- evaluate `
  agentic-workflows/<package>/aw.yml `
  --tests-dir tests/agentic-workflows --runs 1 --verdict-warn-only
```

Guard rubrics verify three things: **recognition** (why it does not apply), **restraint** (no
workflow, no file changes, no installs), **redirection** (the correct next step).

### Step 10: Validate

```bash
dotnet run --project eng/skill-validator/src/SkillValidator.csproj -- check --plugin ./plugins/<plugin>
python eng/eval-quality/check_eval_quality.py
./eng/run-skill-evals.sh <plugin> <skill-name>
```

For an **agent** eval, exercise the native lane directly:

```bash
dotnet run --project eng/skill-validator/src/SkillValidator.csproj -- evaluate \
  plugins/<plugin>/agents/<agent>.agent.md \
  --tests-dir tests/<plugin> \
  --runs 1 \
  --verdict-warn-only
```

CI adapts this result through `eng/vally-adapter/adapt-agent-results.mjs`,
which applies the same distinct-stimulus sign-test policy used by skill results.

Validation must cover four layers:

1. **Deterministic structure:** run `check_eval_quality.py` and the relevant checker self-tests when
   the checker changes.
2. **Production parsing and golden replay:** run skill evals through the repository's Vally entry
   point. For agent evals, run `skill-validator evaluate` to prove the native SDK lane accepts the
   executable scenario fields. That parser does not read `golden_trajectory` or `golden_patch`, so
   validate the references separately: run `check_eval_quality.py`, then materialize the fixture,
   apply the golden patch, and run every applicable deterministic file and command grader against
   the golden workspace. Confirm the final golden response passes its output graders.
3. **Normal execution:** use the normal worker concurrency and the declared `defaults.timeout`.
   Do not certify an eval only with one worker or a larger ad hoc time budget. If normal concurrency
   exposes a race or timeout, classify it as reliability evidence.
4. **Cross-family sensitivity:** for broad routing or behavior changes, evaluate at least one GPT
   family and one Claude family executor. Report each result separately. Different executor or
   judge families do not increase the independent stimulus count.

`check_eval_quality.py` blocks 22 structural defect classes that can corrupt a result:
missing or untracked fixtures, self-contradicting coverage fixtures, empty grader configs, dormancy
guards with `reject_skills`, sub-floor stimulus counts, duplicate YAML keys or stimulus names, and
invalid defaults, tags, golden evidence, test commands, or ATIF trajectories. See
[`eng/eval-quality/README.md`](../../../eng/eval-quality/README.md) for the complete list. Do not add a new eval to
`eng/eval-quality/underpowered-allowlist.txt` — the gate rejects
allowlist entries that are new relative to the base branch.

For the official run, submit a PR review containing `/evaluate` so it binds to the reviewed commit.

## Validation Checklist

- [ ] Directory is `tests/<plugin>/<skill-name>/` or `tests/<plugin>/agent.<agent-name>/`
- [ ] Spec uses `stimuli:` / `graders:` and the current `defaults:` settings block
- [ ] At least 5 preference-eligible distinct stimuli exist; dormancy contracts do not count toward this floor
- [ ] Every stimulus is necessary and fits the target; each preference case adds distinct voting value
- [ ] Each capability stimulus has stable `capability`, `risk`, and `journey` tags and a unique name
- [ ] Prompts never name the skill, the agent, or its vocabulary
- [ ] Every referenced fixture exists and is tracked by `git ls-files`
- [ ] Every fixture behaves as its stimulus assumes — healthy ones build, deliberately broken ones fail only for the stated reason
- [ ] Preservation and scope graders cover the complete in-scope file set
- [ ] Every grader has its required `config` key
- [ ] Any output shape the skill mandates has a grader
- [ ] Golden evidence passes deterministic graders, and a realistic mutation fails them
- [ ] Rubric items are outcome-shaped and never reward using the skill
- [ ] Rewrite skills have a no-op case; routing boundaries use `expect_activation: false` alone
- [ ] The production runner accepts the executable spec and completes under normal concurrency and time limits
- [ ] Golden references pass the standalone checker and deterministic replay
- [ ] Broad routing or behavior changes have separate GPT-family and Claude-family evidence
- [ ] `skill-validator check` and `check_eval_quality.py` pass

## Common Pitfalls

| Pitfall | Solution |
|---------|----------|
| Writing `scenarios:` / `assertions:` | That format no longer loads; use `stimuli:` / `graders:` |
| Using the deprecated top-level `config:` alias | Rename it to `defaults:` and preserve its settings |
| Landing an eval at exactly 5 stimuli | A single tie makes a pass unreachable; size for the effect and tie rate |
| Raising `runs` to clear the floor | Repeats measure reliability for one task; add stimuli |
| Prompt mentions the skill or agent by name | Rewrite as a natural developer request |
| Rubric rewards using the skill | Drop the item — the harness reports activation separately; rubrics measure outcomes |
| Fixture present but ignored by git | Verify with `git ls-files`; CI setup will fail otherwise |
| Fixture that does not build, or breaks for the wrong reason | Fix the fixture before blaming the skill |
| Dormancy guard with `reject_skills` | Use `expect_activation: false` alone |
| `expect_tools: [bash]` on an advisory question | Drop it; it causes timeouts, not quality |
| Timeout too short for code generation | Use ~360s; empty output fails every grader |
| Duplicate YAML key left behind by an edit | It overwrites the next stimulus field by field — delete the stray block |
| Duplicate stimulus names | Vally uses names as comparison identity — give every stimulus a stable, unique name |
| Direct eval for a `disable-model-invocation: true` skill | Remove it and cover the reference through consumer outcomes |
| Agent eval below the stimulus floor | The native agent adapter uses the same sign-test gate; add independent preference-eligible stimuli |
| Agent eval "run" with `./eng/run-skill-evals.sh` | That helper remains skill-only; use `skill-validator evaluate` |
| Agent eval missing `environment.skills` | Declare the skills the agent routes to, or it cannot invoke them |
| `environment.skills` set in a **skill** eval | The experiment varies that key and replaces it in every arm; the declaration does nothing |
