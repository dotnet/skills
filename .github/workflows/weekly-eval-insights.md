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

jobs:
  verify_weekly_eval_report:
    needs: [agent, publish_weekly_eval_report]
    if: ${{ !cancelled() && needs.agent.result != 'skipped' }}
    runs-on: ubuntu-latest
    permissions: {}
    steps:
      - name: Require a published or deduplicated report
        env:
          AGENT_RESULT: ${{ needs.agent.result }}
          PUBLICATION_RESULT: ${{ needs.publish_weekly_eval_report.result }}
        run: |
          if [ "$AGENT_RESULT" != "success" ] || [ "$PUBLICATION_RESULT" != "success" ]; then
            echo "::error::Weekly evaluation report was not published or deduplicated."
            exit 1
          fi

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
  bash: ["cat", "sha256sum"]
  edit: false

safe-outputs:
  report-failure-as-issue: false
  report-incomplete: false
  threat-detection:
    continue-on-error: false
    steps:
      - name: Include weekly report in threat detection
        uses: actions/github-script@3a2844b7e9c422d3c10d287c895573f7108da1b3 # v9.0.0
        env:
          REPORT_PATH: /tmp/gh-aw/agent/weekly-eval-report.md
          DETECTION_INPUT_PATH: /tmp/gh-aw/threat-detection-inputs/agent_output.json
        with:
          script: |
            const fs = require("fs");
            const { createHash } = require("crypto");

            const output = JSON.parse(
              fs.readFileSync(process.env.DETECTION_INPUT_PATH, "utf8")
            );
            if (
              !Array.isArray(output.items) ||
              output.items.length !== 1 ||
              output.items[0].type !== "publish_weekly_eval_report"
            ) {
              throw new Error("Expected exactly one weekly report output");
            }

            const item = output.items[0];
            const stat = fs.lstatSync(process.env.REPORT_PATH);
            if (!stat.isFile() || stat.size < 500 || stat.size > 65000) {
              throw new Error("Weekly report file is missing, invalid, or oversized");
            }
            const bytes = fs.readFileSync(process.env.REPORT_PATH);
            const body = bytes.toString("utf8");
            if (
              !Buffer.from(body, "utf8").equals(bytes) ||
              typeof item.sha256 !== "string" ||
              !/^[a-f0-9]{64}$/.test(item.sha256) ||
              createHash("sha256").update(bytes).digest("hex") !== item.sha256
            ) {
              throw new Error("Weekly report encoding or digest is invalid");
            }

            item.body = body;
            fs.writeFileSync(process.env.DETECTION_INPUT_PATH, JSON.stringify(output));
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
        actions: read
        contents: read
        issues: write
      artifacts:
        - /tmp/gh-aw/agent/weekly-eval-report.md
      inputs:
        week_key:
          description: "ISO week key from the collected evidence"
          required: true
          type: string
        sha256:
          description: "Lowercase SHA-256 digest of the complete UTF-8 report file"
          required: true
          type: string
      steps:
        - name: Load pinned publication normalizer
          uses: github/gh-aw-actions/setup@53d0749e19424a918acd82b4d140264ce48f2f14
        - name: Publish weekly evaluation report
          uses: actions/github-script@v9.0.0
          env:
            EXPECTED_REPOSITORY: ${{ github.repository }}
            GH_AW_ACTIONS_DIR: ${{ runner.temp }}/gh-aw/actions
          with:
            script: |
              const fs = require("fs");
              const path = require("path");
              const { createHash } = require("crypto");

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
              const reportPath = path.join(
                path.dirname(outputPath),
                "agent",
                "weekly-eval-report.md"
              );
              const stat = fs.lstatSync(reportPath);
              if (!stat.isFile() || stat.size < 500 || stat.size > 65000) {
                core.setFailed("Weekly report file is missing, invalid, or oversized");
                return;
              }
              const bytes = fs.readFileSync(reportPath);
              const body = bytes.toString("utf8");
              if (
                !Buffer.from(body, "utf8").equals(bytes) ||
                typeof item.sha256 !== "string" ||
                !/^[a-f0-9]{64}$/.test(item.sha256) ||
                createHash("sha256").update(bytes).digest("hex") !== item.sha256
              ) {
                core.setFailed("Weekly report encoding or digest is invalid");
                return;
              }
              const currentRun = await github.rest.actions.getWorkflowRun({
                owner: context.repo.owner,
                repo: context.repo.repo,
                run_id: context.runId,
              });
              const now = new Date(currentRun.data.created_at);
              if (Number.isNaN(now.getTime())) {
                core.setFailed("Workflow run creation time is unavailable");
                return;
              }
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

              const { canonicalizePublicationText } = require(path.join(
                process.env.GH_AW_ACTIONS_DIR,
                "sanitize_content_core.cjs"
              ));
              let normalized = body;
              for (let pass = 0; ; pass++) {
                const next = canonicalizePublicationText(normalized)
                  .replace(/\\([!"#$%&'()*+,\-./:;<=>?@[\\\]^_`{|}~])/g, "$1");
                if (next === normalized) break;
                if (pass === 15) {
                  throw new Error("Weekly report contains excessively nested encodings");
                }
                normalized = next;
              }
              // Remaining named entities must not become URI characters when rendered.
              normalized = normalized.replace(/&[A-Za-z][A-Za-z0-9]+;/g, "[encoded character removed]");
              const allowedTag = /^<\/?(?:details|summary)>$/i;
              const isAllowedUrl = raw => {
                try {
                  const url = new URL(
                    /^[/\\]{2}/.test(raw) ? `https:${raw}` :
                    /^www\./i.test(raw) ? `https://${raw}` : raw
                  );
                  const host = url.hostname.toLowerCase();
                  return (
                    (url.protocol === "https:" || url.protocol === "http:") &&
                    (host === "github.com" || host.endsWith(".github.com"))
                  );
                } catch {
                  return false;
                }
              };
              const sanitized = normalized
                .replace(/\u001b\[[0-?]*[ -\/]*[@-~]/g, "")
                .replace(/[\u0000-\u0009\u000b-\u001f\u007f\u202a-\u202e\u2066-\u2069]/g, "")
                .replace(/<!--[\s\S]*?-->/g, "")
                .replace(/<[A-Za-z!?\/][^<>]*>?/g, tag =>
                  allowedTag.test(tag) ? tag : `&lt;${tag.slice(1)}`
                )
                .replace(
                  /(?<![A-Za-z0-9])[a-z][a-z0-9+.-]*:[^\s<>()\[\]"'`]+|[/\\]{2}[^\s<>()\[\]"'`]+|(?<![A-Za-z0-9])www\.[^\s<>()\[\]"'`]+/gi,
                  url => (isAllowedUrl(url) ? url : "(external link removed)")
                );
              if (
                /<!--/.test(sanitized) ||
                !requiredHeadings.every(heading => sanitized.includes(heading))
              ) {
                core.setFailed("Weekly report failed validation after sanitization");
                return;
              }

              const [owner, repo] = process.env.EXPECTED_REPOSITORY.split("/");
              const issueTitle = "Weekly Evaluation Health";
              const issueMarker = "<!-- weekly-eval-insights-dashboard:v1 -->";
              const weekMarker = `<!-- weekly-eval-insights:${weekKey} -->`;
              const escapedBody = sanitized.replace(
                /(^|[^A-Za-z0-9])@(?=[A-Za-z0-9])/g,
                "$1&#64;"
              );
              const commentBody = `${escapedBody}\n\n${weekMarker}`;
              if (Buffer.byteLength(commentBody, "utf8") > 65536) {
                core.setFailed("Weekly report exceeds the comment size limit after escaping");
                return;
              }
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

              await github.rest.issues.createComment({
                owner,
                repo,
                issue_number: issue.number,
                body: commentBody,
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
      window_end="$(
        gh api "repos/${REPOSITORY}/actions/runs/${GITHUB_RUN_ID}" \
          --jq .created_at
      )"
      if [ -z "${window_end}" ] || [ "${window_end}" = "null" ]; then
        echo "::error::Workflow run creation time is unavailable"
        exit 1
      fi
      window_start="$(date -u -d "${window_end} - 7 days" +%Y-%m-%dT%H:%M:%SZ)"
      git fetch --no-tags --depth=1 origin dashboard-eval-data
      git archive --format=tar FETCH_HEAD data/ | tar -xf - -C /tmp/gh-aw/eval-dashboard
      gh api --method GET --paginate --slurp \
        "repos/${REPOSITORY}/actions/workflows/evaluation.yml/runs" \
        -f per_page=100 \
        -f event=schedule \
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
  args:
    - --allow-tool
    - "write(/tmp/gh-aw/agent/weekly-eval-report.md)"
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

## Task

Read `/tmp/gh-aw/data/weekly-eval-insights.json`. This is the authoritative,
deterministically aggregated evidence for the current and prior seven-day
windows. Read `/tmp/gh-aw/data/prior-report-comment-pages.json` only to compare
the current evidence with prior weekly findings. Treat prior comments as
historical claims, not as a replacement for current official CI evidence.

Write one evidence-based UTF-8 report to
`/tmp/gh-aw/agent/weekly-eval-report.md`. The file must contain at least 500
characters and at most 65,000 bytes. After writing it, compute its SHA-256
digest and call `publish_weekly_eval_report` with:

- `week_key`: the exact `weekKey` from the evidence file.
- `sha256`: the report file's 64-character lowercase hexadecimal digest.

Do not modify repository files or change the report file after computing the
digest. The final comment, including escaped mentions and the week marker,
must not exceed 65,536 bytes.
Do not call any other safe-output tool. If the evidence file is missing,
malformed, or internally inconsistent, call `noop` with the exact limitation.

## Metric rules

- Use the metric contract in the evidence file. Do not replace it with an
  intuitive or approximate definition.
- Differentiate treatment from control only when the official paired evidence
  meets every recorded statistical, practical-significance, and activation
  condition.
- Published-result reliability is conclusive retained results divided by
  retained results. Expected evaluation coverage is unavailable because failed
  runs can produce no retained target result.
- Keep treatment pass rate separate from differentiation and reliability.
- Report statistically credible reverse preference as a preference loss. Do not
  call preference loss an objective completion regression.
- Report retained native objective completion regression separately. It
  overrides preference evidence and cannot count as differentiation.
- Compare repeated outcomes and model or judge differences only within the same
  evaluated revision. Treat cross-revision changes as trends, not flakiness or
  model sensitivity. When revision evidence is missing and
  `variabilityComparability` is `unknown`, state that limitation instead of
  inferring either signal.
- Report AI Credits as unavailable when `aiCredits.available` is false. Include
  token telemetry as supporting resource evidence, but never estimate AI Credits
  or dollar spend.
- Distinguish invalid measurement, underpowered design, timeout, activation
  failure, native completion regression, no credible change, preference loss,
  and proven differentiation.
- State missing data and small samples. Do not turn absence of evidence into a
  positive or negative conclusion.

## Required report format

Use `###` headings only for main sections.

### For week `<current.start>` to `<current.end>`

### High-level stats

Include:

- official evaluation workflow run count and conclusions
- state that workflow-run statistics cover the scheduled official evaluation
  population; retained skill statistics may also include explicitly published
  main-branch manual evaluations
- executor models and judge models
- average workflow duration
- retained published, conclusive, invalid, underpowered, differentiated, and
  preference loss result counts, plus native completion regressions
- published-result reliability and differentiation rates
- expected evaluation coverage status
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

- retained published result count
- treatment pass rate
- differentiation rate
- average treatment duration
- token use
- published-result reliability
- key status such as consistently differentiating, flaky, invalid,
  model-sensitive, consistently passing, underpowered, timeout, activation
  failure, native completion regression, preference loss, or no credible change

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
Link only to `github.com` URLs, and use no raw HTML other than `<details>` and
`<summary>`. The publisher normalizes encoded text, neutralizes unresolved
entities, and removes other links, HTML tags, and HTML comments,
and shows `<` in other HTML-like text as `&lt;`, including inside code spans.
Do not include dollar amounts, emoji severity markers, attribution footers,
user mentions, or issue-closing phrases.
