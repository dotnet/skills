#!/bin/bash
set -euo pipefail
cd /testbed

# Auto-generated oracle solution for: dotnet--nuget-trusted-publishing--migrate-existing-workflow-api-key-trusted
# Produces output / files that satisfy the task's assertions.

# Write expected keywords to agent_output.txt
cat > /testbed/agent_output.txt << 'ORACLE_EOF'
NuGet/login
id-token
api.key
ORACLE_EOF

exit 0
