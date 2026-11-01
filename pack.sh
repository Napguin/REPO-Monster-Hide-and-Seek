#!/usr/bin/env bash
# Builds the mod and creates the Thunderstore zip in dist/.
# Usage: ./pack.sh            (uses NuGet packages)
#        ./pack.sh /path/libs (uses a local folder with BepInEx + game DLLs)
set -euo pipefail
cd "$(dirname "$0")"
if [ $# -ge 1 ]; then
  dotnet build -c Release -p:LocalLibs="$1"
else
  dotnet build -c Release
fi
VERSION=$(python3 -c "import json;print(json.load(open('thunderstore/manifest.json'))['version_number'])")
STAGE=dist/stage
rm -rf "$STAGE" && mkdir -p "$STAGE/BepInEx/plugins/HideAndSeekMonster"
cp thunderstore/manifest.json thunderstore/icon.png thunderstore/README.md thunderstore/CHANGELOG.md "$STAGE/"
cp bin/Release/netstandard2.1/HideAndSeekMonster.dll "$STAGE/BepInEx/plugins/HideAndSeekMonster/"
OUT="dist/HideAndSeekMonster-$VERSION.zip"
rm -f "$OUT"
(cd "$STAGE" && zip -qr "../$(basename "$OUT")" .)
echo "Created $OUT"
