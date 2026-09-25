---
# Packaged with a workflow-specific filename to avoid collisions in consumers.
description: >-
  Shared `fetch-binlog` job for the Build Failure Analysis workflows. Resolves
  the PR's failed `dotnet-sdk-public-ci` Azure DevOps build, downloads the
  binary logs that build already produced and stages them for the analysis
  agent. It performs **no build**: it only reads published build artifacts.

# Both Build Failure Analysis workflows — the automatic one (`check_run`) and
# the `/analyze-build-failure` slash command (`issue_comment`) — need exactly
# the same download engine, but they cannot be a single workflow: gh-aw's
# `roles:` is one workflow-scoped gate, and the two need different ones. The
# automatic analysis is advisory and must run on **every** failing PR including
# external contributors' (`roles: all`), while the slash command spends the
# download on demand and is restricted to `[admin, maintainer, write]`.
#
# So the job lives here instead and both workflows import it. Only the build/PR
# resolution differs by trigger, and that is a single `if` at the top of the
# script; everything after it — artifact enumeration, missing-leg detection,
# the download/extraction budgets and the fail-closed completeness checks — is
# shared, so a fix lands in both workflows at once.
jobs:
  fetch-binlog:
    name: Fetch binlogs (Azure Pipelines)
    runs-on: ubuntu-latest
    timeout-minutes: 15
    # Cheap pre-gate covering every trigger this job is imported under. Each
    # importing workflow only ever fires one of these branches; the others are
    # simply never true.
    #
    # `check_run` fires for every check on a commit, so only the rollup
    # `dotnet-sdk-public-ci` check reporting failure is acted on.
    #
    # The `issue_comment` branch matters most: this job is a dependency of
    # gh-aw's `pre_activation`, so it runs BEFORE the role / command-position
    # check. Without a guard it would download hundreds of MB of binlogs on
    # *every* comment in the repository, which any public commenter could
    # trigger repeatedly. This expression is only the free first filter —
    # `author_association` is coarse (in an org-owned repo every org member
    # reports MEMBER regardless of the permission they actually hold here), so
    # the step below resolves the commenter's real repository permission before
    # anything is downloaded. `pre_activation` remains the authoritative role +
    # command-position check, and `activation` additionally requires
    # `binlog-found == 'true'`.
    if: >-
      github.event_name == 'workflow_dispatch' ||
      (github.event_name == 'check_run' &&
       github.event.check_run.name == 'dotnet-sdk-public-ci' &&
       github.event.check_run.conclusion == 'failure') ||
      (github.event_name == 'issue_comment' &&
       github.event.repository.fork == false &&
       github.event.issue.pull_request &&
       contains(fromJSON('["OWNER","MEMBER","COLLABORATOR"]'), github.event.comment.author_association) &&
       contains(github.event.comment.body, '/analyze-build-failure'))
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
      # Slash command only. Two things have to be true before this job is
      # allowed to spend a ~600MB download, and neither can be expressed in the
      # job-level `if:`: the comment must actually INVOKE the command (not just
      # mention it — `contains()` is a substring test), and the commenter must
      # really have write access (`author_association` cannot tell an org member
      # with read-only access apart from a maintainer). Both are checked here,
      # before any download. `pre_activation` remains the authoritative role +
      # command-position check, and `activation` additionally requires
      # `binlog-found == 'true'`; this step exists because gh-aw schedules
      # `pre_activation` AFTER this job, so its checks come too late to prevent
      # the cost. KEEP IN SYNC with `roles:` in build-failure-analysis-command.md.
      #
      # `.permission` is the field to test. The REST docs for this endpoint say
      # it returns the legacy base roles admin|write|read|none, "where the
      # maintain role is mapped to write and the triage role is mapped to read",
      # so `admin|write` is exactly "has push access or better" — precisely the
      # set `roles: [admin, maintainer, write]` describes, maintainers included.
      #
      # `.role_name` is deliberately NOT consulted. It reports "the name of the
      # assigned role, including custom roles", and a custom organization role
      # only has to avoid the base names read/triage/write/maintain/admin — so
      # matching on it would let a role merely *named* like a privileged one
      # (say a custom `maintainer` inheriting read) clear this gate with no push
      # access at all.
      #
      # On any API failure the response carries no `.permission`, so the check
      # falls into the deny branch; failing closed is the safe direction here.
      - name: Verify the comment invokes the command and the commenter has write access
        id: perm
        if: github.event_name == 'issue_comment'
        shell: bash
        env:
          GH_TOKEN: ${{ github.token }}
          COMMENTER: ${{ github.event.comment.user.login }}
          COMMENT_BODY: ${{ github.event.comment.body }}
          COMMAND_NAME: "analyze-build-failure"
        run: |
          set +e
          authorized=false
          # --- 1. Command position (free; do this before the API call) ------
          # The job-level `if:` can only use `contains()`, a plain substring
          # test, so it also fires on "see /analyze-build-failure above" or on
          # the command quoted inside an unrelated comment — each of which costs
          # a runner and, past this gate, a ~600MB download. `pre_activation`
          # does the real check, but it runs AFTER this job. Reproduce it here.
          #
          # gh-aw trims the body and requires the command to be the FIRST token:
          # `/^\/([a-zA-Z0-9][a-zA-Z0-9._-]*)(?=$|\s)/` over the trimmed text,
          # then an equality comparison on the captured name
          # (actions/setup/js/slash_command_matcher.cjs). `awk 'NF {print $1;
          # exit}'` is the same rule: skip leading whitespace/blank lines, take
          # the first whitespace-delimited token. The token is delimited by
          # whitespace or end-of-input, which is exactly the `(?=$|\s)`
          # lookahead, so `/analyze-build-failure-now` correctly does NOT match.
          # `tr -d '\r'` is needed because JS `.trim()` and `\s` treat CR as
          # whitespace while awk's default field splitting does not.
          # KEEP IN SYNC with `on.command.name` in build-failure-analysis-command.md.
          first_word=$(printf '%s' "${COMMENT_BODY}" | tr -d '\r' | awk 'NF {print $1; exit}')
          if [ "${first_word}" != "/${COMMAND_NAME}" ]; then
            # Never echo the raw token: it is attacker-controlled and `::`-
            # prefixed text is interpreted by the runner as a workflow command.
            safe_word=$(printf '%s' "${first_word}" | tr -cd 'A-Za-z0-9/._-' | cut -c1-40)
            echo "Comment does not start with '/${COMMAND_NAME}' (first token: '${safe_word}'); skipping the binlog download."
            echo "authorized=false" >> "$GITHUB_OUTPUT"
            exit 0
          fi
          # --- 2. Repository permission -------------------------------------
          # `github.event.comment.user.login` is GitHub-supplied, so this value
          # is already trustworthy. The shape check is kept anyway so the gate
          # never interpolates anything but a plausible login into an API path
          # or into log output.
          case "${COMMENTER}" in
            ""|*[!A-Za-z0-9-]*)
              [ -n "${COMMENTER}" ] && echo "::warning::Ignoring implausible actor name from the event payload."
              COMMENTER="" ;;
          esac
          if [ -z "${COMMENTER}" ]; then
            echo "::warning::No commenter resolved from the event; skipping the binlog download."
          else
            resp=$(gh api "repos/${GITHUB_REPOSITORY}/collaborators/${COMMENTER}/permission" 2>/dev/null)
            # Extract with `jq` rather than `gh api --jq`: on a non-2xx response
            # `gh` prints the error document to stdout, which `--jq` does not
            # filter, so the raw JSON would land in `perm` and be echoed into
            # the log. Reading the field ourselves yields "" for any error shape.
            perm=$(printf '%s' "${resp}" | jq -r '.permission // empty' 2>/dev/null)
            case "${perm}" in
              admin|write) authorized=true ;;
              *)           authorized=false ;;
            esac
            if [ "${authorized}" = "true" ]; then
              echo "'${COMMENTER}' has '${perm}' access to ${GITHUB_REPOSITORY}; proceeding."
            else
              echo "::warning::'${COMMENTER}' does not have write access to ${GITHUB_REPOSITORY} (resolved permission '${perm:-none}'); skipping the binlog download."
            fi
          fi
          echo "authorized=${authorized}" >> "$GITHUB_OUTPUT"

      - name: Download binlogs from the failed Azure Pipelines build
        id: fetch
        # The gate above only runs for the slash command; on `check_run` /
        # `workflow_dispatch` it is skipped and its output is empty, so the
        # first clause lets those triggers through unchanged.
        if: github.event_name != 'issue_comment' || steps.perm.outputs.authorized == 'true'
        shell: bash
        env:
          GH_TOKEN: ${{ github.token }}
          GH_AW_REPO: ${{ github.repository }}
          ADO_API: "https://dev.azure.com/dnceng-public/public/_apis"
          ADO_BUILD_UI: "https://dev.azure.com/dnceng-public/public/_build/results"
          # dotnet-sdk-public-ci pipeline definition id in dnceng-public/public
          # (used to validate the resolved build belongs to the right pipeline).
          ADO_BUILD_DEFINITION_ID: "101"
          EVENT_NAME: ${{ github.event_name }}
          # `check_run` payload.
          CHECK_DETAILS_URL: ${{ github.event.check_run.details_url }}
          CHECK_PR_NUMBER: ${{ github.event.check_run.pull_requests[0].number }}
          # `workflow_dispatch` inputs, read from `github.event.inputs` rather
          # than the `inputs` context: `inputs` only exists for dispatch/call
          # workflows, while `github.event.inputs` is simply absent (empty) on
          # the slash-command event, so one shared job can reference both.
          DISPATCH_BUILD_ID: ${{ github.event.inputs['ado-build-id'] }}
          DISPATCH_PR_NUMBER: ${{ github.event.inputs['pr-number'] }}
          # Slash-command payload (inline `issue_comment`, or `aw_context` when
          # the command is routed through a central dispatcher).
          COMMENT_PR_NUMBER: ${{ github.event.issue.number || fromJSON(github.event.inputs.aw_context || github.event.client_payload.aw_context || '{}').item_number }}
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
          # This is the ONLY part of the job that differs between the two
          # workflows, because they learn about the build in opposite
          # directions:
          #
          #   * `check_run` / `workflow_dispatch` are TOLD which build to look
          #     at — the check payload names it in `details_url`, a manual
          #     dispatch passes it explicitly — so the build is resolved first
          #     and the PR is derived from it.
          #   * `issue_comment` (the slash command) is told nothing about a
          #     build: it is a request to re-analyse whatever the PR's newest
          #     build is, so the PR comes first and the build is looked up from
          #     it. That build is usable only once it has COMPLETED; a still
          #     running newest build (e.g. right after a force-push) would
          #     otherwise pair an older failure with the PR's current head.
          #
          # Both branches end with BUILD_ID, build_json and PR_NUMBER set, and
          # everything below this block is common to both.
          if [ "${EVENT_NAME}" = "issue_comment" ]; then
            PR_NUMBER="${COMMENT_PR_NUMBER}"
            [ -z "${PR_NUMBER}" ] && { echo "::warning::No PR number resolved from the slash-command event / aw_context."; emit_none; }
            # PR_NUMBER feeds GitHub API paths and the `refs/pull/<n>/merge`
            # branch query; require it numeric so a malformed event/aw_context
            # payload can't reach those URLs with unexpected content.
            if ! printf '%s' "${PR_NUMBER}" | grep -qE '^[0-9]+$'; then
              echo "::warning::Resolved PR number '${PR_NUMBER}' is not numeric; refusing."; emit_none
            fi
            # Newest build for the PR's merge ref REGARDLESS of status
            # (queue-time descending), so a build queued after an older failure
            # is seen rather than the stale one being analysed silently.
            ado_get "build list for PR #${PR_NUMBER}" \
              "${ADO_API}/build/builds?definitions=${ADO_BUILD_DEFINITION_ID}&branchName=refs/pull/${PR_NUMBER}/merge&queryOrder=queueTimeDescending&\$top=1&api-version=7.1" || emit_none
            builds_json="${ADO_DOC}"
            BUILD_ID=$(printf '%s' "${builds_json}" | jq -r '.value // [] | .[0].id // empty')
            BUILD_STATUS=$(printf '%s' "${builds_json}" | jq -r '.value // [] | .[0].status // empty')
            echo "Newest dotnet-sdk-public-ci build for PR #${PR_NUMBER}: id='${BUILD_ID}' status='${BUILD_STATUS}'"
            [ -z "${BUILD_ID}" ] && { echo "::warning::No dotnet-sdk-public-ci build found for PR #${PR_NUMBER}."; emit_none; }
            # Require a numeric build id before it feeds subsequent ADO API
            # URLs, so a malformed query response can't inject path/query.
            if ! printf '%s' "${BUILD_ID}" | grep -qE '^[0-9]+$'; then
              echo "::warning::ADO build id '${BUILD_ID}' is not numeric; refusing."; emit_none
            fi
            if [ "${BUILD_STATUS}" != "completed" ]; then
              echo "::warning::PR #${PR_NUMBER}'s newest dotnet-sdk-public-ci build (${BUILD_ID}) is still '${BUILD_STATUS}'; wait for it to finish before analysing."
              emit_none
            fi
            ado_get "details of build ${BUILD_ID}" "${ADO_API}/build/builds/${BUILD_ID}?api-version=7.1" || emit_none
            build_json="${ADO_DOC}"
          else
            if [ "${EVENT_NAME}" = "workflow_dispatch" ]; then
              BUILD_ID="${DISPATCH_BUILD_ID}"
            else
              # details_url looks like: .../_build/results?buildId=NNN&view=...
              BUILD_ID=$(printf '%s' "${CHECK_DETAILS_URL}" | grep -oE 'buildId=[0-9]+' | head -1 | cut -d= -f2)
            fi
            echo "Azure DevOps build id: '${BUILD_ID}'"
            [ -z "${BUILD_ID}" ] && { echo "::warning::Could not resolve an ADO build id."; emit_none; }
            # The build id feeds directly into ADO API URLs below; require it to
            # be purely numeric (esp. on workflow_dispatch, where it is free-form
            # input) so a malformed value can't alter the request path/query.
            if ! printf '%s' "${BUILD_ID}" | grep -qE '^[0-9]+$'; then
              echo "::warning::Resolved ADO build id '${BUILD_ID}' is not numeric; refusing."; emit_none
            fi
            # The build metadata is the authoritative source for the PR number
            # (via sourceBranch) as well as for the definition / result /
            # revision validated in step 3.
            ado_get "details of build ${BUILD_ID}" "${ADO_API}/build/builds/${BUILD_ID}?api-version=7.1" || emit_none
            build_json="${ADO_DOC}"
            # A PR build's sourceBranch is exactly `refs/pull/<n>/merge`, so it
            # identifies the PR unambiguously — unlike the commit->PRs API,
            # which can return several PRs in an unspecified order.
            BUILD_PR_NUM=$(printf '%s' "${build_json}" | jq -r '.sourceBranch // empty' | sed -n 's#^refs/pull/\([0-9]\{1,\}\)/merge$#\1#p')
            if [ "${EVENT_NAME}" = "workflow_dispatch" ]; then
              PR_NUMBER="${DISPATCH_PR_NUMBER}"
            else
              # Prefer the PR named by the build's own sourceBranch
              # (authoritative) over check_run.pull_requests[0], whose order
              # isn't guaranteed and can name a different PR sharing the commit.
              PR_NUMBER="${BUILD_PR_NUM:-${CHECK_PR_NUMBER}}"
            fi
            [ -z "${PR_NUMBER}" ] && { echo "::warning::Could not resolve a PR number."; emit_none; }
            # PR_NUMBER feeds `gh api .../pulls/<n>` and the `refs/pull/<n>/merge`
            # comparison; require it numeric so a malformed value can't reach the
            # GitHub API path (traversal-like input) or skew the branch match.
            if ! printf '%s' "${PR_NUMBER}" | grep -qE '^[0-9]+$'; then
              echo "::warning::Resolved PR number '${PR_NUMBER}' is not numeric; refusing."; emit_none
            fi
          fi
          RESULT=$(printf '%s' "${build_json}" | jq -r '.result // empty')
          DEF_ID=$(printf '%s' "${build_json}" | jq -r '.definition.id // empty')
          SRC_BRANCH=$(printf '%s' "${build_json}" | jq -r '.sourceBranch // empty')

          # --- 2. Scope check: only analyse PRs targeting main / release/* ---
          PR_JSON=$(gh api "repos/${GH_AW_REPO}/pulls/${PR_NUMBER}" 2>/dev/null)
          BASE_REF=$(printf '%s' "${PR_JSON}" | jq -r '.base.ref // empty')
          case "${BASE_REF}" in
            main|release/*) echo "PR #${PR_NUMBER} base '${BASE_REF}' is in scope." ;;
            *) echo "::warning::PR #${PR_NUMBER} base '${BASE_REF}' is out of scope (main, release/*); skipping."; emit_none ;;
          esac

          # --- 3. Validate the build, whichever way it was resolved ---
          # It must be the dotnet-sdk-public-ci definition (101), have failed,
          # and belong to this PR (sourceBranch == refs/pull/<PR>/merge). No
          # entry point is fully trusted: `check_run` parses the build id out of
          # a check payload, dispatch takes the build id and PR number as
          # independent free-form inputs, and the slash command derives the
          # build from a query. Validating here — rather than per trigger —
          # prevents downloading an unrelated build or posting its analysis to
          # the wrong PR no matter how the build was found.
          echo "ADO build ${BUILD_ID}: result='${RESULT}' definition='${DEF_ID}' sourceBranch='${SRC_BRANCH}'"
          if [ "${DEF_ID}" != "${ADO_BUILD_DEFINITION_ID}" ]; then
            echo "::warning::ADO build ${BUILD_ID} is definition '${DEF_ID}', not dotnet-sdk-public-ci (${ADO_BUILD_DEFINITION_ID}); refusing."; emit_none
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
          # The SDK pipeline publishes one logs artifact per build leg, each
          # holding that leg's `log/<Configuration>/*.binlog`, but the artifact
          # NAME depends on the target branch even though the definition id is
          # the same (101):
          #   * `main`      -> `<Leg>_Logs_Attempt<N>` (e.g. `Windows_x64_Logs_Attempt1`,
          #                    `Linux_arm64_AOT_Logs_Attempt1`). A retried leg
          #                    publishes ONE ARTIFACT PER ATTEMPT, so keep only the
          #                    highest `<N>` per leg: `Attempt1` holds the logs of a
          #                    superseded run, and a leg that failed on attempt 1 and
          #                    passed on attempt 2 would otherwise hand the agent a
          #                    binlog full of errors that no longer exist — it would
          #                    then confidently report an already-fixed failure.
          #                    (Real example: build 1535012 publishes both
          #                    `Windows_x64_FullFramework_Logs_Attempt1` and
          #                    `..._Attempt2`.)
          #   * `release/*` -> `<Leg>` (e.g. `TestBuild_linux_x64`, `AoT_macOS_x64`)
          # Both carry the same `log/<Configuration>/*.binlog` tree inside, so
          # only the match differs. Matching just the `main` shape would make the
          # workflow a silent no-op on every `release/*` PR (0 artifacts matched
          # -> binlog-found=false -> agent skipped), which is exactly the class of
          # failure that looks green forever, so handle both.
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
            # `release/*` layout. There is no reliable name-only test for "this
            # artifact holds binlogs", so take every artifact and let the
            # extraction decide; an artifact with no binlog inside is tolerated
            # (but a download/extract FAILURE is still fatal — see below).
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
