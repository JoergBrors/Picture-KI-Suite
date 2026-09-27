#!/usr/bin/env bash
# Builds PictureGeoExif.app for Apple Silicon (osx-arm64) – unsigned development build (no Developer ID, no notarization).
#
#   scripts/build-macos-arm64.sh [--skip-tests] [--no-adhoc-sign] [--dmg] [--build-number N]
#
# Result: artifacts/macos-arm64/PictureGeoExif.app and artifacts/macos-arm64/PictureGeoExif-macOS-arm64.zip
# Details: docs/MacOS-Build.md, docs/MacOS-Deployment.md
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

APP_NAME="PictureGeoExif"
RID="osx-arm64"
CONFIGURATION="Release"
PROJECT="src/PictureGeoExif.Avalonia/PictureGeoExif.Avalonia.csproj"
SOLUTION_FILTER="PictureGeoExif.CrossPlatform.slnf"
OUT="artifacts/macos-arm64"
PUBLISH="$OUT/publish"
BUNDLE="$OUT/$APP_NAME.app"
ZIP="$OUT/$APP_NAME-macOS-arm64.zip"
RUN_TESTS=1
ADHOC_SIGN=1
MAKE_DMG=0
BUILD_NUMBER="${GITHUB_RUN_NUMBER:-$(date +%Y%m%d%H%M)}"

while [[ $# -gt 0 ]]; do
  case "$1" in
    --skip-tests) RUN_TESTS=0 ;;
    --no-adhoc-sign) ADHOC_SIGN=0 ;;
    --adhoc-sign) ADHOC_SIGN=1 ;;
    --dmg) MAKE_DMG=1 ;;
    --build-number) BUILD_NUMBER="$2"; shift ;;
    -h|--help) sed -n '2,8p' "$0"; exit 0 ;;
    *) echo "Unbekannte Option: $1" >&2; exit 2 ;;
  esac
  shift
done

export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 AVALONIA_TELEMETRY_OPTOUT=1

step() { printf '\n==> %s\n' "$*"; }

VERSION="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' Directory.Build.props | head -n1)"
[[ -n "$VERSION" ]] || { echo "Version in Directory.Build.props nicht gefunden." >&2; exit 1; }
echo "PictureGeoExif $VERSION (Build $BUILD_NUMBER) für $RID"
dotnet --version

step "1. Alte Build-Artefakte löschen"
rm -rf "$OUT"
dotnet clean "$PROJECT" -c "$CONFIGURATION" -r "$RID" --nologo -v quiet >/dev/null || true

step "2. Restore"
dotnet restore "$SOLUTION_FILTER"
dotnet restore "$PROJECT" -r "$RID"

if [[ $RUN_TESTS -eq 1 ]]; then
  step "3. Tests"
  dotnet test "$SOLUTION_FILTER" -c "$CONFIGURATION" --no-restore
else
  step "3. Tests übersprungen (--skip-tests)"
fi

step "4. Release-Build"
dotnet build "$PROJECT" -c "$CONFIGURATION" -r "$RID" --no-restore

step "5. Self-contained Publish für $RID"
# No single-file and no trimming: Avalonia loads its native libraries (libAvaloniaNative, libSkiaSharp, libHarfBuzzSharp)
# from the executable folder and uses reflection for XAML; both options can break startup on macOS.
dotnet publish "$PROJECT" -c "$CONFIGURATION" -r "$RID" --self-contained true --no-restore \
  -p:UseAppHost=true -p:PublishSingleFile=false -p:PublishTrimmed=false -p:DebugType=None -p:DebugSymbols=false \
  -o "$PUBLISH"

for native in libAvaloniaNative.dylib libSkiaSharp.dylib libHarfBuzzSharp.dylib; do
  [[ -f "$PUBLISH/$native" ]] || { echo "Native Bibliothek fehlt im Publish: $native" >&2; exit 1; }
done

if command -v codesign >/dev/null && ! codesign --verify "$PUBLISH/$APP_NAME" 2>/dev/null; then
  echo "FEHLER: Der apphost trägt keine gültige Signatur und würde auf Apple Silicon nicht starten. Mit --adhoc-sign erneut bauen." >&2
  [[ $ADHOC_SIGN -eq 1 ]] || exit 1
fi

step "6. App-Bundle erzeugen"
mkdir -p "$BUNDLE/Contents/MacOS" "$BUNDLE/Contents/Resources"
cp -R "$PUBLISH/." "$BUNDLE/Contents/MacOS/"

step "7. Info.plist einfügen"
sed -e "s/__VERSION__/$VERSION/g" -e "s/__BUILD__/$BUILD_NUMBER/g" src/PictureGeoExif.Avalonia/macOS/Info.plist > "$BUNDLE/Contents/Info.plist"
printf 'APPL????' > "$BUNDLE/Contents/PkgInfo"
if command -v plutil >/dev/null; then plutil -lint "$BUNDLE/Contents/Info.plist"; fi

step "8. Resources kopieren"
# Data files belong in Contents/Resources; the app finds them there (ResourceLocator) and falls back to Contents/MacOS.
for item in Templates licenses LICENSE THIRD-PARTY-LICENSES.md; do
  if [[ -e "$BUNDLE/Contents/MacOS/$item" ]]; then mv "$BUNDLE/Contents/MacOS/$item" "$BUNDLE/Contents/Resources/"; fi
done
if [[ -f src/PictureGeoExif.Avalonia/macOS/$APP_NAME.icns ]]; then cp "src/PictureGeoExif.Avalonia/macOS/$APP_NAME.icns" "$BUNDLE/Contents/Resources/"; fi

step "9. Rechte setzen"
chmod -R u+rwX,go+rX "$BUNDLE"
chmod 755 "$BUNDLE/Contents/MacOS/$APP_NAME"
if command -v xattr >/dev/null; then xattr -cr "$BUNDLE" || true; fi

# Local ad-hoc signature of the whole bundle (no Developer ID, no notarization). Technically required: the SDK signs the
# apphost as a standalone binary; inside the bundle `codesign --verify` then fails with "code has no resources but
# signature indicates they must be present" (seen on the macOS CI runner), and Gatekeeper reports a downloaded copy as
# "damaged" instead of offering "Open Anyway". Sealing the bundle ad hoc fixes both. See docs/MacOS-Deployment.md.
if [[ $ADHOC_SIGN -eq 1 ]]; then
  if command -v codesign >/dev/null; then
    step "Lokale Ad-hoc-Signatur des Bundles (keine Developer ID, keine Notarisierung)"
    # A signing problem must not prevent the package; CI verifies the signature strictly after the upload.
    if codesign --force --deep --sign - --timestamp=none "$BUNDLE" && codesign --verify --deep --strict --verbose=2 "$BUNDLE"; then
      echo "Ad-hoc-Signatur OK."
    else
      echo "WARNUNG: Ad-hoc-Signatur fehlgeschlagen – Bundle bleibt unsigniert (Start ggf. nur nach xattr -dr com.apple.quarantine)." >&2
    fi
  else
    echo "WARNUNG: codesign nicht gefunden (kein macOS) – Ad-hoc-Signatur übersprungen." >&2
  fi
fi

step "10. Ergebnis unter $OUT"
if command -v ditto >/dev/null; then
  ditto -c -k --sequesterRsrc --keepParent "$BUNDLE" "$ZIP"
else
  (cd "$OUT" && zip -qry "$(basename "$ZIP")" "$APP_NAME.app")
fi
if [[ $MAKE_DMG -eq 1 ]]; then
  if command -v hdiutil >/dev/null; then
    hdiutil create -volname "$APP_NAME" -srcfolder "$BUNDLE" -ov -format UDZO "$OUT/$APP_NAME-macOS-arm64.dmg"
  else
    echo "hdiutil nicht verfügbar – DMG übersprungen." >&2
  fi
fi
rm -rf "$PUBLISH"

echo
echo "Fertig:"
echo "  $BUNDLE"
echo "  $ZIP"
echo "Start:  open \"$BUNDLE\"   oder direkt:  \"$BUNDLE/Contents/MacOS/$APP_NAME\""
