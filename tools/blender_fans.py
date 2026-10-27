"""
Engine fans as their own objects, so B52Systems can spin them. Run after blender_pylons.py, before blender_atlas.py:
  blender -b blender/out/B52_parts.blend --python tools/blender_fans.py

bohmerang's pods carry both engines' fan discs and spinners (materials Blades1 / Blades2) inside the pod mesh. They are
split out per engine (each pod holds two, side by side) into fan_<pod>_<n> under the pod, with the origin on the fan's
axis, so the object turns about its own forward axis. They keep their materials; blender_atlas.py packs them as usual.
Blender frame: +X right, +Y forward, +Z up.
"""
import bpy, bmesh
from mathutils import Matrix, Vector

FAN_MATS = ('Blades1', 'Blades2')
report = []
for pod in ('pod1_L', 'pod1_R', 'pod2_L', 'pod2_R'):
    o = bpy.data.objects.get(pod)
    if not o: continue
    me = o.data; mw = o.matrix_world
    slots = {i for i, m in enumerate(me.materials) if m and m.name in FAN_MATS}
    if not slots: continue
    bm = bmesh.new(); bm.from_mesh(me); bm.transform(mw)
    fan = [f for f in bm.faces if f.material_index in slots]
    if not fan: bm.free(); continue
    xs = sorted(f.calc_center_median().x for f in fan)
    split = (xs[0] + xs[-1]) / 2                                  # the two engines sit side by side in x
    for n, pick in enumerate((lambda x: x < split, lambda x: x >= split)):
        mine = {f.index for f in fan if pick(f.calc_center_median().x)}
        if not mine: continue
        fbm = bm.copy(); fbm.faces.ensure_lookup_table()
        bmesh.ops.delete(fbm, geom=[f for f in fbm.faces if f.index not in mine], context='FACES')
        lo = Vector((min(v.co.x for v in fbm.verts), min(v.co.y for v in fbm.verts), min(v.co.z for v in fbm.verts)))
        hi = Vector((max(v.co.x for v in fbm.verts), max(v.co.y for v in fbm.verts), max(v.co.z for v in fbm.verts)))
        centre = (lo + hi) / 2
        fbm.transform(Matrix.Translation(-centre))
        name = f'fan_{pod}_{n + 1}'
        old = bpy.data.objects.get(name)
        if old: m_old = old.data; bpy.data.objects.remove(old); bpy.data.meshes.remove(m_old)
        fme = bpy.data.meshes.new(name); fbm.to_mesh(fme); fbm.free()
        for m in me.materials: fme.materials.append(m)
        fo = bpy.data.objects.new(name, fme); bpy.context.scene.collection.objects.link(fo)
        fo.matrix_world = Matrix.Translation(centre); bpy.context.view_layer.update()
        w = fo.matrix_world.copy(); fo.parent = o; fo.matrix_world = w
        report.append(f'{name}: {len(mine)} faces, centre {tuple(round(c, 2) for c in centre)}')
    # take the fan faces out of the pod itself
    bmesh.ops.delete(bm, geom=fan, context='FACES')
    bm.transform(mw.inverted()); bm.to_mesh(me); bm.free(); me.update()

bpy.ops.wm.save_mainfile()
print('FANS', *report, sep='\n  ')
