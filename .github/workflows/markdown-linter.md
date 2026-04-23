---
name: "Markdown Linter"
description: >
  Runs Markdown quality checks using Super Linter and creates issues
  for violations found across the repository.

on:
  workflow_dispatch:
  schedule:
    - cron: "0 14 * * 1-5" # 2 PM UTC, weekdays only

  # ###############################################################
  # Override the COPILOT_GITHUB_TOKEN secret usage for the workflow
  # with a randomly-selected token from a pool of secrets.
  #
  # As soon as organization-level billing is offered for Agentic
  # Workflows, this stop-gap approach will be removed.
  #
  # See: /.github/actions/select-copilot-pat/README.md
  # ###############################################################
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

# Don't run scheduled triggers on forked repositories — forks lack the
# secrets and context required, and scheduled runs would consume the
# fork owner's minutes.
if: ${{ !(github.event_name == 'schedule' && github.event.repository.fork) }}

# Add the pre-activation output of the randomly selected PAT
jobs:
  pre-activation:
    outputs:
      copilot_pat_number: ${{ steps.select-copilot-pat.outputs.copilot_pat_number }}

  super_linter:
    runs-on: ubuntu-latest
    permissions:
      contents: read
      packages: read
      statuses: write
    steps:
      - name: Checkout repository
        uses: actions/checkout@de0fac2e4500dabe0009e67214ff5f5447ce83dd # v6.0.2
        with:
          fetch-depth: 0
          persist-credentials: false

      - name: Super-linter
        uses: super-linter/super-linter@61abc07d755095a68f4987d1c2c3d1d64408f1f9 # v8.5.0
        id: super-linter
        env:
          GITHUB_TOKEN: ${{ secrets.GITHUB_TOKEN }}
          CREATE_LOG_FILE: "true"
          LOG_FILE: super-linter.log
          DEFAULT_BRANCH: main
          ENABLE_GITHUB_ACTIONS_STEP_SUMMARY: "true"
          VALIDATE_MARKDOWN: "true"
          VALIDATE_ALL_CODEBASE: "true"

      - name: Check for linting issues
        id: check-results
        run: |
          if [ -f "super-linter.log" ] && [ -s "super-linter.log" ]; then
            if grep -qE "ERROR|WARN|FAIL" super-linter.log; then
              echo "needs-linting=true" >> "$GITHUB_OUTPUT"
            else
              echo "needs-linting=false" >> "$GITHUB_OUTPUT"
            fi
          else
            echo "needs-linting=false" >> "$GITHUB_OUTPUT"
          fi

      - name: Upload super-linter log
        if: always()
        uses: actions/upload-artifact@043fb46d1a93c77aae656e7c1c64a875d1fc6a0a # v7.0.1
        with:
          name: super-linter-log
          path: super-linter.log
          retention-days: 7

# Override the COPILOT_GITHUB_TOKEN expression used in the activation job
engine:
  id: copilot
  env:
    COPILOT_GITHUB_TOKEN: ${{ case(needs.pre_activation.outputs.copilot_pat_number == '0', secrets.COPILOT_GITHUB_TOKEN, needs.pre_activation.outputs.copilot_pat_number == '1', secrets.COPILOT_GITHUB_TOKEN_2, needs.pre_activation.outputs.copilot_pat_number == '2', secrets.COPILOT_GITHUB_TOKEN_3, needs.pre_activation.outputs.copilot_pat_number == '3', secrets.COPILOT_GITHUB_TOKEN_4, needs.pre_activation.outputs.copilot_pat_number == '4', secrets.COPILOT_GITHUB_TOKEN_5, needs.pre_activation.outputs.copilot_pat_number == '5', secrets.COPILOT_GITHUB_TOKEN_6, needs.pre_activation.outputs.copilot_pat_number == '6', secrets.COPILOT_GITHUB_TOKEN_7, needs.pre_activation.outputs.copilot_pat_number == '7', secrets.COPILOT_GITHUB_TOKEN_8, secrets.COPILOT_GITHUB_TOKEN) }}

permissions:
  contents: read
  actions: read
  issues: read

tools:
  cache-memory: true
  edit:
  bash:
    - "cat"
    - "grep"
    - "head"
    - "tail"
    - "find"
    - "ls"
    - "wc"
    - "sort"
    - "uniq"
    - "date"

safe-outputs:
  create-issue:
    expires: 2d
    title-prefix: "[linter] "
    labels: [automation, code-quality]
    max: 1
  noop:
    report-as-issue: false

network:
  allowed:
    - defaults

timeout-minutes: 15

steps:
  - name: Download super-linter log
    uses: actions/download-artifact@3e5f45b2cfb9172054b4087a40e8e0b5a5461e7c # v8.0.1
    with:
      name: super-linter-log
      path: /tmp/gh-aw/
---

# Markdown Quality Report

You are an expert documentation quality analyst. Your task is to analyze the
Super Linter Markdown output and create a comprehensive issue report for the
repository maintainers.

## Context

- **Repository**: ${{ github.repository }}
- **Triggered by**: @${{ github.actor }}
- **Run ID**: ${{ github.run_id }}

## Your Task

1. **Read the linter output** from `/tmp/gh-aw/super-linter.log` using the bash tool
2. **Analyze the findings**:
   - Categorize errors by severity (critical, high, medium, low)
   - Identify patterns in the errors
   - Determine which errors are most important to fix first
   - Note: This workflow only validates Markdown files
3. **Create a detailed issue** with the following structure:

### Issue Title

Use format: "Markdown Quality Report - [Date] - [X] issues found"

### Issue Body Structure

```markdown
## 🔍 Markdown Linter Summary

**Date**: [Current date]
**Total Issues Found**: [Number]
**Run ID**: ${{ github.run_id }}

## 📊 Breakdown by Severity

- **Critical**: [Count and brief description]
- **High**: [Count and brief description]
- **Medium**: [Count and brief description]
- **Low**: [Count and brief description]

## 📁 Issues by Category

### [Category/Rule Name]
- **File**: `path/to/file`
  - Line [X]: [Error description]
  - Suggested fix: [How to resolve]

[Repeat for other categories]

## 🎯 Priority Recommendations

1. [Most critical issue to address first]
2. [Second priority]
3. [Third priority]

## 📋 Full Linter Output

<details>
<summary>Click to expand complete linter log</summary>

```
[Include the full linter output here]
```

</details>

## 🔗 References

- [Link to workflow run](${{ github.server_url }}/${{ github.repository }}/actions/runs/${{ github.run_id }})
- [Super Linter Documentation](https://github.com/super-linter/super-linter)
```

## Important Guidelines

- **Be concise but thorough**: Focus on actionable insights
- **Prioritize issues**: Not all linting errors are equal
- **Provide context**: Explain why each type of error matters for documentation quality
- **Suggest fixes**: Give practical recommendations
- **Use proper formatting**: Make the issue easy to read and navigate
- **If no errors found**: Call `noop` celebrating clean markdown

**Important**: Always call exactly one safe-output tool before finishing (`create-issue` or `noop`).

```json
{"noop": {"message": "No action needed: [brief explanation of what was analyzed and why]"}}
```
