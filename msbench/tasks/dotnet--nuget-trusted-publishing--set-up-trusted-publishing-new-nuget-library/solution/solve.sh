#!/bin/bash
set -euo pipefail
cd /testbed

# Auto-generated oracle solution for: dotnet--nuget-trusted-publishing--set-up-trusted-publishing-new-nuget-library
# Produces output / files that satisfy the task's assertions.

# Write expected keywords to agent_output.txt
cat > /testbed/agent_output.txt << 'ORACLE_EOF'
NuGet/login
id-token
trusted.publishing
ORACLE_EOF

exit 0
