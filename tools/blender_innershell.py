"""
A dark inner lining for the fuselage around the gear wells and the bomb bay, run after blender_hullfix.py:
  blender -b blender/out/B52_parts.blend --python tools/blender_innershell.py

The fuselage is a single-sided shell: every skin face points outward, and the game doesn't draw a face from behind.
Looking up through an open gear-well door, past the edge of the well box, you saw the inside of the fuselage, which
the game culls, so the sky showed through the aircraft. Each fuselage part that holds a well or the bay gets
innerShell_<part>: a copy of its skin from 2.5 m behind the rear wells to 1 m ahead of the front wells, 4 cm inside
the skin and facing inward, in the bomb bay's dark material. From outside it is never seen (it faces away and the
skin covers it); through an open door it is the dark inside of the airframe.
Blender frame: +X right, +Y forward, +Z up.
"""
import bpy, bmesh

Y_MIN, Y_MAX = -4.0, 17.6      # rear wells start at y -1.54, front wells end at y 16.55
INSET = 0.04
PARTS = ('B52', 'fuselage_F')

mat = bpy.data.materials.get('B52_BayInterior')
if mat is None: raise SystemExit('no B52_BayInterior material: run blender_prep.py first')
report = []
for name in PARTS:
    o = bpy.data.objects.get(name)
    if o is None: report.append(f'{name}: missing'); continue
    sname = 'innerShell_' + name
    old = bpy.data.objects.get(sname)
    if old: m_old = old.data; bpy.data.objects.remove(old); bpy.data.meshes.remove(m_old)
    bm = bmesh.new(); bm.from_mesh(o.data); bm.transform(o.matrix_world)
    keep = [f for f in bm.faces if Y_MIN < f.calc_center_median().y < Y_MAX]
    bmesh.ops.delete(bm, geom=[f for f in bm.faces if f not in set(keep)], context='FACES')
    bm.normal_update()
    # move every vertex in along the skin's smoothed normal, then turn the faces to look inward
    off = {v: -v.normal * INSET for v in bm.verts}
    for v, d in off.items(): v.co += d
    for f in bm.faces:
        f.normal_flip(); f.material_index = 0; f.smooth = True
    bm.transform(o.matrix_world.inverted())
    me = bpy.data.meshes.new(sname); bm.to_mesh(me); bm.free()
    me.materials.append(mat)
    for uv in list(me.uv_layers): me.uv_layers.remove(uv)
    s = bpy.data.objects.new(sname, me); bpy.context.scene.collection.objects.link(s)
    s.parent = o; s.matrix_parent_inverse.identity(); s.matrix_basis.identity()
    report.append(f'{sname}: {len(me.polygons)} faces')

bpy.ops.wm.save_mainfile()
print('INNERSHELL', *report, sep='\n  ')
