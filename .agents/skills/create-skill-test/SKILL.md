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
for them, the stimulus floor does not apply, and `./eng/run-skill-evals.sh` drops them even when you
name one explicitly (its `--eval-filter` is intersected with that glob). The distinct-stimulus
floor therefore applies to **skill** evals only. Author agent evals for the
scenario coverage and the deterministic graders, and run them as described in Step 10.

**Be careful with a skill that sets `disable-model-invocation: true`.** The model cannot invoke it,
so any eval graded on the skill self-activating compares two identical arms and returns judge noise.
The honest coverage for such skills is dependency-level — through the evals of the skills that load
them, and through the plugin arm. Grading the answer rather than activation does not fix the
comparison: the hidden skill is still absent from the model-facing menu in both arms, so treatment
still equals control. The retired direct `filter-syntax` eval was a concrete example of this defect;
do not recreate that pattern.

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
- Give every stimulus a stable, unique `name`. Vally pairs comparison trajectories by
  `(stimulus name, trial index)`; duplicate names make slot identity ambiguous.
- Tag every capability stimulus with non-empty `capability`, `risk`, and `journey` values. Use
  stable lowercase kebab-case values so a failure identifies what behavior, consequence, and
  customer workflow need repair.
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
```

**Do not set `environment.skills` in a skill eval.** The experiment declares
`vary: /environment/skills` and supplies the value itself — `[]` for the baseline arm and
`plugins/<plugin>/skills/<skill>` for the skilled arm — so anything the eval declares is replaced,
in every arm. It cannot add a skill to one arm only. `environment.skills` is meaningful only in an
`agent.*` eval, which the experiment does not vary; there it is the set of skills the agent may
invoke. Copy the shape from an existing agent eval such as
`tests/dotnet-test/agent.test-quality-auditor/eval.yaml` rather than reproducing a remembered form —
the specs in this repo are not consistent about how they spell those entries.

Fixture rules — each one has already cost a real result:

- **Every referenced fixture must be tracked by git.** `.gitignore` (e.g. `coverage*.xml`) has
  silently swallowed a committed fixture: the eval passed locally and failed at setup in CI. Verify
  with `git ls-files`, not by looking at the working tree. A fixture directory must contain a
  tracked file; Git does not preserve empty directories, and a tracked symlink does not include an
  untracked target.
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
| `run-command` | `command` (plus optional `expected_exit_code`, `timeout`, `stdout_contains`, `stdout_matches`) | Verify produced code actually builds/runs |
| `exit-success` | — | Agent produced non-empty output |
| `prompt` | — | Runs the LLM judge against the `rubric` |

Rules:

- A grader whose `config` is absent or missing any required key parses fine and **enforces less than
  it declares**. File-content graders need both `path` and `value`. The usual cause is an indentation
  slip during an edit; `check_eval_quality.py` blocks it.
- Prefer broad patterns that several valid approaches satisfy:
  `(root cause|primary error|underlying issue)`.
- **If the skill mandates an output shape, assert on it.** A skill required to emit a decisive
  `Recommendation:` line can silently stop doing so while the eval still passes.
- Use `file-not-contains` / `file-not-exists` to prove the agent avoided an incorrect action.
- Output graders inspect final assistant prose, not edited files. Use file or command graders for
  artifact requirements.
- A golden patch supplies file state, not assistant prose. If a patch-backed stimulus uses an
  `output-*` grader, also provide a `golden_trajectory` for the reference response.
- Normal `vally eval` runs do not resolve `golden_patch` for prompt-grader evidence. For an
  implementation rubric, select `evidence: [trajectory, diff]` and verify the patch separately;
  selecting `golden_patch` only to silence the lint warning makes the live prompt grader fail.
- `output-not-matches` has no negation awareness: both "use X" and "do not use X" match `X`. Avoid
  broad vocabulary bans when a correct answer may name the rejected technique. Prefer final
  artifact state, a positive redirect, or a prompt rubric.
- A command grader must observe the agent's result, not create the artifact that a later grader
  checks. Preserve the build's exit code when piping its output through `grep`.
- A golden patch is needed only when the expected workspace differs from the starting fixture.
  Command-only checks and file assertions that already pass on the fixture do not need a patch.
- Keep command graders portable and low-fan-out. Prefer one ecosystem command or one cross-platform
  process over a shell pipeline. When arguments contain quoting or regular expressions, declare
  `command` as the executable and pass `args` as a YAML list so Vally does not invoke a shell.

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

### Step 8: Add and validate a reference

A reference is an oracle input for qualification and review, not decorative documentation. It must
represent one valid GREEN result. Every grader that can run against the reference must pass; prove
the remaining behavior graders with an equivalent manual GREEN state.

- Put a concise one-step curated response in `golden_trajectory.inline` so reviewers can read it
  beside the prompt and graders. Use a path for multi-step evidence or a substantive report over
  2,000 characters or 30 lines, so the reference does not hide the graders it calibrates.
- Vally permits fake trajectory events, so a tool observation alone does not prove that work ran.
  A workspace completion claim requires a golden patch, and an execution completion claim requires
  a `run-command` grader. Otherwise use advisory or expected-result voice. Preserve the real model
  and observations when a trajectory was captured from an actual run, but do not treat its label as
  proof.
- Use `golden_patch` for workspace changes. A trajectory does not replay file edits, so it cannot
  prove that generated code builds or behaves correctly.
- Keep golden trajectories valid ATIF. A step source can only be `system`, `user`, or `agent`.
  Record a tool invocation on the agent step with `tool_calls`, and put its result in that same
  step's `observation.results`; never create a standalone `source: tool` step.
- Use `golden_patch` when the reference must materialize file changes. Patch headers target the
  paths produced by `environment.files[].dest`, which may flatten the source fixture directory.
- Apply every golden patch with the same workspace layout and invocation that Vally uses. Confirm
  `git apply --check` succeeds, then run the behavior graders against the applied state.
- Scan each trajectory's final text against its output graders. A correct explanation that says
  "do not use X" must not fail a grader that bans the bare term `X`.
- If no honest executable reference is available, leave the reference absent and record the oracle
  debt. Missing evidence is safer than a narrated GREEN result written only to improve a score.
- Keep patches in LF form with valid unified-diff hunk counts and a prefix on every hunk line.

### Step 9: Add constraints sparingly

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

### Step 10: Add dormancy guards

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

### Step 11: Validate

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

`check_eval_quality.py` blocks twenty-two deterministic defect classes that can corrupt a result,
including missing references or slice tags, references that fail their output graders, missing or
untracked fixtures, invalid ATIF trajectories, self-contradicting coverage fixtures, empty grader
configs, dormancy guards with `reject_skills`, sub-floor stimulus counts, duplicate YAML keys or
stimulus names. Do not add a new eval to
`eng/eval-quality/underpowered-allowlist.txt` — the gate rejects
allowlist entries that are new relative to the base branch.

For the official run, submit a PR review containing `/evaluate` so it binds to the reviewed commit.

## Validation Checklist

- [ ] Directory is `tests/<plugin>/<skill-name>/` or `tests/<plugin>/agent.<agent-name>/`
- [ ] Spec uses `stimuli:` / `graders:` and a `defaults:` block
- [ ] For a skill eval, at least 5 distinct stimuli exist, with more for the effect and tie rate that must be detected (agent evals are exempt)
- [ ] Each stimulus discriminates a different property and has a stable, unique name
- [ ] Every capability stimulus declares capability, risk, and journey tags
- [ ] Prompts never name the skill, the agent, or its vocabulary
- [ ] Every referenced fixture exists and is tracked by `git ls-files`
- [ ] Every fixture behaves as its stimulus assumes — healthy ones build, deliberately broken ones fail only for the stated reason
- [ ] Every grader has its required `config` key
- [ ] Output graders inspect prose only; file and command graders verify produced artifacts
- [ ] Negative output patterns cannot collide with a correct negated explanation
- [ ] Any output shape the skill mandates has a grader
- [ ] Rubric items are outcome-shaped and never reward using the skill
- [ ] Every reference satisfies the graders that can run against it
- [ ] Every golden trajectory is valid ATIF; tool calls and observations are fields on an agent step
- [ ] One-step curated responses are inline; path references carry substantive multi-step evidence
- [ ] Curated responses do not claim unrepresented edits, builds, tests, installs, or commands
- [ ] Every golden patch applies to the exact materialized workspace and passes behavior checks
- [ ] Dormancy guards use `expect_activation: false` alone
- [ ] `skill-validator check` and `check_eval_quality.py` pass

## Common Pitfalls

| Pitfall | Solution |
|---------|----------|
| Writing `scenarios:` / `assertions:` | That format no longer loads; use `stimuli:` / `graders:` |
| Landing an eval at exactly 5 stimuli | A single tie makes a pass unreachable; size for the effect and tie rate |
| Raising `runs` to clear the floor | Repeats measure reliability for one task; add stimuli |
| Prompt mentions the skill or agent by name | Rewrite as a natural developer request |
| Golden trajectory uses `source: tool` | Put `tool_calls` and `observation.results` on the calling agent step |
| Rubric rewards using the skill | Drop the item — the harness reports activation separately; rubrics measure outcomes |
| Fixture present but ignored by git | Verify with `git ls-files`; CI setup will fail otherwise |
| Fixture that does not build, or breaks for the wrong reason | Fix the fixture before blaming the skill |
| `output-not-matches` bans a term used in a correct explanation | Check artifact state or require a positive redirect instead |
| Output grader checks code that the agent should edit | Use `file-contains`, `file-not-contains`, or `run-command` |
| Build output is piped to `grep`, hiding a failed build | Capture and require the build exit code before checking output |
| `dotnet test` checks only exit code 0 | Also assert positive test-run output; discovery can fail with zero tests and still exit 0 |
| Command grader depends on a shell pipeline or complex quoting | Use `command` plus `args` to invoke one cross-platform process directly; verify it under the normal worker count |
| Golden patch keeps the source fixture directory in its headers | Target the materialized `dest` paths and run `git apply --check` there |
| Golden trajectory narrates edits that command graders need | Use a golden patch or treat dynamic qualification as unavailable |
| `reject_skills: ["*"]` on a direct skill eval | Remove the wildcard; for dormancy use `expect_activation: false` alone |
| `expect_tools: [bash]` on an advisory question | Drop it; it causes timeouts, not quality |
| Timeout too short for code generation | Raise `defaults.timeout`; a stimulus-level `timeout` is ignored, and empty output fails every grader |
| Duplicate YAML key left behind by an edit | It overwrites the next stimulus field by field — delete the stray block |
| Duplicate stimulus names | Vally uses names as comparison identity — give every stimulus a stable, unique name |
| Direct eval for a `disable-model-invocation: true` skill | Cover it through an invocable consumer skill; answer-only grading still compares identical arms |
| Agent eval sized for the stimulus floor | `agent.*` evals get no verdict; size them for scenario coverage instead |
| Agent eval "run" with `./eng/run-skill-evals.sh` | The glob drops it — use a widened `EXPERIMENT_FILE` |
| Agent eval missing `environment.skills` | Declare the skills the agent routes to, or it cannot invoke them |
| `environment.skills` set in a **skill** eval | The experiment varies that key and replaces it in every arm; the declaration does nothing |
