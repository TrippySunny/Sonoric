#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")" && pwd)"
export DOTNET_ROOT="${DOTNET_ROOT:-$ROOT/.tools/dotnet}"
export PATH="$DOTNET_ROOT:$PATH"
export DOTNET_CLI_HOME="${DOTNET_CLI_HOME:-$ROOT/.tools/cli-home}"
export NUGET_PACKAGES="${NUGET_PACKAGES:-$ROOT/.tools/nuget}"
export DOTNET_CLI_TELEMETRY_OPTOUT=1

arch="$(uname -m)"
case "$arch" in
  x86_64) RID="linux-x64" ;;
  aarch64|arm64) RID="linux-arm64" ;;
  *)
    echo "Unsupported architecture: $arch" >&2
    exit 1
    ;;
esac

OUT="${1:-$ROOT/dist/$RID}"
mkdir -p "$OUT"
dotnet publish "$ROOT/src/Sonoric/Sonoric.csproj" \
  -c Release \
  -r "$RID" \
  --self-contained true \
  -p:PublishSingleFile=false \
  -p:DebugType=None \
  -p:DebugSymbols=false \
  -o "$OUT"
chmod +x "$OUT/Sonoric"
TAR="$ROOT/dist/Sonoric-$RID.tar.gz"
mkdir -p "$ROOT/dist"
tar -czf "$TAR" -C "$OUT" .
echo "$OUT"
echo "$TAR"
