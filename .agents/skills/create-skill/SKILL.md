---
name: create-skill
description: Scaffolds new agent skills for the dotnet/skills repository. Use when creating a new skill, generating SKILL.md files, writing a skill description that the runtime will actually route to, or setting up skill directory structures. Handles frontmatter generation, section templates, and validation guidance. Do not use for fixing a skill that already fails its evaluation (use improve-skill-quality) or for writing eval.yaml (use create-skill-test).
---

# Create Skill

This skill helps you scaffold new agent skills that conform to the Agent Skills specification and the dotnet/skills repository conventions.

## When to Use

- Creating a new skill from scratch
- Generating a SKILL.md file with proper frontmatter
- Setting up the skill directory structure with optional folders
- Ensuring compliance with agentskills.io specification

## When Not to Use

- Modifying existing skills (edit directly instead)
- Diagnosing or fixing a skill that fails its evaluation (use `improve-skill-quality`)
- Writing the skill's `eval.yaml` (use `create-skill-test`)
- Creating custom agents (use the agents/ directory pattern)

## Inputs

| Input | Required | Description |
|-------|----------|-------------|
| Skill name | Yes | Lowercase, alphanumeric, hyphens only (e.g., `code-review`, `ci-triage`) |
| Description | Yes | What the skill does and when agents should use it (1-1024 chars) |
| Purpose | Yes | One paragraph describing the outcome |
| Workflow steps | Recommended | Numbered steps the agent should follow |

## Workflow

### Step 1: Validate the skill name

Ensure the name:
- Contains only lowercase letters, numbers, and hyphens
- Does not start or end with a hyphen
- Does not contain consecutive hyphens
- Is between 1-64 characters

### Step 2: Write the description — it is the router

The `description` is the **only** text the runtime sees when deciding whether to load the skill.
A perfect body behind a weak description never runs.

```yaml
---
name: <skill-name>
description: <what it does>. USE FOR: <symptoms, error codes, artifact names, quoted user requests>. DO NOT USE FOR: <nearby-but-wrong intents, with the skill that owns them>.
---
```

- Lead with an action verb and use the user's own words: symptoms, error codes (`CS1501`,
  `MSTEST0014`), artifact names (`.testsettings`, `binlog`), and requests phrased as a developer
  would type them.
- Partition against sibling skills on the **real discriminator**, not the topic. "Does the
  abstraction already exist?" separates two skills; "testing" does not. Add the matching exclusion
  to **both** siblings.
- Claim the ambiguous words that would otherwise route to a sibling. If prompts say "review my
  tests" and a sibling owns "review", say so explicitly.
- Check every `DO NOT USE FOR` clause against the scenarios the skill exists to serve — an
  exclusion like "already on v3" can lock out the post-upgrade fixes that are the skill's purpose.
- Budget: 1,024 characters per description, and the whole plugin's rendered skill menu is also
  budgeted. A helper skill users should never invoke directly can set
  `disable-model-invocation: true` to free menu space while staying invocable by name.

### Step 3: Write for delta over the baseline model

Every skill is scored head-to-head against the same model with **no skill loaded**. Content the
model already produces unaided is worth zero; content that makes it slower or more hedged is worth
less than zero. See
[improve-skill-quality/references/writing-for-baseline-delta.md](../improve-skill-quality/references/writing-for-baseline-delta.md)
for the full evidence.

| Do | Instead of |
|----|------------|
| Encode the decision the model would otherwise get wrong | Restating API signatures it already reproduces |
| "When A, do B, never C, verify D" tables | Lists of plausible alternatives |
| A concrete output contract (exact command, verdict line, findings table) | "Consider…", "you may want to…" |
| Scale output structure to input size | A 12-section dashboard for an 8-test suite |
| Stop-conditions that prevent over-applying | Acting before measuring, rewriting working code |
| Instructing the agent to discover repo paths | Marking discoverable paths as required inputs |
| Reporting restore/build/test failures truthfully | Claiming success after a failed command |
| Verifying load-bearing API claims by compiling or probing | Trusting a source read |
| Gating rare or expensive paths behind `references/` | One large SKILL.md carrying every path |

Do not over-correct: a skilled answer shorter and less actionable than the baseline's still loses.

### Step 4: Create the skill directory

```
plugins/<plugin>/skills/<skill-name>/
└── SKILL.md
```

### Step 5: Generate SKILL.md with frontmatter

Create the file with the frontmatter drafted in Step 2.

### Step 6: Add body content sections

Include these recommended sections:

1. **Purpose**: One paragraph describing the outcome
2. **When to Use**: Bullet list of appropriate scenarios
3. **When Not to Use**: Boundaries and exclusions
4. **Inputs**: Table of required and optional inputs
5. **Workflow**: Numbered steps with checkpoints
6. **Validation**: How to confirm the skill worked correctly
7. **Common Pitfalls**: Known traps and how to avoid them

### Step 7: Add optional directories (if needed)

```
plugins/<plugin>/skills/<skill-name>/
├── SKILL.md
├── scripts/       # Executable code agents can run
├── references/    # Additional documentation loaded on demand
└── assets/        # Templates, images, data files
```

Reference bundled files with paths relative to the directory that contains `SKILL.md`, such as
`references/details.md` or `scripts/validate.ps1`. Do not tell the agent to search for the skill's
installation directory. If a missing bundled file reduces the result quality, allow one
non-recursive listing of the specific directory that should contain it, such as `references/`,
`scripts/`, or `assets/`, and require the agent to report the reduced coverage.

### Step 8: Update CODEOWNERS

Add entries in `.github/CODEOWNERS` for the new skill and its test directory:

```
/plugins/<plugin>/skills/<skill-name>/  @owner-team
/tests/<plugin>/<skill-name>/           @owner-team
```

Match the owner pattern used by sibling skills in the same plugin.

### Step 9: Validate the skill

- Confirm frontmatter fields are valid
- Ensure SKILL.md is under 500 lines
- Check that file references use relative paths
- Verify instructions are actionable and specific
- Run `dotnet run --project eng/skill-validator/src/SkillValidator.csproj -- check --plugin ./plugins/<plugin>`

### Step 10: Add the eval

A skill without an `eval.yaml` has no evidence that it improves on the baseline. Use
`create-skill-test` to add one in the same pull request, and size it for statistical power — an eval
below five distinct stimuli can never return a passing verdict.

The exception is a helper skill with `disable-model-invocation: true`: the model cannot
self-activate it, so an activation-graded eval compares two identical arms. Cover it through the
evals of the skills that load it instead.

## SKILL.md Template

Use this template when creating a new skill:

```markdown
---
name: <skill-name>
description: <1-1024 char description of what the skill does and when to use it>
---

# <Skill Title>

<One paragraph describing the skill's purpose and outcome.>

## When to Use

- <Scenario 1>
- <Scenario 2>

## When Not to Use

- <Exclusion 1>
- <Exclusion 2>

## Inputs

| Input | Required | Description |
|-------|----------|-------------|
| <input-name> | Yes/No | <description> |

## Workflow

### Step 1: <Action>

<Instructions for this step>

### Step 2: <Action>

<Instructions for this step>

## Validation

- [ ] <Verification step 1>
- [ ] <Verification step 2>

## Common Pitfalls

| Pitfall | Solution |
|---------|----------|
| <Problem> | <How to avoid or fix> |
```

## Validation Checklist

After creating a skill, verify:

- [ ] Skill name matches directory name exactly
- [ ] Skill name is lowercase with hyphens only
- [ ] Description is non-empty and under 1024 characters
- [ ] SKILL.md body is under 500 lines
- [ ] Instructions are specific and actionable
- [ ] Bundled-file paths are relative to the directory that contains `SKILL.md`
- [ ] Missing bundled files cannot cause silent degradation
- [ ] Workflow has numbered steps with clear checkpoints
- [ ] Validation section exists with observable success criteria
- [ ] No secrets, tokens, or internal URLs included
- [ ] `.github/CODEOWNERS` has entries for the new skill and its test directory
- [ ] The description names concrete triggers and excludes the nearest sibling skills
- [ ] Every section changes a decision the unskilled model would otherwise get wrong
- [ ] The skill states when **not** to act, and what a truthful failure report looks like
- [ ] An `eval.yaml` exists and clears the distinct-stimulus floor (or the skill is `disable-model-invocation: true` and covered through its consumers)

## Common Pitfalls

| Pitfall | Solution |
|---------|----------|
| Name contains uppercase letters | Use only lowercase: `code-review` not `Code-Review` |
| Description is vague | Include what it does AND when to use it |
| Instructions are ambiguous | Use numbered steps with concrete actions |
| Missing validation steps | Add checkpoints that verify success |
| SKILL.md too long | Move detailed content to `references/` files |
| Hardcoded environment assumptions | Document requirements in `compatibility` field |
| Missing CODEOWNERS entry | Add entries for both `/plugins/<plugin>/skills/<skill-name>/` and `/tests/<plugin>/<skill-name>/` matching sibling skills' owner pattern |
| Skill restates what the model already knows | Cut it; a skill is scored as a delta over the unskilled model |
| Discoverable paths listed as required inputs | Tell the agent to discover them, or it will stop and ask the user |
| Description partitioned by topic against a sibling | Partition on the real discriminator and exclude on both sides |
| Exclusion clause blocks the skill's own use cases | Re-read every "do not use for" clause against real workflow phases |
| Skill added without an eval | Add `eval.yaml` in the same PR; unevaluated skills carry no evidence |

## References

- [Agent Skills Specification](https://agentskills.io/specification)
- [Repository README](../../../README.md)
- [Contributing Guidelines](../../../CONTRIBUTING.md)
- [create-skill-test](../create-skill-test/SKILL.md) — authoring the skill's `eval.yaml`
- [improve-skill-quality](../improve-skill-quality/SKILL.md) — fixing a skill that loses to its baseline
