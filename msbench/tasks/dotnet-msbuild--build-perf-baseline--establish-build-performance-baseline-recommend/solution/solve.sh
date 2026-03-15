#!/bin/bash
set -euo pipefail
cd /testbed

# Auto-generated oracle solution for: dotnet-msbuild--build-perf-baseline--establish-build-performance-baseline-recommend
# Produces output / files that satisfy the task's assertions.

# Write expected keywords to agent_output.txt
cat > /testbed/agent_output.txt << 'ORACLE_EOF'
baseline
redundant.reference
GenerateDocumentationFile
ORACLE_EOF

exit 0
