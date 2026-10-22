---
name: "Weekly Evaluation Insights"
description: >
  Publishes an evidence-based weekly report on evaluation reliability,
  treatment-versus-control differentiation, runtime, resource use, and
  plugin or skill grooming priorities.
intent: Keep evaluation suites reliable, current, fast, and able to distinguish treatment from control.

on:
  permissions: {}
  schedule:
    - cron: "0 17 * * 5" # Friday 17:00 UTC
  workflow_dispatch:

if: ${{ (!(github.event_name == 'schedule' && github.event.repository.fork)) }}

concurrency:
  group: gh-aw-weekly-eval-insights
  cancel-in-progress: false
  queue: max

model: ${{ vars.GH_AW_MODEL_AGENT_COPILOT || vars.GH_AW_DEFAULT_MODEL_COPILOT || 'gpt-5.6-sol' }}

graders: {}

permissions:
  contents: read
  actions: read
  issues: read

tools:
  github:
    mode: gh-proxy
    toolsets: [repos, issues, actions]
  bash: ["cat"]
  edit: false

safe-outputs:
  report-failure-as-issue: false
  report-incomplete: false
  jobs:
    publish-weekly-eval-report:
      description: "Find or create the durable evaluation health issue and publish one weekly comment"
      if: >-
        needs.agent.result == 'success' &&
        needs.detection.result == 'success' &&
        needs.detection.outputs.detection_success == 'true' &&
        contains(needs.agent.outputs.output_types, 'publish_weekly_eval_report')
      runs-on: ubuntu-latest
      permissions:
        contents: read
        issues: write
      inputs:
        week_key:
          description: "ISO week key from the collected evidence"
          required: true
          type: string
        body:
          description: "Validated weekly report comment"
          required: true
          type: string
      steps:
        - name: Publish weekly evaluation report
          uses: actions/github-script@v9
          env:
            EXPECTED_REPOSITORY: ${{ github.repository }}
          with:
            script: |
              const fs = require("fs");

              const outputPath = process.env.GH_AW_AGENT_OUTPUT;
              if (!outputPath) {
                core.setFailed("GH_AW_AGENT_OUTPUT is not set");
                return;
              }

              const output = JSON.parse(fs.readFileSync(outputPath, "utf8"));
              const allItems = Array.isArray(output.items) ? output.items : [];
              const items = allItems.filter(
                item => item.type === "publish_weekly_eval_report"
              );
              if (allItems.length !== 1 || items.length !== 1) {
                core.setFailed(
                  `Expected publish_weekly_eval_report as the only output item, got ${allItems.length} total`
                );
                return;
              }

              const item = items[0];
              const weekKey = item.week_key;
              const body = item.body;
              const now = new Date();
              const isoDate = new Date(Date.UTC(
                now.getUTCFullYear(),
                now.getUTCMonth(),
                now.getUTCDate()
              ));
              const isoDay = isoDate.getUTCDay() || 7;
              isoDate.setUTCDate(isoDate.getUTCDate() + 4 - isoDay);
              const isoYear = isoDate.getUTCFullYear();
              const yearStart = new Date(Date.UTC(isoYear, 0, 1));
              const isoWeek = String(Math.ceil(
                ((((isoDate - yearStart) / 86400000) + 1) / 7)
              )).padStart(2, "0");
              const expectedWeekKey = `${isoYear}-W${isoWeek}`;
              const requiredHeadings = [
                "### For week ",
                "### High-level stats",
                "### Executive insights",
                "### Recommendations",
                "### Trends",
                "### Plugin and skill details",
                "### Methodology and evidence limits",
              ];
              if (
                typeof weekKey !== "string" ||
                !/^\d{4}-W\d{2}$/.test(weekKey) ||
                weekKey !== expectedWeekKey ||
                typeof body !== "string" ||
                body.length < 500 ||
                body.length > 65000 ||
                /<!--\s*weekly-eval-insights:/i.test(body) ||
                !requiredHeadings.every(heading => body.includes(heading))
              ) {
                core.setFailed("Weekly report publication input failed validation");
                return;
              }

              const [owner, repo] = process.env.EXPECTED_REPOSITORY.split("/");
              const issueTitle = "Weekly Evaluation Health";
              const issueMarker = "<!-- weekly-eval-insights-dashboard:v1 -->";
              const weekMarker = `<!-- weekly-eval-insights:${weekKey} -->`;
              const issues = await github.paginate(github.rest.issues.listForRepo, {
                owner,
                repo,
                state: "open",
                per_page: 100,
              });
              const matches = issues.filter(
                issue =>
                  !issue.pull_request &&
                  issue.user?.login === "github-actions[bot]" &&
                  issue.title === issueTitle &&
                  (issue.body || "").includes(issueMarker)
              );
              if (matches.length > 1) {
                core.setFailed("Multiple open weekly evaluation health issues found");
                return;
              }

              let issue = matches[0];
              if (!issue) {
                const created = await github.rest.issues.create({
                  owner,
                  repo,
                  title: issueTitle,
                  body:
                    `${issueMarker}\n` +
                    "This issue is the durable home for evidence-based weekly evaluation health reports. " +
                    "Each workflow run posts one deduplicated comment for its ISO week.",
                });
                issue = created.data;
              }

              const comments = await github.paginate(
                github.rest.issues.listComments,
                {
                  owner,
                  repo,
                  issue_number: issue.number,
                  per_page: 100,
                }
              );
              const weekMarkerPattern = new RegExp(
                `\\n\\n<!-- weekly-eval-insights:${weekKey} -->\\s*$`
              );
              const alreadyPublished = comments.some(
                comment =>
                  comment.user?.login === "github-actions[bot]" &&
                  weekMarkerPattern.test(comment.body || "")
              );
              if (alreadyPublished) {
                core.info(`A report for ${weekKey} already exists`);
                return;
              }

              const escapedBody = body.replace(
                /(^|[^A-Za-z0-9])@(?=[A-Za-z0-9])/g,
                "$1&#64;"
              );
              await github.rest.issues.createComment({
                owner,
                repo,
                issue_number: issue.number,
                body: `${escapedBody}\n\n${weekMarker}`,
              });
  noop:
    report-as-issue: false

network:
  allowed:
    - defaults

timeout-minutes: 20

steps:
  - name: Collect official weekly evaluation evidence
    env:
      GH_TOKEN: ${{ github.token }}
      REPOSITORY: ${{ github.repository }}
    run: |
      set -euo pipefail
      mkdir -p /tmp/gh-aw/data /tmp/gh-aw/eval-dashboard
      window_end="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
      window_start="$(date -u -d '7 days ago' +%Y-%m-%dT%H:%M:%SZ)"
      git fetch --no-tags --depth=1 origin dashboard-eval-data
      git archive --format=tar FETCH_HEAD data/ | tar -xf - -C /tmp/gh-aw/eval-dashboard
      gh api --method GET --paginate --slurp \
        "repos/${REPOSITORY}/actions/workflows/evaluation.yml/runs" \
        -f per_page=100 \
        -f "created=${window_start}..${window_end}" \
        > /tmp/gh-aw/data/evaluation-run-pages.json
      node eng/evaluation/weekly-eval-insights.js \
        --dashboard-file /tmp/gh-aw/eval-dashboard/data/skill-value.json \
        --runs-file /tmp/gh-aw/data/evaluation-run-pages.json \
        --window-end "${window_end}" \
        --output /tmp/gh-aw/data/weekly-eval-insights.json

  - name: Collect prior weekly report comments
    env:
      GH_TOKEN: ${{ github.token }}
      REPOSITORY: ${{ github.repository }}
    run: |
      set -euo pipefail
      issue_number="$(
        gh api --method GET --paginate --slurp \
          "repos/${REPOSITORY}/issues" \
          -f state=open \
          -f per_page=100 \
          | jq -r '
              [
                .[][]
                | select(
                    (.pull_request | not)
                    and .user.login == "github-actions[bot]"
                    and .title == "Weekly Evaluation Health"
                    and (.body | contains("<!-- weekly-eval-insights-dashboard:v1 -->"))
                  )
              ]
              | if length > 1
                then error("multiple bot-authored weekly evaluation health issues found")
                elif length == 1 then .[0].number
                else empty
                end
            '
      )"
      if [ -z "${issue_number}" ]; then
        printf '[]\n' > /tmp/gh-aw/data/prior-report-comment-pages.json
      else
        gh api --paginate --slurp \
          "repos/${REPOSITORY}/issues/${issue_number}/comments?per_page=100" \
          | jq '
              [
                .[][]
                | select(
                    .user.login == "github-actions[bot]"
                    and (.body | contains("<!-- weekly-eval-insights:"))
                  )
                | {created_at, body}
              ]
              | sort_by(.created_at)
              | reverse
              | .[0:8]
            ' \
          > /tmp/gh-aw/data/prior-report-comment-pages.json
      fi

# ###############################################################
# Select a PAT from the pool and override COPILOT_GITHUB_TOKEN.
# Run agentic jobs in an isolated `copilot-pat-pool` environment.
#
# When org-level billing is available, this will be removed.
# See `shared/pat_pool.README.md` for more information.
# ###############################################################
imports:
  - uses: shared/pat_pool.md
    with:
      environment: copilot-pat-pool

environment: copilot-pat-pool

engine:
  id: copilot
  env:
    COPILOT_GITHUB_TOKEN: >-
      ${{ case(
        needs.pat_pool.outputs.pat_number == '0', secrets.COPILOT_PAT_0,
        needs.pat_pool.outputs.pat_number == '1', secrets.COPILOT_PAT_1,
        needs.pat_pool.outputs.pat_number == '2', secrets.COPILOT_PAT_2,
        needs.pat_pool.outputs.pat_number == '3', secrets.COPILOT_PAT_3,
        needs.pat_pool.outputs.pat_number == '4', secrets.COPILOT_PAT_4,
        needs.pat_pool.outputs.pat_number == '5', secrets.COPILOT_PAT_5,
        needs.pat_pool.outputs.pat_number == '6', secrets.COPILOT_PAT_6,
        needs.pat_pool.outputs.pat_number == '7', secrets.COPILOT_PAT_7,
        needs.pat_pool.outputs.pat_number == '8', secrets.COPILOT_PAT_8,
        needs.pat_pool.outputs.pat_number == '9', secrets.COPILOT_PAT_9,
        'NO COPILOT PAT AVAILABLE'
      ) }}
---

# Weekly Evaluation Insights

## Task

Read `/tmp/gh-aw/data/weekly-eval-insights.json`. This is the authoritative,
deterministically aggregated evidence for the current and prior seven-day
windows. Read `/tmp/gh-aw/data/prior-report-comment-pages.json` only to compare
the current evidence with prior weekly findings. Treat prior comments as
historical claims, not as a replacement for current official CI evidence.

Create one evidence-based report and call `publish_weekly_eval_report` with:

- `week_key`: the exact `weekKey` from the evidence file.
- `body`: the complete report in the required format below.

Do not call any other safe-output tool. If the evidence file is missing,
malformed, or internally inconsistent, call `noop` with the exact limitation.

## Metric rules

- Use the metric contract in the evidence file. Do not replace it with an
  intuitive or approximate definition.
- Differentiate treatment from control only when the official paired evidence
  meets every recorded statistical, practical-significance, and activation
  condition.
- Reliability is conclusive expected results divided by expected results.
- Keep treatment pass rate separate from differentiation and reliability.
- Report statistically credible reverse preference as a preference loss. Do not
  call preference loss an objective completion regression.
- Treat changes for the same skill, executor model, and judge model as
  flakiness. Treat stable differences across model or judge dimensions as model
  sensitivity, not flakiness.
- Report AI Credits as unavailable when `aiCredits.available` is false. Include
  token telemetry as supporting resource evidence, but never estimate AI Credits
  or dollar spend.
- Distinguish invalid measurement, underpowered design, timeout, activation
  failure, no credible change, preference loss, and proven differentiation.
- State missing data and small samples. Do not turn absence of evidence into a
  positive or negative conclusion.

## Required report format

Use `###` headings only for main sections.

### For week `<current.start>` to `<current.end>`

### High-level stats

Include:

- official evaluation workflow run count and conclusions
- executor models and judge models
- average workflow duration
- expected, conclusive, invalid, underpowered, differentiated, and preference
  loss result counts
- overall reliability and differentiation rates
- AI Credits status, with total or average token telemetry when useful

### Executive insights

Provide 3-5 short findings for an executive audience. Lead with impact, then
cite the exact metric or count.

### Recommendations

Provide about five simple, actionable, evidence-based recommendations. Name the
plugin or skill when the evidence supports that specificity. Do not recommend
changing a skill when the evidence instead indicates harness failure,
underpowered evaluation design, or missing telemetry.

### Trends

Provide:

- 3-5 recurring trends that match prior weekly reports or the previous window
- new trends to watch this week

State when there is not enough historical evidence to establish a trend.

### Plugin and skill details

Group by plugin. For every skill with evidence in the current window, include:

- expected result count
- treatment pass rate
- differentiation rate
- average treatment duration
- token use
- reliability
- key status such as consistently differentiating, flaky, invalid,
  model-sensitive, consistently passing, underpowered, timeout, activation
  failure, preference loss, or no credible change

Use compact tables. Put large plugin sections in `<details>` blocks.

### Methodology and evidence limits

State:

- reporting window and grouping dimensions
- official data sources
- the exact metric definitions
- current retention or history limits
- unavailable fields, especially AI Credits
- whether prior-report comments were available

End with up to three relevant workflow run links from `workflowRuns.references`.
Do not include dollar amounts, emoji severity markers, attribution footers,
user mentions, or issue-closing phrases.
