#!/usr/bin/env bash

set -euo pipefail

: "${COPILOT_GITHUB_TOKEN:?COPILOT_GITHUB_TOKEN is required}"
: "${GATEWAY_IMAGE:?GATEWAY_IMAGE is required}"
: "${RUNNER_TEMP:?RUNNER_TEMP is required}"

name="devops-health-pin-canary-${GITHUB_RUN_ID:-local}-${GITHUB_RUN_ATTEMPT:-1}"
gateway_pid=""
backend_pid=""
cleanup() {
  docker rm --force "$name" >/dev/null 2>&1 || true
  if [[ -n "$gateway_pid" ]]; then
    wait "$gateway_pid" 2>/dev/null || true
  fi
  if [[ -n "$backend_pid" ]]; then
    kill "$backend_pid" 2>/dev/null || true
    wait "$backend_pid" 2>/dev/null || true
  fi
}
trap cleanup EXIT

: > "$RUNNER_TEMP/mcp-canary-evidence.jsonl"
CANARY_PORT=18081 \
CANARY_EVIDENCE="$RUNNER_TEMP/mcp-canary-evidence.jsonl" \
  node eng/devops-health/mcp_canary_server.mjs \
  > "$RUNNER_TEMP/mcp-canary-backend.log" 2>&1 &
backend_pid=$!
for attempt in $(seq 1 20); do
  if curl --fail --silent http://127.0.0.1:18081/health >/dev/null; then
    break
  fi
  if ! kill -0 "$backend_pid" 2>/dev/null; then
    cat "$RUNNER_TEMP/mcp-canary-backend.log"
    echo "::error::Trusted MCP canary backend exited during startup"
    exit 1
  fi
  if [[ "$attempt" == "20" ]]; then
    echo "::error::Trusted MCP canary backend did not become healthy"
    exit 1
  fi
  sleep 1
done

cat > "$RUNNER_TEMP/gateway-config.json" <<'JSON'
{
  "mcpServers": {
    "canary": {
      "type": "http",
      "url": "http://host.docker.internal:18081/mcp"
    }
  },
  "gateway": {
    "port": 18080,
    "domain": "localhost",
    "agentId": "devops-health-pin-canary"
  }
}
JSON

docker pull "$GATEWAY_IMAGE"
docker run --rm --interactive --name "$name" \
  --add-host host.docker.internal:host-gateway \
  --publish 127.0.0.1:18080:18080 \
  --volume /var/run/docker.sock:/var/run/docker.sock \
  --env MCP_GATEWAY_PORT=18080 \
  --env MCP_GATEWAY_DOMAIN=localhost \
  --env MCP_GATEWAY_AGENT_ID=devops-health-pin-canary \
  --env DOCKER_HOST=unix:///var/run/docker.sock \
  "$GATEWAY_IMAGE" \
  < "$RUNNER_TEMP/gateway-config.json" \
  > "$RUNNER_TEMP/gateway.log" 2>&1 &
gateway_pid=$!
for attempt in $(seq 1 20); do
  if curl --fail --silent http://127.0.0.1:18080/health >/dev/null; then
    break
  fi
  if ! kill -0 "$gateway_pid" 2>/dev/null; then
    cat "$RUNNER_TEMP/gateway.log"
    echo "::error::MCP Gateway exited before its health check passed"
    exit 1
  fi
  if [[ "$attempt" == "20" ]]; then
    cat "$RUNNER_TEMP/gateway.log"
    echo "::error::MCP Gateway health canary failed"
    exit 1
  fi
  sleep 1
done

mkdir -p "$HOME/.copilot"
cat > "$HOME/.copilot/mcp-config.json" <<'JSON'
{
  "mcpServers": {
    "canary": {
      "type": "http",
      "url": "http://127.0.0.1:18080/mcp/canary",
      "headers": {
        "Authorization": "devops-health-pin-canary"
      },
      "tools": ["*"]
    }
  }
}
JSON
chmod 600 "$HOME/.copilot/mcp-config.json"
export XDG_CONFIG_HOME="$HOME"
timeout 90 copilot \
  --prompt "Call the canary read_canary tool exactly once, then reply with its result." \
  --disable-builtin-mcps \
  --no-ask-user \
  --allow-tool canary \
  --silent \
  --effort=low \
  > "$RUNNER_TEMP/copilot-canary.log"

jq -s -e '
  [.[] | .method] as $methods |
  ($methods | index("initialize")) as $initialize |
  ($methods | index("notifications/initialized")) as $initialized |
  ($methods | index("tools/list")) as $list |
  ($methods | index("tools/call")) as $call |
  $initialize != null and
  $initialized != null and
  $list != null and
  $call != null and
  $initialize < $initialized and
  $initialized < $list and
  $list < $call and
  any(.[]; .method == "initialize" and
    (.protocolVersion | type == "string") and
    (.protocolVersion | length > 0)) and
  any(.[]; .method == "tools/call" and
    .toolName == "read_canary")
' "$RUNNER_TEMP/mcp-canary-evidence.jsonl" >/dev/null

protocol=$(
  jq -r 'select(.method == "initialize") | .protocolVersion' \
    "$RUNNER_TEMP/mcp-canary-evidence.jsonl" |
    tail -1
)
if ! grep -q "DEVOPS_HEALTH_MCP_CANARY_OK" \
  "$RUNNER_TEMP/copilot-canary.log"; then
  cat "$RUNNER_TEMP/copilot-canary.log"
  echo "::error::Copilot did not return the MCP tool result"
  exit 1
fi

echo "Copilot CLI: $(copilot --version | head -1)"
echo "MCP Gateway: $GATEWAY_IMAGE"
echo "MCP protocol: $protocol"
echo "MCP flow: initialize -> notifications/initialized -> tools/list -> tools/call"
echo "MCP tool: read_canary"
echo "MCP result: DEVOPS_HEALTH_MCP_CANARY_OK"
if [[ -n "${GITHUB_OUTPUT:-}" ]]; then
  echo "protocol_version=$protocol" >> "$GITHUB_OUTPUT"
fi
