#!/usr/bin/env bash
# Full B-52 build: Blender split + cockpit -> FBX -> Unity prefab -> .nobp -> plugin. Run from the repo root.
set -e
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
B="/c/Program Files/Blender Foundation/Blender 5.2/blender.exe"
U="/c/Program Files/Unity/Hub/Editor/2022.3.62f2/Editor/Unity.exe"
P='C:\Users\jayea\BlueprinterProject\Blueprinter-Editor'
OUT="$(cygpath -w "$ROOT/blender/out")"
cd "$ROOT"
echo "== Blender: split parts";   "$B" -b source_assets/bohmerang/source_blend/source/B-52.blend --python tools/blender_prep.py -- "$OUT" 2>&1 | grep -E "EMPTY|Error|Traceback|DONE"
echo "== Blender: cockpit";       "$B" -b blender/out/B52_parts.blend --python tools/blender_cockpit.py 2>&1 | grep -E "COCKPIT|Error|Traceback"
echo "== Blender: exterior atlas"; "$B" -b blender/out/B52_parts.blend --python tools/blender_atlas.py 2>&1 | grep -E "ATLAS|Error|Traceback"
echo "== Atlas image";         python tools/make_atlas.py
echo "== Blender: FBX export";    "$B" -b blender/out/B52_parts.blend --python tools/blender_export.py -- "$(cygpath -w unity/Mods/B52)" 2>&1 | grep -E "EXPORTED|Error|Traceback"
echo "== Unity: prefab";          "$U" -batchmode -nographics -quit -projectPath "$P" -logFile 'C:\Users\jayea\BlueprinterProject\build_prefab.log' -executeMethod B52Tools.B52Builder.Build
grep -E "error CS" /c/Users/jayea/BlueprinterProject/build_prefab.log | head -5 || true
grep -E "FAILED|Saved" /c/Users/jayea/BlueprinterProject/Blueprinter-Editor/B52Build.log
echo "== Unity: mod bundle";      "$U" -batchmode -nographics -quit -projectPath "$P" -logFile 'C:\Users\jayea\BlueprinterProject\build_mod.log' -executeMethod B52Tools.B52Builder.BuildMod
grep -E "\[Blueprinter\] (Built|Missing)" /c/Users/jayea/BlueprinterProject/build_mod.log
echo "== Plugin";                 (cd plugin/B52Systems && dotnet build -c Release -o ../../build/plugin 2>&1 | grep -E " error |Build succeeded" | sort -u)
