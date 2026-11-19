# Changelog

Player-facing changes in each release. The development diary with every detail is [MODLOG.md](MODLOG.md).

## Unreleased (next: 1.0.0)
- **Optional B-52H tail turret.** A new "Tail Turret" station in the loadout screen: the B-52H's M61A1 20 mm
  six-barrel cannon in the tail, aimed by its radar. It engages fighters closing from behind on its own
  (the game's own "engage at will / hold fire" turret toggle), or select it to aim it with your view. Empty by default:
  the B-52J has no gun (the tail guns came off from 1991), so the barrels show only when it's loaded. AI B-52Js fly
  without it unless the AiTailGun setting allows it.
- **Breaks show the aircraft's structure.** A wing, tail or pod that comes off leaves ripped skin with its spars and
  stringers sticking out, and you can see its ribs, frames and primer-green inner skin through the break (on both
  pieces). Fragments and smoke burst from the break, and the broken piece folds away under its own load.
- **Cockpit damage display:** the B-52's own, not the Alkyon's: its top view with all 28 sections, each lighting
  yellow to red as it is hit, and eight engine-fire lights.
- **New flight deck.** Rebuilt from scratch to fit the B-52's real cabin, which narrows forward and has walls that
  lean in: nothing pokes through the skin or floats off the panels any more.
  - A B-52J glass cockpit: each pilot has a primary flight display and a tactical display, with the engine display
    between them. Standby airspeed, attitude, altitude and vertical speed instruments are set flush outboard.
  - The engine fire handles run along the top of the panel, under a glareshield cut to the windscreen line.
  - The lower deck is closed in, with lined walls, a ceiling, bulkheads and a ladder up to the flight deck. Its floor
    now sits on the belly instead of below it, the navigators' consoles follow the curving walls, and the two
    navigators sit higher, at a natural eye height.
  - Every console, wall panel, the overhead and the aisle stand are proper sub-panels on their fasteners, with real
    switches, guards and knobs standing on them.
  - Also new: the throttle quadrant with speed brake and flap levers, ram's-horn control wheels, ejection seats with
    handles and harnesses, and quilted insulation lining the walls and ceiling between the airframe's frames.
- **Tester fixes (looks):**
  - The paint is much less shiny, matching the game's own aircraft: a flat, weathered Gunship Gray with a
    roughness map (rougher in the grime and along panel lines, a little variation panel to panel).
  - Sharper skin: the exterior texture is now 8192 px (was 4096), with crisp engraved panel lines.
  - Battle damage builds up naturally: bullet and shrapnel holes with soot rings appear first and spread as a part
    takes more hits, and only a part near destruction scorches over whole panels. The nose used to turn uniformly
    black. Hits also dent the skin.
  - The structure inside a break looks like a real wing: truss ribs with cap strips, spar webs and straight
    stringers, instead of spiky artifacts.
  - Night flying: the cockpit dims at dusk and night, and with night vision on its glow goes out and the displays
    turn down, so the cockpit no longer flares white under night vision.
- **Brakes:** anti-skid. Full braking stops the B-52 at about a third of a g, as the real aircraft does; it used to
  stop at 1 g and tip onto its nose.
- **Fixed:** the airframe could snap on the runway: the structure now counts the landing gear holding it up.
- **Fixed:** the left wing's spoilers kept working after both its engine pods were shot away.
- Flight deck paint is worn like the game's own cockpits (chipped, scratched grey; a crinkle-black glareshield),
  the window frames inside are riveted metal, and the cabin has a fire extinguisher, oxygen bottles, map lights,
  ceiling frames and wiring looms.
- **Fixed:** AI B-52s bailed out right after takeoff: the game's AI lands only on a runway longer than the
  aircraft's take-off distance, and at 2900 m no runway on Heartland qualified. Now 2200 m.
- **Fixed:** AI B-52s could circle near the runway entrance for minutes: the trucks now steer 45 degrees for taxiing.
- Needs Blueprinter 2.1.1 (built with Blueprinter Editor 0.1.1, which pairs with 2.1.1's material texture fix).
- **Cockpit view:** your eye point is 0.24 m further back, where the pilot really sits, so the windscreen frames no
  longer fill the screen; the instrument panel is a short glance down.
- The pilots now sit in their seats: the seats, control wheels and rudder pedals are placed for the pilot model's
  body (it used to sit a metre ahead of an empty seat). The seat backs recline 13 degrees with the pilot's back;
  every seat on the flight deck used to lean forward.
- A leftover 1.5 m display from the donor aircraft no longer hangs through the glareshield and the nose.
- A copy of the outside windscreen wiper no longer sits in the middle of the pilot's windscreen.
- The flight deck no longer goes nearly black against the bright outside: its paint has a soft fill light (no
  frame-rate cost), and the seat rails behind your head are dark instead of shiny.
- A row of caution and warning lights under the glareshield, and a detailed throttle pedestal (lever slots,
  switch panels).
- No more sky through the aircraft: the bomb-bay opening is cut cleanly (it had saw-tooth edges at both ends with
  gaps into the hollow fuselage), and the fuselage has a dark inner lining around the gear wells and the bay.
- Engine internals: looking into an engine from behind you see the fan duct with its guide vanes, and the core's
  turbine blades and exhaust cone, instead of straight through the pod to the sky.
- From outside (the external camera, or another player's B-52) the windows show the real flight deck and crew;
  the cabin used to be switched off outside cockpit view, leaving the windows black.
- The aircraft menu and the map show a B-52 silhouette, rendered from the model, instead of the Alkyon's.
- The paint is no longer mirror-glossy: every exterior material had full smoothness. The skin now has a satin
  finish like the stock aircraft; bare gear metal is a little shinier, rubber and interiors matte.
- Spoilers are part of the wing: each panel is cut from the wing's own skin, flush and in its paint and livery,
  and lifts out of a shallow dark well (they were grey slabs sitting on top of the wing).
- Wing-pylon loads hang on real racks: an ejector rack over each row of weapons with sway braces on every store,
  hung from the pylon's adapter beam. The racks stay on the pylon after the weapons are released.
- The B-52's mass correction no longer searches every other aircraft each time its fuel or stores change, and
  the per-part lift-point log (`Debug.LogAeroCentres`) is off by default: about 30 fewer log lines per spawn.
  An existing config file keeps its old setting; set it to `false` there too.
- Without Heavy Hangar, a B-52 spawned outside a stock hangar now checks the spot is clear (no aircraft, vehicle or
  building there) and uses the next clear spot instead of landing on top of something.
- Bay loads that didn't fit the bay: AGM-129 is 8 on one rotary ring (12 ran past the bay's ends) and GBU-54 is 8
  (15 hung below the closed doors). Measured in game against the bay for every bay rack.
- **Damage model, part 1:**
  - **Parts come off:** wings, engine pods, tail surfaces, flaps and the nose break off when they're shot away or
    overloaded. Before, nothing could ever come off a B-52, however badly it was hit. Each part's mounting has the
    strength of the real B-52's design (3.0 g ultimate, 4.5 g for the engine mounts), and damage weakens it, so a
    damaged wing can fold in a hard pull. Anything outboard goes with it, and the aircraft's weight and balance
    change with what it lost.
  - **Toughness:** each part's toughness matches the real structure: the centre wing box and wing roots are toughest,
    the control surfaces weakest.
  - **Engines:** each engine is tied to its pod. A badly damaged pod damages both its engines, a pod that comes off
    takes them with it, and a failed engine shows the game's engine fire.
  - **AI crews:** an AI crew bails out of a B-52 that has lost a wing or its nose.
  - **Flying normally:** the fly-by-wire's 2.5 g limit keeps normal flying inside the design strength. Flying
    fast with the flaps down will tear them off.
- **Windscreen:** from outside, black patches round the cockpit windows are gone. The glass used the donor bomber's
  pane mask, laid out for a different canopy, and parts of each pane picked up its dark frame areas. Every pane is
  now the game's clear glass. The view from the cockpit is unchanged.
- **Damage model, part 2:**
  - **Crew of five:** pilot and co-pilot, the EWO on the aft-facing seat behind them, and the radar navigator and
    navigator on the lower deck. Every station has an ejection seat; the two on the lower deck fire downward through
    the belly, as on the real B-52. Before, the B-52 had the donor bomber's crew of two, who climbed out of the side.
  - **Fuel tanks:** leaks, fires and fireballs start at each tank, not at the wing section's root. A burning tank
    heats what's around it, so a wing fire can bring the wing down. A part that comes off tears the line into the
    tank inboard of it.
  - **Fuel order:** the fuselage tanks empty first and the wing keeps its fuel to the end, easing the load on the
    wing root. The B-52 pumps fuel between its own tanks and leaves the fuel draw to the game, so it works with
    mods that change it: with the Aryx weapons pack, its drop tanks still empty first.
  - **Hydraulics:** each wing's spoilers (the B-52's only roll control) need an engine running on that wing. Lose
    all four engines on one side and that side's spoilers go flush. The elevators and rudder are moved by control tabs
    and keep working.
  - **Gear:** a gear leg whose mounting is shot through collapses under load.
  - **Torn stubs:** a part that comes off leaves a ragged, scorched stub where it broke, which adds drag.
  - **Impacts:** every part can be damaged by hitting the ground or another object, sized for a 200 t aircraft. A
    gentle belly landing does no damage; a hard one wrecks the keel. Before, only the centre section could be damaged
    by an impact, and it took the damage for every part's contact.
- **Replacing the donor bomber** (docs/DONOR_REPLACEMENT.md; `docs/DONOR_AUDIT.md`, written by every build, lists
  what is left). Every setting the builder takes from the donor is now set for the B-52, with its reason, in
  `unity/B52Tools/Editor/B52Spec.*.cs`:
  - **Collision:** the centre section collides and is hit as its own shape. It used the donor's fuselage collision
    mesh.
  - **Flight-model parts:** buoyancy from each part's volume (the B-52 floats for a while after ditching), the drag of
    a torn stub, blast armour, the cockpit and centre box as critical parts.
  - **Engines:** the F130's N1 range (idle about 6% of full thrust), spool-up in about 6 s, start-up, flame-out above
    about 16 km, damage threshold. A much cooler IR plume than the donor's low-bypass jets, a smaller heat haze, and
    engine fire messages numbered 1 to 8.
  - **Gear:** gear-down drag about doubles the clean drag, as on a real heavy jet. Before, every gear leg set the part
    it hangs on to the donor's 5 m2, over four times the drag of the whole clean aircraft. Also tyre friction and
    contact area (a loaded B-52 bogs down on grass).
  - **Systems:** the AN/APQ-188 radar, 192 ALE-20 flares, ECM, four 120 kVA generators, autopilot and fly-by-wire
    set for a 200 t aircraft, and the aircraft's identity for the AI (a strategic bomber; no threat to aircraft).
  - **Seats:** the radar navigator and navigator have their own seats, consoles and floor on the lower deck, with the
    hatches their seats leave through. The game's generic seat no longer shows at any station.
  - **Sound and lights:** every audio source set for a 56 m, eight-engine aircraft. The B-52's own nav-light domes and
    lights.
  - **Effects:** heat haze, tyre dust and nav-light glows built for the B-52.
- **Ground effect:** near the runway the wings make up to 25% more lift, fading out by about 30 m (the game has no
  ground effect). It lifts off almost level like the real B-52: 158 kt after 1,450 m at 180 t when rotated at 150 kt
  (was 170 kt after 1,780 m), 163 kt after 2,000 m at 221 t, and by itself, level, at about 170 kt. Setting:
  Physics.GroundEffectLift.
- The tyres throw up dust on dirt instead of pink squares, and the wingtip vortex trails have their smoke material
  (they would have drawn pink too).
- The README's takeoff numbers are now measured in game (the ones above). It used to say a full-load B-52 lifts off
  by itself at 170 kt, which didn't match the game.
- **Performance:** distant B-52s draw a simplified model, as stock aircraft do: once one is under 5% of the screen
  height (about 1.2 km away) it's 9,500 triangles in 38 pieces instead of 62,000 in 130, without the gear and
  cockpit. It keeps its livery and damage.
  The cockpit's plain colours share one material, so it draws in about 60 calls instead of 94 (every nearby
  B-52's cockpit is drawn, seen through its windows).
- **Three more liveries:**
  - **USAF Gunship Gray, clean:** the same aircraft without the weathering streaks, which read as scratches up
    close. Markings, insignia and panel lines stay.
  - **Southeast Asia camouflage, black belly:** tan and two greens on top, black underneath and up the sides and
    fin, like the B-52D over Vietnam.
  - **Southeast Asia camouflage, white belly:** the same camouflage over an anti-flash white belly, the 1970s
    nuclear alert scheme.
  Together they add 2.4 MB to the download.
- README: a tip for low frame rates on laptops with two graphics chips.
- The bomb bay is bigger inside, like the real one: wider at the top than the door opening and taller, so cruise
  missiles on the rotary launcher no longer show through the bay roof and walls when you look into the open bay.
- Rotary-launcher missiles sit 8 cm higher, so none of them pokes below the closed bay doors.

## 0.9.0 (pre-release: the 1.0 release candidate)

Everything planned for 1.0 that one player can test. 1.0.0 follows once it has had a multiplayer test.

**Fixes**
- The crosswind crab angle no longer carries over to your next aircraft after a respawn.
- Other players now see your drag chute at full size (it used to stay tiny or not appear).
- The drag chute now drops away and settles on the runway when released, instead of vanishing, and it no longer
  glows at night.
- The crab keys `[` `]` `\` no longer fire while you type in chat or have the game paused.
- Without Heavy Hangar installed, the B-52 now spawns on the apron outside the hangar instead of inside it with its
  wings through the walls.
- Your own B-52 now uses the game's precise collision detection, like other player aircraft.
- From the cockpit you can now see your own wings and engines (the whole exterior used to vanish in cockpit view).
- A stray bar no longer hangs across the cockpit from the roof to the throttles.
- Looking left, right or up from the pilot's seat no longer shows open sky: the game's camera was cutting away
  every part of the flight deck closer than about a metre (walls, roof, side-window frames). The interior is now on
  the game's cockpit layer, like the stock aircraft.
- No more hole under the windscreen: you could see the runway through the nose, because the game hides the nose's
  outer skin in cockpit view. The inside of the nose is now lined all the way to the tip.
- Weapon data:
  - The BDU-48 practice bomb rack now weighs what 17 practice bombs weigh (it was 15 t).
  - The CALCM loadout is 16 missiles (8 in the bay, 4 per pylon), so it stays inside the payload limit; the old
    20-missile rack is gone from the list.
  - The aircraft description matches the real loadouts.

**Looks**
- The dent and crease on the right side of the hull just behind the cockpit glass are gone (they came with the
  original model), and the flat sides of the fuselage no longer shade as if creased in a pyramid pattern.
- Contact shadows darken the wing roots, pylons and the belly under the wings, and damaged parts turn scorched
  and sooty instead of just darker.
- No more visible seams around the gear doors, and the inside of an open door is painted, not black.
- Proper wing pylons with the adapter beam, instead of plain boxes.
- Tyres, wheels and gear legs in realistic greys instead of pure black and white.
- The spoilers take liveries and damage like the rest of the wing.
- The flight deck is reworked to look like the real one: dark grey panels and window frames, quilted insulation
  on the walls, a padded glareshield you can see the instruments under, a round attitude indicator and dials,
  bevelled edges on the panels and consoles, and round throttle knobs. A stray window post no longer hangs across
  the left side window.
- Landing and taxi lights come on with the gear, and there are red anti-collision beacons and white strobes.
- The engine fans turn as the engines spool up.

**Cockpit**
- The altimeter is now a three-pointer altimeter (hundreds, thousands and ten-thousands of feet), so you can read
  your altitude.
- A help card with the B-52's keys and speeds shows the first time you fly it each session; Shift+F1 brings it back.

**Performance**
- The landing gear wheels use about a third of the triangles they did (they were over half the aircraft).
- The beacon and strobe lights switch fully off between flashes, and the landing lights light only the runway ahead
  (250 m instead of 550 m), so they cost less per frame, especially with several B-52s around.

**Changes**
- Flight logging (telemetry) is now off by default. When you turn it on, it only logs your own aircraft and keeps
  the newest 20 logs. The logs also read correctly in any Windows language.
- GEAR CRAB and the new CHUTE messages appear low in the centre of the screen, sized to your resolution.
- The first time the chute's conditions are almost met, a hint tells you to hold the wheel brakes.
- LRASM and SRAM racks are labelled as rotary-launcher loads.
- Two new mixed presets: JASSM-ER x8 + GBU-38 x48, and LRASM x8 + MALD x16 (anti-ship).
- The onboard jammer is a little stronger and lasts a little longer than the stock bombers' (intensity 10, was 8).
- The eight engines now sound like four pods instead of sixteen identical loops: smoother and no louder than a
  stock bomber.

## 0.4.1
- Real landing-gear wells: each main truck folds into its own well, with doors cut from the skin; the wingtip gear
  stows inside the outer wing.
- Cockpit layout closer to the real B-52, and the dents behind the cockpit are gone.

## 0.4.0
- Landing gear rebuilt like the real B-52 (swivel-and-fold retraction, crosswind crab), nav lights and cockpit
  poke-through fixed.

## 0.3.x
- 0.3.8: transonic drag rise, so the B-52 can't go supersonic in a dive.
- 0.3.7: ADM-160 MALD and MALD-J decoys, and a SEAD preset.
- 0.3.6: liveries and damage shading, with a livery template.
- 0.3.5: main gear doors.
- 0.3.4: speedbrakes and the landing drag chute.
- 0.3.0 to 0.3.3: real aerodynamic lever arms, fly-by-wire tuned for a bomber, takeoff calibrated to B-52H data.
