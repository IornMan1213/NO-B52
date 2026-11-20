# B-52J release audit: what is left (updated 2026-11-20, evening)

The interactive version, where you tick what to do, is the **B-52J Release Planner** page:
https://claude.ai/artifact/6Tvz9Zd9JiRK3ZfRneArKW . Item IDs here match the page.

The first audit (2026-10-27, against 0.4.1) listed 91 items, and 39 more turned up during the work. **99 are done and
no longer listed** (the player-facing ones are in [CHANGELOG.md](../CHANGELOG.md)). Two were dropped on purpose: BR11,
removing the duplicate Heavy Hangar sources (both copies stay and are updated together), and BR10, the GitHub build
check (removed from the repo).

Priority: ✕ release blocker · — fix before 1.0 · / polish for 1.0 · ○ after 1.0. ★ = recommended for 1.0.
Status: where the item stands now. Evidence: code = confirmed in the source, renders = seen in renders, model scan =
measured from B52.fbx, in-game check = needs a flight to confirm, idea = new work.

**29 items left:** no blockers, 5 before 1.0, 11 polish, 13 later. Recommended for 1.0: 8 items, about 18 h.

## Road to 1.0

1. Pitch authority on approach (FP9), then the AI landing (MP1): the AI now flies the approach well but has no elevator left to flare.
2. Two questions for you: FP9 (how the B-52 flares for you) and EX24 (USAF markings on the side liveries).
3. The wing cut-end hairline (EX20).
4. List on NOMNOM (BR6) and publish 1.0.0 as the latest release (BR7), last, as you asked.

## Testing

| ID | Priority | | Item | Status | Effort | Evidence | Where |
|---|---|---|---|---|---|---|---|
| T4 | / Polish |  | **Stress test with several B-52s**. Four player or AI B-52s at one base: frame rate, physics and log spam. | Not started | 2 h | in-game check |  |
| T5 | ○ Later |  | **Unit tests for the maths**. Wave drag, bay and pylon layouts, CSV formatting. A small xunit project for the pure functions (MachDrag.WaveDragCoef, B52Weapons.BayLayout and PylonLayout counts, telemetry row formatting) that also runs in CI. | Not started | 3 h | idea |  |

## Plugin systems

| ID | Priority | | Item | Status | Effort | Evidence | Where |
|---|---|---|---|---|---|---|---|
| SY15 | ○ Later |  | **Sync the crab angle in multiplayer**. Other players see the trucks pointing straight ahead. | Not started | 3 h | idea |  |
| SY16 | ○ Later |  | **Tail hatch opens for the chute**. Animate the tail-cone compartment door when the chute deploys. | Not started | 4 h | idea |  |

## Flight model

| ID | Priority | | Item | Status | Effort | Evidence | Where |
|---|---|---|---|---|---|---|---|
| FP2 | — Before 1.0 | ★ | **Tune the autopilot for a 200 t bomber**. The autopilot gains came from the much lighter FastBomber. Fly altitude and heading hold at light and heavy weights, and damp any porpoising by tuning AutopilotPlane in the builder. *Now:* AI pitch gains as the stock heavy aircraft's; fly-by-wire corner speed lowered to 135 m/s (pitch authority at approach speed about doubled). The rest is MP1 and FP9. | Partly done | 2 h | in-game check |  |
| FP9 | — Before 1.0 | ★ | **Question: can you hold the nose up on a slow approach?**. In the AI tests, at 82 m/s with gear and flaps down, full nose-up stick only got the B-52 to 2-3 deg nose up while it sank 8 m/s. The fly-by-wire's angle-of-attack limit (10 deg) isn't what stops it, so it looks like elevator authority at low speed. Tell me how landings feel when you fly them: if flaring and holding the nose up is hard, I'll measure elevator authority and the trim at approach speed and fix it for players and the AI alike. *Now:* Measured in the AI landing tests: on final at 82-90 m/s with gear and flaps down, the fly-by-wire's elevator output sits at its -1 limit (full up) the whole approach, yet the nose stays at 2-3 deg and it can't flare (touchdown at 7 m/s sink). The offline trim model (tools/trim_check.py) says the approach needs only 6 deg of the 20 deg elevator, so something in game takes away pitch authority at low speed. Next: an in-game readout of elevator and stabiliser angle and angle of attack on approach, then fix it (more stabiliser trim, elevator effectiveness or CG). Question for you: do you find the B-52 hard to flare when you land it? | Needs you | 4 h | in-game check |  |
| FP4 | / Polish |  | **Engine-out handling**. Losing a pod should yaw the aircraft and need rudder. Shoot off one pod in a test and check thrust asymmetry, rudder authority and fly-by-wire yaw damping. | Not started | 3 h | in-game check |  |
| FP6 | ○ Later |  | **Visible wing flex**. B-52 wingtips droop on the ground and rise in flight; the rigid airframe shows none of it. Move each wing section's visible mesh onto a child node in the builder; the plugin bends them a few degrees by g-load and fuel weight. Physics stays rigid. | Not started | 1.5 d | idea |  |

## Exterior looks

| ID | Priority | | Item | Status | Effort | Evidence | Where |
|---|---|---|---|---|---|---|---|
| EX20 | / Polish | ★ | **Spoiler leftovers up close**. A thin dark line along one outboard panel on the left wing, a stepped edge behind the panels from the source model's slots (the black lattice with the flaps down), and a white speck on the right wing. *Now:* Fixed 20 Nov: the gap in the top skin at the outer flaps' front edge (and on the elevators). Still there: a fore-and-aft hairline at grazing angles where two wing parts meet (the source model's wing parts are open shells, and the cut ends meet with a crack you can see through), a white speck, and the toothed slot row (source geometry). Next: cap the parts' cut ends. | Partly done | 3 h | in-game check | `tools/blender_prep.py (spoilers)` |
| EX21 | / Polish | ★ | **Effects and values still from the donor**. Wingtip vortex trails, contact sparks, the transonic vapour cone and two fly-by-wire values are still the donor bomber's. Look at each in game and size it for the B-52, or note why the donor's is fine. *Now:* The two fly-by-wire donor values are reviewed and kept on purpose (recorded in the donor audit). The vapour effect's settings are already the B-52's. Left: the wingtip vortex and contact-spark particle visuals, which need a look in game. | Partly done | 1 h | in-game check |  |
| EX24 | / Polish | ★ | **Question: USAF markings on the side liveries?**. The Boscali and Primeva liveries keep the base skin's markings: the LA tail code, serial and the US insignia. Tell me whether to remove them on those two (plain tails), or to keep them. | Not started | 1 h | renders |  |
| EX10 | / Polish |  | **Smoother outline**. The airframe is about 17k triangles, and the nose and engine pods look faceted up close. Add supporting edge loops or subdivide the nose, pods and fuselage sections in blender_prep (target about 35k). | Not started | 1 d | model scan |  |
| EX12 | / Polish |  | **Contrails from all eight engines**. Check the game's contrail effect follows the cloned nozzles, and add it if not. | Not started | 3 h | in-game check |  |
| EX14 | ○ Later |  | **Small exterior details**. Antennas, pitot tubes, refuelling receptacle markings. | Not started | 1 d | idea |  |
| EX22 | ○ Later |  | **Structure inside a broken-off spoiler**. Spoiler panels have no ribs or inner skin, so one that's shot away shows nothing inside. | Not started | 2 h | idea |  |

## Cockpit

| ID | Priority | | Item | Status | Effort | Evidence | Where |
|---|---|---|---|---|---|---|---|
| CK17 | / Polish |  | **Detail the plain boxes**. Throttle pedestal, centre console and rudder pedals are still plain grey blocks. Switch-panel textures on the pedestal and console tops, shaped pedals, a quadrant with lever slots. *Now:* Rudder pedals now have ribbed treads, toe brakes, heel rests and the pivot shaft (blender_cockpit.py, in the 20 Nov build). Not looked at in game yet. | Needs in-game check | 3 h | renders |  |
| CK8 | ○ Later |  | **Animated gear and flap levers, working crab knob**. | Not started | 4 h | idea |  |
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
| MP1 | — Before 1.0 | ★ | **AI-flown B-52s crash on landing**. An AI B-52 takes off and climbs fine, then comes in to land at about 540 km/h, twice a B-52 approach speed, porpoising ±10°, and breaks up. Give the AI a lower approach speed and more drag on the approach (spoilers as speedbrake, gear down earlier), then rerun tests/ingame/b52_ai.txt until it lands and stops. *Now:* 20 Nov, 14 test flights in all. The AI B-52 now flies the pattern at 240 kt with at most 30 deg of bank, turns to final without diving, and follows the 3 deg glide path to the runway (last run: on the path at 3-5 m/s sink from 1.4 km in). It still touches down too hard (7 m/s sink, the gear and wings break): it has nothing left to flare with, because the fly-by-wire holds full up-elevator the whole approach. That is FP9. | Partly done | 4 h | in-game check | `tests/ingame/b52_ai.txt; B52Spec (AI approach speed); plugin SpoilerDriver` |
| MP2 | / Polish |  | **Showcase missions**. Two or three bundled missions: SEAD with MALD-J, an anti-ship LRASM strike, carpet bombing. | Not started | 1 d | idea |  |

## Performance

| ID | Priority | | Item | Status | Effort | Evidence | Where |
|---|---|---|---|---|---|---|---|
| PF6 | ○ Later |  | **Inside-only parts on the Cockpit layer**. On layer 14 the interior is drawn by both the main and cockpit cameras beyond 1 m (as stock). Move parts only seen from inside to layer 3 and give the outside view a simple stand-in, as the stock aircraft do. | Not started | 4 h | idea |  |

## Build and release

| ID | Priority | | Item | Status | Effort | Evidence | Where |
|---|---|---|---|---|---|---|---|
| BR6 | — Before 1.0 | ★ | **List the B-52 in the mod manager**. Heavy Hangar has a NOMM manifest; the B-52 doesn't. nomnom/B52J.json with the release URL, hash, and Blueprinter and Heavy Hangar as dependencies; then submit it to the index. *Now:* nomnom/B52J.json lists 0.9.1 and 0.9.0 with hashes; submit to NOMNOM last, as you asked. | Partly done | 2 h | code |  |
| BR7 | — Before 1.0 | ★ | **Publish 1.0.0 as the latest release**. 0.4.1 is still marked pre-release. *Now:* Last step, after the two-player test: publish 1.0.0 as Latest. | Needs you | 1 h | docs |  |

## Docs and store page

| ID | Priority | | Item | Status | Effort | Evidence | Where |
|---|---|---|---|---|---|---|---|
| DC5 | ○ Later |  | **Telemetry report script**. Summarise a flight log for bug reports. | Not started | 2 h | idea |  |
