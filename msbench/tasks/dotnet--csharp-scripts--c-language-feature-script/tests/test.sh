#!/bin/bash
set -euo pipefail

# Load assertion framework
source /app/assertion_runner.sh

# Read instance ID from metadata
INSTANCE_ID=$(python3 -c "import json; print(json.load(open('/drop/metadata.json'))['instance_id'])")

# Initialize assertion counters
init_assertions

# --- Deterministic Assertions ---

assert_exit_success
assert_output_matches '(nint|nuint|native)'
assert_output_contains 'stackalloc'
assert_output_not_matches 'dotnet new console'

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
    --skill "csharp-scripts" \
    --plugin "dotnet"
