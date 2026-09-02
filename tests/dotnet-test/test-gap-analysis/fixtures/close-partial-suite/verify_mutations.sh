#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
source_file="$script_dir/src/DiscountRules.cs"
test_project="$script_dir/tests/DiscountRules.Tests.csproj"
backup="$(mktemp)"
cp "$source_file" "$backup"

if command -v python3 >/dev/null 2>&1 && python3 -c 'import sys; raise SystemExit(sys.version_info < (3,))' >/dev/null 2>&1; then
  python_command="python3"
elif command -v python >/dev/null 2>&1 && python -c 'import sys; raise SystemExit(sys.version_info < (3,))' >/dev/null 2>&1; then
  python_command="python"
else
  echo "Python 3 is required to run the mutation verifier." >&2
  exit 1
fi

restore() {
  cp "$backup" "$source_file"
}
trap 'restore; rm -f "$backup"' EXIT

if ! baseline_output="$(dotnet run --project "$test_project" 2>&1)"; then
  printf '%s\n' "$baseline_output" >&2
  exit 1
fi

expect_killed() {
  local old="$1"
  local new="$2"
  local label="$3"

  restore
  "$python_command" - "$source_file" "$old" "$new" <<'PY'
import pathlib
import sys

path = pathlib.Path(sys.argv[1])
old = sys.argv[2]
new = sys.argv[3]
content = path.read_text(encoding="utf-8")
if old not in content:
    raise SystemExit(f"mutation source not found: {old}")
path.write_text(content.replace(old, new, 1), encoding="utf-8")
PY

  local build_output
  if ! build_output="$(dotnet build "$test_project" --nologo -v:q 2>&1)"; then
    printf '%s\n' "$build_output" >&2
    exit 1
  fi

  local test_output
  if test_output="$(dotnet run --project "$test_project" --no-build 2>&1)"; then
    echo "Mutation survived: $label" >&2
    exit 1
  else
    local test_exit_code=$?
    if grep -q "=== TEST EXECUTION SUMMARY ===" <<<"$test_output" &&
       grep -Eq "Failed: [1-9][0-9]*" <<<"$test_output"; then
      return
    fi

    echo "Mutation test infrastructure failed for $label (exit code $test_exit_code)." >&2
    printf '%s\n' "$test_output" >&2
    exit "$test_exit_code"
  fi
}

expect_killed "string.IsNullOrWhiteSpace(code)" "string.IsNullOrEmpty(code)" "whitespace guard"
expect_killed "code.ToUpperInvariant() switch" "code switch" "case normalization"
expect_killed "Math.Max(0m, subtotal - 5m)" "subtotal - 5m" "FLAT5 floor"
expect_killed "subtotal < 0" "subtotal < -1m" "negative subtotal guard"
expect_killed '_ => throw new ArgumentException("Unknown discount code.", nameof(code))' "_ => subtotal" "unknown code rejection"
