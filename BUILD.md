# Bauen & Hochladen

## Voraussetzungen
- .NET SDK 8 (oder neuer)

## Bauen
```bash
dotnet build -c Release
```
Die DLL landet in `bin/Release/netstandard2.1/HideAndSeekMonster.dll`.
Mit `-p:GameDir="C:\Program Files (x86)\Steam\steamapps\common\REPO"` wird sie direkt
nach `BepInEx\plugins\HideAndSeekMonster` kopiert.

## Thunderstore-Paket
```bash
./pack.sh
```
Erstellt `dist/HideAndSeekMonster-<version>.zip`. Auf https://thunderstore.io/c/repo/create/ hochladen.

Vor jedem Upload: Version in `thunderstore/manifest.json` **und** `src/Plugin.cs` (+ `HideAndSeek.csproj`) erhöhen
und `thunderstore/CHANGELOG.md` ergänzen. Die BepInExPack-Version in `manifest.json` mit der aktuellen auf
Thunderstore abgleichen.

## Automatisch veröffentlichen (GitHub Action)
`.github/workflows/publish.yml` lädt die Mod automatisch auf Thunderstore hoch (Team **Napguin**), sobald auf `main`
eine neue Version gepusht wird (`thunderstore/manifest.json` + passende `release/HideAndSeekMonster-<version>.zip`).
Ist die Version schon auf Thunderstore, passiert nichts. Manuell starten: GitHub → Actions → "Publish to Thunderstore" → Run workflow.

Benötigt das Repository-Secret `TSTORE_TOKEN` (Service-Account-Token des Thunderstore-Teams).
