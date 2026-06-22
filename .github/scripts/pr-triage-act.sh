#!/usr/bin/env bash
# pr-triage-act.sh
#
# Per-PR triage worker. Recomputes the PR's state from GitHub API data and
# applies one of: state-label reconciliation, eval-trigger, author-ping,
# maintainer-ping. Cool-downs and idempotency are enforced via marker
# comments and label/run lookups.
#
# Required env:
#   GH_TOKEN        — workflow token with pull-requests:write, issues:write
#   PR_NUMBER       — PR to act on
#   GITHUB_REPOSITORY (set by Actions)
#
# Optional env:
#   COOLDOWN_DAYS       — default 4
#   FIRST_PING_AGE_MIN  — default 30
#   DRY_RUN             — "true" to log but make no writes
#   INTENDED_STATE      — informational; worker re-validates regardless
#
# Exits 0 on success (including no-op). Non-zero only on hard failures.

set -euo pipefail

: "${GH_TOKEN:?GH_TOKEN is required}"
: "${PR_NUMBER:?PR_NUMBER is required}"
: "${GITHUB_REPOSITORY:?GITHUB_REPOSITORY is required}"

COOLDOWN_DAYS="${COOLDOWN_DAYS:-4}"
FIRST_PING_AGE_MIN="${FIRST_PING_AGE_MIN:-30}"
DRY_RUN="${DRY_RUN:-false}"
INTENDED_STATE="${INTENDED_STATE:-}"

REPO="$GITHUB_REPOSITORY"
BOT_LOGIN="github-actions[bot]"
MERGE_APPROVERS_TEAM="@dotnet/skills-merge-approvers"

STATE_LABELS=(
  "pr-state/ready-for-eval"
  "waiting-on-review"
  "ready-to-merge"
  "waiting-on-author"
  "pr-state/in-review"
  # Legacy labels — folded into the existing taxonomy. Kept here so any PR that
  # was tagged with the older names is reconciled away from them.
  "pr-state/ready-for-review"
  "pr-state/ready-for-merge"
  "pr-state/needs-author"
)

log() { printf '[pr-triage] %s\n' "$*" >&2; }
summary() {
  if [ -n "${GITHUB_STEP_SUMMARY:-}" ]; then
    printf '%s\n' "$*" >> "$GITHUB_STEP_SUMMARY"
  fi
}

# ----------------------------------------------------------------------
# Fetch PR metadata
# ----------------------------------------------------------------------
log "Fetching PR #$PR_NUMBER metadata"
PR_JSON=$(gh api "repos/$REPO/pulls/$PR_NUMBER")
HEAD_SHA=$(jq -r .head.sha <<<"$PR_JSON")
HEAD_SHA_SHORT="${HEAD_SHA:0:7}"
AUTHOR=$(jq -r .user.login <<<"$PR_JSON")
AUTHOR_TYPE=$(jq -r .user.type <<<"$PR_JSON")
AUTHOR_ASSOC=$(jq -r .author_association <<<"$PR_JSON")
IS_DRAFT=$(jq -r .draft <<<"$PR_JSON")
IS_FORK=$([[ "$(jq -r '.head.repo.full_name' <<<"$PR_JSON")" != "$(jq -r '.base.repo.full_name' <<<"$PR_JSON")" ]] && echo true || echo false)
MERGEABLE_STATE=$(jq -r .mergeable_state <<<"$PR_JSON")
UPDATED_AT=$(jq -r .updated_at <<<"$PR_JSON")
LABELS=$(jq -r '[.labels[].name] | join(",")' <<<"$PR_JSON")
TODAY=$(date -u +%Y-%m-%d)

log "head=$HEAD_SHA_SHORT author=$AUTHOR ($AUTHOR_ASSOC, type=$AUTHOR_TYPE) draft=$IS_DRAFT fork=$IS_FORK mergeable_state=$MERGEABLE_STATE labels=[$LABELS]"

# ----------------------------------------------------------------------
# Helpers — labels
# ----------------------------------------------------------------------
has_label() {
  local needle="$1"
  [[ ",$LABELS," == *",$needle,"* ]]
}

apply_label() {
  local label="$1"
  if has_label "$label"; then
    log "label '$label' already present"
    return
  fi
  if [ "$DRY_RUN" = "true" ]; then
    log "[DRY_RUN] would add label '$label'"
    return
  fi
  gh pr edit "$PR_NUMBER" --repo "$REPO" --add-label "$label" >/dev/null
  log "added label '$label'"
}

remove_label() {
  local label="$1"
  if ! has_label "$label"; then return; fi
  if [ "$DRY_RUN" = "true" ]; then
    log "[DRY_RUN] would remove label '$label'"
    return
  fi
  gh pr edit "$PR_NUMBER" --repo "$REPO" --remove-label "$label" >/dev/null
  log "removed label '$label'"
}

reconcile_state_label() {
  local desired="$1"
  for l in "${STATE_LABELS[@]}"; do
    if [ "$l" != "$desired" ]; then
      remove_label "$l"
    fi
  done
  if [ -n "$desired" ]; then
    apply_label "$desired"
  fi
}

# ----------------------------------------------------------------------
# Helpers — comment markers (cool-down)
# ----------------------------------------------------------------------
# Returns "" if no prior marker for this action variant, else seconds since most recent.
seconds_since_marker() {
  local marker_substr="$1"   # e.g. "<!-- pr-triage:fingerprint=author-ping:"
  local newest
  # NB: --paginate runs --jq per page, so per-page aggregations (sort | last) would yield
  # one value per page. Emit one .created_at per match and pick the max in the shell.
  newest=$(gh api --paginate "repos/$REPO/issues/$PR_NUMBER/comments" \
    --jq ".[] | select(.user.login == \"$BOT_LOGIN\") | select(.body | contains(\"$marker_substr\")) | .created_at" \
    | sort | tail -n 1)
  if [ -z "$newest" ] || [ "$newest" = "null" ]; then
    echo ""
    return
  fi
  local then now
  then=$(date -u -d "$newest" +%s 2>/dev/null || date -u -j -f "%Y-%m-%dT%H:%M:%SZ" "$newest" +%s)
  now=$(date -u +%s)
  echo $(( now - then ))
}

cooldown_seconds() {
  echo $(( COOLDOWN_DAYS * 86400 ))
}

post_comment() {
  local body="$1"
  if [ "$DRY_RUN" = "true" ]; then
    log "[DRY_RUN] would post comment:"
    printf '%s\n' "$body" | sed 's/^/  | /' >&2
    return
  fi
  gh pr comment "$PR_NUMBER" --repo "$REPO" --body "$body" >/dev/null
  log "posted comment"
}

# ----------------------------------------------------------------------
# Helpers — review state via GraphQL
# ----------------------------------------------------------------------
review_data() {
  gh api graphql -f query='
    query($owner:String!,$repo:String!,$num:Int!){
      repository(owner:$owner,name:$repo){
        pullRequest(number:$num){
          reviewDecision
          reviewThreads(first:100){ nodes { isResolved } }
          latestReviews(first:50){ nodes { state author { login } } }
        }
      }
    }' \
    -F owner="${REPO%/*}" -F repo="${REPO#*/}" -F num="$PR_NUMBER" \
    --jq '.data.repository.pullRequest'
}

# ----------------------------------------------------------------------
# Helpers — evaluation status
# ----------------------------------------------------------------------
eval_status_state() {
  gh api "repos/$REPO/statuses/$HEAD_SHA" \
    --jq '[.[] | select(.context == "evaluation-status")] | (sort_by(.created_at) | last) | .state // "pending"'
}

eval_run_exists_for_head() {
  # Decide whether a *real* evaluation has already run (or is running) for the
  # current head, so we never trigger a duplicate.
  #
  # Path 1 — runs whose head_sha == the PR head. These come from the /evaluate
  # comment (issue_comment) and from a human applying the evaluate-now label
  # (pull_request_target). Filtering by event alone is not reliable: every PR
  # push also produces placeholder status runs (`pr-status` on pull_request, and
  # `fork-pr-status` on pull_request_target — the latter concludes "success",
  # not "skipped"), so an event/conclusion filter would count those. See
  # https://github.com/dotnet/skills/pull/703 and
  # https://github.com/dotnet/skills/pull/720 for the original repro. The
  # reliable discriminator is whether the run actually executed the `gate` job
  # or the `discover` job: in placeholder runs both are present but
  # conclusion=="skipped"; in a real evaluation at least one is non-skipped
  # (success/failure, or null while still in progress).
  local run_ids id real
  run_ids=$(gh api --paginate "repos/$REPO/actions/workflows/evaluation.yml/runs?head_sha=$HEAD_SHA" \
    --jq '.workflow_runs[].id')
  for id in $run_ids; do
    real=$(gh api --paginate "repos/$REPO/actions/runs/$id/jobs" \
      --jq '[.jobs[] | select((.name == "gate" or .name == "discover") and .conclusion != "skipped")] | length' \
      | awk '{s+=$1} END{print s+0}')
    if [ "${real:-0}" -gt 0 ]; then
      return 0
    fi
  done
  # Path 2 — runs this worker dispatched via workflow_dispatch. Those execute
  # against the default branch, so their head_sha is main's HEAD (not the PR
  # head) and Path 1 cannot see them. Match them instead by the deterministic
  # run name evaluation.yml derives from the pr_number/head_sha dispatch inputs
  # ("Evaluate PR #<n> @ <sha7>"). A new push changes <sha7>, so a stale run for
  # an older head never masks a head that still needs evaluation. The 100
  # most-recent dispatch runs are ample given the hourly triage cadence.
  local dispatched
  dispatched=$(gh api "repos/$REPO/actions/workflows/evaluation.yml/runs?event=workflow_dispatch&per_page=100" \
    --jq ".workflow_runs[] | select(.display_title == \"Evaluate PR #$PR_NUMBER @ $HEAD_SHA_SHORT\") | .id" \
    | head -n 1)
  [ -n "$dispatched" ] && return 0
  return 1
}

# ----------------------------------------------------------------------
# Helpers — CODEOWNERS resolution
# ----------------------------------------------------------------------
codeowners_for_paths() {
  # Reads CODEOWNERS from .github/CODEOWNERS in the workspace, matches each
  # changed path against the rules (last matching rule wins, per GitHub semantics),
  # and returns the union of owner handles.
  local files_json="$1"
  python3 - "$files_json" <<'PY'
import json, os, re, sys, fnmatch

with open(".github/CODEOWNERS", "r", encoding="utf-8") as f:
    lines = f.readlines()

rules = []  # list of (pattern, owners)
for line in lines:
    line = line.strip()
    if not line or line.startswith("#"):
        continue
    parts = line.split()
    pat = parts[0]
    owners = parts[1:]
    rules.append((pat, owners))

def match(pattern, path):
    # CODEOWNERS supports gitignore-like patterns. Handle the common cases:
    # - leading '/' anchors at repo root
    # - trailing '/' matches a directory and everything under it
    # - '*' is a single-segment glob
    if pattern.startswith("/"):
        pattern = pattern[1:]
    if pattern.endswith("/"):
        # directory match: any path under it
        return path.startswith(pattern) or path == pattern.rstrip("/")
    # File or glob: match exact, or path under directory if no glob chars
    if any(c in pattern for c in "*?[]"):
        return fnmatch.fnmatchcase(path, pattern) or fnmatch.fnmatchcase(path, pattern + "/*")
    return path == pattern or path.startswith(pattern + "/")

files = json.loads(sys.argv[1])
owners = []
for path in files:
    last_owners = None
    for pat, o in rules:
        if match(pat, path):
            last_owners = o
    if last_owners:
        for h in last_owners:
            if h not in owners:
                owners.append(h)
print(" ".join(owners))
PY
}

# ----------------------------------------------------------------------
# Compute state
# ----------------------------------------------------------------------

# Bot-author short-circuit
IS_BOT="false"
if [ "$AUTHOR_TYPE" = "Bot" ] || [[ "$AUTHOR" == *"[bot]" ]]; then
  IS_BOT="true"
fi

# Trusted contributor?
IS_TRUSTED="false"
case "$AUTHOR_ASSOC" in
  OWNER|MEMBER|COLLABORATOR) IS_TRUSTED="true" ;;
esac

# Skip conditions
STATE=""
if [ "$IS_DRAFT" = "true" ]; then STATE="skip"; fi
if has_label "no-stale" && [ -z "$STATE" ]; then
  # no-stale only suppresses pings; still reconcile state labels.
  : # handled below by skipping ping actions
fi
if [ "$MERGEABLE_STATE" = "unknown" ] && [ -z "$STATE" ]; then
  STATE="skip"
  log "mergeable_state=unknown — skipping this cycle"
fi

if [ -z "$STATE" ]; then
  RV=$(review_data)
  REVIEW_DECISION=$(jq -r '.reviewDecision // ""' <<<"$RV")
  UNRESOLVED=$(jq -r '[.reviewThreads.nodes[] | select(.isResolved == false)] | length' <<<"$RV")
  EVAL_STATE=$(eval_status_state)
  log "reviewDecision=$REVIEW_DECISION unresolved_threads=$UNRESOLVED eval_status=$EVAL_STATE"

  # Malicious scan precedence (non-bot, untrusted, no marker for current head).
  # Match either the orchestrator-posted dispatched marker (source of truth) or
  # the agent-posted fingerprint marker (set by a successful scan run).
  if [ "$IS_BOT" = "false" ] && [ "$IS_TRUSTED" = "false" ]; then
    SECS=$(seconds_since_marker "<!-- pr-malicious-scan:dispatched=$HEAD_SHA_SHORT -->")
    if [ -z "$SECS" ]; then
      SECS=$(seconds_since_marker "<!-- pr-malicious-scan:fingerprint=$HEAD_SHA_SHORT:")
    fi
    if [ -z "$SECS" ]; then
      STATE="needs-malicious-scan"
    fi
  fi

  if [ -z "$STATE" ]; then
    if [ "$REVIEW_DECISION" = "CHANGES_REQUESTED" ] \
       || [ "${UNRESOLVED:-0}" -gt 0 ] \
       || [ "$MERGEABLE_STATE" = "dirty" ]; then
      STATE="needs-author-attention"
    elif [ "$EVAL_STATE" = "success" ] && [ "$REVIEW_DECISION" = "APPROVED" ]; then
      STATE="ready-for-merge"
    elif [ "$EVAL_STATE" = "success" ]; then
      # ready-for-review when no decision yet, or REVIEW_REQUIRED
      if [ "$REVIEW_DECISION" = "" ] || [ "$REVIEW_DECISION" = "REVIEW_REQUIRED" ]; then
        STATE="ready-for-review"
      else
        STATE="in-review"
      fi
    else
      # eval has not succeeded yet
      STATE="ready-for-eval"
    fi
  fi
fi

log "computed STATE=$STATE (intended was '$INTENDED_STATE')"
summary "- PR #$PR_NUMBER: state=\`$STATE\` head=\`$HEAD_SHA_SHORT\` author=$AUTHOR ($AUTHOR_ASSOC) eval=${EVAL_STATE:-n/a} reviewDecision=${REVIEW_DECISION:-n/a}"

# ----------------------------------------------------------------------
# Apply state-label reconciliation
# ----------------------------------------------------------------------
case "$STATE" in
  skip|needs-malicious-scan)
    : ;;  # do not reconcile labels for skip/scan-only states
  needs-author-attention)         reconcile_state_label "waiting-on-author" ;;
  ready-for-eval)                 reconcile_state_label "pr-state/ready-for-eval" ;;
  ready-for-review)               reconcile_state_label "waiting-on-review" ;;
  ready-for-merge)                reconcile_state_label "ready-to-merge" ;;
  in-review)                      reconcile_state_label "pr-state/in-review" ;;
esac

# ----------------------------------------------------------------------
# Suppress pings while the PR is actively being iterated on
# ----------------------------------------------------------------------
ping_age_gate_ok() {
  # Only applies to the *first* ping on a PR. If we've already posted any
  # triage ping marker, we rely on the per-variant cool-down instead.
  local prior; prior=$(seconds_since_marker "<!-- pr-triage:fingerprint=")
  if [ -n "$prior" ]; then
    return 0
  fi
  local created_at; created_at=$(jq -r .created_at <<<"$PR_JSON")
  local created_secs now_secs
  created_secs=$(date -u -d "$created_at" +%s 2>/dev/null || date -u -j -f "%Y-%m-%dT%H:%M:%SZ" "$created_at" +%s)
  now_secs=$(date -u +%s)
  local age=$(( now_secs - created_secs ))
  local min=$(( FIRST_PING_AGE_MIN * 60 ))
  if [ "$age" -lt "$min" ]; then
    log "first-ping age gate: PR created_at age ${age}s < ${min}s — suppressing ping"
    return 1
  fi
  return 0
}

# ----------------------------------------------------------------------
# Action: eval-trigger
# ----------------------------------------------------------------------
do_eval_trigger() {
  if eval_run_exists_for_head; then
    log "eval-trigger: an evaluation run already exists for $HEAD_SHA_SHORT — skipping"
    return
  fi
  if [ "$DRY_RUN" = "true" ]; then
    log "[DRY_RUN] would dispatch evaluation.yml for PR #$PR_NUMBER @ $HEAD_SHA_SHORT"
    summary "  - action: eval-trigger (DRY_RUN)"
    return
  fi
  # Trigger evaluation by dispatching evaluation.yml directly. We deliberately do
  # NOT add the `evaluate-now` label here: label events emitted by this workflow's
  # GITHUB_TOKEN do not start new workflow runs (GitHub's recursion guard), so the
  # pull_request_target:[labeled] entry point never fires for the bot.
  # workflow_dispatch is exempt from that guard, so it does fire. The label
  # remains a valid *human* entry point and is still honoured by evaluation.yml.
  # The head_sha (short) feeds evaluation.yml's run name, which is the idempotency
  # key eval_run_exists_for_head matches on (Path 2).
  if gh workflow run evaluation.yml --repo "$REPO" \
       -f pr_number="$PR_NUMBER" \
       -f head_sha="$HEAD_SHA_SHORT" >/dev/null; then
    log "eval-trigger: dispatched evaluation.yml for PR #$PR_NUMBER @ $HEAD_SHA_SHORT"
    summary "  - action: eval-trigger (dispatched evaluation.yml)"
  else
    echo "::warning::eval-trigger: failed to dispatch evaluation.yml for PR #$PR_NUMBER" >&2
    summary "  - action: eval-trigger (dispatch FAILED)"
  fi
}

# ----------------------------------------------------------------------
# Action: author-ping
# ----------------------------------------------------------------------
do_author_ping() {
  if has_label "no-stale"; then log "author-ping: no-stale label set — skipping"; return; fi
  if ! ping_age_gate_ok; then summary "  - action: author-ping (suppressed by age gate)"; return; fi
  local marker_prefix="<!-- pr-triage:fingerprint=author-ping:"
  local secs; secs=$(seconds_since_marker "$marker_prefix")
  local cd; cd=$(cooldown_seconds)
  if [ -n "$secs" ] && [ "$secs" -lt "$cd" ]; then
    log "author-ping: within cool-down (${secs}s < ${cd}s) — skipping"
    summary "  - action: author-ping (cool-down)"
    return
  fi

  local reasons=()
  [ "$REVIEW_DECISION" = "CHANGES_REQUESTED" ] && reasons+=("changes requested")
  [ "${UNRESOLVED:-0}" -gt 0 ] && reasons+=("$UNRESOLVED unresolved review thread(s)")
  [ "$MERGEABLE_STATE" = "dirty" ] && reasons+=("merge conflict")
  local reason_text
  reason_text=$(IFS=", "; echo "${reasons[*]}")
  [ -z "$reason_text" ] && reason_text="open review feedback"

  local body
  body=$(cat <<EOF
<!-- pr-triage:fingerprint=author-ping:$HEAD_SHA_SHORT:$TODAY -->
👋 @$AUTHOR — this PR has $reason_text. When you're ready, please address the feedback and push an update; the triage bot will pick up the next state automatically. (Add the \`no-stale\` label to silence further pings.)
EOF
  )
  post_comment "$body"
  summary "  - action: author-ping"
}

# ----------------------------------------------------------------------
# Action: maintainer-ping
# ----------------------------------------------------------------------
do_maintainer_ping() {
  if has_label "no-stale"; then log "maintainer-ping: no-stale label set — skipping"; return; fi
  if ! ping_age_gate_ok; then summary "  - action: maintainer-ping (suppressed by age gate)"; return; fi

  local variant body
  if [ "$STATE" = "ready-for-merge" ]; then
    variant="C"
    local approvers
    approvers=$(jq -r '[.latestReviews.nodes[] | select(.state == "APPROVED") | .author.login] | unique | map("@" + .) | join(" ")' <<<"$RV")
    [ -z "$approvers" ] && approvers="@$AUTHOR"
    local marker_prefix="<!-- pr-triage:fingerprint=maintainer-ping/C:"
    local secs; secs=$(seconds_since_marker "$marker_prefix")
    local cd; cd=$(cooldown_seconds)
    if [ -n "$secs" ] && [ "$secs" -lt "$cd" ]; then
      log "maintainer-ping/C: within cool-down — skipping"
      summary "  - action: maintainer-ping/C (cool-down)"
      return
    fi
    body=$(cat <<EOF
<!-- pr-triage:fingerprint=maintainer-ping/C:$HEAD_SHA_SHORT:$TODAY -->
✅ Approved by $approvers. cc $MERGE_APPROVERS_TEAM — ready to merge.
EOF
)
  else
    # ready-for-review: resolve CODEOWNERS for changed paths
    local files_json owners_str
    # NB: --paginate runs --jq per page, so '[.[] | .filename]' would emit one JSON array
    # per page. Emit one filename per line, then slurp into a single JSON array.
    files_json=$(gh api --paginate "repos/$REPO/pulls/$PR_NUMBER/files" --jq '.[] | .filename' \
      | jq -R . | jq -s .)
    owners_str=""
    if [ -f ".github/CODEOWNERS" ]; then
      owners_str=$(codeowners_for_paths "$files_json" || true)
    fi
    if [ -z "$owners_str" ]; then
      variant="B"
      local marker_prefix="<!-- pr-triage:fingerprint=maintainer-ping/B:"
      local secs; secs=$(seconds_since_marker "$marker_prefix")
      local cd; cd=$(cooldown_seconds)
      if [ -n "$secs" ] && [ "$secs" -lt "$cd" ]; then
        log "maintainer-ping/B: within cool-down — skipping"
        summary "  - action: maintainer-ping/B (cool-down)"
        return
      fi
      body=$(cat <<EOF
<!-- pr-triage:fingerprint=maintainer-ping/B:$HEAD_SHA_SHORT:$TODAY -->
✅ Evaluation passed for \`$HEAD_SHA_SHORT\`. No CODEOWNERS entry matched the changed paths; cc $MERGE_APPROVERS_TEAM — please review.
EOF
)
    else
      variant="A"
      local marker_prefix="<!-- pr-triage:fingerprint=maintainer-ping/A:"
      local secs; secs=$(seconds_since_marker "$marker_prefix")
      local cd; cd=$(cooldown_seconds)
      if [ -n "$secs" ] && [ "$secs" -lt "$cd" ]; then
        log "maintainer-ping/A: within cool-down — skipping"
        summary "  - action: maintainer-ping/A (cool-down)"
        return
      fi
      body=$(cat <<EOF
<!-- pr-triage:fingerprint=maintainer-ping/A:$HEAD_SHA_SHORT:$TODAY -->
✅ Evaluation passed for \`$HEAD_SHA_SHORT\`. cc $owners_str — please review.
EOF
)
    fi
  fi
  post_comment "$body"
  summary "  - action: maintainer-ping/$variant"
}

# ----------------------------------------------------------------------
# Dispatch by state
# ----------------------------------------------------------------------
case "$STATE" in
  skip)
    log "no action for state=skip"
    ;;
  needs-malicious-scan)
    log "needs-malicious-scan — orchestrator dispatches the scanner; worker takes no action here"
    ;;
  needs-author-attention)
    do_author_ping
    ;;
  ready-for-eval)
    do_eval_trigger
    ;;
  ready-for-review|ready-for-merge)
    do_maintainer_ping
    ;;
  in-review)
    log "no action for state=in-review"
    ;;
  *)
    log "unhandled state '$STATE' — no action"
    ;;
esac

log "done"
