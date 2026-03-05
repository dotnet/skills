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

network:
  allowed:
    - defaults
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

### 3.3 Update the Issue Body

Use `update-issue` to write the updated issue body with the linked investigation results.

Only update the issue if at least one change was made (a `🔄 Dispatched` row was resolved, or a new investigation result was linked).

---

## Step 4: Check for Newly Resolved Findings

### 4.1 Load Current Fingerprints

Load the current fingerprint set from `cache-memory` key `health-check-fingerprints`. This contains all findings the health check currently tracks.

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

---

## Step 5: Hide Stale Comments

Use `hide-comment` to collapse stale comments. Hidden comments remain accessible
but are collapsed in the GitHub UI with a reason label. Apply the following
retention policy:

### 5.1 Daily Overview Comments

Hide daily overview comments (`## 📋 Health Check —`) older than **7 days** with reason `outdated`.

```
Age = now - comment.created_at
If Age > 7 days → hide-comment(node_id, reason: "outdated")
```

### 5.2 Investigation Comments — Age-Based

Hide investigation comments (`## 🔍 Investigation:`) older than **7 days** with reason `outdated`.

### 5.3 Investigation Comments — Resolved Findings

Hide investigation comments for findings that have been **resolved** (finding_id is NOT in the current fingerprint set from `cache-memory`), regardless of age, with reason `resolved`. These investigations are no longer relevant since the underlying issue is fixed.

**Exception:** Do NOT hide investigation comments less than 24 hours old, even if the finding is resolved. This gives people time to read the investigation before it's cleaned up.

### 5.4 Hide Order

Process hides in this priority order:
1. Resolved investigation comments (oldest first) — reason: `resolved`
2. Age-expired investigation comments (oldest first) — reason: `outdated`
3. Age-expired daily overview comments (oldest first) — reason: `outdated`

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

- **Be precise with comment parsing**: The comment format is well-defined (see the investigation worker template). Match the exact patterns — don't be fuzzy.
- **Preserve the issue body structure**: When updating the issue body, keep ALL sections intact. Only modify the Investigation Results table rows and any resolved-finding annotations. Do not rewrite sections you don't need to change.
- **Don't hide "Other" comments**: Only hide comments that match the Investigation or Daily overview patterns. Human comments, bot reactions, etc. must be preserved.
- **Idempotent**: Running this workflow twice should produce the same result. If investigation results are already linked, don't re-link them. If comments are already hidden, they won't appear in the API results (collapsed).
- **Graceful degradation**: If the issue body doesn't contain an Investigation Results section (e.g., first run before any investigations), skip Step 3 and proceed to hiding stale comments.
- **Logging**: For each hide operation, note the comment category, age, and reason.
