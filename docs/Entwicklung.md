# Entwicklung

## Voraussetzungen

- .NET SDK **10.0.x** (`global.json`: mindestens 10.0.100, `rollForward: latestFeature`)
- **macOS (Apple Silicon):** keine Workloads, kein Xcode nötig (siehe [MacOS-Build.md](MacOS-Build.md))
- **Windows 10 1809+:** für die WPF-Anwendung zusätzlich die WebView2 Runtime
- **Linux:** Bibliotheken und Headless-UI-Tests laufen; die Avalonia-App startet unter X11
- Optional: Visual Studio 2026 / Rider / VS Code mit C# Dev Kit, PowerShell 7 für das Lizenzskript

Empfohlen: `export AVALONIA_TELEMETRY_OPTOUT=1` (Avalonia-Build-Telemetrie aus).

## Bauen, Starten, Testen

```bash
# plattformneutral (macOS, Linux, Windows)
dotnet build PictureGeoExif.CrossPlatform.slnf
dotnet test  PictureGeoExif.CrossPlatform.slnf
dotnet run --project src/PictureGeoExif.Avalonia/PictureGeoExif.Avalonia.csproj
dotnet run --project src/PictureGeoExif.Avalonia/PictureGeoExif.Avalonia.csproj -- --self-test

# Windows (inkl. WPF)
dotnet build PictureGeoExif.sln -c Debug          # 0 Warnungen Pflicht (TreatWarningsAsErrors)
dotnet test  PictureGeoExif.sln -c Release
dotnet run --project src/PictureGeoExif.Wpf/PictureGeoExif.Wpf.csproj

# macOS-Bundle
scripts/build-macos-arm64.sh
```

Windows-Release der WPF-App wie in der CI:

```powershell
dotnet publish src/PictureGeoExif.Wpf/PictureGeoExif.Wpf.csproj -c Release -r win-x64 `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:SelfContained=true -o out
```

`Resources/` (Leaflet-Karte der WPF-App), `Templates/` (KI-Vorlage aus `docs/examples`) und `licenses/` liegen danach neben der EXE; im macOS-Bundle unter `Contents/Resources`.

## Tests

| Projekt | Prüft |
| --- | --- |
| `tests/PictureGeoExif.Core.Tests` | Formaterkennung (Signaturen, HEIC-Brands, UTIs, Endungen), `AtomicFile`, `GeoCoordinate`, `ImageSourceItem`, Exportergebnisse, virtuelle Trassen (`RouteTests`), Map-Matching mit Fake-Server (`RoadMatcherTests`) |
| `tests/PictureGeoExif.Metadata.Tests` | GPS-Schreiben (JPEG bytegleiche Scan-Daten, Idempotenz, ungültige Werte, PNG-Alpha, BMP/HEIC-Ablehnung, kollisionsfreie Exporte), Sidecar-Merge, Revisionsschutz, Ziel-Allowlist, Vorschau ohne Metadaten, XMP-GPS-Format, Auswahlgeometrie, Blur/Pixelate/Stamp, Vorlagen-/Antwortvalidierung, `MetadataInspector`, Decoder-Fallback |
| `tests/PictureGeoExif.Application.Tests` | Quellen, Fotos-Pipeline (Fake-Mediathek → Cache → Metadaten), `PrivacyGuard`, `EditorOperations`, `MapProjection`/`RouteLayers`, `AppPaths`, `AiMetadataWorkflow` (Preise, Budget, Bestätigung, Apply/Undo, Chat), Diagnose |
| `tests/PictureGeoExif.Platform.Tests` | PhotoKit-Mapping (Status, Typen, Ressourcen, Alben, Fehler), `MacPhotoLibraryService` mit `FakePhotoKitFacade` (Berechtigung, Paging, Export, Abbruch, iCloud-Fehler, kein Überschreiben), Bundle-Erkennung; auf macOS zusätzlich echtes Photos.framework/ImageIO |
| `tests/PictureGeoExif.Avalonia.Tests` | Headless (Avalonia.Headless + xUnit v3): alle Fenster laden, Dateien laden, GPS-Kopie + Undo, Editor, Diagnose |
| `tests/PictureGeoExif.Wpf.Tests` | nur Windows: WPF-Fenster auf STA-Thread, `ImageItem`-Undo |

Regeln:

- **Tests dürfen keine Benutzerdaten anfassen.** `AppSettings` in Tests immer mit `FilePath = <temp>`, `AppPaths` auf Temp-Ordner setzen. Schlüsselbund-/Credential-Manager-Tests laufen nur mit `PGE_TEST_KEYCHAIN=1` bzw. `PGE_TEST_CREDENTIALS=1`.
- Testbilder synthetisch erzeugen (ImageSharp); keine echten Fotos einchecken.
- Keine echten Netzaufrufe (Map-Matching mit Fake-`HttpMessageHandler`, keine KI-Aufrufe). Der FOSSGIS-Server darf nicht aus CI angesprochen werden.
- PhotoKit nie direkt testen: `IPhotoKitFacade` durch `FakePhotoKitFacade` ersetzen. Die Tests in `MacIntegrationTests` laden nur das Framework und lesen den Status – sie fordern nie eine Berechtigung an.

## Konventionen

- **Schichten:** Core ohne Paketabhängigkeit; UI verwendet nie PhotoKit direkt; Plattformadapter nur in `Bootstrap` wählen. Namespace der Avalonia-App: `PictureGeoExif.Desktop`; die Avalonia-Klasse `Application` dort als `global::Avalonia.Application` schreiben.
- **Encoding:** Alle Textdateien UTF-8.
- **Warnungen:** `TreatWarningsAsErrors` für alle Projekte. Ursachen beheben statt unterdrücken.
- **Dateischreiben:** immer über `AtomicFile`; Originale nie überschreiben; Exporte kollisionsfrei.
- **Langlaufende Arbeit:** `Task.Run` plus `CancellationToken`; ViewModels setzen Zustand erst nach Erfolg; keine `ConfigureAwait(false)` in ViewModels (Fortsetzung auf dem UI-Thread).
- **Objective-C-Interop:** nur in `Platform.Mac/Interop` und `ObjCPhotoKitFacade`; Autorelease-Pool je Aufruf; keine Exceptions über Block-Grenzen; nur Rohwerte über die Fassade, Interpretation in `PhotoKitMapping`.
- **Logging:** keine Dateinamen, Pfade von Bildern, Koordinaten, PhotoKit-Kennungen oder Schlüssel.
- **Sprache:** Oberflächentexte Deutsch, Code-Kommentare Englisch oder Deutsch.
- **Pakete:** Versionen nur in `Directory.Packages.props`.

## CI/CD

| Workflow | Auslöser | Jobs |
| --- | --- | --- |
| `.github/workflows/build.yml` | Pull Request, Push auf `main`, manuell | **Windows:** Build (inkl. WPF), Tests, Paketaudit, Publish WPF + Avalonia win-x64, Artifact · **macOS arm64:** `scripts/build-macos-arm64.sh` (Restore, Test, Build, Publish, `.app`, ZIP), Prüfung (`plutil`, `lipo`, `--self-test`), Artifact `PictureGeoExif-macOS-arm64` · **Linux:** Tests |
| `.github/workflows/release-on-tag.yml` | Tag-Push | WPF single-file `win-x64`/`win-arm64` + macOS-arm64-ZIP als Release-Assets |

Release: Version in `Directory.Build.props` anheben, dann `git tag v0.98 && git push origin v0.98`.

## Abhängigkeiten und Lizenzen

- Paketänderungen in `Directory.Packages.props`, danach:

  ```bash
  dotnet list PictureGeoExif.sln package --vulnerable --include-transitive
  pwsh scripts/Update-ThirdPartyLicenses.ps1
  ```

- [THIRD-PARTY-LICENSES.md](../THIRD-PARTY-LICENSES.md) prüfen und ergänzen.
- **SixLabors-Pakete nicht ohne Lizenzprüfung auf eine neue Hauptversion heben** (Split License).
- `docs/examples/*.json` werden als `Templates/` ausgeliefert.

## Fehlersuche

| Symptom | Ursache / Prüfung |
| --- | --- |
| WPF-Karte leer, „WebView2 Runtime prüfen“ | Runtime fehlt oder Profilordner nicht beschreibbar |
| Avalonia-Karte grau | offline oder Kachelserver 403/429; Statuszeile unter der Karte |
| „Kartenserver: HTTP 403/429“ | Kachelanbieter in `settings.json` wechseln |
| KI: „Start blockiert: Preise fehlen“ | Preise und Prüfdatum im KI-Fenster eintragen |
| KI: „Kein API-Schlüssel“ | im Fenster speichern oder `OPENAI_API_KEY` / `GEMINI_API_KEY` / `AZURE_OPENAI_API_KEY` setzen |
| KI: „Anfrage enthält … und wurde nicht gesendet“ | `PrivacyGuard` hat Pfad, Fotos-Kennung oder exakte Koordinate erkannt |
| macOS-spezifisch | [MacOS-Troubleshooting.md](MacOS-Troubleshooting.md) |
