#!/bin/bash
output=$(dotnet build 2>&1)
if echo "$output" | grep -q "warning CS"; then
  echo "Build has warnings"
fi
dotnet workload list | grep "maui" && echo "MAUI workload installed"
