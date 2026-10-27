"""
Trim textures for the flight deck's large plain surfaces (run once; the PNGs are committed in blender/tex):
  python tools/make_cockpit_trim.py
  wall_quilt.png   the quilted insulation blankets that line the B-52's cockpit walls and ceiling: padded panels
                   with stitched seams and snap fasteners, in the grey-green of the real blankets. One tile = 0.5 m.
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
    base = np.array([0.54, 0.57, 0.52], np.float32)              # grey-green insulation fabric (the cabin is lit dimly)
    y, x = np.mgrid[0:W, 0:W].astype(np.float32)
    cells = 3                                                    # 3 x 3 pillows per 0.5 m tile (~17 cm, like the real ones)
    u = (x / W * cells) % 1.0; v = (y / W * cells) % 1.0
    # each pillow bulges: brightest in the middle, falling off towards the stitched seams
    bulge = np.sin(np.pi * u) * np.sin(np.pi * v)
    shade = 0.88 + 0.14 * bulge ** 0.6                            # soft padding, not deep pillows
    seam = (np.minimum(np.minimum(u, 1 - u), np.minimum(v, 1 - v)) < 0.018).astype(np.float32)
    shade = shade * (1 - 0.16 * seam)
    # stitch dashes along the seams
    stitch = seam * ((((x + y) // 6) % 2) == 0)
    shade = shade * (1 - 0.07 * stitch)
    # snap fasteners at the corners of every second pillow
    cu = np.minimum(u, 1 - u); cv = np.minimum(v, 1 - v)
    snap = ((cu ** 2 + cv ** 2) < 0.0012) & ((((x / W * cells).astype(int) + (y / W * cells).astype(int)) % 2) == 0)
    shade = np.where(snap, 0.70, shade)
    shade = shade + noise(8, 0.05) + noise(64, 0.04)
    rgb = np.clip(base[None, None, :] * shade[..., None], 0, 1)
    img = Image.fromarray((rgb * 255).astype(np.uint8), 'RGB').filter(ImageFilter.GaussianBlur(0.6))
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
