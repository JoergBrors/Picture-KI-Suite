# Avalonia-Migration – Analyse des Ausgangsstands

Ausgangsbasis: [PictureGeoExif](https://github.com/JoergBrors/PictureGeoExif) Version 0.97 (Commit `06721bf`), eine WPF-Anwendung auf .NET 10 (`net10.0-windows10.0.17763.0`). Diese Analyse wurde **vor** der Umstrukturierung erstellt und beschreibt, was plattformneutral ist, was abstrahiert werden muss und was Windows-only bleibt. Die daraus abgeleitete Zielarchitektur steht in [Avalonia-Architektur.md](Avalonia-Architektur.md).

## 1. Erkannte Architektur des bestehenden Projekts

```text
PictureExifclone.csproj (WinExe, UseWPF, net10.0-windows)
├── MainWindow.xaml(.cs)          Bildliste, Karte (WebView2), GPS setzen/speichern, Trassen, Map-Matching
├── ImageEditorWindow.xaml(.cs)   Editor-UI; Werkzeuglogik (Operation zusammensetzen) im Code-behind
├── AiMetadataWindow.xaml(.cs)    KI-Workflow: Vorlage, Anbieter, Budget, Batch-Queue, Review, Chat, Undo
├── RoadMatchSettingsWindow       Einstellungen Map-Matching
├── LicenseViewer                 Lizenzanzeige
├── Controls/SimpleColorPicker    WPF-Farbwähler
├── AppSettings.cs                JSON-Einstellungen unter %APPDATA%
├── Models/                       ImageItem (mit WPF BitmapImage), AiModels (neutral), 3 ungenutzte Modelle
├── Services/                     Fachlogik (überwiegend UI-frei, getestet)
├── Resources/                    map.html, map.js, Leaflet (für WebView2)
└── tests/PictureGeoExif.Tests    xUnit (referenziert die WPF-App direkt, daher Windows-only)
```

Charakteristik:

- **Code-behind statt MVVM.** Die Fenster enthalten erhebliche Ablauflogik (z. B. KI-Batch mit Budget-Reservierung, Werkzeugparameter des Editors, Trassen-Neuberechnung).
- **Services sind gut isoliert.** Bis auf `ImageService` (liefert WPF `BitmapImage`) und `CredentialStore` (Win32) sind sie plattformneutral.
- **Ein Projekt für alles.** Tests referenzieren die WPF-App; dadurch sind auch neutrale Tests nur unter Windows lauffähig.
- **Sicherheitsprinzipien sind konsequent umgesetzt:** atomares Schreiben, Originale unverändert, Nachprüfung vor dem Ersetzen, KI-Datenminimierung, Review vor Schreiben.

## 2. Windows-/WPF-Abhängigkeiten

| Fundstelle | Abhängigkeit | Art |
|---|---|---|
| `*.xaml`, `*.xaml.cs` (5 Fenster, 1 Control, `App`) | `System.Windows`, `PresentationFramework`, `PresentationCore` | UI-spezifisch |
| `Services/ImageService.cs` | `System.Windows.Media.Imaging.BitmapImage` für Thumbnails und `LoadBitmapImage` | abstrahierbar |
| `Models/ImageItem.cs` | `BitmapImage Thumbnail` | UI-spezifisch |
| `Models/ImageDocument.cs`, `Models/ToolContext.cs` | `System.Windows.Point/Rect`, `System.Windows.Media.Color` (ungenutzt) | entfernbar |
| `MainWindow.xaml.cs` | `Microsoft.Web.WebView2` (Karte, Virtual Host, `postMessage`, `ExecuteScriptAsync`) | Windows-only |
| `MainWindow.xaml.cs`, `AiMetadataWindow.xaml.cs` | `Microsoft.Win32.OpenFileDialog` | Windows-Dateidialog |
| `MainWindow.xaml.cs` | `Ookii.Dialogs.Wpf.VistaFolderBrowserDialog` | Windows-Dateidialog |
| `MainWindow.xaml.cs` | WPF Drag & Drop (`DataFormats.FileDrop`) | UI-spezifisch |
| alle Fenster | `MessageBox` | UI-spezifisch |
| `Services/CredentialStore.cs` | `advapi32.dll` (`CredReadW/CredWriteW/CredDeleteW`) | Win32, Windows-only |
| `AppSettings.cs`, `AiMetadataWindow`, `AiMetadataService` | `%APPDATA%`/`%LOCALAPPDATA%` über `Environment.SpecialFolder` | plattformneutral (liefert unter macOS `~/Library/Application Support` bzw. `~/.local/share`) |
| `EditorSession.Stamp` | Schriftart „Segoe UI“ oder „Arial“ | Windows-Schrift als Voraussetzung |
| `Services/OptimizedImageService`, `ImageProcessingService`, `UndoService`, `CoordinateMapper` | WPF-Typen, laut `Services/README.md` ungenutzt | Legacy |
| `tests/.../WindowTests.cs` | STA-Thread, WPF-Fenster | Windows-only |
| `.github/workflows/*.yml` | `windows-latest`, PowerShell | Windows-only |
| `scripts/Update-ThirdPartyLicenses.ps1` | PowerShell | plattformneutral mit `pwsh` |

Nicht gefunden: Windows Registry, Windows Clipboard APIs, weitere Win32-Aufrufe, hart kodierte Windows-Pfade in Services (Pfade entstehen über `Path.Combine`).

## 3. Klassifikation

| Kategorie | Komponenten |
|---|---|
| **bereits plattformneutral** | `AtomicFile`, `JpegExifWriter`, `PixelGeometry`, `EditorSession`, `RouteBuilder`, `RoadMatcher`, `AppInfo`, `AiMetadataService`, `AiProviders` (bis auf `CredentialStore`-Aufruf), `Models/AiModels.cs`, `AppSettings`, Leaflet-Ressourcen |
| **abstrahierbar** | `ImageService` (Thumbnail als Bytes statt `BitmapImage`), `CredentialStore` (→ `ICredentialStore`), Karte (→ `IMapViewService`), Dateidialoge (→ Avalonia `StorageProvider`), Editor-Werkzeuglogik (→ Application-Service), KI-Ablauf aus `AiMetadataWindow` (→ Application-Service), HEIC/RAW-Dekodierung (→ `IImageDecoder`) |
| **Windows-only** | `CredentialStore` (Win32), WebView2-Kartenintegration, WPF-Dialoge (Ookii) |
| **UI-spezifisch** | alle WPF-Fenster, `SimpleColorPicker`, `ImageItem.Thumbnail`, `WindowTests` |

## 4. Komponententabelle

| Komponente | Aktuell | Plattformabhängigkeit | Ziel | Maßnahme |
|---|---|---|---|---|
| `AtomicFile` | `Services/` | keine | `PictureGeoExif.Core` (`IO`) | verschieben, Tests ergänzen |
| `AppInfo` | `Services/` | keine | `PictureGeoExif.Core` | verschieben; Version zentral über `Directory.Build.props` |
| `PixelGeometry.ValidGps` | `Services/` | keine | `PictureGeoExif.Core` (`GpsValidation`) | extrahieren; `PixelGeometry` delegiert |
| `RouteBuilder` | `Services/` | keine | `PictureGeoExif.Core` (`Geo`) | verschieben |
| `RoadMatcher` | `Services/` | keine (HttpClient) | `PictureGeoExif.Core` (`Geo`) | verschieben |
| Domänenmodelle (Bildquelle, Fotomediathek, GPS) | nicht vorhanden | – | `PictureGeoExif.Core` | neu: `ImageSourceItem`, `PhotoAsset`, `PhotoAlbum`, `PhotoLibraryAccessStatus`, `GeoCoordinate` … |
| Formaterkennung | nur Dateiendung in `MainWindow.LoadImages` | keine | `PictureGeoExif.Core` (`ImageFormatDetector`) | neu: Signatur + Endung, inkl. HEIC/HEIF, DNG, RAW |
| `JpegExifWriter` | `Services/` | keine | `PictureGeoExif.Metadata` | verschieben |
| `ImageService` | `Services/` | WPF `BitmapImage` | `PictureGeoExif.Metadata` | Thumbnails als JPEG-Bytes, Decoder über `IImageDecoder`; GPS-Schreiben unverändert |
| `PixelGeometry`, `EditorSession` | `Services/` | keine; Schrift „Segoe UI/Arial“ | `PictureGeoExif.Metadata` (`Editing`) | verschieben; Schrift-Fallback (Helvetica, DejaVu Sans) |
| Editor-Werkzeuglogik | `ImageEditorWindow.GetOperation` | WPF-Typen | `PictureGeoExif.Application` (`EditorOperations`) | extrahieren, testen |
| Metadatenanzeige (EXIF/GPS/XMP/IPTC/Raw) | nur Editor „EXIF“-Button | WPF | `PictureGeoExif.Metadata` (`MetadataInspector`) | neu, gruppiert nach Kategorien |
| `AiMetadataService`, `AiModels` | `Services/`, `Models/` | keine | `PictureGeoExif.Metadata` (`Ai`) | verschieben |
| `AiProviders` | `Services/` | `CredentialStore` (Win32) | `PictureGeoExif.Application` (`Ai`) | `ICredentialStore` injizieren |
| KI-Ablauf (Budget, Queue, Chat, Undo) | `AiMetadataWindow.xaml.cs` | WPF | `PictureGeoExif.Application` (`AiMetadataWorkflow`) | extrahieren; UI nur Anzeige |
| `CredentialStore` | `Services/` | Win32 `advapi32` | `PictureGeoExif.Platform.Windows` | `WindowsCredentialStore : ICredentialStore`; macOS: Schlüsselbund |
| `AppSettings` | Projektstamm | keine | `PictureGeoExif.Application` | verschieben; Pfade über `AppPaths` |
| Karte | WebView2 + Leaflet | Windows-only | `IMapViewService` + Avalonia-Kartensteuerelement (OSM-Kacheln) | neu; Leaflet bleibt für WPF |
| Dateidialoge | `OpenFileDialog`, Ookii | Windows-only | Avalonia `StorageProvider` | neu im Avalonia-UI |
| Drag & Drop | WPF `DataFormats.FileDrop` | WPF | Avalonia `DragDrop` | neu im Avalonia-UI |
| Fotomediathek | nicht vorhanden | – | `IPhotoLibraryService` + `PictureGeoExif.Platform.Mac` | neu: PhotoKit-Adapter |
| HEIC/RAW-Thumbnails | nicht unterstützt | – | `IImageDecoder` + macOS ImageIO | neu |
| Hauptfenster, Editor, KI-Fenster | WPF | WPF | Avalonia + MVVM | neu aufgebaut (kein 1:1-XAML) |
| WPF-Anwendung | Projektstamm | Windows | `src/PictureGeoExif.Wpf` (Legacy) | bleibt lauffähig, nutzt die neuen Bibliotheken |
| Legacy-Services | `OptimizedImageService`, `ImageProcessingService`, `UndoService`, `CoordinateMapper`, `ImageDocument`, `ToolContext`, `ToolMode` | WPF | – | entfernt (laut `Services/README.md` ungenutzt, keine Funktion geht verloren) |
| Tests | ein Projekt, Windows-only | WPF | `Core.Tests`, `Metadata.Tests`, `Application.Tests`, `Platform.Tests`, `Avalonia.Tests`, `Wpf.Tests` | bestehende Tests übernommen, neutrale Tests laufen auf allen Plattformen |
| CI | `windows-latest` | Windows | Windows + Linux + macOS-ARM64-Job | erweitert |

## 5. Konkrete Risiken

| Risiko | Bewertung | Gegenmaßnahme |
|---|---|---|
| **Microsoft.macOS-Bindings und Avalonia** schließen sich faktisch aus: Bindings erfordern `net10.0-macos` (Workload + Xcode), die App müsste dann selbst `net10.0-macos` sein, und Avalonia.Native verwaltet `NSApplication` selbst. Der geforderte Build (`dotnet publish -r osx-arm64` + eigenes `.app`) ist der Avalonia-Weg ohne `-macos`-TFM. | hoch | PhotoKit über die **Objective-C-Laufzeit** (`libobjc`, `Photos.framework`) aus einer normalen `net10.0`-Bibliothek ansprechen; ausschließlich öffentliche PhotoKit-APIs. Hinter `IPhotoKitFacade` gekapselt, sodass eine Bindings-Implementierung später austauschbar ist. Entscheidung dokumentiert in [MacOS-PhotoKit.md](MacOS-PhotoKit.md). |
| **Apple Silicon verlangt signierte arm64-Binaries** (mindestens ad hoc), sonst beendet der Kernel den Prozess. TCC (Fotos-Freigabe) identifiziert die App über die Signatur. | hoch | Lokale **Ad-hoc-Signatur** des Bundles im Build-Skript, begründet in [MacOS-Deployment.md](MacOS-Deployment.md). Keine Developer-ID, keine Notarisierung. |
| HEIC/RAW: ImageSharp 3 dekodiert kein HEIC/RAW | mittel | Decoder-Abstraktion; unter macOS ImageIO (Systemframework, keine Zusatzlizenz). Metadaten (EXIF/GPS/XMP) liest MetadataExtractor auch aus HEIC. |
| Keine stabile, freie Cross-Platform-WebView für Avalonia | mittel | Eigenes Kartensteuerelement mit OSM-Kacheln (Cache, User-Agent gemäß Tile Policy); Leaflet bleibt für WPF und eine spätere WebView-Variante. |
| iCloud-Originale sind nicht lokal | mittel | Asynchroner Abruf mit Netzwerkfreigabe, Fortschritt, Abbruch, verständliche Fehlermeldung, „Erneut versuchen“. |
| Große Mediatheken | mittel | Paging, Thumbnails in kleiner Größe, begrenzte Parallelität, Abbruch beim Albumwechsel. |
| Objective-C-Interop ohne Tests auf echter Hardware in dieser Umgebung | mittel | Interop dünn halten, gesamte Zuordnungslogik (Status, Ressourcenauswahl, Export) in testbarem C# hinter der Fassade; Definition-of-Done-Punkte mit echter Hardware im Implementierungsbericht offen markiert. |
| SixLabors-Lizenzwechsel bei Major-Updates | niedrig | Versionen bleiben auf ImageSharp 3.1.x / Drawing 2.1.x / Fonts 2.1.x. |
| Avalonia-Telemetrie (`Avalonia.BuildServices`) | niedrig | dokumentiert; abschaltbar mit `AVALONIA_TELEMETRY_OPTOUT=1` (Build-Skript und CI setzen die Variable). |

## 6. Migrationsreihenfolge

1. **Analyse** (dieses Dokument).
2. **Core Extraction:** Core, Metadata, Application und Platform-Bibliotheken anlegen; Services verschieben; Tests auf neue Projekte verteilen und plattformneutral lauffähig machen.
3. **WPF auf die Bibliotheken umstellen** (Windows-Build erhalten).
4. **Avalonia Shell:** Hauptfenster, Dateidialoge, Drag & Drop, Vorschau, Metadatenansicht, Diagnose.
5. **PhotoKit-Adapter:** Berechtigung, Alben, Assets, Thumbnails, Original-Export, Pipeline in die Metadatenanalyse.
6. **Bestehende Funktionen:** GPS schreiben, Karte, Trassen, Editor, KI-Metadaten.
7. **Packaging:** `osx-arm64`, `.app`, ZIP, GitHub Actions.
8. **Dokumentation und Tests** vervollständigen, Implementierungsbericht.
