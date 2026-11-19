"""
Tail gun (B-52H M61A1), run after the damage display masks and before the FBX export:
  blender -b blender/out/B52_parts.blend --python tools/blender_tailgun.py

bohmerang's model is a B-52H and still has its tail armament: the AN/ASG-21 radar domes above the tail cone and the
M61A1's six barrels in their boot below them. The B-52J has no gun, so the barrels come out of the tail cone's mesh
into their own object, `tailgun`, with its origin on the barrels' axis inside the boot (the turret's pivot). The
Unity builder (B52Builder.TakeTailGun) takes that mesh off the aircraft and gives it to the optional tail turret mount
(B52Weapons.MakeTailTurret), so the barrels show, aim and spin only when the "Tail Turret" station is loaded. The
boot and the radar domes stay on the hull.

Late in the pipeline on purpose: the barrels keep the atlas UVs blender_atlas.py gave them, and the distant model
(blender_lods.py) has been made, so its copy of the barrels is cut out here too.
Blender frame: +X right, +Y forward, +Z up.
"""
import bpy, bmesh
from mathutils import Matrix, Vector

# The barrels, their two clamp rings and the breech hub, behind the boot: every vertex of each piece is in this box
# (world metres). The boot itself (+-0.30 m wide) and the fairings above it are larger, so they stay.
BOX_X, BOX_Z, BOX_Y = 0.07, (-0.25, -0.10), -20.84


def islands(bm):
    seen = set()
    for v in bm.verts:
        if v.index in seen: continue
        stack, comp = [v], []
        while stack:
            a = stack.pop()
            if a.index in seen: continue
            seen.add(a.index); comp.append(a)
            stack += [e.other_vert(a) for e in a.link_edges]
        yield comp


def gun_verts(bm, mw):
    out = []
    for comp in islands(bm):
        if all(abs((mw @ a.co).x) < BOX_X and BOX_Z[0] < (mw @ a.co).z < BOX_Z[1] and (mw @ a.co).y < BOX_Y for a in comp):
            out += comp
    return out


hull = bpy.data.objects['fuselage_R']
if 'tailgun' in bpy.data.objects:
    print('TAILGUN already split:', len(bpy.data.objects['tailgun'].data.vertices), 'vertices')
    raise SystemExit(0)

mw = hull.matrix_world.copy()
bm = bmesh.new(); bm.from_mesh(hull.data); bm.verts.ensure_lookup_table()
gun = gun_verts(bm, mw)
if len(gun) < 40:
    raise SystemExit(f'TAILGUN FAILED: only {len(gun)} barrel vertices found behind the tail cone')
ws = [mw @ v.co for v in gun]
xs, ys, zs = [w.x for w in ws], [w.y for w in ws], [w.z for w in ws]
# Pivot: on the barrels' axis, 8 cm inside the boot from the breech hub's face.
pivot = Vector(((min(xs) + max(xs)) / 2, max(ys) + 0.08, (min(zs) + max(zs)) / 2))

# The new object: a copy of the hull mesh with everything but the gun deleted, in pivot-relative coordinates.
me = hull.data.copy(); me.name = 'tailgun'
gb = bmesh.new(); gb.from_mesh(me); gb.verts.ensure_lookup_table()
keep = {v.index for v in gun}
bmesh.ops.delete(gb, geom=[v for v in gb.verts if v.index not in keep], context='VERTS')
gb.transform(Matrix.Translation(-pivot) @ mw)
gb.to_mesh(me); gb.free()
ob = bpy.data.objects.new('tailgun', me)
for c in hull.users_collection: c.objects.link(ob)
ob.parent = hull
ob.matrix_parent_inverse = Matrix.Identity(4)
ob.matrix_world = Matrix.Translation(pivot)

# ...and the gun out of the hull.
bmesh.ops.delete(bm, geom=gun, context='VERTS')
bm.to_mesh(hull.data); bm.free(); hull.data.update()

# The distant model's copy of the barrels, if the decimation kept any.
cut_lod = 0
lod = bpy.data.objects.get('fuselage_R_LoD')
if lod:
    lb = bmesh.new(); lb.from_mesh(lod.data); lb.verts.ensure_lookup_table()
    lg = gun_verts(lb, lod.matrix_world)
    cut_lod = len(lg)
    if lg: bmesh.ops.delete(lb, geom=lg, context='VERTS')
    lb.to_mesh(lod.data); lb.free(); lod.data.update()

bpy.ops.wm.save_mainfile()
print(f'TAILGUN {len(me.vertices)} vertices, pivot ({pivot.x:.3f}, {pivot.y:.3f}, {pivot.z:.3f}), '
      f'barrels {max(ys) - min(ys):.2f} m long; {cut_lod} vertices cut from the distant model')
