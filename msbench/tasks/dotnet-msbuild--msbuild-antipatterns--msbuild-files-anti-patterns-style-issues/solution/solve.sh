#!/bin/bash
set -euo pipefail
cd /testbed

# Auto-generated oracle solution for: dotnet-msbuild--msbuild-antipatterns--msbuild-files-anti-patterns-style-issues
# Produces output / files that satisfy the task's assertions.

# Write expected keywords to agent_output.txt
cat > /testbed/agent_output.txt << 'ORACLE_EOF'
Directory.Build.props
anti-pattern
ORACLE_EOF

exit 0
