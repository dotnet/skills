#!/usr/bin/env sh
set -eu

out="$1"
mkdir -p "$(dirname "$out")"
printf 'internal static class BuildInfo { public const string Stamp = "' > "$out"
stamp="$(date -u +%Y%m%d)"
printf '%s"; }\n' "${BUILDINFO_STAMP:-$stamp}" >> "$out"
