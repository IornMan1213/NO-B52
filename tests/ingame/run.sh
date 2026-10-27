#!/usr/bin/env bash
# In-game tests for the B-52J (adapted from the MiG-29 Pack's). Feeds a scenario (tests/ingame/*.txt, one NODevDriver command per line) to the running game
# and prints its "[TEST]" lines; exits 1 if any check failed or the scenario did not finish.
#   tests/ingame/run.sh tests/ingame/b52_flight.txt [timeout s]
# Installs the dev-only driver (tests/ingame/NODevDriver, never shipped) into BepInEx/plugins/NODevDriverB52, launches the game
# through Steam if it is not running and waits for Blueprinter to finish loading. Scenarios end with "say END" (and "quit" to
# close the game; or QUIT=1 tests/ingame/run.sh ...). "include <file> [args]" pulls in a shared part (spawn.txt, flight.txt).
# Needs the B-52 installed in BepInEx/plugins (tools/install.sh).
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
scenario="${1:?scenario file}"; timeout="${2:-900}"
game="${GameDir:-$HOME/.steam/steam/steamapps/common/Nuclear Option}"
bep="$game/BepInEx"; log="$bep/LogOutput.log"

dll="$here/NODevDriver/bin/Release/NODevDriverB52.dll"
if [ ! -f "$dll" ] || [ -n "$(find "$here/NODevDriver" -name '*.cs' -newer "$dll")" ]; then
    (cd "$here/NODevDriver" && dotnet build -c Release -nologo -v q "-p:GameDir=$game" >/dev/null)
fi
mkdir -p "$bep/plugins/NODevDriverB52"
if ! cmp -s "$dll" "$bep/plugins/NODevDriverB52/NODevDriverB52.dll"; then
    pgrep -x "NuclearOption.e" >/dev/null && echo "note: the driver changed; restart the game to load it" >&2
    cp "$dll" "$bep/plugins/NODevDriverB52/"
fi

if ! pgrep -x "NuclearOption.e" >/dev/null; then
    rm -f "$bep/nodev_b52_cmd.txt"   # a stale scenario would start before the menu exists
    steam -applaunch 2168680 >/dev/null 2>&1 &
    echo "launching Nuclear Option..."
    for _ in $(seq 60); do pgrep -x "NuclearOption.e" >/dev/null && break; sleep 2; done
fi
# BepInEx starts a new log when the game starts: wait for this run's Blueprinter to finish loading
t0=$(date -d "$(ps -o lstart= -p "$(pgrep -x "NuclearOption.e" | head -1)")" +%s)
if ! { [ -f "$log" ] && [ "$(stat -c %Y "$log")" -ge "$t0" ] && grep -q "\[Plugin\] Done in" "$log"; }; then
    echo "waiting for the game to load..."
    until [ -f "$log" ] && [ "$(stat -c %Y "$log")" -ge "$t0" ] && grep -q "\[Plugin\] Done in" "$log"; do
        sleep 5; [ $(( $(date +%s) - t0 )) -gt 600 ] && { echo "the game did not finish loading in 10 min" >&2; exit 1; }
    done
    sleep 10   # the main menu builds after Blueprinter is done
fi

grep -aq "Loading \[NO Dev Driver B-52" "$log" || { echo "the dev driver is not loaded in this game session (BepInEx skipped it, or it was installed after the game started): restart the game" >&2; exit 1; }

# "include <file> [args]" lines pull in another scenario (relative to this one), its $1, $2... replaced by the args
expand() {
    local dir line i; local -a args inc_args; dir="$(dirname "$1")"; args=("${@:2}")
    while IFS= read -r line || [ -n "$line" ]; do
        if [[ "$line" =~ ^include[[:space:]]+([^[:space:]]+)(.*)$ ]]; then
            read -r -a inc_args <<< "${BASH_REMATCH[2]}"
            expand "$dir/${BASH_REMATCH[1]}" "${inc_args[@]}"
        else
            for i in "${!args[@]}"; do line="${line//\$$((i + 1))/${args[$i]}}"; done
            printf '%s\n' "$line"
        fi
    done < "$1"
}
# the run is fenced by its own markers, so a scenario still running from before (or its log lines) cannot be mistaken for this one
id="run$(date +%s%N)"
cmds="say RUN $id
$(expand "$scenario")
release
say DONE $id"
[ "${QUIT:-0}" = 1 ] && cmds="$cmds
quit"

[ -f "$bep/nodev_b52_cmd.txt" ] && { echo "a scenario is still waiting to start: waiting for it" >&2; while [ -f "$bep/nodev_b52_cmd.txt" ]; do sleep 2; done; }
printf '%s\n' "$cmds" > "$bep/nodev_b52_cmd.txt"
echo "running $(basename "$scenario") ($(printf '%s\n' "$cmds" | grep -cv '^\s*$') commands)..."
t0=$(date +%s)
mine() { sed -n "/\[TEST\] RUN $id/,\$p" "$log"; }
until mine | grep -q "\[TEST\] DONE $id"; do
    sleep 3
    if [ $(( $(date +%s) - t0 )) -gt "$timeout" ]; then echo "timed out after ${timeout} s" >&2; break; fi
    if ! pgrep -x "NuclearOption.e" >/dev/null; then echo "the game closed" >&2; break; fi
done
results=$(mine | grep -a "\[TEST\]\|Exception" | grep -v "\[TEST\] \(RUN\|DONE\) $id" | sed 's/^\[[^]]*\]\s*//')
echo "$results"
fails=$(echo "$results" | grep -c "\[TEST\] FAIL" || true)
ended=$(echo "$results" | grep -c "\[TEST\] END" || true)
echo "---"; echo "failed checks: $fails"
[ "$fails" -eq 0 ] && [ "$ended" -gt 0 ]
