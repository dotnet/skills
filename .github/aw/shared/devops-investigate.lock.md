<!-- AUTO-GENERATED — DO NOT EDIT -->
<!-- Source: devops-health-investigate.md knowledge compilation -->

# DevOps Investigation — Compiled Knowledge

This document contains category-specific investigation playbooks, root-cause patterns, and remediation templates for the DevOps Health Investigation worker agent.

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
   - Compare commits between them:
     ```
     GET /repos/{owner}/{repo}/compare/{success_sha}...{failure_sha}
     ```
   - Look for changes to: workflow YAML files, build scripts, `global.json`, dependency files, the code being tested.

6. **Check if the failure is in repo code or a GitHub Action version update**:
   - Compare action versions between the failing and last successful workflow YAML
   - Check if a new action version was released recently

7. **Determine root cause** with confidence level:
   - **High**: Error message explicitly identifies the cause (e.g., "SDK version 9.0.200 not found")
   - **Medium**: Timing strongly correlates with a specific commit
   - **Low**: No clear evidence — multiple possibilities

8. **Generate 1–3 specific remediation steps**:
   - Include exact file paths, version numbers, or commands
   - Order by recommended priority

9. **Check for existing tracking**:
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

## 2. Quality Investigation Playbook

When `finding_type == "quality"`:

### Step-by-Step Protocol

1. **Fetch benchmark data** for the affected component (last 14+ entries):
   ```
   GET https://raw.githubusercontent.com/{owner}/{repo}/gh-pages/data/{component}.json
   ```

2. **Analyze the trend**:
   - Extract the time series for the affected scenario's "Skilled Quality" bench
   - Is this a **sudden drop** (step function) or **gradual degradation** (slope)?
   - Sudden drops (>2 points between consecutive entries) typically indicate a specific triggering change
   - Gradual degradation may indicate model drift or accumulating prompt issues

3. **For anomaly flags** (`notActivated`, `timedOut`, `testOverfitted`, etc.):
   a. Identify which flag(s) are set on the bench entry
   b. For `notActivated`:
      - The skill failed to activate — the model didn't use any skill-provided context
      - Check the skill definition file (`src/{component}/skills/{skill}/SKILL.md`) for syntax or trigger issues
      - Check if the skill's `description` field changed recently
      - Check if the test prompt still aligns with the skill's trigger keywords
   c. For `timedOut`:
      - The evaluation exceeded the time limit
      - Check recent complexity changes in the skill or test
      - Check if the timeout threshold was changed
   d. For other/unknown flags:
      - Describe the flag name and its value
      - Check `eng/dashboard/generate-benchmark-data.ps1` for context on when this flag is set
      - Report the flag as-is with whatever context is available

4. **For regression** (quality score dropped):
   - Identify the exact entry where quality dropped (compare consecutive entries)
   - Get the commit SHA from the regression entry's `commit` field
   - Check what changed in that commit:
     ```
     GET /repos/{owner}/{repo}/commits/{sha}
     ```
   - Look for changes to: skill definition, test definition, shared knowledge files, prompt templates

5. **For high variance**:
   - Compute the range (max - min) across recent entries
   - Check if variance is skill-specific or affects multiple skills (model instability)
   - Check if test prompts are ambiguous (allowing valid but different approaches)

6. **For no-uplift** (skilled ≤ vanilla):
   - Compare the skill's prompt additions to the vanilla baseline
   - Check if the skill knowledge is relevant to the test scenario
   - Check if the skill is triggering correctly (not `notActivated`)

7. **Check recent skill/test changes**:
   ```
   GET /repos/{owner}/{repo}/commits?path=src/{component}/skills/{skill}&per_page=5
   GET /repos/{owner}/{repo}/commits?path=src/{component}/tests/{skill}&per_page=5
   ```

8. **Determine root cause** with confidence level.

9. **Generate remediation steps** specific to skill quality:
   - For activation issues: suggest trigger keyword adjustments
   - For regression: identify the specific change and suggest reverting or fixing
   - For variance: suggest test prompt clarification or eval parameter tuning

### Common Quality Root Causes

| Pattern | Typical Cause | Remediation |
|---------|---------------|-------------|
| `notActivated` on all scenarios of a skill | Skill description doesn't match test prompts | Review skill trigger keywords vs test prompts |
| `notActivated` on one scenario only | Test prompt is too different from skill scope | Adjust test prompt or broaden skill description |
| Sudden quality drop after commit X | Skill definition or knowledge was changed | Review diff of commit X; consider partial revert |
| Gradual quality decline over 7+ days | Model behavior drift or prompt degradation | Re-evaluate skill knowledge for staleness |
| Skilled ≤ Vanilla consistently | Skill knowledge may confuse rather than help | Review skill content for misleading information |
| `timedOut` on complex scenarios | Scenario requires too many tool calls or reasoning steps | Simplify scenario or increase timeout |
| High variance (stddev > 2.0) | Ambiguous test prompt allows multiple valid approaches | Tighten test prompt expectations |

---

## 3. PR Investigation Playbook

When `finding_type == "pr"`:

### Step-by-Step Protocol

1. **Fetch PR details**:
   ```
   GET /repos/{owner}/{repo}/pulls/{pr_number}
   ```
   Extract: title, author, created_at, updated_at, draft status, labels, body summary.

2. **Fetch PR timeline** (reviews, comments, status changes):
   ```
   GET /repos/{owner}/{repo}/pulls/{pr_number}/reviews
   GET /repos/{owner}/{repo}/issues/{pr_number}/comments
   ```

3. **For no-review PRs**:
   - Check if CODEOWNERS would auto-assign reviewers
   - Check `git blame` or recent contributors to the changed files for potential reviewers
   - Look at the PR size (files changed, lines) — large PRs may discourage review

4. **For stale PRs**:
   - Check last activity date (any comment or push)
   - Check if the author is still active (recent commits/PRs)
   - Check if there are related issues that are still relevant

5. **For failing checks**:
   ```
   GET /repos/{owner}/{repo}/commits/{head_sha}/check-runs
   ```
   - Identify which checks fail
   - Cross-reference with known pipeline issues from the same health check
   - Determine if check failures are PR-specific or repo-wide

6. **Provide actionable summary**:
   - What's blocking the PR
   - Who should review
   - Whether the PR is still relevant

---

## 4. Infrastructure Investigation Playbook

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
   ```
   GET /repos/{owner}/{repo}/pages/builds
   ```
   - Read the latest build log
   - Identify the failure cause (build error, quota, DNS, etc.)

---

## 5. Resource Investigation Playbook

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

## 6. Report Format

All investigation results follow this template:

```markdown
🔍 **Investigation Complete** — [Worker Run #{run_number}]({run_url})

**Root cause:** {Clear, evidence-based description of what went wrong and why.
Include specific error messages, commit SHAs, or file paths as evidence.}

**Confidence:** {High|Medium|Low} — {One sentence justifying the confidence level}

**Blast radius:** {What else is affected by this issue. Be specific about which
components, workflows, or metrics are impacted.}

**Suggested fix:**
1. {Most recommended action — include specific file, line, or command}
2. {Alternative action if applicable}
3. {Additional step if needed}

**Related:** {List related commits (with SHA + author), PRs (with #number), or
issues (with #number). Say "None found" if nothing is related.}
```

### Confidence Level Guidelines

| Level | Criteria | Example |
|-------|----------|---------|
| **High** | Direct evidence links cause to effect | Error log says "SDK 9.0.200 not found"; commit changed SDK version |
| **Medium** | Strong circumstantial correlation | Quality dropped on the same day a skill file was modified |
| **Low** | Possible but speculative | Multiple recent changes could explain the issue; no clear winner |

---

## 7. Common Cross-Category Patterns

These patterns span multiple check categories and may help identify systemic issues:

| Pattern | Indicates |
|---------|-----------|
| Pipeline failure + stale benchmark data | Pipeline is blocking data publication |
| Multiple quality regressions on same date | Common causal commit |
| PR failing checks + same check failing on main | Repo-wide issue, not PR-specific |
| `notActivated` + recent skill definition change | Skill trigger broke |
| Eval duration spike + new skill/scenario added | Expected growth, not a bug |
| Cost increase + new scheduled workflow | Expected growth, not waste |
