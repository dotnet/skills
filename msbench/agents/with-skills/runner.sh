#!/bin/bash
set -euo pipefail

# Read instance metadata
METADATA_PATH="${METADATA_PATH:-/drop/metadata.json}"
INSTANCE_ID=$(python3 -c "import json; print(json.load(open('$METADATA_PATH'))['instance_id'])")

# The Copilot CLI special agent is configured to load skills from /agent/skills/
# via its native SessionConfig.SkillDirectories mechanism.

# Write skill metadata for custom_metrics tracking
SKILL_NAME=$(echo "$INSTANCE_ID" | sed 's/--/\n/g' | head -2 | tail -1)
PLUGIN_NAME=$(echo "$INSTANCE_ID" | sed 's/--/\n/g' | head -1)
SKILL_DIR="/agent/skills/${PLUGIN_NAME}/skills/${SKILL_NAME}"

echo "{\"skill_dir\": \"$SKILL_DIR\", \"skill_injected\": $([ -d \"$SKILL_DIR\" ] && echo true || echo false)}" > /agent/skill_metadata.json

# Launch via the standard CES agent entry point
cd "$AGENT_DIR"
set +u
. entry.sh
