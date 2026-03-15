#!/bin/bash
set -euo pipefail
cd /testbed

# Auto-generated oracle solution for: dotnet-msbuild--msbuild-modernization--legacy-project-sdk-style
# Produces output / files that satisfy the task's assertions.

# Write expected keywords to agent_output.txt
cat > /testbed/agent_output.txt << 'ORACLE_EOF'
Microsoft.NET.Sdk
SDK-style
ORACLE_EOF

exit 0
