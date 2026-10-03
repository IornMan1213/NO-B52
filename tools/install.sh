#!/usr/bin/env bash
# Install the newest B-52 build into Nuclear Option (removes older B-52 .nobp files first).
set -e
G="/c/Program Files (x86)/Steam/steamapps/common/Nuclear Option/BepInEx/plugins/B-52J_Stratofortress"
B="$(dirname "$0")/../build"
mkdir -p "$G"
rm -f "$G"/*.nobp "$G"/B52Systems.dll
NEWEST=$(ls -t "$B"/*.nobp | head -1)
cp "$NEWEST" "$G/"
cp "$B/plugin/B52Systems.dll" "$G/"
ls -la "$G"
