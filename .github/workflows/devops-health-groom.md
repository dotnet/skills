---
name: "DevOps Health — Groom Dashboard"
description: >
  Runs ~3 hours after the daily health check to groom the pinned health
  dashboard issue: links investigation results into the issue body,
  prunes stale comments older than 7 days, and marks resolved findings.

on:
  schedule:
    - cron: "0 6 * * *"  # 06:00 UTC daily (3h after health check)
  workflow_dispatch:

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

# Don't run scheduled triggers on forked repositories — forks lack the
# secrets and context required, and scheduled runs would consume the
# fork owner's minutes.
if: ${{ !(github.event_name == 'schedule' && github.event.repository.fork) }}

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
  - ../aw/shared/devops-health.lock.md

tools:
  github:
    toolsets: [repos, issues, pull_requests, actions]
  bash: ["cat", "grep", "head", "tail", "jq", "date", "sort"]

safe-outputs:
  update-issue:
    target: "*"
    max: 1
  hide-comment:
    max: 50
    allowed-reasons: [outdated, resolved]
  noop:
    report-as-issue: false

network:
  allowed:
    - defaults

timeout-minutes: 60
---

# DevOps Health — Groom Dashboard

You are a dashboard grooming agent. You run after the daily health check and its dispatched investigations have had time to complete. Your job is to:

1. **Link investigation results** into the issue body so the description is self-contained
2. **Hide stale comments** to keep the issue manageable (collapsed with reason)
3. **Mark resolved investigations** so readers know what's still relevant

---

## Step 1: Find the Health Dashboard Issue

Search for open issues with label `devops-health`:
```
GET /repos/{owner}/{repo}/issues?labels=devops-health&state=open&per_page=5
```
Use the most recently created one. If none exist, call `noop` with message "No health dashboard issue found — nothing to groom" and stop.

Record the `issue_number` and current issue `body`.

---

## Step 2: Fetch All Comments

```
GET /repos/{owner}/{repo}/issues/{issue_number}/comments?per_page=100
```

Paginate if needed (follow `Link` header). Collect every comment with:
- `id` (numeric REST comment ID)
- `node_id` (GraphQL node ID, e.g. `IC_kwDOABCD…` — required by `hide-comment`)
- `html_url` (link for the issue body)
- `body` (content to parse)
- `created_at` (timestamp for age checks)

### 2.1 Classify Comments

Parse each comment into one of these categories:

| Category | Detection Rule |
|----------|----------------|
| **Investigation** | Body starts with `## 🔍 Investigation:` |
| **Daily overview** | Body starts with `## 📋 Health Check —` |
| **Other** | Anything else (leave untouched) |

For each **Investigation** comment, extract:
- `finding_id` from the `**Finding ID:** \`{id}\`` line
- `executive_summary` from the `**Executive Summary:**` line (everything after the label)
- `correlation_id` from the `**Correlation:**` line
- `comment_url` = the comment's `html_url`
- `comment_id` = the comment's `id`
- `comment_node_id` = the comment's `node_id`
- `created_at` = the comment's timestamp

For each **Daily overview** comment, extract:
- `date` from the heading `## 📋 Health Check — {date}`
- `comment_id` = the comment's `id`
- `comment_node_id` = the comment's `node_id`
- `created_at` = the comment's timestamp

---

## Step 3: Link Investigation Results into Issue Body

### 3.1 Parse the Current Issue Body

Find the `## 🔍 Investigation Results` section in the issue body. This section contains a markdown table with rows like:

```
| {finding_title} | {severity} | 🔄 Dispatched | [Workflow Run]({url}) |
```

### 3.2 Build the Updated Table

For each row in the Investigation Results table:
1. Determine the `finding_id` for this row. Match by finding title or by checking the fingerprint from the current health check state in `cache-memory`.
2. Look up the `finding_id` in the investigation comments collected in Step 2.
3. If a matching investigation comment exists:
   - Change the status from `🔄 Dispatched` to `✅ Done`
   - Replace the Result cell with `[{executive_summary}]({comment_url})`
4. If no matching investigation comment exists yet, leave the row unchanged.

Also check for investigation comments that correspond to findings in the **📌 Existing Findings** or **🆕 New Findings** sections (from previous runs). Add rows for those too if they aren't already in the table.

### 3.3 Hold Changes (Do Not Update Yet)

Do **not** call `update-issue` yet. Keep the modified issue body in memory — Step 4 will make further edits to the same body before a single combined `update-issue` call.

---

## Step 4: Check for Newly Resolved Findings

### 4.1 Derive Current Fingerprints from Issue Body

Extract the set of currently active findings by parsing the issue body (already loaded in Step 1):
- **🆕 New Findings** section → these are current
- **📌 Existing Findings** section → these are current
- Extract the `Fingerprint:` line from each finding's detail block

The union of new + existing fingerprints forms the current active set. Findings listed under **✅ Resolved Since Yesterday** are NOT current.

### 4.2 Cross-Reference Investigation Comments

For each investigation comment found in Step 2:
1. Check if the `finding_id` is still present in the current fingerprint set.
2. If the `finding_id` is **NOT** in the current fingerprints → the finding has been resolved since the investigation was posted.
3. For these resolved findings, check if they are already marked in the "✅ Resolved Since Yesterday" section or if the investigation table already shows them as resolved.

### 4.3 Mark Resolved Investigations in the Issue Body

In the Investigation Results table, for findings whose investigation is complete AND the finding is now resolved:
- Change status from `✅ Done` to `✅ Resolved`
- Keep the link to the investigation comment (still useful for historical context until pruned)

Additionally, in the **📌 Existing Findings** section, if any finding that was previously `📌 EXISTING` is no longer in the current fingerprint set, annotate it with `(resolved {date})`.

### 4.4 Write the Updated Issue Body

Now that both Step 3 (linking investigation results) and Step 4 (marking resolved findings) have been applied to the in-memory issue body, make a **single** `update-issue` call with the combined changes. You **MUST** set `operation: "replace"` so the body is overwritten (the default operation is `append`, which would duplicate the entire body).

**Before calling `update-issue`**, run the body-length sanity check (see Guidelines). If the check fails, **abort the entire workflow** — skip `update-issue`, skip Step 5 (hide comments), and call `noop` with the error.

Only call `update-issue` if at least one change was made across Steps 3 and 4. If nothing changed, skip the call.

---

## Step 5: Hide Stale Comments

Use `hide-comment` to collapse stale comments. Hidden comments remain accessible
but are collapsed in the GitHub UI with a reason label. Apply the following
retention policy:

### 5.1 Daily Overview Comments

Hide daily overview comments (`## 📋 Health Check —`) older than **7 days** with reason `OUTDATED`.

```
Age = now - comment.created_at
If Age > 7 days → hide-comment(node_id, reason: "OUTDATED")
```

### 5.2 Investigation Comments — Age-Based

Hide investigation comments (`## 🔍 Investigation:`) older than **7 days** with reason `OUTDATED`.

### 5.3 Investigation Comments — Resolved Findings

Hide investigation comments for findings that have been **resolved** (finding_id is NOT in the current active fingerprint set derived from the issue body in Step 4.1), regardless of age, with reason `RESOLVED`. These investigations are no longer relevant since the underlying issue is fixed.

**Exception:** Do NOT hide investigation comments less than 24 hours old, even if the finding is resolved. This gives people time to read the investigation before it's cleaned up.

### 5.4 Hide Order

Process hides in this priority order:
1. Resolved investigation comments (oldest first) — reason: `RESOLVED`
2. Age-expired investigation comments (oldest first) — reason: `OUTDATED`
3. Age-expired daily overview comments (oldest first) — reason: `OUTDATED`

Use the `hide-comment` safe-output for each operation. The `node_id` field is
required (GraphQL node ID starting with `IC_kwDO…`). Include the reason.

### 5.5 Safety Limits

- Maximum 50 hides per run (safe-output budget)
- If more than 50 comments qualify for hiding, prioritize: resolved investigations first, then oldest comments first
- Log the count of skipped hides if the budget is exhausted
- Hidden comments remain on the issue (collapsed); they are NOT deleted

---

## Step 6: Summary

After completing all steps, if no `update-issue` or `hide-comment` calls were made, call `noop` with a summary message:

```
No grooming needed — all investigation results already linked, no stale comments found.
```

If changes were made, the summary is implicit in the safe-output calls. Do NOT call `noop` if you already made other safe-output calls.

---

## Guidelines

- **CRITICAL — Use `operation: "replace"`**: When calling `update-issue`, you **MUST** set `operation: "replace"`. The default operation is `append`, which adds the body after the existing content and will duplicate the entire issue. Since you are providing the complete updated body, always use `replace`.
- **CRITICAL — Safe output body must be inline**: When calling `update-issue`, the `body` field must contain the **complete, literal issue body text**. NEVER write the body to a file and use a shell reference like `$(cat file.txt)` — safe outputs are literal JSON strings, not shell-evaluated. The body must be passed directly as the string value.
- **CRITICAL — Body length sanity check**: Before calling `update-issue`, verify the new body is at least 80% the length of the original body. If it is significantly shorter, something went wrong — **stop immediately**: do NOT call `update-issue`, do NOT call `hide-comment`, and call `noop` with an error message describing the length mismatch. This check runs before any safe-output calls, so `noop` is always safe here. The health check body is typically 3000–8000 characters.
- **Minimal edits only**: You are a groomer, not a rewriter. Only change: (a) investigation table rows (status + link), (b) resolved-finding annotations. Copy all other sections **byte-for-byte** from the original body. Do not reformat, re-wrap, or reorganize sections you are not changing.
- **Be precise with comment parsing**: The comment format is well-defined (see the investigation worker template). Match the exact patterns — don't be fuzzy.
- **Preserve the issue body structure**: When updating the issue body, keep ALL sections intact. Only modify the Investigation Results table rows and any resolved-finding annotations. Do not rewrite sections you don't need to change.
- **Don't hide "Other" comments**: Only hide comments that match the Investigation or Daily overview patterns. Human comments, bot reactions, etc. must be preserved.
- **Idempotent**: Running this workflow twice should produce the same result. If investigation results are already linked, don't re-link them. If comments are already hidden, they won't appear in the API results (collapsed).
- **Graceful degradation**: If the issue body doesn't contain an Investigation Results section (e.g., first run before any investigations), skip Step 3 and proceed to hiding stale comments.
- **No intermediate files**: Do all work in memory. Do NOT write intermediate scripts, JSON files, or body text files. Parse API responses with `jq` inline and hold the issue body as a string variable.
