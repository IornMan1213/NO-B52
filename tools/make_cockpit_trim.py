"""
Trim textures for the flight deck's large plain surfaces (run once; the PNGs are committed in blender/tex):
  python tools/make_cockpit_trim.py
  wall_quilt.png   the quilted insulation blankets that line the B-52's cockpit walls and ceiling: padded panels
                   with sewn seams, in the grey-green of the real blankets. 1024 px, one tile = 0.5 m.
  floor_tread.png  dark non-slip floor covering with raised strips and grit. One tile = 0.5 m.
blender_cockpit.py maps them in world metres (triplanar), so the pattern keeps its size on every wall.
"""
import os
import numpy as np
from PIL import Image, ImageFilter

OUT = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), 'blender', 'tex')
rng = np.random.default_rng(1952)
W = 512


def noise(scale, amp):
    g = Image.fromarray((rng.random((W // scale, W // scale)) * 255).astype(np.uint8)).resize((W, W), Image.BICUBIC)
    return (np.asarray(g, np.float32) / 255.0 - 0.5) * amp


def wall_quilt(path):
    # Crisp, even quilting: the old version's large soft blotches read as dirty plaster in game. Real blankets are
    # rectangles about 12 x 17 cm with sewn seams; the fabric itself is plain, with a fine weave.
    S = 1024
    base = np.array([0.43, 0.46, 0.43], np.float32)              # grey-green insulation fabric, toned down
    y, x = np.mgrid[0:S, 0:S].astype(np.float32)
    cx, cy = 4, 3                                                # 4 x 3 pillows per 0.5 m tile
    u = (x / S * cx) % 1.0; v = (y / S * cy) % 1.0
    du = np.minimum(u, 1 - u) * (0.5 / cx) * 2; dv = np.minimum(v, 1 - v) * (0.5 / cy) * 2   # metres to the seam
    edge = np.minimum(du, dv)
    shade = 0.80 + 0.20 * np.clip(edge / 0.035, 0, 1) ** 0.5    # padding rounds off over the last 3.5 cm
    seam = np.clip(1 - edge / 0.0025, 0, 1)                      # 2.5 mm sewn seam
    shade *= 1 - 0.35 * seam
    stitch = (seam > 0.3) & (((x + y) // 5) % 2 == 0)
    shade = np.where(stitch, shade * 0.9, shade)
    rng_w = np.random.default_rng(7)
    weave = (rng_w.random((S, S)).astype(np.float32) - 0.5) * 0.035   # fine weave, no blotches
    shade = shade + weave
    rgb = np.clip(base[None, None, :] * shade[..., None], 0, 1)
    img = Image.fromarray((rgb * 255).astype(np.uint8), 'RGB').filter(ImageFilter.GaussianBlur(0.5))
    img.save(path, optimize=True)


def floor_tread(path):
    y, x = np.mgrid[0:W, 0:W].astype(np.float32)
    shade = 0.11 + noise(4, 0.03) + noise(1, 0.025)              # dark rubber with fine grit
    strip = ((y % 64) < 10).astype(np.float32)                   # raised strips, 8 per 0.5 m
    edge = (((y % 64) >= 10) & ((y % 64) < 12)).astype(np.float32)
    shade = shade + 0.05 * strip - 0.03 * edge
    rgb = np.clip(np.stack([shade, shade, shade * 1.03], -1), 0, 1)
    Image.fromarray((rgb * 255).astype(np.uint8), 'RGB').save(path, optimize=True)


os.makedirs(OUT, exist_ok=True)
wall_quilt(os.path.join(OUT, 'wall_quilt.png'))
floor_tread(os.path.join(OUT, 'floor_tread.png'))
print('TRIM wall_quilt.png, floor_tread.png ->', OUT)
