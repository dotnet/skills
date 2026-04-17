---
name: "DevOps Health — Deep Investigation"
description: >
  Worker agent that performs deep root-cause analysis on a single
  health check finding (pipeline, infrastructure, or resource).
  Dispatched by the health check orchestrator.

on:
  workflow_dispatch:
    inputs:
      finding_id:
        description: "Fingerprint ID of the finding to investigate"
        required: true
      finding_type:
        description: "Category: pipeline | infra | resource"
        required: true
      finding_title:
        description: "Human-readable title of the finding"
        required: true
      finding_severity:
        description: "Severity: critical | warning | info"
        required: true
      resource_url:
        description: "URL to the primary resource (run, PR, etc.)"
        required: true
      health_issue_number:
        description: "Issue number of the pinned health dashboard"
        required: true
      correlation_id:
        description: "Unique ID linking this investigation to the health check run"
        required: true

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
        # If the secret names are changed here, they must also be changed
        # in the `engine: env` case expression below
        SECRET_0: ${{ secrets.COPILOT_GITHUB_TOKEN }}
        SECRET_1: ${{ secrets.COPILOT_GITHUB_TOKEN_2 }}
        SECRET_2: ${{ secrets.COPILOT_GITHUB_TOKEN_3 }}
        SECRET_3: ${{ secrets.COPILOT_GITHUB_TOKEN_4 }}
        SECRET_4: ${{ secrets.COPILOT_GITHUB_TOKEN_5 }}
        SECRET_5: ${{ secrets.COPILOT_GITHUB_TOKEN_6 }}
        SECRET_6: ${{ secrets.COPILOT_GITHUB_TOKEN_7 }}
        SECRET_7: ${{ secrets.COPILOT_GITHUB_TOKEN_8 }}

concurrency:
  group: gh-aw-${{ github.workflow }}-${{ inputs.finding_id }}

# Add the pre-activation output of the randomly selected PAT
jobs:
  pre-activation:
    outputs:
      copilot_pat_number: ${{ steps.select-copilot-pat.outputs.copilot_pat_number }}

# Override the COPILOT_GITHUB_TOKEN expression used in the activation job
# Consume the PAT number from the pre-activation step and select the corresponding secret
engine:
  id: copilot
  env:
    # We cannot use line breaks in this expression as it leads to a syntax error in the compiled workflow
    # If none of the `COPILOT_GITHUB_TOKEN_#` secrets were selected, then the default COPILOT_GITHUB_TOKEN is used
    COPILOT_GITHUB_TOKEN: ${{ case(needs.pre_activation.outputs.copilot_pat_number == '0', secrets.COPILOT_GITHUB_TOKEN, needs.pre_activation.outputs.copilot_pat_number == '1', secrets.COPILOT_GITHUB_TOKEN_2, needs.pre_activation.outputs.copilot_pat_number == '2', secrets.COPILOT_GITHUB_TOKEN_3, needs.pre_activation.outputs.copilot_pat_number == '3', secrets.COPILOT_GITHUB_TOKEN_4, needs.pre_activation.outputs.copilot_pat_number == '4', secrets.COPILOT_GITHUB_TOKEN_5, needs.pre_activation.outputs.copilot_pat_number == '5', secrets.COPILOT_GITHUB_TOKEN_6, needs.pre_activation.outputs.copilot_pat_number == '6', secrets.COPILOT_GITHUB_TOKEN_7, needs.pre_activation.outputs.copilot_pat_number == '7', secrets.COPILOT_GITHUB_TOKEN_8, secrets.COPILOT_GITHUB_TOKEN) }}

permissions:
  contents: read
  actions: read
  issues: read
  pull-requests: read

imports:
  - ../aw/shared/devops-investigate.lock.md

tools:
  github:
    toolsets: [repos, issues, pull_requests, actions]
  bash: ["cat", "grep", "head", "tail", "find", "ls", "wc", "jq", "date", "sort", "diff"]

safe-outputs:
  add-comment:
    max: 1
  noop:
    report-as-issue: false

network:
  allowed:
    - defaults

timeout-minutes: 60
---

# DevOps Health — Deep Investigation Worker

You are a specialized investigation agent. You have been dispatched by the DevOps Health Check orchestrator to perform a deep root-cause analysis on **one specific finding**.

## Your Mission

Investigate the finding identified by the inputs provided to this workflow run. Determine the root cause, assess the blast radius, and generate actionable remediation steps. Report your findings back to the pinned health issue.

## Inputs Available

- `finding_id`: `${{ inputs.finding_id }}` — The fingerprint ID of the finding
- `finding_type`: `${{ inputs.finding_type }}` — Category (pipeline, infra, resource)
- `finding_title`: `${{ inputs.finding_title }}` — Human-readable title
- `finding_severity`: `${{ inputs.finding_severity }}` — Severity level
- `resource_url`: `${{ inputs.resource_url }}` — URL to the primary resource
- `health_issue_number`: `${{ inputs.health_issue_number }}` — Issue to update
- `correlation_id`: `${{ inputs.correlation_id }}` — Links this investigation to the health check run

---

## Investigation Protocol

### Step 1: Route to Category-Specific Playbook

Based on `finding_type`, follow the appropriate investigation playbook from the compiled knowledge file:

- **pipeline** → Pipeline Investigation Playbook
- **infra** → Infrastructure Investigation Playbook
- **resource** → Resource Investigation Playbook

### Step 2: Gather Evidence

Follow the playbook steps meticulously. For each piece of evidence:
- Record the **source** (API endpoint, file path, log excerpt)
- Note the **timestamp** of the evidence
- Assess **relevance** to the finding

### Step 3: Determine Root Cause

Based on the gathered evidence:
1. Identify the **most likely root cause**
2. Assign a **confidence level**: High / Medium / Low
   - **High**: Direct evidence (error message explicitly states the cause, code change directly correlates)
   - **Medium**: Strong circumstantial evidence (timing correlates, pattern matches known issues)
   - **Low**: Inferential (possible but no direct evidence found)
3. Identify the **blast radius** — what else is affected?
4. Check for **related issues** — is this already tracked?

### Step 4: Generate Remediation Steps

Provide 1–3 specific, actionable remediation steps. Each step should:
- Be concrete (include file paths, commands, or config changes)
- Be ordered by recommended priority
- Include any caveats or risks

### Step 5: Report Back

Post your investigation results as a comment on the pinned health issue.

**IMPORTANT**: You MUST use the `add-comment` safe-output tool (NOT `update-issue`, which does not work for `workflow_dispatch` triggered workflows). Pass the `health_issue_number` as the `item_number` parameter.

```
add-comment:
  item_number: {health_issue_number}
  body: |
    ## 🔍 Investigation: {finding_title}

    **Finding ID:** `{finding_id}`
    **Severity:** {finding_severity}
    **Correlation:** {correlation_id}
    **Executive Summary:** {one-sentence summary of the root cause and recommended action}

    ### Root Cause
    {one-paragraph description with evidence}

    **Confidence:** {High|Medium|Low} — {justification}

    ### Blast Radius
    {what else is affected}

    ### Suggested Fix
    1. {step 1}
    2. {step 2}
    3. {step 3} (if applicable)

    ### Evidence
    {key log excerpts, API responses, or code references}

    ### Related
    {commits, PRs, issues, or "None found"}

    ---
    <sub>🔍 [Investigation Run #{this_run_number}]({this_run_url}) · Dispatched by health check · {correlation_id}</sub>
```

---

## Guidelines

- **Be factual**: Every claim must be backed by evidence from API responses, logs, or code.
- **Don't hallucinate**: If you cannot determine the root cause, say so honestly. A "Low confidence" finding with honest uncertainty is better than a fabricated "High confidence" answer.
- **Be concise**: The investigation report appears inline in the health dashboard. Keep it focused — 1-2 paragraphs for root cause, 1 paragraph for blast radius, numbered list for fixes.
- **Include source evidence**: Quote specific error messages, log lines, or commit SHAs. Use code blocks for log excerpts.
- **Check recent commits**: For pipeline and quality findings, always check commits between the last successful state and the current failure.
- **Cross-reference**: Look for related open issues or PRs that might already be tracking this problem.
- **Time-box yourself**: If evidence is insufficient after reasonable investigation, report what you found with appropriate confidence level rather than spiraling.
