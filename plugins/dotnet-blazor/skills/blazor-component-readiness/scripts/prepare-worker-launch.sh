#!/usr/bin/env bash
set -euo pipefail

usage() {
  printf '%s\n' \
    'usage: prepare-worker-launch.sh --unit DIR --plugin-dir PATH --prompt-file FILE --output FILE'
}

fail() {
  printf 'error: %s\n' "$1" >&2
  exit 2
}

unit=
plugin_dir=
prompt_file=
output_file=

while (($# > 0)); do
  case "$1" in
    --unit)
      (($# >= 2)) || fail '--unit requires a value'
      unit=$2
      shift 2
      ;;
    --plugin-dir)
      (($# >= 2)) || fail '--plugin-dir requires a value'
      plugin_dir=$2
      shift 2
      ;;
    --prompt-file)
      (($# >= 2)) || fail '--prompt-file requires a value'
      prompt_file=$2
      shift 2
      ;;
    --output)
      (($# >= 2)) || fail '--output requires a value'
      output_file=$2
      shift 2
      ;;
    --help|-h)
      usage
      exit 0
      ;;
    *)
      fail "unknown option '$1'"
      ;;
  esac
done

[[ -n "$unit" && -n "$plugin_dir" && -n "$prompt_file" && -n "$output_file" ]] ||
  fail 'all options are required'

[[ -d "$unit" ]] || fail "worker unit does not exist: $unit"
[[ -f "$prompt_file" ]] || fail "worker prompt does not exist: $prompt_file"
[[ ! -L "$prompt_file" ]] || fail "worker prompt cannot be a symbolic link: $prompt_file"

unit_root="$(cd -- "$unit" && pwd -P)" ||
  fail "could not resolve worker unit: $unit"

prompt_root="$(cd -- "$(dirname -- "$prompt_file")" && pwd -P)" ||
  fail "could not resolve worker prompt directory: $prompt_file"
case "$prompt_root/" in
  "$unit_root/"*) ;;
  *) fail "worker prompt must be inside worker unit: $prompt_file" ;;
esac

if [[ "$plugin_dir" = /* ]]; then
  plugin_candidate=$plugin_dir
else
  plugin_candidate="$unit_root/$plugin_dir"
fi

[[ -d "$plugin_candidate" ]] || fail "trusted plugin root does not exist: $plugin_dir"
plugin_root="$(cd -- "$plugin_candidate" && pwd -P)" ||
  fail "could not resolve trusted plugin root: $plugin_dir"

case "$plugin_root/" in
  "$unit_root/"*) ;;
  *) fail "trusted plugin root must be inside worker unit: $plugin_root" ;;
esac

[[ -f "$plugin_root/plugin.json" ]] ||
  fail "trusted plugin root is missing plugin.json: $plugin_root"
skill_path="$plugin_root/skills/blazor-component-readiness/SKILL.md"
[[ -f "$skill_path" ]] ||
  fail "trusted plugin root is missing blazor-component-readiness SKILL.md: $plugin_root"

prompt="$(cat -- "$prompt_file")" ||
  fail "could not read worker prompt: $prompt_file"

if printf '%s\n' "$prompt" | grep -Eiq 'plugin[[:space:]]+root'; then
  fail 'worker prompt must not supply an independent plugin root; use the generated absolute handoff'
fi

output_parent="$(dirname -- "$output_file")"
[[ -d "$output_parent" ]] ||
  fail "launch output directory must already exist: $output_parent"
output_parent="$(cd -- "$output_parent" && pwd -P)" ||
  fail "could not resolve launch output directory: $output_parent"
output_path="$output_parent/$(basename -- "$output_file")"

case "$output_path/" in
  "$unit_root/"*) ;;
  *) fail "launch output must be inside worker unit: $output_path" ;;
esac
[[ ! -L "$output_path" ]] || fail "launch output cannot be a symbolic link: $output_path"

prompt_output="$output_parent/worker-prompt.txt"
prompt_path="$prompt_root/$(basename -- "$prompt_file")"
[[ "$prompt_output" != "$prompt_path" ]] ||
  fail "generated worker prompt cannot overwrite the worker prompt: $prompt_path"
[[ "$output_path" != "$prompt_path" ]] ||
  fail "launch output cannot overwrite the worker prompt: $output_path"
[[ "$output_path" != "$prompt_output" ]] ||
  fail "launch output cannot overwrite the generated worker prompt: $output_path"
[[ ! -L "$prompt_output" ]] || fail "generated worker prompt cannot be a symbolic link: $prompt_output"
{
  printf '%s\n' "$prompt"
  printf 'Explicit trusted plugin root: %s\n' "$plugin_root"
  printf 'Authoritative bundled skill source: %s\n' "$skill_path"
  printf '%s\n' 'In explicit-root mode, load the complete authoritative bundled skill source above first. Do not invoke an unqualified registered skill by name or accept an inherited/global same-name skill. Resolve every referenced file and the validator relative to this exact SKILL.md. If loading this exact file is denied by permissions or content exclusion, fail closed and do not try another path.'
} > "$prompt_output"

{
  printf 'PLUGIN_ROOT=%q\n' "$plugin_root"
  printf 'CLI_PLUGIN_DIR=%q\n' "$plugin_root"
  printf 'WORKER_PROMPT_FILE=%q\n' "$prompt_output"
} > "$output_path"

printf '%s\n' "$output_path"
