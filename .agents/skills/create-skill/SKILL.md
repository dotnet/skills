---
name: create-skill
description: Scaffolds new agent skills for the dotnet/skills repository. Use when creating a new skill, generating SKILL.md files, or setting up skill directory structures. Handles frontmatter generation, section templates, and validation guidance.
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

### Step 2: Create the skill directory

```
skills/<skill-name>/
└── SKILL.md
```

### Step 3: Generate SKILL.md with frontmatter

Create the file with required YAML frontmatter:

```yaml
---
name: <skill-name>
description: <description of what the skill does and when to use it>
---
```

### Step 4: Add body content sections

Include these recommended sections:

1. **Purpose**: One paragraph describing the outcome
2. **When to Use**: Bullet list of appropriate scenarios
3. **When Not to Use**: Boundaries and exclusions
4. **Inputs**: Table of required and optional inputs
5. **Workflow**: Numbered steps with checkpoints
6. **Validation**: How to confirm the skill worked correctly
7. **Common Pitfalls**: Known traps and how to avoid them

### Step 5: Add optional directories (if needed)

```
skills/<skill-name>/
├── SKILL.md
├── scripts/       # Executable code agents can run
├── references/    # Additional documentation loaded on demand
└── assets/        # Templates, images, data files
```

### Step 6: Update CODEOWNERS

Add entries in `.github/CODEOWNERS` for the new skill and its test directory:

```
/plugins/<plugin>/skills/<skill-name>/  @owner-team
/tests/<plugin>/<skill-name>/           @owner-team
```

Match the owner pattern used by sibling skills in the same plugin.

### Step 7: Validate the skill

- Confirm frontmatter fields are valid
- Ensure SKILL.md is under 500 lines
- Check that file references use relative paths
- Verify instructions are actionable and specific

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
- [ ] Workflow has numbered steps with clear checkpoints
- [ ] Validation section exists with observable success criteria
- [ ] No secrets, tokens, or internal URLs included
- [ ] `.github/CODEOWNERS` has entries for the new skill and its test directory

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

## References

- [Agent Skills Specification](https://agentskills.io/specification)
- [Repository README](../../README.md)
- [Contributing Guidelines](../../CONTRIBUTING.md)
