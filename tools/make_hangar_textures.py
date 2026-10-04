"""Draws the heavy hangar textures (original art, PIL only).
Run: python tools/make_hangar_textures.py  ->  blender/tex_hangar/*.png
All textures tile; the Blender script maps them with world-space box UVs (1 UV unit = TILE metres)."""
import os, random
from PIL import Image, ImageDraw, ImageFilter, ImageFont

OUT = os.path.join(os.path.dirname(__file__), '..', 'blender', 'tex_hangar')
os.makedirs(OUT, exist_ok=True)
rnd = random.Random(52)


def font(sz):
    for f in ('arialbd.ttf', 'arial.ttf'):
        try:
            return ImageFont.truetype(f, sz)
        except OSError:
            pass
    return ImageFont.load_default()


def noise(im, amount, blur=1.2):
    """Adds soft grime so large flat panels don't look like solid colour."""
    w, h = im.size
    n = Image.new('L', (w // 8, h // 8))
    n.putdata([rnd.randint(0, 255) for _ in range(n.size[0] * n.size[1])])
    n = n.resize((w, h), Image.BICUBIC).filter(ImageFilter.GaussianBlur(blur * 8))
    dark = Image.new('RGB', (w, h), (0, 0, 0))
    return Image.composite(dark, im, n.point(lambda v: int(v * amount)))


def corrugated(path, base, rib=16, W=512):
    """Vertical corrugated steel cladding: 1 tile = 8 m, a rib every 0.25 m."""
    im = Image.new('RGB', (W, W), base); d = ImageDraw.Draw(im)
    for x in range(0, W, rib):
        for k in range(rib):
            s = 0.80 + 0.35 * abs((k / rib) - 0.5)          # light/dark sweep across each rib
            c = tuple(min(255, int(v * s)) for v in base)
            d.line((x + k, 0, x + k, W), fill=c)
    for y in (W // 2 - 1, W - 2):                           # sheet laps every 4 m
        d.line((0, y, W, y), fill=tuple(int(v * 0.7) for v in base), width=2)
    noise(im, 0.18).save(path)


def concrete(path, W=512):
    """Concrete slab: 1 tile = 8 m, expansion joints on a 4 m grid, tyre marks."""
    im = Image.new('RGB', (W, W), (150, 148, 142))
    im = noise(im, 0.25, 0.6); d = ImageDraw.Draw(im)
    for p in (0, W // 2):
        d.line((p, 0, p, W), fill=(95, 93, 90), width=3); d.line((0, p, W, p), fill=(95, 93, 90), width=3)
    for _ in range(3):
        x = rnd.randint(40, W - 40)
        d.line((x, 0, x + rnd.randint(-30, 30), W), fill=(110, 108, 104), width=10)
    im.save(path)


def door(path, W=512, H=1024):
    """One door leaf (14 m x 17 m mapped 0..1): ribbed panels, hazard stripes along the bottom edge."""
    im = Image.new('RGB', (W, H), (118, 124, 118)); d = ImageDraw.Draw(im)
    for x in range(0, W, 12):
        d.line((x, 0, x, H), fill=(100, 106, 100), width=3)
    for y in range(0, H, H // 6):
        d.rectangle((0, y, W, y + 6), fill=(80, 84, 80))
    sh = 70
    for k in range(-H, W + H, 60):
        d.polygon([(k, H - sh), (k + 30, H - sh), (k + 30 + sh, H), (k + sh, H)], fill=(230, 180, 20))
    d.rectangle((0, H - sh - 6, W, H - sh), fill=(30, 30, 30))
    d.rectangle((0, H - sh, W, H), outline=(30, 30, 30), width=4)
    noise(im, 0.2).save(path)


def roof(path, W=512):
    corrugated(path, (88, 92, 96), rib=12, W=W)


def beam(path, W=256):
    im = Image.new('RGB', (W, W), (70, 78, 86)); d = ImageDraw.Draw(im)
    for y in range(0, W, 64):
        d.ellipse((W // 2 - 6, y + 26, W // 2 + 6, y + 38), fill=(50, 56, 62))   # bolt rows
    noise(im, 0.15).save(path)


def lamp(path, W=128):
    im = Image.new('RGB', (W, W), (255, 250, 235)); d = ImageDraw.Draw(im)
    d.rectangle((0, 0, W - 1, W - 1), outline=(60, 60, 60), width=8)
    im.save(path)


def marking(path, W=1024, H=512):
    """Floor marking decal for the apron: yellow lead-in line and the hangar number."""
    im = Image.new('RGBA', (W, H), (0, 0, 0, 0)); d = ImageDraw.Draw(im)
    d.rectangle((W // 2 - 10, 0, W // 2 + 10, H), fill=(230, 190, 30, 230))
    d.text((W // 2 + 40, H // 2 - 80), 'H1', fill=(235, 235, 235, 220), font=font(160))
    im.save(path)


Image.new('RGB', (16, 16), (225, 180, 30)).save(os.path.join(OUT, 'hangar_paint.png'))   # taxi line yellow
corrugated(os.path.join(OUT, 'hangar_wall.png'), (150, 156, 150))
roof(os.path.join(OUT, 'hangar_roof.png'))
concrete(os.path.join(OUT, 'hangar_floor.png'))
door(os.path.join(OUT, 'hangar_door.png'))
beam(os.path.join(OUT, 'hangar_beam.png'))
lamp(os.path.join(OUT, 'hangar_lamp.png'))
marking(os.path.join(OUT, 'hangar_marking.png'))
print('hangar textures ->', os.path.abspath(OUT))
