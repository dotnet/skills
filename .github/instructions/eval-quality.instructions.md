---
applyTo: "tests/**"
excludeAgent: "cloud-agent"
---

# Evaluation quality review

When reviewing these files, apply the evaluation design quality bar in
`CONTRIBUTING.md` and `.agents/skills/create-skill-test/SKILL.md`. For a grader,
reference, or fixture change, inspect its owning `eval.yaml` and apply only the
checks relevant to that artifact.

Leave an inline comment only for a specific, actionable defect. Check that:

- each scenario is necessary for the target; each preference-eligible scenario adds distinct
  capability, risk, or customer-journey value, while dormancy and no-op guards protect a meaningful
  routing or preservation invariant;
- prompts are natural developer requests and do not name the skill, reveal the answer, or prescribe
  its workflow;
- deterministic graders prove the complete required result and, when scope or preservation is part
  of the request, cover every in-scope file;
- golden evidence passes the deterministic contract and a realistic broken mutation would fail it;
- rewrite scenarios include an already-correct no-op case, and routing boundaries include an
  uncued dormancy case that proves recognition, restraint, and redirection;
- preference-eligible stimuli have enough task breadth for the expected tie rate, not only the
  five-stimulus floor;
- fixtures reproduce only the intended condition and are complete and consistent;
- skill evals use the Vally path, agent evals use the native SDK path, and declared time budgets are
  realistic;
- broad routing or behavior claims have separate evidence across model families;
- a proposed skill-content fix follows failure classification and is supported by the observed evidence.

Do not duplicate deterministic findings already reported by `check_eval_quality.py`. Do not request
style-only changes. If evidence is missing, state what must be added and why the current eval could
accept an incorrect result or reject a correct one.
