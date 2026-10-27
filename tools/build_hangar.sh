#!/usr/bin/env bash
# Heavy Hangar build: textures -> Blender model -> FBX -> Unity prefab + definition -> .nobp -> plugin.
# Run from anywhere with the game closed (Unity and the game both lock files). Paths: see tools/env.sh.
# Heavy Hangar is also released on its own (NO-HeavyHangar); keep the two copies in step.
set -euo pipefail
source "$(dirname "$0")/env.sh"
cd "$ROOT"
export B52_BUILD_DIR="$(winpath "$ROOT/build")"           # where the Unity builders write the .nobp
mkdir -p build

link_unity_sources
unity_prepare
step "Hangar textures"        ""        "$PYTHON" tools/make_hangar_textures.py
step "Blender hangar"         "HANGAR"  blender_run --factory-startup --python tools/blender_hangar.py -- "$(winpath "$ROOT/unity/Mods/HeavyHangar")"
step "Unity hangar prefab"    ""        unity_run B52Tools.HangarBuilder.Build "$LOGS/unity_hangar_editor.log"
grep -E "error CS" "$LOGS/unity_hangar_editor.log" | head -5 || true
cat "$B52_UNITY_PROJECT/HangarBuild.log" || true
step "Unity hangar bundle"    ""        unity_run B52Tools.HangarBuilder.BuildMod "$LOGS/unity_hangar_mod_editor.log"
grep -E "\[Blueprinter\] (Built|Missing)" "$LOGS/unity_hangar_mod_editor.log" || true
step "Hangar plugin" "Build succeeded|warning" dotnet_plugin HeavyHangar
