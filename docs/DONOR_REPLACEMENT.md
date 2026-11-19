# Replacing the donor bomber

The B-52 is built starting from the game's FastBomber1 prefab: the builder copies its components across and overwrites
what it knows. `docs/DONOR_AUDIT.md` (written by every build) lists, setting by setting, what is still the donor's. This
is the plan to take that to nothing, so every value in the B-52 is chosen for the B-52.

## What "custom" means here

- **The game's component types stay.** `Aircraft`, `AeroPart`, `Turbofan`, `LandingGear` and the rest are how the game
  runs any aircraft; a mod has to use them. What becomes custom is everything they are given.
- **Every setting is set on purpose, with its reason** (`B52Spec`, in the builder): from B-52H/J data where there is
  data, from the B-52's size and the game's own scale where there is not (with the working shown), and from the game's
  own default where the setting has no B-52 meaning (e.g. thrust vectoring: none). The audit counts a setting as
  chosen once the builder sets it, even where the B-52 value equals the donor's.
- **Donor geometry goes:** collision meshes, nav-light housings, the canopy glass's mask.
- **Shared game assets are a separate decision:** the game's sounds, smoke and fire textures, pilot model and seat are
  used by every aircraft in the game, not the donor's own. Effects and sound *settings* (sizes, rates, volumes) become
  the B-52's own in phase 6. The pilot character stays the game's (decided).

## Phases

Each phase ends with a build, the audit, and the in-game tests named.

1. **Collision and flight-model parts** (28 AeroParts, root collider, Rigidbody).
   - Root collider: the donor's fuselage collision mesh -> convex hulls of the B-52's own centre section.
   - Per part: streamlining, buoyancy (floats after ditching), airflow channelling, impact damage (threshold and
     multiplier, sized for a 200 t aircraft: damage step 9), damage-texture threshold, integrity threshold, the rest
     of the armour (blast, fire armour, overpressure), critical parts (cockpit and the centre box), break sounds.
   - Rigidbody: mass, drag, interpolation and collision detection set for a 200 t aircraft.
   - Tests: `b52_flight.txt`, `b52_damage.txt`, `b52_crew_fuel.txt`; a ground impact and a belly landing.
2. **Engines and nozzles** (8 Turbofan, 8 JetNozzle) from F130 data: RPM range (N1), start-up rate, the density
   limit for relight and flame-out, damage threshold and core armour, heat-haze size/life/rate for a high-bypass fan,
   IR signature (a high-bypass turbofan is far cooler than the donor's low-bypass jets), no thrust vectoring, engine
   failure message. Tests: `b52_flight.txt` (thrust, spool), `b52_live` checks, engine-out.
3. **Gear, control surfaces, flaps, fuel tanks.**
   - Gear: friction, tyre contact area, response, compression, drag when down, which trucks steer and brake (the
     B-52's four main trucks all steer for crosswind crab), break force (damage step 7: gear collapse).
   - Control surfaces and flaps: travel limits, split/brake (none on the B-52), the Fowler flaps' motion.
   - Fuel tanks: rupture and ignition thresholds from tank type (integral wing tanks, bladder body tanks).
   - Bay doors: open/close speeds of the B-52's doors; jamming (damage step 7).
4. **Systems and the aircraft definition.**
   - Radar (AN/APQ-188), targeting pod view, flares (the B-52H's ALE-20 dispensers), jammer (ALQ-172 / ALQ-249 era),
     power supply (8 engine-driven generators), target detection, autopilot and fly-by-wire limits, controls filter.
   - Crew: armour of the crew stations, exit directions, the downward seats.
   - Definition and parameters: identity and roles, icons and ranges, capture values, armour tier and damage tolerance,
     AoA buffet, PID reference speed, corner speed.
   - Hydraulics (damage step 7): engine-driven pumps; losing pods weakens the surfaces and spoilers.
5. **Ejection seats.** The B-52's own seats (ACES II upward, the downward seats on the lower deck) instead of the
   game's generic seat mesh. The crew stay the game's pilot character (decided: no custom crew model).
6. **Effects and sound settings.** The 20 particle systems (tyre smoke, heat haze, vapour cone, wingtip vortices,
   sparks, nav-light glows) and 25 audio sources, set for the B-52's size and engines. Damage step 8: break stubs where
   parts come off.

## Order tonight

Phase 1 with damage step 9 (impacts), phase 2, damage step 7 (systems) with phases 3 and 4, then phase 6 and damage
step 8. Each is checked in game before the next. Phase 5 (the seats) needs new models and is last.

## Status (15 Nov)

Phases 1 to 6 are built; the numbers are the build's own audit (`docs/DONOR_AUDIT.md`). Of 17,250 settings in components
that came from the donor, 2,741 are still the donor's (16%, from 96%): 2,739 of them are in the four effects listed
below, and the other 2 are the fly-by-wire values below. Phase 5 (the seats) is built too.

In game: phase 1 was checked (flight, belly, crew and fuel tests). The full build loaded and its rollout and ground
clearance are normal (`b52_clearance.txt`: 463 m from 250 km/h, every collider over a metre clear while braking). The
rest of the test set (`b52_flight`, `b52_systems`, `b52_belly`, `b52_damage`, `b52_crew_fuel`, `b52_ai`,
`b52_fullload`) still has to run on this build: the game can't start while the screen is locked.

- **Done:** collision, all 28 flight-model parts, the 8 engines and nozzles, gear, control surfaces, flaps, fuel tanks,
  bay doors, sensors, flares, ECM, power, autopilot, fly-by-wire, crew settings, the lower deck's seats and consoles,
  every audio source, the nav lights and their housings, heat haze, tyre dust, nav-light glows, the vapour emitters
  (with the B-52's contrails, which the donor didn't have), and the whole aircraft definition and parameters.
- **Bugs found on the way and fixed:**
  - The centre section collided and was hit as the donor's fuselage shape.
  - Every gear leg set the drag of the part it hangs on to the donor's 5 m2, over four times the clean aircraft's
    drag when the gear was down.
  - Impact damage reached only the centre section, for every part's contact.
  - The donor's glass pane mask drew dark patches on the windscreen.
- **Left as the game's, on purpose:** the pilot character (decided), the networking components (they must stay the
  game's), and the game's shared assets: sounds, smoke and fire materials, icons, the HUD.
- **Still the donor's configuration:** the two wingtip-vortex trails, the contact sparks and the transonic vapour cone.
  The game feeds them from its own emitters through floating-origin and ribbon-trail setups, and rebuilding them needs
  a visual check in game. Two fly-by-wire values whose meaning isn't clear from the game code
  (`postStallManeuverSpeed`, `iFactor`) are also the donor's.
