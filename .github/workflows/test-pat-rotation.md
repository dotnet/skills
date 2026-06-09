---
name: "TEST - PAT Rotation Verification"
description: >
  TEMPORARY workflow to verify that the Copilot PAT rotation reaches the agent
  job. Non-destructive (read-only, noop safe-output). Delete after testing.
on:
  pull_request:
    types: [opened, synchronize, reopened]
  workflow_dispatch:

  # PAT rotation via a custom job + on.needs (the approach under test).
  needs: [select_copilot_pat]

# Custom job that randomly selects one PAT number from the pool of secrets.
jobs:
  select_copilot_pat:
    runs-on: ubuntu-slim
    permissions:
      contents: read
    outputs:
      copilot_pat_number: ${{ steps.select-copilot-pat.outputs.copilot_pat_number }}
    steps:
      - uses: actions/checkout@de0fac2e4500dabe0009e67214ff5f5447ce83dd # v6.0.2
        name: Checkout the select-copilot-pat action folder
        with:
          persist-credentials: false
          sparse-checkout: .github/actions/select-copilot-pat
          sparse-checkout-cone-mode: true
          fetch-depth: 1

      - id: select-copilot-pat
        name: Select Copilot token from pool
        uses: ./.github/actions/select-copilot-pat
        env:
          SECRET_0: ${{ secrets.COPILOT_GITHUB_TOKEN }}
          SECRET_1: ${{ secrets.COPILOT_GITHUB_TOKEN_2 }}
          SECRET_2: ${{ secrets.COPILOT_GITHUB_TOKEN_3 }}
          SECRET_3: ${{ secrets.COPILOT_GITHUB_TOKEN_4 }}
          SECRET_4: ${{ secrets.COPILOT_GITHUB_TOKEN_5 }}
          SECRET_5: ${{ secrets.COPILOT_GITHUB_TOKEN_6 }}
          SECRET_6: ${{ secrets.COPILOT_GITHUB_TOKEN_7 }}
          SECRET_7: ${{ secrets.COPILOT_GITHUB_TOKEN_8 }}

# Verification step injected into the agent job. This is the exact reference that
# was silently empty with the old `needs.pre_activation.*` wiring. If the rotation
# now reaches the agent job, the number is non-empty and the step passes.
steps:
  - name: Verify PAT rotation reached the agent job
    env:
      PAT_NUMBER: ${{ needs.select_copilot_pat.outputs.copilot_pat_number }}
    run: |
      echo "Agent job sees copilot_pat_number = '${PAT_NUMBER}'"
      if [ -z "${PAT_NUMBER}" ]; then
        echo "::error::PAT rotation did NOT reach the agent job (number is empty)"
        exit 1
      fi
      echo "::notice::PAT rotation reached the agent job with token number ${PAT_NUMBER}"

engine:
  id: copilot
  env:
    COPILOT_GITHUB_TOKEN: ${{ case(needs.select_copilot_pat.outputs.copilot_pat_number == '0', secrets.COPILOT_GITHUB_TOKEN, needs.select_copilot_pat.outputs.copilot_pat_number == '1', secrets.COPILOT_GITHUB_TOKEN_2, needs.select_copilot_pat.outputs.copilot_pat_number == '2', secrets.COPILOT_GITHUB_TOKEN_3, needs.select_copilot_pat.outputs.copilot_pat_number == '3', secrets.COPILOT_GITHUB_TOKEN_4, needs.select_copilot_pat.outputs.copilot_pat_number == '4', secrets.COPILOT_GITHUB_TOKEN_5, needs.select_copilot_pat.outputs.copilot_pat_number == '5', secrets.COPILOT_GITHUB_TOKEN_6, needs.select_copilot_pat.outputs.copilot_pat_number == '6', secrets.COPILOT_GITHUB_TOKEN_7, needs.select_copilot_pat.outputs.copilot_pat_number == '7', secrets.COPILOT_GITHUB_TOKEN_8, secrets.COPILOT_GITHUB_TOKEN) }}

permissions:
  contents: read

safe-outputs:
  noop:
    report-as-issue: false
---

# PAT Rotation Test

This is a temporary, non-destructive test of the Copilot PAT rotation wiring.

Respond with exactly the following text and nothing else:

`PAT rotation test complete.`

Do not use any tools. Do not take any other action.
