# B-52J Stratofortress for Nuclear Option: Design Spec

Target: a flyable B-52 for Nuclear Option, loaded through Nikkorap's Blueprinter (`.nobp`). It flies on real
B-52H airframe numbers and uses the B-52J modernisation package (CERP engines, RMP AESA radar, 1760 internal
bay) as the "modern updates".

## 1. Airframe (real figures → game fields)

| Item | Real value | Game field |
|---|---|---|
| Length | 48.5 m (159 ft 4 in) | model scale |
| Wingspan | 56.4 m (185 ft) | model scale |
| Height | 12.4 m (40 ft 8 in) | model scale |
| Wing area | 371.6 m² (4,000 ft²) | sum of `AeroPart.wingArea` over the wing parts |
| Wing sweep | 35° at quarter chord | part geometry |
| Empty weight | ~83,250 kg (185,000 lb) | sum of `UnitPart.mass` |
| Max takeoff weight | 221,350 kg (488,000 lb) | checked against fuel + stores |
| Internal fuel | 47,975 US gal ≈ 181,600 L ≈ 141,100 kg JP-8 | `FuelTank.fuelCapacity` across the tanks |
| Max speed | 650 mph / 1,047 km/h (M 0.86 at altitude) | `AircraftParameters.maxSpeed` (m/s): 290 |
| Cruise | 509 kt / M 0.77 | AI `cruiseThrottle` |
| Service ceiling | 50,000 ft / 15,240 m | `Turbofan.altitudeThrust` curve |
| Combat radius | ~7,210 km unrefuelled range (8,800 mi ferry) | fuel burn tuning |
| G limit | +2.0 g (structural design ~+2.5 g) | `aircraftGLimit` = 2.5 |
| Takeoff speed | ~150 kt (77 m/s) at heavy weight | `takeoffSpeed` |
| Approach / landing | ~140 kt / ~130 kt | `approachSpeed` / `landingSpeed` |

### Flight-control quirks to reproduce
- **Spoilerons for roll.** The B-52H has no ailerons. Seven spoiler panels per wing do the rolling, so roll
  authority goes on spoiler `ControlSurface`s with `rollRange` and split drag, not on aileron surfaces.
- **All-moving stabiliser trim + small elevators**: limited `pitchRange` and a slow `servoSpeed`, so pitch
  feels heavy.
- **Small rudder**: low `yawRange`. Crosswind landings are made with the crabbing gear (cosmetic).
- **Quadricycle gear**: four main trucks in tandem pairs, plus wingtip outrigger wheels. The aircraft rotates
  very little; it lifts off nearly level thanks to the wing's built-in incidence (~6°).
- **Drag chute** on landing (stretch goal, as a `brakeRange` surface).

## 2. Engines: CERP (B-52J)

| | TF33-P-103 (B-52H) | Rolls-Royce F130 (B-52J) |
|---|---|---|
| Count | 8, in 4 paired pods | 8, in 4 paired pods |
| Thrust each | 17,000 lbf (75.6 kN) | ~17,000 lbf (75.6 kN) |
| Fuel burn | baseline | ~30% lower |

Game mapping: 8 × `Turbofan` with `staticThrust` = 75,600 N each (604.8 kN in total). Fuel consumption is
70% of the TF33-tuned value. A slow `spoolRate` keeps throttle response realistic for a large high-bypass
engine.

## 3. Avionics (modern updates)
- **AN/APQ-188 AESA radar** (RMP, derived from APG-79): radar fitted to the nose `TargetDetector`. Long-range
  ground mapping and air search tuned like a strike radar, not a fighter's.
- **Sniper ATP targeting pod**: EOTS / laser designator for guided bombs.
- **CONECT glass cockpit**: MFDs in the custom interior, with the tac screen on the centre display.
- **ECM**: ALQ-172 → jammer (`ecmIntensity`), flares and chaff on `CountermeasureManager`.

## 4. Weapons (real counts)

Real B-52H stores stations:
- **Internal bay**: either 27 × 500-lb class on clip-in racks, or one rotary launcher with 8 large stores
  (CSRL / CRL after the 1760 Internal Weapons Bay Upgrade).
- **Two wing pylons**: AGM-28-type pylon with 12 light stores each (24 in total), or HSAB with 9 heavy
  stores each (18 in total, up to 2,000 lb each), or 6 cruise missiles each on the ALCM/JASSM pylons.

| Loadout | Internal | External | Total | Real-world reference |
|---|---|---|---|---|
| Mk 82 iron bombs (500 lb) | 27 | 24 (12 + 12) | **51** | AGM-28 pylon + clip-in racks |
| M117 (750 lb) | 27 | 18 (9 + 9) | **45** | HSAB |
| GBU-38 JDAM (500 lb) | 8 (CSRL-C) | 24 | **32** | 1760 IWBU |
| GBU-31 JDAM (2,000 lb) | 8 (CRL) | 12 (6 + 6) | **20** | HSAB |
| JASSM-ER | 8 (CRL) | 12 (6 + 6) | **20** | AGM-158 pylons |
| Cruise missile ALCM/LRSO | 8 (CSRL) | 12 (6 + 6) | **20** | |
| ARRW / hypersonic (AGM-183) | — | 4 (2 + 2) | **4** | HWP pylons (2026 limit) |
| Quickstrike mines (Mk 62) | 8 | 10 | 18 | |
| MALD-J decoys | 8 | — | 8 | mixed with any external load |

### Mapping onto game munitions (vanilla first, so the mod works with no other weapon packs)

| Real store | Closest vanilla munition | Notes |
|---|---|---|
| Mk 82 / M117 | `bomb_250` (unguided) | game "250" is kg, about Mk 82 to M117 class |
| GBU-38 | `bomb_250_glide` | guided 250 |
| GBU-31 | `bomb_500_glide` | guided 500 kg / 1,000-lb class; 2,000-lb custom later |
| Penetrator (GBU-31 v3 / BLU-109) | `bomb_penetrator1` | |
| JASSM-ER / ALCM | `CruiseMissile1` | |
| LRSO (nuclear ALCM) | `CruiseMissile20kt` | |
| ARRW | `BallisticMissile1` (air-launched) | stand-in until a custom hypersonic exists |
| B61 / B83 gravity | `nuclearBomb1` / `nuclearBomb1_strategic` | |

The mod defines its own `WeaponMount` racks: `B52_Mk82_internalx27`, `B52_Mk82_pylonx12`,
`B52_HSAB_bomb250x9`, `B52_CSRL_*x8`, `B52_HSAB_JASSMx6`, `B52_HWP_ARRWx2`, and others. Each rack lays the
weapons out in the real arrangement, and its prefab places them on the matching vanilla munitions.

Stores mass is checked against MTOW: 70,000 lb (31,750 kg) of mixed ordnance max.

### Hardpoint sets (WeaponManager)
0. Internal Bay: clip-in x27 / CSRL x8 variants / nuclear
1. Left Wing Pylon (SymmetryWithPrev → 2)
2. Right Wing Pylon
3. Countermeasures / ECM (built in)
4. Targeting pod (Sniper, on the right pylon root)

## 5. Interior: custom crew compartment
Upper deck, scratch-built in Blender, modern B-52J layout:
- Pilot and co-pilot ACES II seats, yokes, an 8-lever throttle quadrant (2 engines per pod handle → 4 detents
  rendered as 8 levers), rudder pedals.
- CONECT glass: 3 front MFDs, upfront control panel, and standby instruments (ADI, airspeed, altimeter) as
  live game instruments.
- Overhead panel: fuel and engine start switches. Side consoles: radios, trim, gear handle, bomb-bay switch.
- Windows that match the exterior canopy frame, with interior frames and wiper arms.
- Instruments use the game's own components: `Cockpit.joysticks`/`throttles` animate the yoke and throttles,
  and the tac screen goes on the centre MFD.
- Lower deck (radar navigator / EWO stations) appears only as a dark ladder well behind the seats. Stretch
  goal.

## 6. Build pipeline
1. Base model: Sketchfab "Boeing B-52 Stratofortress" by **hruschak30** (CC-BY 4.0, credited).
2. Blender: rescale to real size, split the parts (fuselage, wings L/R, tail, 4 engine pods, 7 spoilers per
   side, flaps, elevators, rudder, gear trucks and doors, bay doors), set pivots, export FBX.
3. Unity 2022.3.62f2 + Blueprinter Editor: build the prefab with Aircraft / AeroPart / Turbofan /
   ControlSurface / LandingGear / FuelTank / WeaponManager, plus AircraftParameters and AircraftDefinition.
4. Ops: `OpAddAircraftToHangars` (airbases), `OpAddWeaponToHardpoint` (racks).
5. Mod Builder → `B-52J Stratofortress_x.y.z.nobp`. Copy it to `BepInEx/plugins` and test.
