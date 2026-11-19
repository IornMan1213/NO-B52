"""
The B-52J flight deck's panel art: one atlas for every switch panel in the cockpit and the main instrument panel's face,
one for every round instrument face, and a normal map for the panels (original art, drawn with PIL).

Run:  python3 tools/make_cockpit_atlas.py   ->  blender/tex/cp_atlas.png, cp_atlas_n.png, cp_dials.png, cp_controls.json

Panels are laid out the way aircraft consoles are: rows of sub-panels on Dzus rails (a quarter-turn fastener in each
corner), each with its name engraved along the top and its controls in a row: toggle switches (some under red or yellow
guards), rotary selectors with their positions marked, push-button lights, indicator lenses and circuit breakers, every
one labelled, at real size (titles about 8 mm high, labels 5 mm). The height map behind the normal map raises the
sub-panels off the console, sinks the gaps between them and raises each fastener, nut and knob skirt, so the panels
catch the light.

Everything is drawn in "base" units of 1/640 m and scaled up as it is drawn (to about 1400 pixels per metre), so a
region's layout is in real sizes whatever the atlas resolution. Regions are packed into the atlas automatically.

cp_controls.json lists the atlas regions (where each one is and its size in metres) and every toggle, guard, knob,
fire handle and lever drawn (region, position in it, size), so tools/blender_cockpit.py can lay each region on its
surface and stand a real lever, guard or knob on each control.
"""
import json, math, os, random
import numpy as np
from PIL import Image, ImageDraw, ImageFont, ImageFilter

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, '..', 'blender', 'tex')
SIZE = 4096
BASE = 640                                # layout units per metre
K = 2.2                                   # atlas pixels per layout unit: 1408 px per metre
FS = 0.42                                 # lettering at real size: a font of n layout units is drawn n x FS high

# region: (width m, height m, density): its surface in blender_cockpit.py; density 1 = K, the main panel's face at half
REGIONS = {
    'console_L': (0.27, 1.38, 1), 'console_R': (0.27, 1.38, 1), 'overhead': (0.78, 0.62, 1), 'centre': (0.38, 0.62, 1),
    'ped_front': (0.38, 0.22, 1), 'ped_L': (0.40, 0.20, 1), 'ped_R': (0.40, 0.20, 1),
    'wall_L': (1.00, 0.24, 1), 'wall_R': (1.00, 0.24, 1),
    'aft_L': (0.95, 0.25, 1), 'aft_R': (0.95, 0.25, 1),
    'low_L': (0.70, 0.10, 1), 'low_R': (0.70, 0.10, 1), 'shelf': (0.50, 0.12, 1),
    'main': (1.86, 0.59, 0.8),
}

NAMES = ['HYD', 'ELEC', 'FUEL', 'ENG START', 'BLEED AIR', 'ANTI-ICE', 'LIGHTS', 'OXYGEN', 'RADIO', 'IFF', 'INTERCOM',
         'NAV', 'TACAN', 'ILS', 'AUTOPILOT', 'TRIM', 'PRESS', 'TEMP', 'GEN', 'BUS TIE', 'AFT BAY', 'FWD BAY', 'REFUEL',
         'BOOST PUMP', 'X-FEED', 'WIPER', 'PITOT HTR', 'APU', 'CHUTE', 'GEAR', 'FLAPS', 'BRAKES', 'STEER', 'WARN TEST',
         'EMER', 'DEFOG', 'CABIN', 'SEAT', 'MASTER ARM', 'WEAPONS', 'BAY DOORS', 'CMDS', 'ECM', 'ALQ-172', 'ARC-210']
POS2 = [('ON', 'OFF'), ('AUTO', 'MAN'), ('NORM', 'ALT'), ('OPEN', 'CLOSE'), ('ARM', 'SAFE'), ('HI', 'LO'), ('START', 'OFF'),
        ('TEST', 'NORM'), ('BRT', 'DIM'), ('L', 'R'), ('FWD', 'AFT'), ('EXT', 'RET')]
KNOB = [('OFF', 'LO', 'MED', 'HI'), ('OFF', '1', '2', '3', '4'), ('NORM', 'TEST', 'RESET'), ('MAN', 'AUTO', 'STBY'),
        ('L', 'BOTH', 'R'), ('VOL',), ('BRT',), ('SEL', 'TR', 'T/R+G')]

CONSOLE = (52, 55, 58)            # the console under the panels, between them
PANEL = (72, 76, 80)               # sub-panel face: medium grey
MAINP = (64, 67, 71)               # the main instrument panel
EDGE = (40, 42, 45)
TEXT = (232, 232, 226)             # engraved white
NUT = (176, 176, 170)
BLACK = (18, 18, 19)
GUARD_R = (176, 34, 30)
GUARD_Y = (214, 172, 30)
LENS = {'G': (40, 150, 70), 'A': (210, 140, 30), 'R': (190, 40, 30), 'W': (210, 210, 200), 'B': (60, 110, 190)}


def load_font(px, bold):
    for f in (('DejaVuSansCondensed-Bold.ttf' if bold else 'DejaVuSansCondensed.ttf'), 'DejaVuSans.ttf'):
        for d in ('/usr/share/fonts/truetype/dejavu/', ''):
            try:
                return ImageFont.truetype(d + f, max(6, int(round(px))))
            except OSError:
                pass
    return ImageFont.load_default()


class Draw:
    """ImageDraw in layout units: coordinates and line widths scaled by k as they are drawn."""
    def __init__(self, im, k):
        self.d = ImageDraw.Draw(im); self.k = k; self.fonts = {}

    def _c(self, xy): return [v * self.k for v in xy]
    def _w(self, kw):
        if 'width' in kw: kw = dict(kw, width=max(1, int(round(kw['width'] * self.k))))
        return kw
    def ellipse(self, xy, **kw): self.d.ellipse(self._c(xy), **self._w(kw))
    def rectangle(self, xy, **kw): self.d.rectangle(self._c(xy), **self._w(kw))
    def line(self, xy, **kw): self.d.line(self._c(xy), **self._w(kw))
    def font(self, size, bold=False):
        key = (size, bold)
        if key not in self.fonts: self.fonts[key] = load_font(size * self.k * FS * 2.0, bold)
        return self.fonts[key]
    def text_c(self, x, y, s, size, fill=TEXT, bold=False):
        f = self.font(size, bold)
        w = self.d.textlength(s, font=f)
        self.d.text((x * self.k - w / 2, y * self.k), s, font=f, fill=fill)


controls = []
rng = random.Random(52)
D = H = None                       # the region being drawn: colour and height
CUR = None                         # (name, width, height) in layout units


def ctl(kind, x, y, **kw):
    controls.append(dict(region=CUR[0], type=kind, u=x / CUR[1], v=y / CUR[2], **kw))


def dzus(x, y):
    D.ellipse((x - 4, y - 4, x + 4, y + 4), fill=(96, 98, 100), outline=EDGE)
    D.line((x - 3, y - 2, x + 3, y + 2), fill=EDGE, width=1.5)
    H.ellipse((x - 4, y - 4, x + 4, y + 4), fill=170)


def toggle(x, y, labels, guard=None):
    if guard:
        D.rectangle((x - 11, y - 20, x + 11, y + 20), fill=guard, outline=EDGE)
        H.rectangle((x - 11, y - 20, x + 11, y + 20), fill=190)
    D.ellipse((x - 8, y - 8, x + 8, y + 8), fill=NUT, outline=EDGE)
    D.ellipse((x - 4, y - 4, x + 4, y + 4), fill=(60, 60, 60))
    H.ellipse((x - 8, y - 8, x + 8, y + 8), fill=200)
    D.text_c(x, y - (32 if guard else 26), labels[0], 8)
    D.text_c(x, y + (23 if guard else 14), labels[1], 8)
    ctl('guard' if guard else 'toggle', x, y, colour='red' if guard == GUARD_R else 'yellow' if guard else None, up=rng.random() < 0.6)


def knob(x, y, labels):
    r = 12
    D.ellipse((x - r - 4, y - r - 4, x + r + 4, y + r + 4), fill=(46, 48, 50))
    for i, s in enumerate(labels):
        a = math.radians(-135 + 270 * (i / max(len(labels) - 1, 1))) if len(labels) > 1 else math.radians(-90)
        D.line((x + math.sin(a) * (r + 3), y - math.cos(a) * (r + 3), x + math.sin(a) * (r + 7), y - math.cos(a) * (r + 7)), fill=TEXT, width=1)
        D.text_c(x + math.sin(a) * (r + 14), y - math.cos(a) * (r + 14) - 4, s, 8)
    D.ellipse((x - r, y - r, x + r, y + r), fill=BLACK)
    H.ellipse((x - r - 4, y - r - 4, x + r + 4, y + r + 4), fill=165); H.ellipse((x - r, y - r, x + r, y + r), fill=230)
    ctl('knob', x, y, r=r / BASE)


def lamp(x, y, colour, legend=None, w=26, hh=16):
    c = LENS[colour]
    D.rectangle((x - w / 2 - 2, y - hh / 2 - 2, x + w / 2 + 2, y + hh / 2 + 2), fill=BLACK)
    D.rectangle((x - w / 2, y - hh / 2, x + w / 2, y + hh / 2), fill=tuple(int(v * 0.55) for v in c))
    if legend: D.text_c(x, y - 4, legend, 8, fill=tuple(min(255, int(v * 1.3)) for v in c), bold=True)
    H.rectangle((x - w / 2 - 2, y - hh / 2 - 2, x + w / 2 + 2, y + hh / 2 + 2), fill=185)


def breaker(x, y, label):
    D.ellipse((x - 6, y - 6, x + 6, y + 6), fill=BLACK)
    D.ellipse((x - 3.5, y - 3.5, x + 3.5, y + 3.5), fill=(30, 30, 30), outline=(205, 205, 200), width=1)
    D.text_c(x, y + 8, label, 7)
    H.ellipse((x - 6, y - 6, x + 6, y + 6), fill=210)


def subpanel(x0, y0, x1, y1, kind):
    D.rectangle((x0, y0, x1, y1), fill=PANEL, outline=EDGE, width=2)
    H.rectangle((x0, y0, x1, y1), fill=140, outline=120, width=2)
    for cx, cy in ((x0 + 7, y0 + 7), (x1 - 7, y0 + 7), (x0 + 7, y1 - 7), (x1 - 7, y1 - 7)): dzus(cx, cy)
    D.text_c((x0 + x1) / 2, y0 + 4, rng.choice(NAMES), 11, bold=True)
    D.line((x0 + 16, y0 + 16, x1 - 16, y0 + 16), fill=(110, 112, 114), width=1)
    w, hh = x1 - x0, y1 - y0
    ix0, iy0, ix1, iy1 = x0 + 14, y0 + 20, x1 - 14, y1 - 8
    if kind == 'breakers':
        cols = max(1, int((ix1 - ix0) / 26)); rows = max(1, int((iy1 - iy0) / 26))
        for r in range(rows):
            for c in range(cols):
                breaker(ix0 + 13 + c * ((ix1 - ix0 - 26) / max(cols - 1, 1)), iy0 + 9 + r * ((iy1 - iy0 - 24) / max(rows - 1, 1)),
                        rng.choice(['PWR', 'CTL', 'IND', 'HTR', 'PUMP', 'VLV', 'LT', 'XFR']) + str(rng.randint(1, 8)))
        return
    n = max(1, int(w / 52))
    rows = max(1, int((hh - 24) / 62))
    for r in range(rows):
        cy = iy0 + (iy1 - iy0) * (r + 0.5) / rows + 3
        for i in range(n):
            cx = ix0 + (ix1 - ix0) * (i + 0.5) / n
            t = rng.random()
            if t < 0.45: toggle(cx, cy, rng.choice(POS2))
            elif t < 0.55 and hh / rows > 58: toggle(cx, cy, rng.choice(POS2), guard=GUARD_R if rng.random() < 0.6 else GUARD_Y)
            elif t < 0.75 and hh / rows > 58: knob(cx, cy, rng.choice(KNOB))
            else: lamp(cx, cy, rng.choice('GAAWRB'), rng.choice(['ON', 'FAIL', 'LOW', 'OPEN', 'XFR', 'HOT', 'OK']))


def rows_layout(kinds=('ctl',)):
    def lay(W, Hh):
        yy = 3
        while yy < Hh - 60:
            ph = min(rng.choice((80, 92, 106, 124, 150)), Hh - 3 - yy)
            if ph < 60: break
            subpanel(3, yy, W - 4, yy + ph - 2, rng.choice(kinds)); yy += ph
    return lay


def grid_layout(cols, kinds=('ctl',)):
    def lay(W, Hh):
        cw = W / cols
        for c in range(cols):
            x0, x1 = c * cw + 2, (c + 1) * cw - 2
            if rng.random() < 0.5 and Hh > 180:
                m = Hh / 2
                subpanel(x0, 2, x1, m - 1, rng.choice(kinds)); subpanel(x0, m + 1, x1, Hh - 3, rng.choice(kinds))
            else:
                subpanel(x0, 2, x1, Hh - 3, rng.choice(kinds))
    return lay


def strip_layout(W, Hh):
    n = max(1, int(W / 150))
    for c in range(n): subpanel(c * W / n + 2, 2, (c + 1) * W / n - 2, Hh - 3, 'ctl')


# The main panel's face, in panel coordinates (metres): x right, v up along the face (it leans back 12 deg); the face
# spans x -0.93..0.93, v -0.30..0.29. INSTR is the one list of what is mounted in it: tools/blender_cockpit.py builds
# each display and instrument where it is listed here (cp_controls.json 'instruments'), and the art below draws the
# cut-out behind it and its name.
MX0, MX1, MZ0, MZ1 = -0.93, 0.93, -0.30, 0.29
INSTR = []
for sx, live in ((-1, True), (1, False)):
    side = 'L' if sx < 0 else 'R'
    # displays: primary flight display in front of each pilot, the tactical map inboard of it
    INSTR.append(dict(kind='screen', name='mfd_L1_screen' if live else 'mfd_R1_screen', mat='pfd', x=sx * 0.515, v=0.07, w=0.19, h=0.2375))
    INSTR.append(dict(kind='screen', name='mfd_C_screen' if live else 'mfd_R2_screen', mat='tsd', x=sx * 0.260, v=0.07, w=0.20, h=0.20))
    # standby instruments outboard, in a T: airspeed, attitude / vertical speed, altitude; then hydraulics and fuel
    for name, x, v, r in (('asi', 0.818, 0.150, 0.042), ('adi', 0.700, 0.150, 0.048), ('vsi', 0.818, 0.035, 0.042),
                          ('alt', 0.700, 0.035, 0.042)):
        INSTR.append(dict(kind='gauge', name=name, x=sx * x, v=v, r=r, live=live))
    for name, x in ((('FUEL QTY' if live else 'CABIN ALT'), 0.818), (('HYD' if live else 'OXYGEN'), 0.700)):
        INSTR.append(dict(kind='dial', name=name, x=sx * x, v=-0.075, r=0.028))
INSTR.append(dict(kind='screen', name='mfd_E_screen', mat='eng', x=0.0, v=0.07, w=0.19, h=0.2375))   # engines (EICAS)
INSTR.append(dict(kind='dial', name='CLOCK', x=0.30, v=-0.215, r=0.034))
INSTR.append(dict(kind='ann', x0=-0.64, x1=-0.27, v=0.247, h=0.028))                                    # annunciator strips
INSTR.append(dict(kind='ann', x0=0.27, x1=0.64, v=0.247, h=0.028))


def main_layout(W, Hh):
    def P(x, z): return ((x - MX0) / (MX1 - MX0) * W, (MZ1 - z) / (MZ1 - MZ0) * Hh)
    def M_(m): return m * BASE                                    # metres to layout units
    D.rectangle((0, 0, W, Hh), fill=MAINP)
    H.rectangle((0, 0, W, Hh), fill=140)
    # the three panels it is made of, on their fasteners
    for a, b in ((MX0, -0.122), (-0.122, 0.122), (0.122, MX1)):
        x0, _ = P(a, 0); x1, _ = P(b, 0)
        D.rectangle((x0 + 2, 2, x1 - 2, Hh - 2), outline=EDGE, width=2); H.rectangle((x0 + 2, 2, x1 - 2, Hh - 2), outline=115, width=2)
        for z in (MZ1 - 0.015, MZ0 + 0.015):
            for x in (a + 0.015, (a + b) / 2, b - 0.015):
                px_, pz = P(x, z); dzus(px_, pz)
    for it in INSTR:
        if it['kind'] == 'screen':
            x0, y0 = P(it['x'] - it['w'] / 2 - 0.022, it['v'] + it['h'] / 2 + 0.022)
            x1, y1 = P(it['x'] + it['w'] / 2 + 0.022, it['v'] - it['h'] / 2 - 0.022)
            nm = {'pfd': 'PFD', 'eng': 'ENGINES', 'tsd': 'TSD' if it['x'] < 0 else 'MFD'}[it['mat']]
            D.text_c((x0 + x1) / 2, y1 + 4, nm, 10, bold=True)
            D.rectangle((x0, y0, x1, y1), fill=BLACK); H.rectangle((x0, y0, x1, y1), fill=90)
        elif it['kind'] in ('gauge', 'dial'):
            cx, cy = P(it['x'], it['v']); rr = M_(it['r'] + 0.013)
            D.ellipse((cx - rr, cy - rr, cx + rr, cy + rr), fill=BLACK); H.ellipse((cx - rr, cy - rr, cx + rr, cy + rr), fill=90)
        elif it['kind'] == 'ann':
            x0, y0 = P(it['x0'] - 0.006, it['v'] + it['h'] / 2 + 0.006); x1, y1 = P(it['x1'] + 0.006, it['v'] - it['h'] / 2 - 0.006)
            D.rectangle((x0, y0, x1, y1), fill=BLACK); H.rectangle((x0, y0, x1, y1), fill=185)
    for sx in (-1, 1):
        # master warning and caution over the standby instruments, the display names under the displays
        x, z = P(sx * 0.76, 0.247); lamp(x - 26, z, 'R', 'WARN', w=44, hh=20); lamp(x + 26, z, 'A', 'CAUTN', w=44, hh=20)
        # the knee panels: switch panels across under the displays
        xs = (0.93, 0.66, 0.44) if sx < 0 else (0.93, 0.66, 0.40)
        for a, b in ((xs[0], xs[1]), (xs[1] - 0.005, xs[2])):
            x0, y0 = P(sx * a if sx < 0 else sx * b, -0.145); x1, y1 = P(sx * b if sx < 0 else sx * a, -0.295)
            subpanel(x0 + 2, y0, x1 - 2, y1, 'ctl')
    # engine fire handles across the top of the centre panel: pull to shut the engine down and fire the bottle
    x0, y0 = P(-0.245, 0.285); x1, y1 = P(0.245, 0.212)
    D.rectangle((x0, y0, x1, y1), fill=BLACK); H.rectangle((x0, y0, x1, y1), fill=180)
    D.text_c((x0 + x1) / 2, y0 + 2, 'ENGINE FIRE - PULL', 9, fill=(225, 70, 55), bold=True)
    for i in range(8):
        cx = x0 + (x1 - x0) * (i + 0.5) / 8
        D.text_c(cx, y1 - 13, str(i + 1), 10, bold=True)
        ctl('fire', cx, y0 + (y1 - y0) * 0.5)
    # the landing gear lever (modelled: a wheel-shaped knob on a lever) left of the pedestal, on the pilot's side
    gx, gz = P(-0.30, -0.215)
    D.rectangle((gx - 26, gz - 46, gx + 26, gz + 46), fill=(50, 52, 55), outline=EDGE, width=2)
    D.text_c(gx, gz - 62, 'GEAR', 12, bold=True); D.text_c(gx + 40, gz - 44, 'UP', 10); D.text_c(gx + 40, gz + 32, 'DN', 10)
    for i in range(3): lamp(gx - 46, gz - 28 + i * 28, 'G', None, w=14, hh=14)
    H.rectangle((gx - 26, gz - 46, gx + 26, gz + 46), fill=125)
    ctl('gear', gx, gz)


# ------------------------------------------------------------------ draw every region, pack them, write the atlas
LAYOUTS = {
    'console_L': rows_layout(('ctl', 'ctl', 'breakers')), 'console_R': rows_layout(('ctl', 'ctl', 'breakers')),
    'overhead': grid_layout(4), 'centre': rows_layout(),
    'ped_front': grid_layout(2), 'ped_L': grid_layout(2), 'ped_R': grid_layout(2),
    'wall_L': grid_layout(5, ('breakers', 'breakers', 'ctl')), 'wall_R': grid_layout(5, ('breakers', 'breakers', 'ctl')),
    'aft_L': grid_layout(4), 'aft_R': grid_layout(4),
    'low_L': strip_layout, 'low_R': strip_layout, 'shelf': grid_layout(2), 'main': main_layout,
}
tiles = {}
for name, (wm, hm, dens) in REGIONS.items():
    W, Hh = wm * BASE, hm * BASE
    k = K * dens
    im = Image.new('RGB', (int(W * k), int(Hh * k)), CONSOLE); hm_ = Image.new('L', im.size, 100)
    D, H = Draw(im, k), Draw(hm_, k)
    CUR = (name, W, Hh)
    LAYOUTS[name](W, Hh)
    tiles[name] = (im, hm_)

# shelf packing, tallest first
atlas = Image.new('RGB', (SIZE, SIZE), CONSOLE); height = Image.new('L', (SIZE, SIZE), 100)
placed = {}
x = y = row_h = 0
for name in sorted(tiles, key=lambda n: -tiles[n][0].size[1]):
    im, hm_ = tiles[name]
    w, hh = im.size
    if x + w > SIZE: x, y, row_h = 0, y + row_h + 4, 0
    if y + hh > SIZE: raise SystemExit(f'atlas full at {name}')
    atlas.paste(im, (x, y)); height.paste(hm_, (x, y))
    wm, hm, _ = REGIONS[name]
    placed[name] = [x, y, w, hh, wm, hm]
    x += w + 4; row_h = max(row_h, hh)

grain = np.asarray(Image.effect_noise((SIZE, SIZE), 9).convert('L'), dtype=np.float32)
a = np.asarray(atlas, dtype=np.float32) * (1 - np.clip(grain - 128, 0, None)[..., None] / 1500.0)
Image.fromarray(a.clip(0, 255).astype(np.uint8)).save(os.path.join(OUT, 'cp_atlas.png'))

hgt = np.asarray(height.filter(ImageFilter.GaussianBlur(1.2)), dtype=np.float32)
gx = np.zeros_like(hgt); gy = np.zeros_like(hgt)
gx[:, 1:-1] = hgt[:, :-2] - hgt[:, 2:]
gy[1:-1, :] = hgt[2:, :] - hgt[:-2, :]
s = 2.0 / 255 * 5
nx, ny, nz = gx * s, gy * s, np.ones_like(hgt)
l = np.sqrt(nx * nx + ny * ny + nz * nz)
nm = np.stack([nx / l, ny / l, nz / l], axis=-1) * 0.5 + 0.5
Image.fromarray((nm * 255).astype(np.uint8)).save(os.path.join(OUT, 'cp_atlas_n.png'))

# ------------------------------------------------------------------ instrument faces: 8 x 8 grid of 256 px dials
DIALS = []
dial = Image.new('RGB', (2048, 2048), (10, 10, 10)); dd = ImageDraw.Draw(dial)
F_D, F_DS, F_DT = load_font(19, True), load_font(19, False), load_font(13, False)


def face(slot, title, lo, hi, major, minor, unit='', arc=(-135, 135), labels=None, bands=()):
    cx = (slot % 8) * 256 + 128; cy = (slot // 8) * 256 + 128
    R = 118
    dd.ellipse((cx - R, cy - R, cx + R, cy + R), fill=(14, 14, 15))
    a0, a1 = arc
    def ang(v): return math.radians(a0 + (a1 - a0) * (v - lo) / (hi - lo))
    for b0, b1, col in bands:
        for v in [b0 + (b1 - b0) * k / 40 for k in range(41)]:
            a = ang(v)
            dd.ellipse((cx + math.sin(a) * (R - 10) - 4, cy - math.cos(a) * (R - 10) - 4,
                        cx + math.sin(a) * (R - 10) + 4, cy - math.cos(a) * (R - 10) + 4), fill=col)
    n = int(round((hi - lo) / minor))
    for k in range(n + 1):
        v = lo + k * minor; a = ang(v)
        big = abs((v - lo) / major - round((v - lo) / major)) < 1e-6
        r0 = R - (24 if big else 14)
        dd.line((cx + math.sin(a) * r0, cy - math.cos(a) * r0, cx + math.sin(a) * (R - 4), cy - math.cos(a) * (R - 4)),
                fill=(235, 235, 230), width=4 if big else 2)
        if big:
            s_ = labels[int(round((v - lo) / major))] if labels else (f'{v:g}')
            tx, ty = cx + math.sin(a) * (R - 44), cy - math.cos(a) * (R - 44)
            w = dd.textlength(s_, font=F_DS); dd.text((tx - w / 2, ty - 11), s_, font=F_DS, fill=(235, 235, 230))
    w = dd.textlength(title, font=F_D); dd.text((cx - w / 2, cy + 22), title, font=F_D, fill=(235, 235, 230))
    if unit:
        w = dd.textlength(unit, font=F_DT); dd.text((cx - w / 2, cy + 44), unit, font=F_DT, fill=(190, 190, 185))
    dd.ellipse((cx - 9, cy - 9, cx + 9, cy + 9), fill=(40, 40, 40))
    DIALS.append({'slot': slot, 'name': title, 'uv': [(slot % 8) / 8, 1 - (slot // 8 + 1) / 8, (slot % 8 + 1) / 8, 1 - (slot // 8) / 8]})


G, A, Rd = (40, 170, 70), (220, 160, 30), (200, 40, 30)
face(0, 'EPR', 1.0, 2.4, 0.2, 0.05, '', bands=((1.2, 1.9, G),))
face(1, '%RPM', 0, 110, 10, 2, 'N1', bands=((60, 100, G), (100, 105, A)))
face(2, 'EGT', 0, 1000, 100, 20, 'x C', bands=((300, 800, G), (800, 900, A)))
face(3, '%RPM', 0, 110, 10, 2, 'N2', bands=((60, 100, G),))
face(4, 'FUEL FLOW', 0, 10, 1, 0.5, 'PPH x1000', bands=((1, 8, G),))
face(5, 'OIL', 0, 100, 20, 5, 'PSI', bands=((30, 80, G),))
face(6, 'HYD', 0, 4000, 1000, 200, 'PSI', labels=['0', '1', '2', '3', '4'], bands=((2800, 3200, G),))
face(7, 'FUEL QTY', 0, 50, 10, 2, 'LB x1000')
face(8, 'FLAPS', 0, 100, 25, 5, '%', arc=(-90, 90))
face(9, 'CABIN ALT', 0, 50, 10, 2, 'FT x1000')
face(10, 'OXYGEN', 0, 500, 100, 20, 'PSI', bands=((300, 450, G),))
face(11, 'AC VOLTS', 0, 150, 25, 5, '', bands=((110, 120, G),))
face(12, 'LOAD', 0, 1.5, 0.25, 0.05, '', labels=['0', '.25', '.5', '.75', '1', '1.25', '1.5'], bands=((0, 1, G),))
face(13, 'G', -2, 4, 1, 0.25, '', bands=((-1, 2.5, G),))
face(14, 'AOA', 0, 30, 5, 1, 'UNITS', arc=(-120, 60), bands=((10, 18, G),))
face(15, 'CLOCK', 0, 12, 1, 0.2, '', arc=(0, 360), labels=['12', '1', '2', '3', '4', '5', '6', '7', '8', '9', '10', '11', '12'])
dial.save(os.path.join(OUT, 'cp_dials.png'))

json.dump({'size': SIZE, 'regions': placed, 'main_panel': [MX0, MX1, MZ0, MZ1], 'instruments': INSTR, 'controls': controls, 'dials': DIALS},
          open(os.path.join(OUT, 'cp_controls.json'), 'w'), indent=1)
print('ATLAS', len(controls), 'controls,', len(DIALS), 'dial faces; used', y + row_h, 'of', SIZE, 'rows')

# ------------------------------------------------------------------ window frames from inside: painted metal, riveted
# One 0.5 m tile (blender_cockpit.py maps it at real size): two panel seams, rows of rivets along them, a little wear.
T = 512
fr = Image.new('RGB', (T, T), (66, 69, 72)); fd = ImageDraw.Draw(fr)
frh = Image.new('L', (T, T), 128); fh = ImageDraw.Draw(frh)
for y0 in (0, T // 2):
    fd.line((0, y0, T, y0), fill=(44, 46, 48), width=3); fh.line((0, y0, T, y0), fill=90, width=3)
    for yy in (y0 + 14, y0 - 14):
        for xx in range(8, T, 24):
            fd.ellipse((xx - 3, yy % T - 3, xx + 3, yy % T + 3), fill=(86, 89, 92)); fh.ellipse((xx - 3, yy % T - 3, xx + 3, yy % T + 3), fill=175)
for x0 in (0, T // 2):
    fd.line((x0, 0, x0, T), fill=(48, 50, 52), width=2); fh.line((x0, 0, x0, T), fill=100, width=2)
noise = np.asarray(Image.effect_noise((T, T), 14).convert('L'), dtype=np.float32)
fa = np.asarray(fr, dtype=np.float32) * (0.93 + noise[..., None] / 255 * 0.12)
Image.fromarray(fa.clip(0, 255).astype(np.uint8)).save(os.path.join(OUT, 'cp_frame.png'))
print('FRAME tile')

# ------------------------------------------------------------------ worn paint for the consoles and panel bodies, and the
# glareshield's black crinkle finish: 0.5 m tiles at real size (blender_cockpit.py maps them like the walls)
rs = np.random.RandomState(7)
def worn(base, seed, chips=True):
    n1 = np.asarray(Image.effect_noise((T, T), 40).filter(ImageFilter.GaussianBlur(6)).convert('L'), dtype=np.float32) / 255
    n2 = np.asarray(Image.effect_noise((T, T), 25).convert('L'), dtype=np.float32) / 255
    a = np.ones((T, T, 3), np.float32) * np.array(base, np.float32)
    a *= (0.90 + 0.14 * n1)[..., None] * (0.97 + 0.06 * n2)[..., None]       # uneven paint
    im = Image.fromarray(a.clip(0, 255).astype(np.uint8)); dr = ImageDraw.Draw(im)
    r = random.Random(seed)
    for _ in range(140):                                                         # fine scratches, a lighter grey
        x, y = r.uniform(0, T), r.uniform(0, T); ang = r.uniform(0, math.pi); L = r.uniform(4, 30)
        c = tuple(min(255, int(v * 1.35)) for v in base)
        dr.line((x, y, x + math.cos(ang) * L, y + math.sin(ang) * L), fill=c, width=1)
    if chips:
        for _ in range(26):                                                      # chipped paint, bare metal under it
            x, y = r.uniform(0, T), r.uniform(0, T); rr = r.uniform(1.5, 5)
            dr.ellipse((x - rr, y - rr * r.uniform(0.5, 1), x + rr, y + rr), fill=(150, 152, 150))
    return im
worn((96, 100, 104), 3).save(os.path.join(OUT, 'cp_paint.png'))
worn((24, 24, 26), 5, chips=False).save(os.path.join(OUT, 'cp_glare.png'))
print('PAINT tiles')
