---
mode: ask
description: Evaluate skill description quality in frontmatter across all plugins and rate them 1-5.
---

# Evaluate Skill Description Quality

This repo contains several skills for helping AI coding agents like GitHub Copilot and Claude Code with .NET-related coding tasks. Evaluate how good the description is in the YAML front matter for each skill under `plugins/**/skills/*/SKILL.md` and rate them on a scale of 1–5 where 5 is the best.

## What the Description Is For

The `description` field in each skill's frontmatter is the **only** information a coding agent sees (alongside the skill name) when deciding whether to load the skill. Agents use progressive loading — they read the full skill content only after they decide the skill is relevant. A poor description means a great skill never gets used.

A good description answers four questions:

1. **What it does** — the concrete outcome or capability
2. **When to use it** — trigger phrases, scenarios, or user intents that should activate the skill
3. **When NOT to use it** — clear boundaries, non-goals, or nearby-but-wrong intents where the skill should be skipped
4. **Key capabilities** — enough specifics to differentiate it from other skills

## Rating Scale

| Rating | Meaning |
|--------|---------|
| **5** | Excellent — clearly states what, when to use, when NOT to use, and key capabilities; includes concrete trigger phrases, user-intent signals, and clear non-goals/boundaries |
| **4** | Good — covers what and when to use and mentions some boundaries or non-goals, but could be more specific on triggers, edge cases, or capabilities |
| **3** | Adequate — describes the skill but is missing clear when-to-use or when-NOT-to-use guidance, lacks strong trigger phrases, or is too generic to reliably match user intent |
| **2** | Weak — vague or overly technical; an agent would struggle to know when to activate it or might over-trigger because boundaries and non-goals are unclear |
| **1** | Poor — missing, trivially short, or provides almost no actionable information |

## Examples of Good Descriptions (and Why)

```yaml
# Good — specific, actionable, with clear boundaries
description: Analyzes Figma design files and generates developer handoff
  documentation. Use when user uploads .fig files, asks for "design specs",
  "component documentation", or "design-to-code handoff". Avoid when user is
  asking about general UX research, copywriting, or non-Figma assets.

# Good — includes trigger phrases and non-goals
description: Manages Linear project workflows including sprint planning, task
  creation, and status tracking. Use when user mentions "sprint", "Linear
  tasks", "project planning", or asks to "create tickets". Avoid when the
  user is managing GitHub issues or other ticketing tools instead of Linear.

# Good — clear value proposition and scope limits
description: End-to-end customer onboarding workflow for PayFlow. Handles
  account creation, payment setup, and subscription management. Use when user
  says "onboard new customer", "set up subscription", or "create PayFlow
  account". Avoid when working with other payment providers or generic CRM
  onboarding flows unrelated to PayFlow.
```

## Examples of Bad Descriptions (and Why)

```yaml
# Too vague — no actionable detail
description: Helps with projects.

# Missing triggers — no user-intent signals
description: Creates sophisticated multi-page documentation systems.

# Too technical, no user triggers
description: Implements the Project entity model with hierarchical
  relationships.
```

## Instructions

1. Read each `SKILL.md` file under `plugins/**/skills/*/SKILL.md`.
2. Extract the `description` from the YAML frontmatter.
3. Rate each description 1–5 using the scale above.
4. For each skill, provide:
   - **Skill name** and plugin
   - **Current description** (quote it)
   - **Rating** (1–5)
   - **Explanation** — why you gave that rating, what is good, and what is missing
5. At the end, provide a summary table sorted by rating (lowest first) so the worst descriptions are easy to find and prioritize for improvement.
