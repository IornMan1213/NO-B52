"""Writes a clean version of the B-52 exterior atlas for a second livery ("USAF Gunship Gray, clean"): the source
model's weathering (grime runs and streaks, which read as scratches up close) fades into the paint, while markings,
insignia, panel lines and rivets stay. Same layout as the atlas, so damage shading and contact shadows still line up.

Per atlas cell (the plain-colour swatches and the small engine-fan and sensor cells are left alone):
  local paint  = the cell blurred over ~50 px, ignoring the empty white background
  deviation    = pixel - local paint
  kept         = strong contrast (lettering), colour (insignia, the blue fin band) or fine detail (panel lines,
                 rivets: hairline high-pass)
  everything else keeps only CLEAN_KEEP of its deviation, and broad grime patches are pulled halfway to the cell's
  median paint.
Run: python tools/make_clean_livery.py   (after tools/make_atlas.py)"""
import json, os
import numpy as np
from PIL import Image
from atlas_util import blur, ramp

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..'))
L = json.load(open(os.path.join(ROOT, 'blender', 'out', 'atlas_layout.json')))
SRC = L['out']
OUT = os.path.join(ROOT, 'unity', 'Mods', 'B52', 'Textures', 'B52_atlas_clean.png')   # B52Builder makes it a livery
SKIP = ('colour', 'engfan', 'evs')           # palette swatches, fan blades, sensor blisters: no weathering
CLEAN_KEEP = 0.2                                 # share of the weathering left in
BROAD_PULL = 0.5                                 # how far broad grime patches move towards the cell's paint


def clean_cell(a):
    paint = (a.min(-1) < 232).astype(np.float32)            # the white background outside the UV islands stays as is
    if paint.sum() < 100:
        return a
    w = blur(paint, 24)[..., None] + 1e-4
    local = blur(a * paint[..., None], 24) / w             # normalized blur: the background doesn't darken the edges
    med = np.median(a[paint > 0], axis=0)
    dev = a - local
    lum = dev.mean(-1, keepdims=True)
    chroma = np.abs(dev - lum).max(-1, keepdims=True)
    hp = a - blur(a, 1.0)
    fine = np.abs(hp.mean(-1, keepdims=True))
    keep = np.maximum.reduce([ramp(np.abs(lum), 17, 28), ramp(chroma, 12, 26), ramp(fine, 4, 10)])
    broad = local - med
    broad_keep = np.maximum(ramp(np.abs(broad).max(-1, keepdims=True), 35, 60),         # big real colour areas stay
                            ramp(np.abs(broad - broad.mean(-1, keepdims=True)).max(-1, keepdims=True), 10, 25))
    base = med + broad * (1 - BROAD_PULL * (1 - broad_keep))
    out = base + dev * (CLEAN_KEEP + (1 - CLEAN_KEEP) * keep)
    m = paint[..., None]
    return out * m + a * (1 - m)


atlas = np.asarray(Image.open(SRC).convert('RGB')).astype(np.float32)
res = atlas.copy()
for e in L['images']:
    if any(k in e['name'] for k in SKIP):
        continue
    x, y, w, h = e['rect']
    res[y:y + h, x:x + w] = clean_cell(atlas[y:y + h, x:x + w])
Image.fromarray(np.clip(res, 0, 255).astype(np.uint8)).save(OUT)
print('CLEAN livery ->', OUT)
