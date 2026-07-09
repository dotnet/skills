#!/usr/bin/env bash
#
# invoke-fast-eval.sh — Fast, orchestrated skill-validator sweeps.
#
# Wraps the existing `evaluate`, `evaluate rejudge`, and `evaluate consolidate`
# subcommands into two speed profiles (no core-engine changes):
#
#   --mode split (default)   Lever #4. Run agent arms with `--no-judge
#                            --keep-sessions` (one results dir per skill = one shard),
#                            then `rejudge` each shard, then `consolidate`. Takes the
#                            serial judge tail off the critical path.
#
#   --mode baseline-reuse    Lever #2. Compute the skill-independent baseline arm ONCE
#                            with `--baseline-out`, then evaluate the rest with
#                            `--baseline-from` (removes up to ~1/3 of agent runs).
#                            Judges inline (baseline reuse and --no-judge are mutually
#                            exclusive in the validator).
#
#   --fast                   Lever #5. Cheaper first pass: faster judge model, shorter
#                            judge timeout, and (baseline-reuse only) --no-overfitting-check.
#                            Escalate borderline skills to a full run afterward — see
#                            eng/skill-validator/src/docs/FastEvaluation.md.
#
# Usage:
#   invoke-fast-eval.sh --tests-dir <dir> --output <md> [options] <skill-path>...
#
# Options:
#   --mode split|baseline-reuse   Orchestration mode (default: split)
#   --fast                        Apply the fast first-pass profile
#   --results-root <dir>          Results root (default: .skill-validator-results)
#   --output <path>               Consolidated markdown (default: fast-eval-summary.md)
#   --model <id>                  Agent model (default: claude-opus-4.6)
#   --judge-model <id>            Full-fidelity judge model (default: --model)
#   --fast-judge-model <id>       Judge model when --fast (default: claude-opus-4.6-fast)
#   --runs <n>                    Runs per scenario (default: 5)
#   --judge-timeout <s>           Judge timeout, full profile (default: 300)
#   --fast-judge-timeout <s>      Judge timeout when --fast (default: 120)
#   --max-parallel <n>            Max concurrent shards/skills (default: 3)
#   --validator "<cmd>"           How to invoke the validator (default: auto-detect the
#                                 built dll via `dotnet <dll>`, else `skill-validator`)
set -euo pipefail

MODE="split"
FAST=0
RESULTS_ROOT=".skill-validator-results"
OUTPUT="fast-eval-summary.md"
MODEL="claude-opus-4.6"
JUDGE_MODEL=""
FAST_JUDGE_MODEL="claude-haiku-4.5"
RUNS=5
JUDGE_TIMEOUT=300
FAST_JUDGE_TIMEOUT=120
MAX_PARALLEL=3
VALIDATOR=""
SKILLS=()

while [[ $# -gt 0 ]]; do
  case "$1" in
    --mode) MODE="$2"; shift 2;;
    --fast) FAST=1; shift;;
    --results-root) RESULTS_ROOT="$2"; shift 2;;
    --output) OUTPUT="$2"; shift 2;;
    --model) MODEL="$2"; shift 2;;
    --judge-model) JUDGE_MODEL="$2"; shift 2;;
    --fast-judge-model) FAST_JUDGE_MODEL="$2"; shift 2;;
    --runs) RUNS="$2"; shift 2;;
    --judge-timeout) JUDGE_TIMEOUT="$2"; shift 2;;
    --fast-judge-timeout) FAST_JUDGE_TIMEOUT="$2"; shift 2;;
    --max-parallel) MAX_PARALLEL="$2"; shift 2;;
    --validator) VALIDATOR="$2"; shift 2;;
    --tests-dir) TESTS_DIR="$2"; shift 2;;
    -h|--help) sed -n '2,45p' "$0"; exit 0;;
    -*) echo "Unknown option: $1" >&2; exit 2;;
    *) SKILLS+=("$1"); shift;;
  esac
done

: "${TESTS_DIR:?--tests-dir is required}"
[[ ${#SKILLS[@]} -gt 0 ]] || { echo "At least one skill path is required" >&2; exit 2; }

# Resolve validator invocation.
if [[ -z "$VALIDATOR" ]]; then
  SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
  DLL="$SCRIPT_DIR/../../../artifacts/bin/SkillValidator/release/skill-validator.dll"
  if [[ -f "$DLL" ]]; then
    VALIDATOR="dotnet $DLL"
  elif command -v skill-validator >/dev/null 2>&1; then
    VALIDATOR="skill-validator"
  else
    echo "Could not find skill-validator. Build it (see docs) or pass --validator." >&2
    exit 1
  fi
fi

[[ -n "$JUDGE_MODEL" ]] || JUDGE_MODEL="$MODEL"
if [[ $FAST -eq 1 ]]; then
  EFF_JUDGE_MODEL="$FAST_JUDGE_MODEL"; EFF_JUDGE_TIMEOUT="$FAST_JUDGE_TIMEOUT"
else
  EFF_JUDGE_MODEL="$JUDGE_MODEL"; EFF_JUDGE_TIMEOUT="$JUDGE_TIMEOUT"
fi

shard_name() { basename "${1%/}"; }

newest_ts_dir() {
  local root="$1"
  [[ -d "$root" ]] || return 0
  find "$root" -mindepth 1 -maxdepth 1 -type d -exec test -f '{}/sessions.db' ';' -print 2>/dev/null \
    | while read -r d; do printf '%s\t%s\n' "$(stat -c %Y "$d" 2>/dev/null || echo 0)" "$d"; done \
    | sort -rn | head -n1 | cut -f2-
}

echo "skill-validator fast eval — mode=$MODE fast=$FAST runs=$RUNS judge=$EFF_JUDGE_MODEL"
RESULTS_JSON=()

if [[ "$MODE" == "split" ]]; then
  echo ""
  echo "[Phase 1/3] Executing ${#SKILLS[@]} shard(s) with --no-judge (max $MAX_PARALLEL parallel)..."
  running=0
  for skill in "${SKILLS[@]}"; do
    name="$(shard_name "$skill")"
    root="$RESULTS_ROOT/$name"
    echo "   -> execute shard '$name'"
    # shellcheck disable=SC2086
    $VALIDATOR evaluate "$skill" --tests-dir "$TESTS_DIR" --no-judge --keep-sessions \
      --results-dir "$root" --model "$MODEL" --judge-model "$EFF_JUDGE_MODEL" --runs "$RUNS" &
    running=$((running+1))
    if [[ $running -ge $MAX_PARALLEL ]]; then wait -n 2>/dev/null || wait; running=$((running-1)); fi
  done
  wait

  echo ""
  echo "[Phase 2/3] Rejudging shards (judge=$EFF_JUDGE_MODEL, timeout=${EFF_JUDGE_TIMEOUT}s)..."
  for skill in "${SKILLS[@]}"; do
    name="$(shard_name "$skill")"
    ts_dir="$(newest_ts_dir "$RESULTS_ROOT/$name")"
    if [[ -z "$ts_dir" ]]; then echo "WARN: no sessions.db for '$name'; skipping" >&2; continue; fi
    # shellcheck disable=SC2086
    $VALIDATOR evaluate rejudge "$ts_dir" --judge-model "$EFF_JUDGE_MODEL" --judge-timeout "$EFF_JUDGE_TIMEOUT" || true
    [[ -f "$ts_dir/results.json" ]] && RESULTS_JSON+=("$ts_dir/results.json")
  done
else
  BASELINE_FILE="$RESULTS_ROOT/shared-baseline.json"
  OF_ARG=(); [[ $FAST -eq 1 ]] && OF_ARG=(--no-overfitting-check)

  echo ""
  echo "[1/2] Evaluating first skill with --baseline-out (inline judged)..."
  first="${SKILLS[0]}"; fname="$(shard_name "$first")"; froot="$RESULTS_ROOT/$fname"
  # shellcheck disable=SC2086
  $VALIDATOR evaluate "$first" --tests-dir "$TESTS_DIR" --baseline-out "$BASELINE_FILE" \
    --results-dir "$froot" --keep-sessions --model "$MODEL" --judge-model "$EFF_JUDGE_MODEL" \
    --judge-timeout "$EFF_JUDGE_TIMEOUT" --runs "$RUNS" "${OF_ARG[@]}"
  ts_dir="$(newest_ts_dir "$froot")"
  [[ -n "$ts_dir" && -f "$ts_dir/results.json" ]] && RESULTS_JSON+=("$ts_dir/results.json")
  [[ -f "$BASELINE_FILE" ]] || { echo "Baseline not produced at $BASELINE_FILE; aborting" >&2; exit 1; }

  echo ""
  echo "[2/2] Evaluating remaining skill(s) with --baseline-from (max $MAX_PARALLEL parallel)..."
  running=0
  for skill in "${SKILLS[@]:1}"; do
    name="$(shard_name "$skill")"; root="$RESULTS_ROOT/$name"
    echo "   -> reuse baseline for '$name'"
    # shellcheck disable=SC2086
    $VALIDATOR evaluate "$skill" --tests-dir "$TESTS_DIR" --baseline-from "$BASELINE_FILE" \
      --results-dir "$root" --keep-sessions --model "$MODEL" --judge-model "$EFF_JUDGE_MODEL" \
      --judge-timeout "$EFF_JUDGE_TIMEOUT" --runs "$RUNS" "${OF_ARG[@]}" &
    running=$((running+1))
    if [[ $running -ge $MAX_PARALLEL ]]; then wait -n 2>/dev/null || wait; running=$((running-1)); fi
  done
  wait
  for skill in "${SKILLS[@]:1}"; do
    name="$(shard_name "$skill")"
    ts_dir="$(newest_ts_dir "$RESULTS_ROOT/$name")"
    [[ -n "$ts_dir" && -f "$ts_dir/results.json" ]] && RESULTS_JSON+=("$ts_dir/results.json")
  done
fi

echo ""
echo "[Consolidate] Merging ${#RESULTS_JSON[@]} results.json file(s) -> $OUTPUT"
if [[ ${#RESULTS_JSON[@]} -eq 0 ]]; then
  echo "No results.json files were produced; nothing to consolidate." >&2
  exit 1
fi
# shellcheck disable=SC2086
$VALIDATOR evaluate consolidate "${RESULTS_JSON[@]}" --output "$OUTPUT"

echo ""
echo "Done. Summary: $OUTPUT"
