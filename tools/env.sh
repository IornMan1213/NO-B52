#!/usr/bin/env bash
# Shared settings for the build and install scripts (source it; don't run it). Works on Linux and on Windows under
# Git Bash / MSYS. Override any path with an environment variable of the same name:
#   BLENDER, UNITY            the tools
#   B52_UNITY_PROJECT       the Blueprinter Editor Unity project the B-52 is built in (its own variable, so a
#                           BLUEPRINTER_PROJECT set for other mods never points this build at their project)
#   NO_GAME_DIR               the Nuclear Option install folder (the one holding NuclearOption.exe)

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

case "$(uname -s)" in
  MINGW*|MSYS*|CYGWIN*) IS_WIN=1 ;;
  *) IS_WIN=0 ;;
esac

if [[ $IS_WIN == 1 ]]; then
  BLENDER="${BLENDER:-/c/Program Files/Blender Foundation/Blender 5.2/blender.exe}"
  UNITY="${UNITY:-/c/Program Files/Unity/Hub/Editor/2022.3.62f2/Editor/Unity.exe}"
  B52_UNITY_PROJECT="${B52_UNITY_PROJECT:-$HOME/BlueprinterProject/Blueprinter-Editor}"
  NO_GAME_DIR="${NO_GAME_DIR:-/c/Program Files (x86)/Steam/steamapps/common/Nuclear Option}"
else
  BLENDER="${BLENDER:-$(command -v blender || echo blender)}"
  UNITY="${UNITY:-$HOME/Unity/Hub/Editor/2022.3.62f2/Editor/Unity}"
  B52_UNITY_PROJECT="${B52_UNITY_PROJECT:-$HOME/NuclearOption/Blueprinter-B52}"
  NO_GAME_DIR="${NO_GAME_DIR:-$HOME/.steam/steam/steamapps/common/Nuclear Option}"
fi
PYTHON="${PYTHON:-$(command -v python3 || command -v python)}"
LOGS="$ROOT/build/logs"

# Native path for tools that don't understand MSYS paths (Unity, Blender, dotnet on Windows).
winpath() { if [[ $IS_WIN == 1 ]]; then cygpath -w "$1"; else printf '%s\n' "$1"; fi; }

# The mod version, from the Unity builder (the plugin's [BepInPlugin] attribute must match it).
mod_version() { grep -oE 'const string Version = "[^"]+"' "$ROOT/unity/B52Tools/Editor/B52Builder.cs" | grep -oE '[0-9][0-9.]*[0-9]'; }

game_running() {
  if [[ $IS_WIN == 1 ]]; then tasklist 2>/dev/null | grep -qi "NuclearOption.exe"
  else pgrep -f "NuclearOption.exe" >/dev/null 2>&1; fi
}

# link_dir <target> <link>: make <link> point at <target> (a junction on Windows, a symlink elsewhere).
link_dir() {
  local target="$1" link="$2"
  [[ -e "$link" || -L "$link" ]] && return 0
  mkdir -p "$(dirname "$link")"
  if [[ $IS_WIN == 1 ]]; then cmd //c mklink //J "$(winpath "$link")" "$(winpath "$target")" >/dev/null
  else ln -s "$target" "$link"; fi
}

# The Unity project sees the mod sources through links, so edits in the repo are what gets built.
link_unity_sources() {
  [[ -d "$B52_UNITY_PROJECT/Assets" ]] || { echo "No Unity project at $B52_UNITY_PROJECT (set B52_UNITY_PROJECT)"; exit 1; }
  link_dir "$ROOT/unity/Mods/B52"         "$B52_UNITY_PROJECT/Assets/Blueprinter/Mods/B52"
  link_dir "$ROOT/unity/Mods/HeavyHangar" "$B52_UNITY_PROJECT/Assets/Blueprinter/Mods/HeavyHangar"
  link_dir "$ROOT/unity/B52Tools/Editor"  "$B52_UNITY_PROJECT/Assets/Editor/B52Tools"
}

# step <title> <summary-regex> <command...>
# Runs the command with its output in build/logs/<title>.log. Stops the build if it fails, showing the end of the
# log; otherwise prints the log lines matching the summary regex (empty regex: none).
step() {
  local title="$1" pattern="$2"; shift 2
  local log="$LOGS/$(echo "$title" | tr -c 'A-Za-z0-9\n' '_').log"
  mkdir -p "$LOGS"
  echo "== $title"
  if ! "$@" >"$log" 2>&1; then
    echo "!! $title failed (log: $log)"; tail -n 25 "$log"; exit 1
  fi
  local errors="${STEP_ERRORS:-Traceback|error CS[0-9]+|: error [A-Z]+[0-9]+|FAILED}"
  if grep -qE "$errors" "$log"; then
    echo "!! $title reported errors (log: $log)"; grep -E "$errors" "$log" | head -n 10; exit 1
  fi
  [[ -n "$pattern" ]] && { grep -E "$pattern" "$log" || true; }
  return 0
}

# Blueprinter keeps the game's own assemblies in Library/ScriptAssemblies, read-only, in place of the project's.
# Batch mode can't compile over them, and Blueprinter's own sync (an editor delayCall) never runs in batch mode, so:
# a compile pass with them writable, then the game's copies back, read-only. Run once before the Unity steps.
GAME_DLLS=(Assembly-CSharp.dll Assembly-CSharp-firstpass.dll)
unity_prepare() {
  local sa="$B52_UNITY_PROJECT/Library/ScriptAssemblies"
  mkdir -p "$sa"
  for f in "${GAME_DLLS[@]}"; do [[ -f "$sa/$f" ]] && chmod u+w "$sa/$f"; done
  step "Unity compile" "" "$UNITY" -batchmode -nographics -quit -projectPath "$(winpath "$B52_UNITY_PROJECT")" \
    -logFile "$(winpath "$LOGS/unity_compile_editor.log")"
  for f in "${GAME_DLLS[@]}"; do
    [[ -f "$sa/$f" ]] && chmod u+w "$sa/$f"
    cp "$B52_UNITY_PROJECT/Packages/nuclearoption/$f" "$sa/$f"
    chmod a-w "$sa/$f"
  done
}

# Unity in batch mode on the Blueprinter project; it exits non-zero when the method throws.
unity_run() {
  local method="$1" log="$2"
  "$UNITY" -batchmode -nographics -quit -projectPath "$(winpath "$B52_UNITY_PROJECT")" \
    -logFile "$(winpath "$log")" -executeMethod "$method"
}

# Blender in the background; --python-exit-code makes a script exception fail the step.
blender_run() { "$BLENDER" -b --python-exit-code 1 "$@"; }

dotnet_plugin() {
  local proj="$1"
  dotnet build "$ROOT/plugin/$proj" -c Release -o "$ROOT/build/plugin" -p:GameDir="$(winpath "$NO_GAME_DIR")"
}
