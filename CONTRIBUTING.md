# Contributing

Thanks for your interest in contributing. We expect to accept external contributions, but the bar for merging is intentionally high.

This repository contains shared building blocks for coding agents:

- Skills: reusable, task focused instruction packs
- Agents: role based configurations that bundle tool expectations and skill selection

Because these artifacts can affect many users and workflows, we prioritize correctness, clarity, and long term maintainability over speed.

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

A skill should answer three questions up front:

1. What outcome does the skill produce
2. When should an agent use it
3. How does the agent validate success

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

An agent definition should be opinionated but bounded.

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
