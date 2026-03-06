#!/bin/bash
set -euo pipefail

METADATA_PATH="${METADATA_PATH:-/drop/metadata.json}"

# No skill directories — baseline Copilot CLI run
echo "{\"skill_injected\": false}" > /agent/skill_metadata.json

ghcs run \
  --workspace /testbed \
  --prompt-file /drop/metadata.json \
  --output-dir /output \
  2>&1 | tee /output/trajectory.txt
