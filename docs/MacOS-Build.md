# macOS-Build (Apple Silicon, osx-arm64)

## Versionen

| Komponente | Version | Quelle |
| --- | --- | --- |
| .NET SDK | 10.0.x (mindestens 10.0.100; `global.json`: `rollForward: latestFeature`) | <https://dotnet.microsoft.com/download/dotnet/10.0> |
| Ziel-Framework | `net10.0` (alle plattformneutralen Projekte), `net10.0-windows10.0.17763.0` (nur WPF) | `Directory.Build.props` |
| Runtime Identifier | **`osx-arm64`** (gilt für alle Apple-Silicon-Macs M1 … M4; es gibt keinen M4-spezifischen RID) | Build-Skript |
| Avalonia | 12.1.3 (DataGrid 12.1.2) | `Directory.Packages.props` |
| CommunityToolkit.Mvvm | 8.4.2 | `Directory.Packages.props` |
| Microsoft.Extensions.* | 10.0.12 | `Directory.Packages.props` |
| MetadataExtractor / XmpCore | 2.9.3 / 6.1.10.1 | `Directory.Packages.props` |
| SixLabors ImageSharp / Drawing / Fonts | 3.1.12 / 2.1.7 / 2.1.3 (bewusst keine neue Hauptversion, Lizenz) | `Directory.Packages.props` |
| macOS (Ziel) | macOS 27 auf Apple Silicon; `LSMinimumSystemVersion` 14.0 (Untergrenze von .NET 10) | `Info.plist` |
| Xcode | **nicht erforderlich**. `codesign`, `plutil`, `ditto`, `hdiutil` sind Teil von macOS. Die Command Line Tools (`xcode-select --install`) genügen für `git`. | – |
| .NET-Workloads | **keine** (kein `macos`-Workload, siehe [MacOS-PhotoKit.md](MacOS-PhotoKit.md#entscheidung-objective-c-laufzeit-statt-microsoftmacos-bindings)) | – |

Versionen der verwendeten Pakete sind zentral in `Directory.Packages.props` gepflegt (Central Package Management).

## Voraussetzungen

```bash
# .NET 10 SDK (arm64-Installer von Microsoft oder Homebrew)
brew install --cask dotnet-sdk
dotnet --info          # muss "RID: osx-arm64" und SDK 10.0.x zeigen
export AVALONIA_TELEMETRY_OPTOUT=1   # optional: Avalonia-Build-Telemetrie aus
```

## Bauen, Testen, Starten (Entwicklung)

```bash
git clone https://github.com/JoergBrors/PictureGeoExif.git
cd PictureGeoExif
dotnet build PictureGeoExif.CrossPlatform.slnf           # alles außer WPF
dotnet test  PictureGeoExif.CrossPlatform.slnf           # Unit-, Plattform- und Headless-UI-Tests
scripts/run-macos-local.sh                               # Debug-Start per dotnet run
```

`PictureGeoExif.sln` enthält zusätzlich die WPF-Anwendung; sie lässt sich dank `EnableWindowsTargeting` auch auf dem Mac kompilieren, ihre Tests laufen aber nur unter Windows.

## Release-Build und App-Bundle

```bash
scripts/build-macos-arm64.sh                 # vollständig inkl. Tests
scripts/build-macos-arm64.sh --skip-tests    # schneller
scripts/build-macos-arm64.sh --dmg           # zusätzlich PictureGeoExif-macOS-arm64.dmg
scripts/build-macos-arm64.sh --adhoc-sign    # optional: lokale Ad-hoc-Signatur des ganzen Bundles
```

Das Skript führt aus:

1. alte Artefakte löschen (`artifacts/macos-arm64`, `dotnet clean`)
2. `dotnet restore` (Solution-Filter und RID)
3. `dotnet test PictureGeoExif.CrossPlatform.slnf -c Release`
4. Release-Build für `osx-arm64`
5. Self-contained Publish:

   ```bash
   dotnet publish src/PictureGeoExif.Avalonia/PictureGeoExif.Avalonia.csproj \
     -c Release -r osx-arm64 --self-contained true \
     -p:UseAppHost=true -p:PublishSingleFile=false -p:PublishTrimmed=false \
     -p:DebugType=None -p:DebugSymbols=false -o artifacts/macos-arm64/publish
   ```

   - **Kein Single-File** und **kein Trimming**: Avalonia lädt `libAvaloniaNative.dylib`, `libSkiaSharp.dylib` und `libHarfBuzzSharp.dylib` aus dem Programmordner und nutzt Reflection für XAML; beides kann den Start unter macOS brechen. Das Skript prüft, dass die drei Bibliotheken im Publish liegen.
   - Das SDK signiert den `apphost` beim Publish ad hoc (auch beim Cross-Publish unter Linux/Windows). Das Skript prüft auf macOS mit `codesign --verify`, dass die Signatur gültig ist.
6. `.app`-Bundle erzeugen, Publish-Ausgabe nach `Contents/MacOS`
7. `Info.plist` aus `src/PictureGeoExif.Avalonia/macOS/Info.plist` einsetzen (`__VERSION__` ← `<Version>` aus `Directory.Build.props`, `__BUILD__` ← Build-Nummer), `PkgInfo`, `plutil -lint`
8. Ressourcen (`Templates/`, `licenses/`, `LICENSE`, `THIRD-PARTY-LICENSES.md`, optional `PictureGeoExif.icns`) nach `Contents/Resources`
9. Rechte setzen (`chmod`, ausführbares Programm 755), erweiterte Attribute entfernen
10. Ergebnis: `artifacts/macos-arm64/PictureGeoExif.app` und `PictureGeoExif-macOS-arm64.zip` (mit `ditto`, erhält Symlinks und Rechte)

## Bundle-Struktur

```text
PictureGeoExif.app/
└── Contents/
    ├── Info.plist                    Bundle-ID net.brors.picturegeoexif, NSPhotoLibraryUsageDescription …
    ├── PkgInfo
    ├── MacOS/
    │   ├── PictureGeoExif            apphost (Mach-O arm64, vom SDK ad hoc signiert)
    │   ├── PictureGeoExif.dll        + alle verwalteten Assemblies
    │   ├── libAvaloniaNative.dylib   Avalonia (Cocoa-Fenster, Menüleiste)
    │   ├── libSkiaSharp.dylib, libHarfBuzzSharp.dylib
    │   └── libcoreclr.dylib …        .NET-Runtime (self-contained)
    └── Resources/
        ├── Templates/                KI-Vorlage und Antwortschema
        ├── licenses/, LICENSE, THIRD-PARTY-LICENSES.md
        └── PictureGeoExif.icns       (optional, sobald ein Icon existiert)
```

Die App findet Ressourcen über `ResourceLocator`: zuerst `Contents/Resources`, dann der Programmordner.

**Icon ergänzen:** `PictureGeoExif.icns` nach `src/PictureGeoExif.Avalonia/macOS/` legen (z. B. mit `iconutil -c icns PictureGeoExif.iconset`). `CFBundleIconFile` ist bereits gesetzt; das Skript kopiert die Datei automatisch.

## Selbsttest ohne Oberfläche

```bash
artifacts/macos-arm64/PictureGeoExif.app/Contents/MacOS/PictureGeoExif --self-test
```

Gibt die Diagnosedaten aus, lädt Photos.framework (ohne Berechtigungsanfrage) und rendert ein Testbild mit ImageSharp und ImageIO. Exit-Code 0 = PASS. Die CI führt ihn auf dem Apple-Silicon-Runner aus.

## CI

`.github/workflows/build.yml`, Job **macOS arm64** (`macos-15`, Apple Silicon): Restore, Tests (inkl. echter PhotoKit/ImageIO-Interop-Tests), Release-Build, Publish `osx-arm64`, `.app`, Prüfung (`plutil`, `lipo`, `--self-test`) und Upload als Artifact **PictureGeoExif-macOS-arm64**. Bei Tags hängt `release-on-tag.yml` das ZIP an das GitHub-Release. Keine Signierung, keine Notarisierung.
