#!/usr/bin/env bash
# MSBench runner script for dotnet-maui plugin
#
# Installs the dotnet-maui plugin from the dotnet/skills marketplace
# before launching the Copilot CLI agent.

set -euo pipefail

# --- 1. Retrieve CAPI credentials from Azure Key Vault ---
echo "Retrieving CAPI credentials..."
export CAPI_HMAC_KEY=$(az keyvault secret show \
    --vault-name "e3dd1952ed8e46d89f93dev" \
    --name "copilotcli" \
    --query "value" \
    --output tsv)
export GITHUB_COPILOT_INTEGRATION_ID=copilot-developer-cli

METADATA_PATH="${METADATA_PATH:-/drop/metadata.json}"
INSTANCE_ID=$(python3 -c "import json; print(json.load(open('$METADATA_PATH'))['instance_id'])")

# --- 2. Read model from run metadata ---
MODEL=$(jq -r '.model // empty' "${AGENT_DIR}/run_metadata.json")
if [ -z "$MODEL" ]; then
  echo "ERROR: No model specified in run_metadata.json"
  exit 1
fi
export COPILOT_AGENT_MODEL="sweagent-capi:${MODEL}"
echo "Model: $MODEL"

export USE_COPILOT_CLI_VERSION="1.0.3"

### Create pre-run.sh
cat > "${AGENT_DIR}/pre-run.sh" << 'EOF'
echo "List marketplaces:"
copilot plugin marketplace list
echo "Adding dotnet-agent-skills marketplace..."
copilot plugin marketplace add dotnet/skills
echo "Installing dotnet-maui plugin from dotnet-agent-skills..."
copilot plugin install dotnet-maui@dotnet-agent-skills
echo "Plugins installed:"
copilot plugin list
EOF

# --- 3. Run Copilot CLI ---
echo "Starting Copilot CLI ..."
cd "$AGENT_DIR"
set +u
. entry.sh
