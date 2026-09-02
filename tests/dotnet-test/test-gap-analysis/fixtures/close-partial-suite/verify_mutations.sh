#!/usr/bin/env bash
set -euo pipefail

source_file="fixtures/close-partial-suite/src/DiscountRules.cs"
test_project="fixtures/close-partial-suite/tests/DiscountRules.Tests.csproj"
backup="$(mktemp)"
cp "$source_file" "$backup"

if python3 --version >/dev/null 2>&1; then
  python_command="python3"
else
  python_command="python"
fi

restore() {
  cp "$backup" "$source_file"
}
trap 'restore; rm -f "$backup"' EXIT

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

  if dotnet run --project "$test_project" >/dev/null 2>&1; then
    echo "Mutation survived: $label" >&2
    exit 1
  fi
}

expect_killed "string.IsNullOrWhiteSpace(code)" "string.IsNullOrEmpty(code)" "whitespace guard"
expect_killed "code.ToUpperInvariant() switch" "code switch" "case normalization"
expect_killed "Math.Max(0m, subtotal - 5m)" "subtotal - 5m" "FLAT5 floor"
expect_killed "subtotal < 0" "subtotal < -1m" "negative subtotal guard"
expect_killed '_ => throw new ArgumentException("Unknown discount code.", nameof(code))' "_ => subtotal" "unknown code rejection"
