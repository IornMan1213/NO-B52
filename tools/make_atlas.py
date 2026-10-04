"""Composes the B-52 exterior atlas from blender/out/atlas_layout.json (written by tools/blender_atlas.py).
Each source image is scaled into its cell's inner rect; the bleed border around it is filled by extending the
edge pixels, so mip-mapping doesn't pull neighbouring cells into a part's edges. Plain-colour materials become
solid swatches. Also writes docs/livery_template.png: the atlas with cell outlines and labels, for painting
new liveries. Run: python tools/make_atlas.py"""
import json, os
from PIL import Image, ImageDraw, ImageFont

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..'))
L = json.load(open(os.path.join(ROOT, 'blender', 'out', 'atlas_layout.json')))
N = L['size']
atlas = Image.new('RGB', (N, N), (128, 128, 128))

for e in L['images']:
    x, y, w, h = e['rect']; ix, iy, iw, ih = e['inner']
    src = Image.open(e['path']).convert('RGB').resize((iw, ih), Image.LANCZOS)
    # bleed: stretch the image to the outer rect first, then paste the exact-size copy in the middle
    atlas.paste(src.resize((w, h), Image.NEAREST), (x, y))
    edge = Image.new('RGB', (w, h))
    edge.paste(src.resize((w, h), Image.BILINEAR))
    atlas.paste(edge, (x, y))
    atlas.paste(src, (ix, iy))
for s in L['swatches']:
    x, y, w, h = s['rect']
    atlas.paste(tuple(int(c * 255) for c in s['rgb']), (x, y, x + w, y + h))

os.makedirs(os.path.dirname(L['out']), exist_ok=True)
atlas.save(L['out'])

# Livery template: same pixels, cells outlined and named
tpl = atlas.copy(); d = ImageDraw.Draw(tpl)
try: f = ImageFont.truetype('arial.ttf', 28)
except OSError: f = ImageFont.load_default()
for e in L['images']:
    x, y, w, h = e['rect']
    d.rectangle((x, y, x + w - 1, y + h - 1), outline=(255, 0, 255), width=3)
    d.text((x + 12, y + 8), os.path.splitext(e['name'])[0], fill=(255, 0, 255), font=f)
os.makedirs(os.path.join(ROOT, 'docs'), exist_ok=True)
tpl.resize((2048, 2048), Image.LANCZOS).save(os.path.join(ROOT, 'docs', 'livery_template.png'))
print('ATLAS ->', L['out'], f'({len(L["images"])} images, {len(L["swatches"])} swatches)')
