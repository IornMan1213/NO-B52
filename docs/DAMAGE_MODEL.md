# B-52J damage model: findings and plan

Status, 18 Nov: steps 1 to 9 are built, step 8 now with the internal structure. Steps 1 to 6 and the gentle and hard belly landings are checked in game (`tests/ingame/b52_damage.txt`, `b52_crew_fuel.txt`, `b52_belly.txt`). Still to run in game: `b52_systems.txt` (hydraulics, torn stubs) and the belly test with impacts routed to each part. Step 10's two-player check needs a second player.

## Built

- **Structural failure** (`plugin/B52Systems/Structure.cs`). At spawn every part gets a root point, where it hangs on
  its parent, and an ultimate strength there. The strength is the worst of these design cases:
  - wing: 1 g at full fuel and at zero fuel, x 3.0 ultimate. Lift is split as the game's aero model really splits it,
    measured in game both clean and slow with the flaps down (`LiftShare`; re-measure with
    `tests/ingame/b52_loads.txt`). The ground case (full fuel, no lift) gives the strength for bending downward;
  - engine mounts: x 4.5, plus 1.5 x full thrust;
  - fuselage sections: x 3.0 of what they carry;
  - tail surfaces, flaps and spoilers, and the structure carrying them: a floor, the air load at 400 kt EAS with a
    normal-force coefficient of 1.2, x 1.5;
  - negative g at half the positive strength.

  The load at each root is split along its structural axes:
  - main bending, per sense;
  - torsion about the spar;
  - chordwise bending;
  - shear.

  Measured in game at 1 g:
  - wing roots: about 30% of ultimate, so they fail at about 3.3 g at that fuel load;
  - engine mounts: 22%;
  - nose: 26–34%.

  The fly-by-wire and the AI both hold 2.5 g. A 3.3–3.5 g pull at 220 m/s breaks the airframe.

  Each physics step it adds up the force and bending moment at every root, from:
  - the aero force at each part's aerodynamic centre;
  - thrust;
  - gravity;
  - the inertia of everything outboard: the body's acceleration, rotation rate and spin at each part.

  Damage weakens a root as the game weakens its joints: `(hp - structuralThreshold) / (100 - structuralThreshold)`.
  Damage to the parent part weakens it half as much. When the load passes what's left, or the hit points pass the
  threshold, the part comes off through `Unit.DetachPart`, which the server sends to every client.

  After a part comes off:
  - the parts outboard of it go with its rigidbody;
  - the aircraft's mass and centre of mass follow (`MassSync`);
  - the debris keeps its real lever arms (`AeroCentres`);
  - an AI crew bails out 2 s after the aircraft loses a wing root, an inner wing, a fuselage section or the cockpit.
- **Armour per part** (`B52Builder.Armour`): the centre box and wing roots are toughest, then the wing, fuselage and
  pods, with control surfaces weakest. The structural thresholds run from -100 (centre box) to -25 (control surfaces).
- **Engines and their pods**: each Turbofan has its pod as a critical part. Once the pod is below 60 hp, every further
  point of damage to it reaches both engines, and a pod that comes off kills both. F130 core armour is 5 / 3 / 3 / 2.
- **Tests**: `tests/ingame/b52_damage.txt`, using the driver commands `hit`, `structure`, `expectoff` and
  `expectdead`.
- **Where effects start** (step 3). The donor bomber's parts have no effects of their own: its smoke and fire come
  from its fuel tanks and engines. Tank effects start at each tank (below), and each engine's fire and smoke start
  at its nozzle. The game's only stock smoke for damaged structure is its ground vehicles' smoulder, and that goes out
  at flying speed and deletes itself within a second, so the B-52 adds none: bare structure doesn't burn.
- **Fuel tanks** (steps 3 and 5; builder `BuildTank`, `B52Systems.FuelTanks`).
  - Each of the 11 tanks is its own object at the centroid of its fuel, so leaks, fires and fireballs start there.
    A burning tank heats whatever is around it: the game burns everything within 1.5 part radii.
    - Wing tanks: strips along the span, weighted by chord squared, at 35% chord.
    - Centre wing tank: on the centreline, in line with the wing roots.
  - Sizes come from the tank, rates from the hole.
    - A tank torn open empties in about a minute.
    - The integral wing tanks, whose skin is the tank wall, leak below 75 hp; the body bladder tanks leak below 55 hp.
    - At the part's structural threshold, the holes leak at 60% of an open tear.
    - Fire heat runs from 1.5 (wingtip tanks) to 3.0 (the biggest).
  - Each tank feeds through the one inboard of it. A part that comes off tears the line into the next tank in:
    wingtip → outer wing → inner wing → wing root → centre wing, and both body tanks → centre wing.
  - Fuel order (`FuelTransfer`): the fuselage tanks empty first, then the centre wing, then the wing tanks from the
    root out. Keeping fuel out in the wing to the end eases the bending at the wing root for as long as possible.
    This is the usual rule for big jets; the B-52's own fuel schedule isn't modelled panel for panel.
    - The B-52 leaves the fuel draw itself to the game, or to a mod that orders it: the Aryx weapons pack draws its
      drop tanks first, then the internal tanks in proportion.
    - After each physics step, the B-52's transfer pumps make up whatever the engines took from a tank later in the
      order, from the earliest tanks that still have fuel.
    - The pumps never touch another mod's tanks. They never pump into a leaking tank, or into a tank on a part that
      has come off.
  - The structure model carries each tank's fuel at the tank, not at the part's centre.
  - Two game paths assume a tank sits on its part's own object, and are routed for the B-52's own tanks: the fire
    and rupture message, and crash impacts (`TankImpacts`). The game's own handling still runs after, so other mods'
    tanks on the aircraft work as they do on any aircraft.
- **Crew and escape** (step 6; builder `AddCrew`, `B52Systems.DownwardSeats`).
  - Five crew, as on the B-52H:
    - pilot and co-pilot;
    - the EWO, on the aft-facing seat at the left of the upper deck;
    - the radar navigator and navigator, side by side on the lower deck, facing forward.
  - The right aft seat (the old gunner's, now the instructor's) stays empty: six seats, five crew.
  - Hits reach each crewman through the game's own damage to the crew: shrapnel and rounds that get through the
    skin.
  - Every station has an ejection seat. The donor bomber's crew have none and bail out of the side 1.5 s apart.
  - The ejection sequence fires the seats one by one, last crewman first.
  - The two lower-deck seats fire downward: 2.2 m down a rail through the belly at 60% of the upward seats' thrust.
    Then the crewman falls free on the seat, and the game's own sequence takes over (seat separation, parachute).
    Other clients run the same rail.
  - Not modelled: the belly hatches have no mesh, and the co-pilot cannot take over. The pilot's seat is the one the
    game flies from, so losing the pilot loses the aircraft, as on every aircraft in the game.
- **Tests**: `tests/ingame/b52_crew_fuel.txt`, using the driver commands `crew`, `tanks`, `fuelwatch`, `expecttank`
  and `ejectwatch`.

- **Systems** (step 7; `B52Systems.Hydraulics`).
  - The B-52 rolls with spoilers only, and they are hydraulic; its elevators and rudder are moved by aerodynamic
    control tabs through cables, so they keep working without hydraulic power.
  - Modelled as: each wing's spoilers are powered by pumps on that wing's engines. They work while at least one engine
    on that wing runs. Without pressure, or with the spoiler panels shot through, they go flush and stay there
    (`ControlSurface.SetLocked`). The real aircraft has six hydraulic systems; this keeps what matters to the pilot.
  - Landing gear: a leg whose mounting part is shot through (0 hp) collapses as soon as it carries load. The game
    already breaks a leg that is over-compressed or side-loaded beyond its spring.
  - Not modelled: bay doors jamming. Done properly it also has to stop weapon release; without that, bombs would fall
    through closed doors.
- **Breaks** (step 8; `B52Systems.BreakStubs`, `tools/blender_structure.py`). An aircraft is not a tube: a part that
  comes off leaves torn structure on both sides of the break.
  - **Internal structure.** Every flight-model part has its structure built inside it from its own skin
    (`<part>_struct`): wings, tail surfaces, flaps and spoilers have ribs every 0.7 m (truss ribs with a lightening
    hole), front and rear spars at 15% and 65% chord on straight lines, and stringers. Fuselage sections and engine pods
    have frames every 0.6 m with stringers every 15 degrees, two of them heavier longerons. Each part also has an
    inner skin in zinc-chromate primer, so a break looks into the structure, not out through the far side of the part.
    It ships switched off and costs nothing to draw until the part, or one attached to it, breaks away.
  - **At the break.** The skin is ripped back in jagged petals that curl outward (bare metal outside, soot inside), and
    the spar webs and stringers stick out of it, snapped at different lengths. The section is measured from the seam
    the two parts share (a pod's from the top of its pylon).
  - **Debris.** A burst of bent skin fragments and a puff of smoke at the break, left behind in the airflow.
  - **The piece that comes off** swings away about its root under the load it was carrying: lift folds a wing up,
    drag tears a pod back.
  - All of it is built from the parts' geometry with a seed from the part's name, so every client sees the same break.
    The torn stub also adds its drag to the part it came off (`streamlining`, from the B-52's section sizes).
- **Ground loads** (`B52Systems.Structure`). The landing gear's force (each strut's spring and damper, and the brakes'
  drag) is part of the loads on the parts it is mounted on, and those parts are built for the full force their gear
  can deliver. Without it, standing on the runway read as the fuselage hanging unsupported, and a small bump snapped
  the nose.
- **Anti-skid** (`B52Systems.Hydraulics`). Above taxi speed each wheel brake is held to 45% of the tyres' grip, about
  0.35 g with the chute: the game's brakes used all of it and stopped the B-52 at 1 g, nose down on the runway.
- **Impacts** (step 9; `B52Builder.Impact`, `B52Systems.ImpactRouting`).
  - Every part takes impact damage, sized for a 200 t aircraft: the keel and fuselage take it best, the nacelles,
    wingtips and control surfaces worst. The donor only had it on its root.
  - The game measures a contact as its impulse in one physics step, in g of the aircraft. A belly landing at 1 m/s
    sink does no damage; at 4 m/s the keel loses about half its hit points.
  - With the B-52 flying as one rigid body, the game's collision message reaches only the centre section. The damage
    is routed to the part that actually touched, with that part's own threshold.
- **Tests**: `tests/ingame/b52_belly.txt` (belly landings at 1 and 4 m/s).

## What the mod did before the damage model (the findings)

Every value below was read from the built prefab (`unity/Mods/B52/B52.prefab`) and from the game's own code.

| Area | Today | Why it matters |
|---|---|---|
| Part hit points and armour | All 28 parts are at 100 hp, with the donor Alkyon's armour values (two sets: one for big structure, one for flaps, spoilers and pods). | A wing root that carries 40 t takes damage exactly like a flap. |
| Breaking off | The B-52 flies as one rigid body (B52Systems' RigidAirframe), which merges every part joint ("27 of 27 joints broken" in the test log). The game breaks aircraft parts off only through those joints, so **no part can ever come off**: 0 of 28 parts detached, whatever the damage. | Wings, engines and the tail never come off; a B-52 can only "die" whole. |
| Critical parts | None: no part is marked critical. | The aircraft is never shot down by losing the cockpit or the centre box. |
| Damage effects | No part has effects of its own: the donor's smoke and fire come from its tanks and engines. The game spawns a tank's effects at its part's pivot. | A wing fire shows at the wing section's pivot, not at the tank that's burning. |
| Fragmenting | `integrityThreshold` is at minus infinity on every part. | Parts never break up. |
| Engines | 8 engine components with the donor's armour (5/2). Losing a pod doesn't stop its two engines. | |
| Fuel | 11 tanks, 142 t in all (the real B-52H holds about 141 t), but the leak and fire thresholds are the donor's. | The total is right; the behaviour isn't tuned. |
| Crew | 2 crew, the donor's "Pilot" and "WSO"; one ejection seat fires. | The B-52H has 5 crew and 6 ejection seats: 4 fire upward, 2 (the navigators) downward. |

## Plan

1. **Structural failure (custom, B52Systems).** Each part gets a strength: its design limit load (+2.0 g limit, 3.0 ultimate), scaled down as it loses hit points. Each physics step it is checked against the part's real aero and inertial load (already computed for AeroCentres and ground effect). When the load exceeds the strength, or the hit points drop below the part's structural threshold, the part comes off through the game's own `DetachPart`. That call is server-side and sent to every client, so multiplayer stays in sync. Parts break off along the real load paths, outer pieces before inner ones:
   - wing: wingtip, then wing2, then wing1, then wingroot;
   - engines: each pod off its strut;
   - tail: hstab, then fin, then the whole aft fuselage.
2. **Hit points and armour per part, from the real structure.**
   - The centre wing box and wing roots are toughest, then the fuselage sections, then the pods. Control surfaces and flaps are weakest.
   - Tolerances scale with skin thickness and spar depth.
   - The cockpit section (the crew) and the centre box are critical.
   - The aircraft definition's `damageTolerance` is raised to match a 200 t airframe.
3. **Damage effects where they belong.** Each part gets effect anchors at its real sources: tank centroids, each engine, hydraulic bays. A small patch spawns effects there instead of at the part's origin, so you'd see an engine fire on the right engine, a fuel leak trailing from the wing tank it comes from, and smoke from the pod that was hit.
4. **Engines.**
   - Each engine has its own health: a hit on a pod damages one or both of its engines, by position.
   - Engines fail, catch fire and windmill on their own, and a pod that comes off takes its two engines with it.
   - The thrust asymmetry is real, so this ties in with engine-out handling (FP4).
5. **Fuel system.**
   - Leak and fire thresholds come from tank size and position.
   - Tanks are connected so fuel transfers the way it does on the B-52.
   - A rupture leaves a fuel trail.
   - A fire spreads to neighbouring tanks over time.
6. **Crew and escape.**
   - 5 crew in their real stations: pilot and co-pilot on the flight deck; the EWO behind them; radar navigator and navigator on the lower deck.
   - 6 ejection seats: 4 fire upward, 2 downward.
   - Crew can be hurt or killed by hits on their part. Losing both pilots means the aircraft is lost.
7. **Systems.**
   - Hydraulics are powered from engine-driven pumps. Losing pods loses systems, which in turn weakens the control surfaces and spoilers.
   - A damaged wing loses its spoilers.
   - The gear can collapse on a hard landing or after hits.
   - The bay doors can jam.
8. **Visual damage.**
   - The damage texture exists already.
   - New: break meshes (jagged stubs) where each part comes off, and debris that falls.
   - Burned or holed skin decals at the hit points.
9. **Impacts.** Ground and collision thresholds sized for a 200 t aircraft, plus belly-landing scrape damage.
10. **Tests.** Driver scenarios that, by checks with clear pass/fail lines:
    - hit each part (the existing `hurt` command);
    - overload the wing (`stick` at high speed);
    - check each detach, effect anchor, engine failure and crew loss.

    Plus a two-player check that other clients see the same failures.

Suggested order: 1, 2, 4 (the airframe and engines), then 3, 5, 6, then 7 to 10.
