#!/usr/bin/env bash
#
# run-vally-evals.sh — Run vally evaluations locally, mirroring the CI workflow.
#
# Usage:
#   ./eng/vally-adapter/run-vally-evals.sh                        # all evals
#   ./eng/vally-adapter/run-vally-evals.sh dotnet-maui             # one suite/plugin
#   ./eng/vally-adapter/run-vally-evals.sh dotnet-maui maui-theming  # one skill
#
# Environment:
#   PARALLEL=8        Max concurrent evals (default: 8)
#   RUNS=1            Trials per stimulus (default: 1)
#   WORKERS=3         Concurrent stimuli within an eval (default: 3)
#   MODEL             Agent model (default: claude-sonnet-4.6)
#   JUDGE_MODEL       Judge model (default: claude-sonnet-4.6)
#   SKIP_EVALS=""     Override skip list (default: reads skip-evals.txt)
#
# Prerequisites:
#   - GITHUB_TOKEN set for Copilot SDK
#   - @microsoft/vally-cli available (installed globally or via npx)
#
# Results go to ./vally-results/<plugin>/<skill>/

set -euo pipefail

SKILLS_ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
VALLY="${VALLY:-npx @microsoft/vally-cli}"
RESULTS_ROOT="${RESULTS_DIR:-$SKILLS_ROOT/vally-results}"
MODEL="${MODEL:-claude-sonnet-4.6}"
JUDGE_MODEL="${JUDGE_MODEL:-claude-sonnet-4.6}"
RUNS="${RUNS:-1}"
WORKERS="${WORKERS:-3}"
PARALLEL="${PARALLEL:-8}"

PLUGIN="${1:-}"
SKILL="${2:-}"

GREEN='\033[0;32m'
RED='\033[0;31m'
YELLOW='\033[0;33m'
CYAN='\033[0;36m'
BOLD='\033[1m'
NC='\033[0m'

# ---- Skip list --------------------------------------------------------------

SKIP_FILE="$SKILLS_ROOT/eng/vally-adapter/skip-evals.txt"
if [ -z "${SKIP_EVALS+x}" ] && [ -f "$SKIP_FILE" ]; then
  SKIP_EVALS=$(grep -v '^#' "$SKIP_FILE" | grep -v '^$' | tr '\n' ' ')
fi
SKIP_EVALS="${SKIP_EVALS:-}"

# ---- Discover evals ---------------------------------------------------------
# .vally.yaml configures eval discovery (evalFilenames, paths).
# We still use find here because we need the file list for parallel orchestration
# of the baseline/skilled dual-run — vally's --suite runs end-to-end.

if [ -n "$SKILL" ] && [ -n "$PLUGIN" ]; then
  ALL_SPECS=("$SKILLS_ROOT/tests/$PLUGIN/$SKILL/eval.vally.yaml")
elif [ -n "$PLUGIN" ]; then
  ALL_SPECS=()
  while IFS= read -r f; do ALL_SPECS+=("$f"); done \
    < <(find "$SKILLS_ROOT/tests/$PLUGIN" -name "eval.vally.yaml" -type f | sort)
else
  ALL_SPECS=()
  while IFS= read -r f; do ALL_SPECS+=("$f"); done \
    < <(find "$SKILLS_ROOT/tests" -name "eval.vally.yaml" -type f | sort)
fi

EVAL_SPECS=()
for spec in "${ALL_SPECS[@]}"; do
  EVAL_NAME=$(basename "$(dirname "$spec")")
  SKIPPED=false
  for skip in $SKIP_EVALS; do
    if [ "$EVAL_NAME" = "$skip" ]; then SKIPPED=true; break; fi
  done
  if [ "$SKIPPED" = "true" ]; then
    echo -e "${YELLOW}⚠ Skipping $EVAL_NAME (in skip-evals.txt)${NC}"
  else
    EVAL_SPECS+=("$spec")
  fi
done

if [ ${#EVAL_SPECS[@]} -eq 0 ]; then
  echo "No eval.vally.yaml files to run"
  exit 1
fi

echo -e "${BOLD}Running ${#EVAL_SPECS[@]} eval(s) with PARALLEL=$PARALLEL RUNS=$RUNS${NC}"
echo ""

# ---- Per-eval function (runs in background) --------------------------------

STATUS_DIR=$(mktemp -d)

run_one_eval() {
  local EVAL_SPEC="$1"
  local EVAL_DIR="$(dirname "$EVAL_SPEC")"
  local EVAL_NAME="$(basename "$EVAL_DIR")"
  local EVAL_PLUGIN="$(basename "$(dirname "$EVAL_DIR")")"
  local SKILL_DIR="$SKILLS_ROOT/plugins/$EVAL_PLUGIN/skills/$EVAL_NAME"
  local BASELINE_DIR="$RESULTS_ROOT/$EVAL_PLUGIN/$EVAL_NAME/baseline"
  local SKILLED_DIR="$RESULTS_ROOT/$EVAL_PLUGIN/$EVAL_NAME/skilled"
  local LOG="$RESULTS_ROOT/$EVAL_PLUGIN/$EVAL_NAME/eval.log"

  mkdir -p "$RESULTS_ROOT/$EVAL_PLUGIN/$EVAL_NAME"
  rm -rf "$BASELINE_DIR" "$SKILLED_DIR"
  mkdir -p "$BASELINE_DIR" "$SKILLED_DIR"

  if [ ! -d "$SKILL_DIR" ]; then
    echo "SKIP: skill dir not found: $SKILL_DIR" > "$LOG"
    echo -e "  ${YELLOW}⚠${NC} $EVAL_PLUGIN/$EVAL_NAME (skipped — no skill dir)"
    echo "skip" > "$STATUS_DIR/$EVAL_PLUGIN--$EVAL_NAME"
    return
  fi

  echo -e "  ${BOLD}▶${NC} $EVAL_PLUGIN/$EVAL_NAME — baseline..." >&2

  {
    echo "=== $EVAL_PLUGIN/$EVAL_NAME ==="

    # Baseline
    echo "--- Baseline run ---"
    $VALLY eval \
      --eval-spec "$EVAL_SPEC" \
      --model "$MODEL" \
      --runs "$RUNS" --workers "$WORKERS" \
      --skip-validate \
      --judge-model "$JUDGE_MODEL" \
      --output-dir "$BASELINE_DIR" \
      2>&1 || echo "WARNING: Baseline eval failed"

    echo -e "  ${BOLD}▶${NC} $EVAL_PLUGIN/$EVAL_NAME — skilled..." >&2

    # Skilled
    echo "--- Skilled run ---"
    $VALLY eval \
      --eval-spec "$EVAL_SPEC" \
      --skill-dir "$SKILL_DIR" \
      --model "$MODEL" \
      --runs "$RUNS" --workers "$WORKERS" \
      --skip-validate \
      --judge-model "$JUDGE_MODEL" \
      --output-dir "$SKILLED_DIR" \
      2>&1 || echo "WARNING: Skilled eval failed"

    # Adapt
    local BASELINE_JSONL=$(find "$BASELINE_DIR" -name "*.jsonl" -type f 2>/dev/null | head -1)
    local SKILLED_JSONL=$(find "$SKILLED_DIR" -name "*.jsonl" -type f 2>/dev/null | head -1)

    if [ -n "$BASELINE_JSONL" ] && [ -n "$SKILLED_JSONL" ]; then
      echo "--- Adapting results ---"
      node "$SKILLS_ROOT/eng/vally-adapter/adapt.mjs" \
        --baseline "$(dirname "$BASELINE_JSONL")" \
        --skilled "$(dirname "$SKILLED_JSONL")" \
        --skill-name "$EVAL_NAME" \
        --skill-path "plugins/$EVAL_PLUGIN/skills/$EVAL_NAME" \
        --model "$MODEL" \
        --judge-model "$JUDGE_MODEL" \
        --output "$RESULTS_ROOT/$EVAL_PLUGIN/$EVAL_NAME/results.json" \
        2>&1
    fi
  } > "$LOG" 2>&1

  # Determine status outside the log-capture block
  local RESULTS_FILE="$RESULTS_ROOT/$EVAL_PLUGIN/$EVAL_NAME/results.json"
  if [ -f "$RESULTS_FILE" ]; then
    local PASSED=$(node -e "const r=JSON.parse(require('fs').readFileSync('$RESULTS_FILE','utf-8')); console.log(r.verdicts[0].passed)" 2>/dev/null || echo "")
    if [ "$PASSED" = "true" ]; then
      echo "pass" > "$STATUS_DIR/$EVAL_PLUGIN--$EVAL_NAME"
      echo -e "  ${GREEN}✔${NC} $EVAL_PLUGIN/$EVAL_NAME"
    else
      echo "no_improvement" > "$STATUS_DIR/$EVAL_PLUGIN--$EVAL_NAME"
      echo -e "  ${CYAN}⊘${NC} $EVAL_PLUGIN/$EVAL_NAME (no improvement)"
    fi
  else
    echo "error" > "$STATUS_DIR/$EVAL_PLUGIN--$EVAL_NAME"
    echo -e "  ${RED}✘${NC} $EVAL_PLUGIN/$EVAL_NAME (see $LOG)"
  fi
}

export -f run_one_eval
export SKILLS_ROOT VALLY RESULTS_ROOT MODEL JUDGE_MODEL RUNS WORKERS STATUS_DIR
export GREEN RED YELLOW CYAN BOLD NC

# ---- Run in parallel --------------------------------------------------------

PIDS=()
RUNNING=0

for EVAL_SPEC in "${EVAL_SPECS[@]}"; do
  run_one_eval "$EVAL_SPEC" &
  PIDS+=($!)
  RUNNING=$((RUNNING + 1))

  if [ "$RUNNING" -ge "$PARALLEL" ]; then
    wait -n 2>/dev/null || true
    RUNNING=$((RUNNING - 1))
  fi
done

wait

# ---- Summary ---------------------------------------------------------------

echo ""
PASS=0; NOIMPROVE=0; FAIL=0; SKIP=0
for f in "$STATUS_DIR"/*; do
  [ ! -f "$f" ] && continue
  case "$(cat "$f")" in
    pass)           PASS=$((PASS + 1)) ;;
    no_improvement) NOIMPROVE=$((NOIMPROVE + 1)) ;;
    skip)           SKIP=$((SKIP + 1)) ;;
    *)              FAIL=$((FAIL + 1)) ;;
  esac
done
rm -rf "$STATUS_DIR"

TOTAL=$((PASS + NOIMPROVE))
echo -e "${BOLD}━━━ Summary ━━━${NC}"
echo -e "  ${GREEN}✔ $PASS passed${NC}"
[ $NOIMPROVE -gt 0 ] && echo -e "  ${CYAN}⊘ $NOIMPROVE no improvement${NC}"
echo -e "  Completed: $TOTAL/$((TOTAL + FAIL + SKIP))"
[ $FAIL -gt 0 ] && echo -e "  ${RED}✘ $FAIL errors${NC}"
[ $SKIP -gt 0 ] && echo -e "  ${YELLOW}⚠ $SKIP skipped${NC}"
echo -e "  Results: $RESULTS_ROOT"

[ $FAIL -gt 0 ] && exit 1 || exit 0
