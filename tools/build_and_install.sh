#!/usr/bin/env bash
# One command for a test session: build the B-52 and the Heavy Hangar, then install both. Game must be closed.
set -e
cd "$(dirname "$0")/.."
if tasklist 2>/dev/null | grep -qi "NuclearOption.exe"; then echo "Close Nuclear Option first."; exit 1; fi
bash tools/build_all.sh
bash tools/build_hangar.sh
bash tools/install.sh all
