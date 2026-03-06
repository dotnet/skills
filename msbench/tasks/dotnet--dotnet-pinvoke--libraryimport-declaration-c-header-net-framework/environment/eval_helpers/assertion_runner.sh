#!/bin/bash
# assertion_runner.sh — Generic assertion evaluation framework
# Source this from test.sh to get assertion helper functions.
#
# Usage in test.sh:
#   source /app/assertion_runner.sh
#   init_assertions
#   assert_output_contains "expected text"
#   assert_file_exists "*.csv"
#   finalize_assertions
#   write_eval_result "$INSTANCE_ID" "$ALL_PASSED"

set -euo pipefail

# --- Globals ---
TOTAL_ASSERTIONS=0
PASSED_ASSERTIONS=0

# Collect agent output from known locations
AGENT_OUTPUT=""
if [ -f /testbed/agent_output.txt ]; then
    AGENT_OUTPUT=$(cat /testbed/agent_output.txt 2>/dev/null || true)
fi
if [ -f /output/agent_output.txt ]; then
    AGENT_OUTPUT="${AGENT_OUTPUT}$(cat /output/agent_output.txt 2>/dev/null || true)"
fi
if [ -f /output/trajectory.txt ]; then
    AGENT_OUTPUT="${AGENT_OUTPUT}$(cat /output/trajectory.txt 2>/dev/null || true)"
fi

# --- Initialization ---
init_assertions() {
    TOTAL_ASSERTIONS=0
    PASSED_ASSERTIONS=0
}

# --- Assertion Functions ---

# Check if agent output or any modified file contains a string (case-insensitive)
assert_output_contains() {
    local expected="$1"
    local label="${2:-output_contains: $expected}"
    TOTAL_ASSERTIONS=$((TOTAL_ASSERTIONS + 1))

    if echo "$AGENT_OUTPUT" | grep -qi "$expected" 2>/dev/null; then
        echo "PASS: $label"
        PASSED_ASSERTIONS=$((PASSED_ASSERTIONS + 1))
        return 0
    fi

    echo "FAIL: $label"
    return 0  # Don't exit on failure — we count them
}

# Check that agent output does NOT contain a string (case-insensitive)
assert_output_not_contains() {
    local unexpected="$1"
    local label="${2:-output_not_contains: $unexpected}"
    TOTAL_ASSERTIONS=$((TOTAL_ASSERTIONS + 1))

    if echo "$AGENT_OUTPUT" | grep -qi "$unexpected" 2>/dev/null; then
        echo "FAIL: $label (found unwanted content)"
        return 0
    fi

    echo "PASS: $label"
    PASSED_ASSERTIONS=$((PASSED_ASSERTIONS + 1))
    return 0
}

# Check if agent output matches a regex pattern (case-insensitive, Perl-compatible)
assert_output_matches() {
    local pattern="$1"
    local label="${2:-output_matches: $pattern}"
    TOTAL_ASSERTIONS=$((TOTAL_ASSERTIONS + 1))

    if echo "$AGENT_OUTPUT" | grep -qPi "$pattern" 2>/dev/null; then
        echo "PASS: $label"
        PASSED_ASSERTIONS=$((PASSED_ASSERTIONS + 1))
        return 0
    fi

    echo "FAIL: $label"
    return 0
}

# Check that agent output does NOT match a regex pattern
assert_output_not_matches() {
    local pattern="$1"
    local label="${2:-output_not_matches: $pattern}"
    TOTAL_ASSERTIONS=$((TOTAL_ASSERTIONS + 1))

    if echo "$AGENT_OUTPUT" | grep -qPi "$pattern" 2>/dev/null; then
        echo "FAIL: $label (matched unwanted pattern)"
        return 0
    fi

    echo "PASS: $label"
    PASSED_ASSERTIONS=$((PASSED_ASSERTIONS + 1))
    return 0
}

# Check if a file matching a glob pattern exists under /testbed/
assert_file_exists() {
    local pattern="$1"
    local label="${2:-file_exists: $pattern}"
    TOTAL_ASSERTIONS=$((TOTAL_ASSERTIONS + 1))

    if compgen -G "/testbed/$pattern" > /dev/null 2>&1; then
        echo "PASS: $label"
        PASSED_ASSERTIONS=$((PASSED_ASSERTIONS + 1))
        return 0
    fi

    # Try find for recursive glob patterns
    if find /testbed/ -path "/testbed/$pattern" -print -quit 2>/dev/null | grep -q .; then
        echo "PASS: $label (found via find)"
        PASSED_ASSERTIONS=$((PASSED_ASSERTIONS + 1))
        return 0
    fi

    # Handle ** glob patterns
    if [[ "$pattern" == *"**"* ]]; then
        local base_pattern="${pattern##**/}"
        if find /testbed/ -name "$base_pattern" -print -quit 2>/dev/null | grep -q .; then
            echo "PASS: $label (found via recursive search)"
            PASSED_ASSERTIONS=$((PASSED_ASSERTIONS + 1))
            return 0
        fi
    fi

    echo "FAIL: $label"
    return 0
}

# Check that a file matching a glob pattern does NOT exist
assert_file_not_exists() {
    local pattern="$1"
    local label="${2:-file_not_exists: $pattern}"
    TOTAL_ASSERTIONS=$((TOTAL_ASSERTIONS + 1))

    if compgen -G "/testbed/$pattern" > /dev/null 2>&1; then
        echo "FAIL: $label (file exists)"
        return 0
    fi

    echo "PASS: $label"
    PASSED_ASSERTIONS=$((PASSED_ASSERTIONS + 1))
    return 0
}

# Check if a file contains specific content (case-insensitive)
assert_file_contains() {
    local filepath="$1"
    local content="$2"
    local label="${3:-file_contains: $filepath -> $content}"
    TOTAL_ASSERTIONS=$((TOTAL_ASSERTIONS + 1))

    # Handle glob patterns in filepath
    local found=false
    for f in /testbed/$filepath; do
        if [ -f "$f" ] && grep -qi "$content" "$f" 2>/dev/null; then
            found=true
            break
        fi
    done

    # Try recursive find for ** patterns
    if [ "$found" = false ] && [[ "$filepath" == *"**"* ]]; then
        local base_pattern="${filepath##**/}"
        while IFS= read -r f; do
            if grep -qi "$content" "$f" 2>/dev/null; then
                found=true
                break
            fi
        done < <(find /testbed/ -name "$base_pattern" 2>/dev/null)
    fi

    if [ "$found" = true ]; then
        echo "PASS: $label"
        PASSED_ASSERTIONS=$((PASSED_ASSERTIONS + 1))
    else
        echo "FAIL: $label"
    fi
    return 0
}

# exit_success is always true in MSBench (agent ran to completion)
assert_exit_success() {
    local label="${1:-exit_success}"
    TOTAL_ASSERTIONS=$((TOTAL_ASSERTIONS + 1))
    echo "PASS: $label (always true in MSBench)"
    PASSED_ASSERTIONS=$((PASSED_ASSERTIONS + 1))
    return 0
}

# --- Finalization ---

# Check if all assertions passed
finalize_assertions() {
    if [ "$TOTAL_ASSERTIONS" -gt 0 ] && [ "$PASSED_ASSERTIONS" -eq "$TOTAL_ASSERTIONS" ]; then
        ALL_PASSED=true
    else
        ALL_PASSED=false
    fi
    echo ""
    echo "=== Assertion Summary ==="
    echo "Passed: $PASSED_ASSERTIONS / $TOTAL_ASSERTIONS"
    echo "Resolved: $ALL_PASSED"
}

# --- Build Check ---

# Try to build the .NET project and return success/failure
check_dotnet_build() {
    local build_dir="${1:-/testbed}"
    local BUILD_SUCCESS=0

    if ls "$build_dir"/*.sln "$build_dir"/*.csproj 2>/dev/null | head -1 > /dev/null 2>&1; then
        if dotnet build "$build_dir" 2>&1 | tee /output/build.log; then
            BUILD_SUCCESS=1
        fi
    else
        BUILD_SUCCESS=1  # No buildable project — skip
    fi

    echo "$BUILD_SUCCESS"
}
