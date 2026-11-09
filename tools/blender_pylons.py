"""
Wing pylons (Heavy Stores Adapter Beams), run after blender_wingwells.py and before blender_atlas.py:
  blender -b blender/out/B52_parts.blend --python tools/blender_pylons.py

One pylon under each inner wing, between the fuselage and the inboard engine pod (x +-5.6 m), as on the B-52H:
a slim fairing whose top follows the wing's lower skin, swept leading edge, and the adapter beam below it that the
weapon racks hang from. Painted with a plain-colour material (PylonPaint) that blender_atlas.py packs as a swatch, so
the pylons take liveries and damage shading like the skin. The Unity builder (B52Builder.BuildHardpoints) finds
hsabPylon_<s>, hangs the rack mount under the beam and shows the pylon only while a pylon weapon is loaded.
Blender frame: +X right, +Y forward, +Z up.
"""
import bpy, bmesh
from mathutils import Matrix, Vector
from mathutils.bvhtree import BVHTree

X = 5.6                      # station, m from the centreline
LEN_TOP, LEN_BOT = 5.2, 4.4  # fairing length along the skin / at its foot
DEPTH = 0.62                 # fairing height below the skin at its deepest
HALF_W = 0.16                # fairing half-width
BEAM = (6.0, 0.13, 0.11)     # adapter beam: length, half-width, half-height
SINK = 0.06                  # fairing top sunk into the skin so no gap shows

def paint(name, rgb):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.use_nodes = True
    m.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value = (*rgb, 1)
    m.diffuse_color = (*rgb, 1)                                   # blender_atlas.py reads this for the swatch
    return m

M_PYLON = paint('PylonPaint', (0.58, 0.59, 0.61))          # = the wing-underside paint's average

report = []
for s, side in (('L', -1), ('R', 1)):
    wing = bpy.data.objects[f'wingroot_{s}']
    bm = bmesh.new(); bm.from_mesh(wing.data); bm.transform(wing.matrix_world)
    tree = BVHTree.FromBMesh(bm)
    ys = [v.co.y for v in bm.verts]; bm.free()
    x = side * X

    def skin(y):
        hit = tree.ray_cast(Vector((x, y, -6.0)), Vector((0, 0, 1)), 12.0)[0]
        return hit.z if hit else None

    # Centre the pylon under the wing chord at this station (where the lower skin exists).
    found = [y for y in [min(ys) + i * 0.05 for i in range(int((max(ys) - min(ys)) / 0.05))] if skin(y) is not None]
    y_mid = (min(found) + max(found)) / 2 - 0.4                     # a little aft of mid-chord, like the real beam
    foot = min(skin(y_mid + d) for d in (-LEN_TOP / 2, 0.0, LEN_TOP / 2)) - DEPTH

    g = bmesh.new()
    def ring(y, z_top, z_bot, hw):
        return [g.verts.new((x - hw, y, z_top)), g.verts.new((x + hw, y, z_top)),
                g.verts.new((x + hw, y, z_bot)), g.verts.new((x - hw, y, z_bot))]
    # stations along the fairing: nose (narrow, swept), body, tail (narrow); the foot is shorter than the top
    st = [(+LEN_TOP / 2, +LEN_BOT / 2, 0.03), (+LEN_TOP / 2 - 0.7, +LEN_BOT / 2 - 0.3, HALF_W),
          (-LEN_TOP / 2 + 0.8, -LEN_BOT / 2 + 0.3, HALF_W), (-LEN_TOP / 2, -LEN_BOT / 2, 0.04)]
    rings = []
    for yt, yb, hw in st:
        top = skin(y_mid + yt)
        top = (top if top is not None else foot + DEPTH) + SINK
        r = ring(y_mid + yt, top, foot, hw)
        r[2].co.y = r[3].co.y = y_mid + yb                       # the foot stations sit inboard of the top ones
        rings.append(r)
    for a, b in zip(rings, rings[1:]):
        for i in range(4):
            g.faces.new((a[i], a[(i + 1) % 4], b[(i + 1) % 4], b[i]))
    g.faces.new(rings[0][::-1]); g.faces.new(rings[-1])
    # adapter beam under the fairing
    L, W, H = BEAM
    zc = foot - H
    c = [(x + dx * W, y_mid + dy * L / 2, zc + dz * H) for dx in (-1, 1) for dy in (-1, 1) for dz in (-1, 1)]
    bv = [g.verts.new(p) for p in c]
    for f in ((0, 1, 3, 2), (4, 6, 7, 5), (0, 4, 5, 1), (2, 3, 7, 6), (0, 2, 6, 4), (1, 5, 7, 3)):
        g.faces.new([bv[i] for i in f])
    bmesh.ops.recalc_face_normals(g, faces=g.faces)
    # simple box-projected UVs: the swatch is a plain colour, so any UV inside the face's island works
    uv = g.loops.layers.uv.new('UVMap')
    for f in g.faces:
        for l in f.loops: l[uv].uv = (0.5, 0.5)

    name = f'hsabPylon_{s}'
    old = bpy.data.objects.get(name)
    if old: me_old = old.data; bpy.data.objects.remove(old); bpy.data.meshes.remove(me_old)
    origin = Vector((x, y_mid, foot))
    g.transform(Matrix.Translation(-origin))
    me = bpy.data.meshes.new(name); g.to_mesh(me); g.free()
    me.materials.append(M_PYLON)
    o = bpy.data.objects.new(name, me); bpy.context.scene.collection.objects.link(o)
    o.matrix_world = Matrix.Translation(origin); bpy.context.view_layer.update()
    mw = o.matrix_world.copy(); o.parent = wing; o.matrix_world = mw
    report.append(f'{name}: y {y_mid:.2f}, foot z {foot:.2f}, beam bottom z {zc - H:.2f}')

bpy.ops.wm.save_mainfile()
print('PYLONS', *report, sep='\n  ')
