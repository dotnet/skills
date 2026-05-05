#!/usr/bin/env bash
#
# Initializes the development session by ensuring the roslyn-language-server
# tool is installed and up to date.

set -euo pipefail

TOOL_NAME="roslyn-language-server"
NUGET_SOURCE="https://api.nuget.org/v3/index.json"

create_session_directory() {
    local dir
    dir="$(mktemp -d)"
    echo "[$TOOL_NAME] Created temp directory: $dir" >&2
    cd "$dir"
    echo "$dir"
}

write_global_json() {
    local dir="$1"
    cat > "$dir/global.json" <<'EOF'
{
  "sdk": {
    "version": "10.0.100",
    "rollForward": "latestMajor"
  }
}
EOF
    echo "[$TOOL_NAME] Wrote global.json pinning SDK to 10.0.100 (rollForward: latestMajor)" >&2
}

get_installed_version() {
    local name="$1"
    dotnet tool list --global | grep -i "$name" | awk '{print $2}' || true
}

get_latest_version() {
    local name="$1"
    local source="$2"
    local search_json
    search_json=$(dotnet package search "$name" --source "$source" --exact-match --prerelease --format json)
    echo "$search_json" | jq -r \
        --arg name "$name" \
        '.searchResult[].packages[] | select(.id == $name) | .version' | tail -1
}

install_or_update_tool() {
    local current_version
    current_version=$(get_installed_version "$TOOL_NAME")

    if [[ -n "$current_version" ]]; then
        echo "[$TOOL_NAME] Installed version: $current_version"

        local latest_version
        latest_version=$(get_latest_version "$TOOL_NAME" "$NUGET_SOURCE")

        if [[ -z "$latest_version" ]]; then
            echo "[$TOOL_NAME] WARNING: Unable to determine the latest version. Skipping update." >&2
            return
        fi

        echo "[$TOOL_NAME] Latest available version: $latest_version"

        if [[ "$current_version" != "$latest_version" ]]; then
            echo "[$TOOL_NAME] Updating from $current_version to $latest_version..."
            dotnet tool update --global --prerelease "$TOOL_NAME" --add-source "$NUGET_SOURCE"
            echo "[$TOOL_NAME] Update complete."
        else
            echo "[$TOOL_NAME] Already up to date."
        fi
    else
        echo "[$TOOL_NAME] Not found. Installing latest prerelease..."
        dotnet tool install --global --prerelease "$TOOL_NAME" --add-source "$NUGET_SOURCE"
        echo "[$TOOL_NAME] Installation complete."
    fi
}

temp_dir=$(create_session_directory)
write_global_json "$temp_dir"
install_or_update_tool
