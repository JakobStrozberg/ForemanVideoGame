#!/usr/bin/env bash
# Build + run the desktop game straight into a block (skips the menu).
# Usage: dev/scripts/run.sh [BlockName]   (default Block1)
set -euo pipefail
cd "$(dirname "$0")/../../platforms/Desktop"
CREWBOSS_AUTOSTART="${1:-Block1}" dotnet run
