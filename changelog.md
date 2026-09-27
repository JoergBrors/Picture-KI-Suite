# Changelog

Alle nennenswerten Änderungen an PictureGeoExif. Format angelehnt an [Keep a Changelog](https://keepachangelog.com/de/1.1.0/). Versionen entsprechen den Git-Tags.

## [0.98] – 2026-09-27

### Hinzugefügt

- **macOS auf Apple Silicon** (osx-arm64, Ziel macOS 27): neue **Avalonia-Oberfläche** (`src/PictureGeoExif.Avalonia`) als `PictureGeoExif.app`, unsigniert; Build mit `scripts/build-macos-arm64.sh`, Start mit `scripts/run-macos-local.sh`.
- **Apple Fotos (PhotoKit):** Berechtigungs-Flow (nur auf Benutzeraktion), Mediathek, Favoriten, Alben, intelligente Alben, paginierte Fotoliste mit Thumbnails, Details (Datum, Typ, Maße, Favorit, Standort, Asset-Kennung), Original lesen/exportieren inkl. iCloud-Download mit Fortschritt und Abbruch; read-only.
- **Metadaten-Tabs** Allgemein, EXIF, GPS, XMP (eingebettet + Sidecar), IPTC, Raw – für Dateien und Fotos-Originale mit derselben Analyse (`MetadataInspector`).
- **HEIC/HEIF, AVIF, DNG, RAW:** Erkennung per Dateisignatur, Metadaten überall, Vorschau unter macOS über ImageIO.
- **Plattformneutrale Karte** (OSM-Kacheln, ohne WebView2) hinter `IMapViewService`.
- **Help → Diagnostics** mit „Diagnoseinformationen kopieren“, Logdatei, `PictureGeoExif --self-test`.
- **Datenschutzprüfung** (`PrivacyGuard`) vor jeder KI-Anfrage: keine Pfade, keine Fotos-Kennungen, keine exakten GPS-Daten.
- macOS-Schlüsselbund für API-Schlüssel.
- **Installer `PictureGeoExif-macOS-arm64.pkg`** (installiert nach /Applications, nur Apple Silicon, ab macOS 14).
- CI-Jobs für macOS arm64 (Artifacts `PictureGeoExif-macOS-arm64` und `…-pkg`, Test-Installation auf dem Runner) und Linux; Releases enthalten ZIP und `.pkg` für macOS.

### Geändert

- **Neue Solution-Struktur:** `PictureGeoExif.Core` (ohne Paketabhängigkeiten), `.Metadata`, `.Application`, `.Platform.Mac`, `.Platform.Windows`, `.Avalonia`; die WPF-App liegt unter `src/PictureGeoExif.Wpf` und nutzt dieselben Bibliotheken. Zentrale Paketversionen (`Directory.Packages.props`), Version in `Directory.Build.props`.
- `ImageService` liefert Thumbnails als JPEG-Bytes (ohne WPF-Typen) über austauschbare Decoder; Vorschauen enthalten keine EXIF/XMP/IPTC-Profile mehr.
- Editor-Werkzeuglogik und KI-Ablauf aus dem Code-behind in Application-Services verschoben (`EditorOperations`, `AiMetadataWorkflow`).
- Stempelschrift mit Fallback (Segoe UI, Arial, Helvetica Neue, Helvetica, DejaVu Sans).
- Tests auf mehrere Projekte verteilt; plattformneutrale Tests laufen auch unter macOS und Linux.

### Entfernt

- Ungenutzte Legacy-Klassen `OptimizedImageService`, `ImageProcessingService`, `UndoService`, `CoordinateMapper`, `ImageDocument`, `ToolContext`, `ToolMode`.

## [0.97] – 2026-09-27

### Hinzugefügt

- **Virtuelle Trassen:** GPS-Punkte werden zu Trassen gruppiert (einstellbarer maximaler Punktabstand) und entlang des Verlaufs Süd → Nord bzw. West → Ost geordnet; die Bildliste folgt dieser Reihenfolge (abschaltbar). Kacheln zeigen „Trasse n · Nr. k“. (`Services/RouteBuilder.cs`)
- **Abzweige / Hausanschlüsse:** Jede Trasse besteht aus Hauptstrang und Abzweigen.
  - Fotos, die weiter als „Abzweig ab“ (Standard 10 m) neben dem Hauptstrang liegen, werden senkrecht angebunden, statt den Hauptstrang umzuleiten.
  - Die Kachel zeigt „· Abzweig“; in der Bildliste steht das Foto hinter seiner Ansatzstelle.
  - Beim Anlegen an Wege folgt ein Abzweig einem Weg in Reichweite, sonst dem direkten Weg zur Trasse.
- **Layer-Schalter** für Trassen und Bilder auf der Karte.
- **Rückgängig pro Bild** (↶ an der Kachel): stellt Pfad und Koordinaten vor der letzten Speicherung bzw. GPS-Zuweisung wieder her.
- **Trassen an Wege anlegen** (Map-Matching über Valhalla, `Services/RoadMatcher.cs`):
  - eigene Ebene „Wegverlauf“, Fußweg-Profil als Standard;
  - Fotos weiter als 25 m vom Weg werden gerade verbunden; der Abstand je Foto steht in der Infozeile;
  - Server, Profil und Abstand über ⚙ einstellbar. Standard ist der öffentliche FOSSGIS-Demo-Server (fair use, gedrosselt auf ≤ 1 Anfrage/s);
  - die GPS-Daten der Fotos bleiben unverändert.

### Geändert

- Neuer GPS-Punkt nur noch per **Doppelklick** (der Doppelklick-Zoom ist dafür deaktiviert).
- Bildname und Koordinaten erscheinen im grauen Bereich über der Karte statt als Popup, das andere Punkte verdeckt.

### Behoben

- Kachelbuttons waren abgeschnitten, weil der globale Button-Innenabstand die schmalen Buttons überdeckte. Die Kacheln haben jetzt einen eigenen Stil.

## [0.96] – 2026-09-27

Details und Nachweise: [docs/GUI-AI-Update-Plan.md](docs/GUI-AI-Update-Plan.md), Abschnitt 10.

### Hinzugefügt

- **KI-Metadaten-Fenster** (`AiMetadataWindow`):
  - Anbieter: OpenAI (`gpt-5-mini`, Standard), Azure OpenAI (Entra ID oder Schlüssel), Google Gemini; jeweils Structured Outputs.
  - Stapelverarbeitung mit lokaler Queue, begrenzter Parallelität, Budgetreservierung und Ergebnis-Cache.
  - Metadaten-Chat mit typisierten Aktionen; manuelle Jahreszeit ohne Modellaufruf.
  - JSON-Vorlagen mit Validierung und Versionierung.
  - Review und Speichern als XMP-Sidecar mit Nachprüfung, Audit-Log und Rückgängig.
- Lokale Übernahme von EXIF nach XMP: Aufnahmezeit, GPS, Urheber, Copyright.
- Erkennung von Nutzungspräferenzen: EXIF `LearningOptOutIn` führt zu einer Entscheidung vor dem Senden, IPTC-Data-Mining-Einschränkungen zum Überspringen.
- API-Schlüssel in der Windows-Anmeldeinformationsverwaltung.
- Neue Services: `JpegExifWriter`, `AtomicFile`, `PixelGeometry`, `EditorSession`, `AiMetadataService`, `AiProviders`, `CredentialStore`.
- **Editor:** Undo/Redo, echte Unschärfe (Blur), Vorher/Nachher-Vergleich, Formatwahl beim Export, Abbrechen langer Vorgänge.
- **Tests:** `tests/PictureGeoExif.Tests` (xUnit, 46 Tests).
- **CI:** `build.yml` für Pull Requests (Build ohne Warnungen, Tests, Paketaudit).
- **Lizenzen:** `scripts/Update-ThirdPartyLicenses.ps1` erzeugt `licenses/` reproduzierbar aus den NuGet-Paketen.
- **Dokumentation:** `docs/README.md`, `Code-Wegweiser.md`, `Architektur.md`, `Entwicklung.md`, `Benutzerhandbuch.md`, `Betrieb-und-Unternehmenseinsatz.md`.

### Geändert

- **Laufzeit:** .NET 8 → **.NET 10** (`global.json` 10.0.401), `TreatWarningsAsErrors`, generierte Assembly-Metadaten, NuGet-Audit.
- **Karte:**
  - Leaflet 1.9.4 wird lokal über einen WebView2-Virtual-Host ausgeliefert; Content-Security-Policy.
  - Tile-URL `tile.openstreetmap.org` ohne Subdomains; eigene App-Kennung im User-Agent.
  - Sichtbare Attribution, Fehleranzeige bei 403/429, begrenztes Raster, validierte Webnachrichten.
  - Tile-Anbieter über `settings.json` konfigurierbar.
- **GPS-Schreiben:**
  - JPEG wird nicht mehr neu kodiert (vorher Qualitätsverlust); nur der EXIF-Block wird ersetzt.
  - PNG/TIFF verlustfrei; BMP wird abgelehnt.
  - Nachprüfung und atomares Schreiben.
- **Exporte:** kollisionsfreie Namen (Millisekunden + GUID), kein Überschreiben vorhandener Dateien, keine Teildateien bei Fehlern.
- **Editor:**
  - Pixelgenaue Auswahl unabhängig von Zoom und DPI; Fit auch unter 10 %.
  - Stempel mit Textmessung und Randabstand an allen Ankern.
  - Verlustfreie Historie; Export im gewählten statt erzwungenem JPEG-Format.
- **Release-Workflow:** baut den Tag-Commit statt `main`, nutzt .NET aus `global.json` und führt die Tests aus.
- **Pakete:** MetadataExtractor 2.9.3, WebView2 1.0.4191.47, XmpCore 6.1.10.1 (explizit), Azure.Identity 1.21.0 (neu). SixLabors bleibt aus Lizenzgründen auf den bisherigen Hauptversionen.
- **`THIRD-PARTY-LICENSES.md`:**
  - Alle Laufzeitkomponenten sind erfasst, auch transitive und die .NET-Runtime.
  - Die Six Labors Split License ist erläutert.
  - Neu ist ein Abschnitt zum Einsatz im Unternehmen.

### Behoben

- Build-Warnungen CA1416, CS0108, CS8618, CS8073 (46 Meldungen) an der Ursache behoben.
- „Alle entfernen“ leerte die Karte auch nach „Nein“.
- Bildauswahl über die Kachel führte die Karte nicht nach.
- Zeichenkodierung: 8 Quelldateien waren Windows-1252. Umlaute in Meldungen, Firmenname und GPS-Anzeige („??“) waren fehlerhaft.
- `licenses/` enthielt gespeicherte NuGet-HTML-Seiten statt Lizenztexten und veraltete Versionen. Die falschen Ordnernamen `LeafFleat` und `OpenStreetView` heißen jetzt `Leaflet` und `OpenStreetMap`.

### Bekannte Einschränkungen

- `LearningOptOutIn` wird noch nicht decodiert oder geschrieben (erst nach dem Abgleich mit der CIPA-Norm).
- XMP wird nur als Sidecar geschrieben.
- Der native Batch-Modus der Anbieter ist nicht umgesetzt.
- „Alle speichern“ läuft synchron im UI-Thread.
- Die Legacy-Services (`OptimizedImageService`, `ImageProcessingService`, `UndoService`, `CoordinateMapper`) sind noch im Projekt, aber ungenutzt.

## [0.95] und früher (Februar 2026)

Frühere Stände sind über die Git-Tags `0.6`, `0.8`, `v0.9`, `v0.92` und `v0.95` erreichbar. Zusammenfassung:

- Erste WPF-Anwendung zur Georeferenzierung mit Kachelansicht, Karte (Leaflet/OSM), Referenzbild und Stapelspeicherung.
- Bildeditor mit Zuschneiden, Text, GPS-Stempel, Verpixeln und einfacher Temp-Datei-Versionierung.
- „GPS auf alle Bilder anwenden“ mit Kartensynchronisation.
- Thumbnail-Caching und asynchrones Laden.
- Open-Source-`SimpleColorPicker` statt einer externen ColorPicker-Komponente.
- In-App-Lizenzfenster und erste Lizenzdateien.
- GitHub-Release-Workflow für `win-x64` und `win-arm64`.

Mehrere Fehler dieser Phase wurden damals in einzelnen `*_FIX.md`-Dateien beschrieben. Diese Dateien sind nicht mehr im Repository; die betroffenen Stellen (Bildanzeige nach Änderungen, Thumbnails, Editor-Laden, Scrollposition) wurden mit Version 0.96 durch `EditorSession` und `ImageService` ersetzt.
