# Contributing

Thanks for your interest in contributing. We expect to accept external contributions, but the bar for merging is intentionally high.

This repository contains shared building blocks for coding agents:

- Skills: reusable, task focused instruction packs
- Agents: role based configurations that bundle tool expectations and skill selection

Because these artifacts can affect many users and workflows, we prioritize correctness, clarity, and long term maintainability over speed.

## Repository layout

```text
src/
  <component>/
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
      <test>/
        README.md
```

Each component under `src/` is self-contained. A plugin.json implies that the component is distributed as a plugin in the marketplace.

## Before you start

- Search existing issues and pull requests to avoid duplicates.
- Start with an issue before you submit a pull request for a new skill, a new agent, or any non trivial change. This helps us align on scope and avoids wasted work.
- Small fixes like typos, broken links, or clearly isolated corrections can go straight to a pull request.
- Keep changes small and focused. One skill or one agent per pull request is a good default.

## What we look for

We are most likely to accept contributions that are:

- Narrow in scope and easy to review
- Clearly motivated by a real use case
- Tool conscious and explicit about assumptions
- Verifiable with concrete validation steps
- Written to be durable across repo changes

We are less likely to accept contributions that:

- Add broad frameworks, meta tooling, or large reorganizations
- Duplicate guidance that already exists in another skill
- Encode private environment details, credentials, or company specific secrets
- Depend on proprietary tools or access that most contributors will not have

## Proposing a new skill

A skill should be self-contained and:

- Clearly state **what it does** and **when to use it**.
- Specify required inputs (repo context, environment, access needs).
- Prefer concrete checklists and verification steps over vague guidance.

Create a new folder under a component's `skills/` directory:

```text
src/<component>/skills/<skill-name>/SKILL.md
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
description: <description of what the skill does and when to use it>
---
```

### Recommended `SKILL.md` sections

- **Purpose**: one paragraph describing the outcome.
- **When to use** / **When not to use**
- **Inputs**: what the agent needs (files, commands, permissions).
- **Workflow**: numbered steps with checkpoints.
- **Validation**: how to confirm the result (tests, linters, manual checks).
- **Common pitfalls**: known traps and how to avoid them.

### Skill checklist

Include a `SKILL.md` that covers:

- Purpose and non goals
- When to use and when not to use
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

Add an agent file under a component's `agents/` directory:

```text
src/<component>/agents/<agent-name>.agent.md
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

## Writing style

- Be concise and specific.
- Prefer numbered steps for workflows.
- Prefer checklists for requirements.
- Define terminology the first time it appears.
- Avoid excessive formatting and avoid clever wording that could be misread by an agent.

## Security and safety

- Do not include secrets, tokens, or internal URLs.
- If you discover a security issue, do not open a public issue with sensitive details. Use the repository or organization security reporting process instead.

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
