#!/usr/bin/env bash
set -euo pipefail

dotnet build Contoso.slnx -c Release -m
