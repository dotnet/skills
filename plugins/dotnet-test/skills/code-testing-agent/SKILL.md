---
name: code-testing-agent
description: >-
  Legacy compatibility alias for the code-testing skill. Do not self-activate
  for user requests and do not treat this name as a custom agent. When invoked
  explicitly by older instructions, load code-testing and follow it instead.
user-invocable: false
disable-model-invocation: true
license: MIT
---

# Legacy Code Testing Alias

This skill preserves explicit calls that still use the former
`code-testing-agent` name. The public custom agent is `test-engineer`; the
model-facing implicit skill is `code-testing`.

1. Invoke the `code-testing` skill with the original user request.
2. Follow that skill's workflow without adding another interpretation layer.
