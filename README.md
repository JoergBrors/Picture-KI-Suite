# PictureGeoExif

Desktop-Anwendung zum **Georeferenzieren, Analysieren, Bearbeiten und Verschlagworten von Fotos** – für **Windows** und **macOS auf Apple Silicon**. GPS-Koordinaten kommen aus einer OpenStreetMap-Karte oder einem Referenzbild und werden verlustfrei in die EXIF-Daten einer Kopie geschrieben. Unter macOS lassen sich Fotos direkt aus **Apple Fotos** (PhotoKit) lesen, analysieren und als Original exportieren. Ein Editor mit pixelgenauen Werkzeugen und ein optionales KI-Modul für Metadaten ergänzen die Anwendung. **Originaldateien und die Fotomediathek werden nie verändert.**

Lizenz: [MIT](LICENSE) · Drittanbieter: [THIRD-PARTY-LICENSES.md](THIRD-PARTY-LICENSES.md)

## Funktionen

- **Bilder laden** per Dialog, Ordner oder Drag & Drop aus Finder/Explorer: JPG/JPEG, PNG, TIFF, BMP, GIF, WebP, **HEIC/HEIF**, AVIF, **DNG und RAW** (CR2/CR3, NEF, ARW, ORF, RW2, RAF …)
- **Apple Fotos** (macOS): Berechtigung, Mediathek, Favoriten, Alben, intelligente Alben, Thumbnails, Aufnahmedatum, Dateityp, Pixelmaße, Favoritenstatus, Standort, Asset-Kennung; **Original lesen und exportieren** (inkl. iCloud-Download mit Fortschritt)
- **Metadaten** in Registerkarten: Allgemein, EXIF, GPS, XMP (eingebettet und Sidecar), IPTC, Raw – für Dateien und Fotos-Assets mit derselben Analyse
- **Karte:** OpenStreetMap-Kacheln, Marker aller Bilder, Klick setzt Koordinaten, Meter-Raster, virtuelle **Trassen** mit Abzweigen und „An Wege anlegen“ (Valhalla)
- **GPS schreiben:** JPEG verlustfrei (nur EXIF-Segment), PNG/TIFF verlustfrei; nachgeprüft, atomar, immer auf eine **Kopie** im Ausgabeordner; einzeln, für alle oder aus einem Referenzbild; Rückgängig pro Bild
- **Bildeditor:** Zuschneiden, Unschärfe, Verpixeln, Text, GPS-Stempel, Drehen; pixelgenau; Undo/Redo; Export PNG/JPEG/TIFF/BMP
- **KI-Metadaten (optional):** Jahreszeit, Titel, Stichwörter per OpenAI, Azure OpenAI oder Gemini; Vorlagen, Budgetgrenzen, Cache, Chat; Speichern erst nach Prüfung als XMP-Sidecar mit Rückgängig; **datensparsam** (keine Originalbilder, keine exakten GPS-Daten, keine Pfade, keine Fotos-Kennungen)
- **Hilfe → Diagnostics** mit „Diagnoseinformationen kopieren“, Logdatei, `--self-test`

## Plattformen

### Windows

- Windows 10 1809 oder neuer, x64 oder ARM64
- **WPF-Anwendung** (`src/PictureGeoExif.Wpf`, bisherige Oberfläche mit WebView2-Karte) – Releases als self-contained ZIP
- **Avalonia-Anwendung** (`src/PictureGeoExif.Avalonia`) läuft ebenfalls unter Windows (CI-Artifact); Apple Fotos steht dort nicht zur Verfügung

### macOS Apple Silicon

```text
macOS:
Apple Silicon M1 oder neuer (M1 … M4)
ARM64 (Runtime Identifier osx-arm64)
macOS 27 als Zielsystem (Mindestversion laut Info.plist: macOS 14)
.NET muss bei self-contained Build nicht separat installiert werden
```

- Avalonia-Anwendung als `PictureGeoExif.app`; Installation per **`PictureGeoExif-macOS-arm64.pkg`** nach /Applications oder per ZIP
- Phase 1: **ohne Developer-ID-Signatur** (nur lokale Ad-hoc-Signatur) und **nicht notarisiert** – macOS zeigt beim ersten Start eines heruntergeladenen Builds eine Gatekeeper-Warnung, siehe [MacOS-Deployment.md](docs/MacOS-Deployment.md)
- Intel-Macs werden nicht getestet und nicht unterstützt.

## Schnellstart

Voraussetzung: [.NET SDK 10](https://dotnet.microsoft.com/download/dotnet/10.0) (siehe `global.json`).

**macOS (Apple Silicon)**

```bash
git clone https://github.com/JoergBrors/PictureGeoExif.git && cd PictureGeoExif
dotnet test PictureGeoExif.CrossPlatform.slnf     # Tests
scripts/run-macos-local.sh                        # Entwicklungsstart (dotnet run)
scripts/build-macos-arm64.sh                      # → artifacts/macos-arm64/PictureGeoExif.app, ZIP und .pkg-Installer
open artifacts/macos-arm64/PictureGeoExif.app
```

**Windows**

```powershell
git clone https://github.com/JoergBrors/PictureGeoExif.git; cd PictureGeoExif
dotnet build PictureGeoExif.sln -c Debug
dotnet test PictureGeoExif.sln
dotnet run --project src/PictureGeoExif.Wpf/PictureGeoExif.Wpf.csproj        # WPF (WebView2 Runtime nötig)
dotnet run --project src/PictureGeoExif.Avalonia/PictureGeoExif.Avalonia.csproj  # Avalonia
```

## Projektstruktur

```text
PictureGeoExif.sln                    alle Projekte (inkl. WPF)
PictureGeoExif.CrossPlatform.slnf     alle Projekte außer WPF (macOS/Linux)
src/
  PictureGeoExif.Core/                Domänenmodelle, Interfaces, Formaterkennung, Validierung – ohne UI-/Plattformabhängigkeit
  PictureGeoExif.Metadata/            EXIF/GPS/XMP/IPTC, Thumbnails, Decoder, Editor-Engine
  PictureGeoExif.Application/         Quellen, Fotos-Pipeline, KI-Workflow, Datenschutz, Karte, Diagnose
  PictureGeoExif.Platform.Mac/        PhotoKit-Adapter, ImageIO (HEIC/RAW), Schlüsselbund
  PictureGeoExif.Platform.Windows/    Windows-Anmeldeinformationen
  PictureGeoExif.Avalonia/            Avalonia-Oberfläche (macOS, Windows), Info.plist
  PictureGeoExif.Wpf/                 bisherige WPF-Oberfläche (Windows)
tests/                                Core-, Metadata-, Application-, Platform-, Avalonia- (headless) und WPF-Tests
scripts/                              build-macos-arm64.sh, run-macos-local.sh, Update-ThirdPartyLicenses.ps1
docs/                                 Dokumentation, docs/examples (KI-Vorlage und Schema)
licenses/                             Lizenztexte aller ausgelieferten Komponenten
```

## Dokumentation

| Thema | Dokument |
| --- | --- |
| Übersicht aller Dokumente | [docs/README.md](docs/README.md) |
| Neue Architektur (Avalonia, Core, PhotoKit) | [docs/Avalonia-Architektur.md](docs/Avalonia-Architektur.md) |
| Analyse der Migration | [docs/Avalonia-Migration-Analyse.md](docs/Avalonia-Migration-Analyse.md) |
| Apple Fotos / PhotoKit | [docs/MacOS-PhotoKit.md](docs/MacOS-PhotoKit.md) |
| macOS: Build, Deployment, Fehlersuche | [MacOS-Build.md](docs/MacOS-Build.md), [MacOS-Deployment.md](docs/MacOS-Deployment.md), [MacOS-Troubleshooting.md](docs/MacOS-Troubleshooting.md) |
| Umsetzungsstand macOS (Phase 1) | [docs/MacOS-Implementation-Report.md](docs/MacOS-Implementation-Report.md) |
| Wo liegt was im Code? | [docs/Code-Wegweiser.md](docs/Code-Wegweiser.md) |
| Build, Tests, CI/Release, Konventionen | [docs/Entwicklung.md](docs/Entwicklung.md) |
| Bedienung | [docs/Benutzerhandbuch.md](docs/Benutzerhandbuch.md) |
| Einsatz im Unternehmen | [docs/Betrieb-und-Unternehmenseinsatz.md](docs/Betrieb-und-Unternehmenseinsatz.md) |
| Änderungen | [changelog.md](changelog.md) |

## Lizenz und Unternehmenseinsatz

MIT-Lizenz, kommerzielle Nutzung erlaubt. Die Bildbibliotheken von Six Labors unterliegen einer **Split License** (für dieses Open-Source-Projekt Apache-2.0; proprietäre Weiterentwicklungen durch Unternehmen ab 1 Mio. USD Jahresumsatz benötigen eine kommerzielle Lizenz). Avalonia, SkiaSharp und CommunityToolkit stehen unter MIT, die Schrift Inter unter OFL-1.1. Für OpenStreetMap-Kacheln gilt die OSM Tile Usage Policy. Einzelheiten: [THIRD-PARTY-LICENSES.md](THIRD-PARTY-LICENSES.md).

## Mitwirken

Issues und Pull Requests gegen `main` sind willkommen. Die CI baut ohne Warnungen auf Windows, macOS (Apple Silicon) und Linux, führt alle Tests aus, prüft Pakete auf Schwachstellen und erzeugt das macOS-`.app` als Artifact. Konventionen: [docs/Entwicklung.md](docs/Entwicklung.md).
