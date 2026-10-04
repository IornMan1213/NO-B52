# Heavy Aircraft Hangar

A separate mod in this repo: a hangar large enough to spawn the B-52 or any other aircraft, plus a plugin that
places it at airbases and sends oversize aircraft to it. Nothing in it depends on the B-52.

## The building

| | |
|---|---|
| Outside | 114 m wide, 78 m deep, 22 m eaves, 27 m ridge, plus a 46 m concrete apron |
| Door | 84 m x 17 m, three telescoping leaves per side stacking into 14 m pockets |
| Fits | B-52 (56.4 m span, 12.4 m tall) with 13.8 m each side and 4.6 m over the fin |
| Stock hangar_med for comparison | ~46 m x 8 m door, 80 x 45 m building |

Steel portal frames, arched roof, ceiling lamps that come on at night, a warning lamp over the door while it moves,
taxi line out to the apron. A 6 m concrete foundation skirt hides gently sloping ground.

In game it's `Heavy Aircraft Hangar` (json key `hangar_heavy`). It behaves like any stock hangar: it can be
damaged, repaired and captured with its airbase.

## How aircraft use it (plugin `HeavyHangar.dll`)

- **Routing**: an aircraft wider than 44 m or taller than 9 m (both configurable) won't spawn from a stock hangar
  when its airbase has a working heavy hangar. The airbase uses the heavy hangar instead.
- **What it offers**: everything the airbase's other hangars list. It never adds or removes aircraft types from a
  base, so mission and faction restrictions still apply.
- **No heavy hangar at the base**: oversize aircraft spawn on the ground just outside the stock hangar's door,
  instead of inside a building they don't fit.

## Where it appears

- **Automatically** (config `AutoPlace`, on by default): when a mission starts, the host puts one beside the
  longest takeoff runway of each land airbase that has one at least 1,800 m long, if it finds a site that is:
  - bare terrain: no tarmac, taxiways, buildings or water under the building or apron;
  - flat within 5 m, with the apron edge no more than 1.5 m above the ground;
  - joined to the runway edge by a taxi path no steeper than 10 %.

  It tries 40 positions per airbase (along the runway, both sides, 70–220 m back from the edge), and retries for
  about five minutes in case terrain around distant airbases hadn't loaded yet. Clients receive it through
  normal network spawning.
- **Mission editor**: it's listed with the other buildings and can be attached to any airbase. An airbase that
  already has one is skipped by automatic placement.

## Config (`BepInEx/config/com.ironman1213.heavyhangar.cfg`)

| Key | Default | |
|---|---|---|
| Placement.AutoPlace | true | add one per eligible airbase at mission start |
| Placement.MinRunwayLength | 1800 | metres |
| Routing.OversizeSpan | 44 | metres; wider aircraft prefer heavy hangars |
| Routing.OversizeHeight | 9 | metres |
| Debug.VerboseLog | false | log each rejected site and why |

## Install

Folder `BepInEx/plugins/HeavyHangar/` with `Heavy Hangar_x.y.z.nobp` and `HeavyHangar.dll`. Needs Blueprinter.
Every player in a multiplayer game needs it.

## Build

```
bash tools/build_hangar.sh     # textures, Blender model, Unity prefab + definition, .nobp, plugin
bash tools/install.sh hangar   # game closed
```

Sources: `tools/make_hangar_textures.py`, `tools/blender_hangar.py`, `unity/B52Tools/Editor/HangarBuilder.cs`
(donor: the game's hangar_med), `plugin/HeavyHangar/`.
