"""
Damage texture for the exterior skin. Run after make_atlas.py:
  python tools/make_skin_detail.py
Reads blender/tex/B52_atlas.png, writes unity/Mods/B52/Textures/B52_atlas_dmg.png (2048 px): the skin scorched,
sooty and darkened, for the game's damage shading (it blends a part towards this as the part loses hit points).
B52Builder.ConvertMaterials puts it in the skin material; liveries painted from docs/livery_template.png share the
same layout, so the scorch lines up with any livery.

There is deliberately no normal map. One made from the atlas (luminance high-pass) embossed everything printed on
the skin, so the nose art, numbers, insignia and dirt streaks looked like dents; the panel lines stay as paint.
"""
import os
import numpy as np
from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC = os.path.join(ROOT, 'blender', 'tex', 'B52_atlas.png')
DMG = os.path.join(ROOT, 'unity', 'Mods', 'B52', 'Textures', 'B52_atlas_dmg.png')

# ---- damaged skin: desaturated, darkened by blotchy soot (smooth noise at a few scales), with the panel lines kept
rng = np.random.default_rng(52)
rgb = np.asarray(Image.open(SRC).convert('RGB'), dtype=np.float32) / 255.0
size = rgb.shape[1]
soot = np.zeros((size, size), np.float32)
for cells, weight in ((8, 0.45), (32, 0.30), (128, 0.25)):
    grid = Image.fromarray((rng.random((cells, cells)) * 255).astype(np.uint8))
    soot += np.asarray(grid.resize((size, size), Image.BICUBIC), dtype=np.float32) / 255.0 * weight
soot = np.clip((soot - 0.35) / 0.4, 0.0, 1.0)                  # 0 = scorched paint, 1 = burnt through to soot
grey = rgb.mean(axis=2, keepdims=True)
burnt = (grey * 0.6 + rgb * 0.4) * 0.55 * (1.0 - 0.75 * soot[..., None])   # 40% colour left, darker where sooty
burnt += np.array([0.035, 0.025, 0.015], np.float32) * soot[..., None]        # a warm brown in the soot
Image.fromarray(np.clip(burnt * 255.0 + 0.5, 0, 255).astype(np.uint8), 'RGB').resize((size // 2, size // 2), Image.LANCZOS) \
    .save(DMG, optimize=True)                                  # 2048 px is plenty for scorch marks, and half the file
print(f'SKIN damage texture -> {DMG}')
