# macOS-Deployment (Phase 1: unsigniert)

Phase 1 liefert ein lokal lauffähiges `.app`-Bundle **ohne** Developer-ID-Signierung, **ohne** Apple-Distribution-/App-Store-Signierung, **ohne** Notarisierung und **ohne** Stapling. Ein kostenpflichtiger Apple-Developer-Account wird nicht vorausgesetzt.

## Begriffe

| Begriff | Was es ist | In Phase 1 |
| --- | --- | --- |
| **Ad-hoc-Signatur** | Signatur ohne Zertifikat (`codesign -s -`). Beweist nur die Integrität, nicht die Herkunft. Auf Apple Silicon ist sie für jeden arm64-Code **Pflicht**. | Das Build-Skript versiegelt das Bundle **lokal ad hoc** (Begründung unten). Abschaltbar mit `--no-adhoc-sign`. |
| **Developer-ID-Signierung** | Signatur mit einem Apple-Zertifikat (kostenpflichtiger Account), Hardened Runtime. Voraussetzung für Weitergabe ohne Gatekeeper-Warnung. | nein |
| **Notarisierung** | Upload an Apple (`notarytool`), automatische Prüfung, Ticket. | nein |
| **Stapling** | Ticket in das Bundle heften (`stapler`), damit die Prüfung offline gelingt. | nein |

### Warum eine Ad-hoc-Signatur des Bundles nötig ist

Das .NET SDK signiert beim Publish nur den `apphost` als **einzelne** Datei. Liegt er als Hauptprogramm in `PictureGeoExif.app/Contents/MacOS`, behandelt macOS ihn als Teil des Bundles und erwartet eine Bundle-Signatur mit versiegelten Ressourcen. Auf dem macOS-CI-Runner (Apple Silicon, macOS 15.7) meldete `codesign --verify` für das unversiegelte Bundle:

```text
PictureGeoExif.app/Contents/MacOS/PictureGeoExif: code has no resources but signature indicates they must be present
```

Folgen ohne Bundle-Signatur: Ein lokal gebautes Bundle startet zwar (kein Quarantäne-Attribut), ein **heruntergeladenes** ZIP meldet Gatekeeper aber als „beschädigt“ – ohne die Möglichkeit „Dennoch öffnen“. Deshalb führt `scripts/build-macos-arm64.sh` nach dem Zusammenbau aus:

```bash
codesign --force --deep --sign - --timestamp=none PictureGeoExif.app
codesign --verify --deep --strict --verbose=2 PictureGeoExif.app
```

Das ist **keine** Developer-ID-Signierung und **keine** Notarisierung; es wird kein Zertifikat und kein Apple-Account benötigt. Schlägt die Signatur fehl, erzeugt das Skript das Paket trotzdem (mit Warnung); die CI prüft die Signatur anschließend streng.

Hinweis: Bei ad-hoc-signierten Apps hängt die Fotos-Freigabe (TCC) an der Code-Identität. Nach jedem Neubau kann macOS erneut fragen bzw. muss die Freigabe unter Systemeinstellungen → Datenschutz & Sicherheit → Fotos neu gesetzt werden. Ebenso kann der Schlüsselbund nach einem Neubau erneut um Zugriff auf gespeicherte API-Schlüssel bitten.

## Installation mit dem Installer (.pkg)

`PictureGeoExif-macOS-arm64.pkg` (CI-Artifact bzw. Release-Asset) installiert `PictureGeoExif.app` nach **/Applications**.

- Doppelklick auf das `.pkg` → Lizenz bestätigen → Installieren (Administratorkennwort erforderlich).
- Der Installer läuft nur auf Apple Silicon (`hostArchitectures="arm64"`) und ab macOS 14.
- Das Paket ist **nicht** mit einem „Developer ID Installer“-Zertifikat signiert. Bei einem heruntergeladenen `.pkg` zeigt macOS deshalb „… kann nicht geöffnet werden, da es von einem nicht verifizierten Entwickler stammt“. Freigabe: Systemeinstellungen → Datenschutz & Sicherheit → „Dennoch öffnen“, oder per Terminal:

  ```bash
  sudo installer -pkg PictureGeoExif-macOS-arm64.pkg -target /
  ```
- Eine Neuinstallation ersetzt `/Applications/PictureGeoExif.app`; Einstellungen, Caches und Logs im Benutzerordner bleiben erhalten.
- Deinstallation: `sudo rm -rf /Applications/PictureGeoExif.app && sudo pkgutil --forget net.brors.picturegeoexif`; optional `~/Library/Application Support/PictureGeoExif`, `~/Library/Caches/PictureGeoExif`, `~/Library/Logs/PictureGeoExif` löschen.
- Für Firmen-Rollouts (MDM) ist später ein mit „Developer ID Installer“ signiertes und notarisiertes Paket nötig (`productsign`, `notarytool`).

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
