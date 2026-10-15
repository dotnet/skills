---
name: code-testing
description: >-
  ALWAYS USE for test work that requires changes: write, add, generate, repair,
  or strengthen tests for existing code in xUnit, MSTest, NUnit, pytest,
  Vitest/Jest, Go, or another framework. Includes regression cases, failing or
  flaky tests, coverage-driven additions, and audit-then-fix requests. Focused
  work stays direct; broad or multi-stage work invokes test-engineer. DO NOT USE
  for only running tests, analysis-only audits, framework/platform migrations,
  a test blocked on a missing production seam (testability-obstacle), or MSTest
  API/configuration corrections that do not design new cases
  (writing-mstest-tests). Within an active test-engineer pipeline, reuse supplied
  guidance and do not re-enter this skill.
license: MIT
---

# Code Testing

The implicit entry point for generating, repairing and strengthening tests.
`test-engineer` owns orchestration; this skill routes the request rather than
starting a second Research-Plan-Implement pipeline.

## Workflow

1. **Check ownership and intent.** If already inside `test-engineer` or an
   assigned worker phase, continue that work directly. Never invoke another
   generator or reload this skill to restart the task.
2. **Apply the shared contract.** Read the relevant sections of
   [unit-test-generation.prompt.md](unit-test-generation.prompt.md):
   operation/resource permissions, behavior-pinning assertions, report-safe
   naming, result validation and completion. Reuse sections already supplied.
3. **Choose the smallest route.**

   | Request | Route |
   | --- | --- |
   | One function, class, file or bounded repair | Follow the shared prompt's focused workflow directly; no phase agents or intermediate state files. |
   | Project/package-wide generation or several modules | Invoke the available `test-engineer` custom agent once with the original scope, permissions, known paths and evidence. |
   | Audit then fix | Let `test-engineer` coordinate assessment and the authorized repairs; do not turn a static audit into editing. |
   | Only execute tests | Use `run-tests` for .NET, otherwise the repository runner. |
   | Framework/platform migration | Route to `test-migration`, not the generation workflow. |

   A project-wide request stays broad even when the checkout is sparse. If
   `test-engineer` is unavailable, execute the same
   [bounded generation workflow](references/generation-workflow.md) inline.
   Do not use a generic agent merely bearing the same name.
4. **Reuse guidance and evidence.** Resolve language files relative to the
   supplied/runtime-listed `code-testing-extensions` catalog. Read only the
   detected language and use examples only for an unanswered question. For
   .NET test commands, `run-tests` owns runner/mode/filter selection; preserve
   classic projects, pinned framework APIs and existing test infrastructure.
   Apply the shared contract's evidence-informed selection: consume relevant
   existing pairing, measured coverage and quality findings, and request CRAP
   analysis only for risk intent with reliable inputs. Do not collect optional
   metrics or rerun an audit merely because tests are being generated.
5. **Deliver the result once.** Consume the owner's requirement mapping and
   validation evidence. Follow up only on a concrete missing item; do not
   repeat discovery, skill audits or unchanged passing commands. Apply the
   shared completion contract to the final response itself.

## Capability limits

Use only advertised agents, skills and permitted tools. A denied operation is
not an assertion failure: do not retry it through an alias, another shell or
another agent. Continue independently permitted work and report unrun checks.
If bundled guidance is unavailable, use repository evidence and report the
limitation; do not search installation directories or invent framework semantics.

Broad work keeps research, plan and status in the non-stageable
`<TESTAGENT_DIR>` resolved by the generation workflow. If scratch storage is
denied, keep that reasoning in context, continue permitted edits and report
the missing artifacts. Never hide intermediate files with `.gitignore`.

## Validation

- Every requested behavior maps to an independent assertion and exact test.
- The final diff respects the selected operation, including valid failing
  regressions and mixed source/test files.
- Build, discovery, test results, requested coverage and configured reports
  support the reported status; unavailable or failing checks stay explicit.
- Focused work stayed direct; broad work had one owner, not an agent per phase.
