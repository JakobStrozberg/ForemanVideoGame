#!/usr/bin/env bash
# Regenerate all generated art: sprite atlases + every block map.
# Usage: dev/scripts/regen.sh            (everything)
#        dev/scripts/regen.sh Block1     (one block's map only)
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
TOOL="dotnet run --project $ROOT/dev/ArtTool -c Release --"
if [ $# -eq 0 ]; then
  $TOOL sprites "$ROOT/game/Content/GameTextures/Generated"
  BLOCKS=$(ls "$ROOT/assets/blocks"/*.json)
else
  BLOCKS="$ROOT/assets/blocks/$1.json"
fi
for def in $BLOCKS; do
  name=$(basename "$def" .json)
  $TOOL compose "$def" "$ROOT/assets/brushes" "$ROOT/game/Content/Maps/$name"
done
