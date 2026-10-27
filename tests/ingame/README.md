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
- `spawn.txt`: the menu clicks from the main menu to sitting in the aircraft (`include spawn.txt <aircraft>`).

Driver commands are listed at the top of each `case` in `NODevDriver/Driver.cs`; `b52check` is in `B52Check.cs`.
