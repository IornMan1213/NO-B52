#!/usr/bin/env bash
# One command for a test session: build the B-52 and the Heavy Hangar, then install both. Game must be closed.
# Pass --from-fbx to skip the Blender stages of the B-52 build.
set -euo pipefail
source "$(dirname "$0")/env.sh"
cd "$ROOT"
if game_running; then echo "Close Nuclear Option first."; exit 1; fi
bash tools/build_all.sh "${1:-all}"
bash tools/build_hangar.sh
bash tools/install.sh all
