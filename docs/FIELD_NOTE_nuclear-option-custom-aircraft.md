# Field note: a custom aircraft for Nuclear Option (Blueprinter), built headless

- Game: Nuclear Option 0.34.1 (Unity 2022.3.62f2, Mono), BepInEx 5.4.23.5, Blueprinter 2.0.1, Blueprinter Editor 0.1.0
- Route: AssetRipper (headless HTTP API) → Blueprinter Editor (Unity batchmode `-executeMethod`) → `.nobp`, plus a
  small BepInEx/Harmony plugin for runtime behaviour.
- Result: a B-52J with 8 engines, quadricycle gear, a custom flight deck, 62 weapon racks, and a flight model tuned
  against the game's own equations.

## Steps that worked
1. AssetRipper 2.0.0 `--headless --port N`: POST `/Settings/Update` (ScriptContentLevel=Level1,
   ScriptExportMode=Decompiled), then `/LoadFile` (Path=NuclearOption.exe), then `/Export/UnityProject` (Path=out).
2. Blueprinter Project Setup without the GUI: call `GameAssemblies.Import(managed, version)`,
   `AssetRipperImporter.Import(assetsDir)`, `OpReferenceIndex.Refresh()` and `ModBuilder.BuildGameAssets()`, each
   in its own Unity batchmode run. Editor scripts must live in an asmdef (loose .cs in Assets breaks Blueprinter).
3. Graft a vanilla aircraft prefab (`*_PLACEHOLDER`) onto your own FBX hierarchy: copy components with
   `EditorUtility.CopySerialized`, move whole sub-systems (pilots, nav lights), then remap every ObjectReference
   that points into the donor.
4. `ModBuilder.Build(modName, displayName, version, outDir)` builds the .nobp headless.

## Gotchas (numbered)
1. FBX from Blender: don't use `bake_space_transform` on hierarchies. Use Unity `bakeAxisConversion`, then bake
   the leftover Rx(90) into the meshes.
2. ControlSurface rotates the visible mesh about local X. Each hinge needs a frame with X along the hinge.
3. Lift: alpha is measured in the `liftNormal` frame (`atan2(v.y, v.z)`), so rotate that frame for incidence or
   flap camber. Airfoil curves are in radians. Parasitic drag is 0.25·ρ·V²·dragArea.
4. Turbofan thrust = static × altitudeThrust(m) × speedThrust(m/s), with no density term. Supply lapse curves.
5. Complex physics (local player) makes every part its own rigidbody with a FixedJoint. Big aircraft sag, fight
   their own colliders and snap. A Harmony prefix that skips `Aircraft.SetComplexPhysics` keeps it rigid.
6. LandingGear's ground ray starts at the bumpstop. Its BoxCollider must start disabled (as the stock gear does).
7. `LoadoutSelector.LoadDefaults` reads `loadouts[1]`. Liveries are Addressables, so you need your own
   LiveryData asset.
8. Donor leftovers that NRE: NavLights entries, RadarLocator.essentialParts, TargetCam.attachedPart,
   Turbofan.criticalParts, JetNozzle missing (no IR source, so StatusGauges NRE), swing-wing HUDExtras, Downwash.
9. In simple physics the CG comes from the root UnitPart's `centerOfMass` transform (applied in ModifyMass).

## How it was verified
In-game BepInEx log + Player.log (zero B-52 exceptions); a CSV flight recorder in the plugin; an offline checker
that reimplements AeroJob_Math (lift-off 150-196 kt, 631 mph at 10.7 km).
