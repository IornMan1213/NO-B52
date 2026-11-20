# B-52J release audit: what is left (updated 2026-11-20)

The interactive version, where you tick what to do, is the **B-52J Release Planner** page:
https://claude.ai/artifact/6Tvz9Zd9JiRK3ZfRneArKW . Item IDs here match the page.

The first audit (2026-10-27, against 0.4.1) listed 91 items, and 34 more turned up during the work. **81 are done and
no longer listed** (the player-facing ones are in [CHANGELOG.md](../CHANGELOG.md), up to 0.9.1). Two were dropped on
purpose: BR11, removing the duplicate Heavy Hangar sources (both copies stay and are updated together), and BR10, the
GitHub build check (removed from the repo). T3, the two-player test, is done: you've flown it in multiplayer.

Priority: ✕ release blocker · — fix before 1.0 · / polish for 1.0 · ○ after 1.0. ★ = recommended for 1.0.
Status: where the item stands now. Evidence: code = confirmed in the source, renders = seen in renders, model scan =
measured from B52.fbx, in-game check = needs a flight to confirm, idea = new work.

**43 items left:** no blockers, 6 before 1.0, 19 polish, 18 later. Recommended for 1.0: 13 items, about 34 h.

## Road to 1.0

1. Fix the AI landing (MP1) and the weak full-load climb (FP8): the two flight problems the last test runs found.
2. Run every in-game test on one build (T7), then a night flight with night vision (T9).
3. List on NOMNOM (BR6) and publish 1.0.0 as the latest release (BR7), last, as you asked.

## Testing

| ID | Priority | | Item | Status | Effort | Evidence | Where |
|---|---|---|---|---|---|---|---|
| T7 | — Before 1.0 | ★ | **Run every in-game test on the 0.9.1 build**. Flight, systems, belly landing, damage, crew and fuel, AI, full load, looks and spoilers have each passed on some build, but not all on the current one. tests/ingame/run.sh over every b52_*.txt in a row, failing on any B-52 exception in the log. *Now:* On 19 Nov systems, belly landing, crew and fuel, damage and spoilers passed; AI and full load failed (MP1, FP8). Not all rerun on 0.9.1. | Partly done | 3 h | in-game check |  |
| T8 | / Polish | ★ | **The g test's measurement glitch**. The g readings in the test runs can't be trusted yet, so the airframe-strength checks are weaker than they look. | Not started | 1 h | code |  |
| T9 | / Polish | ★ | **Night flight with night vision**. The best single check of the cockpit dimming: if the screens are too dim or too bright, the levels are quick to adjust. | Not started | 0.5 h | in-game check |  |
| T4 | / Polish |  | **Stress test with several B-52s**. Four player or AI B-52s at one base: frame rate, physics and log spam. | Not started | 2 h | in-game check |  |
| T10 | / Polish |  | **A flaps command for the test driver**. The flaps-down view (the dark lattice behind the spoilers) has only been checked in Unity renders, because the driver can't lower the flaps. | Not started | 1 h | idea | `tests/ingame/NODevDriver/Driver.cs` |
| T5 | ○ Later |  | **Unit tests for the maths**. Wave drag, bay and pylon layouts, CSV formatting. A small xunit project for the pure functions (MachDrag.WaveDragCoef, B52Weapons.BayLayout and PylonLayout counts, telemetry row formatting) that also runs in CI. | Not started | 3 h | idea |  |

## Plugin systems

| ID | Priority | | Item | Status | Effort | Evidence | Where |
|---|---|---|---|---|---|---|---|
| SY17 | / Polish |  | **Speedbrake at idle on the runway is inconsistent**. Spawned at the hangar, the spoilers rose at idle; spawned on the runway they stayed flush. Not yet worked out why. | Not started | 1 h | in-game check |  |
| SY15 | ○ Later |  | **Sync the crab angle in multiplayer**. Other players see the trucks pointing straight ahead. | Not started | 3 h | idea |  |
| SY16 | ○ Later |  | **Tail hatch opens for the chute**. Animate the tail-cone compartment door when the chute deploys. | Not started | 4 h | idea |  |

## Flight model

| ID | Priority | | Item | Status | Effort | Evidence | Where |
|---|---|---|---|---|---|---|---|
| FP2 | — Before 1.0 | ★ | **Tune the autopilot for a 200 t bomber**. The autopilot gains came from the much lighter FastBomber. Fly altitude and heading hold at light and heavy weights, and damp any porpoising by tuning AutopilotPlane in the builder. *Now:* Not tuned yet. The AI landing problem (MP1) is the clearest sign: it porpoises ±10° on the approach. | Partly done | 2 h | in-game check |  |
| FP8 | — Before 1.0 | ★ | **Weak climb-out at full load**. At full load the B-52 is only 57 m up 10 s after lift-off, with the gear retracting: under the full-load test's threshold. Measure the climb against the real B-52H's at maximum weight, then tune thrust, drag or the test threshold. | Not started | 3 h | in-game check | `tests/ingame/b52_fullload.txt` |
| FP4 | / Polish |  | **Engine-out handling**. Losing a pod should yaw the aircraft and need rudder. Shoot off one pod in a test and check thrust asymmetry, rudder authority and fly-by-wire yaw damping. | Not started | 3 h | in-game check |  |
| FP6 | ○ Later |  | **Visible wing flex**. B-52 wingtips droop on the ground and rise in flight; the rigid airframe shows none of it. Move each wing section's visible mesh onto a child node in the builder; the plugin bends them a few degrees by g-load and fuel weight. Physics stays rigid. | Not started | 1.5 d | idea |  |

## Exterior looks

| ID | Priority | | Item | Status | Effort | Evidence | Where |
|---|---|---|---|---|---|---|---|
| EX9 | / Polish | ★ | **Livery pack**. Vietnam camouflage with black belly, Cold War white anti-flash belly, NASA NB-52B 008, and one livery per game faction. *Now:* Done: Gunship Gray clean, and Southeast Asia camouflage with black or white belly (in 0.9.1). Still to do: NASA NB-52B 008 and a livery per game faction. | Partly done | 1.5 d | idea | `docs/livery_template.png; B52Builder.MakeDefinition (liveries)` |
| EX20 | / Polish | ★ | **Spoiler leftovers up close**. A thin dark line along one outboard panel on the left wing, a stepped edge behind the panels from the source model's slots (the black lattice with the flaps down), and a white speck on the right wing. | Not started | 3 h | in-game check | `tools/blender_prep.py (spoilers)` |
| EX21 | / Polish | ★ | **Effects and values still from the donor**. Wingtip vortex trails, contact sparks, the transonic vapour cone and two fly-by-wire values are still the donor bomber's. Look at each in game and size it for the B-52, or note why the donor's is fine. | Not started | 1 h | in-game check |  |
| EX10 | / Polish |  | **Smoother outline**. The airframe is about 17k triangles, and the nose and engine pods look faceted up close. Add supporting edge loops or subdivide the nose, pods and fuselage sections in blender_prep (target about 35k). | Not started | 1 d | model scan |  |
| EX12 | / Polish |  | **Contrails from all eight engines**. Check the game's contrail effect follows the cloned nozzles, and add it if not. | Not started | 3 h | in-game check |  |
| EX14 | ○ Later |  | **Small exterior details**. Antennas, pitot tubes, refuelling receptacle markings. | Not started | 1 d | idea |  |
| EX22 | ○ Later |  | **Structure inside a broken-off spoiler**. Spoiler panels have no ribs or inner skin, so one that's shot away shows nothing inside. | Not started | 2 h | idea |  |
| EX23 | ○ Later |  | **Rudder shading**. The rudder is only 8 triangles, so its shading is uneven whatever its normals (about 1 m² off by over 45°). Add a few edge loops to the rudder in blender_prep. | Not started | 1 h | model scan |  |

## Cockpit

| ID | Priority | | Item | Status | Effort | Evidence | Where |
|---|---|---|---|---|---|---|---|
| CK5 | / Polish |  | **Airspeed gauge may show true airspeed**. At altitude true airspeed reads far above indicated, so the README's approach speeds won't match the gauge. Check what the game's speed value is; if it's true airspeed, convert to indicated. | Not started | 1 h | in-game check |  |
| CK6 | / Polish |  | **Panel lighting at night**. Backlit gauges and switch labels, plus a dim flood light. | Not started | 4 h | idea |  |
| CK17 | / Polish |  | **Detail the plain boxes**. Throttle pedestal, centre console and rudder pedals are still plain grey blocks. Switch-panel textures on the pedestal and console tops, shaped pedals, a quadrant with lever slots. *Now:* Pedestal has lever slots and switch panels on three sides; console tops are textured. The rudder pedals are still plain plates. | Partly done | 3 h | renders |  |
| CK8 | ○ Later |  | **Animated gear and flap levers, working crab knob**. | Not started | 4 h | idea |  |
| CK9 | ○ Later |  | **Warning lights**. Master caution and gear-unsafe lights, using the game's cockpit warning lights. *Now:* A row of caution and warning lights is painted under the glareshield (0.9.1), but nothing lights them yet. | Partly done | 3 h | idea |  |
| CK16 | ○ Later |  | **Baked shading for the cockpit**. Corners and under-panel areas have no shadow, so boxes look flat. Bake ambient occlusion for the interior in Blender into a small second-UV texture and plug it into the URP occlusion slot. | Not started | 6 h | idea |  |

## Sound

| ID | Priority | | Item | Status | Effort | Evidence | Where |
|---|---|---|---|---|---|---|---|
| SD2 | / Polish |  | **Systems sounds**. Chute pop, gear motors, door thunk, spoiler hydraulics, reusing the game's own clips. | Not started | 3 h | idea |  |
| SD3 | ○ Later |  | **Engine sound for the F130**. A proper high-bypass sound set; needs audio you're allowed to ship. | Not started | 1.5 d | idea |  |

## Weapons and loadouts

| ID | Priority | | Item | Status | Effort | Evidence | Where |
|---|---|---|---|---|---|---|---|
| WP7 | / Polish |  | **Multiplayer balance option**. 20 nuclear cruise missiles or 80 JDAMs per sortie may be too much for some servers. A config switch for lower counts, or a higher cost for the nuclear presets. This one is your call. | Not started | 3 h | idea |  |
| WP8 | ○ Later |  | **B-52-specific weapon models**. JASSM-ER, ALCM and MALD reuse the game's closest munitions. | Not started | 2.5 d | idea |  |
| WP9 | ○ Later |  | **Rotary launcher turns between releases**. | Not started | 4 h | idea |  |

## AI and missions

| ID | Priority | | Item | Status | Effort | Evidence | Where |
|---|---|---|---|---|---|---|---|
| MP1 | — Before 1.0 | ★ | **AI-flown B-52s crash on landing**. An AI B-52 takes off and climbs fine, then comes in to land at about 540 km/h, twice a B-52 approach speed, porpoising ±10°, and breaks up. Give the AI a lower approach speed and more drag on the approach (spoilers as speedbrake, gear down earlier), then rerun tests/ingame/b52_ai.txt until it lands and stops. *Now:* Takeoff and climb-out work (AI B-52s no longer bail out or circle at the runway entrance). The landing fails: about 540 km/h, porpoising, breaks up (test b52_ai, 19 Nov). | Partly done | 4 h | in-game check | `tests/ingame/b52_ai.txt; B52Spec (AI approach speed); plugin SpoilerDriver` |
| MP2 | / Polish |  | **Showcase missions**. Two or three bundled missions: SEAD with MALD-J, an anti-ship LRASM strike, carpet bombing. | Not started | 1 d | idea |  |

## Performance

| ID | Priority | | Item | Status | Effort | Evidence | Where |
|---|---|---|---|---|---|---|---|
| PF7 | / Polish | ★ | **Memory use with the 8192 px skin**. The download is 24.5 MB; how much video memory the bigger skin and damage textures take in game hasn't been measured. | Not started | 1 h | in-game check |  |
| PF6 | ○ Later |  | **Inside-only parts on the Cockpit layer**. On layer 14 the interior is drawn by both the main and cockpit cameras beyond 1 m (as stock). Move parts only seen from inside to layer 3 and give the outside view a simple stand-in, as the stock aircraft do. | Not started | 4 h | idea |  |

## Build and release

| ID | Priority | | Item | Status | Effort | Evidence | Where |
|---|---|---|---|---|---|---|---|
| BR6 | — Before 1.0 | ★ | **List the B-52 in the mod manager**. Heavy Hangar has a NOMM manifest; the B-52 doesn't. nomnom/B52J.json with the release URL, hash, and Blueprinter and Heavy Hangar as dependencies; then submit it to the index. *Now:* nomnom/B52J.json lists 0.9.1 and 0.9.0 with hashes; submit to NOMNOM last, as you asked. | Partly done | 2 h | code |  |
| BR7 | — Before 1.0 | ★ | **Publish 1.0.0 as the latest release**. 0.4.1 is still marked pre-release. *Now:* Last step, after the two-player test: publish 1.0.0 as Latest. | Needs you | 1 h | docs |  |
| BR15 | / Polish | ★ | **Fail the build instead of using box pylons**. If blender_pylons.py didn't run, the builder quietly puts grey boxes on the wings. | Not started | 0.5 h | code | `unity/B52Tools/Editor/B52Builder.cs (BuildHardpoints, CreatePrimitive)` |
| BR12 | ○ Later |  | **Delete one-off scripts**. patch_v020 to patch_v022 and six probe or render scripts that nothing uses. | Not started | 0.5 h | code |  |

## Docs and store page

| ID | Priority | | Item | Status | Effort | Evidence | Where |
|---|---|---|---|---|---|---|---|
| DC5 | ○ Later |  | **Telemetry report script**. Summarise a flight log for bug reports. | Not started | 2 h | idea |  |

## Code health

| ID | Priority | | Item | Status | Effort | Evidence | Where |
|---|---|---|---|---|---|---|---|
| CH4 | ○ Later |  | **Hard-coded game path and version in BatchSetup**. | Not started | 0.5 h | code |  |
| CH5 | ○ Later |  | **Small duplicates**. Two different knot conversions (1.944 and 1.94384) and three copies of FindDeep. | Not started | 0.5 h | code |  |
