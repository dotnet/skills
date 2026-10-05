<!-- AUTO-GENERATED — DO NOT EDIT -->
<!-- Source: devops-health-investigate.md knowledge compilation -->

# DevOps Investigation — Compiled Knowledge

This document contains category-specific investigation playbooks, root-cause patterns, and remediation templates for the DevOps Health Investigation worker agent. Focused on pipeline, infrastructure, and resource investigations only.

---

## 1. Pipeline Investigation Playbook

When `finding_type == "pipeline"`:

### Step-by-Step Protocol

1. **Fetch the failed run** using `resource_url`:
   ```
   GET /repos/{owner}/{repo}/actions/runs/{run_id}
   ```
   Extract: workflow name, conclusion, created_at, updated_at, triggering_actor, head_sha.

2. **Identify the failed job and step**:
   ```
   GET /repos/{owner}/{repo}/actions/runs/{run_id}/jobs
   ```
   Find the job with `conclusion: failure`. Within that job, find the step with `conclusion: failure`.

3. **Read the failed step's logs** (last 200 lines):
   ```
   GET /repos/{owner}/{repo}/actions/jobs/{job_id}/logs
   ```
   Extract error messages, exception types, exit codes. Look for patterns:
   - `.NET SDK version mismatch` → check `global.json`
   - `process exited with code 1` → build/test failure
   - `Error: ENOMEM` or `killed` → resource exhaustion
   - `rate limit` or `403` → API throttling
   - `timeout` → long-running operation exceeded limit

4. **Fetch the last 5 successful runs** of the same workflow:
   ```
   GET /repos/{owner}/{repo}/actions/workflows/{workflow_id}/runs?branch=main&status=success&per_page=5
   ```

5. **Compare: what changed between last success and this failure?**
   - Get the `head_sha` of the last successful run
   - Get the `head_sha` of the failed run
   - Use `list_commits` on the default branch and bound the result to commits
     after the successful SHA through the failed SHA. Use `get_commit` for each
     candidate SHA.
   - Look for changes to: workflow YAML files, build scripts, `global.json`, dependency files, the code being tested.
   - If the bounded commit list does not contain both SHAs, state that the
     change range is incomplete and lower confidence. Do not invent a compare
     result.

6. **Identify the PR that introduced the breaking change**:
   - For each suspect commit, use `search_pull_requests` with the exact SHA.
   - Verify candidates with `pull_request_read`: use method `get` for metadata,
     `get_files` for changed files, and `get_diff` for the patch.
   - Record the PR number, title, author, and merge date only for a verified
     match.
   - This helps attribute the regression and identify who can help fix it

7. **Check if the failure is in repo code or a GitHub Action version update**:
   - Compare action versions between the failing and last successful workflow YAML
   - Check if a new action version was released recently

8. **Determine root cause** with confidence level:
   - **High**: Error message explicitly identifies the cause (e.g., "SDK version 9.0.200 not found")
   - **Medium**: Timing strongly correlates with a specific commit
   - **Low**: No clear evidence — multiple possibilities

9. **Generate 1–3 specific remediation steps**:
   - Include exact file paths, version numbers, or commands
   - Order by recommended priority

10. **Check for existing tracking**:
   ```
   GET /repos/{owner}/{repo}/issues?state=open&labels=bug
   ```
   Search for issues mentioning the same workflow or error.

### Common Pipeline Root Causes

| Pattern | Typical Cause | Remediation |
|---------|---------------|-------------|
| `SDK version not found` | `global.json` pins a version not installed on the runner | Update `global.json` or add `setup-dotnet` step |
| `process exited with code 1` in test step | Test failure (assertion or runtime error) | Check test output for specific failure |
| `Error: HttpError: rate limit exceeded` | GitHub API rate limiting | Add retry logic or reduce API calls |
| `The operation was canceled` | Timeout (default 360 min for Actions) | Optimize the step or increase timeout |
| `No space left on device` | Runner disk full (14 GB limit) | Add cleanup steps or reduce artifact size |
| Action `X` failed with `Node.js 16 actions are deprecated` | Action needs version update | Update action to latest version |

---

## 2. Infrastructure Investigation Playbook

When `finding_type == "infra"`:

### Step-by-Step Protocol

1. **Audit the configuration**:
   - For missing files (CODEOWNERS, dependabot): confirm absence and explain impact
   - For relaxed settings: read the config file and explain what the setting does

2. **Check if intentional**:
   - Search for issues or PRs that discuss the configuration choice
   - Check commit history of the config file for context

3. **Compare with best practices**:
   - Reference GitHub's recommended security settings
   - Note any compliance or security implications

4. **For Pages deployment failures**:
   - Use `actions_list` to find the `pages-build-deployment` workflow runs.
   - Use `actions_get` to verify the latest completed run and its conclusion.
   - Use the run's jobs and `get_job_logs` for the failed job.
   - Identify the failure cause from Actions evidence. Do not claim Pages API
     build, quota, or DNS evidence because that API is not exposed.

---

## 3. Resource Investigation Playbook

When `finding_type == "resource"`:

### Step-by-Step Protocol

1. **Gather usage data**:
   ```
   GET /repos/{owner}/{repo}/actions/runs?per_page=100
   ```
   - Compute daily/weekly compute hours by summing run durations
   - Break down by workflow

2. **Identify cost drivers**:
   - Which workflows consume the most time?
   - Has a new workflow been added recently?
   - Did an existing workflow's duration increase?

3. **For eval duration warnings**:
   - Check if the number of skills/scenarios being evaluated increased
   - Check if individual scenario duration increased
   - Look for parallelism changes in the workflow configuration

4. **Provide optimization suggestions**:
   - Can any workflows be consolidated?
   - Are there unnecessary re-runs (e.g., missing `concurrency` groups)?
   - Can caching reduce execution time?

---

## 4. Report Format

All investigation results follow this template:

```markdown
## 🔍 Investigation: {canonical_title derived from trusted metadata}

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
3. {step 3, if applicable}

### Remediation Status
Report-only. {Trusted evidence, proposed change, validation plan, and owner,
or why the available evidence cannot verify an exact fix.}

### Evidence
{key log excerpts, API responses, or code references}

### Related
{commits, PRs, issues, or "None found"}

---
<sub>🔍 [Investigation Run #{run_number}]({run_url}) · Dispatched by health check · {correlation_id}</sub>
```

### Confidence Level Guidelines

| Level | Criteria | Example |
|-------|----------|---------|
| **High** | Direct evidence links cause to effect | Error log says "SDK 9.0.200 not found"; commit changed SDK version |
| **Medium** | Strong circumstantial correlation | Pipeline failed right after a config change was merged |
| **Low** | Possible but speculative | Multiple recent changes could explain the issue; no clear winner |

---

## 5. Common Cross-Category Patterns

These patterns span multiple check categories and may help identify systemic issues:

| Pattern | Indicates |
|---------|-----------|
| Pipeline failure + Pages deployment failure | Infrastructure-wide issue |
| Eval duration spike + new workflow added | Expected growth, not a bug |
| Cost increase + new scheduled workflow | Expected growth, not waste |
| Multiple pipeline failures on same day | Common infrastructure root cause |
