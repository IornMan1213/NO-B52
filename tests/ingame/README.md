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
- `b52_hud.txt`: the HUD readouts: the key card shows itself on the first flight (and a screenshot of it), and the
  CHUTE OUT / CHUTE RELEASED messages appear during a rollout (`hudwatch`).
- `b52_fullload.txt`: every bay rack measured against the bay (`bayfit`), then full tanks (`fuel full`) and a
  hands-off takeoff, for the full-load numbers.
- `b52_flight.txt` also runs `b52live` on the ground and in the climb (crab reset, one engine sound per pod, the
  altimeter's pointers, fans turning) and ends with `eject`.
- `b52_cockpit_audit.txt`: the flight deck as the player sees it: an inventory of the interior (`cpinv`), where the
  pilot model and camera sit, eight head directions, then views of the cabin with the crew shown (`crewvis`) to check
  the pilots sit in their seats. Screenshots: `BepInEx/b52_audit_*.png`. `b52_pilot_pose.txt` logs the pilot's
  joints relative to the camera (the numbers tools/blender_cockpit.py places the seats, wheels and pedals from).
- `b52_cockpit_fill.txt`: the cockpit's fill light at four levels (`cpfill <k>`), for choosing `CockpitFill` in the
  builder. Cockpit scenarios start with `expectac B52J`, so a spawn that picked another aircraft fails.
- `b52_dust.txt` and `b52_fxcheck.txt`: tyre dust shots (`emitfx`) and the particle-material check on its own.
- `b52_local_checks.txt`: one run over the 13 Nov work: bay fit against the bigger bay, particle materials, the
  cockpit with its merged plain-colour material, and the distant model up close (`forcelod 1`) and from 1.3 km.
- `b52_holes.txt`: in the air with the gear down and the bay open (`bayopen`), the bay, wheel wells and engine rears
  from many angles, at the game's own detail level and again at full detail. Screenshots: `BepInEx/b52_h_*.png`.
- `b52_damage.txt`: the damage model (docs/DAMAGE_MODEL.md).
  - Loads at every part root on the runway and in the climb (`structure`).
  - Real hits through the game's armour (`hit`): the left wingtip comes off, and the right outboard pod comes off
    with its two engines (`expectoff`, `expectdead`).
  - A badly weakened left outer wing holds at 1 g, then folds with its pod in a full pull.
  - Finally a 3.3 g pull at 220 m/s (`gtest`), past the ultimate load, logs what gives first.

  Screenshots: `BepInEx/b52_d_*.png`.
- `b52_loads.txt`: the structure's loads in slow flight with the flaps down, and after 90 s more. Its
  "lift shares" lines are where `Structure.LiftShare` comes from: re-measure if wing areas or airfoils change.
  `gwatch` logs g, speed, pitch and bank each second (the cruise hold now keeps the wings level).
- `b52_crew_fuel.txt`: the crew and fuel parts of the damage model.
  - The crew of five at their stations (`crew`).
  - Each tank at its fuel (`tanks`), and the fuel order (`fuelwatch`: only the fuselage tanks feed at first).
  - A hit that holes the inner wing's tank and a fire hit that lights it (`expecttank`).
  - Then all five eject (`ejectwatch`): the two navigators down through the belly, the rest up. The burning wing
    comes off some seconds later.
- `b52_cockpit_glass.txt`: the windscreen glass.
  - In cockpit view the game hides the cockpit section's outer skin and glass (`renderers cockpit`), so changes to
    the glass show only from outside.
  - `glassprops` shows the glass material, with the B-52's tint.
  - Shots from the seats (`look`, `shot`: `BepInEx/b52_gl_in_*.png`) and from outside (`b52_gl_out_*.png`).
- `b52_glass_texture.txt`: why the windscreen showed black patches from outside. The game's glass shader takes the
  donor bomber's pane mask (`glasstex save` writes it out: its panes semi-clear, its frames dark and nearly opaque), and
  the B-52's glass UVs land on its frame areas. `glasstex none` shows the glass without it. `B52Systems.CanopyGlass`
  gives the B-52's glass a plain pane texture instead.
- `b52_cabin_paint.txt`: each interior object painted magenta in turn (`rpaint`), seen from outside, to tell which
  object a patch of the image is. Also `rayall` (everything a ray meets, in order), `lights`, `matinfo` and
  `glasspass`.
- `hh_place.txt`: Heavy Hangar automatic placement: where each hangar went (the log), shots of the site from above,
  and `taxiout`: the B-52 rolls straight out of the door and must stay on paving (hangar floor, apron, taxiway).
- `hh_outside.txt`: spawning outside a stock hangar's door (run it with Heavy Hangar's `AutoPlace = false`).
- `b52_tailgun.txt`: the optional B-52H tail turret (TailGun.cs). No gun on the default loadout (`tailgun off`),
  then the "Tail Turret" station loaded and emptied again (`loadout tail <mount|none>`), with shots of the tail both
  ways (`BepInEx/b52_tg_*.png`). Held in the air, a hostile fighter is spawned 1.4 km astern (`spawnhostile`) and
  `turretwatch` passes if the turret takes it as a target and fires.
- `spawn.txt`: the menu clicks from the main menu to sitting in the aircraft (`include spawn.txt <aircraft>`).

Driver commands are listed at the top of each `case` in `NODevDriver/Driver.cs`; `b52check` is in `B52Check.cs`.
- `cockpit_compare.txt <aircraft>`: the same six head directions in any aircraft's cockpit (`cmp_<aircraft>_*.png`),
  for comparing the B-52's flight deck with the game's own: `include` it with a definition name, or run it through
  a one-line scenario (`include cockpit_compare.txt Darkreach`). A wrong name lists the aircraft the menu offers.
- `b52_systems.txt` also checks the B-52's own damage display is in use (`expecthidden ENGINE 8 FIRE`: the game hides
  each fire light until that engine burns) and that a break leaves torn structure on both pieces.
- Every ejection is logged with the game code that started it (`ejection started on ...`), to tell the AI's own
  bail-out decisions from the B-52's.

Long unattended runs: `LAUNCH=proton bash tests/ingame/run.sh ...` starts the game straight through Proton beside the
Steam client instead of through it. Each launch through the Steam client leaves it holding about a dozen more X server
connections; when the X server runs out (about 256: `ss -x | grep -c X11-unix/X0`) Wine crashes at startup and the
game freezes on its first screen until Steam is restarted.
