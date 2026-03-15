#!/bin/bash
# prepare_agent_packages.sh
# Prepares the with-skills agent package from the plugins/ directory.
# Copies only in-scope skills (excludes MCP-dependent and deferred skills).

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"

# Excluded skills (MCP-dependent or deferred)
EXCLUDED_SKILLS=(
    "binlog-failure-analysis"
    "binlog-generation"
    "build-perf-diagnostics"
    "build-parallelism"
    "dump-collect"
)

is_excluded() {
    local skill="$1"
    for excluded in "${EXCLUDED_SKILLS[@]}"; do
        if [ "$skill" = "$excluded" ]; then return 0; fi
    done
    return 1
}

DEST="$REPO_ROOT/msbench/agents/with-skills/skills"
rm -rf "$DEST"

TOTAL_SKILLS=0

for plugin in dotnet dotnet-msbuild; do
    # Copy plugin.json
    mkdir -p "$DEST/$plugin/skills"
    cp "$REPO_ROOT/plugins/$plugin/plugin.json" "$DEST/$plugin/"

    # Copy each in-scope skill
    for skill_dir in "$REPO_ROOT/plugins/$plugin/skills"/*/; do
        skill_name=$(basename "$skill_dir")
        if is_excluded "$skill_name"; then
            echo "Skipping excluded skill: $plugin/$skill_name"
            continue
        fi
        cp -r "$skill_dir" "$DEST/$plugin/skills/$skill_name"
        echo "Copied: $plugin/$skill_name"
        TOTAL_SKILLS=$((TOTAL_SKILLS + 1))
    done
done

echo ""
echo "Agent package prepared: $TOTAL_SKILLS skills"
echo "Skills directory: $DEST"
echo "SKILL.md count: $(find "$DEST" -name SKILL.md | wc -l)"
