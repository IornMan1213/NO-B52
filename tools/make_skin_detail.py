"""
Surface and damage maps for the exterior skin. Run after make_atlas.py:
  python tools/make_skin_detail.py
Reads blender/tex/B52_atlas.png and writes, on the same layout (so they line up with every livery):

  unity/Mods/B52/Textures/B52_atlas_ms.png      metal (R) and smoothness (A), 2048 px, linear
  unity/Mods/B52/Textures/B52_atlas_dmg.png     damage colour (RGB) and damage order (A), 2048 px
  unity/Mods/B52/Textures/B52_atlas_dmg_n.png   damage dents, a normal map, 2048 px

Smoothness: testers found the skin far shinier than the game's own aircraft. It had one flat value (0.40) over
the whole airframe. A B-52's Gunship Gray is a flat, weathered paint: this gives it 0.20 on average, rougher
still in the grime and along the panel lines, a little variation panel to panel, and slightly glossier where
the paint is cleaner. Metalness stays a trace (paint, not bare metal).

Damage, in the game's own format (as its aircraft's *_skin_dmg_b maps are made): the skin shader cuts the skin
open where the part's damage has passed the alpha (low alpha = a hole, opening early), and blends the colour in by
the part's damage. So the colour is mostly white (no change) with soft scorch only round the hits, and the alpha
marks only the holes, a few centimetres across, opening one cluster after another and tearing a little wider as
the part is hurt more. The old map was a dark copy of the skin with no alpha, so a damaged part, the nose most
often, went uniformly black. The dents (normal map) follow the same holes.
"""
import os
import numpy as np
from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC = os.path.join(ROOT, 'blender', 'tex', 'B52_atlas.png')
OUT = os.path.join(ROOT, 'unity', 'Mods', 'B52', 'Textures')
MS, DMG, DMGN = (os.path.join(OUT, n) for n in ('B52_atlas_ms.png', 'B52_atlas_dmg.png', 'B52_atlas_dmg_n.png'))
S = 2048

rng = np.random.default_rng(52)
rgb = np.asarray(Image.open(SRC).convert('RGB').resize((S, S), Image.LANCZOS), dtype=np.float32) / 255.0
lum = rgb @ np.array([0.299, 0.587, 0.114], np.float32)


def blur(a, r):
    """Box blur of radius r (a mean over a (2r+1)^2 window), by summed-area table, edges clamped."""
    p = np.pad(a, r + 1, mode='edge')
    c = p.cumsum(0).cumsum(1)
    k = 2 * r + 1
    return (c[k:k + S, k:k + S] - c[:S, k:k + S] - c[k:k + S, :S] + c[:S, :S]) / (k * k)


def noise(cells, seed):
    """Smooth value noise, 0..1, with `cells` features across the map."""
    g = np.random.default_rng(seed).random((cells, cells)).astype(np.float32)
    return np.asarray(Image.fromarray((g * 255).astype(np.uint8)).resize((S, S), Image.BICUBIC), np.float32) / 255.0


# ---- the skin's own detail, measured from the paint: panel lines (thin and darker than around them) and grime
local = blur(lum, 6)
lines = np.clip((local - lum - 0.012) / 0.03, 0, 1)                   # 1 on a panel line
grime = np.clip((blur(lum, 40) - local) / 0.06, 0, 1)                 # 1 in dirty, darker patches
clean = np.clip((local - blur(lum, 40)) / 0.06, 0, 1)                 # 1 where the paint is lighter than around it
panel = noise(48, 1) - 0.5                                            # panel-to-panel variation

# ---- metal / smoothness
smooth = 0.20 + 0.05 * panel - 0.07 * grime - 0.08 * lines + 0.05 * clean + (noise(256, 2) - 0.5) * 0.03
smooth = np.clip(smooth, 0.06, 0.34)
metal = np.full((S, S), 0.05, np.float32)
ms = np.dstack([metal, metal, metal, smooth])
Image.fromarray((ms * 255 + 0.5).astype(np.uint8), 'RGBA').save(MS, optimize=True)
print(f'SKIN metal/smoothness -> {MS} (smoothness mean {smooth.mean():.2f}, was 0.40 flat)')

# ---- damage: colour (multiplied into the paint) and order (alpha: low = shows at the first hits)
col = np.ones((S, S, 3), np.float32)
order = np.full((S, S), 1.0, np.float32)
height = np.zeros((S, S), np.float32)

# Measured in game (tests/ingame/b52_look_fixes.txt, 19 Nov): the shader CUTS the skin open where alpha is below the
# part's damage (low alpha = a hole you see through, earliest first), and blends the colour in by the part's damage
# everywhere at once. So: alpha marks only the holes themselves (each may tear a little wider as damage grows), and
# the colour holds only scorch that belongs to hits, round the hole clusters. A first version also had broad noise
# burn over half the skin: by 50% it covered the nose in hard-edged camouflage blotches, and its soot rings, given
# low alpha, were cut out too, into holes half a metre wide.

# Hits: clusters of holes, each with a soft soot scorch around it.
yy, xx = np.mgrid[0:S, 0:S].astype(np.float32)
n_clusters = 700
for k in range(n_clusters):
    cx, cy = rng.random(2) * S
    first = 0.02 + rng.random() * 0.6                                 # how early this cluster's holes open
    spread = rng.uniform(5, 12)
    # the cluster's scorch: one soft patch round all its holes, brown-black, never pure black
    r_scorch = spread * rng.uniform(2.5, 4.0)
    x0, x1 = int(max(cx - r_scorch - 2, 0)), int(min(cx + r_scorch + 3, S))
    y0, y1 = int(max(cy - r_scorch - 2, 0)), int(min(cy + r_scorch + 3, S))
    if x1 > x0 and y1 > y0:
        d = np.hypot(xx[y0:y1, x0:x1] - cx, yy[y0:y1, x0:x1] - cy) / r_scorch
        wob = 1 + 0.35 * (np.random.default_rng(k).random() - 0.5)
        f = np.clip(1 - d * wob, 0, 1) ** 1.8                         # soft edge, densest in the middle
        col[y0:y1, x0:x1] *= (1 - 0.45 * f)[..., None]
    for _ in range(rng.integers(1, 6)):
        hx, hy = cx + rng.normal(0, spread), cy + rng.normal(0, spread)
        r_hole = rng.uniform(0.7, 2.0)                                # 2048 px over the whole airframe: a few cm
        r_tear = r_hole * rng.uniform(1.2, 1.7)                       # how far it tears open by the end
        r_soot = r_hole * rng.uniform(3, 6)
        x0, x1 = int(max(hx - r_soot - 2, 0)), int(min(hx + r_soot + 3, S))
        y0, y1 = int(max(hy - r_soot - 2, 0)), int(min(hy + r_soot + 3, S))
        if x1 <= x0 or y1 <= y0: continue
        d = np.hypot(xx[y0:y1, x0:x1] - hx, yy[y0:y1, x0:x1] - hy)
        o = np.where(d < r_hole, first, np.where(d < r_tear, first + 0.15 + 0.2 * (d - r_hole) / (r_tear - r_hole + 1e-6), 1.0))
        order[y0:y1, x0:x1] = np.minimum(order[y0:y1, x0:x1], o)
        t = np.clip(d / r_soot, 0, 1)                                 # 0 at the hole, 1 at the soot's edge
        col[y0:y1, x0:x1] *= (0.35 + 0.65 * t ** 1.3)[..., None]      # a dark ring at the edge of the hole, fading out
        petals = np.clip(1 - d / (r_hole * 2.2), 0, 1)                 # torn metal lips around the hole, then a dimple
        height[y0:y1, x0:x1] += -np.clip(1 - d / r_hole, 0, 1) * 2.0 + petals * 0.6 - np.clip(1 - t, 0, 1) ** 2 * 0.5

col *= np.array([1.0, 0.94, 0.86], np.float32) ** (1 - col)          # soot is a warm brown-black, not grey

col = np.clip(col, 0.03, 1.0)
order = np.clip(order, 0.0, 1.0)
dmg = np.dstack([col, order])
Image.fromarray((dmg * 255 + 0.5).astype(np.uint8), 'RGBA').save(DMG, optimize=True)

# dents -> tangent-space normal map (OpenGL convention, as Unity's)
gy, gx = np.gradient(height)
strength = 1.6
n = np.dstack([-gx * strength, gy * strength, np.ones_like(gx)])
n /= np.linalg.norm(n, axis=2, keepdims=True)
Image.fromarray(((n * 0.5 + 0.5) * 255 + 0.5).astype(np.uint8), 'RGB').save(DMGN, optimize=True)
print(f'SKIN damage texture -> {DMG} and {DMGN}: {n_clusters} hit clusters; '
      f'{(order < 0.3).mean():.1%} shows at the first hits, {(order < 1).mean():.1%} can be cut open')
