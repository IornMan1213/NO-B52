# Making a B-52J livery

The whole exterior uses one 4096 x 4096 texture atlas (`blender/tex/B52_atlas.png`, built by
`tools/blender_atlas.py` + `tools/make_atlas.py`). A livery is a repaint of that atlas.

1. Start from `blender/tex/B52_atlas.png` (full resolution). `docs/livery_template.png` is the same layout at
   2048 px with every cell outlined and named (nose, forward/middle/rear fuselage, fin sides, tailplane, wing
   tops and bottoms, nacelles, flaps, small parts, colour swatches).
2. Paint only inside the cells; keep the 8 px border around each cell the same colour as its edge (it stops
   mip-mapping from bleeding neighbouring cells onto part edges).
3. Save as PNG at 4096 x 4096 and add it as a Nuclear Option `LiveryData` texture (Blueprinter, or the game's
   livery tools), for the B-52J (`B52J`).

The mod ships four, all made by the build from the one atlas:
- `USAF Gunship Gray`: the atlas as it is.
- `USAF Gunship Gray, clean`: `tools/make_clean_livery.py` fades the weathering streaks and grime into the paint
  and keeps markings, insignia and panel lines. A clean starting point for a repaint:
  `unity/Mods/B52/Textures/B52_atlas_clean.png`.
- `Southeast Asia camouflage, black belly` (the B-52D over Vietnam) and `Southeast Asia camouflage, white belly`
  (the 1970s nuclear alert scheme): `tools/make_liveries.py` paints tan FS 30219, medium green FS 34102 and dark
  green FS 34079 over the clean atlas. The patches and the belly line come from masks baked on the aircraft
  (`tools/blender_livery_masks.py`: which way each surface faces, and a 3D noise), so they carry on across parts
  and texture seams. Change the colours or where the belly line sits at the top of `make_liveries.py`.

The game applies the livery to every exterior part and uses the same texture for its damage shading. Gear,
bay, spoiler-panel grey, tyres, glass and the cockpit are separate materials and don't change with the livery.
