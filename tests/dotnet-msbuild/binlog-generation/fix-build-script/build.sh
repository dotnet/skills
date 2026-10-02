#!/usr/bin/env bash
# Local repro of our CI build step.
# Runs a normal build and a diagnostics build back to back so we always have
# a binlog to attach to a failed pipeline run.
set -euo pipefail

dotnet build SimpleApp.csproj -c Debug
dotnet build SimpleApp.csproj -c Release /bl
