# In-game tests (dev only, never shipped)

`run.sh <scenario>` installs the test driver (`NODevDriver/`, a B-52 copy of the MiG-29 pack's, with its own plugin ID
and command file so both can be installed), starts Nuclear Option through Steam if it isn't running, feeds the
scenario to the driver and prints its `[TEST]` lines. It exits 1 if a check failed or the scenario didn't finish.

```bash
bash tools/build_all.sh && bash tools/install.sh b52
bash tests/ingame/run.sh tests/ingame/b52_flight.txt
```

- `b52_flight.txt`: spawn at Boscali North (Heavy Hangar there), mod checks, takeoff, bombs from the bay and both
  pylons (`salvo <station> <pulls>`), gear cycle, rollout with the drag chute. Screenshots go to `BepInEx/b52_test_*.png`.
- `b52_looks.txt`: spawn, then hull close-ups and real screenshots of the flight deck with the pilot's head turned
  (`look <pan> <tilt>`, then `shot`: the game's cockpit camera, exposure and post-processing, as the player sees it),
  and a frame-time sample on the runway. Screenshots: `BepInEx/b52_cp_*.png`. For judging the model and cockpit.
  (`view` and `pview` render with a single camera: handy for geometry, but they miss the game's cockpit camera.)
- `hh_place.txt`: Heavy Hangar automatic placement: where each hangar went (the log), shots of the site from above,
  and `taxiout`: the B-52 rolls straight out of the door and must stay on paving (hangar floor, apron, taxiway).
- `hh_outside.txt`: spawning outside a stock hangar's door (run it with Heavy Hangar's `AutoPlace = false`).
- `spawn.txt`: the menu clicks from the main menu to sitting in the aircraft (`include spawn.txt <aircraft>`).

Driver commands are listed at the top of each `case` in `NODevDriver/Driver.cs`; `b52check` is in `B52Check.cs`.
