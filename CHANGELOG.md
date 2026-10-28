# Changelog

Player-facing changes in each release. The development diary with every detail is [MODLOG.md](MODLOG.md).

## Unreleased (next: 1.0.0)
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
- Without Heavy Hangar, a B-52 spawned outside a stock hangar now checks the spot is clear (no aircraft, vehicle or
  building there) and uses the next clear spot instead of landing on top of something.
- Bay loads that didn't fit the bay: AGM-129 is 8 on one rotary ring (12 ran past the bay's ends) and GBU-54 is 8
  (15 hung below the closed doors). Measured in game against the bay for every bay rack.
- The README's takeoff numbers are now measured in game: rotate at 150 kt, lift-off at 170-175 kt, about 1,800 m at
  the default load and 2,400 m at maximum takeoff weight. (It used to say a full-load B-52 lifts off by itself at
  170 kt; it doesn't.)
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
