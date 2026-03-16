---
mode: ask
description: Evaluate skill description quality in frontmatter across all plugins and rate them 1-5.
---

# Evaluate Skill Description Quality

This repo contains several skills for helping AI coding agents like GitHub Copilot and Claude Code with .NET related coding tasks. Evaluate how good the description is in the YAML front matter for each skill under `plugins/**/skills/*/SKILL.md` and rate them on a scale of 1–5 where 5 is the best.

## What the Description Is For

The `description` field in each skill's frontmatter is the **only** information a coding agent sees (alongside the skill name) when deciding whether to load the skill. Agents use progressive loading — they read the full skill content only after they decide the skill is relevant. A poor description means a great skill never gets used.

A good description answers three questions:

1. **What it does** — the concrete outcome or capability
2. **When to use it** — trigger phrases, scenarios, or user intents that should activate the skill
3. **Key capabilities** — enough specifics to differentiate it from other skills

## Rating Scale

| Rating | Meaning |
|--------|---------|
| **5** | Excellent — clearly states what, when, and key capabilities; includes trigger phrases or user-intent signals |
| **4** | Good — covers what and when but could be more specific on triggers or capabilities |
| **3** | Adequate — describes the skill but is missing trigger phrases or is too generic to reliably match user intent |
| **2** | Weak — vague or overly technical; an agent would struggle to know when to activate it |
| **1** | Poor — missing, trivially short, or provides almost no actionable information |

## Examples of Good Descriptions (and Why)

```yaml
# Good — specific and actionable
description: Analyzes Figma design files and generates developer handoff
  documentation. Use when user uploads .fig files, asks for "design specs",
  "component documentation", or "design-to-code handoff".

# Good — includes trigger phrases
description: Manages Linear project workflows including sprint planning, task
  creation, and status tracking. Use when user mentions "sprint", "Linear
  tasks", "project planning", or asks to "create tickets".

# Good — clear value proposition
description: End-to-end customer onboarding workflow for PayFlow. Handles
  account creation, payment setup, and subscription management. Use when user
  says "onboard new customer", "set up subscription", or "create PayFlow
  account".
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
