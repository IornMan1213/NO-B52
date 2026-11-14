"""
Bakes the masks the camouflage liveries are painted from (tools/make_liveries.py), onto the atlas UVs:
  R  which way the skin faces: world normal z, mapped -1..1 -> 0..1 (belly 0, sides 0.5, top 1)
  G  a large-scale 3D noise sampled at the surface point (patches about 6 m across), for the camouflage colours
  B  a second, independent noise, for the second colour split and the wavy belly line
  A  1 where a face covers the texel (a second bake with no margin): markings and shading are measured only there,
     so the empty space around each part doesn't outline it
Sampled in 3D on the aircraft itself, so a patch carries on across texture seams and from one part to the next.
Run after blender_atlas.py:  blender -b blender/out/B52_parts.blend --python tools/blender_livery_masks.py
Writes blender/out/livery_masks.png (2048 px). The .blend isn't saved.
Faces that can't be seen from outside are left out: the source model has hidden structure inside the fuselage (behind
the bomb bay) that reuses the wing-top texture, and baked over the wing it painted a dark square there.
"""
import bpy, bmesh, os
import numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree

SIZE = 2048
ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..'))
OUT = os.path.join(ROOT, 'blender', 'out', 'livery_masks.png')

skin = bpy.data.materials.get('B52_Skin')
if skin is None: raise SystemExit('no B52_Skin material: run blender_atlas.py first')
skip = ('gear_', 'wheel_', 'tlink', 'wellBox', 'cockpit_int', 'fan_',   # the doors stay: they close flush with the skin
        'hsabPylon', 'spoilerPanels')                                      # pylons share wing texture space; sideways faces
objs = [o for o in bpy.data.objects
        if o.type == 'MESH' and not o.name.startswith(skip) and any(m == skin for m in o.data.materials)]
for o in bpy.data.objects:
    o.select_set(False)

# Leave out faces inside the airframe: a face is inside if every one of 26 rays going out (the cube's faces, edges and
# corners) from its centre and from near each corner hits the aircraft. Anything on the outside, even deep in the wing
# root, sees the sky somewhere; the corners matter for big faces that another part covers only in the middle.
verts, polys, owner = [], [], []
for o in objs:
    me = o.data; mw = o.matrix_world; base = len(verts)
    verts += [mw @ v.co for v in me.vertices]
    for p in me.polygons:
        polys.append([base + i for i in p.vertices]); owner.append((o, p.index))
tree = BVHTree.FromPolygons(verts, polys)
DIRS = [Vector((x, y, z)).normalized() for x in (-1, 0, 1) for y in (-1, 0, 1) for z in (-1, 0, 1) if (x, y, z) != (0, 0, 0)]
hide = {}
for o, pi in owner:
    p = o.data.polygons[pi]
    c = o.matrix_world @ p.center
    pts = [c] + [c.lerp(o.matrix_world @ o.data.vertices[i].co, 0.85) for i in p.vertices]
    seen = any(tree.ray_cast(q + d * 0.02, d, 200)[0] is None for q in pts for d in DIRS)
    if not seen: hide.setdefault(o, []).append(pi)
for o, faces in hide.items():
    bm = bmesh.new(); bm.from_mesh(o.data); bm.faces.ensure_lookup_table()
    bmesh.ops.delete(bm, geom=[bm.faces[i] for i in faces], context='FACES'); bm.to_mesh(o.data); bm.free()
print('LIVERY faces inside the airframe left out:', sum(len(f) for f in hide.values()))

nt = skin.node_tree; n = nt.nodes; L = nt.links
out = next(x for x in n if x.type == 'OUTPUT_MATERIAL')
geo = n.new('ShaderNodeNewGeometry')
nz = n.new('ShaderNodeSeparateXYZ'); L.new(geo.outputs['Normal'], nz.inputs[0])
r = n.new('ShaderNodeMath'); r.operation = 'MULTIPLY_ADD'; r.inputs[1].default_value = 0.5; r.inputs[2].default_value = 0.5
L.new(nz.outputs['Z'], r.inputs[0])

def noise(scale, offset):
    shift = n.new('ShaderNodeVectorMath'); shift.operation = 'ADD'; shift.inputs[1].default_value = offset
    L.new(geo.outputs['Position'], shift.inputs[0])
    t = n.new('ShaderNodeTexNoise'); t.noise_dimensions = '3D'
    t.inputs['Scale'].default_value = scale; t.inputs['Detail'].default_value = 2.0
    t.inputs['Roughness'].default_value = 0.45; t.inputs['Distortion'].default_value = 0.4
    L.new(shift.outputs[0], t.inputs['Vector'])
    return t.outputs['Fac']

col = n.new('ShaderNodeCombineColor')
L.new(r.outputs[0], col.inputs[0]); L.new(noise(0.16, (3.1, 7.7, 1.3)), col.inputs[1]); L.new(noise(0.21, (11.9, 2.3, 5.6)), col.inputs[2])
em = n.new('ShaderNodeEmission'); L.new(col.outputs[0], em.inputs[0])
L.new(em.outputs[0], out.inputs['Surface'])
tex = n.new('ShaderNodeTexImage'); n.active = tex

scn = bpy.context.scene
scn.render.engine = 'CYCLES'; scn.cycles.device = 'CPU'; scn.cycles.samples = 4
# Two passes, each into its own image: the centre fuselage ('B52') first, then every other part, which wins wherever it
# baked. The centre fuselage's hidden structure behind the bay shares texture space with the wing top (and can be seen
# through the open gear wells, so the test above keeps it).
def bake(parts, margin):
    im = bpy.data.images.new('bake', SIZE, SIZE, alpha=True, float_buffer=False, is_data=True)
    im.generated_color = (0, 0, 0, 0); tex.image = im
    for o in bpy.data.objects: o.select_set(o in parts)
    bpy.context.view_layer.objects.active = parts[0]
    bpy.ops.object.bake(type='EMIT', margin=margin, margin_type='EXTEND', use_clear=True)
    a = np.empty(SIZE * SIZE * 4, np.float32); im.pixels.foreach_get(a)
    bpy.data.images.remove(im)
    a = a.reshape(SIZE, SIZE, 4)
    a[..., 3] = (a[..., 1] + a[..., 2]) > 0.01      # baked = took the noise (the bake's own alpha is 1 everywhere for some parts)
    return a

first = [o for o in objs if o.name == 'B52']; rest = [o for o in objs if o.name != 'B52']
def layered(margin):
    top = bake(rest, margin)
    if not first: return top
    under = bake(first, margin)
    return np.where(top[..., 3:] > 0.5, top, under)
values = layered(16); cover = layered(0)
values[..., 3] = cover[..., 3] > 0.5
out_img = bpy.data.images.new('livery_masks_rgba', SIZE, SIZE, alpha=True, float_buffer=False, is_data=True)
out_img.pixels.foreach_set(values.ravel())
out_img.filepath_raw = OUT; out_img.file_format = 'PNG'; out_img.save()
print(f'LIVERY masks baked from {len(objs)} parts at {SIZE} px -> {OUT}')
