# macOS-Deployment (Phase 1: unsigniert)

Phase 1 liefert ein lokal lauffähiges `.app`-Bundle **ohne** Developer-ID-Signierung, **ohne** Apple-Distribution-/App-Store-Signierung, **ohne** Notarisierung und **ohne** Stapling. Ein kostenpflichtiger Apple-Developer-Account wird nicht vorausgesetzt.

## Begriffe

| Begriff | Was es ist | In Phase 1 |
| --- | --- | --- |
| **Ad-hoc-Signatur** | Signatur ohne Zertifikat (`codesign -s -`). Beweist nur die Integrität der Datei, nicht die Herkunft. Auf Apple Silicon ist sie für jeden arm64-Code **Pflicht**. | Das .NET SDK signiert den `apphost` beim Publish automatisch ad hoc; die mitgelieferten dylibs sind von ihren Herstellern signiert. **Kein zusätzlicher Schritt nötig.** Optional `--adhoc-sign` für das ganze Bundle. |
| **Developer-ID-Signierung** | Signatur mit einem Apple-Zertifikat (kostenpflichtiger Account), Hardened Runtime. Voraussetzung für Weitergabe ohne Gatekeeper-Warnung. | nein |
| **Notarisierung** | Upload an Apple (`notarytool`), automatische Prüfung, Ticket. | nein |
| **Stapling** | Ticket in das Bundle heften (`stapler`), damit die Prüfung offline gelingt. | nein |

### Warum keine automatische Ad-hoc-Signatur des Bundles

Sie ist technisch nicht zwingend: Der Kernel verlangt eine gültige Signatur für jeden **ausführbaren arm64-Code**. Das erfüllt der vom SDK signierte `apphost`; das Bundle selbst (Info.plist, Ressourcen) muss für einen lokalen Start nicht versiegelt sein. `scripts/build-macos-arm64.sh` prüft auf macOS die Signatur des `apphost` und bricht mit Hinweis ab, falls sie fehlt.

`--adhoc-sign` ist sinnvoll, wenn

- Dateien in `Contents/MacOS` nach dem Publish verändert wurden (z. B. ein Patch einer dylib) und macOS mit „Killed: 9“ oder „code signature invalid“ reagiert,
- ein Werkzeug eine vollständig versiegelte App erwartet (`codesign --verify --deep --strict`).

Hinweis: Bei ad-hoc-signierten Apps hängt die Fotos-Freigabe (TCC) an der Code-Identität. Nach jedem Neubau kann macOS erneut fragen bzw. muss die Freigabe unter Systemeinstellungen → Datenschutz & Sicherheit → Fotos neu gesetzt werden. Ebenso kann der Schlüsselbund nach einem Neubau erneut um Zugriff auf gespeicherte API-Schlüssel bitten.

## Lokaler Start

```bash
scripts/build-macos-arm64.sh --skip-tests
open artifacts/macos-arm64/PictureGeoExif.app                       # normaler Start (empfohlen)
./artifacts/macos-arm64/PictureGeoExif.app/Contents/MacOS/PictureGeoExif   # direkter Start zur Fehlerdiagnose (Log im Terminal)
scripts/run-macos-local.sh --bundle | --direct                       # dasselbe per Skript
```

- `open` startet die App als eigenständige Anwendung; die Fotos-Freigabe gehört dann zu **PictureGeoExif**.
- Beim direkten Start ordnet macOS die Fotos-Freigabe dem **Terminal** zu. Für die Fehlersuche ideal (Konsolenlog), für den Berechtigungs-Test `open` verwenden.
- `dotnet run` (Debug) läuft ohne Bundle: Fotos-Zugriff kann dort nicht angefordert werden (die App zeigt einen Hinweis statt abzustürzen).

## Gatekeeper-Verhalten

Lokal gebaute Apps tragen kein Quarantäne-Attribut und starten direkt. Wird das ZIP **heruntergeladen** (z. B. GitHub-Actions-Artifact oder Release), setzt macOS das Attribut `com.apple.quarantine`, und Gatekeeper blockiert die unsignierte/nicht notarisierte App:

> „PictureGeoExif“ kann nicht geöffnet werden, da Apple es nicht auf Schadsoftware überprüfen kann.

Optionen (nur für vertrauenswürdige, selbst gebaute Artefakte):

1. **Systemeinstellungen → Datenschutz & Sicherheit →** „PictureGeoExif wurde blockiert …“ → **Dennoch öffnen**, danach erneut starten und bestätigen. (Seit macOS 15 ist das Rechtsklick-„Öffnen“ ohne diesen Umweg entfallen.)
2. Terminal:

   ```bash
   xattr -dr com.apple.quarantine /Applications/PictureGeoExif.app
   ```

Das ZIP mit `ditto -x -k PictureGeoExif-macOS-arm64.zip /Applications` oder per Doppelklick im Finder entpacken.

## Später: Signierung und Notarisierung

Die Architektur ist darauf vorbereitet; alle Schritte lassen sich an `scripts/build-macos-arm64.sh` nach Schritt 9 anhängen.

1. **Zertifikat:** „Developer ID Application“ im Apple-Developer-Account, im Schlüsselbund des Build-Rechners bzw. als verschlüsseltes CI-Secret.
2. **Entitlements** (Hardened Runtime für .NET), z. B. `macOS/PictureGeoExif.entitlements`:

   ```xml
   <key>com.apple.security.cs.allow-jit</key><true/>
   <key>com.apple.security.cs.allow-unsigned-executable-memory</key><true/>
   <key>com.apple.security.cs.disable-library-validation</key><true/>
   <key>com.apple.security.personal-information.photos-library</key><true/>
   ```

   (`photos-library` ist nur für App-Sandbox/Mac App Store nötig.)
3. **Signieren** von innen nach außen: jede `*.dylib` und das Programm in `Contents/MacOS`, danach das Bundle:

   ```bash
   codesign --force --timestamp --options runtime --entitlements macOS/PictureGeoExif.entitlements \
            --sign "Developer ID Application: <Name> (<TEAMID>)" PictureGeoExif.app
   ```
4. **Notarisieren:** `ditto -c -k --keepParent PictureGeoExif.app app.zip` → `xcrun notarytool submit app.zip --keychain-profile <Profil> --wait`
5. **Stapling:** `xcrun stapler staple PictureGeoExif.app`, danach das ZIP/DMG erneut erzeugen.
6. **Prüfen:** `spctl --assess --type execute -vv PictureGeoExif.app`

Für den Mac App Store wären zusätzlich App-Sandbox, Provisioning-Profile und ein Installer-Paket nötig; das ist nicht Ziel von Phase 1.
