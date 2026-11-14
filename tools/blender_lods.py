"""
Level-of-detail meshes, like the stock aircraft's "LoD" children: a decimated copy of each skinned part, named
<part>_LoD and parented to it, so it moves (and breaks off) with the part. B52Builder.BuildLods shows them once the
B-52 is under 5 % of the screen height. Same atlas UVs and materials, so liveries and damage shading work on them.
Left out (nothing in the distance): gear, wheels, wells, the cockpit interior, fans, spoilers and weapon pylons.
Run last before the FBX export (the AO and livery bakes mustn't see the copies):
  blender -b blender/out/B52_parts.blend --python tools/blender_lods.py
"""
import bpy, bmesh, math

RATIO, MIN_TRIS = 0.25, 16       # a quarter of the triangles; tiny parts keep what they have
THIN = ('hstab', 'elevator', 'rudder', 'tail', 'flap')   # thin plates fall apart below half (0.5)
SHARP = math.radians(40)         # edges sharper than this stay hard (trailing edges, fin tips)

skin = bpy.data.materials.get('B52_Skin')
if skin is None: raise SystemExit('no B52_Skin material: run blender_atlas.py first')
skip = ('gear_', 'wheel_', 'tlink', 'wellBox', 'cockpit_int', 'fan_', 'spoiler', 'hsabPylon')
inside = set(bpy.data.objects['cockpit_int'].children_recursive) if 'cockpit_int' in bpy.data.objects else set()
parts = [o for o in bpy.data.objects
         if o.type == 'MESH' and not o.name.startswith(skip) and not o.name.endswith('_LoD') and o not in inside
         and any(m == skin for m in o.data.materials)]

dg = bpy.context.evaluated_depsgraph_get()
before = after = 0
for o in parts:
    tris = sum(len(p.vertices) - 2 for p in o.data.polygons)
    low = o.copy(); low.data = o.data.copy(); low.name = low.data.name = o.name + '_LoD'
    for c in o.users_collection: c.objects.link(low)
    low.modifiers.clear()
    ratio = 0.5 if o.name.startswith(THIN) else RATIO
    if tris > MIN_TRIS / ratio:
        # the part's open edges (where it meets the next part) stay put, or gaps open between parts
        bm = bmesh.new(); bm.from_mesh(low.data)
        edge = [v.index for v in bm.verts if any(e.is_boundary for e in v.link_edges)]; bm.free()
        g = low.vertex_groups.new(name='keep'); g.add(edge, 1.0, 'REPLACE')
        d = low.modifiers.new('decimate', 'DECIMATE'); d.decimate_type = 'COLLAPSE'
        d.ratio = ratio; d.use_collapse_triangulate = True
        d.vertex_group = 'keep'; d.invert_vertex_group = True; d.vertex_group_factor = 100.0
        dg.update()
        me = bpy.data.meshes.new_from_object(low.evaluated_get(dg))
        low.modifiers.clear(); low.vertex_groups.clear(); old = low.data; low.data = me; bpy.data.meshes.remove(old)
    bm = bmesh.new(); bm.from_mesh(low.data)
    for f in bm.faces: f.smooth = True
    for e in bm.edges:
        e.smooth = not (len(e.link_faces) == 2 and e.calc_face_angle(0) > SHARP)
    bm.to_mesh(low.data); bm.free()
    low.parent = o; low.matrix_parent_inverse = o.matrix_world.inverted(); low.matrix_world = o.matrix_world
    before += tris; after += sum(len(p.vertices) - 2 for p in low.data.polygons)

bpy.ops.wm.save_as_mainfile(filepath=bpy.data.filepath)
print(f'LODS {len(parts)} parts: {before} -> {after} triangles ({100 * after / max(before, 1):.0f} %)')
