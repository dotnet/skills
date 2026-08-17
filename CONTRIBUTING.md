# Contributing

Thanks for your interest in contributing. We expect to accept external contributions, but the bar for merging is intentionally high.

This repository contains shared building blocks for coding agents:

- Skills: reusable, task focused instruction packs
- Agents: role based configurations that bundle tool expectations and skill selection

Because these artifacts can affect many users and workflows, we prioritize correctness, clarity, and long term maintainability over speed.

## Code ownership

Every plugin, skill, and agent must have designated owners in the `.github/CODEOWNERS` file. When you add a new skill or agent, add a matching CODEOWNERS entry. Ownership must be either:

- **Two or more FTE GitHub aliases** (e.g., `@user1 @user2`), or
- **A GitHub team alias** (e.g., `@dotnet/my-team`)

This ensures that every contribution area has accountable reviewers and that PRs are automatically routed to the right people.

## Repository layout

```text
plugins/
  <plugin>/
    plugin.json
    skills/
      <skill-name>/
        SKILL.md
        scripts/
        references/
        assets/
    agents/
      <agent-name>.agent.md
tests/
  <plugin>/
    <skill-name>/
      eval.yaml
      <fixture files>
```

Every plugin must have a plugin.json file in the plugin root that is linked to from the marketplace.json file.

### Plugin organization

Skills are grouped into domain-specific plugins. When proposing a new skill, place it in the plugin that best matches its domain. See [README.md](README.md) for the current list of plugins.
If your skill does not fit any existing plugin, consider creating a new one.

To create a new plugin:

1. Add `plugins/<plugin-name>/plugin.json` and a `skills/` directory beneath it.
2. Add a matching entry in `.github/plugin/marketplace.json`, `.claude-plugin/marketplace.json`, `.cursor-plugin/marketplace.json`, and `.agents/plugins/marketplace.json`. Keep plugin entries consistent across all marketplace manifests (including `plugins[].source` format) to reduce drift and make future updates safer.
   Also add a `plugins/<plugin-name>/version.json` (copy an existing one) so the plugin participates in automated versioning. Start its `plugin.json` version at `0.1.0`.
3. Add a CODEOWNERS entry for the new plugin and its tests (see [Code ownership](#code-ownership)).
4. Add the plugin to the **What's Included** table in the root `README.md`.
5. Create a `tests/<plugin-name>/` directory for skill tests.

See existing plugins for the expected format.

### The `dotnet-experimental` plugin

Use `dotnet-experimental` when you want to try out a skill idea but are not yet confident it belongs in a stable plugin — for example, when the skill is outside your usual area of responsibility, the approach is unproven, or you want community feedback before committing to a long-term home.

Skills in `dotnet-experimental`:

- May change, be reworked, or be removed without notice.
- Are held to the same quality and testing standards as any other skill (frontmatter, `eval.yaml`, etc.).
- Should eventually graduate to a stable plugin or be retired. When a skill has proven itself, move it to the appropriate domain plugin and update tests accordingly.

Place experimental skills under `plugins/dotnet-experimental/skills/` with matching tests in `tests/dotnet-experimental/`.

## Plugin versioning

Each plugin is versioned independently. The same version is duplicated across every manifest a
consumer reads: `plugins/<plugin>/plugin.json` and `plugins/<plugin>/.codex-plugin/plugin.json`
(both present for every plugin), plus an optional `plugins/<plugin>/.claude-plugin/plugin.json`
that only plugins needing an inline Claude manifest carry (e.g. `dotnet-msbuild`'s binlog MCP
server). Consumers (Copilot CLI, Claude, Codex, Cursor) read the version directly from this
repository.

Versioning is automated with [Nerdbank.GitVersioning](https://github.com/dotnet/Nerdbank.GitVersioning).
A per-plugin `plugins/<plugin>/version.json` scopes the git height to that plugin's subtree, so the
**patch** number is derived from history — you do not edit it by hand. The generated manifests
(`plugin.json`, `.codex-plugin/plugin.json`, and `.claude-plugin/plugin.json` where present) and
`version.json` itself are excluded from that height via the `pathFilters`, so editing only manifest
metadata (anything other than a deliberate base bump in `version.json`) does **not** change the patch
number and is **not** picked up by `/version-bump` or the weekly sync. Touch a skill or other plugin
content to bump the version.

What this means when you contribute:

- **Every plugin carries its own `plugins/<plugin>/version.json`.** It declares the plugin's version
  base; the weekly sync fails fast if a plugin ships a `plugin.json` without one, so none is ever
  left unversioned.
- **Don't hand-edit the `version` field** in any of the manifests (`plugin.json`,
  `.codex-plugin/plugin.json`, or `.claude-plugin/plugin.json`). The patch number is computed and
  stamped automatically, and a manual edit will be overwritten.
- **The only version field you may change is the base** (`"version"`) in `plugins/<plugin>/version.json`,
  and only to declare a deliberate **minor or major** release of that plugin (e.g. `0.1` → `0.2` or `1.0`).
  Changing the base resets the patch number to `0`.
- After a PR changes a plugin's content, bumping its version is optional:
  - A maintainer can comment **`/version-bump`** on a same-repo PR to stamp the new version onto the branch.
  - Otherwise the **weekly version sync** opens a PR that stamps any plugin whose content changed without a
    version bump, explaining each change. Nothing is ever missed.

Patch numbers are predicted from git history, so two PRs bumped concurrently can land the same patch
number for a plugin; the weekly sync recomputes the authoritative height on `main` and reconciles any
collision. Version-only changes do not trigger skill evaluations.

## Before you start

- Search existing issues and pull requests to avoid duplicates.
- Start with an issue before you submit a pull request for a new skill, a new agent, or any non trivial change. This helps us align on scope and avoids wasted work.
- Small fixes like typos, broken links, or clearly isolated corrections can go straight to a pull request.
- Keep changes small and focused. One skill or one agent per pull request is a good default.

## What we look for

We are most likely to accept contributions that are:

- Addresses a LLM gap and is clearly motivated by a real use case
- Likely to be used frequently and is general (not repo-specific)
- Narrow in scope and easy to review
- Tool conscious and explicit about assumptions
- Verifiable with concrete validation steps
- Written to be durable across repo changes

We are less likely to accept contributions that:

- Add broad frameworks, meta tooling, or large reorganizations
- Duplicate guidance that already exists in another skill
- Encode private environment details, credentials, or company specific secrets
- Depend on proprietary tools or access that most contributors will not have
- Skills that make use of third party tools will be evaluated on a case by case basis. Acceptance of such skills will depend on our evaluation of the provenance and maturity of any such tools.

## Proposing a new skill

Please review the **What we look for** section and add justification for the skill in your issue and PR.

A skill should be self-contained and:

- Clearly state **what it does** and **when to use it**.
- Frontmatter (name and description) is small and minimal, just enough for LLM to understand when to use it
- Keep the SKILL.md body under 500 lines for optimal performance. Split content into separate files when you approach this limit. Use a progressive disclosure pattern, referring to those files from the SKILL.md file where needed.
- Specify required inputs (repo context, environment, access needs).
- Prefer concrete checklists and verification steps over vague guidance.

Create a new folder under a plugin's `skills/` directory:

```text
plugins/<plugin>/skills/<skill-name>/SKILL.md
```

A skill should answer three questions up front:

1. What outcome does the skill produce
2. When should an agent use it
3. How does the agent validate success

### Skill naming

Use short, kebab-case names that mirror how developers naturally phrase the task, prioritizing keyword overlap over grammar — e.g., add-aspnet-auth, configure-jwt-auth, setup-identity-server. Optionally using gerund style (verb-ing) is acceptable as well - e.g., configuring-caching.

Optimize for intent matching: lead with the action verb users actually say (add, configure, setup, deploy) followed the outcome the skill is aiming to assist.

The `SKILL.md` is required to have front-matter at a minimum:

Create the file with required YAML frontmatter:

```yaml
---
name: <skill-name>
description: <description of what the skill does, when to use it, and when not to use it>
---
```

> **Tip:** The `description` field is used by the agent runtime to decide whether to load the full skill.
> Include **when to use** and **when not to use** guidance directly in the description so the agent can
> select or skip skills without reading the entire `SKILL.md`. This avoids unnecessary token usage.
> See [`thread-abort-migration/SKILL.md`](plugins/dotnet-upgrade/skills/thread-abort-migration/SKILL.md) for a good example.

### Recommended `SKILL.md` sections

- **Purpose**: one paragraph describing the outcome.
- **When to use** / **When not to use** (put the essentials in the frontmatter `description`; expand here only if more detail is needed).
- **Inputs**: what the agent needs (files, commands, permissions).
- **Workflow**: numbered steps with checkpoints.
- **Validation**: how to confirm the result (tests, linters, manual checks).
- **Common pitfalls**: known traps and how to avoid them.

### Skill checklist

Include a `SKILL.md` that covers:

- Purpose and non goals
- When to use and when not to use (summarized in the frontmatter `description`; body section for extended detail)
- Inputs and prerequisites
- Step by step workflow with checkpoints
- Validation steps that can be run or observed
- Failure modes and recovery guidance

Also:

- Avoid duplicating text across multiple skills. Prefer referencing shared patterns.
- Do not include content copied from other repositories. If you are inspired by existing work, rewrite in your own words and adapt it to our conventions.

## Proposing a new agent

An agent definition should be opinionated but bounded:

- Describe the **role** (e.g., "WinForms Expert", "Security Reviewer", "Docs Maintainer").
- Define boundaries (what the agent should not do).
- List the skills it expects to use and how it chooses among them.

Add an agent file under a plugin's `agents/` directory:

```text
plugins/<plugin>/agents/<agent-name>.agent.md
```

### Agent checklist

Include documentation that explains:

- Role and intended tasks
- Boundaries and safety constraints
- Tooling assumptions
- How the agent chooses which skills to apply
- What a good completion looks like, including validation expectations

## Testing and validation

Skills and agents are documentation driven, but we still treat them as production assets.

- Every change should include a validation section that a reviewer can follow.
- If your change references commands, keep them cross platform when practical. If not, state the supported environment.
- If your change depends on external services, document how a reviewer can validate without privileged access, or explain why validation is not possible.

### Writing skill tests

Each skill should have an `eval.yaml` file that defines test scenarios. Tests live under the repo root `tests/` directory, matching the plugin and skill name:

```text
tests/<plugin>/<skill-name>/eval.yaml
```

The exception is a helper or reference skill that sets `disable-model-invocation: true`. The model
cannot self-activate it, so an activation-graded eval would compare two identical arms. Cover those
through the evals of the skills that load them and through the plugin arm instead.

The skeleton below shows the shape only — it declares a single trial and would therefore be rejected
by the quality gate. See [Size the eval so it can return a verdict](#size-the-eval-so-it-can-return-a-verdict) for the real bar.

```yaml
name: my-skill
description: Evaluates the <plugin>/<skill-name> skill
type: capability
defaults:
  timeout: 3m
  runs: 1
stimuli:
  - name: "Describe what the agent should do"
    prompt: |
      The prompt sent to the agent.
    graders:
      # Deterministic graders check the produced output/artifacts.
      - type: exit-success
      - type: output-contains
        config:
          substring: "expected text in agent output"
      # The `prompt` grader runs the LLM judge against the rubric below.
      - type: prompt
    rubric:
      - The agent correctly identified the issue
      - The agent suggested a concrete fix
```

> [!IMPORTANT]
> `defaults:` and `config:` are the same block — `config` is a deprecated alias — and vally
> **rejects** a spec declaring both. Many existing evals still open with `config:`; when you add
> `runs`, merge the two into a single `defaults:` block. The failure is silent: the job exits 0 with
> no verdicts and the PR comment blames "transient infrastructure".

Each skill is evaluated in up to three variants — **baseline** (no skills), **skilled** (only the skill under test), and **plugin** (the whole plugin loaded) — and a skill "passes" only when the skilled run is a *credible* improvement over baseline. To assert that a skill should stay dormant for an out-of-scope task, add `expect_activation: false` to that stimulus. See any existing `tests/*/*/eval.yaml` for a fuller example of the grader and stimulus format.

#### Size the eval so it can return a verdict

The pass gate has two independent bars. `trials = stimuli × runs`.

1. **Counted trials ≥ 5**, else the verdict is reported `underpowered` — never a pass, never a
   regression.
2. **p ≤ 0.05 on an exact one-sided sign test over the *discordant* (non-tie) trials.** Ties are not
   discarded; they hold the discordant count down.

| discordant trials | records that pass | p |
| ---: | --- | ---: |
| ≤ 4 | none, however good the skill | ≥ 0.0625 |
| 5–7 | zero losses only (5W/0L) | 0.031 |
| 8 | one loss survivable (7W/1L) | 0.035 |

At exactly 5 counted trials a single tie is fatal — it leaves 4 discordant. At 6 counted trials one
tie is survivable (5W/1T/0L); at 7, up to two are (5W/2T/0L). A loss is not. Five is an *eligibility
floor*, not adequate
power. A run that measured a 32% tie rate certified a
genuinely-helping five-trial eval about one time in ten; at fifteen trials, about nine times in ten.
Prefer adding **discriminating stimuli** over raising `runs` — repeats measure the same task. See
[`eng/eval-quality/README.md`](eng/eval-quality/README.md) for the full derivation and for the ten
structural defects the CI quality gate blocks.

Run the gate locally before pushing:

```bash
python eng/eval-quality/check_eval_quality.py
```

<!-- TODO: Vally is not yet public. Check with Aditya (Aditya Mandaleeka) on the
     canonical public location for Vally docs, then link the grader/stimulus
     reference and the CLI usage guide here instead of the local examples. -->

### Running tests locally

Prerequisites: Node.js 20+ and the [GitHub CLI](https://cli.github.com) signed in (`gh auth login`). The script checks these and tells you what's missing, so just run it:

```bash
# Run tests for a single skill
./eng/run-skill-evals.sh dotnet-msbuild binlog-failure-analysis

# Run tests for a whole plugin
./eng/run-skill-evals.sh dotnet-msbuild

# Run every skill's tests
./eng/run-skill-evals.sh
```

Per-skill verdicts are written to `./eval-results/<plugin>/<skill>/results.json`, and the raw experiment output goes to `./eval-results/_experiment/`. Model and judge model come from the `overrides:` block in `dotnet-skills.experiment.yaml`.

> [!WARNING]
> LLM evaluations are noisy. Runs-per-stimulus is deliberately **not** set in
> `dotnet-skills.experiment.yaml`: an experiment-level `runs` overwrites every eval's own value
> instead of defaulting it, making per-eval trial counts impossible to express. Raise the eval's own
> `defaults.runs` instead — or, better, add discriminating stimuli.

### CI evaluation

Tests do **not** run automatically on pull requests. When a PR changes skills, the `pr-status` job posts a pending commit status and a maintainer must trigger the evaluation, binding it to a specific reviewed commit — either by submitting a PR review ("Files changed" → "Review changes") whose body contains `/evaluate` (recommended, no SHA to copy), or by commenting `/evaluate <sha>`. A bare `/evaluate` comment only posts guidance. Results are posted as a PR comment and uploaded as build artifacts.

If a scenario fails or regresses, see [Investigating Results](eng/vally-adapter/InvestigatingResults.md) for how to download artifacts, interpret `results.json`, and diagnose common failure patterns.

## Writing style

- Be concise and specific.
- Prefer numbered steps for workflows.
- Prefer checklists for requirements.
- Define terminology the first time it appears.
- Avoid excessive formatting and avoid clever wording that could be misread by an agent.

## Security and safety

- Do not include secrets, tokens, or internal URLs.
- If you discover a security issue, do not open a public issue with sensitive details. Use the repository or organization security reporting process instead.

### External references

Skills often reference external tools, documentation, and projects — this is
expected and welcome, including community and third-party resources. To help
reviewers stay aware of external dependencies, the repository includes an
automated reference scanner (integrated into `skill-validator check`) that runs
in CI against plugin content (SKILL.md, agent files, and reference docs).

The scanner treats all of the following as CI-blocking errors:
- `http://` URLs where `https://` should be used
- `<script>` tags loading external resources without an `integrity` (SRI) attribute
- Pipe-to-shell patterns (`curl ... | bash`)
- URLs pointing to domains not listed in `eng/known-domains.txt`

Community tools and third-party projects are evaluated on a case-by-case basis
(see "What we look for" above). If your skill references a new external domain,
add it to `eng/known-domains.txt` in the same PR — the reviewer will
approve it alongside the skill content.

## Review process

Maintainers may request changes for:

- Clarity and unambiguous instructions
- Reduced scope
- More explicit validation
- Compatibility with multiple agent runtimes
- Consistency with existing conventions

We may close pull requests that are out of scope or too large to review. If that happens, we are happy to suggest a smaller path forward.

## Licensing and provenance

Only submit content that you have the right to contribute.

- Do not include copyrighted text from other projects.
- You may be asked to confirm that your contribution is original or appropriately licensed.

## Getting help

If you are unsure where a change belongs or how to structure a skill or agent, open an issue describing:

- The user problem
- The proposed outcome
- A small example of the desired behavior

If you're not sure whether something belongs under `skills/` or `agents/`, a good rule of thumb is:

- Put **reusable task playbooks** in `skills/`.
- Put **role + operating model** in `agents/`.

## Quality bar

Skills and agents in this repo should be:

- **Actionable**: the agent can follow them without guesswork.
- **Minimal**: no extra features or scope creep; focus on the task.
- **Verifiable**: always include a way to validate success.
- **Tool-conscious**: don't assume capabilities that might not exist in every runtime.

### What consistently separates a passing skill from a failing one

Every skill is scored head-to-head against the *same model with no skill loaded*, so the score is a
**delta**. The rules below are the ones this repo has learned the hard way, each from a merged fix.

**Content**

- Encode the decisions the model gets wrong; delete anything it already produces unaided. A skill
  that reads as reference prose ties its own baseline.
- Prefer "when A, do B, never C, verify D" tables over lists of plausible alternatives, and end with
  a concrete output contract (the exact command, the verdict line, the findings table).
- Scale output structure to input size. A twelve-section report for an eight-test suite loses to a
  concise direct answer.
- Add stop-conditions so a strong skill doesn't over-apply — then check you haven't over-corrected
  into answering more narrowly than the baseline did.
- Tell the agent to discover repo paths rather than listing them as required inputs; a "required"
  project path makes the agent ask the user for a file that is already in the working directory.
- Require truthful validation reporting. Claiming "Build succeeded" after a failed restore is an
  automatic loss.
- Verify load-bearing API claims by compiling or probing, not by reading source.
- Keep the common path in `SKILL.md` and gate rare or expensive paths behind `references/` reads.

**Activation**

- The `description` is the only text the runtime sees when choosing a skill. Put the user's own
  words in it: symptoms, error codes, artifact names, quoted requests.
- Partition against sibling skills on the real discriminator, not the shared topic, and add the
  matching exclusion to both siblings.
- Re-read every "do not use for" clause against the scenarios the skill exists to serve — an
  exclusion can lock out the skill's own purpose.
- Watch both budgets: 1,024 characters per description, and the plugin's rendered skill menu.

**When something fails**

- Classify before you rewrite. Broken fixtures, underpowered trial counts, forced tools, stale spec
  keys and harness errors have all masqueraded as skill regressions.
- Read the losing trial and the judge's stated reason, and drive the fix from that evidence rather
  than from style preference.
- A positive win/tie/loss record with a failing verdict is a power problem, not a content problem.
- A skill that is weak across model families, thinly used, and costing menu budget is a candidate
  for retirement, not indefinite polishing.

### Authoring skills for this repository

The repository ships agent skills for working on itself, under `.agents/skills/`:

| Skill | Use it when |
|-------|-------------|
| `create-skill` | Scaffolding a new skill and writing a description the runtime will route to |
| `create-skill-test` | Writing or resizing an `eval.yaml` |
| `improve-skill-quality` | An evaluation regressed, returned no verdict, or the skill didn't activate |
| `create-custom-agent` | Adding an agent definition |
| `authoring-github-workflows` | Editing anything under `.github/workflows/` |

## Skill-Validator & Evaluation workflow

Changes to `eng/skill-validator` or the `.github/workflows/evaluation*.yml` workflows must be made from a branch in the `dotnet/skills` repository (i.e., not from a fork). This is a security measure.
For pull requests from forks, the evaluation workflow (triggered via `/evaluate`) always uses the workflow YAML from the default branch of `dotnet/skills` and builds the validator from that default-branch checkout, so any changes to these files in the forked PR will be ignored during evaluation.
