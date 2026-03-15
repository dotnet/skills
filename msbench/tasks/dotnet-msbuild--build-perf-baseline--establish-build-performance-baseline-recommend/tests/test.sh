#!/bin/bash
set -euo pipefail

# Load assertion framework
source /app/assertion_runner.sh

# Read instance ID from metadata
INSTANCE_ID=$(python3 -c "import json; print(json.load(open('/drop/metadata.json'))['instance_id'])")

# Initialize assertion counters
init_assertions

# --- Deterministic Assertions ---

assert_output_matches '(baseline|cold.build|warm.build|no.op)'
assert_output_matches '(redundant|transitive|unnecessary).*(reference|depend)'
assert_output_matches '(GenerateDocumentationFile|RunAnalyzers|EnforceCodeStyleInBuild)'

# --- Finalize ---
finalize_assertions

# Write evaluation results
mkdir -p /output /logs/verifier
echo "{\"$INSTANCE_ID\": {\"resolved\": $ALL_PASSED}}" > /output/eval.json

# Compute fractional reward (passed_assertions / total_assertions)
compute_reward

# Write custom metrics
python3 /app/write_eval.py "$INSTANCE_ID" "$ALL_PASSED" \
    --assertions-total $TOTAL_ASSERTIONS \
    --assertions-passed $PASSED_ASSERTIONS \
    --skill "build-perf-baseline" \
    --plugin "dotnet-msbuild"
