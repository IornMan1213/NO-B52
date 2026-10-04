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

The game applies the livery to every exterior part and uses the same texture for its damage shading. Gear,
bay, spoiler-panel grey, tyres, glass and the cockpit are separate materials and don't change with the livery.
