#!/usr/bin/env bash
# Starts PictureGeoExif locally on a development Mac.
#
#   scripts/run-macos-local.sh            Debug build via "dotnet run" (fast, console log in the terminal)
#   scripts/run-macos-local.sh --bundle   builds the .app (without tests) and opens it with "open" (Photos permission belongs to the app)
#   scripts/run-macos-local.sh --direct   builds the .app and starts Contents/MacOS/PictureGeoExif directly (log output in the terminal)
#
# Note: when started from Terminal (dotnet run / --direct), macOS attributes the Photos permission request to the
# Terminal app. To test the PhotoKit permission flow of PictureGeoExif itself, use --bundle.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 AVALONIA_TELEMETRY_OPTOUT=1
APP="artifacts/macos-arm64/PictureGeoExif.app"

case "${1:-}" in
  --bundle)
    scripts/build-macos-arm64.sh --skip-tests
    open "$APP"
    ;;
  --direct)
    scripts/build-macos-arm64.sh --skip-tests
    exec "$APP/Contents/MacOS/PictureGeoExif"
    ;;
  ""|--debug)
    exec dotnet run --project src/PictureGeoExif.Avalonia/PictureGeoExif.Avalonia.csproj -c Debug
    ;;
  *)
    sed -n '2,10p' "$0"; exit 2
    ;;
esac
