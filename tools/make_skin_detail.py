"""
Surface detail for the exterior skin: a normal map made from the atlas's own panel lines and rivets, so they read as
shallow grooves and dimples under light instead of paint. Run after make_atlas.py:
  python tools/make_skin_detail.py
Reads blender/tex/B52_atlas.png, writes to unity/Mods/B52/Textures/:
  B52_atlas_normal.png  normal map (OpenGL / Unity convention: green = +V)
  B52_atlas_dmg.png     the damaged skin (2048 px): scorched, sooty and darkened, for the game's damage shading (it
                        blends a part towards this as the part loses hit points)
B52Builder.ConvertMaterials puts them in the skin material; liveries painted from docs/livery_template.png share the
same layout, so the detail lines up with any livery.

Method: luminance high-pass (fine detail only: panel lines, rivets, hatch outlines), dark = recessed, turned into a
normal map with central differences. The solid colour swatches and flat paint have no detail, so they stay flat.
"""
import os
import numpy as np
from PIL import Image, ImageFilter

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC = os.path.join(ROOT, 'blender', 'tex', 'B52_atlas.png')
DST = os.path.join(ROOT, 'unity', 'Mods', 'B52', 'Textures', 'B52_atlas_normal.png')
DMG = os.path.join(ROOT, 'unity', 'Mods', 'B52', 'Textures', 'B52_atlas_dmg.png')
BLUR = 5.0          # px at 4096: wider features (camouflage, markings, dirt) are left out
CLIP = 0.06         # luminance swing treated as a full-depth line; larger jumps (paint edges) are capped
STRENGTH = 2.2      # slope scale: panel lines a few px wide come out as gentle grooves
DEAD = 0.15         # swings below this (of CLIP) are print grain and scan noise, not panel detail

img = Image.open(SRC).convert('L')
lum = np.asarray(img, dtype=np.float32) / 255.0
low = np.asarray(img.filter(ImageFilter.GaussianBlur(BLUR)), dtype=np.float32) / 255.0
height = np.clip(lum - low, -CLIP, CLIP) / CLIP           # -1 recessed (dark lines) .. +1 raised
height = np.sign(height) * np.maximum(np.abs(height) - DEAD, 0.0) / (1.0 - DEAD)   # texture grain stays flat
height = np.asarray(Image.fromarray(((height + 1) * 127.5).astype(np.uint8)).filter(ImageFilter.GaussianBlur(0.7)),
                    dtype=np.float32) / 127.5 - 1.0       # soften the pixel steps

dx = np.zeros_like(height); dy = np.zeros_like(height)
dx[:, 1:-1] = (height[:, 2:] - height[:, :-2]) * 0.5
dy[1:-1, :] = (height[2:, :] - height[:-2, :]) * 0.5     # rows go down the image, i.e. -V
nx = -dx * STRENGTH
ny = dy * STRENGTH                                        # +V is up the image: d/dV = -d/drow, and ny = -dH/dV
nz = np.ones_like(height)
n = np.sqrt(nx * nx + ny * ny + nz * nz)
rgb = np.stack([nx / n, ny / n, nz / n], axis=-1) * 0.5 + 0.5
Image.fromarray(np.clip(rgb * 255.0 + 0.5, 0, 255).astype(np.uint8), 'RGB').save(DST, optimize=True)
flat = float(np.mean(np.abs(nz / n - 1.0) < 1e-3))
print(f'SKIN normal map {img.size[0]}x{img.size[1]} -> {DST} ({flat:.0%} flat)')

# ---- damaged skin: desaturated, darkened by blotchy soot (smooth noise at a few scales), with the panel lines kept
rng = np.random.default_rng(52)
size = img.size[0]
soot = np.zeros((size, size), np.float32)
for cells, weight in ((8, 0.45), (32, 0.30), (128, 0.25)):
    grid = Image.fromarray((rng.random((cells, cells)) * 255).astype(np.uint8))
    soot += np.asarray(grid.resize((size, size), Image.BICUBIC), dtype=np.float32) / 255.0 * weight
soot = np.clip((soot - 0.35) / 0.4, 0.0, 1.0)                  # 0 = scorched paint, 1 = burnt through to soot
rgb = np.asarray(Image.open(SRC).convert('RGB'), dtype=np.float32) / 255.0
grey = rgb.mean(axis=2, keepdims=True)
burnt = (grey * 0.6 + rgb * 0.4) * 0.55 * (1.0 - 0.75 * soot[..., None])   # 40% colour left, darker where sooty
burnt += np.array([0.035, 0.025, 0.015], np.float32) * soot[..., None]        # a warm brown in the soot
Image.fromarray(np.clip(burnt * 255.0 + 0.5, 0, 255).astype(np.uint8), 'RGB').resize((size // 2, size // 2), Image.LANCZOS) \
    .save(DMG, optimize=True)                                  # 2048 px is plenty for scorch marks, and half the file
print(f'SKIN damage texture -> {DMG}')
