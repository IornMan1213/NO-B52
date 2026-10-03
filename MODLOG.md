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

## Next
- [ ] User: download bohmerang's GLB to `source_assets/`
- [x] Private repo https://github.com/IornMan1213/NO-B52
- [ ] Get AssetRipper and the Blueprinter Editor → set up the Unity project (1-3 h)
- [ ] Blender split/rig script (`tools/blender_prep.py`)
- [ ] Vertical slice: the B-52 spawns at an airbase and flies, with no weapons and a placeholder cockpit
