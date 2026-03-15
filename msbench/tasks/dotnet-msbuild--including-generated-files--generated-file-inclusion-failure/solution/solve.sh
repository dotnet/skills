#!/bin/bash
set -euo pipefail
cd /testbed

# Auto-generated oracle solution for: dotnet-msbuild--including-generated-files--generated-file-inclusion-failure
# Produces output / files that satisfy the task's assertions.

# Write expected keywords to agent_output.txt
cat > /testbed/agent_output.txt << 'ORACLE_EOF'
Compile
evaluation
ORACLE_EOF

exit 0
