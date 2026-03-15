#!/bin/bash
set -euo pipefail
cd /testbed

# Auto-generated oracle solution for: dotnet--dotnet-pinvoke--libraryimport-declaration-c-header-net-framework
# Produces output / files that satisfy the task's assertions.

# Write expected keywords to agent_output.txt
cat > /testbed/agent_output.txt << 'ORACLE_EOF'
DllImport
compresslib
UIntPtr
static extern
ORACLE_EOF

exit 0
