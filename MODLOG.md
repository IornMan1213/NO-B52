# MODLOG: B-52 for Nuclear Option

## Environment (2026-10-03)
- Game: Nuclear Option, Steam 2168680, `C:\Program Files (x86)\Steam\steamapps\common\Nuclear Option`
  - Unity 2022.3.62f2 (Mono), no anti-cheat. BepInEx 5.4.23.5 installed. Assembly-CSharp.dll is dated
    2026-08-14.
  - Blueprinter (Nikkorap) is installed in `BepInEx/plugins` and loads `.nobp` files (UnityFS asset bundles).
  - Saves: `%USERPROFILE%\AppData\LocalLow\Shockfront\NuclearOption`
- Unity Editor 2022.3.62f2 is installed (matches the game). Blender 5.2 is installed.
- Fresh decompile: `~/no-decomp-current` (ilspycmd, outside the repo; never commit it).
- Vanilla weapon mount names: `BepInEx/LoadoutDiagnostics*.txt`.

## Route
Blueprinter Editor (Unity project) → `.nobp`. This is the community route, the one used by the Aryx F-16M and
the MiG-29 Fulcrum. No C# patches are needed for the aircraft. A small BepInEx plugin is optional later, for
custom instrument behaviour.

Blueprinter Editor setup (from GUIDE.md, release 0.1.0, 2026-08-31):
1. AssetRipper export of NuclearOption.exe (Script Content Level 1, Decompilation, Export Unity Project)
2. Open the Blueprinter project in Unity 2022.3.62f2 → Blueprinter > Project Setup (game version,
   assemblies, assets, refresh ops, build `_donotship`)
3. Mod folder `Assets/Blueprinter/Mods/B52`. Use the Ops `OpAddAircraftToHangars` and
   `OpAddWeaponToHardpoint`.
4. Mod Builder → `<DisplayName>_<ver>.nobp`

## Key game classes (flight model)
- `AeroPart`: wingArea, dragArea, airfoil index, liftNormal, centerOfLift (lift is computed per part)
- `UnitPart`: mass, hitPoints
- `Turbofan`: staticThrust (N), altitudeThrust/speedThrust curves, spoolRate, fuelConsumptionMin/Max
- `ControlSurface`: pitchRange/rollRange/yawRange/brakeRange, splitDrag (use for spoilerons), flap,
  servoSpeed
- `LandingGear`: springRate, dampingRate, suspensionTravel, steering, braked
- `FuelTank`: fuelCapacity, connectedTanks
- `AircraftParameters` (ScriptableObject): maxSpeed, takeoffSpeed, aircraftGLimit, airfoils, loadouts
- `WeaponManager.hardpointSets` → `HardpointSet` {name, weaponOptions (WeaponMount), hardpoints}
- `WeaponMount` (ScriptableObject): prefab, jsonKey, ammo, mass, drag, RCS, missileBay
- `Hardpoint`: bayDoors, doorOpenDuration
- `Cockpit`: joysticks, throttles, tacScreen

## Vanilla reference prefabs (from the AssetRipper export, dumped with tools/prefab_dump.py)
Units are SI: AeroPart.mass in kg, wingArea in m², Turbofan.staticThrust in N, FuelTank.fuelCapacity in kg.

| Prefab | ΣAeroPart mass | Σ wingArea | Σ thrust | Σ fuel |
|---|---|---|---|---|
| Darkreach (flying-wing bomber) | 54,311 kg | 383 m² | 2 × 180 kN | 24,998 kg |
| FastBomber1 (swing-wing, conventional tail) | 34,100 kg | 150 m² | 4 × 45 kN | 18,200 kg |
| Multirole1 | 16,040 kg | 123 m² | n/a | 8,200 kg |

Template: FastBomber1's hierarchy (root fuselage → wingroot_L/R → wing1 → wing2 → aileron/wingtip, engines as
child `Turbofan` objects under an engines AeroPart, plus elevator/rudder ControlSurfaces). The B-52 follows
the same tree with 4 pods × 2 Turbofan.
- Signs: aileron_L rollRange −30 / aileron_R +30, elevators pitchRange +20, rudder yawRange −15.
- `HighLiftDevice` on the flaps. Cockpit tree: cockpit (AeroPart + AutopilotPlane + ControlsFilter +
  WeaponManager) → Pilot/WSO (CapsuleCollider + Pilot), cockpit_int (Cockpit).

## Setup progress
- AssetRipper 2.0.0 export → `~/no-ripped/NuclearOption/ExportedProject` (3.0 GB). Game version
  0.34.1 (from bundleVersion).
- Driven headless over its HTTP API: `--headless --port 47110`, POST /Settings/Update
  (ScriptContentLevel=Level1, ScriptExportMode=Decompiled), /LoadFile, /Export/UnityProject.
- Blueprinter project at `~/BlueprinterProject/Blueprinter-Editor`. The repo's `unity/B52Tools`
  is junctioned into Assets/B52Tools. It's an editor asmdef, because a loose *.cs in Assets breaks Blueprinter
  (Assembly-CSharp is read-only).
- Setup steps run headless: `Unity.exe -batchmode -nographics -quit -projectPath ... -executeMethod
  B52Tools.BatchSetup.StepN`, one Unity run per step (assembly import forces a domain reload).

## Decisions
- Base model: **bohmerang** "Boeing B-52 Stratofortress" (CC-BY 4.0, 16.6k faces, 27 materials, 262 likes).
  - It was switched from hruschak30 after a provenance check. hruschak30's uploads include rips from Halo 2A,
    Halo CE Anniversary, Halo 4 and FNAF, so its CC-BY label is not trustworthy for a public release. The B-52
    had no description and odd tags. Don't use it.
  - bohmerang is a prolific original aircraft modeller (4.5k followers, 32 free aircraft such as the F-16, B-2
    and F-15E), and the panel-lined PBR textures look good up close.
  - Rejected: S1Priv (stylised Blockbench pixel-art), ATD "B52" (542k faces, flat white, untextured), VuckyZ123
    (Ace Combat 7 rip, NC-SA), manilov.ap (2017, OK fallback), thomas333 (an edit of bohmerang's model).
- Repo: `Documents\GitHub\NO-B52`, private on GitHub via the gh CLI.
- Variant: B-52J (CERP F130 engines, APQ-188 AESA, 1760 IWBU) on the B-52H airframe. Numbers are in SPEC.md.

## Build pipeline (all scripted, run `bash tools/build_all.sh`, then `bash tools/install.sh` with the game closed)
1. `tools/blender_prep.py`: splits bohmerang's .blend into flight parts (bisect + loose-part binning), sets hinge
   pivots, builds the gear and the bomb bay. → `blender/out/B52_parts.blend`
2. `tools/blender_cockpit.py`: scratch-built flight deck under `cockpit_int`.
3. `tools/blender_export.py`: FBX → `unity/Mods/B52/B52.fbx` (`bake_space_transform=False`; Unity's
   `bakeAxisConversion` does the axis flip).
4. `B52Tools.B52Builder.Build` (Unity batchmode): grafts FastBomber1's components onto our hierarchy → `B52.prefab`,
   definition, parameters, livery, hangar op, weapon racks (`B52Weapons.cs`).
5. `B52Tools.B52Builder.BuildMod` → `build/B-52J Stratofortress_<ver>.nobp`
6. `plugin/B52Systems` (BepInEx/Harmony) → `build/plugin/B52Systems.dll`
Installed to `BepInEx/plugins/B-52J_Stratofortress/` (the .nobp and the DLL).

## Hard-won facts (numbered gotchas)
1. **FBX axes**: Blender `bake_space_transform=True` breaks nested hierarchies (children arrive rotated -90° and
   offset). Export plainly and let Unity bake the axis conversion. Every node still carries +90° X, so
   `NormalizeFrames()` bakes `Rx(90)` into copies of the meshes and resets the frames.
2. **Control surfaces** rotate the visible mesh about **local X** (`ControlSurfaceJob_Math`: `restingRotation *
   AngleAxis(angle, right)`). The rudder needs an explicit frame with X = up.
3. **Lift**: `alpha = atan2(v_local.y, v_local.z)` in the part's `liftNormal` frame, and lift = `-cross(v, right) *
   CL`. Pitching the lift frame nose-up (Euler(-θ,0,0)) gives incidence. The airfoil curves are in radians.
   Parasitic drag = 0.25·ρ·V²·dragArea, plus CD(α)·q·wingArea.
4. **Turbofan thrust** = staticThrust × altitudeThrust(m) × speedThrust(m/s). There is no density term, so
   high-bypass engines need their own lapse curves (the FastBomber AB curves keep full thrust to 10 km).
5. **Complex physics** (player aircraft near the camera): every AeroPart becomes its own rigidbody plus a FixedJoint
   (breakForce ×10). This led to joint breaks, collider fights between overlapping parts, and visible sag. The
   fix is `B52Systems`, which keeps the B-52 in simple physics (one rigidbody), config `RigidAirframe`.
6. **LandingGear** casts a ray from the bumpstop down `suspensionTravel`. The gear's own BoxCollider must start
   **disabled** (the game enables it only in `BreakWheel`), otherwise the ray hits it and the gear snaps off.
   `gearCollider` must be set.
7. **Grafting a donor prefab**: every reference into the donor must be remapped or the arrays cleaned. NREs came
   from Aircraft.dopplerSounds, NavLights entries, RadarLocator.essentialParts, TargetCam.attachedPart,
   Turbofan.criticalParts, missing JetNozzle (IR source for StatusGauges), the swing-wing HUDExtras (use
   SFB_HUDExtras), and Downwash. `LoadoutSelector.LoadDefaults` reads `loadouts[1]`.
8. **Liveries** are Addressables. The donor's references can't be bundled, so make our own LiveryData asset and
   point `assetReference.m_AssetGUID` at it. Livery textures go to `UnitPart.damageMaterial.renderers` and
   `WeaponManager.skinnables`. These are cleared for now, because the model uses many textures, not one atlas.
9. **Weapon racks**: WeaponMount asset + prefab of N `MountedMissile` children. They are cloned from vanilla donor
   racks, and `info` stays vanilla. Hardpoint `pylonOptions` with `mount=null` show the pylon for any rack.
10. Blueprinter in batchmode: `ModBuilder.Build(mod, displayName, version, outDir)`. Missing addressables abort it.

## Flight model check (`python tools/flightmodel_check.py`, same equations as the game)
| Case | Lift-off | Ground roll |
|---|---|---|
| 127 t (30% fuel, clean) | 150 kt | ~730 m |
| 181 t (60% fuel, 51 × Mk 82) | 179 kt | ~1,590 m |
| 221 t (MTOW) | 196 kt | ~2,450 m |

Max level speed: 631 mph at 10.7 km (real: 650 mph). Cruise attitude is slightly nose-down, L/D ~13-15.

Pitch balance (Unity z, + = forward). Lift acts at each part's lift-normal position: wingroot 8.94, wing1 3.62, wing2
-0.58, wingtip -2.51, flap1 7.42, flap2 4.03, so the area-weighted wing lift centre is z 4.65. The CG (`CoM`
transform, applied by UnitPart.ModifyMass in simple physics) is at z 5.8: 1.15 m ahead, roughly 15% MAC static
margin. Cruise trim needs ~77 kN of tail download (CL_tail ~0.07), and at lift-off (90 m/s) CL_tail ~0.22. Both
are well within the elevators' ±20°.

## Version history
- 0.1.0: first build. Spawned, then fell apart (joint strengths).
- 0.1.1-0.1.3: load-sized joints, self-collision ignore, NRE cleanup, nozzles/IR.
- 0.1.4: gear collider fix. Gear held.
- 0.2.0: full ordnance list (32 bay racks, 30 HSAB racks, 9 presets).
- 0.2.1: rigid airframe, 6° wing incidence, CG at ~25% MAC.
- 0.2.2: B-52 airfoil, F130 thrust curves, flaps as inner-wing camber.
- 0.2.5: upper-deck EWO + gunner/instructor aft-facing stations, lower-deck ladder well; B52Systems telemetry CSV.
- 0.2.4: live standby instruments (ASI, ALT, VSI, ADI) driven by B52Systems.Instruments; throttles +45° (forward = full), yokes 10°.
- 0.2.3: one-way spoiler panels (SpoilerDriver), gear folds up into belly, engines at measured nacelle centres, analytic (symmetric) hinge axes.

## Next
- [ ] User test: hull clean? Lift-off speed? Max speed at altitude?
- [ ] One-way spoilerons (plugin), visible spoiler panels
- [ ] MALD / MALD-J custom decoy
- [ ] Single-texture atlas so liveries work
- [ ] Animate the gear retraction properly (B-52 trucks swivel 90° then fold)

## 0.2.6 (plugin only)
- In-game telemetry of 0.2.5 showed rb.mass 28,450 kg at spawn, then ~14,780 kg. Cause: with RigidAirframe the
  aircraft never goes through Aircraft.SetSimplePhysics, and UnitPart.ModifyMass (fuel burn, stores) sets
  `rb.mass = part.mass` (that one part only). The game only expects simple physics on remote aircraft, where
  fuel never changes mass.
- Fix: MassSync keeps rb.mass = sum of attached UnitPart.mass (structure + fuel + stores) every FixedUpdate and
  pins rb.centerOfMass to the root CoM transform.
- Gotcha 11: a locally simulated aircraft held in simple physics must manage its own rigidbody mass.
- Fuselage step (user screenshot): the root mesh (centre fuselage) sat 1.26 m above the front/rear sections.
  blender_export.py reset the root object to identity without baking its offset (origin z -1.26) into the
  mesh; children were placed by world matrix so only the root moved. Fixed by baking the root's world matrix
  into its mesh. Prefab bounds now: B52 centre y -0.6, fuselage_F -0.6, fuselage_R -0.6.

## 0.2.7
In-game report on 0.2.6: one piece now, but bounces, can't steer, outriggers fragile and don't roll, stuck ~30 kt.
- Bounce: telemetry showed rb.mass flicking 237 t -> 14.8 t. Fuel burn's ModifyMass ran after MassSync in some
  frames, so the solver saw a 15 t body on 237 t springs. Harmony postfix on UnitPart.ModifyMass re-syncs at once.
  Main damping 450k -> 750k N·s/m (~0.85 critical per truck at MTOW).
- Steering: forward trucks copied the FastBomber main gear (steeringSpeed 0, aligningStrength 0). Now 30 °/s, 2, lock 25°.
- ~30 kt: every truck copied gear_L's differentialBrakeFactor, so any rudder input under 30 m/s braked all four
  trucks. Set to 0 (all trucks are on the centreline).
- Outriggers: 400 kN/m spring broke at 0.35 m; the 2.5 m-wide main track means the outriggers carry all roll
  stability, and 0.8° roll is 0.3 m at the tip. Now 1.5 MN/m, 300k damping, 1.2 m maxCompression. A broken
  gear stops its wheels, which was the "doesn't roll".
- Gotcha 12: LandingGear copied from a donor keeps steering/brake settings meant for that donor's position.

## 0.2.8
In-game report on 0.2.7: steers now; rudder swings sideways about its bottom; won't lift off; pitch inverted.
Telemetry: 237 t (100% fuel, over MTOW), 180 kt at 67 s, nose went -2.3 deg when the pilot pulled, then left
the runway (172 -> 128 kt in 0.6 s) and settled at 112 kt.
- Pitch: B52Inspect.Surfaces compares trailing-edge motion per +input with the template. FastBomber elevators
  move the TE down for +pitch; ours moved it up. pitchRange 20 -> -20.
- Rudder: NormalizeFrames forced local X straight up, through the rudder's bottom corner; the swept rudder's top
  sits metres aft, so it swung like a wiper. Hinge now from the forward-most vertex of the bottom and top 12 %
  height bands (forward-most overall bunches at the root), pivot at its midpoint; ReFrame can now move the pivot.
- Lift: the flap panels were at 6 + 20 = 26 deg, past the 17 deg CLmax of the airfoil, and flaps began
  retracting at 194 kt, below the heavy lift-off speed. Camber 10/10 deg (16 deg total), deployedArea = 1.0 x
  flap area (Fowler), flaps down below 105 m/s, up by 130 m/s. Checker: 161 kt at 181 t, 179 kt at 221 t.
- The B-52 can't usefully rotate (tail strike ~3 deg, 15 m elevator arm vs 237 t on the rear trucks), so
  hands-off lift-off speed is the number that matters.

## Heavy Hangar 0.1.0 (written, not yet built; user asked for no builds while playing)
User: the B-52 is too big for the stock hangars; wants a hangar big enough for it and future aircraft.
- Stock hangar_med (largest land hangar): 80 x 45 m, door ~46 m x 8 m (two 23 m leaves). B-52: 56.4 x 48.5 x 12.4 m.
- Game facts used (decompile): Hangar.CanSpawnAircraft only checks availableAircraft; Airbase.TrySpawnAircraft
  takes the first hangar in priority order that accepts. A Building joins an airbase via SetAirbase /
  OnStartClient -> ClientAddBuildingToAirbase, and any building with a Hangar component becomes a spawn point.
  Spawner.SpawnBuilding(prefab, pos, rot, HQ, airbase, name, capturable, null) is the runtime path missions use.
  MissionRunner.OnMissionStart runs on the server only, after mission units spawn. Terrain = collider
  sharedMaterial == GameAssets.i.terrainMaterial (as LandingGear uses). Blueprinter registers BuildingDefinitions.
- Model: tools/blender_hangar.py (114 x 78 m, 84 x 17 m door, 6 telescoping leaves), textures from
  tools/make_hangar_textures.py. Checked with a workbench render: a B-52-sized box fits with the doors open.
- Prefab: HangarBuilder.cs clones hangar_med's root components and door UnitParts, MeshCollider on the body.
- Plugin HeavyHangar.dll: routing (oversize -> heavy), heavy list = union of the airbase's hangars, outside-door
  fallback when a base has no heavy hangar, auto-placement with site checks and retries. Compiles.
- install.sh now picks the B-52 .nobp by name (it took the newest .nobp in build/, which would be the hangar's).

## 0.2.9 (plugin; written, not yet built)
- Telemetry for a full-game test: pilot inputs (pitch/roll/yaw/brake), peak and minimum g per row (FixedUpdate
  differentiation), flap position (HighLiftDevice.position), most-damaged part; events LOADOUT, FIRED <weapon>
  (WeaponManager.OnStationFired), DAMAGE <part> when a part falls below 50 hp and 0 hp, DISABLED, LIFTOFF with
  speed and mass, TOUCHDOWN with sink rate; summary adds g range and release count.
- Checked: bay hardpoint has both bay doors (MountedMissile.Launch -> Hardpoint.SpringOpenBayDoors, 2.5 s).
- tools/build_and_install.sh: build_all + build_hangar + install, refuses to run while the game is open.

## 0.3.0: real lever arms on the rigid airframe
Test of 0.2.9 (223 t): lift-off 185 kt (checker 179), then with the stick held at +1 (= push; Autopilot's hover
PID damps with -angularVelocity.x, and +x rotation is nose-down) the nose rose 6 -> 78 deg and it flipped over.
- Cause (decompile, AeroJob_Math): part forces are applied with rb.AddForce (through the CoM) plus a torque from
  AeroPart.centerOfLift, a serialized offset (ours inherited FastBomber values). With every part on one rigidbody
  (RigidAirframe), wings, tail and elevator had no real leverage. AeroPart.UpdateJobFields also sets
  velocity = rb.velocity, so rotation never changed any part's AoA (no damping). Thrust is fine
  (JetNozzle: AddForceAtPosition).
- Fix: B52Systems.AeroCentres, a postfix on UpdateJobFields: centerOfLift = lift-frame vector from the CoM to the
  part's aerodynamic centre (quarter chord at mid-span for thin surfaces, centroid for bodies),
  velocity = rb.GetPointVelocity(that point).
- tools/aero_centres.py (same rule, Blender) + tools/trim_check.py: neutral point z 1.8 vs CoM 5.8 (stable).
  Flap camber on wingroot (AC z 11.8, 6 m ahead of the CoM) needed > 40 deg of TE-down elevator to trim with
  flaps. Camber moved to wing1/wing2: every phase trims within +-7 deg, full nose-down elevator beats a 5 deg
  over-rotation by 3.5-10 MN.m. Hands-off lift-off 157 kt at 150 t, 173 kt at 180 t, 191 kt at 221 t.
- Roll: with real arms the 4 m2 spoilers gave ~3.6 deg/s steady roll at 150 m/s; 12 m2 gives ~11 deg/s.
- Gotcha 13: simple physics ignores part positions for aero moments; any rigid-airframe aircraft must supply them.

## 0.3.1: left list, takeoff calibration
Test of 0.3.0 (152 t): it flew, but rolled left; pilot held ~-0.48 rudder and +0.5 (push) all climb.
- Cause: AeroCentres put the cockpit's centre at (764, 1168, 1325) m: it averaged every mesh under the part
  (interior, displays). 1 m2 of lift and 0.3 of drag at that arm rolled left, yawed right, pitched up.
  Now only the part's own mesh and its "<name>_visible" copy count, and anything > 40 m falls back to the origin.
- Takeoff reference (Fairchild 1994 docket, AF data): B-52H at 488,000 lb, flaps down, 8 engines, takeoff
  thrust: ~8,000 ft (2,440 m) ground run. AOPA: rotate 5-10 kt before lift-off, lift off at 5-7 deg nose up,
  climb at 180 kt to 1,000 ft. Implies ~170-175 kt lift-off at MTOW.
- tools/trim_check.py takeoff(): game forces + gear drag (LandingGear.extendedDrag *replaces* its part's
  dragArea while down) + tyre rolling resistance, rotation to 6 deg. Matches the 152 t game log segment
  71->111 kt (6.2 s sim vs 6.3 s game), ~10 % optimistic above 140 kt.
- The elevator alone could not lift the nose of a 221 t B-52 until ~176 kt. Added the real B-52's moving
  stabilizer: hstab lift frames trim at 0.4x elevator (+-8 deg), mesh unchanged. Rotation possible from 159 kt.
- F130 low-speed thrust lapse steeper (0.77 at 100 m/s), tyre rollingResistance 0.015.
  Result: 221 t lift-off 169 kt, ground run 2,080 m sim (~2,250-2,350 m in game with spool-up) vs 2,440 m real.
  180 t: 155 kt / 1,330 m. 150 t: 142 kt / 890 m.

## 0.3.2: fly-by-wire retuned for a bomber
Test of 0.3.1: rolls left and right. Aero centres now symmetric (cockpit fixed). Telemetry inputs are the
ControlsFilter *output*: FastBomber1's FlyByWire was on with fighter gains. Roll demand = stick x 6 rad/s
x 0.5..1 (the B-52 rolls ~0.19 rad/s), so any stick saturated the spoilers; rate feedback gain 0.3 was too weak
-> bang-bang roll, over-bank both ways. Also 8 g limit, pFactor 10.
New: gLimitPositive 2.5, maxPitchAngularVel 0.15, takeoffSpeed 80, alphaLimiter 10 (0.15), pFactorFast 4,
dFactorFast 1, maxRollAngularVel 0.35, rollTightness 4, rollTrimRate 0.02 / limit 0.05, yawTightness 2.
MassSync now logs the rigidbody inertia tensor for later roll/yaw tuning.
Note for reading logs: pitch_in/roll_in/yaw_in are post-FBW surface commands, not raw stick.

## 0.3.3: the rudder never worked
User video (0.3.2): wings rock left/right, heading wanders 81-107 deg with an 8-10 s period. Telemetry: hands
off, the FBW held ~+0.4 rudder for 30 s while the aircraft sat in a steady 2 deg bank.
- B52Inspect.LiftFrames: every AeroPart's lift frame must have forward ~ +Z. The rudder's was (0,-0.26,-0.97):
  forward pointed aft, so AeroJob saw ~180 deg AoA, where the lift curve is 0. The rudder has made no force since
  0.1.x (both LookRotation(back, right) and the 0.2.8 swept-hinge frame). No yaw damping, no coordination, so the
  Dutch roll ran free and the FBW yaw loop pushed a dead surface.
- Fixed: rudder frame = LookRotation(forward projected off the hinge, left), like tail_liftNormal; local X is still
  the hinge, so the visual swing and the +yaw = TE right convention are unchanged.
- Telemetry: added sideslip (beta), yaw rate and roll rate columns.
- Inertia logged in game (152 t): Ixx 17.2e6, Iyy 36.1e6, Izz 19.3e6 kg m2.
- Gotcha 14: check every lift frame's forward after building (B52Inspect.LiftFrames); a backwards frame fails silently.

## 0.3.3 verified in game (2026-10-04)
User: "flies straight as an arrow". Telemetry, 152 t, airborne 193-263 kt, climb at full throttle, hands off:
sideslip -0.3..+0.2 deg (sd 0.08), yaw rate sd 0.13 deg/s, roll -1..+6 deg, roll rate sd 0.9 deg/s, FBW yaw
output 0.00 (no rudder needed), pitch_in +0.22 mean (FBW trim at full power), g 0.9-1.2.
Takeoff, nose held level (no rotation): 38 s / 1,482 m to lift-off at 173 kt (ra > 3 ft; wheels unloading from
~165 kt). Model hands-off 157 kt: game ~8-10 kt later (~10 % less lift than the checker) - acceptable; with the
real procedure (rotate ~6 deg at ~145 kt) the checker gives 143 kt / ~900 m.
Status: hull + flight model done per the user's priority. Next candidates: landing test, weapons release test,
MALD/MALD-J, single-atlas livery, gear doors.

## 0.3.4: speedbrakes and drag chute
Landing test (175 t T/O, bombs 44/44 released OK): came in at 400+ kt at idle, touched down at 309 kt with flaps
up (they retract > 253 kt), 1,280 fpm, no damage; could not stop. Idle decel from 400 kt only ~2 kt/s.
"Airbrake does nothing": in Nuclear Option the airbrake (Airbrake component) opens when throttle == 0 and adds
dragAmount * rho * V^2; the brake key is wheel brakes. ControlSurface.brakeRange is stored but never used by the
job. FastBomber1 has no Airbrake, so the B-52 had none.
- Airbrake on the root: dragAmount 8 (CdA ~16 m2, ~230 kN at 300 kt; idle decel ~5 kt/s from 300 kt), no
  transforms; SpoilerDriver raises both wings' spoiler panels to 60 deg x open amount.
- DragChute (plugin): on the ground, idle, wheel brakes > 0.3, < 165 kt -> 13.4 m canopy, Cd 0.55, drag at the
  tail along -velocity, 1.5 s inflation; jettison < 10 m/s or throttle > 0.3; one per landing. Procedural
  canopy + riser. Telemetry events CHUTE deployed/jettisoned.

## 0.3.5: main gear doors
LandingGear.gearDoors: doors open over 1 s before extension, stay open while down, close over 1 s after
retraction, then localEulerAngles = 0 (so a pivot's closed rotation must be identity). One door per main truck:
0.8 x 3.0 m, hinged inboard at x +-0.55 on the belly (skin y found from part vertices: -2.60), opens 100 deg
(past vertical: free edge at x 0.41, clear of the inner wheel, ~0.24 m above the runway). Gray spoiler material.
Outriggers have none (they fold under the wingtip pods). Verified with B52Inspect.RenderDoors
(renders/gear_doors_open.png).

## 0.3.6: single exterior atlas (liveries + damage shading)
- bohmerang's exterior used ~20 textures (10 x 1024, 4 x 512, 6 x 256) + 4 plain colours, UVs all in 0..1.
  tools/blender_atlas.py packs them at full resolution into a 4096 atlas (13 of 16 1024-cells), 8 px edge bleed
  per cell (make_atlas.py), remaps 17,138 faces, one material B52_Skin. Packed .blend images are written out
  with their original bytes (blender/out/atlas_src).
- Livery: LiveryData texture = the atlas. UnitPart.SetLivery / _HitPoints touch material slot 0 of each
  damageMaterial renderer, and a part only auto-lists its own renderer (control surfaces keep theirs on
  "<name>_visible"). SetLiveryTargets lists every skin renderer per part (30 on 26 parts), swapping the skin to
  slot 0 where needed (none needed). Atlas imported at 4096.
- docs/livery_template.png + docs/LIVERIES.md for painting new liveries. Verified with B52Inspect.RenderExterior.

## 0.3.7: ADM-160 MALD / MALD-J
- Game facts: radar signal ~ RCS^0.25 (RadarParams.GetSignalStrength); a unit's RCS = definition.radarSize
  (Unit.Awake). Jamming = Unit.Jam(JamEventArgs{jammingUnit, jamAmount}) every 0.2 s, server side
  (JammingPod: power 13, falloff 1 -> 0 over 80 km). Missile top speed = sqrt(thrust/(0.5 Cd rho finArea)).
- B52Weapons.MakeDecoy clones the ALM-C450 (CruiseMissile1) into MissileDefinition + WeaponInfo + prefab:
  scale 0.45, mass 115, finArea x0.2 and thrust x0.2 (same top speed), fuel 45 kg / 2,400 s, radarSize 0.1
  (= B-52J), blast 0. Pylon racks of 8 (HSAB), MountedMissile.info and WeaponMount.info -> the decoy info.
- B52Systems.MaldJammer (Missile.Awake postfix, jsonKey B52_ADM160C): jams emitting enemy radars within
  30 km, 0.8 x (1 - d/30 km). Preset "JASSM-ER x8 + MALD-J x16 (SEAD)".

## Flights of 2026-10-05 (0.3.7)
- Bombing at altitude works: PAB-250LR x80 released from 39,500 ft at 397 kt TAS, all 80 over ~25 s, mass
  155 -> 133 t. Climb to 42,800 ft at full power, cruise 400-420 kt TAS (Mach 0.70-0.73).
- After the drop the aircraft was pushed into a dive (pitch input +0.2 to +0.6 = nose down, g 0.4..-1.4, never
  pulled). It reached 849 kt TAS at 3,400 ft, about Mach 1.3, and hit the ground at -65 deg. The game's only
  compressibility term (AeroJob_Math) adds at most +15 % drag around Mach 0.8-1.2: nothing stops a B-52 going
  supersonic.
- The other flight (ALND-4 x12 + MALD-J x16): an aborted takeoff at 57 kt (the chute deployed, as designed), then
  a second takeoff run from mid-runway. Still on the ground at 160 kt (156 t needs ~175 kt), rudder at full
  deflection in pulses, then a 137 deg/s yaw and 70 hp of damage: most likely off the runway end.
- Telemetry: WeaponManager.OnStationFired is raised for every trigger pull, including when nothing is released
  (on the ground, before the bay doors open). One flight logged 7,751 FIRED rows with the count unchanged.

## 0.3.8: transonic drag, telemetry cleanup (plugin only; the .nobp stays 0.3.7)
- B52Systems.MachDrag: Lock's fourth-power wave drag, dCd = 50 (M - 0.80)^4 on S = 371 m2, capped at 0.10, applied
  at the CoM along -V. Cruise Mach 0.84: +0.0001. Drag divergence (dCd 0.002) at Mach 0.88, the real B-52's limit
  is Mmo 0.90. Estimated top speeds at 132 t (existing CdS ~13.6 m2, fitted from the Oct 5 dive): 30 deg dive
  Mach 0.98, 65 deg dive at 5,000 ft Mach 0.96, vertical ~Mach 1.0. Airframe buffet (Aircraft.ShakeAircraft)
  above Mach 0.88.
- Telemetry: a FIRED row only when a station's count drops ("FIRED name xN (left)"); pulls that release nothing
  are summed into one "NO RELEASE" row. New column `mach` (before `event`).

## 0.4.0: landing gear rebuilt like the real one; model clean-up
Clean-up:
- Navigation lights: Move() reparented the donor's lights to our wingtips but kept the donor's world position, so
  they floated as black boxes under the wing at x +-15.75. Now placed at the wingtip's outermost point (x +-28.2),
  by the housing's bounds (its mesh is offset from its pivot).
- Cockpit interior poked through the skin as black slabs over the side windows and nose art (the flight deck is a
  straight box; the nose narrows and the roof drops). blender_cockpit.py now ray-casts each interior vertex from
  the cabin axis against the cockpit + fuselage_F skin and pulls anything within 3 cm of it or outside back in
  (366 vertices).
- B52Inspect.RenderShots (cameras from B52Shots.txt, optional hide list; lists renderers < 1.5 m) and
  RenderGearFolds (poses the gear as LandingGear.MoveGear does at given fold fractions).

Gear (bohmerang's model has none; the previous gear was placeholder cylinders):
- Real B-52G/H: four two-wheel trucks in tandem under the fuselage, a tip-protection outrigger near each wingtip
  that retracts into the outer wing. To retract, each truck swivels ~90 deg and folds flat into its well, port
  trucks forward and starboard aft. All four trucks can be turned up to 20 deg either way for crosswind landings
  (crab-angle knob on the centre pedestal). Sources: aircraftinformation.info (XB-52 history: swivel, opposite fold
  directions), Wikipedia "Crosswind landing" and "Undercarriage arrangements", migflug.com / simpleflying.com
  (crab +-20 deg), reviews.ipmsusa.org (outrigger doors).
- tools/blender_gear.py (after blender_prep.py): trunnion + bearings, oleo cylinder, chrome piston, gland nut,
  yoke, axle, brake housings, 56x16 tyres with rounded shoulders, dished rims, hub caps and bolts, torque-link
  scissor; outriggers: fore-aft trunnion, oleo, fork, single tyre. Pivots and names kept for the builder.
- B52Builder.BuildGear: fold hinge at the trunnion; mains fold 90 deg (port -90 = forward, starboard +90 = aft),
  swivel 90 deg (strutRotation, on a gear_<k>_swivel node of its own: steering writes the unsprung node every
  frame) and slide to the centreline + 0.2 m up (hingeFoldMotion), wheels flat side by side; foldSpeed 25 deg/s.
  Wells: FL 14.30..15.95, FR 12.65..14.30, RL -1.01..0.64, RR -2.66..-1.01 (clear of the bomb bay 1.16..12.16).
  Outriggers: hinge under a mount turned 125 deg so the leg folds inboard along the 35-deg swept outer wing, pivot
  raised into the wing's mid-plane; wheel ends flat at x +-19.2 inside the wing. Torque links are LandingGear IK
  joints (follow the suspension).
- Well doors: two clamshell panels per well, hinged at x +-0.78, opening 95 deg; each a curved panel cast onto the
  belly by vertical rays against the fuselage skin, with the skin's atlas UVs (reads as belly, takes liveries),
  dark inside.
- B52Systems.GearSystem (plugin 0.4.0): closes the well doors once the gear is locked down and opens them for
  retraction (LandingGear leaves them open while down and closes them after retracting). Unconfirmed for the real
  B-52 (no source found either way); it is the usual heavy-jet arrangement. Crosswind crab: [ / ] turn all four
  trucks 5 deg left/right (max 20), \ centres; trucks slew at 5 deg/s while the gear is down; the forward trucks
  still steer on top. On-screen "GEAR CRAB" readout. Config section [Gear].
- Verified with renders (renders/gear_v2): gear down doors flush; mid-fold doors open, trucks swivelling; retracted
  belly and wing clean. Not yet flown.

## 0.4.0 (cont.): real gear wells
- Main wells (tools/blender_wells.py): each truck's well is cut out of the lower fuselage skin along the V-shaped
  door outline; the cut-out skin becomes the door (gearDoorPanel_<k>, flush and in the skin's paint when shut,
  hinged at the chine, hangs 35 deg outboard while the gear is down), and a dark well box sits behind it for the
  truck to fold into. The model's internal faces inside the well are removed.
- Tip gear (tools/blender_wingwells.py): the outer wing is only 0.31 m deep at the outrigger, so the old fold
  (wheel stowed tilted 35 deg) pushed the wheel and leg through both skins. Now the wheel swivels 35 deg on the leg
  as it folds (strutRotation on the swivel node) and stows flat; fold 89.75 deg, 2.5 cm down (hingeFoldMotion); the
  tyre is the real 32x8.8 width and the knee, arm and oleo are slimmed so the stowed gear clears both skins to
  within a few mm. The trunnion now lies on the fold axis.
  The fold is simulated and everything that passes through the lower skin, in plan, is the slot (convex hull + 3 cm):
  4.7 m long, 0.25 m wide at the hinge to 0.8 m at the wheel. The cut-out skin becomes a strut door fixed to the
  leg (folds with it, hangs outboard of the oleo when down) and a wheel door over the wide end, hinged on its aft
  edge and hanging down 10 deg out. A dark well box under the upper skin closes the slot inside.
