"""
Hull clean-up, run after the geometry steps (prep, normals, gear, wells, wing wells, pylons, fans) and before
blender_cockpit.py / blender_atlas.py:
  blender -b blender/out/B52_parts.blend --python tools/blender_hullfix.py -- <source B-52.blend>

1. Shading. bohmerang's mesh carries custom (hand-set) normals that keep its flat panels shading flat. Blender stores
   them relative to the faces around each corner, so every cut, split and flip in the earlier steps scrambles them
   near the change: the flat side of the forward fuselage behind the cockpit shaded as if creased, in a pyramid
   pattern (normals off by up to 37 degrees). Here every smooth skin corner gets back the normal the original had at
   that point, read from the source file and matched by position and facing. Corners the original doesn't have
   (well rims, cut lines) take the nearest original corner on a face facing the same way.
2. Shape. The original has a dent on the right side just behind the cockpit glass (y 20.5-24.5 m, z 0.3-1.4 m: up to
   9 cm in, with a 5 cm bulge above it), while the left side is smooth. The vertices are symmetric; the right side's
   long triangles are split along the diagonal that folds the skin in (and a band higher up on the left the same
   way). On a convex hull no outer-skin edge should fold in, so those edges are rotated.
Blender frame: +X right, +Y forward, +Z up.
"""
import bpy, bmesh, sys
from mathutils import Matrix, Vector
from mathutils.bvhtree import BVHTree
from mathutils.kdtree import KDTree

SRC = sys.argv[sys.argv.index('--') + 1]
SCALE = 56.39 / 55.98                                     # = blender_prep.py
SKIP = ('gear_', 'wheel_', 'tlink', 'wellBox', 'hsabPylon', 'gearDoorPanel_', 'outDoor', 'spoilers_')
HULL = ('cockpit', 'fuselage_F', 'B52')                   # objects the dent window can touch
DENT = dict(y0=20.5, y1=24.5, z0=0.3, z1=1.4)       # the dent's extent (right side)

def in_cockpit(o):
    while o:
        if o.name == 'cockpit_int': return True
        o = o.parent
    return False


parts = [o for o in bpy.data.objects if o.type == 'MESH' and not o.name.startswith(SKIP) and not in_cockpit(o)]

# ---- the source's corners in output (world) space: position, face normal, corner normal
with bpy.data.libraries.load(SRC, link=False) as (src_from, src_to):
    src_to.objects = list(src_from.objects)
# in the scene for a moment, so their world matrices (bohmerang's objects sit under a transformed parent) are evaluated
tmp = bpy.data.collections.new('_hullfix_src'); bpy.context.scene.collection.children.link(tmp)
for so in src_to.objects:
    if so: tmp.objects.link(so)
bpy.context.view_layer.update()
src_objs = [o for o in src_to.objects if o and o.type == 'MESH']
pos, fns, cns = [], [], []
for so in src_objs:
    me = so.data; me.update()
    M = Matrix.Scale(SCALE, 4) @ so.matrix_world
    N = M.to_3x3().inverted().transposed()
    cn = me.corner_normals
    for p in me.polygons:
        fn = (N @ p.normal).normalized()
        for li in p.loop_indices:
            pos.append(M @ me.vertices[me.loops[li].vertex_index].co)
            fns.append(fn); cns.append((N @ cn[li].vector).normalized())
kd = KDTree(len(pos))
for i, p in enumerate(pos): kd.insert(p, i)
kd.balance()
for so in list(tmp.objects):                             # the appended source objects aren't needed any more
    me = so.data if so.type == 'MESH' else None
    bpy.data.objects.remove(so)
    if me and me.users == 0: bpy.data.meshes.remove(me)
bpy.data.collections.remove(tmp)


def lookup(p, fn):
    """The original corner normal at p on a face facing like fn (None if the original has nothing there)."""
    for radius in (1e-4, 0.05):
        best, best_dot = None, 0.8
        for _, i, _ in kd.find_range(p, radius):
            d = abs(fns[i].dot(fn))
            if d > best_dot: best, best_dot = i, d
        if best is not None:
            n = cns[best]
            return -n if n.dot(fn) < 0 else n              # some original faces were wound inward; face outward
    return None


# ---- 2. the dent: long skin triangles folded the wrong way
# Between the rings of vertices at y 20.4 and 23.8 m the skin is a strip of 3.4 m triangles. Most quads are split
# along the diagonal that bulges out with the hull's curve; several are split along the other one, which folds the
# skin in: the dent on the right, and a shallower band on the left. On a convex hull no outer-skin edge should fold
# in, so any that does here is rotated to the other diagonal (same vertices, same UVs).
def in_window(p):
    return abs(p.x) > 0.3 and DENT['y0'] - 0.9 < p.y < DENT['y1'] + 0.8 and DENT['z0'] - 0.5 < p.z < DENT['z1'] + 0.3

rotated = 0
for o in [bpy.data.objects[n] for n in HULL if n in bpy.data.objects]:
    mw = o.matrix_world
    bm = bmesh.new(); bm.from_mesh(o.data); bm.transform(mw); bm.normal_update()
    # quads here would be split by the game whichever way it likes: make them triangles, then fix the folds
    quads = [f for f in bm.faces if len(f.verts) > 3 and in_window(f.calc_center_median())
             and abs(f.normal.x) > 0.3 and f.normal.x * f.calc_center_median().x > 0]
    if quads: bmesh.ops.triangulate(bm, faces=quads, quad_method='BEAUTY', ngon_method='BEAUTY')
    for _ in range(6):                                   # rotating one edge can leave its neighbour folded: repeat
        bm.normal_update()
        fold = []
        for e in bm.edges:
            if len(e.link_faces) != 2: continue
            f1, f2 = e.link_faces
            if len(f1.verts) != 3 or len(f2.verts) != 3: continue
            m = (e.verts[0].co + e.verts[1].co) / 2
            side = 1.0 if m.x > 0 else -1.0
            if not in_window(m) or f1.normal.x * side < 0.3 or f2.normal.x * side < 0.3: continue   # outer side skin only
            o1 = next(v for v in f1.verts if v not in e.verts); o2 = next(v for v in f2.verts if v not in e.verts)
            if (o2.co - m).dot(f1.normal) > 0.002 and (o1.co - m).dot(f2.normal) > 0.002: fold.append(e)
        if not fold: break
        bmesh.ops.rotate_edges(bm, edges=fold, use_ccw=False)
        rotated += len(fold)
    bm.transform(mw.inverted()); bm.to_mesh(o.data); bm.free(); o.data.update()

# ---- 1. shading: original normals back on every smooth skin corner
restored = fallback = 0
for o in parts:
    me = o.data; me.update()
    mw = o.matrix_world; N = mw.to_3x3().inverted().transposed(); to_local = mw.to_3x3().inverted()
    normals = [c.vector.copy() for c in me.corner_normals]
    changed = False
    for poly in me.polygons:
        if not poly.use_smooth: continue
        fn = (N @ poly.normal).normalized()
        for li in poly.loop_indices:
            p = mw @ me.vertices[me.loops[li].vertex_index].co
            n = lookup(p, fn)
            if n is None: fallback += 1; continue
            normals[li] = (to_local @ n).normalized(); restored += 1; changed = True
    if changed: me.normals_split_custom_set(normals)

bpy.ops.wm.save_mainfile()
print(f'HULLFIX dent: {rotated} folded-in skin edges rotated on the hull sides behind the cockpit')
print(f'HULLFIX shading: {restored} corner normals restored from the original on {len(parts)} parts, {fallback} left as they were')
