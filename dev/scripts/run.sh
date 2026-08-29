#!/usr/bin/env bash
# Build + run the desktop game straight into a block (skips the menu).
# Usage: dev/scripts/run.sh [BlockName] [--phone]
#   --phone   landscape iPhone aspect window (19.5:9) to preview mobile framing
set -euo pipefail
BLOCK=Block1; WINDOW=
for a in "$@"; do
  case "$a" in
    --phone) WINDOW=phone ;;
    *) BLOCK="$a" ;;
  esac
done
cd "$(dirname "$0")/../../platforms/Desktop"
CREWBOSS_AUTOSTART="$BLOCK" CREWBOSS_WINDOW="$WINDOW" dotnet run
