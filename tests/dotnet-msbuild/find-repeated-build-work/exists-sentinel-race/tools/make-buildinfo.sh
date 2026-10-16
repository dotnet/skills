#!/usr/bin/env sh
set -eu

out="$1"
mkdir -p "$(dirname "$out")"
printf 'internal static class BuildInfo { public const string Stamp = "%s"; }\n' "$(date -u +%Y%m%d)" > "$out"
