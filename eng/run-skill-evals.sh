#!/usr/bin/env bash
# Run local skill evaluations with the repository's pinned CI toolchain.
# Usage: ./eng/run-skill-evals.sh [plugin [skill]]
set -euo pipefail

if ! command -v node >/dev/null 2>&1; then
  echo "Node.js 22.14 or newer is required to run evaluations." >&2
  exit 1
fi

exec node "$(cd "$(dirname "$0")" && pwd)/evaluation-tools/run-skill-evals.mjs" "$@"
