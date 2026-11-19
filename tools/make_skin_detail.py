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

Damage, in the game's own format (as its aircraft's *_skin_dmg_b maps are made): the skin shader darkens a part's
paint by the damage colour wherever the part's damage has passed the damage order in alpha. So the colour is
mostly white (no change), and the alpha says what shows first: bullet and shrapnel holes and their soot (low
alpha) appear at the first hits and their scorch spreads outward as the part is hurt more; broad burn and soot
over whole panels (high alpha) only on a part near destruction, and some paint never darkens at all. The old map
was a dark copy of the skin with no alpha, so a damaged part, the nose most often, went uniformly black.
The dents (normal map) follow the same holes, with a light crumple over the burnt panels.
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

# Broad burn: whole panels scorch late, in blotches, and about half the skin never darkens.
burn = noise(40, 3) * 0.6 + noise(160, 4) * 0.4
burnt = burn > 0.47
order = np.where(burnt, np.clip(0.95 - (burn - 0.47) * 1.2, 0.62, 0.95), 1.0).astype(np.float32)
soot_tone = np.clip(0.55 + 0.45 * noise(220, 5), 0, 1)                # darker and lighter soot
col *= np.where(burnt, 0.48 + 0.42 * soot_tone, 1.0)[..., None]
col *= np.array([1.0, 0.97, 0.93], np.float32)                        # soot is a warm brown-black, not grey
height += np.where(burnt, (noise(300, 6) - 0.5) * 0.6, 0)             # a light crumple over burnt panels

# Hits: clusters of holes, each with a soot ring that grows as the part is hurt more.
yy, xx = np.mgrid[0:S, 0:S].astype(np.float32)
n_clusters = 700
for k in range(n_clusters):
    cx, cy = rng.random(2) * S
    first = rng.random() * 0.45                                       # when this cluster's first holes appear
    for _ in range(rng.integers(1, 7)):
        hx, hy = cx + rng.normal(0, 9), cy + rng.normal(0, 9)
        r_hole = rng.uniform(0.9, 2.6)                                # 2048 px over the whole airframe: a few cm
        r_soot = r_hole * rng.uniform(4, 9)
        x0, x1 = int(max(hx - r_soot - 2, 0)), int(min(hx + r_soot + 3, S))
        y0, y1 = int(max(hy - r_soot - 2, 0)), int(min(hy + r_soot + 3, S))
        if x1 <= x0 or y1 <= y0: continue
        d = np.hypot(xx[y0:y1, x0:x1] - hx, yy[y0:y1, x0:x1] - hy)
        t = np.clip(d / r_soot, 0, 1)                                 # 0 at the hole, 1 at the soot's edge
        o = np.where(d < r_hole, first, first + 0.06 + t * rng.uniform(0.25, 0.5))
        order[y0:y1, x0:x1] = np.minimum(order[y0:y1, x0:x1], np.where(t < 1, o, 1.0))
        shade = np.where(d < r_hole, 0.06, 0.22 + 0.78 * t ** 1.6)    # the hole near black, soot fading out
        col[y0:y1, x0:x1] *= shade[..., None]
        petals = np.clip(1 - d / (r_hole * 2.2), 0, 1)                 # torn metal lips around the hole, then a dimple
        height[y0:y1, x0:x1] += -np.clip(1 - d / r_hole, 0, 1) * 2.0 + petals * 0.6 - np.clip(1 - t, 0, 1) ** 2 * 0.5

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
      f'{(order < 0.3).mean():.1%} shows at the first hits, {(order >= 1).mean():.0%} never darkens')
