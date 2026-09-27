# macOS: Fehlersuche

Erste Schritte bei jedem Problem:

1. **Hilfe → Diagnostics** öffnen und „Diagnoseinformationen kopieren“ (enthält keine Bildnamen, GPS-Daten oder Fotos-Kennungen).
2. Log ansehen: `~/Library/Logs/PictureGeoExif/PictureGeoExif-JJJJMMTT.log` (Hilfe → Diagnostics → „Log-Ordner öffnen“).
3. Direkt starten, um Konsolenausgaben zu sehen: `./PictureGeoExif.app/Contents/MacOS/PictureGeoExif`
4. Ohne Oberfläche prüfen: `./PictureGeoExif.app/Contents/MacOS/PictureGeoExif --self-test`

| Symptom | Ursache / Prüfung | Abhilfe |
| --- | --- | --- |
| „PictureGeoExif kann nicht geöffnet werden, da Apple es nicht überprüfen kann“ | Heruntergeladenes, nicht notarisiertes Bundle (Quarantäne) | Systemeinstellungen → Datenschutz & Sicherheit → „Dennoch öffnen“ oder `xattr -dr com.apple.quarantine PictureGeoExif.app` (siehe [MacOS-Deployment.md](MacOS-Deployment.md)) |
| „PictureGeoExif ist beschädigt und kann nicht geöffnet werden“ | Quarantäne + ungültige Signatur, oft nach Entpacken mit einem Werkzeug, das Symlinks/Rechte verliert | Mit `ditto -x -k` oder Finder entpacken; `xattr -dr com.apple.quarantine …`; Bundle mit `scripts/build-macos-arm64.sh` (Ad-hoc-Signatur ist Standard) neu bauen; `codesign --verify --deep --strict PictureGeoExif.app` |
| Start bricht sofort ab, Terminal zeigt `Killed: 9` | arm64-Code ohne gültige Signatur (z. B. Datei in `Contents/MacOS` nachträglich geändert) | `codesign --verify --deep --strict PictureGeoExif.app`; mit dem Build-Skript neu bauen (signiert ad hoc) |
| `Bad CPU type in executable` | x64-Build auf Apple Silicon ohne Rosetta, oder falscher RID | Mit `-r osx-arm64` bauen; `file Contents/MacOS/PictureGeoExif` muss `arm64` zeigen |
| `DllNotFoundException: libAvaloniaNative` / `libSkiaSharp` | Native Bibliotheken fehlen (Single-File, Trimming oder unvollständige Kopie) | Nur mit dem Build-Skript publishen (kein Single-File, kein Trimming); `ls Contents/MacOS/*.dylib` |
| Fenster erscheint nicht, Log: „The application has not been started with an application bundle“ o. Ä. | Start außerhalb des Bundles, fehlende Info.plist | `open PictureGeoExif.app`; `plutil -lint Contents/Info.plist` |
| Apple Fotos: „Nicht verfügbar auf dieser Plattform“ | Photos.framework nicht ladbar (nicht macOS) | nur unter macOS verfügbar; Dateien funktionieren weiter |
| Apple Fotos: Hinweis „Zugriff kann nur angefordert werden, wenn PictureGeoExif als App gestartet wird“ | Start per `dotnet run` oder ohne Info.plist | `scripts/run-macos-local.sh --bundle` |
| Kein Systemdialog beim Klick auf „Zugriff anfordern“ | Status ist nicht mehr „Noch nicht autorisiert“ (bereits abgelehnt) | Systemeinstellungen → Datenschutz & Sicherheit → Fotos → PictureGeoExif aktivieren; zum Zurücksetzen: `tccutil reset Photos net.brors.picturegeoexif` |
| Freigabe gilt für „Terminal“ statt PictureGeoExif | Direkter Start aus dem Terminal | Mit `open` starten |
| Nach Neubau fragt macOS erneut nach Fotos-Zugriff oder Schlüsselbund | Ad-hoc-Signatur ändert sich mit jedem Build | erwartet in Phase 1; mit Developer-ID-Signatur stabil |
| „Eingeschränkt erlaubt“, es fehlen Fotos | Nutzer hat nur ausgewählte Fotos freigegeben (Limited Library) | In den Systemeinstellungen „Voller Zugriff“ wählen oder Auswahl erweitern |
| „Asset konnte nicht geladen werden. Grund: Original derzeit nicht verfügbar (iCloud nicht erreichbar).“ | Original nur in iCloud, offline oder iCloud-Fehler | Netzwerk prüfen, „Erneut versuchen“; in Fotos → Einstellungen → iCloud ggf. „Originale auf diesen Mac laden“ |
| HEIC/RAW ohne Vorschau | Kein Decoder für das Format (unter Windows/Linux) | Unter macOS rendert ImageIO HEIC/DNG/RAW; Metadaten werden überall gelesen |
| „Das Format HEIC kann hier keine GPS-Metadaten speichern“ | GPS-Schreiben nur für JPEG/PNG/TIFF | XMP-Sidecar (KI-Fenster) nutzen oder als JPEG exportieren |
| Karte bleibt grau | Offline oder Kachelserver blockiert (HTTP 403/429) | Netzwerk prüfen; Kachelanbieter in `~/Library/Application Support/PictureGeoExif/settings.json` (`TileUrl`) ändern; Cache: `~/Library/Caches/PictureGeoExif/map-tiles` |
| Stempel: „Keine Systemschrift zum Stempeln gefunden“ | Keine der Schriften Segoe UI/Arial/Helvetica vorhanden | Unter macOS vorhanden; sonst Arial oder DejaVu Sans installieren |
| KI: „Kein API-Schlüssel“ | Schlüssel nicht im Schlüsselbund | Im KI-Fenster speichern (macOS-Schlüsselbund, Dienst „PictureGeoExif“) oder `OPENAI_API_KEY`/`GEMINI_API_KEY`/`AZURE_OPENAI_API_KEY` setzen (bei `open` gelten Umgebungsvariablen der Shell nicht – `launchctl setenv` oder direkter Start) |
| Einstellungen zurücksetzen | – | App beenden, `~/Library/Application Support/PictureGeoExif/settings.json` löschen |

## Nützliche Befehle

```bash
file PictureGeoExif.app/Contents/MacOS/PictureGeoExif         # Architektur
codesign -dv --verbose=4 PictureGeoExif.app/Contents/MacOS/PictureGeoExif
plutil -p PictureGeoExif.app/Contents/Info.plist
log stream --predicate 'process == "PictureGeoExif"' --level debug    # Systemlog live
log show --last 10m --predicate 'subsystem == "com.apple.TCC"' | grep -i photos   # Datenschutzentscheidungen
```
