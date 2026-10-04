#!/usr/bin/env bash
# Heavy Hangar build: textures -> Blender model -> FBX -> Unity prefab + definition -> .nobp -> plugin.
# Run from the repo root with the game closed (Unity and the game both lock files).
set -e
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
B="/c/Program Files/Blender Foundation/Blender 5.2/blender.exe"
U="/c/Program Files/Unity/Hub/Editor/2022.3.62f2/Editor/Unity.exe"
P='C:\Users\jayea\BlueprinterProject\Blueprinter-Editor'
cd "$ROOT"
# The Unity project sees the mod source through a junction, like Mods/B52.
J="/c/Users/jayea/BlueprinterProject/Blueprinter-Editor/Assets/Blueprinter/Mods/HeavyHangar"
[ -e "$J" ] || cmd //c mklink //J "$(cygpath -w "$J")" "$(cygpath -w "$ROOT/unity/Mods/HeavyHangar")"
echo "== Textures";          python tools/make_hangar_textures.py
echo "== Blender: hangar";   "$B" -b --factory-startup --python tools/blender_hangar.py -- "$(cygpath -w unity/Mods/HeavyHangar)" 2>&1 | grep -E "HANGAR|Error|Traceback"
echo "== Unity: prefab";     "$U" -batchmode -nographics -quit -projectPath "$P" -logFile 'C:\Users\jayea\BlueprinterProject\build_hangar.log' -executeMethod B52Tools.HangarBuilder.Build
grep -E "error CS" /c/Users/jayea/BlueprinterProject/build_hangar.log | head -5 || true
cat /c/Users/jayea/BlueprinterProject/Blueprinter-Editor/HangarBuild.log
echo "== Unity: mod bundle"; "$U" -batchmode -nographics -quit -projectPath "$P" -logFile 'C:\Users\jayea\BlueprinterProject\build_hangar_mod.log' -executeMethod B52Tools.HangarBuilder.BuildMod
grep -E "\[Blueprinter\] (Built|Missing)" /c/Users/jayea/BlueprinterProject/build_hangar_mod.log
echo "== Plugin";            (cd plugin/HeavyHangar && dotnet build -c Release -o ../../build/plugin 2>&1 | grep -E " error |Build succeeded" | sort -u)
