#!/usr/bin/env bash
set -euo pipefail

dotnet restore src/Gateway/Gateway.csproj
dotnet build src/Gateway/Gateway.csproj -c Release --no-restore
dotnet publish src/Gateway/Gateway.csproj -c Release -r linux-x64 --self-contained
