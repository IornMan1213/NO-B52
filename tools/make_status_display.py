"""
The B-52J damage display's background (status_bg.png) from tools/blender_status.py's renders: the whole aircraft as a dim
fill with every part's outline drawn in light grey, so the display shows the sections the damage model works with. The
part masks stay white fills (the game tints them), pulled in by a pixel so the outlines between parts stay visible.
Run: python3 tools/make_status_display.py [dir]
"""
import sys, os, glob
from PIL import Image, ImageFilter, ImageChops
D = sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'unity', 'Mods', 'B52', 'Textures', 'Status')
allm = Image.open(os.path.join(D, 'status_all.png')).getchannel('A').point(lambda a: 255 if a > 40 else 0)
size = allm.size
bg = Image.new('RGBA', size, (0, 0, 0, 0))
fill = Image.new('RGBA', size, (40, 46, 52, 150))
bg.paste(fill, (0, 0), allm)
lines = Image.new('L', size, 0)
masks = sorted(glob.glob(os.path.join(D, 'status_*.png')))
for m in masks:
    n = os.path.basename(m)
    if n in ('status_all.png', 'status_bg.png'): continue
    a = Image.open(m).getchannel('A').point(lambda v: 255 if v > 40 else 0)
    edge = ImageChops.subtract(a, a.filter(ImageFilter.MinFilter(3)))           # a one-pixel outline of the part
    lines = ImageChops.lighter(lines, edge)
    inner = a.filter(ImageFilter.MinFilter(3))                                   # the mask, pulled in by a pixel
    out = Image.new('RGBA', size, (255, 255, 255, 0)); out.putalpha(inner)
    out.save(m)
outline = Image.new('RGBA', size, (205, 210, 215, 230))
bg.paste(outline, (0, 0), lines)
bg.save(os.path.join(D, 'status_bg.png'))
os.remove(os.path.join(D, 'status_all.png'))
print('STATUSBG', len(masks) - 1, 'parts outlined ->', os.path.join(D, 'status_bg.png'))
