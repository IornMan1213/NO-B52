#!/usr/bin/env bash
# Full B-52 build: Blender split + gear + cockpit + atlas -> FBX -> Unity prefab -> .nobp -> plugin. Linux or Windows
# (Git Bash). Paths: see tools/env.sh.
#   bash tools/build_all.sh              everything
#   bash tools/build_all.sh --from-fbx   skip Blender: build from the committed unity/Mods/B52/B52.fbx
#   bash tools/build_all.sh --plugin     only the B52Systems plugin
set -euo pipefail
source "$(dirname "$0")/env.sh"
cd "$ROOT"

MODE="${1:-all}"
SRC="source_assets/bohmerang/source_blend/source/B-52.blend"
OUT="$(winpath "$ROOT/blender/out")"
PARTS="blender/out/B52_parts.blend"
export B52_BUILD_DIR="$(winpath "$ROOT/build")"           # where the Unity builders write the .nobp
mkdir -p build

if [[ "$MODE" == all ]]; then
  if [[ ! -f "$SRC" ]]; then
    echo "Missing $SRC: download bohmerang's model (README, 'Building from source') and unzip it into source_assets/bohmerang/source_blend/"
    exit 1
  fi
  step "Blender split parts"     "EMPTY|DONE"           blender_run "$SRC" --python tools/blender_prep.py -- "$OUT"
  step "Blender face normals"    "NORMALS total"        blender_run "$PARTS" --python tools/blender_normals.py
  step "Blender landing gear"    "GEAR|  [FROL][LR]:"   blender_run "$PARTS" --python tools/blender_gear.py
  step "Blender gear wells"      "WELLS|  [FR][LR]:"    blender_run "$PARTS" --python tools/blender_wells.py
  step "Blender tip gear wells"  "WINGWELLS|  O[LR]:"   blender_run "$PARTS" --python tools/blender_wingwells.py
  step "Blender wing pylons"     "PYLONS|  hsab"        blender_run "$PARTS" --python tools/blender_pylons.py
  step "Blender engine fans"     "FANS"                 blender_run "$PARTS" --python tools/blender_fans.py
  step "Blender hull fix"        "HULLFIX"              blender_run "$PARTS" --python tools/blender_hullfix.py -- "$(winpath "$ROOT/$SRC")"
  step "Blender cockpit"         "COCKPIT"              blender_run "$PARTS" --python tools/blender_cockpit.py
  step "Blender exterior atlas"  "ATLAS"                blender_run "$PARTS" --python tools/blender_atlas.py
  step "Atlas image"             ""                     "$PYTHON" tools/make_atlas.py
  step "Skin damage texture"     "SKIN"                 "$PYTHON" tools/make_skin_detail.py
  if [[ "${B52_SKIP_AO:-0}" != 1 ]]; then              # ~5 min of CPU; B52_SKIP_AO=1 keeps the committed AO map
    step "Blender skin AO"       "AO baked"             blender_run "$PARTS" --python tools/blender_ao.py -- "$(winpath "$ROOT/unity/Mods/B52")"
  fi
  step "Blender FBX export"      "EXPORTED"             blender_run "$PARTS" --python tools/blender_export.py -- "$(winpath "$ROOT/unity/Mods/B52")"
fi

if [[ "$MODE" == all || "$MODE" == --from-fbx ]]; then
  link_unity_sources
  unity_prepare
  step "Unity prefab" "" unity_run B52Tools.B52Builder.Build "$LOGS/unity_prefab_editor.log"
  grep -E "error CS" "$LOGS/unity_prefab_editor.log" | head -5 || true
  grep -E "FAILED|Saved|Hardpoints|Materials|Livery targets" "$B52_UNITY_PROJECT/B52Build.log" || true
  step "Unity mod bundle" "" unity_run B52Tools.B52Builder.BuildMod "$LOGS/unity_mod_editor.log"
  grep -E "\[Blueprinter\] (Built|Missing)|\[B52\] mod built" "$LOGS/unity_mod_editor.log" || true
  ls -la build/*.nobp
fi

step "Plugin" "Build succeeded|warning" dotnet_plugin B52Systems
echo "Done: $(ls build/*.nobp 2>/dev/null | tr '\n' ' ') build/plugin/B52Systems.dll"
