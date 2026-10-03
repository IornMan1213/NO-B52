# MODLOG: B-52 for Nuclear Option

## Environment (2026-10-03)
- Game: Nuclear Option, Steam 2168680, `C:\Program Files (x86)\Steam\steamapps\common\Nuclear Option`
  - Unity 2022.3.62f2 (Mono), no anti-cheat. BepInEx 5.4.23.5 installed. Assembly-CSharp.dll is dated
    2026-08-14.
  - Blueprinter (Nikkorap) is installed in `BepInEx/plugins` and loads `.nobp` files (UnityFS asset bundles).
  - Saves: `C:\Users\jayea\AppData\LocalLow\Shockfront\NuclearOption`
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
- AssetRipper 2.0.0 export → `C:/Users/jayea/no-ripped/NuclearOption/ExportedProject` (3.0 GB). Game version
  0.34.1 (from bundleVersion).
- Driven headless over its HTTP API: `--headless --port 47110`, POST /Settings/Update
  (ScriptContentLevel=Level1, ScriptExportMode=Decompiled), /LoadFile, /Export/UnityProject.
- Blueprinter project at `C:/Users/jayea/BlueprinterProject/Blueprinter-Editor`. The repo's `unity/B52Tools`
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

## Version history
- 0.1.0: first build. Spawned, then fell apart (joint strengths).
- 0.1.1-0.1.3: load-sized joints, self-collision ignore, NRE cleanup, nozzles/IR.
- 0.1.4: gear collider fix. Gear held.
- 0.2.0: full ordnance list (32 bay racks, 30 HSAB racks, 9 presets).
- 0.2.1: rigid airframe, 6° wing incidence, CG at ~25% MAC.
- 0.2.2: B-52 airfoil, F130 thrust curves, flaps as inner-wing camber.
- 0.2.4: live standby instruments (ASI, ALT, VSI, ADI) driven by B52Systems.Instruments; throttles +45° (forward = full), yokes 10°.
- 0.2.3: one-way spoiler panels (SpoilerDriver), gear folds up into belly, engines at measured nacelle centres, analytic (symmetric) hinge axes.

## Next
- [ ] User test: hull clean? Lift-off speed? Max speed at altitude?
- [ ] One-way spoilerons (plugin), visible spoiler panels
- [ ] MALD / MALD-J custom decoy
- [ ] Single-texture atlas so liveries work
- [ ] Animate the gear retraction properly (B-52 trucks swivel 90° then fold)
