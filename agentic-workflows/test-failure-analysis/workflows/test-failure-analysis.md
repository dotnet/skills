---
name: "Test Failure Analysis"
description: >-
  Validates a same-repository GitHub Actions artifact containing bounded,
  normalized test evidence, then reports evidence-backed failures, retries,
  hangs, crashes, or duration regressions without running code or tests.

on:
  workflow_call:
    inputs:
      evidence-run-id:
        description: "Numeric Actions run ID that owns the evidence artifact."
        required: true
        type: string
      evidence-artifact-name:
        description: "Exact same-repository artifact name."
        required: true
        type: string
      analysis-phase:
        description: "Analysis phase: preliminary or final."
        required: true
        type: string
      pr-number:
        description: "Trusted pull request number that may receive the comment."
        required: true
        type: string
      expected-head-sha:
        description: "Expected current PR head SHA."
        required: true
        type: string
      expected-tested-sha:
        description: "Exact tested head or merge SHA represented by the evidence."
        required: true
        type: string
      expected-build-identity:
        description: "Stable CI build identity expected in metadata.json."
        required: true
        type: string
      trusted-comment-author:
        description: "Login used by gh-aw safe outputs for lifecycle comments."
        required: false
        type: string
        default: "github-actions[bot]"
      source-run-url:
        description: "Optional trusted source run URL; must match the Actions run."
        required: false
        type: string
        default: ""
      evidence-summary-location:
        description: "Optional trusted logical summary location; never fetched."
        required: false
        type: string
        default: ""
      duration-regression-percent:
        description: "Minimum percentage increase required for duration findings."
        required: false
        type: number
        default: 25
      duration-regression-minimum-seconds:
        description: "Minimum absolute increase in seconds for duration findings."
        required: false
        type: number
        default: 30
      duration-regression-minimum-baseline-samples:
        description: "Minimum comparable baseline sample count."
        required: false
        type: number
        default: 5
  workflow_dispatch:
    inputs:
      evidence-run-id:
        description: "Numeric Actions run ID that owns the evidence artifact."
        required: true
        type: string
      evidence-artifact-name:
        description: "Exact same-repository artifact name."
        required: true
        type: string
      analysis-phase:
        description: "Analysis phase."
        required: true
        type: choice
        options: [preliminary, final]
      pr-number:
        description: "Trusted pull request number that may receive the comment."
        required: true
        type: string
      expected-head-sha:
        description: "Expected current PR head SHA."
        required: true
        type: string
      expected-tested-sha:
        description: "Exact tested head or merge SHA represented by the evidence."
        required: true
        type: string
      expected-build-identity:
        description: "Stable CI build identity expected in metadata.json."
        required: true
        type: string
      trusted-comment-author:
        description: "Login used by gh-aw safe outputs for lifecycle comments."
        required: false
        type: string
        default: "github-actions[bot]"
      source-run-url:
        description: "Optional trusted source run URL; must match the Actions run."
        required: false
        type: string
      evidence-summary-location:
        description: "Optional trusted logical summary location; never fetched."
        required: false
        type: string
      duration-regression-percent:
        description: "Minimum percentage increase required for duration findings."
        required: false
        type: number
        default: 25
      duration-regression-minimum-seconds:
        description: "Minimum absolute increase in seconds for duration findings."
        required: false
        type: number
        default: 30
      duration-regression-minimum-baseline-samples:
        description: "Minimum comparable baseline sample count."
        required: false
        type: number
        default: 5
  needs: [collect-test-evidence]

if: needs.collect-test-evidence.outputs.evidence-found == 'true'

permissions:
  actions: read
  contents: read
  issues: read
  pull-requests: read
  copilot-requests: write

concurrency:
  group: test-failure-analysis-${{ inputs['pr-number'] }}-${{ inputs['expected-tested-sha'] }}
  cancel-in-progress: false
  queue: max
  job-discriminator: ${{ github.run_id }}

timeout-minutes: 20

imports:
  - test-failure-analysis-fetch.md
  - test-failure-analysis-shared.md

steps:
  - name: Download sanitized evidence
    uses: actions/download-artifact@v8.0.1
    with:
      name: test-failure-analysis-data
      path: .gh-aw/test-failure-analysis/evidence

  - name: Export trusted analysis context
    shell: bash
    env:
      GH_AW_PHASE_VALUE: ${{ needs.collect-test-evidence.outputs.analysis-phase }}
      GH_AW_PR_NUMBER_VALUE: ${{ needs.collect-test-evidence.outputs.pr-number }}
      GH_AW_HEAD_SHA_VALUE: ${{ needs.collect-test-evidence.outputs.head-sha }}
      GH_AW_TESTED_SHA_VALUE: ${{ needs.collect-test-evidence.outputs.tested-sha }}
      GH_AW_BUILD_IDENTITY_VALUE: ${{ needs.collect-test-evidence.outputs.build-identity }}
      GH_AW_TRUSTED_COMMENT_AUTHOR_VALUE: ${{ needs.collect-test-evidence.outputs.trusted-comment-author }}
      GH_AW_SOURCE_RUN_ID_VALUE: ${{ needs.collect-test-evidence.outputs.source-run-id }}
      GH_AW_SOURCE_RUN_URL_VALUE: ${{ needs.collect-test-evidence.outputs.source-run-url }}
      GH_AW_SUMMARY_LOCATION_VALUE: ${{ needs.collect-test-evidence.outputs.summary-location }}
      GH_AW_COMPLETENESS_VALUE: ${{ needs.collect-test-evidence.outputs.evidence-complete }}
      GH_AW_COMPLETENESS_REASONS_VALUE: ${{ needs.collect-test-evidence.outputs.completeness-reasons }}
      GH_AW_DURATION_PERCENT_VALUE: ${{ inputs['duration-regression-percent'] }}
      GH_AW_DURATION_SECONDS_VALUE: ${{ inputs['duration-regression-minimum-seconds'] }}
      GH_AW_DURATION_SAMPLES_VALUE: ${{ inputs['duration-regression-minimum-baseline-samples'] }}
      GH_AW_WORKSPACE_VALUE: ${{ github.workspace }}
    run: |
      {
        echo "GH_AW_EVIDENCE_DIR=${GH_AW_WORKSPACE_VALUE}/.gh-aw/test-failure-analysis/evidence"
        echo "GH_AW_ANALYSIS_PHASE=${GH_AW_PHASE_VALUE}"
        echo "GH_AW_PR_NUMBER=${GH_AW_PR_NUMBER_VALUE}"
        echo "GH_AW_EXPECTED_HEAD_SHA=${GH_AW_HEAD_SHA_VALUE}"
        echo "GH_AW_EXPECTED_TESTED_SHA=${GH_AW_TESTED_SHA_VALUE}"
        echo "GH_AW_BUILD_IDENTITY=${GH_AW_BUILD_IDENTITY_VALUE}"
        echo "GH_AW_TRUSTED_COMMENT_AUTHOR=${GH_AW_TRUSTED_COMMENT_AUTHOR_VALUE}"
        echo "GH_AW_SOURCE_RUN_ID=${GH_AW_SOURCE_RUN_ID_VALUE}"
        echo "GH_AW_SOURCE_RUN_URL=${GH_AW_SOURCE_RUN_URL_VALUE}"
        echo "GH_AW_EVIDENCE_SUMMARY_LOCATION=${GH_AW_SUMMARY_LOCATION_VALUE}"
        echo "GH_AW_EVIDENCE_COMPLETE=${GH_AW_COMPLETENESS_VALUE}"
        echo "GH_AW_COMPLETENESS_REASONS=${GH_AW_COMPLETENESS_REASONS_VALUE}"
        echo "GH_AW_DURATION_REGRESSION_PERCENT=${GH_AW_DURATION_PERCENT_VALUE}"
        echo "GH_AW_DURATION_REGRESSION_MINIMUM_SECONDS=${GH_AW_DURATION_SECONDS_VALUE}"
        echo "GH_AW_DURATION_REGRESSION_MINIMUM_BASELINE_SAMPLES=${GH_AW_DURATION_SAMPLES_VALUE}"
      } >> "$GITHUB_ENV"

---

<!-- Prompt and tool configuration are imported from test-failure-analysis-shared.md. -->
