"""Paints the camouflage liveries from the clean atlas and the baked masks (tools/blender_livery_masks.py):
  SEA camouflage over a black belly: the B-52D over Vietnam, the black up the fuselage and fin sides
  SEA camouflage over an anti-flash white belly: the B-52G/H's 1970s nuclear alert scheme
Top: the T.O. 1-1-4 Southeast Asia colours, tan FS 30219, medium green FS 34102 and dark green FS 34079, about a
third each, in patches about 6 m across that run on across parts and texture seams (the masks are sampled in 3D).
Panel lines and shading come from the clean atlas, and so do the markings (lettering, insignia, nose art).
Run: python tools/make_liveries.py   (after make_clean_livery.py and blender_livery_masks.py)"""
import os, warnings
import numpy as np
from PIL import Image
from atlas_util import ROOT, blur, ramp, masks

TEX = os.path.join(ROOT, 'unity', 'Mods', 'B52', 'Textures')
CLEAN = os.path.join(TEX, 'B52_atlas_clean.png')

TAN, MED_GREEN, DARK_GREEN = (150, 124, 92), (88, 100, 70), (58, 70, 52)
BLACK, WHITE = (32, 32, 34), (232, 232, 226)
# livery file -> belly colour, the normal z the belly reaches up to (0 = the sides), how much the line waves
LIVERIES = {
    'B52_livery_sea.png':  (BLACK, 0.30, 0.12),      # black over the lower sides and the fin, as on the B-52D
    'B52_livery_siop.png': (WHITE, -0.20, 0.08),     # white under the wings and belly, the line along the lower sides
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
camo = tan * np.array(TAN) + (1 - tan) * (dark * np.array(DARK_GREEN) + (1 - dark) * np.array(MED_GREEN))

# detail from the clean atlas, measured only on the faces (the empty space around them would outline every part):
# shading and panel lines as a ratio, markings pasted over
lum = clean.mean(-1)
local = blur(lum * cov, 6) / (blur(cov, 6) + 1e-4)
shade = np.where(covered, np.clip(lum / (local + 1e-3), 0.85, 1.12), 1.0)[..., None]
chroma = np.abs(clean - lum[..., None]).max(-1)
mark = np.maximum(ramp(np.abs(lum - local), 22, 36), ramp(chroma, 14, 28)) * cov
mark = np.maximum(mark, blur(mark, 1.0) * cov)[..., None]          # include the strokes' soft edges

for name, (belly, line, wave) in LIVERIES.items():
    edge = line + wave * (nb - 0.5) * 2
    under = (1 - ramp(nz, edge - 0.015, edge + 0.015))[..., None]
    out = (under * np.array(belly) + (1 - under) * camo) * shade
    out = out * (1 - mark) + clean * mark
    res = np.where(baked[..., None], out, clean)                    # the bake margin too, so mip-maps bleed camouflage
    Image.fromarray(np.clip(res, 0, 255).astype(np.uint8)).save(os.path.join(TEX, name))
    print('LIVERY', name)
