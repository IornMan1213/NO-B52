# B-52J Stratofortress for Nuclear Option

A flyable B-52 for [Nuclear Option](https://store.steampowered.com/app/2168680/Nuclear_Option/). It uses real
B-52H airframe numbers and the B-52J modernisation package (F130 engines, AESA radar, smart-weapon internal
bay). It also has a custom crew compartment and real-world weapon loadouts (51 × Mk 82, 20 × JASSM-ER, and
more).

**Status:** work in progress.

## Requirements
- BepInEx 5 in Nuclear Option
- [Nikkorap's Blueprinter](https://github.com/nikkorap/NOBlueprinter-Releases/releases/latest)

## Install
1. Install BepInEx 5 and Nikkorap's Blueprinter (see Requirements).
2. Create `Nuclear Option/BepInEx/plugins/B-52J_Stratofortress/`.
3. Copy `B-52J Stratofortress_<version>.nobp` **and** `B52Systems.dll` into it. Remove any older B-52 `.nobp`.
4. The B-52J spawns from **medium hangars** (the ones that host the Darkreach).

`B52Systems.dll` keeps the airframe rigid and holds the B-52's runtime fixes. Its options are in
`BepInEx/config/com.ironman1213.b52systems.cfg`.

## Heavy Aircraft Hangar (companion mod)

The B-52 doesn't fit through stock hangar doors. The Heavy Hangar mod in this repo adds an 84 m-door hangar,
places one beside long runways automatically, and sends oversize aircraft (any mod, not just the B-52) to it.
Without it, the B-52 spawns just outside a stock hangar instead. See [HANGAR.md](HANGAR.md).

## Building from source
Run `bash tools/build_all.sh`, then `bash tools/install.sh`. See [MODLOG.md](MODLOG.md) for the pipeline, and
[LOADOUTS.md](LOADOUTS.md) for the weapons.

## Credits
- Base 3D model: "Boeing B-52 Stratofortress" by **bohmerang** on Sketchfab
  (https://sketchfab.com/3d-models/boeing-b-52-stratofortress-38b0c64bd552431394efa8625d7f5144), licensed CC BY 4.0.
  It was modified: split into parts, rescaled, rigged, and given a custom interior.
- Blueprinter by Nikkorap.
- Built with help from Claude (AI).

See [SPEC.md](SPEC.md) for the flight model and loadout numbers.
