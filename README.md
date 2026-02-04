> **Note:** This repository contains the .NET team's curated set of core skills and custom agents for coding agents. For information about the Agent Skills standard, see [agentskills.io](http://agentskills.io).

# Skills & Custom Agents

This repository is the home for **Skills** and **custom Agents** used by coding assistants (for example: GitHub Copilot CLI, VS Code, and Claude Code).

- **Skills** are reusable, task-focused instruction packs you can apply to an agent (e.g., "migrate a test suite to xUnit", "triage CI failures", "write a minimal design doc").
- **Agents** are role-based configurations that bundle a personality, tool expectations, and when/where specific skills should be used.

For background on the emerging standard, see [agentskills.io](http://agentskills.io).

## What’s in this repo

You’ll typically find two kinds of artifacts:

- `skills/…`: skill packages (each in its own folder)
- `agents/…`: agent definitions / profiles

This repo is intentionally tool-agnostic: the same skill can often be used across multiple agent runtimes with small wiring changes.

## Repository layout

Typical structure (exact filenames may evolve as tooling changes):

```text
skills/
 <skill-name>/
  SKILL.md
  scripts/
  references/
  assets/

agents/
 <agent-name>/
  README.md
  *.agent.md
```

### Skill conventions

A skill folder should be self-contained and:

- Clearly state **what it does** and **when to use it**.
- Specify required inputs (repo context, environment, access needs).
- Prefer concrete checklists and verification steps over vague guidance.

### Agent conventions

An agent definition should:

- Describe the **role** (e.g., "WinForms Expert", "Security Reviewer", "Docs Maintainer").
- Define boundaries (what the agent should not do).
- List the skills it expects to use and how it chooses among them.

## Using these skills and agents

Different tools load "skills" and "agents" in different ways. The goal of this repo is to keep the content reusable, while the tool-specific wiring remains minimal.

### GitHub Copilot CLI

Copilot CLI workflows vary by environment and wrapper scripts. Common approaches:

- Keep this repo as a submodule or sibling folder.
- Create a small wrapper command that injects the chosen skill text into the prompt/context.
- Standardize on a short “skill selector” (e.g., `skill=ci-triage`) so teammates can reproduce results.

### Claude Code

You can register this repository as a Claude Code Plugin marketplace by running the following command in Claude Code:

```
/plugin marketplace add dotnet/skills
```

Then, to install a specific set of skills:

1. Select `Browse and install plugins`
2. Select `dotnet-agent-skills`
3. Select `core-skills`
4. Select `Install now`

Alternatively, directly install either Plugin via:

```
/plugin install core-skills@dotnet-agent-skills
```

After installing the plugin, you can use the skill by just mentioning it. For instance, if you install the `core-skills` plugin from the marketplace, you can ask Claude Code to do something like: "Use the dotnet profile skill to identify memory leaks and CPU optimizations"

## Adding a new skill

Create a new folder under `skills/`:

```text
skills/<skill-name>/SKILL.md
```

Recommended `SKILL.md` sections:

- **Purpose**: one paragraph describing the outcome.
- **When to use** / **When not to use**
- **Inputs**: what the agent needs (files, commands, permissions).
- **Workflow**: numbered steps with checkpoints.
- **Validation**: how to confirm the result (tests, linters, manual checks).
- **Common pitfalls**: known traps and how to avoid them.

## Adding a new agent

Add a folder under `agents/` and include a short README describing:

- the agent's role
- expected tools and operating assumptions
- which skills it should use (and the order/priority)

## Quality bar

Skills and agents in this repo should be:

- **Actionable**: the agent can follow them without guesswork.
- **Minimal**: no extra features or scope creep; focus on the task.
- **Verifiable**: always include a way to validate success.
- **Tool-conscious**: don’t assume capabilities that might not exist in every runtime.

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for contribution guidelines.

---

If you’re not sure whether something belongs under `skills/` or `agents/`, a good rule of thumb is:

- Put **reusable task playbooks** in `skills/`.
- Put **role + operating model** in `agents/`.
