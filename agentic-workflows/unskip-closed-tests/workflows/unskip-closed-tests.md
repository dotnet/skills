---
name: "Unskip Closed Tests"
description: >-
  Inventories source-bound .NET Ignore attributes at one trusted revision,
  permits a read-only agent to select only verified sites, and opens at most
  one draft pull request after deterministic issue and test-result validation.

on:
  schedule: weekly
  workflow_dispatch:
  skip-if-match: 'is:pr is:open in:title "[unskip-closed-tests]"'

strict: true
tracker-id: unskip-closed-tests

permissions:
  contents: read
  issues: read
  pull-requests: read
  copilot-requests: write

concurrency:
  group: unskip-closed-tests-${{ github.repository }}
  cancel-in-progress: false

network:
  allowed:
    - defaults
    - dotnet

timeout-minutes: 20

jobs:
  agent:
    needs: [collect-unskip-candidates]
    if: needs.collect-unskip-candidates.outputs.eligible-count != '0'

imports:
  - unskip-closed-tests-prepare.md
  - unskip-closed-tests-shared.md

steps:
  - name: Download trusted candidate manifest
    uses: actions/download-artifact@v8.0.1
    with:
      name: unskip-closed-tests-manifest-${{ github.run_id }}-${{ github.run_attempt }}
      path: .gh-aw/unskip-closed-tests

  - name: Export trusted manifest context
    shell: bash
    env:
      GH_AW_WORKSPACE_VALUE: ${{ github.workspace }}
      GH_AW_SOURCE_COMMIT_VALUE: ${{ needs.collect-unskip-candidates.outputs.source-commit }}
      GH_AW_MANIFEST_DIGEST_VALUE: ${{ needs.collect-unskip-candidates.outputs.manifest-digest }}
    run: |
      {
        echo "GH_AW_UNSKIP_MANIFEST=${GH_AW_WORKSPACE_VALUE}/.gh-aw/unskip-closed-tests/manifest.json"
        echo "GH_AW_UNSKIP_SOURCE_COMMIT=${GH_AW_SOURCE_COMMIT_VALUE}"
        echo "GH_AW_UNSKIP_MANIFEST_DIGEST=${GH_AW_MANIFEST_DIGEST_VALUE}"
      } >> "$GITHUB_ENV"

---

# Unskip Closed Tests

Review the trusted manifest at `GH_AW_UNSKIP_MANIFEST` and load
`.github/agents/unskip-closed-tests.agent.md`.

The deterministic collector, not you, owns source identity and GitHub
eligibility. You may defer an eligible candidate when its source or issue
context remains ambiguous, but you must not invent or repair candidate IDs,
paths, spans, containing types, test FQNs, issue references, issue states, or
revision data.

When one or more manifest candidates are safe to attempt, call
`apply_verified_unskips` exactly once with:

- `manifest_digest` equal to `GH_AW_UNSKIP_MANIFEST_DIGEST`;
- `candidate_ids_json` containing a JSON array of unique candidate IDs copied
  exactly from the manifest.

The read-only safe-output job revalidates source, remote state, proposed sites,
and structured test evidence. A fresh read-only authorizer derives the retained
set before a separate write-scoped job reconstructs the exact edits and
publishes one draft pull request. If no candidate should proceed, call `noop`
once with a short reason.
