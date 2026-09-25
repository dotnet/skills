---
name: "Build Failure Analysis"
description: >-
  When the configured Azure Pipelines PR build fails, downloads the binary logs
  that build already produced — it does NOT rebuild — and delegates to the
  `build-failure-analyst` agent, which queries the binlogs live via the
  containerized `binlog-mcp` MCP server to identify root causes, post a PR
  comment summarizing them, and attach inline `suggestion` blocks tied to the
  diff.

# This workflow is **advisory**, not gating, and it performs **no build of its
# own**. The consuming repository configures a public Azure DevOps organization,
# project, pipeline definition, and GitHub rollup check name through repository
# variables. When that check reports failure, this workflow downloads available
# binlogs from the build artifacts and the agent analyses whichever leg(s)
# contain errors. Reusing those binlogs avoids a duplicate build: the analysis
# pipeline only downloads build artifacts (data) and reads them — it does **not**
# build or execute PR code. (gh-aw's generated agent job **does** check out the
# repository — via `actions/checkout` — to load the workflow's own agent
# configuration; that checkout is for tooling only and uses the event's ref,
# **not** the PR head, so no PR code is built or executed.)

on:
  # `check_run` fires for every check on a commit, so the `fetch-binlog` job
  # below filters tightly to the configured rollup build check reporting
  # failure. Exact-name matching deliberately ignores per-leg checks so the
  # analysis runs once per build.
  check_run:
    types: [completed]
  # Advisory analysis should run for **every** failing PR — including external
  # contributors' PRs, which are the most likely to break the build. Disable
  # gh-aw's default author-association gate (which would otherwise skip
  # non-write-access actors, and on `check_run` the actor is the pipeline app
  # anyway). This is safe here: the workflow only reads a public binlog and
  # posts advisory comments — it never builds or executes PR code.
  roles: all
  # Manual entry point for reruns / testing: analyse a specific Azure DevOps
  # build id and post to a specific PR.
  workflow_dispatch:
    inputs:
      ado-build-id:
        description: "Azure DevOps build id to analyze."
        required: true
        type: string
      pr-number:
        description: "PR number to post the analysis on."
        required: true
        type: string
  # Gate the whole AI pipeline on the fetch job so the agent only runs when a
  # binlog was actually retrieved.
  needs: [fetch-binlog]

# Activate (and run the agent) only when the fetch job retrieved at least one
# binlog. When `check_run` fires for an unrelated / passing check the
# fetch-binlog job is skipped, its output is empty, and this cascades into a
# skipped agent — no AI calls on anything but a real failure from the
# configured build pipeline.
if: needs.fetch-binlog.outputs.binlog-found == 'true'

# Least-privilege for the workflow/agent jobs. The agent runs read-only; it
# does NOT post directly. All PR writes (summary comment + inline review
# suggestions) go through gh-aw **safe-outputs**, which the compiler emits as
# a separate `safe_outputs` job granted `pull-requests: write` + `issues:
# write` in the generated lock. Keep `pull-requests: read` here so the AI
# agent job stays least-privilege — do NOT raise it to `write`, that would
# hand PR-write scope to the agent job unnecessarily.
permissions:
  contents: read
  pull-requests: read
  copilot-requests: write

concurrency:
  # Only real configured build check_run events (and manual dispatch for
  # a PR) use a PR/head-scoped group, so a newer analysis supersedes an
  # in-progress one for the same PR. Every OTHER completed check_run on the PR
  # would otherwise land in the same group and — with cancel-in-progress —
  # abort the running real analysis, so those get a unique per-run group that
  # collides with nothing.
  group: ${{ (github.event_name == 'check_run' && github.event.check_run.name == vars.BUILD_FAILURE_ANALYSIS_CHECK_NAME && format('build-failure-analysis-{0}', github.event.check_run.pull_requests[0].number || github.event.check_run.head_sha)) || (github.event_name == 'workflow_dispatch' && format('build-failure-analysis-{0}', inputs['pr-number'])) || format('build-failure-analysis-run-{0}', github.run_id) }}
  cancel-in-progress: true
  job-discriminator: ${{ github.run_id }}

timeout-minutes: 30

imports:
  - build-failure-analysis-fetch.md
  - build-failure-analysis-shared.md

engine:
  id: copilot


# Custom job that reuses the binlogs from the failed Azure DevOps build instead
# of rebuilding. It resolves the ADO build id (from the check details URL or
# the dispatch input), verifies the PR targets an in-scope base branch,
# downloads build artifacts, extracts their `*.binlog` files, and uploads them
# for the agent job.
# Steps that run in the agent job. Because the top-level `if:` gates activation
# on `needs.fetch-binlog.outputs.binlog-found == 'true'`, these only run once
# binlogs have been retrieved from the failed Azure DevOps build.
steps:
  - name: Download analysis artifact
    uses: actions/download-artifact@v8.0.1
    with:
      name: build-failure-analysis-data
      path: /tmp/binlogs

  - name: Export agent context
    shell: bash
    env:
      GH_AW_BINLOG_FOUND_VALUE: ${{ needs.fetch-binlog.outputs.binlog-found }}
      GH_AW_PR_NUMBER_VALUE: ${{ needs.fetch-binlog.outputs.pr-number }}
      GH_AW_PR_HEAD_SHA_VALUE: ${{ needs.fetch-binlog.outputs.pr-head-sha }}
      GH_AW_PR_MERGE_SHA_VALUE: ${{ needs.fetch-binlog.outputs.pr-merge-sha }}
      GH_AW_ADO_BUILD_URL_VALUE: ${{ needs.fetch-binlog.outputs.ado-build-url }}
      GH_AW_MISSING_LEGS_VALUE: ${{ needs.fetch-binlog.outputs.missing-legs }}
      GH_AW_GITHUB_WORKSPACE: ${{ github.workspace }}
    run: |
      # The binlogs are mounted into the binlog-mcp container at
      # `/data/binlogs`. Build the list of in-container binlog paths (one per
      # build leg) that the agent should query. `GH_AW_BINLOG_PATH` is the
      # first entry for tools/prompts that expect a single path.
      BINLOG_DIR="/data/binlogs"
      LIST=""
      if [ "${GH_AW_BINLOG_FOUND_VALUE:-false}" = "true" ] && [ -d /tmp/binlogs ]; then
        for f in /tmp/binlogs/*.binlog; do
          [ -f "$f" ] || continue
          LIST="${LIST}${BINLOG_DIR}/$(basename "$f")"$'\n'
        done
      fi
      FIRST=$(printf '%s' "$LIST" | head -1)
      {
        echo "GH_AW_BUILD_OUTCOME=failure"
        echo "GH_AW_BINLOG_DIR=${BINLOG_DIR}"
        echo "GH_AW_BINLOG_PATH=${FIRST}"
        echo "GH_AW_BINLOG_HOST_PATH=${GH_AW_ADO_BUILD_URL_VALUE}"
        echo "GH_AW_PR_NUMBER=${GH_AW_PR_NUMBER_VALUE}"
        echo "GH_AW_PR_HEAD_SHA=${GH_AW_PR_HEAD_SHA_VALUE}"
        echo "GH_AW_PR_MERGE_SHA=${GH_AW_PR_MERGE_SHA_VALUE}"
        echo "GH_AW_MISSING_LEGS=${GH_AW_MISSING_LEGS_VALUE}"
        echo "GH_AW_WORKSPACE=${GH_AW_GITHUB_WORKSPACE}"
        echo "GH_AW_BINLOG_LIST<<GH_AW_EOF"
        printf '%s' "$LIST"
        echo "GH_AW_EOF"
      } >> "$GITHUB_ENV"


---

<!--
  Body provided by shared/build-failure-analysis-shared.md.

  All build-failure analysis expertise (binlog parsing, error grouping,
  suggestion authoring) lives in the reusable agent at
  .github/agents/build-failure-analyst.agent.md.
-->
