#!/bin/bash
set -euo pipefail
cd /testbed

# Auto-generated oracle solution for: dotnet-msbuild--check-bin-obj-clash--bin-obj-output-path-clashes
# Produces output / files that satisfy the task's assertions.

# Write expected keywords to agent_output.txt
cat > /testbed/agent_output.txt << 'ORACLE_EOF'
AppendTargetFrameworkToOutputPath
OutputPath
IntermediateOutputPath
ORACLE_EOF

exit 0
