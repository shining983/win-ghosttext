#!/usr/bin/env bash
# Cross-host helper: Core tests always; full App publish requires Windows.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"

echo "==> Core tests"
dotnet test tests/WinGhostText.Core.Tests/WinGhostText.Core.Tests.csproj -c Release

if [[ "$(uname -s)" == MINGW* || "$(uname -s)" == CYGWIN* || "$(uname -s)" == MSYS* || -n "${WINDIR:-}" ]]; then
  echo "==> Windows publish"
  powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$ROOT/scripts/publish-zip.ps1"
else
  echo "==> Skipping win-x64 publish (not Windows). Use scripts/publish-zip.ps1 on a Windows SDK host."
  echo "    App project target: net8.0-windows (WPF)."
fi
