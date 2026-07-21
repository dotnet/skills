#!/usr/bin/env bash
#
# run-skill-evals.sh — Run skill-vs-baseline evaluations locally, mirroring CI.
#
# Harness-agnostic entry point for contributors. Drives a single experiment run
# over the dotnet-skills experiment (baseline = no skills, skilled = the one
# skill under test), then uses the adapter (eng/vally-adapter/adapt.mjs) to split
# the output by eval and score each skill head-to-head, producing the per-skill
# results.json CI also produces. A skill passes only on a credible improvement
# (mean preference > 0 with its 95% CI above 0). The underlying evaluation
# harness is an implementation detail and may change without affecting this
# command or its output layout.
#
# Usage:
#   ./eng/run-skill-evals.sh                          # all skills
#   ./eng/run-skill-evals.sh dotnet-maui              # one plugin
#   ./eng/run-skill-evals.sh dotnet-maui maui-theming # one skill
#
# Subsetting uses the harness's `--eval-filter`, intersected with the experiment
# file's `evals:` glob — so `agent.*` evals (excluded by the glob) are dropped
# automatically and a filter can never pull in an undeclared eval. No filter runs
# the full declared set.
#
# Environment:
#   WORKERS=8         Max concurrent trials across the whole experiment (default: 8)
#   EXPERIMENT_FILE   Base experiment file (default: dotnet-skills.experiment.yaml)
#   VALLY             Eval CLI invocation (default: npx @microsoft/vally-cli)
#   RESULTS_DIR       Output root (default: ./eval-results)
#
# Model, judge model, and runs-per-stimulus come from the experiment file's
# `overrides:` block — edit dotnet-skills.experiment.yaml (or point EXPERIMENT_FILE
# at your own copy) to change them.
#
# Prerequisites (verified automatically at startup, with actionable errors):
#   - Node.js 20+ (CI uses 22); the eval CLI is fetched on first use via npx
#   - GITHUB_TOKEN for the Copilot SDK, or an authenticated GitHub CLI
#     (the script derives the token from `gh auth token` when GITHUB_TOKEN is unset)
#
# Per-skill verdicts go to ./eval-results/<plugin>/<skill>/results.json; the raw
# experiment output (per-variant JSONL + report.md) goes to
# ./eval-results/_experiment/<timestamp>/.

set -euo pipefail

SKILLS_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
ADAPTER_DIR="$SKILLS_ROOT/eng/vally-adapter"
VALLY="${VALLY:-npx @microsoft/vally-cli}"
EXPERIMENT_FILE="${EXPERIMENT_FILE:-$SKILLS_ROOT/dotnet-skills.experiment.yaml}"
RESULTS_ROOT="${RESULTS_DIR:-$SKILLS_ROOT/eval-results}"
WORKERS="${WORKERS:-8}"

# Read a key from the experiment file's top-level `overrides:` block (for display
# and to label the adapted verdicts). The experiment file is the source of truth.
read_override() {
  awk -v k="$1" '
    /^overrides:/ { f = 1; next }
    /^[^[:space:]]/ { f = 0 }
    f && $1 == k":" { print $2; exit }
  ' "$EXPERIMENT_FILE"
}
MODEL="$(read_override model)"
JUDGE_MODEL="$(read_override judge_model)"
RUNS="$(read_override runs)"
MODEL="${MODEL:-claude-opus-4.6}"
JUDGE_MODEL="${JUDGE_MODEL:-claude-opus-4.6}"
RUNS="${RUNS:-1}"

PLUGIN="${1:-}"
SKILL="${2:-}"

GREEN='\033[0;32m'
RED='\033[0;31m'
YELLOW='\033[0;33m'
CYAN='\033[0;36m'
BOLD='\033[1m'
NC='\033[0m'

# ---- Preflight: verify prerequisites, fail early with actionable guidance ---
# Node.js runs the eval CLI (fetched on first use via npx). Require 20+ (CI uses 22).
if ! command -v node >/dev/null 2>&1; then
  echo -e "${RED}Node.js is not installed.${NC} Install Node.js 20 or newer (CI uses 22):" >&2
  echo "  https://nodejs.org/en/download   (or, with nvm: 'nvm install 20')" >&2
  exit 1
fi
NODE_MAJOR="$(node -p 'process.versions.node.split(".")[0]' 2>/dev/null || echo 0)"
if [ "$NODE_MAJOR" -lt 20 ]; then
  echo -e "${RED}Node.js $(node --version) is too old.${NC} Install Node.js 20 or newer (CI uses 22)." >&2
  exit 1
fi

# GITHUB_TOKEN for the Copilot SDK. Use an explicit token if set; otherwise derive
# one from an authenticated GitHub CLI. Fail clearly when neither is available.
if [ -z "${GITHUB_TOKEN:-}" ]; then
  if command -v gh >/dev/null 2>&1 && gh auth token >/dev/null 2>&1; then
    GITHUB_TOKEN="$(gh auth token)"
    export GITHUB_TOKEN
    echo -e "${CYAN}Using a token from the GitHub CLI for the Copilot SDK.${NC}"
  else
    echo -e "${RED}No GITHUB_TOKEN, and the GitHub CLI is not authenticated.${NC} Do one of these, then re-run:" >&2
    echo "  - Run 'gh auth login'  (install the GitHub CLI from https://cli.github.com), or" >&2
    echo "  - export GITHUB_TOKEN=<a token with Copilot access>" >&2
    exit 1
  fi
fi

cd "$SKILLS_ROOT"

# ---- Scope ------------------------------------------------------------------
# Narrow the experiment to a plugin or single skill via --eval-filter. No filter
# = the full declared set. The skilled variant loads plugins/<plugin>/skills/<skill>,
# and vally fails fast if that directory is missing; adapt.mjs warns on any eval
# that produced no verdict, so a misconfigured eval is surfaced, not silent.
FILTER=()
SCOPE_DESC="all skills"
if [ -n "$PLUGIN" ] && [ -n "$SKILL" ]; then
  FILTER=(--eval-filter "tests/$PLUGIN/$SKILL/eval.yaml")
  SCOPE_DESC="$PLUGIN/$SKILL"
  CLEAR_DIR="$RESULTS_ROOT/$PLUGIN/$SKILL"
elif [ -n "$PLUGIN" ]; then
  FILTER=(--eval-filter "tests/$PLUGIN/**/eval.yaml")
  SCOPE_DESC="$PLUGIN"
  CLEAR_DIR="$RESULTS_ROOT/$PLUGIN"
else
  CLEAR_DIR=""
fi

echo -e "${BOLD}Running $SCOPE_DESC — model=$MODEL runs=$RUNS workers=$WORKERS${NC}"
echo ""

# ---- Run the experiment -----------------------------------------------------

EXPERIMENT_OUT="$RESULTS_ROOT/_experiment"
mkdir -p "$EXPERIMENT_OUT"

# Clear prior verdicts for the scope we're about to run so the summary reflects
# only this invocation's fresh output, not a stale results.json from earlier.
# For a full run (no plugin/skill arg) clear every per-skill verdict, but keep
# the _experiment raw output alongside.
if [ -n "$CLEAR_DIR" ]; then
  rm -rf "$CLEAR_DIR"
elif [ -d "$RESULTS_ROOT" ]; then
  find "$RESULTS_ROOT" -name results.json -not -path "$EXPERIMENT_OUT/*" -delete
fi

# Snapshot existing run dirs so we adapt the directory THIS run creates, never
# a stale one left behind when a run fails before writing any output.
RUN_DIRS_BEFORE=$(find "$EXPERIMENT_OUT" -mindepth 1 -maxdepth 1 -type d | sort)

EXPERIMENT_RC=0
$VALLY experiment run "$EXPERIMENT_FILE" \
  "${FILTER[@]}" \
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
  --vally "$VALLY" \
  --model "$MODEL" \
  --judge-model "$JUDGE_MODEL"

# ---- Summary ----------------------------------------------------------------

echo ""
PASS=0; NOIMPROVE=0; FAIL=0
while IFS= read -r RESULTS_JSON; do
  PASSED=$(node -e "const r=JSON.parse(require('fs').readFileSync(process.argv[1],'utf-8')); console.log(r.verdicts[0].passed)" "$RESULTS_JSON" 2>/dev/null || echo "")
  case "$PASSED" in
    true)  PASS=$((PASS + 1)) ;;
    false) NOIMPROVE=$((NOIMPROVE + 1)) ;;
    *)     FAIL=$((FAIL + 1)) ;;
  esac
done < <(find "$RESULTS_ROOT" -name results.json -not -path "$EXPERIMENT_OUT/*")

PRODUCED=$((PASS + NOIMPROVE + FAIL))
echo -e "${BOLD}━━━ Summary ━━━${NC}"
echo -e "  ${GREEN}✔ $PASS passed${NC}"
[ $NOIMPROVE -gt 0 ] && echo -e "  ${CYAN}⊘ $NOIMPROVE no improvement${NC}"
[ $FAIL -gt 0 ] && echo -e "  ${RED}✘ $FAIL unreadable${NC}"
echo -e "  Skills evaluated: $PRODUCED"
echo -e "  Results: $RESULTS_ROOT"
echo -e "  Experiment output: $RUN_DIR"

# Fail only when no verdict was produced at all (e.g. the experiment crashed
# before writing output). Per-skill "no improvement" is an informational shadow
# result, not a harness failure.
[ "$PRODUCED" -eq 0 ] && exit 1 || exit 0
