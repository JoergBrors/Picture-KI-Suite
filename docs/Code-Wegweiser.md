# Code-Wegweiser: Wo ist was?

Schneller Einstieg in den Code. Stand 0.98: .NET 10, Avalonia (macOS/Windows) und WPF (Windows, Legacy). Die Schichten erklärt [Avalonia-Architektur.md](Avalonia-Architektur.md), die Datenflüsse [Architektur.md](Architektur.md).

## Verzeichnisstruktur

```text
PictureGeoExif.sln / PictureGeoExif.CrossPlatform.slnf
Directory.Build.props                 gemeinsame Einstellungen, <Version>
Directory.Packages.props              zentrale Paketversionen
src/PictureGeoExif.Core/
  AppInfo.cs                          Produktname, Bundle-ID, Version, User-Agent
  Geo/                                GeoCoordinate, GpsValidation, RouteBuilder, RoadMatcher
  Imaging/                            ImageFormat, ImageFormatDetector (Signatur/Endung/UTI), IImageDecoder
  IO/AtomicFile.cs                    atomares Schreiben, Exportnamen, FreePath, SafeFileName
  Photos/                             IPhotoLibraryService, PhotoAsset/Album/Query/ExportResult, Ressourcenauswahl
  Sources/                            ImageSourceItem, IImageSourceProvider
  Platform/IPlatformInfo.cs, Security/ICredentialStore.cs
src/PictureGeoExif.Metadata/
  ImageService.cs                     Thumbnails (Bytes), GPS lesen/schreiben, Kopien speichern
  JpegExifWriter.cs                   EXIF-Segment ersetzen ohne Neukodierung
  MetadataInspector.cs                EXIF/GPS/XMP/IPTC/Raw für die Metadaten-Tabs
  Decoding/                           ImageSharpDecoder, CompositeImageDecoder
  Editing/                            EditorSession (Historie, Blur, Pixelate, Stamp), PixelGeometry
  Ai/                                 AiModels (Vorlage, Antwort, Zeilen), AiMetadataService (lesen, Vorschau, Sidecar)
src/PictureGeoExif.Application/
  AppSettings.cs, AppPaths.cs         Einstellungen und Speicherorte je Plattform
  Ai/                                 AiProviders (OpenAI/Azure/Gemini), AiMetadataWorkflow (Budget, Queue, Review, Chat, Undo)
  Sources/                            FileSystemImageSourceProvider, ApplePhotosImageSourceProvider
  Photos/PhotoOriginalCache.cs        Fotos-Original → lokaler Cache für die Metadatenanalyse
  Privacy/PrivacyGuard.cs             Sperre für Pfade, PhotoKit-IDs, exakte GPS in KI-Anfragen
  Editing/EditorOperations.cs         Werkzeugparameter → Pixeloperation
  Maps/                               IMapViewService, MapProjection, RouteLayers
  Diagnostics/, Logging/, Platform/   DiagnosticsService, FileLoggerProvider, DefaultPlatformInfo
src/PictureGeoExif.Platform.Mac/
  MacPhotoLibraryService.cs           IPhotoLibraryService für Apple Fotos
  PhotoKit/                           IPhotoKitFacade, ObjCPhotoKitFacade, PhotoKitMapping
  Interop/                            ObjC (objc_msgSend), ObjCBlock, CoreFoundation
  MacImageIODecoder.cs, MacKeychainCredentialStore.cs, MacPlatformInfo.cs
src/PictureGeoExif.Platform.Windows/  WindowsCredentialStore
src/PictureGeoExif.Avalonia/
  Program.cs, App.axaml, Bootstrap.cs DI, Logging, Startzeile, --self-test (SelfTest.cs)
  Views/                              MainWindow, ImageEditorWindow, AiMetadataWindow, DiagnosticsWindow, LicenseWindow, RoadMatchSettingsWindow, MetadataTable
  ViewModels/                         MainWindowViewModel, PhotosBrowserViewModel, ImageEntryViewModel, MapViewModel, MetadataPanelViewModel, …
  Controls/                           OsmMapControl, TileCache
  Services/                           UiContext (Dialoge, Picker, Zwischenablage), WindowService, ResourceLocator
  macOS/Info.plist                    Vorlage für PictureGeoExif.app
src/PictureGeoExif.Wpf/               bisherige WPF-Oberfläche (MainWindow, Editor, KI-Fenster, Leaflet-Karte in Resources/)
tests/                                *.Core/Metadata/Application/Platform/Avalonia/Wpf.Tests
scripts/                              build-macos-arm64.sh, run-macos-local.sh, Update-ThirdPartyLicenses.ps1
```

## Aufgabenindex

| Ich will … | Datei / Stelle |
| --- | --- |
| Unterstützte Dateitypen ändern | `Core/Imaging/ImageFormatDetector.cs` (`SupportedExtensions`, `Detect`), Fähigkeiten in `ImageFormat.cs` (`ImageFormatCapabilities`) |
| Bilder laden (Avalonia) | `MainWindowViewModel.LoadFilesAsync`; Picker in `Services/UiServices.cs`; Drag & Drop in `Views/MainWindow.axaml.cs` |
| GPS lesen / schreiben | `Metadata/ImageService.cs` → `ReadGps`, `WriteGpsToImage`, `SaveSingleImage` |
| Metadaten-Tabs befüllen | `Metadata/MetadataInspector.cs`; Anzeige `ViewModels/MetadataPanelViewModel.cs`, `Views/MetadataTable.axaml` |
| Apple-Fotos-Aufrufe | `Platform.Mac/PhotoKit/ObjCPhotoKitFacade.cs` (Objective-C), Logik in `MacPhotoLibraryService.cs`, Zuordnungen in `PhotoKitMapping.cs` |
| Fotos-Berechtigung, Alben, Paging in der UI | `ViewModels/PhotosBrowserViewModel.cs` |
| Fotos-Original laden/analysieren | `Application/Photos/PhotoOriginalCache.cs`; `MainWindowViewModel.MaterializeAsync`, `LoadSelectionAsync` |
| Original exportieren | `MainWindowViewModel.ExportOriginalAsync` / `ImportToFilesAsync` → `IPhotoLibraryService.ExportOriginalAsync` |
| HEIC/RAW-Vorschau | `Platform.Mac/MacImageIODecoder.cs`; Registrierung in `Avalonia/Bootstrap.cs` |
| Karte (Darstellung, Eingabe) | `Avalonia/Controls/OsmMapControl.cs`, Kacheln `TileCache.cs`; Zustand `ViewModels/MapViewModel.cs`; Mathematik `Application/Maps/MapProjection.cs` |
| Trassen / An Wege anlegen | `Core/Geo/RouteBuilder.cs`, `RoadMatcher.cs`; Ebenen `Application/Maps/RouteLayers.cs`; Ablauf `MainWindowViewModel.RebuildRoutes`, `RoadMatchAsync` |
| Kachelanbieter konfigurieren | `AppSettings` (`TileUrl`, `TileAttribution`, `TileAttributionUrl`) |
| Editor-Werkzeug ändern | `Application/Editing/EditorOperations.cs`; Pixel in `Metadata/Editing/EditorSession.cs`; UI `ViewModels/ImageEditorViewModel.cs` |
| KI-Ablauf, Budget, Chat | `Application/Ai/AiMetadataWorkflow.cs`; UI `ViewModels/AiMetadataViewModel.cs` |
| Was an die KI gesendet wird | `Metadata/Ai/AiMetadataService.cs` → `ModelContext`, `Preview`; Prüfung `Application/Privacy/PrivacyGuard.cs` |
| Neuen KI-Anbieter anbinden | `Application/Ai/AiProviders.cs` (`IAiMetadataProvider`, `AiProviderFactory.Create`), `AiTemplate.Validate` |
| API-Schlüssel ablegen | `ICredentialStore`: `Platform.Windows/WindowsCredentialStore.cs`, `Platform.Mac/MacKeychainCredentialStore.cs` |
| Plattformdienst tauschen | `Avalonia/Bootstrap.cs` → `BuildServices` |
| Diagnose / Startlog | `Application/Diagnostics/DiagnosticsService.cs`, `Avalonia/Bootstrap.LogStartup`, `Avalonia/SelfTest.cs` |
| Speicherorte | `Application/AppPaths.cs` |
| macOS-Bundle, Info.plist | `scripts/build-macos-arm64.sh`, `src/PictureGeoExif.Avalonia/macOS/Info.plist` |
| Build- oder Release-Pipeline | `.github/workflows/build.yml`, `.github/workflows/release-on-tag.yml` |
| WPF-Oberfläche | `src/PictureGeoExif.Wpf/*.xaml(.cs)`; Karte `Resources/map.js` |
