# Dokumentation

Einstieg für Entwicklung, Betrieb und Nutzung von PictureGeoExif. Die Projektübersicht mit Schnellstart steht im [Root-README](../README.md).

| Dokument | Für wen | Inhalt |
| --- | --- | --- |
| [Avalonia-Architektur](Avalonia-Architektur.md) | Entwicklung | Neue Solution-Struktur, Schichten, Quellen-Konzept, Fotos-Pipeline, Karte, DI, Tests |
| [Avalonia-Migration-Analyse](Avalonia-Migration-Analyse.md) | Entwicklung | Analyse des WPF-Ausgangsstands, Abhängigkeiten, Risiken, Reihenfolge |
| [macOS: PhotoKit](MacOS-PhotoKit.md) | Entwicklung, Datenschutz | Apple-Fotos-Adapter, APIs, Berechtigungen, Export, iCloud, Fehlerfälle |
| [macOS: Build](MacOS-Build.md) | Entwicklung | SDK, Versionen, Build-/Publish-Befehle, Bundle-Struktur, CI |
| [macOS: Deployment](MacOS-Deployment.md) | Entwicklung, IT | Unsigniertes Deployment, Gatekeeper, Start, spätere Signierung/Notarisierung |
| [macOS: Fehlersuche](MacOS-Troubleshooting.md) | Anwender, IT | Symptome, Ursachen, Abhilfe |
| [macOS: Implementierungsbericht](MacOS-Implementation-Report.md) | alle | Umsetzungsstand Phase 1, Testergebnisse, Einschränkungen |
| [Code-Wegweiser](Code-Wegweiser.md) | Entwicklung | Wo liegt was? Aufgabenindex („Ich will X ändern → Datei Y“) |
| [Architektur (WPF)](Architektur.md) | Entwicklung | Datenflüsse, Sicherheits- und Integritätsregeln (gelten für beide Oberflächen), WPF-Details |
| [Entwicklung](Entwicklung.md) | Entwicklung | Build, Tests, CI/Release, Konventionen, Lizenzpflege |
| [Bibliotheken (Services)](Services.md) | Entwicklung | Kurzreferenz der wichtigsten Klassen |
| [Benutzerhandbuch](Benutzerhandbuch.md) | Anwender | Hauptfenster, Karte, Editor, KI-Metadaten |
| [Betrieb und Unternehmenseinsatz](Betrieb-und-Unternehmenseinsatz.md) | IT, Datenschutz, Management | Installation, Konfiguration, Speicherorte, Lizenz- und DSGVO-Checkliste |
| [GUI & AI Update Plan](GUI-AI-Update-Plan.md) | Entwicklung | Frühere Modernisierung (WPF), Anforderungen und Stand |
| [KI-Metadaten-Konzept](AI-Metadata-Konzept.md) | Entwicklung | Fachkonzept für KI-Analyse, Batch, Chat und JSON-Vorlagen |
| [Beispielvorlagen](examples/) | Entwicklung, Power-User | `ai-metadata.template.json`, Antwortschema, fiktive Beispielantwort |

Weitere Dateien im Repository-Stamm:

- [THIRD-PARTY-LICENSES.md](../THIRD-PARTY-LICENSES.md): alle Fremdkomponenten mit Lizenz und Einsatz im Unternehmen
- [changelog.md](../changelog.md): Änderungsprotokoll
