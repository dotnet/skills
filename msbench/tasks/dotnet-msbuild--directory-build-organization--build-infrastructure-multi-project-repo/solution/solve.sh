#!/bin/bash
set -euo pipefail
cd /testbed

# Auto-generated oracle solution for: dotnet-msbuild--directory-build-organization--build-infrastructure-multi-project-repo
# Produces output / files that satisfy the task's assertions.

# Write expected keywords to agent_output.txt
cat > /testbed/agent_output.txt << 'ORACLE_EOF'
Directory.Build.props
Directory.Build.targets
ORACLE_EOF

exit 0
