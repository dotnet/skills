---
# Packaged with a workflow-specific filename to avoid collisions in consumers.
description: >-
  Shared `fetch-binlog` job for the Build Failure Analysis workflow. Resolves
  the PR's failed configured Azure DevOps build, downloads the binary logs that
  build already produced and stages them for the analysis agent. It performs
  **no build**: it only reads published build artifacts.

# The job is imported by the main workflow so the artifact-fetching logic stays
# separate from agent configuration and prompt instructions.
jobs:
  fetch-binlog:
    name: Fetch binlogs (Azure Pipelines)
    runs-on: ubuntu-latest
    timeout-minutes: 15
    # `check_run` fires for every check on a commit, so only the configured
    # rollup check reporting failure is acted on.
    if: >-
      github.event_name == 'workflow_dispatch' ||
      (github.event_name == 'check_run' &&
       github.event.check_run.name == vars.BUILD_FAILURE_ANALYSIS_CHECK_NAME &&
       github.event.check_run.conclusion == 'failure')
    permissions:
      contents: read
      pull-requests: read
    outputs:
      binlog-found: ${{ steps.fetch.outputs.binlog-found }}
      pr-number: ${{ steps.fetch.outputs.pr-number }}
      pr-head-sha: ${{ steps.fetch.outputs.pr-head-sha }}
      pr-merge-sha: ${{ steps.fetch.outputs.pr-merge-sha }}
      ado-build-id: ${{ steps.fetch.outputs.ado-build-id }}
      ado-build-url: ${{ steps.fetch.outputs.ado-build-url }}
      missing-legs: ${{ steps.fetch.outputs.missing-legs }}
    steps:
      - name: Download binlogs from the failed Azure Pipelines build
        id: fetch
        shell: bash
        env:
          GH_TOKEN: ${{ github.token }}
          GH_AW_REPO: ${{ github.repository }}
          ADO_ORGANIZATION: ${{ vars.BUILD_FAILURE_ANALYSIS_ADO_ORGANIZATION }}
          ADO_PROJECT: ${{ vars.BUILD_FAILURE_ANALYSIS_ADO_PROJECT }}
          ADO_API: "https://dev.azure.com/${{ vars.BUILD_FAILURE_ANALYSIS_ADO_ORGANIZATION }}/${{ vars.BUILD_FAILURE_ANALYSIS_ADO_PROJECT }}/_apis"
          ADO_BUILD_UI: "https://dev.azure.com/${{ vars.BUILD_FAILURE_ANALYSIS_ADO_ORGANIZATION }}/${{ vars.BUILD_FAILURE_ANALYSIS_ADO_PROJECT }}/_build/results"
          ADO_BUILD_DEFINITION_ID: ${{ vars.BUILD_FAILURE_ANALYSIS_ADO_DEFINITION_ID }}
          BUILD_CHECK_NAME: ${{ vars.BUILD_FAILURE_ANALYSIS_CHECK_NAME }}
          EVENT_NAME: ${{ github.event_name }}
          # `check_run` payload.
          CHECK_DETAILS_URL: ${{ github.event.check_run.details_url }}
          CHECK_PR_NUMBER: ${{ github.event.check_run.pull_requests[0].number }}
          # `workflow_dispatch` inputs.
          DISPATCH_BUILD_ID: ${{ github.event.inputs['ado-build-id'] }}
          DISPATCH_PR_NUMBER: ${{ github.event.inputs['pr-number'] }}
        run: |
          # Advisory + best-effort: on any gap emit binlog-found=false and the
          # agent pipeline stays inert.
          set +e
          set +o pipefail

          # A set but unwritable path would pass a non-empty check and then
          # fail on every append, leaving the step with no outputs at all
          # instead of the intended controlled no-op. Probe with a zero-byte
          # append, which verifies writability without adding content.
          if [ -z "${GITHUB_OUTPUT}" ] || ! printf '' >> "${GITHUB_OUTPUT}" 2>/dev/null; then
            echo "::error::GITHUB_OUTPUT is unset or not writable; refusing to run without a way to emit step outputs." >&2
            exit 1
          fi

          emit_none() { echo "binlog-found=false" >> "$GITHUB_OUTPUT"; exit 0; }

          if [ -z "${BUILD_CHECK_NAME}" ] || [ -z "${ADO_ORGANIZATION}" ] ||
             [ -z "${ADO_PROJECT}" ] || [ -z "${ADO_BUILD_DEFINITION_ID}" ]; then
            echo "::warning::Build Failure Analysis repository variables are not configured; skipping."
            emit_none
          fi
          if ! printf '%s' "${ADO_BUILD_DEFINITION_ID}" | grep -qE '^[0-9]+$'; then
            echo "::warning::Configured Azure Pipelines definition id '${ADO_BUILD_DEFINITION_ID}' is not numeric; refusing."
            emit_none
          fi

          # Fetch an Azure DevOps API document into ADO_DOC. A network failure
          # or a non-JSON body is a data-resolution failure, not evidence that
          # there is nothing to analyse, so it is reported as such instead of
          # falling through to an empty `.records`/`.value` and a misleading
          # "no failed jobs" warning. These are small JSON documents, so they
          # are also given a time budget: without one a stalled endpoint hangs
          # the step until the whole job times out.
          # Returns non-zero rather than calling emit_none directly, because a
          # call inside a command substitution would only exit the subshell.
          ado_get() {
            local what="$1" url="$2" rc tmp
            # `mktemp` rather than a fixed /tmp name: a predictable path is one
            # pre-created symlink -- or one collision with another job sharing the
            # runner -- away from being someone else's file.
            tmp=$(mktemp) || {
              echo "::warning::Could not create a temporary file for the ${what}; treating as a data-resolution failure."
              return 1
            }
            # Write to a file rather than capturing stdout: `curl --retry` can only
            # rewind seekable output, and command-substitution stdout is a pipe. A
            # retry after a partial or error body would append to it, so a *successful*
            # retry would yield two concatenated documents, `jq` would reject them, and
            # the run would be reported as a data-resolution failure. With `-o` curl
            # truncates the file before each attempt, so only the last response
            # survives.
            timeout 60 curl -sSL --fail --retry 3 --connect-timeout 10 --max-time 20 --retry-max-time 40 -o "${tmp}" "${url}"
            rc=$?
            ADO_DOC=$(cat "${tmp}" 2>/dev/null)
            rm -f "${tmp}"
            if [ "${rc}" -ne 0 ] || [ -z "${ADO_DOC}" ]; then
              echo "::warning::Could not fetch the ${what} from Azure DevOps (curl exit ${rc}); treating as a data-resolution failure."
              return 1
            fi
            if ! printf '%s' "${ADO_DOC}" | jq -e . >/dev/null 2>&1; then
              echo "::warning::Azure DevOps returned a non-JSON ${what}; treating as a data-resolution failure."
              return 1
            fi
            return 0
          }

          # --- 1. Resolve the Azure DevOps build and the PR it belongs to ---
          if [ "${EVENT_NAME}" = "workflow_dispatch" ]; then
            BUILD_ID="${DISPATCH_BUILD_ID}"
          else
            # details_url looks like: .../_build/results?buildId=NNN&view=...
            BUILD_ID=$(printf '%s' "${CHECK_DETAILS_URL}" | grep -oE 'buildId=[0-9]+' | head -1 | cut -d= -f2)
          fi
          echo "Azure DevOps build id: '${BUILD_ID}'"
          [ -z "${BUILD_ID}" ] && { echo "::warning::Could not resolve an ADO build id."; emit_none; }
          # The build id feeds directly into ADO API URLs below; require it to
          # be purely numeric (especially on workflow_dispatch, where it is a
          # free-form input) so a malformed value cannot alter the path/query.
          if ! printf '%s' "${BUILD_ID}" | grep -qE '^[0-9]+$'; then
            echo "::warning::Resolved ADO build id '${BUILD_ID}' is not numeric; refusing."; emit_none
          fi
          # Build metadata is the authoritative source for the PR association,
          # definition, result, and revision validated below.
          ado_get "details of build ${BUILD_ID}" "${ADO_API}/build/builds/${BUILD_ID}?api-version=7.1" || emit_none
          build_json="${ADO_DOC}"
          BUILD_PR_NUM=$(printf '%s' "${build_json}" | jq -r '.sourceBranch // empty' | sed -n 's#^refs/pull/\([0-9]\{1,\}\)/merge$#\1#p')
          if [ "${EVENT_NAME}" = "workflow_dispatch" ]; then
            PR_NUMBER="${DISPATCH_PR_NUMBER}"
          else
            # Safe outputs are pinned to this trusted event PR number. Require
            # the build metadata to agree rather than letting build content
            # redirect output to another PR that shares the same commit.
            PR_NUMBER="${CHECK_PR_NUMBER}"
            if [ -n "${BUILD_PR_NUM}" ] && [ "${BUILD_PR_NUM}" != "${CHECK_PR_NUMBER}" ]; then
              echo "::warning::Azure Pipelines build belongs to PR #${BUILD_PR_NUM}, but the triggering check names PR #${CHECK_PR_NUMBER}; refusing."
              emit_none
            fi
          fi
          [ -z "${PR_NUMBER}" ] && { echo "::warning::Could not resolve a PR number."; emit_none; }
          # PR_NUMBER feeds `gh api .../pulls/<n>` and the merge-ref comparison;
          # require it numeric so malformed input cannot reach an API path.
          if ! printf '%s' "${PR_NUMBER}" | grep -qE '^[0-9]+$'; then
            echo "::warning::Resolved PR number '${PR_NUMBER}' is not numeric; refusing."; emit_none
          fi
          RESULT=$(printf '%s' "${build_json}" | jq -r '.result // empty')
          DEF_ID=$(printf '%s' "${build_json}" | jq -r '.definition.id // empty')
          SRC_BRANCH=$(printf '%s' "${build_json}" | jq -r '.sourceBranch // empty')

          # --- 2. Confirm the target PR exists ---
          PR_JSON=$(gh api "repos/${GH_AW_REPO}/pulls/${PR_NUMBER}" 2>/dev/null)
          BASE_REF=$(printf '%s' "${PR_JSON}" | jq -r '.base.ref // empty')
          [ -z "${BASE_REF}" ] && { echo "::warning::Could not resolve PR #${PR_NUMBER}; skipping."; emit_none; }
          echo "PR #${PR_NUMBER} targets '${BASE_REF}'."

          # --- 3. Validate the build, whichever way it was resolved ---
          # It must be the configured definition, have failed, and belong to
          # this PR (sourceBranch == refs/pull/<PR>/merge). No
          # entry point is fully trusted: `check_run` parses the build id out of
          # a check payload, while dispatch takes the build id and PR number as
          # independent free-form inputs. Validating here prevents downloading
          # an unrelated build or posting its analysis to the wrong PR.
          echo "ADO build ${BUILD_ID}: result='${RESULT}' definition='${DEF_ID}' sourceBranch='${SRC_BRANCH}'"
          if [ "${DEF_ID}" != "${ADO_BUILD_DEFINITION_ID}" ]; then
            echo "::warning::ADO build ${BUILD_ID} is definition '${DEF_ID}', not configured definition '${ADO_BUILD_DEFINITION_ID}'; refusing."; emit_none
          fi
          if [ "${RESULT}" != "failed" ]; then
            echo "::warning::ADO build ${BUILD_ID} did not fail (result='${RESULT}'); nothing to analyze."; emit_none
          fi
          if [ "${SRC_BRANCH}" != "refs/pull/${PR_NUMBER}/merge" ]; then
            echo "::warning::ADO build ${BUILD_ID} sourceBranch '${SRC_BRANCH}' does not match PR #${PR_NUMBER} (refs/pull/${PR_NUMBER}/merge); refusing to avoid posting to the wrong PR."; emit_none
          fi

          # --- 4. Require the build's analyzed revision to equal the PR's
          #        CURRENT head. gh-aw safe-output review comments carry no
          #        `commit_id` — they target the current PR diff — so analyzing
          #        a stale revision would produce inline suggestions that get
          #        rejected or land on the wrong lines. If the PR has advanced
          #        since this build ran, skip: a newer build/check for the
          #        current head will cover it.
          BUILD_PR_SHA=$(printf '%s' "${build_json}" | jq -r '.triggerInfo["pr.sourceSha"] // empty')
          # ADO builds GitHub's `refs/pull/<n>/merge` ref, so build_json.sourceVersion
          # is the merge commit GitHub produced at build time and equals the PR's
          # `merge_commit_sha` then. If the base branch advances (even with the PR
          # head unchanged) GitHub recomputes that merge and merge_commit_sha
          # changes, so this catches base-advance staleness the head check misses.
          BUILD_MERGE_SHA=$(printf '%s' "${build_json}" | jq -r '.sourceVersion // empty')
          # Re-read the PR rather than reusing the snapshot from the scope check:
          # selecting the build costs an ADO round trip, and right after a
          # force-push the newest-build query can still return the previous
          # failed build. The point of this check is to skip BEFORE paying for
          # the download, so it should compare against the freshest head
          # available. A post-download re-read below independently catches a
          # head that moves while the artifacts are being fetched.
          PR_JSON=$(gh api "repos/${GH_AW_REPO}/pulls/${PR_NUMBER}" 2>/dev/null)
          CURRENT_HEAD=$(printf '%s' "${PR_JSON}" | jq -r '.head.sha // empty')
          CURRENT_MERGE=$(printf '%s' "${PR_JSON}" | jq -r '.merge_commit_sha // empty')
          # Fail CLOSED: if either the build's analyzed revision or the current
          # PR head can't be resolved, skip — we must not analyze a possibly
          # stale binlog against the current diff (inline comments have no
          # commit_id and target the current PR diff).
          if [ -z "${BUILD_PR_SHA}" ] || [ -z "${CURRENT_HEAD}" ]; then
            echo "::warning::Could not resolve build revision ('${BUILD_PR_SHA}') and/or current PR head ('${CURRENT_HEAD}'); skipping to avoid analyzing a stale binlog against the current diff."
            emit_none
          fi
          if [ "${BUILD_PR_SHA}" != "${CURRENT_HEAD}" ]; then
            echo "::warning::Build ${BUILD_ID} analyzed revision '${BUILD_PR_SHA}' but PR #${PR_NUMBER} head is now '${CURRENT_HEAD}'; skipping stale build (a newer build/check will cover the current revision)."
            emit_none
          fi
          # When both merge revisions are known and differ, the base branch moved
          # since the build — the binlog reflects an obsolete merge. Skip.
          if [ -n "${BUILD_MERGE_SHA}" ] && [ -n "${CURRENT_MERGE}" ] && [ "${BUILD_MERGE_SHA}" != "${CURRENT_MERGE}" ]; then
            echo "::warning::Build ${BUILD_ID} merge revision '${BUILD_MERGE_SHA}' but PR #${PR_NUMBER} current merge is '${CURRENT_MERGE}' (base branch advanced); skipping stale merge."
            emit_none
          fi
          # Consistent now: build revision == current PR head. Use it for
          # permalinks so they line up with the inline comments' diff target.
          HEAD_SHA="${CURRENT_HEAD}"
          echo "Analyzing build ${BUILD_ID} at PR head revision '${HEAD_SHA}'."
          # --- 5. Download every logs artifact and extract binlogs ---
          # Pipelines that publish `<Leg>_Logs_Attempt<N>` get retry-aware
          # deduplication: keep only the highest attempt per leg, because an
          # earlier failed attempt may have been superseded by a successful
          # retry. For every other naming scheme, inspect all artifacts and let
          # extraction identify which ones contain binlogs.
          ado_get "artifact list of build ${BUILD_ID}" "${ADO_API}/build/builds/${BUILD_ID}/artifacts?api-version=7.1" || emit_none
          artifacts_json="${ADO_DOC}"
          mapfile -t names < <(printf '%s' "${artifacts_json}" | jq -r '
            .value // []
            | map(select(.name | test("_Logs_Attempt[0-9]+$")))
            | map({ leg:     (.name | sub("_Attempt[0-9]+$"; "")),
                    attempt: (.name | capture("_Attempt(?<n>[0-9]+)$") | .n | tonumber),
                    name:    .name })
            | group_by(.leg)
            | map(max_by(.attempt).name)
            | sort
            | .[]')
          ARTIFACT_LAYOUT="attempt"
          if [ "${#names[@]}" -eq 0 ]; then
            # Generic layout. There is no reliable name-only test for "this
            # artifact holds binlogs", so take every artifact and let extraction
            # decide; an artifact with no binlog inside is tolerated (but a
            # download/extract FAILURE is still fatal — see below).
            ARTIFACT_LAYOUT="leg"
            mapfile -t names < <(printf '%s' "${artifacts_json}" | jq -r '.value // [] | .[].name')
          fi
          [ "${#names[@]}" -eq 0 ] && { echo "::warning::No log artifacts on build ${BUILD_ID}."; emit_none; }
          echo "Artifact layout: ${ARTIFACT_LAYOUT} (${#names[@]} candidate artifact(s))."

          # --- 5a. Which failed legs never published logs at all? ---
          # The fail-closed check further down compares staged legs against the
          # artifacts ADO *returned*, so it cannot see a leg that died before
          # publishing its logs artifact — that leg is simply absent from
          # `names`. Ask the timeline instead. This is advisory rather than
          # fail-closed: a failed job that legitimately publishes no logs would
          # otherwise suppress analysis of a real compile break in the same
          # build. The agent is told about the gap so it cannot conclude "no
          # build failure" from the legs that happened to upload.
          #
          # Ask the timeline whether each leg's log *publish* succeeded rather
          # than guessing its artifact name from its display name. The two are
          # not spelled alike — the artifact is built from `$(Agent.Os)` and
          # `$(Agent.JobName)`, so on these shared arcade templates a `MacOS`
          # job publishes `..._Darwin_...` — and every name rule we tried
          # reported healthy legs as missing on real builds. Arcade's
          # `Publish Logs` task record answers the question directly, so no
          # spelling has to be inferred. A failed job carrying no such task —
          # `Monitor Helix Jobs`, which fails routinely here and publishes no
          # logs at all — does not stage logs and is not a missing leg.
          #
          # `canceled` and `abandoned` legs count alongside `failed`: they also
          # finish without logs, and are a real gap in the artifact set.
          # Write to a file, not a command substitution: `curl --retry` can only rewind
          # seekable output, so a retry would append to whatever a failed attempt had
          # already emitted and a *successful* retry would yield two concatenated
          # documents. That parses as neither, so a recoverable blip would look like an
          # unreadable timeline and needlessly disable the analysis below.
          TL_TMP=$(mktemp) || TL_TMP=""
          timeout 60 curl -sSL --fail --retry 3 --connect-timeout 10 --max-time 20 --retry-max-time 40 -o "${TL_TMP}" "${ADO_API}/build/builds/${BUILD_ID}/timeline?api-version=7.1" 2>/dev/null || true
          timeline_json=$(cat "${TL_TMP}" 2>/dev/null)
          rm -f "${TL_TMP}"
          MISSING_LEGS=""
          # An unreadable timeline must not look like a complete build. A failed
          # request, a non-JSON error page and an ADO error document all left
          # the list empty, which is exactly how "every failed leg published
          # logs" is reported — so a transient outage could let the agent
          # conclude "non-build failure" from an artifact set whose completeness
          # was never established. Probe for the `records` array first and
          # report an explicit unknown when it isn't there.
          timeline_ok=0
          if printf '%s' "${timeline_json}" | jq -e 'type == "object" and (.records | type == "array")' >/dev/null 2>&1; then
            timeline_ok=1
          fi
          if [ "${timeline_ok}" -eq 1 ]; then
            # Job display names come from the pipeline YAML in the PR branch, so
            # on a fork PR they are attacker-controlled. Strip control characters
            # and bound the length before this value reaches `$GITHUB_OUTPUT` and
            # `$GITHUB_ENV`, where an embedded newline would inject further
            # `key=value` lines. The task name is matched on its alphanumerics
            # because arcade spells it both `Publish logs` and `Publish Logs`,
            # and some pipelines prefix a decorative emoji.
            MISSING_LEGS=$(printf '%s' "${timeline_json}" | jq -r '
              (.records // []) as $records
              | ($records
                 | map(select(.type == "Task"
                              and (.name | ascii_downcase | gsub("[^a-z0-9]"; "") | test("publishlogs"))))) as $publishes
              | $records
              | map(select(.type == "Job"
                           and (.result == "failed" or .result == "canceled" or .result == "abandoned")))
              | map(. as $job
                    | ($publishes | map(select(.parentId == $job.id))) as $mine
                    | select(($mine | length) > 0
                             and (($mine | map(select(.result == "succeeded")) | length) == 0))
                    | ($job.name | gsub("[[:cntrl:]]"; " ")))
              | join(", ")' 2>/dev/null | tr -d '\r\n' | cut -c1-400)
          fi
          if [ "${timeline_ok}" -ne 1 ]; then
            MISSING_LEGS="(unknown - could not read the build timeline)"
            echo "::warning::Could not read the timeline for build ${BUILD_ID}; unable to verify that every failed leg published a logs artifact."
          elif [ -n "${MISSING_LEGS}" ]; then
            echo "::warning::Failed leg(s) whose logs were never published: ${MISSING_LEGS}"
          fi

          # Guards for untrusted PR-produced archives: cap the compressed
          # download and the reported uncompressed size per artifact, bound
          # extraction time, AND enforce a cumulative uncompressed budget across
          # all legs so many individually-small artifacts can't collectively
          # exhaust the runner's disk.
          # `MAX_ZIP_BYTES` is a *download* guard, not a size expectation: an
          # artifact that grows past a cap is silently dropped from the analysis
          # rather than reported, so a too-tight value quietly hides the very
          # failure the workflow exists to explain. (On dotnet/roslyn a 500 MB
          # cap excluded a 636 MB analyzer-logs artifact and the run produced
          # nothing.) The cumulative caps below are what actually bound the
          # runner's disk and network, so this one is set well clear of the
          # legitimate range.
          MAX_ZIP_BYTES=2147483648      # 2 GB compressed per artifact
          MAX_UNZIP_BYTES=2147483648    # 2 GB uncompressed per artifact
          MAX_TOTAL_BYTES=4294967296    # 4 GB uncompressed across all artifacts
          MAX_TOTAL_ZIP_BYTES=3221225472 # 3 GB compressed downloaded in total
          # `--max-time` is per attempt, so `--retry N` multiplies it: the whole
          # download phase, not one transfer, is what has to fit inside this job's
          # `timeout-minutes`. Give the loop a wall-clock deadline and derive every
          # transfer's budget from what is left of it, so no combination of slow
          # artifacts and retries can take the job down before the controlled no-op.
          FETCH_BUDGET=420           # 7 minutes for all artifact transfers
          MAX_ATTEMPT_SECONDS=120       # per attempt; the full set really takes ~30s
          FETCH_DEADLINE=$(( $(date +%s) + FETCH_BUDGET ))
          MAX_ARTIFACTS=40              # cap only; the real count is path-dependent
          TOTAL_BYTES=0
          TOTAL_ZIP_BYTES=0
          # One private scratch file for every download. A fixed /tmp name is a
          # pre-created symlink, or a second job on the same runner, away from being
          # someone else's file.
          ZIP_TMP=$(mktemp) || { echo "::warning::Could not create a temporary file for downloads."; emit_none; }
          # A private extraction directory, for the same reason as ZIP_TMP: a fixed
          # path is another job's directory on a runner we do not have to ourselves.
          AX_DIR=$(mktemp -d) || { echo "::warning::Could not create a temporary directory for extraction."; emit_none; }
          # Bound the work before starting: a pipeline change (or repeated leg
          # retries adding Attempt<N> artifacts) could grow the matched set well
          # past today's 10. Refuse rather than process a prefix of the list,
          # because a partial view is exactly what the fail-closed check below
          # exists to prevent.
          if [ "${#names[@]}" -gt "${MAX_ARTIFACTS}" ]; then
            echo "::warning::Build ${BUILD_ID} matched ${#names[@]} log artifacts, above the ${MAX_ARTIFACTS} cap; skipping."
            emit_none
          fi
          mkdir -p /tmp/binlogs
          # Only binlogs extracted by this run may be analyzed. Anything left in
          # the directory by an earlier run on the same runner would otherwise be
          # uploaded and attributed to this build.
          rm -f /tmp/binlogs/*.binlog
          count=0
          staged_legs=0
          # Artifacts we tried to use but could not read (download, size-guard or
          # extraction failure). Always fatal: a leg we failed to READ may be the
          # one that broke the build. Distinct from an artifact that extracted
          # fine and simply held no binlog, which is normal in the `leg` layout.
          legs_failed=0
          budget_hit=0
          ai=0
          for name in "${names[@]}"; do
            # `name` is PR-controlled ADO artifact metadata and the
            # `_Logs_Attempt<N>` filter only anchors the suffix, so sanitize it
            # before using it in any on-disk path (guards against `/` or `..`
            # traversal); keep the original `name` for the artifacts_json lookup.
            safe_name=$(printf '%s' "${name}" | tr -c 'A-Za-z0-9._-' '_')
            ai=$((ai + 1))
            url=$(printf '%s' "${artifacts_json}" | jq -r --arg n "${name}" '.value[] | select(.name==$n) | .resource.downloadUrl // empty')
            [ -z "${url}" ] && { echo "::warning::No download URL for ${safe_name}."; legs_failed=$((legs_failed + 1)); continue; }
            find "${AX_DIR:?}" -mindepth 1 -delete
            : > "${ZIP_TMP}"
            # Hard-cap the bytes written to disk regardless of Content-Length:
            # `ulimit -f` bounds what this subshell may write, and the size check
            # below is authoritative. Total time is bounded too. This
            # closes the gap where `curl --max-filesize` alone would let a
            # length-less response write unbounded data before any post-check.
            #
            # Bound this transfer by whatever is left of the cumulative budget
            # as well as by the per-artifact cap. Checking the cumulative total
            # only *after* the transfer would let a download start just under
            # the limit and still pull a further MAX_ZIP_BYTES, making the real
            # ceiling `MAX_TOTAL_ZIP_BYTES + MAX_ZIP_BYTES`.
            ZIP_CAP="${MAX_ZIP_BYTES}"
            ZIP_ALLOWANCE=$((MAX_TOTAL_ZIP_BYTES - TOTAL_ZIP_BYTES))
            [ "${ZIP_ALLOWANCE}" -lt "${ZIP_CAP}" ] && ZIP_CAP="${ZIP_ALLOWANCE}"
            if [ "${ZIP_CAP}" -le 0 ]; then
              echo "::warning::Cumulative compressed download budget ${MAX_TOTAL_ZIP_BYTES} is exhausted before ${safe_name}; stopping downloads."; budget_hit=1; break
            fi
            # Bound this transfer by the time left as well, and never start one with
            # no time to finish in.
            TIME_LEFT=$(( FETCH_DEADLINE - $(date +%s) ))
            if [ "${TIME_LEFT}" -le 0 ]; then
              echo "::warning::Download time budget ${FETCH_BUDGET}s exhausted before ${safe_name}; stopping downloads."; budget_hit=1; break
            fi
            ATTEMPT_SECONDS="${MAX_ATTEMPT_SECONDS}"
            [ "${TIME_LEFT}" -lt "${ATTEMPT_SECONDS}" ] && ATTEMPT_SECONDS="${TIME_LEFT}"
            # `--retry-max-time` only gates whether curl may *start* another retry, so a
            # retry begun just inside it can still run a further `--max-time`. `timeout`
            # around the whole invocation is what makes the deadline real rather than a
            # scheduling hint; a killed transfer is treated like any other failed one and
            # the leg is reported as missing, which fails closed.
            # Download to a file, never a pipe: curl can only rewind seekable output, so
            # through a pipe a retried body is *appended* and a 503 error page followed
            # by a successful retry yields a corrupt `<error page><zip>` that can still
            # pass the size guards, only to make `unzip` return warning status 1 later
            # and drop the leg. `--fail` additionally keeps HTTP error bodies out of the
            # file. `ulimit -f` is the disk backstop for responses that declare no
            # Content-Length; the size check below is authoritative. The block count is
            # rounded UP so any positive ZIP_CAP still buys at least one block. SIGXFSZ
            # is ignored so hitting the cap is an ordinary write error.
            (
              # Fail the leg rather than the backstop: if the shell will not apply
              # the limit, downloading anyway would leave a response with no usable
              # Content-Length free to fill the disk before the size check below runs.
              # bash counts `ulimit -f` in 1024-byte units, except in POSIX mode where
              # it counts 512-byte blocks. Pin the mode so this arithmetic means one
              # thing regardless of how the runner's shell was invoked.
              set +o posix
              ulimit -f $(( (ZIP_CAP + 1023) / 1024 )) || exit 1
              trap '' XFSZ
              timeout "${TIME_LEFT}" curl -sSL --fail --retry 3 --retry-delay 2 \
                --connect-timeout 15 --max-time "${ATTEMPT_SECONDS}" \
                --retry-max-time "${TIME_LEFT}" -o "${ZIP_TMP}" "${url}"
            ) 2>/dev/null
            curl_rc=$?
            ZIP_BYTES=$(stat -c%s "${ZIP_TMP}" 2>/dev/null || echo 0)
            # Charge the budget with the bytes retained on disk, including those of an
            # artifact about to be skipped. This is a disk and extraction budget, not a
            # meter of network egress: `-o` truncates before each retry, so failed
            # attempts are not counted here. What bounds those is FETCH_DEADLINE via
            # the `timeout` wrapper, plus `ulimit -f`, which caps every individual
            # attempt at ZIP_CAP.
            TOTAL_ZIP_BYTES=$((TOTAL_ZIP_BYTES + ZIP_BYTES))
            # A timed-out, killed or size-limited transfer can still leave a file that
            # happens to parse as a ZIP; without this the leg would be accepted from a
            # truncated download. Skipping fails closed via the completeness check.
            if [ "${curl_rc}" -ne 0 ]; then
              echo "::warning::Skipping ${safe_name}: download failed or was truncated (curl exit ${curl_rc})."; legs_failed=$((legs_failed + 1)); continue
            fi
            if [ "${ZIP_BYTES}" -eq 0 ]; then
              echo "::warning::Skipping ${safe_name}: empty or failed download."; legs_failed=$((legs_failed + 1)); continue
            fi
            if [ "${ZIP_BYTES}" -gt "${ZIP_CAP}" ]; then
              echo "::warning::Skipping ${safe_name}: download exceeded the ${ZIP_CAP}-byte cap."; legs_failed=$((legs_failed + 1)); continue
            fi
            UNCOMP=$(unzip -l "${ZIP_TMP}" 2>/dev/null | tail -1 | awk '{print $1}')
            # Fail safe: if the uncompressed size isn't a plain integer (corrupt
            # zip / unexpected `unzip -l` output), we can't verify it — skip the
            # artifact rather than let a non-numeric value bypass the `-gt` guard.
            if ! printf '%s' "${UNCOMP}" | grep -qE '^[0-9]+$'; then
              echo "::warning::Skipping ${safe_name}: could not determine uncompressed size (unparseable unzip output)."; legs_failed=$((legs_failed + 1)); continue
            fi
            # ZIP64 uncompressed sizes can reach ~20 digits — beyond Bash's
            # signed 64-bit range, where `-gt` (and the cumulative `$((...))`
            # below) error out and, under `set +e`, would let an oversized
            # archive slip past the guard. Any value with more digits than the
            # limit is unambiguously larger, so reject on decimal length first;
            # after this, UNCOMP fits safely in the integer range used below.
            if [ "${#UNCOMP}" -gt "${#MAX_UNZIP_BYTES}" ]; then
              echo "::warning::Skipping ${safe_name}: uncompressed size has ${#UNCOMP} digits, exceeding the ${MAX_UNZIP_BYTES} guard (possible zip bomb)."; legs_failed=$((legs_failed + 1)); continue
            fi
            if [ "${UNCOMP}" -gt "${MAX_UNZIP_BYTES}" ]; then
              echo "::warning::Skipping ${safe_name}: uncompressed size ${UNCOMP} exceeds ${MAX_UNZIP_BYTES} guard (possible zip bomb)."; legs_failed=$((legs_failed + 1)); continue
            fi
            if [ $((TOTAL_BYTES + UNCOMP)) -gt "${MAX_TOTAL_BYTES}" ]; then
              echo "::warning::Cumulative uncompressed budget ${MAX_TOTAL_BYTES} reached at ${safe_name}; stopping extraction."; budget_hit=1; break
            fi
            # Refuse the archive if any entry path is absolute or has a `..`
            # component (defense-in-depth over unzip's own traversal guard),
            # then extract `*.binlog` entries *preserving* their in-archive
            # paths (no `-j`) under a fresh dir + timeout, so two binlogs that
            # share a basename in different folders don't overwrite each other.
            if unzip -Z1 "${ZIP_TMP}" 2>/dev/null | grep -qE '(^/|(^|/)\.\.(/|$))'; then
              echo "::warning::Skipping ${safe_name}: archive has a suspicious (absolute or ..) entry path."; legs_failed=$((legs_failed + 1)); continue
            fi
            # `unzip` exit 11 means "no files matched" -- the artifact simply
            # carries no binlog. In the `leg` layout the candidate set is every
            # artifact on the build, so non-log artifacts (e.g.
            # `BuildConfiguration`) legitimately hit this; it is not a read
            # failure and must not fail the run closed. Any other non-zero exit
            # (corrupt archive, timeout) still counts as an unreadable leg.
            #
            # Both cases `continue`, so nothing was written to "${AX_DIR}" and the
            # uncompressed budget below is left untouched. Charging it for an
            # archive that extracted nothing would let one large binlog-free
            # artifact push a genuinely useful later leg past MAX_TOTAL_BYTES
            # and trip the fail-closed check on a build that was fine.
            uz=0
            # Extraction shares the deadline with the transfers. Otherwise a run that
            # spent most of its budget downloading could still queue one bounded
            # extraction per artifact and walk the job past `timeout-minutes` without
            # ever reaching the controlled no-op below.
            TIME_LEFT=$(( FETCH_DEADLINE - $(date +%s) ))
            if [ "${TIME_LEFT}" -le 0 ]; then
              echo "::warning::Fetch budget exhausted before extracting ${safe_name}; stopping."; budget_hit=1; break
            fi
            [ "${TIME_LEFT}" -gt 120 ] && TIME_LEFT=120
            timeout "${TIME_LEFT}" unzip -o "${ZIP_TMP}" '*.binlog' -d "${AX_DIR}" >/dev/null 2>&1 || uz=$?
            if [ "${uz}" -eq 11 ]; then
              echo "${safe_name}: no binlog inside; nothing to stage from this artifact."; continue
            fi
            if [ "${uz}" -ne 0 ]; then
              echo "::warning::Skipping ${safe_name}: extraction failed or timed out (unzip exit ${uz})."; legs_failed=$((legs_failed + 1)); continue
            fi
            # Consume the cumulative budget only once the archive actually
            # extracted — not on a suspicious-path or extraction-failure skip
            # above — so a skipped leg can't wrongly exhaust the budget and
            # force later legs to be dropped as "incomplete".
            TOTAL_BYTES=$((TOTAL_BYTES + UNCOMP))
            i=0
            leg_staged=0
            while IFS= read -r bl; do
              [ -f "${bl}" ] || continue
              # Every destination is uniquely prefixed with the artifact index
              # (`ai`) and a per-file counter (`i`), so neither a cross-artifact
              # sanitize collision nor same-basename entries within one archive
              # can overwrite a previously staged leg's binlog. `safe_name` is
              # kept only for readability.
              dest="/tmp/binlogs/${ai}_${i}_${safe_name}.binlog"
              # Only count a staged binlog when the copy actually succeeds —
              # `set +e` is on, so a failed `cp` must not inflate the counts.
              if cp "${bl}" "${dest}"; then
                count=$((count + 1))
                i=$((i + 1))
                leg_staged=1
              else
                echo "::warning::Failed to stage ${bl}; skipping."
              fi
            done < <(find "${AX_DIR}" -type f -name '*.binlog')
            # This leg produced at least one usable binlog.
            [ "${leg_staged}" -eq 1 ] && staged_legs=$((staged_legs + 1))
          done
          rm -rf "${AX_DIR:?}" "${ZIP_TMP}"
          echo "Extracted ${count} binlog(s) from ${staged_legs}/${#names[@]} artifact(s) into /tmp/binlogs:"
          ls -la /tmp/binlogs || true
          [ "${count}" -eq 0 ] && { echo "::warning::No *.binlog found in any log artifact of build ${BUILD_ID}."; emit_none; }
          # Fail CLOSED on a partial set. Activating on an incomplete view would
          # let the agent treat the retrieved legs as the whole build and
          # mis-classify a real break in a missing leg as a clean compile /
          # non-build failure. A later build/check re-triggers the analysis.
          #
          # What counts as "partial" depends on the layout: in the `attempt`
          # layout every matched artifact is a logs artifact, so any leg that
          # yielded no binlog is a gap. In the `leg` layout the candidate set is
          # *every* artifact on the build, some of which legitimately carry no
          # binlog, so only a read FAILURE (or a truncated run) is a gap.
          if [ "${budget_hit}" -ne 0 ]; then
            echo "::warning::Stopped early on a size budget, so some legs were never inspected; skipping to avoid analyzing an incomplete build."
            emit_none
          fi
          if [ "${legs_failed}" -ne 0 ]; then
            echo "::warning::${legs_failed} log artifact(s) could not be downloaded or extracted; skipping to avoid analyzing an incomplete build (an unreadable leg could be the one that failed)."
            emit_none
          fi
          if [ "${ARTIFACT_LAYOUT}" = "attempt" ] && [ "${staged_legs}" -ne "${#names[@]}" ]; then
            echo "::warning::Only ${staged_legs} of ${#names[@]} *_Logs_Attempt* legs produced a usable binlog; skipping to avoid analyzing an incomplete build (a missing leg could be the one that failed)."
            emit_none
          fi

          # The download/extract loop above can take minutes. Re-read the PR
          # head right before activating and fail CLOSED if it moved or can't
          # be resolved: a force-push during that window would otherwise leave
          # the analyzed binlog stale relative to the current diff (inline
          # comments carry no commit_id and target the current diff).
          LATEST_PR=$(gh api "repos/${GH_AW_REPO}/pulls/${PR_NUMBER}" 2>/dev/null)
          LATEST_HEAD=$(printf '%s' "${LATEST_PR}" | jq -r '.head.sha // empty')
          LATEST_MERGE=$(printf '%s' "${LATEST_PR}" | jq -r '.merge_commit_sha // empty')
          if [ -z "${LATEST_HEAD}" ] || [ "${LATEST_HEAD}" != "${HEAD_SHA}" ]; then
            echo "::warning::PR #${PR_NUMBER} head changed during artifact download ('${HEAD_SHA}' -> '${LATEST_HEAD}') or could not be re-resolved; skipping to avoid posting stale-build suggestions against the new diff."
            emit_none
          fi
          # The base branch may also have advanced during the download; if the
          # merge revision moved from what the build analyzed, skip (stale merge).
          if [ -n "${BUILD_MERGE_SHA}" ] && [ -n "${LATEST_MERGE}" ] && [ "${LATEST_MERGE}" != "${BUILD_MERGE_SHA}" ]; then
            echo "::warning::PR #${PR_NUMBER} merge revision changed during artifact download ('${BUILD_MERGE_SHA}' -> '${LATEST_MERGE}'); skipping stale merge."
            emit_none
          fi

          {
            # `missing-legs` is derived from ADO job display names, which come
            # from pipeline YAML in the PR branch and are therefore
            # fork-controlled. It is sanitized where it is assembled, and it is
            # written first here so that even a future regression in that
            # sanitizing cannot let it override a key emitted below.
            echo "missing-legs=${MISSING_LEGS}"
            echo "binlog-found=true"
            echo "pr-number=${PR_NUMBER}"
            echo "pr-head-sha=${HEAD_SHA}"
            echo "pr-merge-sha=${BUILD_MERGE_SHA}"
            echo "ado-build-id=${BUILD_ID}"
            echo "ado-build-url=${ADO_BUILD_UI}?buildId=${BUILD_ID}"
          } >> "$GITHUB_OUTPUT"

      - name: Upload analysis artifact
        if: steps.fetch.outputs.binlog-found == 'true'
        uses: actions/upload-artifact@v7.0.1
        with:
          name: build-failure-analysis-data
          path: /tmp/binlogs
          if-no-files-found: warn
          # Quoted so the import's YAML round-trip keeps it `1` — an unquoted
          # integer comes back out of the shared-job merge as `1.0`.
          retention-days: "1"
---
