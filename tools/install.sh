#!/usr/bin/env bash
# Install the newest B-52 and Heavy Hangar builds into Nuclear Option (removes older versions first).
# The game must be closed. Usage: tools/install.sh [b52|hangar|all]  (default all)
set -e
P="/c/Program Files (x86)/Steam/steamapps/common/Nuclear Option/BepInEx/plugins"
B="$(dirname "$0")/../build"
WHAT="${1:-all}"

if [[ "$WHAT" == b52 || "$WHAT" == all ]]; then
  G="$P/B-52J_Stratofortress"; mkdir -p "$G"
  rm -f "$G"/*.nobp "$G"/B52Systems.dll
  cp "$(ls -t "$B"/B-52J\ Stratofortress_*.nobp | head -1)" "$G/"
  cp "$B/plugin/B52Systems.dll" "$G/"
  ls -la "$G"
fi
if [[ "$WHAT" == hangar || "$WHAT" == all ]] && ls "$B"/Heavy\ Hangar_*.nobp >/dev/null 2>&1; then
  H="$P/HeavyHangar"; mkdir -p "$H"
  rm -f "$H"/*.nobp "$H"/HeavyHangar.dll
  cp "$(ls -t "$B"/Heavy\ Hangar_*.nobp | head -1)" "$H/"
  cp "$B/plugin/HeavyHangar.dll" "$H/"
  ls -la "$H"
fi
