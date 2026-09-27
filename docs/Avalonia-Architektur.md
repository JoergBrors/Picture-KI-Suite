# Architektur: Avalonia + plattformneutraler Core + macOS-PhotoKit-Adapter

Stand 0.98. Die Analyse des Ausgangsstands steht in [Avalonia-Migration-Analyse.md](Avalonia-Migration-Analyse.md), die PhotoKit-Details in [MacOS-PhotoKit.md](MacOS-PhotoKit.md).

## Zielbild

```text
                    PictureGeoExif.Core
                            |
                  PictureGeoExif.Metadata
                            |
                PictureGeoExif.Application
                            |
                 Avalonia UI (PictureGeoExif.Avalonia)
                            |
              ┌─────────────┴─────────────┐
              │                           │
   PictureGeoExif.Platform.Windows   PictureGeoExif.Platform.Mac
   (Credential Manager)              (PhotoKit, ImageIO, Schlüsselbund)
              │                           │
         Dateisystem                   PhotoKit → PHPhotoLibrary → Apple Fotos
```

Der Core weiß nicht, ob ein Bild aus dem Finder, dem Dateisystem, per Drag & Drop, aus Apple Fotos oder aus iCloud-Fotos kommt. Er sieht nur `ImageSourceItem` und – für die Metadatenanalyse – eine lokale Datei.

## Projekte

| Projekt | TFM | Abhängigkeiten | Inhalt |
| --- | --- | --- | --- |
| `src/PictureGeoExif.Core` | `net10.0` | **keine** Pakete | Domänenmodelle (`ImageSourceItem`, `PhotoAsset`, `PhotoAlbum`, `PhotoQuery`, `PhotoExportResult`, `GeoCoordinate`), Interfaces (`IPhotoLibraryService`, `IImageSourceProvider`, `IImageDecoder`, `ICredentialStore`, `IPlatformInfo`), Validierung (`GpsValidation`), Formaterkennung (`ImageFormatDetector`), `AtomicFile`, `RouteBuilder`, `RoadMatcher`, `AppInfo` |
| `src/PictureGeoExif.Metadata` | `net10.0` | MetadataExtractor, XmpCore, SixLabors | EXIF/GPS/XMP/IPTC lesen (`MetadataInspector`), GPS schreiben (`ImageService`, `JpegExifWriter`), Thumbnails als JPEG-Bytes, Decoder (`ImageSharpDecoder`, `CompositeImageDecoder`), Editor-Engine (`EditorSession`, `PixelGeometry`), lokale KI-Hälfte (`AiMetadataService`, `AiModels`) |
| `src/PictureGeoExif.Application` | `net10.0` | Core, Metadata, Azure.Identity, Logging.Abstractions | Anwendungsfälle: `AppSettings`, `AppPaths`, Bildquellen (`FileSystemImageSourceProvider`, `ApplePhotosImageSourceProvider`), Fotos-Pipeline (`PhotoOriginalCache`), `PrivacyGuard`, KI-Anbieter und `AiMetadataWorkflow`, Editor-Operationen (`EditorOperations`), Karte (`IMapViewService`, `MapProjection`, `RouteLayers`), `DiagnosticsService`, `FileLoggerProvider` |
| `src/PictureGeoExif.Platform.Mac` | `net10.0` (AllowUnsafeBlocks) | Core | `MacPhotoLibraryService`, `IPhotoKitFacade`/`ObjCPhotoKitFacade`, `PhotoKitMapping`, `MacImageIODecoder`, `MacKeychainCredentialStore`, `MacPlatformInfo` |
| `src/PictureGeoExif.Platform.Windows` | `net10.0` | Core | `WindowsCredentialStore` |
| `src/PictureGeoExif.Avalonia` | `net10.0`, Assembly `PictureGeoExif` | Avalonia 12, CommunityToolkit.Mvvm, M.E.DependencyInjection/Logging | Views, ViewModels, `OsmMapControl`, Dialog-/Storage-Dienste, DI (`Bootstrap`), `SelfTest` |
| `src/PictureGeoExif.Wpf` | `net10.0-windows` | alle Bibliotheken + WebView2, Ookii | bisherige WPF-Anwendung (Legacy, Windows), nutzt dieselben Bibliotheken |

Regeln:

- **Core** referenziert weder WPF, Avalonia, Microsoft.macOS, AppKit, PhotoKit noch Windows-APIs (hat überhaupt keine Paketabhängigkeit).
- **UI-Code verwendet PhotoKit nie direkt**, nur `IPhotoLibraryService`.
- Plattformadapter werden ausschließlich in `Bootstrap.BuildServices` ausgewählt.
- Der Root-Namespace der Avalonia-App ist `PictureGeoExif.Desktop`, damit `Avalonia.*` nie auf einen Projekt-Namespace aufgelöst wird. In Dateien unter `PictureGeoExif.*` muss die Avalonia-Klasse `Application` als `global::Avalonia.Application` geschrieben werden, weil der Namespace `PictureGeoExif.Application` existiert.

## Quellen-Konzept (Source Provider)

```csharp
public interface IImageSourceProvider
{
    string Name { get; }
    Task<IReadOnlyList<ImageSourceItem>> GetImagesAsync(CancellationToken cancellationToken = default);
    Task<Stream> OpenImageAsync(ImageSourceItem image, CancellationToken cancellationToken = default);
}
```

- `FileSystemImageSourceProvider`: Dateien aus Dialog, Ordner oder Drag & Drop (Finder/Explorer).
- `ApplePhotosImageSourceProvider`: ein Album bzw. die Mediathek, **paginiert** (`IPagedImageSourceProvider.GetPageAsync`, Standard 120 pro Seite).

`ImageSourceItem` (Metadata-Source-Modell) enthält: `SourceType`, `SourceId`, `OriginalFileName`, `OriginalFormat`, `LocalPath` (optional), `PhotoKitAssetId` (optional), `IsReadOnly`, `CanExport`, `CanEdit` sowie Erstellungsdatum, Pixelmaße, Favorit und Standort aus der Quelle. Ein lokaler Pfad wird **nicht** erzwungen.

## Pipeline: Apple-Fotos-Asset → Metadaten

```text
PHAsset (Liste, nur Thumbnail)
   ↓  Auswahl
PHAssetResource (Original: Photo → AlternatePhoto → AdjustmentBasePhoto → FullSizePhoto)
   ↓  PHAssetResourceManager.requestData… (iCloud erlaubt, Fortschritt, Abbruch)
PhotoOriginalCache: ~/Library/Caches/PictureGeoExif/photos-originals/<Hash>/<Originalname>
   ↓
MetadataInspector (derselbe Code wie für Dateien): EXIF / GPS / XMP / IPTC / Raw
```

Es gibt **keinen** zweiten EXIF-Parser für Fotos. Der Cache-Ordner ist nach einem Hash benannt, nicht nach der PhotoKit-Kennung.

## Karte

`IMapViewService` (Application) beschreibt, was die App von einer Karte braucht: Marker, Auswahl, aktuelle Koordinate, Trassen, Wegverlauf, Ebenen, Raster, Zentrieren – die Funktionen, die bisher per `ExecuteScriptAsync` an Leaflet gingen. Umsetzung in Avalonia: `MapViewModel` + `OsmMapControl` (eigene Kachelkarte, ohne WebView):

- OSM-Kacheln mit ehrlichem User-Agent, höchstens 2 Verbindungen, 7 Tage Plattencache unter `…/Caches/PictureGeoExif/map-tiles`, Pause nach HTTP 403/429;
- Attribution sichtbar in der Karte, Link über „© OSM“;
- Klick setzt eine Koordinate, Klick auf Marker wählt ein Bild, Ziehen verschiebt, Mausrad/± zoomt.

Warum keine WebView: Avalonia 12 bringt keine freie, stabile Cross-Platform-WebView mit; die kommerzielle Avalonia-WebView bzw. Community-Pakete wären eine zusätzliche Abhängigkeit mit eigenem Risiko. Leaflet und `map.js` bleiben für WPF erhalten und können über eine zweite `IMapViewService`-Implementierung wieder angebunden werden.

## Bildeditor

- Pixel-Engine und Historie: `EditorSession` (Metadata).
- Werkzeuglogik (früher `ImageEditorWindow.GetOperation`): `EditorOperations.Create(EditorToolSettings, width, height)` (Application), getestet.
- Avalonia: nur Darstellung (Zoom über `LayoutTransformControl`, eine lokale Einheit = ein Bildpixel) und Zeigereingabe.
- Kein `System.Drawing`. Stempelschrift mit Fallback-Kette (Segoe UI, Arial, Helvetica Neue, Helvetica, DejaVu Sans).

## KI-Metadaten

`AiMetadataWorkflow` (Application) enthält die Ablauflogik des früheren WPF-Code-behind: Vorlagen, Preise, Budget-Reservierung, lokale Prüfung, Analyse mit begrenzter Parallelität, Review, Sidecar-Schreiben mit Audit und Undo, Chat. Jede ausgehende Anfrage passiert `PrivacyGuard` (keine Pfade, keine PhotoKit-Kennungen, keine exakten Koordinaten). Apple-Fotos-Assets werden **vor** der KI-Nutzung als Datei übernommen („In Dateien übernehmen“) und laufen dann durch dieselbe Pipeline wie normale Dateien.

## Dependency Injection und Logging

`Bootstrap.BuildServices` registriert Einstellungen, Pfade, Plattformadapter, Decoder, Dienste und ViewModels. Logging: Konsole + Datei (`FileLoggerProvider`, ein File pro Tag, 14 Tage). Beim Start wird eine Zeile mit Version, .NET, OS/macOS-Version, Architektur, ARM64, Avalonia-Version, PhotoKit-Verfügbarkeit, Berechtigungsstatus, Bundle- und Resources-Pfad geloggt – ohne Benutzerdaten.

## Speicherorte

| Zweck | macOS | Windows |
| --- | --- | --- |
| Einstellungen, Vorlagen | `~/Library/Application Support/PictureGeoExif/` | `%APPDATA%\PictureExifclone\` (wie bisher) |
| KI-Cache, Audit | `~/Library/Application Support/PictureGeoExif/ai-cache`, `ai-audit` | `%LOCALAPPDATA%\PictureGeoExif\…` |
| Fotos-Originale (Cache), Kartenkacheln | `~/Library/Caches/PictureGeoExif/` | `%LOCALAPPDATA%\PictureGeoExif\cache\` |
| Log | `~/Library/Logs/PictureGeoExif/` | `%LOCALAPPDATA%\PictureGeoExif\logs\` |
| Ausgabeordner (Standard) | `~/Pictures/PictureGeoExif_Output/` | `Dokumente\PictureExifclone_Output\` |
| API-Schlüssel | Anmeldeschlüsselbund, Dienst `PictureGeoExif` | Windows-Anmeldeinformationen |

## Tests

| Projekt | Plattform | Inhalt |
| --- | --- | --- |
| `PictureGeoExif.Core.Tests` | alle | Formaterkennung (Signaturen, HEIC-Brands, UTI), AtomicFile, Modelle, Trassen, Map-Matching (Fake-Server) |
| `PictureGeoExif.Metadata.Tests` | alle | bisherige Metadaten-/Logiktests, `MetadataInspector`, Decoder-Fallback, HEIC-GPS-Ablehnung |
| `PictureGeoExif.Application.Tests` | alle | Quellen, Fotos-Pipeline mit Fake-Mediathek, `PrivacyGuard`, Editor-Operationen, Kartenmathematik, KI-Workflow, Diagnose |
| `PictureGeoExif.Platform.Tests` | alle (+ macOS-Interop auf macOS) | PhotoKit-Mapping, Berechtigungs-Mapping, Export-Logik mit `FakePhotoKitFacade`; auf macOS zusätzlich echtes Laden von Photos.framework und ImageIO |
| `PictureGeoExif.Avalonia.Tests` | alle (headless) | Alle Fenster laden, Dateien laden, GPS-Kopie + Undo, Editor, Diagnose |
| `PictureGeoExif.Wpf.Tests` | Windows | bisherige WPF-Fenstertests |

`PictureGeoExif.CrossPlatform.slnf` enthält alle Projekte außer WPF und wird unter macOS/Linux gebaut und getestet.
