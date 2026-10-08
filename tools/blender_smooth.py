"""
Smooth shading for the B-52's hull, run after blender_prep.py:
  blender -b blender/out/B52_parts.blend --python tools/blender_smooth.py

Two shading faults came from bohmerang's low-poly model and from splitting it into parts:
  - About a quarter of the fuselage faces are flat-shaded among smooth ones, so with any side light they read as
    dents (most visibly on both sides of the hull just behind the cockpit).
  - Each part computes its own normals, so where two parts meet (cockpit / fuselage_F / fuselage / fuselage_R,
    wing root / fuselage) the light steps across the seam.
Fix: build one reference surface from all the hull parts (copies joined, seam vertices merged, smooth by angle so
real hard edges over 40 deg stay sharp), then give every hull part the reference's normals (Data Transfer, custom
split normals from the nearest face). Glass and the scratch-built parts (gear, doors, cockpit interior) are left alone.
"""
import bpy, bmesh, math

HULL = ['B52', 'fuselage_F', 'fuselage_R', 'cockpit', 'tail', 'bayDoor_L', 'bayDoor_R']
SHARP = math.radians(40)

objs = [bpy.data.objects[n] for n in HULL if n in bpy.data.objects]

# reference surface
bm = bmesh.new()
for o in objs:
    t = bmesh.new(); t.from_mesh(o.data); t.transform(o.matrix_world)
    glass = {i for i, m in enumerate(o.data.materials) if m and m.name == 'Glass'}
    bmesh.ops.delete(t, geom=[f for f in t.faces if f.material_index in glass], context='FACES')
    me = bpy.data.meshes.new('_t'); t.to_mesh(me); t.free(); bm.from_mesh(me); bpy.data.meshes.remove(me)
bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=0.004)
for f in bm.faces: f.smooth = True
for e in bm.edges:                                   # keep real creases hard
    if len(e.link_faces) == 2 and e.calc_face_angle(0) > SHARP: e.smooth = False
ref_me = bpy.data.meshes.new('_hull_ref'); bm.to_mesh(ref_me); bm.free()
ref = bpy.data.objects.new('_hull_ref', ref_me); bpy.context.scene.collection.objects.link(ref)

fixed = 0
for o in objs:
    me = o.data
    glass = {i for i, m in enumerate(me.materials) if m and m.name == 'Glass'}
    for p in me.polygons:
        if p.material_index not in glass: p.use_smooth = True
    bpy.context.view_layer.objects.active = o
    for s in bpy.context.selected_objects: s.select_set(False)
    o.select_set(True)
    mod = o.modifiers.new('hull_normals', 'DATA_TRANSFER')
    mod.object = ref
    mod.use_loop_data = True
    mod.data_types_loops = {'CUSTOM_NORMAL'}
    mod.loop_mapping = 'POLYINTERP_NEAREST'
    mod.mix_factor = 1.0
    bpy.ops.object.modifier_apply(modifier=mod.name)
    fixed += 1

bpy.data.objects.remove(ref); bpy.data.meshes.remove(ref_me)
bpy.ops.wm.save_mainfile()
print('SMOOTH hull normals transferred to', fixed, 'parts')
