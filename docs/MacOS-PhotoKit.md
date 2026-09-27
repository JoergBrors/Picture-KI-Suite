# Apple Fotos (PhotoKit) unter macOS

## Architektur

```text
Avalonia (PhotosBrowserViewModel, MainWindowViewModel)
   |
IPhotoLibraryService                      (Core, plattformneutral)
   |
MacPhotoLibraryService                    (Platform.Mac: Status-Mapping, Paging, Export, Fehlerabbildung)
   |
IPhotoKitFacade                           (1:1-Rohzugriff, im Test durch FakePhotoKitFacade ersetzt)
   |
ObjCPhotoKitFacade                        (Objective-C-Laufzeit: objc_msgSend, Blocks)
   |
Photos.framework: PHPhotoLibrary, PHAsset, PHAssetCollection, PHFetchOptions, PHFetchResult,
                  PHImageManager, PHAssetResource, PHAssetResourceManager
   |
Apple Fotos
```

Es wird **ausschließlich** die öffentliche PhotoKit-API verwendet. Keine direkte Manipulation der `.photoslibrary`, keine SQLite-Zugriffe, keine Dateisystemzugriffe in der Mediathek. Phase 1 ist **read-only**: Die App ändert, löscht oder ergänzt nichts in Apple Fotos.

## Entscheidung: Objective-C-Laufzeit statt Microsoft.macOS-Bindings

Geprüft wurde der Einsatz der offiziellen .NET-Bindings (`Microsoft.macOS`, TFM `net10.0-macos`, Namespace `Photos`). Entscheidung für Phase 1: **nicht** verwenden, stattdessen dieselben PhotoKit-Klassen über die Objective-C-Laufzeit (`libobjc`) aus einer normalen `net10.0`-Bibliothek ansprechen.

| Kriterium | Microsoft.macOS-Bindings | Objective-C-Laufzeit (gewählt) |
| --- | --- | --- |
| Einbindung in Avalonia | App müsste selbst `net10.0-macos` sein; Avalonia.Native verwaltet `NSApplication`/Run-Loop selbst – Kombination nicht offiziell unterstützt | normale Avalonia-App `net10.0` / `osx-arm64`, wie von Avalonia dokumentiert |
| Build | macOS-Workload + Xcode zwingend; Bindings-Bundle wird vom SDK erzeugt | nur .NET SDK; `.app` per Skript (wie in der Aufgabenstellung vorgesehen) |
| Build unter Windows/Linux | nicht möglich | möglich (Cross-Publish `osx-arm64`, Tests laufen überall) |
| Typsicherheit | hoch | geringer – deshalb dünne Fassade, alle Logik in getestetem C# |
| APIs | PhotoKit | dieselben PhotoKit-Klassen und -Selektoren |

Die Fassade `IPhotoKitFacade` ist bewusst so geschnitten, dass später eine Implementierung auf Basis von `Microsoft.macOS` (z. B. in einem eigenen `net10.0-macos`-Host) sie ersetzen kann, ohne Service, ViewModels oder Tests zu ändern.

## Verwendete APIs

| Zweck | API |
| --- | --- |
| Status | `+[PHPhotoLibrary authorizationStatusForAccessLevel:]` mit `PHAccessLevelReadWrite` |
| Anfrage | `+[PHPhotoLibrary requestAuthorizationForAccessLevel:handler:]` (Block) |
| Alben | `+[PHAssetCollection fetchAssetCollectionsWithType:subtype:options:]` (Alben und intelligente Alben), `localIdentifier`, `localizedTitle`, `assetCollectionSubtype`, `estimatedAssetCount` |
| Assets | `+[PHAsset fetchAssetsWithOptions:]`, `+[PHAsset fetchAssetsInAssetCollection:options:]`, `+[PHAsset fetchAssetsWithLocalIdentifiers:options:]`, `PHFetchOptions` mit `sortDescriptors` (`creationDate`) und `predicate` (`mediaType`, `favorite`), `PHFetchResult count/objectAtIndex:` |
| Eigenschaften | `localIdentifier`, `mediaType`, `mediaSubtypes`, `pixelWidth`, `pixelHeight`, `creationDate`, `modificationDate`, `isFavorite`, `location.coordinate` |
| Ressourcen | `+[PHAssetResource assetResourcesForAsset:]`: `type`, `originalFilename`, `uniformTypeIdentifier` |
| Thumbnail | `-[PHImageManager requestImageForAsset:targetSize:contentMode:options:resultHandler:]` mit `PHImageRequestOptions` (`synchronous` im Hintergrund-Thread, `deliveryMode` HighQuality, `resizeMode` Fast, `networkAccessAllowed = NO`); `NSImage` → `NSBitmapImageRep` → JPEG |
| Original | `-[PHAssetResourceManager requestDataForAssetResource:options:dataReceivedHandler:completionHandler:]` mit `PHAssetResourceRequestOptions` (`networkAccessAllowed = YES`, `progressHandler`), `cancelDataRequest:` |
| Info.plist-Prüfung | `-[NSBundle objectForInfoDictionaryKey:@"NSPhotoLibraryUsageDescription"]` |

Blocks (Completion-, Daten-, Fortschritts-Handler) werden nach dem Clang-Block-ABI als Stack-Blocks im nativen Speicher erzeugt; PhotoKit kopiert sie bei Bedarf selbst. Der Kontext ist eine Registry-ID statt eines GC-Handles, damit verspätete Rückrufe nie freigegebenen Speicher erreichen. Jede öffentliche Methode läuft in einem eigenen Autorelease-Pool und gibt nur verwaltete Kopien zurück.

## Berechtigungs-Flow

```text
Start ──► Status lesen (fragt NICHT nach)
             │
   NotDetermined ──► UI: "Apple Fotos Zugriff · Status: Noch nicht autorisiert · [Zugriff anfordern]"
             │            └─ Klick ──► Info.plist-Prüfung ──► Systemdialog ──► Status
   Authorized / Limited ──► "[Fotomediathek öffnen]" ──► Alben, Fotos
   Denied / Restricted ──► Hinweis auf Systemeinstellungen; Dateien öffnen funktioniert weiter
   Unavailable (Windows/Linux) ──► Hinweis; Dateien öffnen funktioniert weiter
```

| PHAuthorizationStatus | Wert | App |
| --- | --- | --- |
| notDetermined | 0 | `NotDetermined` – Anfrage möglich |
| restricted | 1 | `Restricted` |
| denied | 2 | `Denied` |
| authorized | 3 | `Authorized` |
| limited | 4 | `Limited` – nur freigegebene Fotos sichtbar |
| unbekannt | – | `Denied` (sicherer Standard) |

- Zugriff wird **nur auf Benutzeraktion** angefordert.
- Läuft der Prozess nicht aus `PictureGeoExif.app` (z. B. `dotnet run`), fehlt `NSPhotoLibraryUsageDescription` im Haupt-Bundle. macOS würde den Prozess bei einer Anfrage beenden; die App fragt deshalb nicht an und zeigt einen Hinweis.
- Startet man die App aus dem Terminal (`./PictureGeoExif.app/Contents/MacOS/PictureGeoExif`), ordnet macOS die Freigabe dem Terminal zu. Zum Testen des Flows `open PictureGeoExif.app` verwenden.
- Info.plist enthält nur `NSPhotoLibraryUsageDescription` (Lesen). `NSPhotoLibraryAddUsageDescription` ist nicht nötig, weil nichts hinzugefügt wird.

## Asset-Zugriff und Performance

- Listen sind paginiert (120 je Seite, „Weitere Fotos laden“), sortiert nach Aufnahmedatum absteigend, nur Bilder (`mediaType == 1`).
- „Mediathek“ und „Favoriten“ sind feste Einträge; Benutzeralben, geteilte Alben und intelligente Alben folgen. „Ausgeblendet“ und „Zuletzt gelöscht“ werden nie gelistet.
- Thumbnails (256 px) laden im Hintergrund, höchstens 4 parallele PhotoKit-Anfragen, abbrechbar beim Albumwechsel. Listen laden nie ein Original und lösen nie einen iCloud-Download aus.
- Das Original wird erst bei **Auswahl** eines Fotos geladen.

## Original-Export

```text
PHAsset → Ressourcen → Auswahl Original → PHAssetResourceManager (Datenstrom) → temporäre Datei → atomar an Zielpfad
```

- Auswahl der Ressource (`PhotoResourceSelection.SelectOriginal`): `Photo` (Original) → `AlternatePhoto` (z. B. RAW eines RAW+JPEG-Paars) → `AdjustmentBasePhoto` → nur wenn nichts anderes existiert `FullSizePhoto` (bearbeitete Fassung).
- Unterstützt werden alle Bildressourcen, die PhotoKit liefert: JPEG, HEIC/HEIF, PNG, TIFF, DNG und RAW-Originale – es werden die Bytes des Originals geschrieben, nicht neu kodiert.
- Zieldatei wird **nie überschrieben** (`DestinationExists`), temporäre Dateien werden bei Fehler/Abbruch entfernt.
- „Original exportieren …“: Sichern-Dialog; „In Dateien übernehmen“: Export in `<Ausgabeordner>/Apple Fotos/JJJJMMTT/` und Aufnahme in die Dateiliste (dann Bearbeiten, GPS-Kopie, KI möglich).

## iCloud-Verhalten

- Ein `PHAsset` kann existieren, obwohl das Original nur in iCloud liegt („Mac-Speicher optimieren“).
- Beim Laden des Originals ist `networkAccessAllowed` gesetzt; der Fortschritt erscheint als „Original wird aus iCloud geladen … 42 %“.
- Abbruch: Auswahl eines anderen Fotos bricht die laufende Anfrage ab (`cancelDataRequest:`).
- Offline oder iCloud-Fehler → `CloudUnavailable`: „Asset konnte nicht geladen werden. Grund: Original derzeit nicht verfügbar (iCloud nicht erreichbar).“ mit **[Erneut versuchen]**. Technische Details (Domain, Code) stehen im Log.

## Fehlerfälle

| Fehler | Abbildung | Anzeige |
| --- | --- | --- |
| Benutzer-/Programmabbruch (`PHPhotosErrorUserCancelled` 3072) | `Cancelled` | keine Meldung |
| Netzwerk nötig / Netzwerkfehler (3164, 3169, `NSURLErrorDomain`, `CloudPhotoLibraryErrorDomain`, `CKErrorDomain`) | `CloudUnavailable` | iCloud nicht erreichbar, erneut versuchen |
| Kennung nicht gefunden (3201) | `AssetNotFound` | Foto nicht mehr vorhanden |
| Ressource fehlt/ungültig (3302, 3303) | `NoOriginalResource` | kein Original verfügbar |
| zu wenig Speicher (3305), Schreibfehler | `IoError` | Datei konnte nicht geschrieben werden |
| Zugriff verweigert/eingeschränkt (3310, 3311) | `NotAuthorized` | kein Zugriff |

Ein Fehler bei einem einzelnen Asset beendet nie die Anwendung.

## Datenschutz

- PhotoKit-Zugriff nur auf Benutzeraktion; Status lesen fragt nicht nach.
- Originale landen nur im Cache der App (`~/Library/Caches/PictureGeoExif/photos-originals`, Ordnername = Hash, nicht die PhotoKit-Kennung). Leeren über Hilfe → Diagnostics.
- Keine automatische Übertragung an KI-Anbieter. Fotos-Assets müssen zuerst als Datei übernommen werden und laufen dann durch dieselbe Datenschutzprüfung (`PrivacyGuard`) wie jede Datei: keine Originalbilder (nur verkleinerte Vorschau ohne Metadaten), keine exakten GPS-Koordinaten, keine Dateipfade, keine PhotoKit-Kennungen.
- Das Log enthält keine Dateinamen, Koordinaten oder Asset-Kennungen.
