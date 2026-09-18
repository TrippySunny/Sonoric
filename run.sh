#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")" && pwd)"
export DOTNET_ROOT="${DOTNET_ROOT:-$ROOT/.tools/dotnet}"
export PATH="$DOTNET_ROOT:$PATH"
export DOTNET_CLI_HOME="${DOTNET_CLI_HOME:-$ROOT/.tools/cli-home}"
export NUGET_PACKAGES="${NUGET_PACKAGES:-$ROOT/.tools/nuget}"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
exec dotnet run --project "$ROOT/src/Sonorics/Sonorics.csproj" "$@"
