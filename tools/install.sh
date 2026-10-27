#!/usr/bin/env bash
# Install the current B-52 and Heavy Hangar builds into Nuclear Option (removes older versions first).
# The game must be closed. Usage: tools/install.sh [b52|hangar|all]  (default all). Game folder: NO_GAME_DIR (env.sh).
set -euo pipefail
source "$(dirname "$0")/env.sh"

if game_running; then echo "Close Nuclear Option first (it locks the mod files)."; exit 1; fi
P="$NO_GAME_DIR/BepInEx/plugins"
[[ -d "$P" ]] || { echo "No BepInEx plugins folder at $P (set NO_GAME_DIR, and install BepInEx 5 first)"; exit 1; }
B="$ROOT/build"
WHAT="${1:-all}"

if [[ "$WHAT" == b52 || "$WHAT" == all ]]; then
  V="$(mod_version)"
  NOBP="$B/B-52J Stratofortress_$V.nobp"
  [[ -f "$NOBP" && -f "$B/plugin/B52Systems.dll" ]] || { echo "Build $V first (bash tools/build_all.sh): missing $NOBP or the plugin"; exit 1; }
  G="$P/B-52J_Stratofortress"; mkdir -p "$G"
  rm -f "$G"/*.nobp "$G"/B52Systems.dll
  cp "$NOBP" "$G/"
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
