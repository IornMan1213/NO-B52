#!/usr/bin/env bash
# Heavy Hangar build: textures -> Blender model -> FBX -> Unity prefab + definition -> .nobp -> plugin.
# Run from the repo root with the game closed (Unity and the game both lock files).
set -e
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
# Tools and the Blueprinter Editor project: override with environment variables if yours live elsewhere.
B="${BLENDER:-/c/Program Files/Blender Foundation/Blender 5.2/blender.exe}"
U="${UNITY:-/c/Program Files/Unity/Hub/Editor/2022.3.62f2/Editor/Unity.exe}"
PROJ="${BLUEPRINTER_PROJECT:-$HOME/BlueprinterProject/Blueprinter-Editor}"     # Blueprinter Editor Unity project
P="$(cygpath -w "$PROJ")"
LOGS="$(dirname "$PROJ")"
export B52_BUILD_DIR="$(cygpath -w "$ROOT/build")"                               # where the builders write the .nobp
cd "$ROOT"
# The Unity project sees the mod source through a junction, like Mods/B52.
J="$PROJ/Assets/Blueprinter/Mods/HeavyHangar"
[ -e "$J" ] || cmd //c mklink //J "$(cygpath -w "$J")" "$(cygpath -w "$ROOT/unity/Mods/HeavyHangar")"
echo "== Textures";          python tools/make_hangar_textures.py
echo "== Blender: hangar";   "$B" -b --factory-startup --python tools/blender_hangar.py -- "$(cygpath -w unity/Mods/HeavyHangar)" 2>&1 | grep -E "HANGAR|Error|Traceback"
echo "== Unity: prefab";     "$U" -batchmode -nographics -quit -projectPath "$P" -logFile "$(cygpath -w "$LOGS/build_hangar.log")" -executeMethod B52Tools.HangarBuilder.Build
grep -E "error CS" "$LOGS/build_hangar.log" | head -5 || true
cat "$PROJ/HangarBuild.log"
echo "== Unity: mod bundle"; "$U" -batchmode -nographics -quit -projectPath "$P" -logFile "$(cygpath -w "$LOGS/build_hangar_mod.log")" -executeMethod B52Tools.HangarBuilder.BuildMod
grep -E "\[Blueprinter\] (Built|Missing)" "$LOGS/build_hangar_mod.log"
echo "== Plugin";            (cd plugin/HeavyHangar && dotnet build -c Release -o ../../build/plugin 2>&1 | grep -E " error |Build succeeded" | sort -u)
