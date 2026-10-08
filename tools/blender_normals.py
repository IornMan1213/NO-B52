"""
Flips hull faces that point into the aircraft, run after blender_prep.py:
  blender -b blender/out/B52_parts.blend --python tools/blender_normals.py

Some of bohmerang's faces face inward (on the lower sides of the forward fuselage, y 10-18.7, among others). Blender
draws both sides so they look fine there, but the game culls back faces: from outside the patch vanishes and you see
the inside of the far wall through it, which reads as a big sunken box behind the cockpit. A face is flipped only
if it is the outer skin (the first thing a ray from outside the hull, aimed at the fuselage axis, hits) and its
normal points toward the axis (wings: the first hit from straight above or below, facing the other way). Inner surfaces (bomb bay, intakes, wells) are not the first hit, so they are left alone.
"""
import bpy, bmesh
from mathutils import Vector
from mathutils.bvhtree import BVHTree

PARTS = ['B52', 'fuselage_F', 'fuselage_R', 'cockpit', 'tail']
AXIS_Z = -0.63                                       # centre of the fuselage cross-section (top 1.38, belly -2.64)

total = 0
for n in PARTS:
    o = bpy.data.objects.get(n)
    if not o: continue
    bm = bmesh.new(); bm.from_mesh(o.data); bm.transform(o.matrix_world)
    bm.faces.ensure_lookup_table()
    tree = BVHTree.FromBMesh(bm)
    flip = []
    for f in bm.faces:
        c = f.calc_center_median()
        r = Vector((c.x, 0.0, c.z - AXIS_Z))
        if r.length < 0.3: continue
        r.normalize()
        if f.normal.dot(r) > -0.2: continue           # already outward (or edge-on)
        loc, _, idx, _ = tree.ray_cast(c + r * 4.0, -r, 4.5)
        if idx is not None and (idx == f.index or (loc - c).length < 0.02):
            flip.append(f)
    if flip:
        bmesh.ops.reverse_faces(bm, faces=flip)
        bm.transform(o.matrix_world.inverted())
        bm.to_mesh(o.data); o.data.update()
    bm.free()
    total += len(flip)
    print(f'NORMALS {n}: flipped {len(flip)} inward outer-skin faces')
# Wings and tail surfaces: the outer skin is what a vertical ray from above or below hits first.
WINGS = [o.name for o in bpy.data.objects if o.type == 'MESH' and o.name.split('_')[0] in
         ('wingroot', 'wing1', 'wing2', 'wingtip', 'flap1', 'flap2', 'hstab', 'elevator', 'aileron')]
for n in WINGS:
    o = bpy.data.objects[n]
    bm = bmesh.new(); bm.from_mesh(o.data); bm.transform(o.matrix_world)
    bm.faces.ensure_lookup_table()
    tree = BVHTree.FromBMesh(bm)
    flip = []
    for f in bm.faces:
        c = f.calc_center_median()
        for d in (Vector((0, 0, 1)), Vector((0, 0, -1))):          # d = outward direction tested
            if f.normal.dot(d) > -0.2: continue
            loc, _, idx, _ = tree.ray_cast(c + d * 3.0, -d, 3.5)
            if idx is not None and (idx == f.index or (loc - c).length < 0.02):
                flip.append(f); break
    if flip:
        bmesh.ops.reverse_faces(bm, faces=flip)
        bm.transform(o.matrix_world.inverted())
        bm.to_mesh(o.data); o.data.update()
    bm.free()
    total += len(flip)
    if flip: print(f'NORMALS {n}: flipped {len(flip)} inward skin faces')
bpy.ops.wm.save_mainfile()
print('NORMALS total flipped', total)
