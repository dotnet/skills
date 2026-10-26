#!/usr/bin/env bash
#
# Run local skill-vs-baseline evaluations with the pinned CI toolchain.
#
# Usage:
#   ./eng/run-skill-evals.sh
#   ./eng/run-skill-evals.sh dotnet-maui
#   ./eng/run-skill-evals.sh dotnet-maui maui-theming
#
# Environment:
#   WORKERS=8         Max concurrent trials (default: 8)
#   EXPERIMENT_FILE   Base experiment file
#   VALLY             Optional Vally command override
#   RESULTS_DIR       Output root (default: ./eval-results)

set -euo pipefail

SKILLS_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
ADAPTER_DIR="$SKILLS_ROOT/eng/vally-adapter"
TOOLS_DIR="$SKILLS_ROOT/eng/evaluation-tools"
EXPERIMENT_FILE="${EXPERIMENT_FILE:-$SKILLS_ROOT/dotnet-skills.experiment.yaml}"
RESULTS_ROOT="${RESULTS_DIR:-$SKILLS_ROOT/eval-results}"
WORKERS="${WORKERS:-8}"
PLUGIN="${1:-}"
SKILL="${2:-}"

GREEN='\033[0;32m'
RED='\033[0;31m'
YELLOW='\033[0;33m'
CYAN='\033[0;36m'
BOLD='\033[1m'
NC='\033[0m'

if ! command -v node >/dev/null 2>&1; then
  echo -e "${RED}Node.js is not installed.${NC} Install Node.js 22 or newer (CI uses 22)." >&2
  exit 1
fi
NODE_MAJOR="$(node -p 'process.versions.node.split(".")[0]' 2>/dev/null || echo 0)"
if [ "$NODE_MAJOR" -lt 22 ]; then
  echo -e "${RED}Node.js $(node --version) is too old.${NC} Install Node.js 22 or newer (CI uses 22)." >&2
  exit 1
fi

if [ -z "${GITHUB_TOKEN:-}" ]; then
  if command -v gh >/dev/null 2>&1 && gh auth token >/dev/null 2>&1; then
    GITHUB_TOKEN="$(gh auth token)"
    export GITHUB_TOKEN
    echo -e "${CYAN}Using a token from the GitHub CLI for the Copilot SDK.${NC}"
  else
    echo -e "${RED}No GITHUB_TOKEN, and the GitHub CLI is not authenticated.${NC} Do one of these, then re-run:" >&2
    echo "  - Run 'gh auth login' (install the GitHub CLI from https://cli.github.com), or" >&2
    echo "  - export GITHUB_TOKEN=<a token with Copilot access>" >&2
    exit 1
  fi
fi

if [ -n "$PLUGIN" ] && { [[ ! "$PLUGIN" =~ ^[A-Za-z0-9._-]+$ ]] || [ "$PLUGIN" = "." ] || [[ "$PLUGIN" == *".."* ]]; }; then
  echo -e "${RED}Invalid plugin '$PLUGIN'. Use one directory name without '..'.${NC}" >&2
  exit 1
fi
if [ -n "$SKILL" ] && { [[ ! "$SKILL" =~ ^[A-Za-z0-9._-]+$ ]] || [ "$SKILL" = "." ] || [[ "$SKILL" == *".."* ]]; }; then
  echo -e "${RED}Invalid skill '$SKILL'. Use one directory name without '..'.${NC}" >&2
  exit 1
fi
if [ -n "$SKILL" ] && [ -z "$PLUGIN" ]; then
  echo -e "${RED}A skill name requires a plugin name.${NC}" >&2
  exit 1
fi
if [ "$PLUGIN" = "agentic-workflows" ]; then
  echo -e "${RED}agentic-workflows uses the native SDK evaluation lane, not this skill runner.${NC}" >&2
  exit 1
fi
if [[ "$SKILL" == agent.* ]]; then
  echo -e "${RED}Custom-agent evals use the native SDK evaluation lane, not this skill runner.${NC}" >&2
  exit 1
fi

cd "$SKILLS_ROOT"

if [ ! -f "$EXPERIMENT_FILE" ]; then
  echo -e "${RED}Experiment file not found: $EXPERIMENT_FILE${NC}" >&2
  exit 1
fi
if [ -n "$PLUGIN" ] && [ ! -d "plugins/$PLUGIN/skills" ]; then
  echo -e "${RED}No skill directory found at plugins/$PLUGIN/skills${NC}" >&2
  exit 1
fi
if [ -n "$SKILL" ] && [ ! -f "plugins/$PLUGIN/skills/$SKILL/SKILL.md" ]; then
  echo -e "${RED}No skill file found at plugins/$PLUGIN/skills/$SKILL/SKILL.md${NC}" >&2
  exit 1
fi

if ! command -v npm >/dev/null 2>&1; then
  echo -e "${RED}npm is required to verify or install the pinned evaluation tools.${NC}" >&2
  exit 1
fi
if ! npm ls --depth=0 --prefix "$TOOLS_DIR" >/dev/null 2>&1; then
  echo "Installing the pinned CI evaluation tools from eng/evaluation-tools/package-lock.json..."
  npm ci --prefix "$TOOLS_DIR" --registry https://registry.npmjs.org/
fi

if [ -n "${VALLY:-}" ]; then
  VALLY_COMMAND="$VALLY"
  read -r -a VALLY_ARGS <<< "$VALLY"
else
  VALLY_COMMAND="node \"$TOOLS_DIR/vally.mjs\""
  VALLY_ARGS=(node "$TOOLS_DIR/vally.mjs")
fi
if [ "${#VALLY_ARGS[@]}" -eq 0 ]; then
  echo -e "${RED}VALLY must name a command.${NC}" >&2
  exit 1
fi

read_override() {
  awk -v k="$1" '
    /^overrides:/ { f = 1; next }
    /^[^[:space:]]/ { f = 0 }
    f && $1 == k":" { print $2; exit }
  ' "$EXPERIMENT_FILE"
}
MODEL="$(read_override model)"
JUDGE_MODEL="$(read_override judge_model)"
MODEL="${MODEL:-claude-opus-4.6}"
JUDGE_MODEL="${JUDGE_MODEL:-claude-opus-4.6}"

EXPECTED_EVALS=()
if [ -n "$PLUGIN" ] && [ -n "$SKILL" ]; then
  EVAL_FILE="tests/$PLUGIN/$SKILL/eval.yaml"
  if [ ! -f "$EVAL_FILE" ]; then
    echo -e "${RED}No eval spec found at $EVAL_FILE${NC}" >&2
    exit 1
  fi
  EXPECTED_EVALS=("$EVAL_FILE")
elif [ -n "$PLUGIN" ]; then
  mapfile -t EXPECTED_EVALS < <(
    find "tests/$PLUGIN" -mindepth 2 -maxdepth 2 -type f -name eval.yaml \
      ! -path '*/agent.*/*' 2>/dev/null | LC_ALL=C sort
  )
else
  mapfile -t EXPECTED_EVALS < <(
    find tests -mindepth 3 -maxdepth 3 -type f -name eval.yaml \
      ! -path '*/agent.*/*' \
      ! -path 'tests/agentic-workflows/*' 2>/dev/null | LC_ALL=C sort
  )
fi
if [ "${#EXPECTED_EVALS[@]}" -eq 0 ]; then
  echo -e "${RED}No skill eval specs found for the requested scope.${NC}" >&2
  exit 1
fi

PLUGIN_GROUPS=()
while IFS= read -r item; do
  [ -n "$item" ] && PLUGIN_GROUPS+=("$item")
done < <(printf '%s\n' "${EXPECTED_EVALS[@]}" | awk -F/ '{ print $2 }' | LC_ALL=C sort -u)

if [ -n "$SKILL" ]; then
  SCOPE_DESC="$PLUGIN/$SKILL"
elif [ -n "$PLUGIN" ]; then
  SCOPE_DESC="$PLUGIN"
else
  SCOPE_DESC="all skills"
fi
echo -e "${BOLD}Running $SCOPE_DESC — model=$MODEL workers=$WORKERS${NC}"
echo "Expected skill evals: ${#EXPECTED_EVALS[@]}"
echo ""

EXPERIMENT_ROOT="$RESULTS_ROOT/_experiment"
RUN_ID="$(date -u +%Y%m%dT%H%M%SZ)-$$"
STAGING_ROOT="$RESULTS_ROOT/_local-staging/$RUN_ID"
mkdir -p "$EXPERIMENT_ROOT/$RUN_ID" "$STAGING_ROOT"
GENERATED_EXPERIMENTS=()
RUN_COMPLETE=0
cleanup() {
  if [ "$RUN_COMPLETE" -eq 1 ]; then
    rm -rf "$STAGING_ROOT"
  else
    echo "Preserving partial local-evaluation diagnostics at $STAGING_ROOT" >&2
  fi
  if [ "${#GENERATED_EXPERIMENTS[@]}" -gt 0 ]; then
    rm -f "${GENERATED_EXPERIMENTS[@]}"
  fi
}
trap cleanup EXIT

if [ -n "$SKILL" ]; then
  CLEAR_DIR="$RESULTS_ROOT/$PLUGIN/$SKILL"
elif [ -n "$PLUGIN" ]; then
  CLEAR_DIR="$RESULTS_ROOT/$PLUGIN"
else
  CLEAR_DIR=""
fi
if [ -n "$CLEAR_DIR" ]; then
  rm -rf "$CLEAR_DIR"
else
  find "$RESULTS_ROOT" -name results.json -type f \
    -not -path "$EXPERIMENT_ROOT/*" \
    -not -path "$RESULTS_ROOT/_local-staging/*" -delete
fi

TOTAL_PASS=0
TOTAL_NO_CHANGE=0
TOTAL_UNDERPOWERED=0
TOTAL=0

for RUN_PLUGIN in "${PLUGIN_GROUPS[@]}"; do
  PLUGIN_EVALS=()
  for EVAL_PATH in "${EXPECTED_EVALS[@]}"; do
    [ "${EVAL_PATH#tests/$RUN_PLUGIN/}" != "$EVAL_PATH" ] && PLUGIN_EVALS+=("$EVAL_PATH")
  done
  [ "${#PLUGIN_EVALS[@]}" -gt 0 ] || continue

  PLUGIN_STAGE="$STAGING_ROOT/$RUN_PLUGIN"
  EXPECTED_FILE="$PLUGIN_STAGE/expected-evals.txt"
  EXPERIMENT_OUT="$EXPERIMENT_ROOT/$RUN_ID/$RUN_PLUGIN"
  mkdir -p "$PLUGIN_STAGE" "$EXPERIMENT_OUT"
  printf '%s\n' "${PLUGIN_EVALS[@]}" > "$EXPECTED_FILE"

  GENERATED_EXPERIMENT="${EXPERIMENT_FILE%.*}.local.$RUN_ID.$RUN_PLUGIN.yaml"
  GENERATED_EXPERIMENTS+=("$GENERATED_EXPERIMENT")
  node "$ADAPTER_DIR/gen-experiment.mjs" \
    --plugin "$RUN_PLUGIN" \
    --base "$EXPERIMENT_FILE" \
    --content-root "$SKILLS_ROOT" \
    --model "$MODEL" \
    --judge-model "$JUDGE_MODEL" \
    --out "$GENERATED_EXPERIMENT"

  FILTER_ARGS=()
  if [ -n "$SKILL" ]; then
    FILTER_ARGS=(--eval-filter "tests/$RUN_PLUGIN/$SKILL/eval.yaml")
  else
    FILTER_ARGS=(--eval-filter "tests/$RUN_PLUGIN/**/eval.yaml")
  fi

  echo "Running $RUN_PLUGIN (${#PLUGIN_EVALS[@]} expected eval(s))"
  EXPERIMENT_RC=0
  "${VALLY_ARGS[@]}" experiment run "$GENERATED_EXPERIMENT" \
    "${FILTER_ARGS[@]}" \
    --output-dir "$EXPERIMENT_OUT" \
    --workers "$WORKERS" || EXPERIMENT_RC=$?
  RUN_DIR="$(find "$EXPERIMENT_OUT" -mindepth 1 -maxdepth 1 -type d | LC_ALL=C sort | tail -1)"
  if [ -z "$RUN_DIR" ]; then
    echo -e "${RED}No new experiment output directory produced for $RUN_PLUGIN.${NC}" >&2
    exit 1
  fi

  node "$ADAPTER_DIR/adapt.mjs" \
    --experiment-dir "$RUN_DIR" \
    --experiment-file "$GENERATED_EXPERIMENT" \
    --output-root "$PLUGIN_STAGE" \
    --vally "$VALLY_COMMAND" \
    --repo-root "$SKILLS_ROOT" \
    --expected-evals "$EXPECTED_FILE" \
    --require-plugin \
    --model "$MODEL" \
    --judge-model "$JUDGE_MODEL"

  node "$TOOLS_DIR/validate-results.mjs" \
    --results-dir "$PLUGIN_STAGE" \
    --expected-evals "$EXPECTED_FILE" \
    --adapter-summary "$PLUGIN_STAGE/adapter-summary.json"
  if [ "$EXPERIMENT_RC" -ne 0 ]; then
    echo -e "${RED}Vally experiment run failed with exit code $EXPERIMENT_RC for $RUN_PLUGIN. No trials were rerun.${NC}" >&2
    exit "$EXPERIMENT_RC"
  fi

  while IFS= read -r RESULTS_JSON; do
    [ -n "$RESULTS_JSON" ] || continue
    RELATIVE_PATH="${RESULTS_JSON#"$PLUGIN_STAGE"/}"
    FINAL_PATH="$RESULTS_ROOT/$RELATIVE_PATH"
    mkdir -p "$(dirname "$FINAL_PATH")"
    cp "$RESULTS_JSON" "$FINAL_PATH"
  done < <(find "$PLUGIN_STAGE" -type f -name results.json | LC_ALL=C sort)

  TOTAL=$((TOTAL + ${#PLUGIN_EVALS[@]}))
  while IFS= read -r EVAL_PATH; do
    [ -n "$EVAL_PATH" ] || continue
    PLUGIN_NAME="$(printf '%s' "$EVAL_PATH" | cut -d/ -f2)"
    SKILL_NAME="$(basename "$(dirname "$EVAL_PATH")")"
    RESULTS_JSON="$RESULTS_ROOT/$PLUGIN_NAME/$SKILL_NAME/results.json"
    STATE="$(node -e "
      const r = JSON.parse(require('fs').readFileSync(process.argv[1], 'utf8'));
      const v = r.verdicts[0];
      const s = v.state ?? (v.conclusive === false || v.underpowered === true
        ? 'INVALID_INCONCLUSIVE' : v.passed ? 'VALID_PASS' : 'VALID_NO_CHANGE');
      console.log(s);
    " "$RESULTS_JSON")"
    case "$STATE" in
      VALID_PASS) TOTAL_PASS=$((TOTAL_PASS + 1)) ;;
      VALID_NO_CHANGE) TOTAL_NO_CHANGE=$((TOTAL_NO_CHANGE + 1)) ;;
      INVALID_INCONCLUSIVE) TOTAL_UNDERPOWERED=$((TOTAL_UNDERPOWERED + 1)) ;;
    esac
  done < <(printf '%s\n' "${PLUGIN_EVALS[@]}")
done

echo ""
echo -e "${BOLD}━━━ Summary ━━━${NC}"
echo -e "  ${GREEN}✔ $TOTAL_PASS passed${NC}"
[ "$TOTAL_NO_CHANGE" -gt 0 ] && echo -e "  ${CYAN}⊘ $TOTAL_NO_CHANGE no improvement${NC}"
[ "$TOTAL_UNDERPOWERED" -gt 0 ] && echo -e "  ${YELLOW}⚠ $TOTAL_UNDERPOWERED underpowered${NC}"
echo -e "  Skills evaluated: $TOTAL"
echo -e "  Results: $RESULTS_ROOT"
echo -e "  Experiment output: $EXPERIMENT_ROOT/$RUN_ID"
RUN_COMPLETE=1
