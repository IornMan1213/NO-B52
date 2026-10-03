"""Draws the B-52J cockpit display and panel textures (original art, PIL only).
Run: python tools/make_cockpit_textures.py  ->  blender/tex/*.png"""
import math, os, random
from PIL import Image, ImageDraw, ImageFont

OUT = os.path.join(os.path.dirname(__file__), '..', 'blender', 'tex')
os.makedirs(OUT, exist_ok=True)
random.seed(52)


def font(sz):
    for f in ('consola.ttf', 'cour.ttf', 'arial.ttf'):
        try:
            return ImageFont.truetype(f, sz)
        except OSError:
            pass
    return ImageFont.load_default()


GREEN, CYAN, WHITE, AMBER, MAG = (60, 255, 90), (70, 220, 255), (235, 235, 235), (255, 180, 40), (255, 80, 255)


def pfd(path):
    """Primary flight display: attitude, speed tape, altitude tape, heading."""
    W, H = 512, 640
    im = Image.new('RGB', (W, H), (0, 0, 0)); d = ImageDraw.Draw(im)
    cx, cy = W // 2, 280
    d.rectangle((90, 80, 420, 480), fill=(30, 110, 220))
    d.polygon([(90, 300), (420, 260), (420, 480), (90, 480)], fill=(120, 75, 30))
    d.line((90, 300, 420, 260), fill=WHITE, width=3)
    for p in range(-20, 25, 5):
        if p == 0: continue
        y = cy - p * 7; w = 60 if p % 10 == 0 else 30
        d.line((cx - w, y + p * 0.6, cx + w, y - p * 0.6), fill=WHITE, width=2)
        if p % 10 == 0:
            d.text((cx + w + 6, y - 8), str(abs(p)), fill=WHITE, font=font(16))
    d.line((cx - 110, cy, cx - 40, cy), fill=AMBER, width=6); d.line((cx + 40, cy, cx + 110, cy), fill=AMBER, width=6)
    d.rectangle((cx - 5, cy - 5, cx + 5, cy + 5), outline=AMBER, width=3)
    # tapes
    d.rectangle((10, 80, 85, 480), fill=(25, 25, 25)); d.rectangle((425, 80, 500, 480), fill=(25, 25, 25))
    for i, v in enumerate(range(260, 360, 10)):
        y = 460 - i * 40; d.line((70, y, 85, y), fill=WHITE); d.text((20, y - 9), str(v), fill=WHITE, font=font(18))
    for i, v in enumerate(range(28000, 33000, 500)):
        y = 460 - i * 40; d.line((425, y, 440, y), fill=WHITE); d.text((443, y - 9), str(v)[:-2], fill=WHITE, font=font(16))
    d.rectangle((5, 262, 90, 300), fill=(0, 0, 0), outline=WHITE, width=2); d.text((14, 268), '312', fill=GREEN, font=font(26))
    d.rectangle((420, 262, 508, 300), fill=(0, 0, 0), outline=WHITE, width=2); d.text((426, 270), '31000', fill=GREEN, font=font(20))
    d.text((14, 40), 'M.78', fill=GREEN, font=font(22)); d.text((410, 40), 'BARO', fill=CYAN, font=font(18))
    # heading arc
    d.arc((106, 500, 406, 800), 200, 340, fill=WHITE, width=2)
    for k in range(-6, 7):
        a = math.radians(270 + k * 10); x = cx + 150 * math.cos(a); y = 650 + 150 * math.sin(a)
        d.line((x, y, cx + 140 * math.cos(a), 650 + 140 * math.sin(a)), fill=WHITE, width=2)
    d.text((cx - 20, 510), '045', fill=GREEN, font=font(24))
    d.text((10, 600), 'AP  ALT HOLD   NAV', fill=GREEN, font=font(18))
    im.save(path)


def engine_page(path):
    """EICAS-style page for eight F130s: N1 dials, EGT bars, fuel flow."""
    W, H = 512, 640
    im = Image.new('RGB', (W, H), (0, 0, 0)); d = ImageDraw.Draw(im)
    d.text((150, 8), 'ENGINES  F130-RR-100', fill=CYAN, font=font(20))
    for i in range(8):
        col, row = i % 4, i // 4
        cx, cy = 64 + col * 128, 110 + row * 150
        n1 = 88 + random.random() * 4
        d.arc((cx - 50, cy - 50, cx + 50, cy + 50), 135, 405, fill=WHITE, width=3)
        a = math.radians(135 + 270 * n1 / 110)
        d.line((cx, cy, cx + 44 * math.cos(a), cy + 44 * math.sin(a)), fill=GREEN, width=4)
        d.text((cx - 22, cy + 18), f'{n1:4.1f}', fill=GREEN, font=font(18))
        d.text((cx - 6, cy - 72), str(i + 1), fill=WHITE, font=font(18))
    d.text((10, 400), 'EGT', fill=WHITE, font=font(18)); d.text((10, 470), 'FF', fill=WHITE, font=font(18))
    for i in range(8):
        x = 70 + i * 54; h = 50 + random.randint(0, 8)
        d.rectangle((x, 455 - h, x + 30, 455), fill=GREEN); d.rectangle((x, 395, x + 30, 455), outline=WHITE)
        d.text((x - 4, 470), f'{2.9 + random.random() * 0.3:.1f}', fill=GREEN, font=font(16))
    d.text((10, 520), 'FUEL  TOTAL  112.4 K LB', fill=WHITE, font=font(20))
    d.text((10, 552), 'GW 401.8 K LB   CG 24.1%', fill=WHITE, font=font(20))
    d.text((10, 600), 'OIL  NORM    HYD  NORM', fill=GREEN, font=font(18))
    im.save(path)


def tsd(path):
    """Tactical situation display placeholder (the game's live tac screen replaces it on the centre MFD)."""
    W, H = 640, 640
    im = Image.new('RGB', (W, H), (4, 14, 10)); d = ImageDraw.Draw(im)
    for r in (100, 200, 300):
        d.ellipse((320 - r, 360 - r, 320 + r, 360 + r), outline=(30, 90, 60), width=2)
    for k in range(25):
        x, y = random.randint(40, 600), random.randint(60, 600)
        d.line((x, y, x + random.randint(-80, 80), y + random.randint(-80, 80)), fill=(40, 70, 50), width=2)
    d.polygon([(320, 340), (308, 380), (332, 380)], fill=WHITE)
    d.line((320, 340, 470, 140), fill=MAG, width=3); d.ellipse((462, 132, 478, 148), outline=MAG, width=3)
    d.text((10, 10), 'TSD   RNG 80', fill=GREEN, font=font(22)); d.text((480, 10), 'CONECT', fill=CYAN, font=font(22))
    im.save(path)


def panel(path, W=1024, H=512, seed=1):
    """Grey panel with switch rows, knobs and placards, used on consoles, overhead and the main panel trim."""
    rnd = random.Random(seed)
    im = Image.new('RGB', (W, H), (58, 62, 66)); d = ImageDraw.Draw(im)
    for _ in range(14):
        x0, y0 = rnd.randint(0, W - 200), rnd.randint(0, H - 120)
        x1, y1 = x0 + rnd.randint(120, 260), y0 + rnd.randint(70, 140)
        d.rectangle((x0, y0, x1, y1), fill=(44, 47, 50), outline=(20, 20, 20), width=3)
        for sx in range(x0 + 16, x1 - 10, 28):
            kind = rnd.random()
            sy = y0 + 30
            if kind < 0.5:
                d.rectangle((sx - 4, sy, sx + 4, sy + 22), fill=(200, 200, 200)); d.ellipse((sx - 6, sy + 20, sx + 6, sy + 30), fill=(30, 30, 30))
            elif kind < 0.8:
                d.ellipse((sx - 9, sy, sx + 9, sy + 18), fill=(15, 15, 15), outline=(160, 160, 160))
            else:
                d.rectangle((sx - 9, sy, sx + 9, sy + 14), fill=(rnd.choice([(30, 150, 40), (180, 140, 20), (40, 40, 40)])))
            d.text((sx - 10, sy + 34), rnd.choice(['ON', 'OFF', 'ARM', 'NORM', 'AUTO', 'TEST', 'BAT', 'GEN']), fill=(225, 225, 225), font=font(10))
        d.text((x0 + 6, y0 + 4), rnd.choice(['FUEL', 'ELEC', 'HYD', 'BLEED', 'RADIO', 'IFF', 'LIGHTS', 'ANTI-ICE', 'TRIM', 'START']), fill=(240, 240, 240), font=font(13))
    for x in range(0, W, 128):
        for y in range(0, H, 128):
            d.ellipse((x + 4, y + 4, x + 10, y + 10), fill=(90, 92, 95))   # dzus fasteners
    im.save(path)


def seat_fabric(path):
    im = Image.new('RGB', (256, 256), (52, 58, 40)); d = ImageDraw.Draw(im)
    for y in range(0, 256, 16):
        d.line((0, y, 256, y), fill=(40, 45, 30), width=2)
    im.save(path)


pfd(os.path.join(OUT, 'mfd_pfd.png'))
engine_page(os.path.join(OUT, 'mfd_engines.png'))
tsd(os.path.join(OUT, 'mfd_tsd.png'))
panel(os.path.join(OUT, 'panel_a.png'), seed=1)
panel(os.path.join(OUT, 'panel_b.png'), seed=2)
seat_fabric(os.path.join(OUT, 'seat.png'))
print('textures ->', os.path.abspath(OUT))
