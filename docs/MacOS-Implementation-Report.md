# Implementierungsbericht macOS / Avalonia / PhotoKit – Phase 1

Stand: 27.09.2026 · Version 0.98.0 · Branch `claude/elegant-shannon-rfkwle`

> **Wichtig:** Dieser Stand wurde in einer Linux-x64-Build-Umgebung (Container) entwickelt, gebaut und getestet. Ein Apple-Silicon-Mac mit macOS 27 stand dort nicht zur Verfügung. Alles, was nur auf echter macOS-Hardware geprüft werden kann (Fensterstart, Systemdialog für Fotos, Zugriff auf eine echte Mediathek, iCloud), ist unten als **PARTIAL** bzw. **offen** gekennzeichnet – nicht als bestanden. Der CI-Job „macOS arm64“ (`macos-15`, Apple Silicon) führt die macOS-Interop-Tests und den `--self-test` des fertigen Bundles aus und schließt damit einen Teil dieser Lücke, sobald er läuft.

## Status

### Umgesetzt

- Analyse des Ausgangsprojekts ([Avalonia-Migration-Analyse.md](Avalonia-Migration-Analyse.md)).
- Neue Solution-Architektur: `Core` (ohne Paketabhängigkeit), `Metadata`, `Application`, `Platform.Mac`, `Platform.Windows`, `Avalonia`; WPF als `src/PictureGeoExif.Wpf` weiter baubar und mit den Bibliotheken verbunden.
- Plattformabstraktion: `IPhotoLibraryService`, `IImageSourceProvider` (+ paginiert), `IImageDecoder`, `ICredentialStore`, `IPlatformInfo`, `IMapViewService`.
- PhotoKit-Adapter (`MacPhotoLibraryService` + `ObjCPhotoKitFacade`): Status, Anfrage (nur auf Benutzeraktion, nur mit Usage-Description), Alben/intelligente Alben/Favoriten/Mediathek, paginierte Assets (nur Bilder), Thumbnails ohne iCloud-Download, Asset-Details und Ressourcen, Original-Export per `PHAssetResourceManager` mit iCloud, Fortschritt, Abbruch, atomarem Schreiben, ohne Überschreiben; Fehlerabbildung (iCloud, nicht gefunden, keine Ressource, verweigert).
- Pipeline Fotos-Asset → Original → lokaler Cache → derselbe `MetadataInspector` (EXIF/GPS/XMP/IPTC/Raw) wie für Dateien.
- Avalonia-Oberfläche (MVVM, DI): Quellen „Dateien“/„Apple Fotos“, Bildliste mit Thumbnails, Vorschau, Metadaten-Tabs, Karte, GPS-Funktionen (Karte, Referenzbild, einzeln, alle, speichern, Rückgängig), Trassen inkl. „An Wege anlegen“, Bildeditor, KI-Metadaten-Fenster, Lizenzen, **Help → Diagnostics** mit „Diagnoseinformationen kopieren“, native macOS-Menüleiste.
- Dateiauswahl über Avalonia `StorageProvider` (Dateien, mehrere Dateien, Ordner, Speichern unter); Drag & Drop mit Avalonia `DragDrop`.
- HEIC/HEIF/AVIF/DNG/RAW: Erkennung per Signatur, Metadaten über MetadataExtractor, Vorschau unter macOS über ImageIO, Export des Originals; GPS-Schreiben für diese Formate bewusst abgelehnt.
- Karte ohne WebView2: eigenes OSM-Kachel-Steuerelement hinter `IMapViewService`.
- Editor-Logik aus der UI in `EditorOperations` (Application) verschoben; Avalonia nur Rendering/Eingabe.
- KI-Ablauf aus dem WPF-Code-behind in `AiMetadataWorkflow` extrahiert; `PrivacyGuard` prüft jede ausgehende Anfrage.
- macOS-Schlüsselbund für API-Schlüssel.
- Logging (Konsole + Datei, Startzeile mit allen geforderten Angaben), `--self-test`.
- `Info.plist` mit `NSPhotoLibraryUsageDescription` (Deutsch), `scripts/build-macos-arm64.sh`, `scripts/run-macos-local.sh`, ZIP, optional DMG.
- GitHub Actions: Windows (Build, Test, Paketaudit, Publish), macOS arm64 (Restore, Build, Test, Publish, `.app`, Prüfung, Artifact), Linux (Tests); Release-Workflow hängt das macOS-ZIP an Releases.
- Dokumentation: Architektur, PhotoKit, Build, Deployment, Troubleshooting, dieser Bericht, README.

### Teilweise umgesetzt

- **WPF und Avalonia parallel:** Die WPF-Anwendung behält ihre eigene (ältere) KI- und Editor-Ablauflogik im Code-behind; nur Avalonia nutzt `AiMetadataWorkflow`/`EditorOperations`. WPF gilt als Legacy.
- **Karte:** Marker, Auswahl, Koordinate, Trassen, Wegverlauf, Raster, Zoom/Pan, Attribution sind umgesetzt. Hover-Infos pro Marker (WPF/Leaflet) fehlen; Marker-Infos erscheinen bei Auswahl.
- **Editor:** Stempelfarbe über eine feste Farbpalette statt freiem Farbwähler; kein „EXIF anzeigen“-Knopf im Editor (Metadaten stehen im Hauptfenster).
- **Drag & Drop aus dem Finder:** implementiert (Dateien und Ordner), in Headless-Tests nicht automatisiert prüfbar.

### Offen

- Test auf echter Hardware (Apple M4, macOS 27): Start, Menüleiste, Fotos-Berechtigungsdialog, echte Mediathek, iCloud-Originale, Limited Library.
- Developer-ID-Signierung, Notarisierung, Stapling (bewusst nicht Teil von Phase 1).
- App-Icon (`PictureGeoExif.icns`) – Platzhalter in Info.plist und Skript vorbereitet.
- Schreiben in Apple Fotos (neue Assets/Versionen) – Phase 1 ist read-only.
- Videos, Live-Photo-Videoanteile (Phase 1 konzentriert sich auf Bilder).
- WebView-basierte Kartenvariante für Avalonia (Leaflet-Ressourcen liegen im WPF-Projekt bereit).

## Build

```bash
# macOS (Apple Silicon)
dotnet test PictureGeoExif.CrossPlatform.slnf -c Release
scripts/build-macos-arm64.sh                       # → artifacts/macos-arm64/PictureGeoExif.app, PictureGeoExif-macOS-arm64.zip
artifacts/macos-arm64/PictureGeoExif.app/Contents/MacOS/PictureGeoExif --self-test
open artifacts/macos-arm64/PictureGeoExif.app

# Kern des Publish-Schritts
dotnet publish src/PictureGeoExif.Avalonia/PictureGeoExif.Avalonia.csproj -c Release -r osx-arm64 --self-contained true \
  -p:UseAppHost=true -p:PublishSingleFile=false -p:PublishTrimmed=false -p:DebugType=None -p:DebugSymbols=false

# Windows
dotnet build PictureGeoExif.sln -c Debug
dotnet test PictureGeoExif.sln -c Release
```

## Testsystem

| | Entwicklungs-/Testumgebung dieses Stands | Zielsystem |
| --- | --- | --- |
| Architecture | x64 (Linux-Container, Ubuntu 24.04) | **ARM64** |
| Runtime Identifier | `linux-x64` (Tests), Cross-Publish **`osx-arm64`** | **`osx-arm64`** |
| OS | Linux | **macOS 27** |
| CPU | x64 | **Apple Silicon (M1 … M4)** |
| .NET SDK | 10.0.112 (Runtime 10.0.12) | 10.0.x |
| Avalonia | 12.1.3 | 12.1.3 |

Ergebnisse in dieser Umgebung:

| Prüfung | Ergebnis |
| --- | --- |
| `dotnet build PictureGeoExif.sln -c Release` (inkl. WPF dank `EnableWindowsTargeting`) | PASS, 0 Warnungen |
| `dotnet test PictureGeoExif.CrossPlatform.slnf` | PASS – 195 Tests (Core 63, Metadata 51, Application 33, Platform 41, Avalonia headless 7) |
| Bisherige Tests (Metadaten, Logik, Trassen, Map-Matching) | PASS (übernommen; WPF-Fenstertests nur unter Windows) |
| `scripts/build-macos-arm64.sh` (Cross-Build unter Linux) | PASS: Bundle, Info.plist, Resources, ZIP; apphost `Mach-O 64-bit arm64` mit Ad-hoc-Signatur (LC_CODE_SIGNATURE) vom SDK; `libAvaloniaNative`, `libSkiaSharp`, `libHarfBuzzSharp` im Bundle |
| `PictureGeoExif --self-test` (Linux) | PASS |
| macOS-Interop-Tests, `--self-test` des Bundles auf Apple Silicon | in CI (`macos-15`) eingerichtet, Ergebnis steht aus |
| Start der Oberfläche auf Apple M4 / macOS 27 | offen (keine Hardware) |

## PhotoKit

| Funktion | Status | Nachweis / Einschränkung |
| --- | --- | --- |
| Permission Request | **PARTIAL** | Status-Mapping (0–4, unbekannt → Denied), „nur bei NotDetermined“, „nie ohne NSPhotoLibraryUsageDescription“ per Unit-Test mit Fake-Fassade; Systemdialog nicht auf Hardware geprüft |
| Library Query | **PARTIAL** | Paging, Bildfilter, Favoriten-Prädikat per Fake getestet; `fetchAssetsWithOptions:`-Aufruf wird im CI-Job auf macOS nur bis zum Status geprüft (keine Mediathek-Freigabe im CI) |
| Album Query | **PARTIAL** | Albumliste (Mediathek, Favoriten, Benutzer-, geteilte, intelligente Alben; ohne Ausgeblendet/Gelöscht) per Fake getestet; nicht auf echter Mediathek geprüft |
| Asset Query | **PARTIAL** | Mapping (Typ, Subtypen inkl. RAW, Datum, Maße, Favorit, Standort, Originalname/UTI) getestet; nicht auf echter Mediathek geprüft |
| Thumbnail | **PARTIAL** | PhotoKit-Weg (`PHImageManager`, synchron im Hintergrund, ohne Netz) implementiert; ImageIO-Rendering wird im CI auf macOS getestet |
| Original Resource | **PARTIAL** | Auswahl Original vor Bearbeitung, Export atomar, kein Überschreiben, Abbruch, iCloud-Fehler per Fake getestet; `PHAssetResourceManager` nicht auf Hardware geprüft |
| EXIF extraction | **PARTIAL** | Pipeline Original → Cache → `MetadataInspector` mit Fake-Mediathek getestet (JPEG); HEIC-Originale aus echter Mediathek nicht geprüft |

## Bekannte Einschränkungen

1. **Keine Hardware-Verifikation** in dieser Umgebung (siehe oben). Vor einer Freigabe auf einem Apple-Silicon-Mac mit macOS 27 prüfen: Start per `open`, Menüleiste, Drag & Drop, Fotos-Zugriff anfordern/ablehnen/erlauben, Limited Library, Album mit > 1000 Fotos, iCloud-Original (offline/online), Original-Export HEIC/DNG, `--self-test`.
2. **PhotoKit über die Objective-C-Laufzeit** statt Microsoft.macOS-Bindings (Begründung in [MacOS-PhotoKit.md](MacOS-PhotoKit.md)). Tippfehler in Selektoren würden erst zur Laufzeit auffallen; die Interop-Schicht ist deshalb klein gehalten und im CI auf macOS teilweise abgedeckt.
3. **Unsigniert/nicht notarisiert:** Gatekeeper-Warnung bei heruntergeladenen Builds; Fotos- und Schlüsselbund-Freigaben können nach jedem Neubau erneut abgefragt werden.
4. **Start aus dem Terminal:** Fotos-Freigabe gehört dann zum Terminal; per `dotnet run` ist keine Anfrage möglich (bewusst abgefangen).
5. **HEIC/RAW:** Vorschau nur unter macOS (ImageIO); unter Windows/Linux nur Metadaten. GPS-Schreiben nur JPEG/PNG/TIFF; für HEIC/RAW XMP-Sidecar oder Export als JPEG.
6. **Editor** öffnet nur ImageSharp-Formate (JPEG, PNG, TIFF, BMP, GIF, WebP); HEIC/RAW vorher exportieren/umwandeln.
7. **KI mit Fotos-Assets** nur nach „In Dateien übernehmen“ (dann gleiche Datenschutzprüfung wie Dateien); Sidecars liegen neben der exportierten Datei, nicht in Apple Fotos.
8. **Karte:** eigene Kachelkarte ohne Vektor-/Retina-Kacheln; Kachel-Cache 7 Tage; bei OSM-Sperre (403/429) Pause und Hinweis.
9. **Minimum macOS 14** laut Info.plist (Untergrenze von .NET 10); getestet werden soll macOS 27. Intel-Macs werden nicht unterstützt.
10. **Bestehende Einschränkungen** aus [Architektur.md](Architektur.md#bekannte-grenzen) gelten weiter (u. a. XMP nur als Sidecar, `LearningOptOutIn` nicht decodiert, MakerNote-Offsets).
