"""Paints the camouflage liveries from the clean atlas and the baked masks (tools/blender_livery_masks.py):
  SEA camouflage over a black belly: the B-52D over Vietnam, the black up the fuselage and fin sides
  SEA camouflage over an anti-flash white belly: the B-52G/H's 1970s nuclear alert scheme
  Boscali and Primeva: one scheme for each of the game's sides
Top: the T.O. 1-1-4 Southeast Asia colours, tan FS 30219, medium green FS 34102 and dark green FS 34079, about a
third each, in patches about 6 m across that run on across parts and texture seams (the masks are sampled in 3D).
Panel lines and shading come from the clean atlas, and so do the markings (lettering, insignia, nose art) on the
SEA schemes; the two side schemes have none.
Run: python tools/make_liveries.py   (after make_clean_livery.py and blender_livery_masks.py)"""
import os, warnings
import numpy as np
from PIL import Image
from atlas_util import ROOT, blur, ramp, masks

TEX = os.path.join(ROOT, 'unity', 'Mods', 'B52', 'Textures')
CLEAN = os.path.join(TEX, 'B52_atlas_clean.png')

TAN, MED_GREEN, DARK_GREEN = (150, 124, 92), (88, 100, 70), (58, 70, 52)
BLACK, WHITE = (32, 32, 34), (232, 232, 226)
SEA = (TAN, MED_GREEN, DARK_GREEN)
# The two game sides' schemes (offered only to that side; B52Builder.ExtraLiveries): new designs in the spirit of each,
# not copies of the game's own liveries. Boscali: a low-visibility blue-grey three-tone over a light grey belly.
# Primeva: a dark charcoal, olive and grey-green scheme over a mid grey belly.
BOSCALI = ((162, 170, 176), (118, 132, 146), (92, 104, 118))
PRIMEVA = ((84, 82, 64), (56, 59, 62), (74, 84, 78))
# livery file -> top colours (a third the first, the rest split between the other two), belly colour, the normal z the
# belly reaches up to (0 = the sides), how much the line waves, and whether the clean atlas's markings (the USAF
# insignia, LA tail code, serial and lettering) go on: not on the two game sides' schemes, which aren't USAF aircraft
LIVERIES = {
    'B52_livery_sea.png':  (SEA, BLACK, 0.30, 0.12, True),         # black over the lower sides and the fin, as on the B-52D
    'B52_livery_siop.png': (SEA, WHITE, -0.20, 0.08, True),        # white under the wings and belly, the line along the lower sides
    'B52_livery_boscali.png': (BOSCALI, (196, 200, 204), -0.10, 0.06, False),
    'B52_livery_primeva.png': (PRIMEVA, (122, 126, 128), -0.10, 0.06, False),
}

clean = np.asarray(Image.open(CLEAN).convert('RGB')).astype(np.float32)
n = clean.shape[0]
mk = masks(n)
if mk is None: raise SystemExit('no livery masks: run tools/blender_livery_masks.py first')
nz, na, nb, baked, covered = mk
cov = covered.astype(np.float32)


def local_median(a, ok, cell=16, win=11):
    """Median of a over the faces (ok) in a window of win x win cells of cell x cell texels, at full size."""
    h = a.shape[0] // cell
    b = np.where(ok, a, np.nan).reshape(h, cell, h, cell).transpose(0, 2, 1, 3).reshape(h, h, -1)
    with np.errstate(all='ignore'), warnings.catch_warnings():
        warnings.simplefilter('ignore', RuntimeWarning)         # windows with no faces in them
        small = np.nanmedian(b, -1)
        p = win // 2
        padded = np.pad(small, p, constant_values=np.nan)
        win_ = np.lib.stride_tricks.sliding_window_view(padded, (win, win))
        med = np.nanmedian(win_.reshape(h, h, -1), -1)
    med = np.where(np.isnan(med), 0.0, med)
    return np.asarray(Image.fromarray(med.astype(np.float32), 'F').resize(a.shape[::-1], Image.BILINEAR))


# A few texels hold another face's facing (hidden structure in the source model shares texture space with the wing top,
# and painted a dark square there): where the facing is far from what the faces around it (~1 m) face, use theirs.
med = local_median(nz, covered)
nz = np.where(np.abs(nz - med) > 0.6, med, nz)

# camouflage: a third tan (the top of noise a), the rest split dark/medium green by noise b; soft edges
ta = np.quantile(na[covered], 0.64); tb = np.quantile(nb[covered], 0.5)
tan = ramp(na, ta - 0.006, ta + 0.006)[..., None]
dark = ramp(nb, tb - 0.006, tb + 0.006)[..., None]
def camo_of(top):
    c0, c1, c2 = (np.array(c) for c in top)
    return tan * c0 + (1 - tan) * (dark * c2 + (1 - dark) * c1)

# detail from the clean atlas, measured only on the faces (the empty space around them would outline every part):
# shading and panel lines as a ratio, markings pasted over
lum = clean.mean(-1)
local = blur(lum * cov, 6) / (blur(cov, 6) + 1e-4)
shade = np.where(covered, np.clip(lum / (local + 1e-3), 0.85, 1.12), 1.0)[..., None]
chroma = np.abs(clean - lum[..., None]).max(-1)
mark = np.maximum(ramp(np.abs(lum - local), 22, 36), ramp(chroma, 14, 28)) * cov
mark = np.maximum(mark, blur(mark, 1.0) * cov)[..., None]          # include the strokes' soft edges
# shading for the schemes without markings: measured with the markings (grown to cover their soft edges) left out, so
# neither the lettering nor a halo round it (the dark strokes pull the local average down) shows through
m2 = np.clip(blur(mark[..., 0], 2.0) * 3, 0, 1)
okm = cov * (1 - m2)
local_u = blur(lum * okm, 6) / (blur(okm, 6) + 1e-4)
lum_u = lum * (1 - m2) + local_u * m2
shade_u = np.where(covered, np.clip(lum_u / (local_u + 1e-3), 0.85, 1.12), 1.0)[..., None]

for name, (top, belly, line, wave, marked) in LIVERIES.items():
    edge = line + wave * (nb - 0.5) * 2
    under = (1 - ramp(nz, edge - 0.015, edge + 0.015))[..., None]
    if marked:
        out = (under * np.array(belly) + (1 - under) * camo_of(top)) * shade
        out = out * (1 - mark) + clean * mark
    else:   # no markings: and none of their outline in the shading either
        out = (under * np.array(belly) + (1 - under) * camo_of(top)) * shade_u
    res = np.where(baked[..., None], out, clean)                    # the bake margin too, so mip-maps bleed camouflage
    Image.fromarray(np.clip(res, 0, 255).astype(np.uint8)).save(os.path.join(TEX, name))
    print('LIVERY', name)
