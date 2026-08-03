---
name: create-skill-test
description: Scaffolds eval.yaml evaluation specs for agent skills in the dotnet/skills repository. Use when creating skill tests, writing evaluation stimuli, defining graders and rubrics, sizing an eval for statistical power, or setting up test fixture files. Handles the Vally eval.yaml schema, fixture organization, and overfitting avoidance. Do not use for running or debugging existing evals (use improve-skill-quality) nor for skills authoring (use create-skill).
---

# Create Skill Test

Scaffold an evaluation spec (`eval.yaml`) for a skill or agent so it conforms to the Vally schema,
passes `skill-validator check` and `check_eval_quality.py`, is powerful enough to return a verdict,
and does not overfit to the skill's own wording.

## When to Use

- Creating a new `eval.yaml` for a skill or agent
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
| Failure modes to discriminate | Recommended | Each becomes one stimulus |

## Workflow

### Step 1: Locate the target and the test directory

```text
tests/<plugin>/<skill-name>/eval.yaml          # skills
tests/<plugin>/agent.<agent-name>/eval.yaml    # agents (the agent. prefix disambiguates)
```

Verify the target exists at `plugins/<plugin>/skills/<skill-name>/SKILL.md` or
`plugins/<plugin>/agents/<agent-name>.agent.md`, and read it.

**Agent evals sit outside the verdict flow.** The canonical experiment declares
`evals: tests/*/!(agent.*)/eval.yaml`, so `agent.*` specs are excluded: no verdict is ever computed
for them, the trial floor does not apply, and `./eng/run-skill-evals.sh` drops them even when you
name one explicitly (its `--eval-filter` is intersected with that glob). Everything below about
sizing for statistical power therefore applies to **skill** evals. Author agent evals for the
scenario coverage and the deterministic graders, and run them as described in Step 10.

**Be careful with a skill that sets `disable-model-invocation: true`.** The model cannot invoke it,
so any eval graded on the skill self-activating compares two identical arms and returns judge noise.
The honest coverage for such skills is dependency-level — through the evals of the skills that load
them, and through the plugin arm. Two here take the other route and grade the *answer* rather than
activation: `tests/dotnet-test/filter-syntax/eval.yaml` and
`tests/dotnet-test/platform-detection/eval.yaml`. Whether that produces a measurable gap for a skill
the model cannot invoke is still unconfirmed, so read a real verdict before copying the pattern.

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

> **`defaults:` replaces `config:` — it does not join it.** `config` is a deprecated alias for the
> same block and vally **throws** on a spec declaring both. Most existing evals here still open with
> `config:`; when you add `runs`, merge the two into one `defaults:` block carrying `timeout` and
> `runs`. The failure is invisible otherwise: the job exits 0 with no verdicts and the PR comment
> blames "transient infrastructure".

### Step 3: Size the eval for power before writing content

`trials = stimuli × runs`, and the gate has two independent bars:

1. **Counted trials ≥ 5**, else the verdict is `underpowered` — never a pass, never a regression.
2. **p ≤ 0.05 on an exact one-sided sign test over the *discordant* (non-tie) trials.** Ties are not
   discarded; they hold the discordant count down.

| discordant trials | records that pass | p |
|---:|---|---:|
| ≤ 4 | none | ≥ 0.0625 |
| 5–7 | zero losses only (5W/0L) | 0.031 |
| 8 | one loss survivable (7W/1L) | 0.035 |

At exactly 5 counted trials a single tie is fatal — it leaves 4 discordant. At 6 counted trials one
tie is survivable (5W/1T/0L); at 7, up to two are (5W/2T/0L). A loss is not. Five is an
**eligibility floor**, not adequate power — one tie at five trials makes a pass
arithmetically unreachable. A run measuring a 32% tie rate certified a genuinely-helping five-trial
eval roughly one time in ten; at fifteen trials, nine times in ten.

Prefer **more stimuli** over more `runs`: repeats measure the same task. Raise `runs` only when a
stimulus is genuinely expensive to add (full build/test pipelines), and write the reasoning in a
comment above `defaults:`.

Do not set `runs` in `dotnet-skills.experiment.yaml`; experiment overrides overwrite every eval's
own value rather than defaulting it.

### Step 4: Write stimuli

- **Name** describes *what* is tested, not *how*.
- **Prompt** is a natural developer request. Never mention the skill, the agent, or its vocabulary —
  cued prompts inflate the overfit score and bias the baseline.
- Each stimulus should discriminate a **different** property of the skill. Five stimuli covering one
  property give arithmetic, not evidence.
- Include a boundary / no-op stimulus for any skill that migrates or rewrites code, proving it
  leaves already-correct input alone.

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
  skills:
    - binlog-failure-analysis                    # extra skills loaded in the isolated arm only
```

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
> skilled arm to run skill-free, making it identical to the baseline; the score is then pure judge
> noise. Across four evals the same guard scored −0.4, +0.4, +0.4 and 0, and twice cost a skill its
> pass. `expect_activation: false` **alone** is the repo convention.

Guard rubrics verify three things: **recognition** (why it does not apply), **restraint** (no
workflow, no file changes, no installs), **redirection** (the correct next step).

### Step 10: Validate

```bash
dotnet run --project eng/skill-validator/src/SkillValidator.csproj -- check --plugin ./plugins/<plugin>
python eng/eval-quality/check_eval_quality.py
./eng/run-skill-evals.sh <plugin> <skill-name>
```

For an **agent** eval, the third command is a no-op: `agent.*` is outside the experiment's `evals:`
glob. Exercise one by pointing the runner at an experiment file whose glob includes it:

```bash
# copy dotnet-skills.experiment.yaml, widen its evals: glob to tests/*/agent.*/eval.yaml
EXPERIMENT_FILE=my-agent.experiment.yaml ./eng/run-skill-evals.sh <plugin>
```

Read the trajectories rather than the verdict — there is no sign-test result for an agent eval.

`check_eval_quality.py` blocks ten structural defect classes that each already cost a real result:
missing or untracked fixtures, self-contradicting coverage fixtures, empty grader configs, dormancy
guards with `reject_skills`, sub-floor trial counts, duplicate YAML keys, and `config:`/`defaults:`
collisions. Do not add a new eval to `eng/eval-quality/underpowered-allowlist.txt` — the gate rejects
allowlist entries that are new relative to the base branch.

For the official run, submit a PR review containing `/evaluate` so it binds to the reviewed commit.

## Validation Checklist

- [ ] Directory is `tests/<plugin>/<skill-name>/` or `tests/<plugin>/agent.<agent-name>/`
- [ ] Spec uses `stimuli:` / `graders:`, and exactly one of `defaults:` or `config:`
- [ ] For a skill eval, `stimuli × runs` clears 5 with room for the expected tie rate (agent evals are exempt — they get no verdict)
- [ ] Each stimulus discriminates a different property
- [ ] Prompts never name the skill, the agent, or its vocabulary
- [ ] Every referenced fixture exists and is tracked by `git ls-files`
- [ ] Every fixture behaves as its stimulus assumes — healthy ones build, deliberately broken ones fail only for the stated reason
- [ ] Every grader has its required `config` key
- [ ] Any output shape the skill mandates has a grader
- [ ] Rubric items are outcome-shaped and never reward using the skill
- [ ] Dormancy guards use `expect_activation: false` alone
- [ ] `skill-validator check` and `check_eval_quality.py` pass

## Common Pitfalls

| Pitfall | Solution |
|---------|----------|
| Writing `scenarios:` / `assertions:` | That format no longer loads; use `stimuli:` / `graders:` |
| Adding `defaults: runs:` beside an existing `config:` | Merge into one `defaults:` block |
| Landing an eval at exactly 5 trials | A single tie makes a pass unreachable; size for the tie rate |
| Raising `runs` instead of adding stimuli | Repeats measure one task and add no cross-task evidence |
| Prompt mentions the skill or agent by name | Rewrite as a natural developer request |
| Rubric rewards using the skill | Drop the item — the harness reports activation separately; rubrics measure outcomes |
| Fixture present but ignored by git | Verify with `git ls-files`; CI setup will fail otherwise |
| Fixture that does not build, or breaks for the wrong reason | Fix the fixture before blaming the skill |
| Dormancy guard with `reject_skills` | Use `expect_activation: false` alone |
| `expect_tools: [bash]` on an advisory question | Drop it; it causes timeouts, not quality |
| Timeout too short for code generation | Use ~360s; empty output fails every grader |
| Duplicate YAML key left behind by an edit | It overwrites the next stimulus field by field — delete the stray block |
| Direct activation-graded eval for a `disable-model-invocation: true` skill | Cover it through a consumer skill, or grade the answer content as `filter-syntax` does |
| Agent eval sized for the trial floor | `agent.*` evals get no verdict; size them for scenario coverage instead |
| Agent eval "run" with `./eng/run-skill-evals.sh` | The glob drops it — use a widened `EXPERIMENT_FILE` |
| Agent eval missing `environment.skills` | Declare the skills the agent routes to, or the isolated arm cannot use them |
