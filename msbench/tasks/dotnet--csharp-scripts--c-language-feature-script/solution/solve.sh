#!/bin/bash
set -euo pipefail
cd /testbed

# Auto-generated oracle solution for: dotnet--csharp-scripts--c-language-feature-script
# Produces output / files that satisfy the task's assertions.

# Write expected keywords to agent_output.txt
cat > /testbed/agent_output.txt << 'ORACLE_EOF'
nint
stackalloc
ORACLE_EOF

exit 0
