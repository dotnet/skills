---
name: "TEST - PAT Pool Import Verification"
description: >
  TEMPORARY workflow to verify that the Copilot PAT pool shared import
  (shared/pat_pool.md) reaches the agent job. Non-destructive (read-only, noop
  safe-output). Delete after testing.
on:
  pull_request:
    types: [opened, synchronize, reopened]
  workflow_dispatch:

  # Force a no-op pre_activation job so the imported pat_pool job (which
  # declares `needs: [pre_activation]`) has a dependency to attach to.
  permissions: {}

# ###############################################################
# Select a PAT from the pool and override COPILOT_GITHUB_TOKEN.
# See `shared/pat_pool.README.md` for more information.
# ###############################################################
imports:
  - shared/pat_pool.md

# Verification step injected into the agent job. This references the exact value
# that was silently empty with the old `needs.pre_activation.*` wiring. If the
# pat_pool output now reaches the agent job, the number is non-empty and passes.
steps:
  - name: Verify PAT pool reached the agent job
    env:
      PAT_NUMBER: ${{ needs.pat_pool.outputs.pat_number }}
    run: |
      echo "Agent job sees pat_number = '${PAT_NUMBER}'"
      if [ -z "${PAT_NUMBER}" ]; then
        echo "::error::PAT pool did NOT reach the agent job (number is empty)"
        exit 1
      fi
      echo "::notice::PAT pool reached the agent job with token number ${PAT_NUMBER}"

engine:
  id: copilot
  env:
    COPILOT_GITHUB_TOKEN: ${{ case(needs.pat_pool.outputs.pat_number == '0', secrets.COPILOT_GITHUB_TOKEN, needs.pat_pool.outputs.pat_number == '1', secrets.COPILOT_GITHUB_TOKEN_2, needs.pat_pool.outputs.pat_number == '2', secrets.COPILOT_GITHUB_TOKEN_3, needs.pat_pool.outputs.pat_number == '3', secrets.COPILOT_GITHUB_TOKEN_4, needs.pat_pool.outputs.pat_number == '4', secrets.COPILOT_GITHUB_TOKEN_5, needs.pat_pool.outputs.pat_number == '5', secrets.COPILOT_GITHUB_TOKEN_6, needs.pat_pool.outputs.pat_number == '6', secrets.COPILOT_GITHUB_TOKEN_7, needs.pat_pool.outputs.pat_number == '7', secrets.COPILOT_GITHUB_TOKEN_8, secrets.COPILOT_GITHUB_TOKEN) }}

permissions:
  contents: read

safe-outputs:
  noop:
    report-as-issue: false
---

# PAT Pool Import Test

This is a temporary, non-destructive test of the Copilot PAT pool shared import.

Respond with exactly the following text and nothing else:

`PAT pool import test complete.`

Do not use any tools. Do not take any other action.
