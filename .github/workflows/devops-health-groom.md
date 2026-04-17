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

imports:
  - ../aw/shared/devops-health.lock.md

tools:
  github:
    toolsets: [repos, issues, actions]
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

## Step 2: Fetch Recent Comments

Compute a `since` timestamp equal to **30 days ago** (ISO-8601 format, e.g. `2026-03-16T00:00:00Z`). This covers the 28-day P4 hard age cutoff plus a 2-day buffer, ensuring all comments within the retention window are fetched — including older investigations whose findings are still active.

```
GET /repos/{owner}/{repo}/issues/{issue_number}/comments?per_page=100&since={since_timestamp}
```

The `since` parameter filters to comments created or updated after the timestamp, which keeps the result set bounded.

**You MUST paginate**: If the response contains a `Link` header with `rel="next"`, you MUST fetch subsequent pages until no `rel="next"` link is present. Failure to paginate means investigation comments may be missed, which is the primary failure mode of this workflow.

Collect every comment with:
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

Look for the `## 🔍 Investigation Results` section in the issue body. This section, when present, contains a markdown table with rows like:

```
| {finding_title} | {severity} | 🔄 Dispatched | [Workflow Run]({url}) |
```

**If the section is missing** (the health check agent sometimes omits it), you MUST
create it. Do NOT skip this step — creating the section is the primary purpose of
this workflow. Proceed to Step 3.2 with an empty table.

### 3.2 Build the Updated Table

**If the Investigation Results section already exists** in the issue body:

For each row in the existing Investigation Results table:
1. Determine the `finding_id` for this row. Match by comparing the finding title in the table row against the `finding_id` or heading title in each investigation comment.
2. Look up the `finding_id` in the investigation comments collected in Step 2.
3. If a matching investigation comment exists:
   - Change the status from `🔄 Dispatched` to `✅ Done`
   - Replace the Result cell with `[{executive_summary}]({comment_url})`
4. If no matching investigation comment exists yet, leave the row unchanged.

**If the Investigation Results section does NOT exist** in the issue body:

You must INSERT it. Build the section from scratch using the investigation
comments collected in Step 2:

1. For each investigation comment, create a table row:
   ```
   | {finding_title from comment heading} | {severity from comment} | ✅ Done | [{executive_summary}]({comment_url}) |
   ```
2. Wrap the rows in the standard section structure:
   ```markdown
   ## 🔍 Investigation Results

   > Deep investigations are dispatched for new critical/warning findings.
   > The [grooming workflow](../workflows/devops-health-groom.md) links results ~3 hours after this run.

   | Finding | Severity | Status | Result |
   |---------|----------|--------|--------|
   {rows}
   ```
3. Insert this section into the issue body **immediately before** the first of
   these sections (whichever appears first): `## ✅ Resolved`, `## 📌 Existing`,
   `## 📊 Trends`. If none of those headings are found, append the section at
   the end of the body (before the `<sub>` footer if present).

**In both cases** (section existed or was created), also check for investigation
comments that correspond to findings in the **📌 Existing Findings** or **🆕 New
Findings** sections (from previous runs). Add rows for those too if they aren't
already in the table.

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

### 4.4 Write the Updated Issue Body

Now that both Step 3 (linking investigation results) and Step 4 (marking resolved investigations) have been applied to the Investigation Results table, write **only the `## 🔍 Investigation Results` section** using a **single** `update-issue` call with `operation: "replace-island"`.

The `replace-island` operation replaces only the content between the `## 🔍 Investigation Results` heading and the next `##`-level heading (or end of body), leaving every other section untouched. This eliminates the risk of accidentally truncating or reformatting the issue body.

The `body` field must contain **only** the Investigation Results island — starting with `## 🔍 Investigation Results` and ending just before the next section heading. Example:

```markdown
## 🔍 Investigation Results

> Deep investigations are dispatched for new critical/warning findings.
> The [grooming workflow](../workflows/devops-health-groom.md) links results ~3 hours after this run.

| Finding | Severity | Status | Result |
|---------|----------|--------|--------|
| ... | ... | ✅ Done | [summary](url) |
```

Only call `update-issue` if at least one change was made across Steps 3 and 4. If nothing changed, skip the call.

---

## Step 5: Hide Stale Comments

Use `hide-comment` to collapse stale comments. Hidden comments remain accessible
but are collapsed in the GitHub UI with a reason label.

**Minimum age safeguard:** NEVER hide any comment less than **72 hours** old,
regardless of which rule matches. This gives people time to read investigations
before they are cleaned up.

Apply the following retention rules in priority order:

### 5.1 P1 — Daily Summary Comments (> 7 days)

Hide daily overview comments (`## 📋 Health Check —`) older than **7 days** with reason `OUTDATED`.

```
Age = now - comment.created_at
If Age > 7 days → hide-comment(node_id, reason: "OUTDATED")
```

### 5.2 P2 — Already-Hidden / Resolved Investigation Comments (> 7 days)

Hide investigation comments (`## 🔍 Investigation:`) older than **7 days** that
have already been collapsed (hidden) in a previous grooming run, or whose
`finding_id` is NOT in the current active fingerprint set (i.e. the finding is
resolved). Use reason `RESOLVED` for resolved findings, `OUTDATED` for others.

### 5.3 P3 — Unreferenced Investigation Comments (> 7 days)

Hide investigation comments older than **7 days** whose `finding_id` does **not**
appear anywhere in the current issue body's `## 🔍 Investigation Results` table.
These investigations are orphaned — not linked from the dashboard. Use reason
`OUTDATED`.

### 5.4 P4 — Hard Age Cutoff (> 28 days)

Hide **any** bot comment (`github-actions[bot]` author) older than **28 days**,
regardless of type or status, with reason `OUTDATED`. This is a catch-all to
prevent unbounded comment accumulation.

**Never hide human comments** — only comments authored by `github-actions[bot]`.

### 5.5 Hide Order

Process hides in this priority order:
1. P2 — Resolved investigation comments (oldest first) — reason: `RESOLVED`
2. P3 — Unreferenced investigation comments (oldest first) — reason: `OUTDATED`
3. P1 — Age-expired daily overview comments (oldest first) — reason: `OUTDATED`
4. P4 — Hard age cutoff (oldest first) — reason: `OUTDATED`

Use the `hide-comment` safe-output for each operation. The `node_id` field is
required (GraphQL node ID starting with `IC_kwDO…`). Include the reason.

### 5.6 Safety Limits

- Maximum 50 hides per run (safe-output budget)
- If more than 50 comments qualify for hiding, prioritize: resolved investigations first, then oldest comments first
- Log the count of skipped hides if the budget is exhausted
- Hidden comments remain on the issue (collapsed); they are NOT deleted
- **Actual deletion** is handled by the separate [`devops-health-cleanup.yml`](devops-health-cleanup.yml) workflow, which runs weekly and permanently removes bot comments matching the same P1–P4 rules. This groomer only hides (collapses) comments.

---

## Step 6: Summary

After completing all steps, if no `update-issue` or `hide-comment` calls were made, call `noop` with a summary message:

```
No grooming needed — all investigation results already linked, no stale comments found.
```

If changes were made, the summary is implicit in the safe-output calls. Do NOT call `noop` if you already made other safe-output calls.

---

## Guidelines

- **CRITICAL — Use `operation: "replace-island"`**: When calling `update-issue`, you **MUST** set `operation: "replace-island"`. This replaces only the `## 🔍 Investigation Results` section in the issue body, leaving all other sections untouched. The `body` field must contain only the Investigation Results section content (from the `## 🔍 Investigation Results` heading up to but not including the next `##`-level heading). Do NOT pass the full issue body — `replace-island` handles scoping automatically.
- **CRITICAL — Safe output body must be inline**: When calling `update-issue`, the `body` field must contain the **literal section text**. NEVER write the body to a file and use a shell reference like `$(cat file.txt)` — safe outputs are literal JSON strings, not shell-evaluated. The body must be passed directly as the string value.
- **Minimal edits only**: You are a groomer, not a rewriter. Only change: (a) investigation table rows (status + link), (b) resolved-finding annotations. Copy all other sections **byte-for-byte** from the original body. Do not reformat, re-wrap, or reorganize sections you are not changing.
- **Be precise with comment parsing**: The comment format is well-defined (see the investigation worker template). Match the exact patterns — don't be fuzzy.
- **Preserve the issue body structure**: When updating the issue body, keep ALL sections intact. Only modify the Investigation Results table rows and any resolved-finding annotations. Do not rewrite sections you don't need to change.
- **Don't hide human comments**: Never hide comments authored by humans. For bot comments (`github-actions[bot]`), P1–P3 only target Investigation and Daily overview patterns. P4 (hard age cutoff > 28 days) may hide any bot comment regardless of pattern. Never hide human comments, bot reactions from humans, etc.
- **Idempotent**: Running this workflow twice should produce the same result. If investigation results are already linked, don't re-link them. If comments are already hidden, they won't appear in the API results (collapsed).
- **Create missing sections**: If the issue body doesn't contain a `## 🔍 Investigation Results` section, **create it** from investigation comments (see Step 3). Do NOT silently skip linking — this is the groomer's primary job. Only skip Step 3 if there are zero investigation comments to link. When creating a missing section, use `operation: "replace-island"` — this will insert the section at the appropriate location.
- **No intermediate files**: Do all work in memory. Do NOT write intermediate scripts, JSON files, or body text files. Parse API responses with `jq` inline and hold the issue body as a string variable.
- **Pagination is mandatory**: Always follow `Link: <…>; rel="next"` headers when fetching comments. Even with the `since` parameter, the result set can exceed 100 comments — if you only fetch page 1, you will miss recent investigation comments and silently fail to link them.
