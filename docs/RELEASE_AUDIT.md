# B-52J release audit (2026-10-27)

The interactive version of this list, where you tick what to do, is the **B-52J Release Planner** page:
https://claude.ai/artifact/6Tvz9Zd9JiRK3ZfRneArKW . Item IDs here match the page.

Checked against the game build of 2026-10-23 and Blueprinter 2.1.1. Both plugins compile cleanly, and every game member they
reach by reflection or Harmony still exists. Model: 78,039 triangles, 54 materials (the ten wheels are about 41k of it).

Priority: ✕ release blocker · — fix before 1.0 · / polish for 1.0 · ○ after 1.0. ★ = recommended for 1.0.
Evidence: code = confirmed in the source, renders = visible in the repo's renders, model scan = measured from B52.fbx,
in-game check = needs a flight to confirm, idea = new work.

**91 items:** 9 blockers, 32 before 1.0, 30 polish, 20 later. Recommended set: 59 items, about 147 h.

## Testing

What has to be flown and checked before anything ships.

| ID | Priority | | Item | Effort | Evidence | Where |
|---|---|---|---|---|---|---|
| T1 | ✕ Blocker | ★ | **Fly the 0.4 build in game before release**. The new gear, doors, liveries and MALD decoys have only been checked in editor renders. Write NODevDriver scenarios: spawn at a base with a Heavy Hangar, taxi, take off, cycle the gear twice, drop one weapon of each kind, fire MALD-J, land and stop with the chute. Fail the run on any exception in LogOutput.log and save a screenshot at each step. | 6 h | in-game check | `new tests/ingame/b52_*.txt (the driver is already installed in the game)` |
| T2 | — Before 1.0 | ★ | **One-command smoke test before each release**. Install the build, launch the game, run the scenarios and report pass or fail. tests/ingame/run.sh like the MiG-29 pack's: copy the build in, start the game through Steam, wait for the driver, grep the log for errors and collect screenshots into build/test/. | 4 h | idea |  |
| T3 | — Before 1.0 | ★ | **Two-player multiplayer check**. A host and one client, both with the mod installed. Check the other player sees the gear, doors, spoilers, bay doors, chute and weapon release, and that a client without the DLL gets a clear failure instead of a half-working aircraft. | 3 h | in-game check |  |
| T4 | / Polish |  | **Stress test with several B-52s**. Four player or AI B-52s at one base: frame rate, physics and log spam. | 2 h | in-game check |  |
| T5 | ○ Later |  | **Unit tests for the maths**. Wave drag, bay and pylon layouts, CSV formatting. A small xunit project for the pure functions (MachDrag.WaveDragCoef, B52Weapons.BayLayout and PylonLayout counts, telemetry row formatting) that also runs in CI. | 3 h | idea |  |

## Plugin systems

Gear, drag chute, telemetry, config and everything B52Systems.dll adds at runtime.

| ID | Priority | | Item | Effort | Evidence | Where |
|---|---|---|---|---|---|---|
| SY1 | ✕ Blocker | ★ | **Crosswind crab carries over to the next aircraft**. Respawn and the trucks are still turned, with GEAR CRAB stuck on screen. Reset it when the local player's B-52 spawns, and centre it after touchdown once the aircraft has stopped. | 1 h | code | `plugin/B52Systems/GearSystem.cs:25` |
| SY2 | ✕ Blocker | ★ | **Drag chute looks wrong to other players**. On other players' screens it stays at 15% size, or never appears. Advance inflation on every client and keep only the drag force owner-side. Better: send deploy and jettison over the network so every client shows the same chute. | 2 h | code | `plugin/B52Systems/DragChute.cs:44-46` |
| SY3 | ✕ Blocker | ★ | **Telemetry is on for every player**. Each client writes a CSV for every B-52 it sees, AI and other players included, and never deletes them. Default it off. When it's on, log only the local player's aircraft and keep the newest 20 files. | 2 h | code | `plugin/B52Systems/Plugin.cs:27, Telemetry.cs:37` |
| SY4 | ✕ Blocker | ★ | **Telemetry files break on non-English Windows**. German or French number formats put commas inside a comma-separated file. Format every number with CultureInfo.InvariantCulture and quote text fields. | 1 h | code | `plugin/B52Systems/Telemetry.cs:113-127 and the summary line` |
| SY5 | — Before 1.0 | ★ | **Chute drops away instead of vanishing**. It disappears in one frame on jettison or below 20 kt. On jettison, detach the canopy, let it drift back and settle on the runway over a few seconds, then remove it. | 2 h | code | `plugin/B52Systems/DragChute.cs:75` |
| SY6 | — Before 1.0 | ★ | **Chute leaks memory every landing and glows at night**. A new material and mesh per deployment, never destroyed; the canopy is unlit. Build the canopy mesh and material once per aircraft and destroy them with it; use a lit shader so it shades like the rest of the scene. | 1 h | code | `plugin/B52Systems/DragChute.cs:113, 126` |
| SY7 | — Before 1.0 | ★ | **Make the GEAR CRAB readout match the game's HUD**. Fixed 16 px text at the top of the screen, restyled every frame. Cache the style, scale it with screen height, add a shadow, place it near the HUD gear indicator and fade it out after a few seconds. | 2 h | code | `plugin/B52Systems/GearSystem.cs:72-74` |
| SY8 | — Before 1.0 | ★ | **Crab keys fire while typing in chat**. [ ] and \ also change the crab in chat and menus, and some keyboard layouts don't have them. Ignore the keys while chat, a menu or the pause screen is open; explain how to remap them in the config description and README. | 1 h | code | `plugin/B52Systems/GearSystem.cs (HandleCrabKeys)` |
| SY9 | — Before 1.0 | ★ | **Without Heavy Hangar the B-52 spawns inside small hangars**. 56 m of wing through the hangar walls. Either list Heavy Hangar as a required mod in the mod manager, or move the spawn-outside-the-door fallback into B52Systems so it works without it. | 2 h | in-game check | `B52Builder.MakeOps; plugin/HeavyHangar/OutsideSpawn.cs` |
| SY10 | — Before 1.0 | ★ | **Check the crew can eject**. The donor bomber's escape capsule isn't copied to the B-52's cockpit. Test eject in game. If it's broken, copy the escape capsule (or wire the crew's ejection seats) to the B-52 crew. | 1 h | in-game check | `unity/B52Tools/Editor/B52Builder.cs, Parts table (cockpit row)` |
| SY11 | / Polish | ★ | **Show CHUTE on screen when it deploys or drops**. Players can't tell why the chute did or didn't come out. A short readout in the same style as GEAR CRAB (CHUTE OUT, CHUTE GONE), plus a one-time hint when the conditions are almost met. | 1 h | idea |  |
| SY12 | / Polish |  | **Tune the onboard jammer for a B-52**. The radar jammer kept FastBomber1's settings. Compare with the stock bombers' RadarJammer and give it B-52-like power and range in the builder. | 2 h | in-game check |  |
| SY13 | / Polish | ★ | **Landing lights, taxi lights and beacons**. Night takeoffs and landings are dark apart from the nav lights. Landing and taxi lights by the gear, red anti-collision beacons top and bottom, white strobes, using the game's own light setup. | 3 h | idea |  |
| SY14 | / Polish |  | **In-game help card**. A key (F1 by default) shows the B-52's keys and speeds to fly by. | 2 h | idea |  |
| SY15 | ○ Later |  | **Sync the crab angle in multiplayer**. Other players see the trucks pointing straight ahead. | 3 h | idea |  |
| SY16 | ○ Later |  | **Tail hatch opens for the chute**. Animate the tail-cone compartment door when the chute deploys. | 4 h | idea |  |

## Flight model

How it flies: physics, autopilot, handling.

| ID | Priority | | Item | Effort | Evidence | Where |
|---|---|---|---|---|---|---|
| FP1 | — Before 1.0 | ★ | **Player's B-52 uses the coarse collision mode**. Skipping the game's complex-physics switch also skips continuous collision for the player's aircraft. When the plugin skips the method, set continuous collision detection itself for the local player's aircraft. | 1 h | code | `plugin/B52Systems/Plugin.cs:55 (IgnoreSelfCollision.Prefix)` |
| FP2 | — Before 1.0 | ★ | **Tune the autopilot for a 200 t bomber**. The autopilot gains came from the much lighter FastBomber. Fly altitude and heading hold at light and heavy weights, and damp any porpoising by tuning AutopilotPlane in the builder. | 2 h | in-game check |  |
| FP3 | — Before 1.0 | ★ | **Re-check takeoff and landing numbers with the new gear**. 0.4.0 rebuilt the gear (ride height, springs, rolling resistance); the README numbers predate it. Measure takeoff run, lift-off speed and landing roll from telemetry at three weights, then update the README's numbers to fly by. | 2 h | in-game check |  |
| FP4 | / Polish |  | **Engine-out handling**. Losing a pod should yaw the aircraft and need rudder. Shoot off one pod in a test and check thrust asymmetry, rudder authority and fly-by-wire yaw damping. | 3 h | in-game check |  |
| FP5 | / Polish | ★ | **Warn when a part's lift point falls back to its origin**. If a part's mesh can't be read, its lift silently acts at the wrong point. Log one warning naming the part. | 1 h | code | `plugin/B52Systems/AeroCentres.cs:79` |
| FP6 | ○ Later |  | **Visible wing flex**. B-52 wingtips droop on the ground and rise in flight; the rigid airframe shows none of it. Move each wing section's visible mesh onto a child node in the builder; the plugin bends them a few degrees by g-load and fuel weight. Physics stays rigid. | 1.5 d | idea |  |

## Exterior looks

Model, materials and textures on the outside of the aircraft.

| ID | Priority | | Item | Effort | Evidence | Where |
|---|---|---|---|---|---|---|
| EX1 | ✕ Blocker | ★ | **Stowed outrigger pokes through the wing**. With the gear up, a white bar sticks out below the lower wing skin. Check the stowed pose in game, then slim or tuck the leg or torque link and re-cut the wing slot. | 4 h | renders | `tools/blender_wingwells.py; B52Builder.BuildGear (OutriggerFold)` |
| EX2 | — Before 1.0 | ★ | **Surface detail on the skin**. No normal map and solid-white ambient occlusion, so the panels look painted on. Bake a panel-line normal map and an ambient-occlusion map in Blender from the source model, and plug them into the slots the builder currently fills with flat textures. | 1 d | code | `B52Builder.ConvertMaterials` |
| EX3 | — Before 1.0 | ★ | **Seams around the gear doors**. The cut-out doors shade differently from the skin around them. Copy the surrounding skin's normals onto the cut-out door faces, and give the door insides a proper interior colour. | 3 h | renders | `tools/blender_wells.py, tools/blender_wingwells.py` |
| EX4 | — Before 1.0 | ★ | **Tyres, hubs and struts**. Pure black tyres, pure white hubs, a plain white outrigger leg. Dark grey rubber with some roughness, grey metal hubs and struts, chrome only on the oleo. | 1 h | renders |  |
| EX5 | — Before 1.0 | ★ | **Real pylons instead of cubes**. The wing pylons are Unity cubes in the engine pod's material. Model a tapered pylon in Blender and use the skin atlas. | 3 h | code | `B52Builder.BuildHardpoints (CreatePrimitive)` |
| EX6 | — Before 1.0 | ★ | **Check bombs don't clip the bay with the doors open**. Rack layouts are computed from weapon sizes, not fitted to the bay. | 2 h | in-game check | `B52Weapons.BayLayout` |
| EX7 | / Polish | ★ | **Proper damage texture**. Damage only darkens the paint: the 'damaged' texture is the clean one. Paint a scorched version (soot streaks, burnt panels) for the damage slot. | 4 h | code | `B52Builder.ConvertMaterials (_BasecolorDmg)` |
| EX8 | / Polish | ★ | **Spoilers don't take liveries**. They use a separate grey material instead of the skin atlas. | 1 h | model scan |  |
| EX9 | / Polish | ★ | **Livery pack**. Vietnam camouflage with black belly, Cold War white anti-flash belly, NASA NB-52B 008, and one livery per game faction. | 1.5 d | idea | `docs/livery_template.png; B52Builder.MakeDefinition (liveries)` |
| EX10 | / Polish |  | **Smoother outline**. The airframe is about 17k triangles, and the nose and engine pods look faceted up close. Add supporting edge loops or subdivide the nose, pods and fuselage sections in blender_prep (target about 35k). | 1 d | model scan |  |
| EX11 | / Polish |  | **Spinning engine fans**. The fan blade meshes exist but never turn. | 2 h | idea |  |
| EX12 | / Polish |  | **Contrails from all eight engines**. Check the game's contrail effect follows the cloned nozzles, and add it if not. | 3 h | in-game check |  |
| EX13 | ○ Later |  | **Sharper textures**. One 4096 px atlas covers a 48 m aircraft, so it's soft up close. Two 4K atlases (fuselage, wings) or one 8K atlas; check memory use. | 1 d | idea |  |
| EX14 | ○ Later |  | **Small exterior details**. Antennas, pitot tubes, refuelling receptacle markings. | 1 d | idea |  |

## Cockpit

Flight deck art and instruments.

| ID | Priority | | Item | Effort | Evidence | Where |
|---|---|---|---|---|---|---|
| CK1 | — Before 1.0 | ★ | **Altimeter can't show your altitude**. One needle that wraps every 1,000 ft. Add a 10,000 ft pointer or a drum counter. | 3 h | code | `plugin/B52Systems/Instruments.cs:45` |
| CK2 | — Before 1.0 | ★ | **Cockpit art pass**. Around the screens it's plain grey boxes. A trim texture with panel screws, switch rows and labels; an anti-glare black glareshield; darker cockpit grey; baked ambient occlusion; placards on the throttle quadrant. | 1.5 d | renders | `tools/blender_cockpit.py, tools/make_cockpit_textures.py` |
| CK3 | / Polish | ★ | **Merge 19 cockpit materials into one or two**. Fewer draw calls, and the art pass needs a single atlas anyway. | 4 h | model scan |  |
| CK4 | / Polish | ★ | **Attitude indicator leaks a material each spawn**.  | 1 h | code | `plugin/B52Systems/Instruments.cs:29` |
| CK5 | / Polish |  | **Airspeed gauge may show true airspeed**. At altitude true airspeed reads far above indicated, so the README's approach speeds won't match the gauge. Check what the game's speed value is; if it's true airspeed, convert to indicated. | 1 h | in-game check |  |
| CK6 | / Polish |  | **Panel lighting at night**. Backlit gauges and switch labels, plus a dim flood light. | 4 h | idea |  |
| CK7 | ○ Later |  | **Attitude indicator flips near straight up or down**. Roll comes from Euler angles, which jump near ±90° pitch. | 1 h | code | `plugin/B52Systems/Instruments.cs (LateUpdate)` |
| CK8 | ○ Later |  | **Animated gear and flap levers, working crab knob**.  | 4 h | idea |  |
| CK9 | ○ Later |  | **Warning lights**. Master caution and gear-unsafe lights, using the game's cockpit warning lights. | 3 h | idea |  |
| CK10 | ○ Later |  | **Lower deck crew stations**. The real B-52 has five crew; only the two pilots' seats exist. | 2.5 d | idea |  |

## Sound

Engines and systems audio.

| ID | Priority | | Item | Effort | Evidence | Where |
|---|---|---|---|---|---|---|
| SD1 | — Before 1.0 | ★ | **Sixteen copies of the same engine sound**. Every engine and nozzle clone keeps the donor's sound: 16 identical loops. Keep sound on one engine per pod, offset start time and pitch slightly, and match the volume to the stock bombers. | 2 h | code | `B52Builder.BuildEngines` |
| SD2 | / Polish |  | **Systems sounds**. Chute pop, gear motors, door thunk, spoiler hydraulics, reusing the game's own clips. | 3 h | idea |  |
| SD3 | ○ Later |  | **Engine sound for the F130**. A proper high-bypass sound set; needs audio you're allowed to ship. | 1.5 d | idea |  |

## Weapons and loadouts

Racks, presets and weapon data.

| ID | Priority | | Item | Effort | Evidence | Where |
|---|---|---|---|---|---|---|
| WP1 | — Before 1.0 | ★ | **BDU-48 weighs 907 kg in the build**. The real BDU-48 is a 4.5 kg practice bomb, so 17 of them load 15 t. Fix the weight, or switch to BDU-50 (the 500 lb inert bomb). | 0.5 h | code | `unity/B52Tools/Editor/B52Weapons.cs:72` |
| WP2 | — Before 1.0 | ★ | **20 CALCMs exceed the payload limit**. 39 t, against the 31.75 t limit LOADOUTS.md says the build respects. 8 in the bay plus 4 per pylon (16 missiles, 31 t). | 0.5 h | code | `unity/B52Tools/Editor/B52Weapons.cs:63` |
| WP3 | — Before 1.0 | ★ | **In-game description doesn't match the loadouts**. It says 20 GBU-31 and 4 hypersonic missiles; the preset is 24, and there's no hypersonic weapon. | 0.5 h | code | `unity/B52Tools/Editor/B52Builder.cs:1595` |
| WP4 | — Before 1.0 | ★ | **MALD racks fall back to real cruise missiles**. If the decoy can't be built, the racks silently carry armed cruise missiles. Fail the build instead. | 0.5 h | code | `B52Weapons.Generate / MakeMount` |
| WP5 | / Polish | ★ | **Rotary label missing on LRASM and SRAM**. They load on the rotary launcher but the rack name doesn't say so. | 0.5 h | code | `unity/B52Tools/Editor/B52Weapons.cs:168` |
| WP6 | / Polish | ★ | **Mixed presets**. For example JASSM-ER x8 in the bay with GBU-38 x48 on the pylons, or LRASM x8 with MALD x16. | 2 h | idea |  |
| WP7 | / Polish |  | **Multiplayer balance option**. 20 nuclear cruise missiles or 80 JDAMs per sortie may be too much for some servers. A config switch for lower counts, or a higher cost for the nuclear presets. This one is your call. | 3 h | idea |  |
| WP8 | ○ Later |  | **B-52-specific weapon models**. JASSM-ER, ALCM and MALD reuse the game's closest munitions. | 2.5 d | idea |  |
| WP9 | ○ Later |  | **Rotary launcher turns between releases**.  | 4 h | idea |  |

## AI and missions

AI-flown B-52s and bundled content.

| ID | Priority | | Item | Effort | Evidence | Where |
|---|---|---|---|---|---|---|
| MP1 | / Polish | ★ | **Check AI-flown B-52s**. Takeoff distance, landing, chute and weapon use. Spawn an AI flight and tune the aircraft parameters (takeoff distance, approach speeds) if they overrun the runway. | 4 h | in-game check |  |
| MP2 | / Polish |  | **Showcase missions**. Two or three bundled missions: SEAD with MALD-J, an anti-ship LRASM strike, carpet bombing. | 1 d | idea |  |

## Performance

Triangles, draw calls, per-frame work.

| ID | Priority | | Item | Effort | Evidence | Where |
|---|---|---|---|---|---|---|
| PF1 | — Before 1.0 | ★ | **Wheels are over half the triangles**. 10 wheels at 3.5k-4.3k triangles each: about 41k of the aircraft's 78k. Cut tyre and rim segments to roughly 600-800 triangles per wheel; it won't show at runway distance. | 2 h | model scan | `tools/blender_gear.py` |
| PF2 | / Polish | ★ | **Add level-of-detail meshes**. The builder deletes the LOD group, so distant B-52s draw at full detail. LOD1 at about 40% and LOD2 at about 10% from Blender's decimate; drop the gear and cockpit at distance. | 1 d | code | `B52Builder.ClearLiveryTargets` |
| PF3 | / Polish | ★ | **Mass correction runs for every aircraft in the game**. It looks up a component on every fuel tick of every aircraft, not just B-52s. | 1 h | code | `plugin/B52Systems/MassSync.cs:57-62` |
| PF4 | / Polish | ★ | **Aero-centre logging on by default**. About 30 log lines every time a B-52 spawns. | 0.5 h | code | `plugin/B52Systems/Plugin.cs:29` |

## Build and release

Scripts, packaging, versioning and the mod manager.

| ID | Priority | | Item | Effort | Evidence | Where |
|---|---|---|---|---|---|---|
| BR1 | ✕ Blocker | ★ | **Build scripts only work on Windows**. cygpath, mklink, tasklist and C:\ paths, and this machine runs Linux. Detect the OS, use symlinks and pgrep on Linux, and take game and tool paths from environment variables with Linux defaults. | 4 h | code | `tools/build_all.sh, build_hangar.sh, install.sh, build_and_install.sh, both .csproj files` |
| BR2 | ✕ Blocker | ★ | **Script for the release zip**. The 0.4.1 zip was put together by hand. tools/package.sh: folder layout, .nobp, DLL, LICENSE, NOTICE, CC BY text and a player README; prints the sha256 for the mod manager. | 3 h | code |  |
| BR3 | ✕ Blocker | ★ | **One version number**. It's typed separately in the plugin, the builder and the README. A VERSION file the scripts read; the plugin warns if its version and the aircraft file's differ. | 2 h | code | `Plugin.cs:10, B52Builder.cs:1485` |
| BR4 | — Before 1.0 | ★ | **Build steps hide failures**. A Blender crash can pass, and a quiet step that succeeded can stop the build. set -euo pipefail, log each step to a file, check exit codes, then grep the log. | 1 h | code |  |
| BR5 | — Before 1.0 | ★ | **Rebuild from source on this machine**. The source model is now in source_assets (ignored by git), but the Unity project here only links the MiG mods. Link unity/Mods/B52 and unity/B52Tools into the Blueprinter project, add a mode that starts from the committed FBX, and document the model download and checksum. | 3 h | code |  |
| BR6 | — Before 1.0 | ★ | **List the B-52 in the mod manager**. Heavy Hangar has a NOMM manifest; the B-52 doesn't. nomnom/B52J.json with the release URL, hash, and Blueprinter and Heavy Hangar as dependencies; then submit it to the index. | 2 h | code |  |
| BR7 | — Before 1.0 | ★ | **Publish 1.0.0 as the latest release**. 0.4.1 is still marked pre-release. | 1 h | docs |  |
| BR8 | / Polish | ★ | **Release checklist**. RELEASING.md: version bump, build, smoke test, package, hash, manifest, notes. | 1 h | idea |  |
| BR9 | / Polish | ★ | **.gitignore tidy-up**. build/ is listed twice, and it mentions a CREDITS file that doesn't exist. | 0.5 h | code |  |
| BR10 | ○ Later |  | **Build check on every push**. A GitHub Action that compiles both plugins and lints the Python tools. | 4 h | idea |  |
| BR11 | ○ Later |  | **Remove the duplicate Heavy Hangar sources**. The same code lives in NO-HeavyHangar; two copies will drift apart. | 1 h | code |  |
| BR12 | ○ Later |  | **Delete one-off scripts**. patch_v020 to patch_v022 and six probe or render scripts that nothing uses. | 0.5 h | code |  |

## Docs and store page

README, images, changelog and loadout tables.

| ID | Priority | | Item | Effort | Evidence | Where |
|---|---|---|---|---|---|---|
| DC1 | — Before 1.0 | ★ | **Real in-game screenshots**. README and release images are flat editor renders on a blue background. Driver screenshots of takeoff, cruise over terrain, bay open, gear down on approach and the cockpit at dusk, plus a short GIF of the gear cycle. | 4 h | renders |  |
| DC2 | — Before 1.0 | ★ | **README rewrite for players**. Quick start, controls, FAQ, compatibility table, known issues. | 3 h | docs |  |
| DC3 | — Before 1.0 | ★ | **Broken table in LOADOUTS.md**. The MALD rows show as a separate table, and the SEAD preset isn't listed. | 0.5 h | docs |  |
| DC4 | — Before 1.0 | ★ | **Player changelog**. MODLOG is a 400-line dev diary; players need short release notes. | 2 h | docs |  |
| DC5 | ○ Later |  | **Telemetry report script**. Summarise a flight log for bug reports. | 2 h | idea |  |

## Code health

Tidy-ups that make the next version easier.

| ID | Priority | | Item | Effort | Evidence | Where |
|---|---|---|---|---|---|---|
| CH1 | / Polish | ★ | **One attach patch instead of seven**. Seven separate patches on Aircraft.Awake each check whether it's a B-52. | 1 h | code |  |
| CH2 | / Polish | ★ | **Telemetry start-up glitches**. The first g reading is garbage, an air start logs a fake lift-off, and a disk error throws on every spawn. | 1 h | code | `plugin/B52Systems/Telemetry.cs:43, 90` |
| CH3 | ○ Later |  | **Builder material cache isn't cleared**. A second build in the same editor session reuses deleted materials. | 0.5 h | code | `unity/B52Tools/Editor/B52Builder.cs:1099` |
| CH4 | ○ Later |  | **Hard-coded game path and version in BatchSetup**.  | 0.5 h | code |  |
| CH5 | ○ Later |  | **Small duplicates**. Two different knot conversions (1.944 and 1.94384) and three copies of FindDeep. | 0.5 h | code |  |
