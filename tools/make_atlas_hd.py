"""High-resolution exterior atlas for the game: 8192 px instead of 4096. Run after blender_export.py, which copies
blender/tex/B52_atlas.png (4096) into unity/Mods/B52/Textures; this overwrites that copy:
  python tools/make_atlas_hd.py

Testers found the skin low-res next to the game's own aircraft. The model's source paintings are 1024 px for a
15 m fuselage section, so each pixel was about 1.5 cm and the panel lines were soft, a couple of pixels wide.
Each source here is enlarged 2x (Lanczos) into the same layout at twice the size, then:
  - sharpened a little (unsharp mask),
  - its panel lines found (thin strokes darker than the paint around them) and redrawn on their centre line as
    one crisp, slightly darker stroke, so they read as engraved lines and not a smudge,
  - given a faint fine-grained paint texture, so flat panels don't look like a smooth gradient up close.
Nothing is invented: lettering, insignia and nose art are only enlarged and sharpened.

The 4096 atlas stays the master for everything else (livery template, extra liveries, damage and smoothness maps,
which all share the UV layout, so any resolution lines up). B52Builder imports this one at 8192 with crunch
compression, which keeps the download close to the old 4096 BC7 texture.
"""
import json, os
import numpy as np
from PIL import Image, ImageFilter

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..'))
L = json.load(open(os.path.join(ROOT, 'blender', 'out', 'atlas_layout.json')))
K = 2
N = L['size'] * K
OUT = os.path.join(ROOT, 'unity', 'Mods', 'B52', 'Textures', 'B52_atlas.png')


def blur(x, r):
    h, w = x.shape
    p = np.pad(x, r + 1, mode='edge'); c = p.cumsum(0).cumsum(1); k = 2 * r + 1
    return (c[k:k + h, k:k + w] - c[:h, k:k + w] - c[k:k + h, :w] + c[:h, :w]) / (k * k)


def enhance(img, seed):
    a = np.asarray(img.filter(ImageFilter.UnsharpMask(radius=2, percent=60, threshold=1)), np.float32) / 255
    lum = a @ np.array([0.299, 0.587, 0.114], np.float32)
    dark = np.clip((blur(lum, 5) - lum - 0.008) / 0.025, 0, 1)
    # centre line of each stroke: a local maximum of darkness across it (either direction)
    ridge = (((dark >= np.roll(dark, 1, 0)) & (dark >= np.roll(dark, -1, 0))) |
             ((dark >= np.roll(dark, 1, 1)) & (dark >= np.roll(dark, -1, 1))))
    line = np.where(ridge & (dark > 0.45), 1.0, 0.0).astype(np.float32)
    line = np.maximum(line, 0.5 * np.maximum(np.roll(line, 1, 0), np.roll(line, 1, 1)))
    # only where the paint is plain: markings and lettering have strong edges of their own
    busy = blur(np.abs(lum - blur(lum, 2)), 4) > 0.03
    line *= ~busy
    a = a * (1 - 0.22 * line[..., None])
    g = np.random.default_rng(seed).normal(0, 1, lum.shape).astype(np.float32)
    a += (blur(g, 1) * 0.012)[..., None]
    return Image.fromarray(np.clip(a * 255 + 0.5, 0, 255).astype(np.uint8))


atlas = Image.new('RGB', (N, N), (128, 128, 128))
for i, e in enumerate(L['images']):
    x, y, w, h = (v * K for v in e['rect']); ix, iy, iw, ih = (v * K for v in e['inner'])
    src = enhance(Image.open(e['path']).convert('RGB').resize((iw, ih), Image.LANCZOS), i)
    atlas.paste(src.resize((w, h), Image.BILINEAR), (x, y))   # bleed border: the image stretched under itself
    atlas.paste(src, (ix, iy))
for s in L['swatches']:
    x, y, w, h = (v * K for v in s['rect'])
    atlas.paste(tuple(int(c * 255) for c in s['rgb']), (x, y, x + w, y + h))
atlas.save(OUT)
print(f'ATLASHD {N} px, {len(L["images"])} paintings enlarged and sharpened -> {OUT}')
