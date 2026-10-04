"""Single-texture atlas for the B-52 exterior, so Nuclear Option's livery and damage shading work.

Nuclear Option applies a livery by putting one texture into every part's skin material (_Livery), so the
whole exterior must share one UV layout. bohmerang's model uses ~20 textures (all UVs within 0..1). This packs
them, at full resolution, into a 4096 x 4096 atlas: 1024 px images get a 1024 cell, 512 px images a quarter
cell, 256 px images a sixteenth, plain-colour materials a 64 px swatch. Each image is scaled into its cell
minus a bleed border (8 px at 1024) that tools/make_atlas.py fills by edge extension. UVs are remapped into
the cells and every exterior material slot becomes one material, B52_Skin.

Writes blender/out/atlas_layout.json for tools/make_atlas.py, which writes blender/tex/B52_atlas.png.
Run after blender_cockpit.py, before blender_export.py:
  blender -b blender/out/B52_parts.blend --python tools/blender_atlas.py
"""
import bpy, json, os

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..'))
ATLAS = 4096
CELL = 1024
LAYOUT = os.path.join(ROOT, 'blender', 'out', 'atlas_layout.json')
ATLAS_PNG = os.path.join(ROOT, 'blender', 'tex', 'B52_atlas.png')
KEEP = {'Glass'}                       # keeps its own (game glass) material
SKIP_PREFIX = ('B52_', 'CP_')          # generated gear/bay/spoiler and cockpit materials keep theirs

interior = set()
ci = bpy.data.objects.get('cockpit_int')
if ci:
    stack = [ci]
    while stack:
        o = stack.pop(); interior.add(o.name); stack.extend(o.children)
exterior = [o for o in bpy.data.objects if o.type == 'MESH' and o.name not in interior and o.data.uv_layers]


def image_of(mat):
    if mat and mat.use_nodes:
        for n in mat.node_tree.nodes:
            if n.type == 'TEX_IMAGE' and n.image: return n.image
    return None


# ---- which materials go in, and with what
mats = {}
for o in exterior:
    for s in o.material_slots:
        m = s.material
        if not m or m.name in KEEP or m.name.startswith(SKIP_PREFIX) or m.name == 'B52_Skin': continue
        mats[m.name] = m
images = {}                             # image name -> (abs path, size)
solids = {}                             # material name -> rgb
SRC = os.path.join(ROOT, 'blender', 'out', 'atlas_src')
os.makedirs(SRC, exist_ok=True)
for name, m in mats.items():
    img = image_of(m)
    if not img:
        solids[name] = [round(c, 4) for c in m.diffuse_color[:3]]; continue
    if img.name in images: continue
    # Source pixels for make_atlas.py: the original file bytes (most of bohmerang's images are packed in the .blend).
    out = os.path.join(SRC, bpy.path.clean_name(img.name) + os.path.splitext(img.filepath or '.png')[1].lower())
    if img.packed_file:
        open(out, 'wb').write(img.packed_file.data)
    elif os.path.exists(bpy.path.abspath(img.filepath)):
        import shutil; shutil.copy2(bpy.path.abspath(img.filepath), out)
    else:
        img.save_render(out)
    images[img.name] = (out, max(img.size))

# ---- layout: cells of 1024 filled big-first; small images share subdivided cells
cells = [(x, y) for y in range(0, ATLAS, CELL) for x in range(0, ATLAS, CELL)]   # pixel x, y from top-left
place, sub = {}, {}                     # image -> outer rect (x, y, w, h); sub-cell allocators per size
ci_ = 0
def take_cell():
    global ci_
    c = cells[ci_]; ci_ += 1; return c
for name, (path, size) in sorted(images.items(), key=lambda kv: (-kv[1][1], kv[0])):
    s = min(CELL, 1 << (max(size, 64) - 1).bit_length())                  # 1024, 512, 256 ...
    if s >= CELL:
        x, y = take_cell(); place[name] = (x, y, CELL, CELL); continue
    pool = sub.setdefault(s, [])
    if not pool:
        x0, y0 = take_cell()
        pool.extend((x0 + i, y0 + j, s, s) for j in range(0, CELL, s) for i in range(0, CELL, s))
    place[name] = pool.pop(0)
swatch_pool = []
swatch = {}
for name in sorted(solids):
    if not swatch_pool:
        x0, y0 = take_cell()
        swatch_pool.extend((x0 + i, y0 + j, 64, 64) for j in range(0, CELL, 64) for i in range(0, CELL, 64))
    swatch[name] = swatch_pool.pop(0)


def inner(rect):
    x, y, w, h = rect
    pad = max(2, w // 128)                                                   # 8 px at 1024, 4 at 512, 2 at 256
    return (x + pad, y + pad, w - 2 * pad, h - 2 * pad)


def to_uv(rect, u, v):
    """Source UV (0..1, origin bottom-left) -> atlas UV inside rect (pixel rect from the top-left)."""
    x, y, w, h = inner(rect)
    u = min(max(u, 0.0), 1.0); v = min(max(v, 0.0), 1.0)
    px = x + u * w
    py = y + (1.0 - v) * h                                                  # pixels from the top
    return px / ATLAS, 1.0 - py / ATLAS


# ---- remap UVs (once per mesh datablock) and swap materials
skin = bpy.data.materials.get('B52_Skin') or bpy.data.materials.new('B52_Skin')
skin.use_nodes = True
nt = skin.node_tree
tex = next((n for n in nt.nodes if n.type == 'TEX_IMAGE'), None) or nt.nodes.new('ShaderNodeTexImage')
img = bpy.data.images.get('B52_atlas.png')
if img is None:
    img = bpy.data.images.new('B52_atlas.png', 8, 8); img.source = 'FILE'
img.filepath = ATLAS_PNG
tex.image = img
bsdf = nt.nodes.get('Principled BSDF')
if bsdf: nt.links.new(tex.outputs['Color'], bsdf.inputs['Base Color'])

done_meshes = set()
remapped = 0
for o in exterior:
    me = o.data
    if me.name in done_meshes: continue
    done_meshes.add(me.name)
    uv = me.uv_layers.active.data
    for p in me.polygons:
        if p.material_index >= len(me.materials): continue
        m = me.materials[p.material_index]
        if not m or m.name not in mats: continue
        im = image_of(m)
        for li in p.loop_indices:
            if im: uv[li].uv = to_uv(place[im.name], *uv[li].uv)
            else:
                x, y, w, h = swatch[m.name]
                uv[li].uv = ((x + w / 2) / ATLAS, 1.0 - (y + h / 2) / ATLAS)
        remapped += 1
    for i, m in enumerate(me.materials):
        if m and m.name in mats: me.materials[i] = skin

json.dump({'size': ATLAS, 'out': ATLAS_PNG,
           'images': [{'name': n, 'path': images[n][0], 'rect': place[n], 'inner': inner(place[n])} for n in place],
           'swatches': [{'material': n, 'rgb': solids[n], 'rect': swatch[n]} for n in swatch]},
          open(LAYOUT, 'w'), indent=1)
bpy.ops.wm.save_mainfile()
print(f'ATLAS {len(place)} images, {len(swatch)} swatches, {ci_} of {len(cells)} cells, {remapped} faces remapped -> {LAYOUT}')
