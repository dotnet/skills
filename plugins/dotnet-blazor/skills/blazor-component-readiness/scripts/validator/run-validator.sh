#!/usr/bin/env bash
set -euo pipefail

environment_failure() {
  printf 'error: %s\n' "$1" >&2
  exit 3
}

if ! script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"; then
  environment_failure 'could not resolve the validator script directory.'
fi
if ! skill_root="$(cd -- "$script_dir/../.." && pwd -P)"; then
  environment_failure 'could not resolve the readiness skill directory.'
fi
filesystem_name() {
  case "$(uname -s)" in
    MINGW*|MSYS*|CYGWIN*) printf '%s' "${1,,}" ;;
    *) printf '%s' "$1" ;;
  esac
}
if [[ "$(filesystem_name "$(basename -- "$skill_root")")" != blazor-component-readiness ||
      ! -f "$skill_root/SKILL.md" || ! -f "$skill_root/plugin-manifest.json" ]]; then
  environment_failure 'the readiness skill requires SKILL.md and its bundled plugin-manifest.json.'
fi
plugin_root="$skill_root"
if [[ "$(filesystem_name "$(basename -- "$(dirname -- "$skill_root")")")" == skills ]]; then
  plugin_root="$(cd -- "$skill_root/../.." && pwd -P)" ||
    environment_failure 'could not resolve the readiness plugin directory.'
  [[ -f "$plugin_root/plugin.json" ]] ||
    environment_failure 'the installed readiness layout requires its authoritative plugin.json.'
fi
project="$script_dir/BlazorComponentReadiness.Validator.csproj"
restore_config="$script_dir/restore-offline.config"

sdk_version="$(dotnet --version 2>/dev/null || true)"
if [[ ! "$sdk_version" =~ ^11\. ]]; then
  printf 'error: the readiness validator requires the repository-selected .NET 11 SDK; active SDK is %s\n' \
    "${sdk_version:-unavailable}" >&2
  printf 'Select an installed .NET 11 SDK before running this launcher.\n' >&2
  exit 3
fi

cleanup=false
if [[ -n "${READINESS_TEMP:-}" ]]; then
  readiness_parent="$(dirname -- "$READINESS_TEMP")"
  if [[ ! -d "$readiness_parent" ]]; then
    printf 'error: the parent of READINESS_TEMP must already exist: %s\n' \
      "$readiness_parent" >&2
    exit 3
  fi

  readiness_temp="$(cd -- "$readiness_parent" && pwd -P)/$(basename -- "$READINESS_TEMP")"
else
  temp_parent="${TMPDIR:-${TEMP:-${TMP:-}}}"
  if [[ -z "$temp_parent" ]]; then
    printf 'error: no temporary directory is configured; set READINESS_TEMP.\n' >&2
    exit 3
  fi

  temp_parent="$(cd -- "$temp_parent" && pwd -P)" ||
    environment_failure 'could not resolve the temporary parent directory.'
  readiness_temp="$temp_parent/blazor-readiness-validator-$$-$RANDOM"
  cleanup=true
fi

is_inside_plugin() {
  local candidate="$1/" boundary="$plugin_root/"
  case "$(uname -s)" in
    MINGW*|MSYS*|CYGWIN*) candidate="${candidate,,}"; boundary="${boundary,,}" ;;
  esac
  [[ "$candidate" == "$boundary"* ]]
}
requested_temp="$readiness_temp"
if [[ -d "$readiness_temp" ]]; then
  physical_temp="$(cd -- "$readiness_temp" && pwd -P)" ||
    environment_failure 'could not resolve READINESS_TEMP.'
  if is_inside_plugin "$physical_temp"; then
    if is_inside_plugin "$requested_temp"; then
      environment_failure 'READINESS_TEMP must be outside the plugin tree.'
    fi
    environment_failure 'READINESS_TEMP resolves inside the plugin tree.'
  fi
  readiness_temp="$physical_temp"
fi
if is_inside_plugin "$readiness_temp"; then
  environment_failure 'READINESS_TEMP must be outside the plugin tree.'
fi
if [[ "$cleanup" == true ]]; then
  (umask 077 && mkdir -- "$readiness_temp") ||
    environment_failure 'could not create the temporary validator directory.'
elif ! mkdir -p -- "$readiness_temp"; then
  environment_failure 'could not create READINESS_TEMP.'
fi
if ! readiness_temp="$(cd -- "$readiness_temp" && pwd -P)"; then
  environment_failure 'could not resolve READINESS_TEMP.'
fi
if is_inside_plugin "$readiness_temp"; then
    printf 'error: READINESS_TEMP resolves inside the plugin tree.\n' >&2
    exit 3
fi

if ! mkdir -p -- "$readiness_temp/packages" "$readiness_temp/bin" "$readiness_temp/obj"; then
  environment_failure 'could not create external artifact directories.'
fi
for artifact_directory in packages bin obj; do
  if [[ -L "$readiness_temp/$artifact_directory" ]]; then
    printf 'error: external artifact directories cannot be symbolic links: %s\n' \
      "$readiness_temp/$artifact_directory" >&2
    exit 3
  fi
done

if [[ "$cleanup" == true ]]; then
  trap 'rm -rf -- "$readiness_temp"' EXIT
fi

common=(
  -noAutoResponse
  -property:Configuration=Release
  -property:ImportDirectoryBuildProps=false
  -property:ImportDirectoryBuildTargets=false
  -property:ImportDirectoryPackagesProps=false
  "-property:RestoreConfigFile=$restore_config"
  "-property:RestorePackagesPath=$readiness_temp/packages"
  -property:NuGetAudit=false
  "-property:BaseOutputPath=$readiness_temp/bin/"
  "-property:BaseIntermediateOutputPath=$readiness_temp/obj/"
)

if ! dotnet msbuild "$project" -target:Restore "${common[@]}"; then
  environment_failure 'readiness validator restore failed.'
fi
if ! dotnet msbuild "$project" '-target:VerifyNoPackageReferences;Build' "${common[@]}"; then
  environment_failure 'readiness validator build failed.'
fi

validator="$readiness_temp/bin/Release/net11.0/BlazorComponentReadiness.Validator.dll"
if [[ ! -f "$validator" || -L "$validator" ]]; then
  printf 'error: the expected external validator DLL was not produced: %s\n' "$validator" >&2
  exit 3
fi

if READINESS_SKILL_ROOT="$skill_root" dotnet "$validator" "$@"; then
  exit 0
else
  validator_exit=$?
  case "$validator_exit" in
    1|2|3)
      exit "$validator_exit"
      ;;
    *)
      environment_failure \
        "readiness validator returned undocumented exit code $validator_exit."
      ;;
  esac
fi
