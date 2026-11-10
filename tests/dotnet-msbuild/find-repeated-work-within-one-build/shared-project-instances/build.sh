#!/usr/bin/env bash
set -euo pipefail

for p in Web Api Worker; do dotnet restore "$p/$p.csproj"; done
dotnet msbuild Build.proj -m
