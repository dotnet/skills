#!/usr/bin/env bash
#
# run-vally-evals.sh — Run vally skill-vs-baseline evaluations locally, mirroring
# the CI workflow.
#
# Drives a single `vally experiment run` over the dotnet-skills experiment
# (baseline = no skills, skilled = the one skill under test), then splits the
# per-variant output into the per-skill results.json the skill-validator
# pipeline consumes.
#
# Usage:
#   ./eng/vally-adapter/run-vally-evals.sh                          # all skills
#   ./eng/vally-adapter/run-vally-evals.sh dotnet-maui              # one plugin
#   ./eng/vally-adapter/run-vally-evals.sh dotnet-maui maui-theming # one skill
#
# Environment:
#   WORKERS=8         Max concurrent trials across the whole experiment (default: 8)
#   RUNS=1            Trials per stimulus (default: 1)
#   MODEL             Agent model (default: claude-sonnet-4.6)
#   JUDGE_MODEL       Judge model (default: claude-sonnet-4.6)
#   SKIP_EVALS=""     Override skip list (default: reads skip-evals.txt)
#   EXPERIMENT_FILE   Base experiment file (default: dotnet-skills.experiment.yaml)
#   VALLY             vally CLI invocation (default: npx @microsoft/vally-cli)
#   RESULTS_DIR       Output root (default: ./vally-results)
#
# Prerequisites:
#   - GITHUB_TOKEN set for Copilot SDK
#   - @microsoft/vally-cli available (installed globally or via npx)
#
# Per-skill verdicts go to ./vally-results/<plugin>/<skill>/results.json;
# the raw experiment output (per-variant JSONL + report.md) goes to
# ./vally-results/_experiment/<timestamp>/.

set -euo pipefail

SKILLS_ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
ADAPTER_DIR="$SKILLS_ROOT/eng/vally-adapter"
VALLY="${VALLY:-npx @microsoft/vally-cli}"
EXPERIMENT_FILE="${EXPERIMENT_FILE:-$SKILLS_ROOT/dotnet-skills.experiment.yaml}"
RESULTS_ROOT="${RESULTS_DIR:-$SKILLS_ROOT/vally-results}"
MODEL="${MODEL:-claude-sonnet-4.6}"
JUDGE_MODEL="${JUDGE_MODEL:-claude-sonnet-4.6}"
RUNS="${RUNS:-1}"
WORKERS="${WORKERS:-8}"

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
  # awk (not chained greps) so a comment-only / empty file doesn't return a
  # non-zero status and abort the script under `set -o pipefail`.
  SKIP_EVALS=$(awk 'NF && $1 !~ /^#/' "$SKIP_FILE" | tr '\n' ' ')
fi
SKIP_EVALS="${SKIP_EVALS:-}"

# ---- Discover evals ---------------------------------------------------------
# Build the explicit eval-file list (relative to SKILLS_ROOT) that the scoped
# experiment will run. We enumerate rather than glob so the skip list and the
# skill-dir / agent.* exclusions are applied here, and only matching evals cost
# tokens.

cd "$SKILLS_ROOT"

if [ -n "$SKILL" ] && [ -n "$PLUGIN" ]; then
  CANDIDATES=("tests/$PLUGIN/$SKILL/eval.vally.yaml")
elif [ -n "$PLUGIN" ]; then
  CANDIDATES=()
  while IFS= read -r f; do CANDIDATES+=("$f"); done \
    < <(find "tests/$PLUGIN" -name "eval.vally.yaml" -type f | sort)
else
  CANDIDATES=()
  while IFS= read -r f; do CANDIDATES+=("$f"); done \
    < <(find tests -name "eval.vally.yaml" -type f | sort)
fi

EVAL_FILES=()
for spec in "${CANDIDATES[@]}"; do
  [ -f "$spec" ] || { echo -e "${YELLOW}⚠ No eval spec at $spec${NC}"; continue; }
  EVAL_NAME=$(basename "$(dirname "$spec")")
  EVAL_PLUGIN=$(basename "$(dirname "$(dirname "$spec")")")

  # agent.* evals exercise multi-skill orchestrator agents and do not map to a
  # single plugins/<plugin>/skills/<skill> directory — out of scope here.
  case "$EVAL_NAME" in
    agent.*)
      echo -e "${YELLOW}⚠ Skipping $EVAL_PLUGIN/$EVAL_NAME (agent eval)${NC}"
      continue
      ;;
  esac

  SKIPPED=false
  for skip in $SKIP_EVALS; do
    if [ "$EVAL_NAME" = "$skip" ]; then SKIPPED=true; break; fi
  done
  if [ "$SKIPPED" = "true" ]; then
    echo -e "${YELLOW}⚠ Skipping $EVAL_NAME (in skip-evals.txt)${NC}"
    continue
  fi

  # Defensive: the skilled variant loads plugins/<plugin>/skills/<skill>, and
  # vally fails fast if that directory is missing.
  if [ ! -d "plugins/$EVAL_PLUGIN/skills/$EVAL_NAME" ]; then
    echo -e "${YELLOW}⚠ Skipping $EVAL_PLUGIN/$EVAL_NAME (no skill dir)${NC}"
    continue
  fi

  EVAL_FILES+=("$spec")
done

if [ ${#EVAL_FILES[@]} -eq 0 ]; then
  echo "No eval.vally.yaml files to run"
  exit 1
fi

echo -e "${BOLD}Running ${#EVAL_FILES[@]} skill eval(s) — model=$MODEL runs=$RUNS workers=$WORKERS${NC}"
echo ""

# ---- Build the scoped experiment --------------------------------------------
# The scoped file must live next to the base experiment (SKILLS_ROOT) because
# vally resolves eval and skill paths relative to the experiment file's dir.

SCOPED_EXPERIMENT="$SKILLS_ROOT/.vally-experiment-scoped.$$.yaml"
trap 'rm -f "$SCOPED_EXPERIMENT"' EXIT

node "$ADAPTER_DIR/scope-experiment.mjs" \
  --base "$EXPERIMENT_FILE" \
  --out "$SCOPED_EXPERIMENT" \
  --model "$MODEL" \
  --judge-model "$JUDGE_MODEL" \
  --runs "$RUNS" \
  "${EVAL_FILES[@]}"

# ---- Run the experiment -----------------------------------------------------

EXPERIMENT_OUT="$RESULTS_ROOT/_experiment"
mkdir -p "$EXPERIMENT_OUT"

# Clear any prior verdict for exactly the evals we're about to run so the
# completeness check below reflects only this invocation's fresh output and
# can't be satisfied by a stale results.json from an earlier run.
EXPECTED_RESULTS=()
for spec in "${EVAL_FILES[@]}"; do
  en=$(basename "$(dirname "$spec")")
  ep=$(basename "$(dirname "$(dirname "$spec")")")
  rp="$RESULTS_ROOT/$ep/$en/results.json"
  EXPECTED_RESULTS+=("$rp")
  rm -f "$rp"
done

# Snapshot existing run dirs so we adapt the directory THIS run creates, never
# a stale one left behind when a run fails before writing any output.
RUN_DIRS_BEFORE=$(find "$EXPERIMENT_OUT" -mindepth 1 -maxdepth 1 -type d | sort)

EXPERIMENT_RC=0
$VALLY experiment run "$SCOPED_EXPERIMENT" \
  --output-dir "$EXPERIMENT_OUT" \
  --workers "$WORKERS" 2>&1 || EXPERIMENT_RC=$?
if [ "$EXPERIMENT_RC" -ne 0 ]; then
  echo -e "${YELLOW}⚠ vally experiment run exited $EXPERIMENT_RC (some trials may have failed); adapting available output${NC}"
fi

RUN_DIRS_AFTER=$(find "$EXPERIMENT_OUT" -mindepth 1 -maxdepth 1 -type d | sort)
RUN_DIR=$(comm -13 <(printf '%s\n' "$RUN_DIRS_BEFORE") <(printf '%s\n' "$RUN_DIRS_AFTER") | awk 'NF' | tail -1)
if [ -z "$RUN_DIR" ]; then
  echo -e "${RED}✘ No new experiment output directory produced${NC}"
  exit 1
fi

# ---- Adapt: split the experiment output into per-skill results.json ---------

node "$ADAPTER_DIR/adapt.mjs" \
  --experiment-dir "$RUN_DIR" \
  --output-root "$RESULTS_ROOT" \
  --model "$MODEL" \
  --judge-model "$JUDGE_MODEL"

# ---- Summary ----------------------------------------------------------------

echo ""
PASS=0; NOIMPROVE=0; FAIL=0; MISSING=0
for RESULTS_JSON in "${EXPECTED_RESULTS[@]}"; do
  if [ ! -f "$RESULTS_JSON" ]; then
    MISSING=$((MISSING + 1))
    continue
  fi
  PASSED=$(node -e "const r=JSON.parse(require('fs').readFileSync(process.argv[1],'utf-8')); console.log(r.verdicts[0].passed)" "$RESULTS_JSON" 2>/dev/null || echo "")
  case "$PASSED" in
    true)  PASS=$((PASS + 1)) ;;
    false) NOIMPROVE=$((NOIMPROVE + 1)) ;;
    *)     FAIL=$((FAIL + 1)) ;;
  esac
done

PRODUCED=$((PASS + NOIMPROVE + FAIL))
echo -e "${BOLD}━━━ Summary ━━━${NC}"
echo -e "  ${GREEN}✔ $PASS passed${NC}"
[ $NOIMPROVE -gt 0 ] && echo -e "  ${CYAN}⊘ $NOIMPROVE no improvement${NC}"
[ $FAIL -gt 0 ] && echo -e "  ${RED}✘ $FAIL unreadable${NC}"
[ $MISSING -gt 0 ] && echo -e "  ${RED}✘ $MISSING missing (eval produced no verdict)${NC}"
echo -e "  Skills evaluated: $PRODUCED/${#EVAL_FILES[@]}"
echo -e "  Results: $RESULTS_ROOT"
echo -e "  Experiment output: $RUN_DIR"

# Fail only when the harness could not produce a verdict for every skill we ran
# (e.g. the experiment crashed before writing output). Per-skill "no improvement"
# is an informational shadow result, not a harness failure.
[ "$PRODUCED" -lt "${#EVAL_FILES[@]}" ] && exit 1 || exit 0
