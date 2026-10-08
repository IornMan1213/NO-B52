#!/usr/bin/env bash
# Full B-52 build: Blender split + cockpit -> FBX -> Unity prefab -> .nobp -> plugin. Run from the repo root.
set -e
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
# Tools and the Blueprinter Editor project: override with environment variables if yours live elsewhere.
B="${BLENDER:-/c/Program Files/Blender Foundation/Blender 5.2/blender.exe}"
U="${UNITY:-/c/Program Files/Unity/Hub/Editor/2022.3.62f2/Editor/Unity.exe}"
PROJ="${BLUEPRINTER_PROJECT:-$HOME/BlueprinterProject/Blueprinter-Editor}"     # Blueprinter Editor Unity project
P="$(cygpath -w "$PROJ")"
LOGS="$(dirname "$PROJ")"
export B52_BUILD_DIR="$(cygpath -w "$ROOT/build")"                               # where the builders write the .nobp
OUT="$(cygpath -w "$ROOT/blender/out")"
cd "$ROOT"
echo "== Blender: split parts";   "$B" -b source_assets/bohmerang/source_blend/source/B-52.blend --python tools/blender_prep.py -- "$OUT" 2>&1 | grep -E "EMPTY|Error|Traceback|DONE"
echo "== Blender: landing gear";  "$B" -b blender/out/B52_parts.blend --python tools/blender_gear.py 2>&1 | grep -E "GEAR|  [FROL][LR]:|Error|Traceback"
echo "== Blender: cockpit";       "$B" -b blender/out/B52_parts.blend --python tools/blender_cockpit.py 2>&1 | grep -E "COCKPIT|Error|Traceback"
echo "== Blender: exterior atlas"; "$B" -b blender/out/B52_parts.blend --python tools/blender_atlas.py 2>&1 | grep -E "ATLAS|Error|Traceback"
echo "== Atlas image";         python tools/make_atlas.py
echo "== Blender: FBX export";    "$B" -b blender/out/B52_parts.blend --python tools/blender_export.py -- "$(cygpath -w unity/Mods/B52)" 2>&1 | grep -E "EXPORTED|Error|Traceback"
echo "== Unity: prefab";          "$U" -batchmode -nographics -quit -projectPath "$P" -logFile "$(cygpath -w "$LOGS/build_prefab.log")" -executeMethod B52Tools.B52Builder.Build
grep -E "error CS" "$LOGS/build_prefab.log" | head -5 || true
grep -E "FAILED|Saved" "$PROJ/B52Build.log"
echo "== Unity: mod bundle";      "$U" -batchmode -nographics -quit -projectPath "$P" -logFile "$(cygpath -w "$LOGS/build_mod.log")" -executeMethod B52Tools.B52Builder.BuildMod
grep -E "\[Blueprinter\] (Built|Missing)" "$LOGS/build_mod.log"
echo "== Plugin";                 (cd plugin/B52Systems && dotnet build -c Release -o ../../build/plugin 2>&1 | grep -E " error |Build succeeded" | sort -u)
