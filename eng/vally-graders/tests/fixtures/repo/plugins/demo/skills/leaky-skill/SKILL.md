---
name: leaky-skill
description: Demonstration skill with deliberately leaky evals (test fixture).
---

# leaky-skill

USE FOR: exercising the eval-review graders in tests.
DO NOT USE FOR: creating brand-new widgets (use widget-create instead).

This guidance was learned from telemetry across 42 real merged PRs.

## Guidance

Prefer `NewApi` over `OldApi`.
