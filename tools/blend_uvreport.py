"""Report each exterior material of B52_parts.blend: its image, size, the UV bounds of its faces, and how much
surface it covers. Input for the single-atlas livery work.
Run: blender -b blender/out/B52_parts.blend --python tools/blend_uvreport.py"""
import bpy
from collections import defaultdict

interior = set()
ci = bpy.data.objects.get('cockpit_int')
if ci:
    stack = [ci]
    while stack:
        o = stack.pop(); interior.add(o.name); stack.extend(o.children)

info = defaultdict(lambda: {'objs': set(), 'area': 0.0, 'umin': 9, 'umax': -9, 'vmin': 9, 'vmax': -9, 'faces': 0})
for o in bpy.data.objects:
    if o.type != 'MESH' or o.name in interior: continue
    me = o.data
    if not me.uv_layers: continue
    uv = me.uv_layers.active.data
    for p in me.polygons:
        if p.material_index >= len(me.materials) or not me.materials[p.material_index]: continue
        m = me.materials[p.material_index].name
        d = info[m]; d['objs'].add(o.name); d['area'] += p.area; d['faces'] += 1
        for li in p.loop_indices:
            u, v = uv[li].uv
            d['umin'] = min(d['umin'], u); d['umax'] = max(d['umax'], u); d['vmin'] = min(d['vmin'], v); d['vmax'] = max(d['vmax'], v)

def image_of(mat):
    if not mat.use_nodes: return None
    for n in mat.node_tree.nodes:
        if n.type == 'TEX_IMAGE' and n.image: return n.image
    return None

for m, d in sorted(info.items(), key=lambda kv: -kv[1]['area']):
    mat = bpy.data.materials[m]; img = image_of(mat)
    col = tuple(round(c, 2) for c in mat.diffuse_color[:3])
    print(f"UVR {m:28s} area {d['area']:8.1f} m2 faces {d['faces']:6d}  uv u[{d['umin']:.2f},{d['umax']:.2f}] v[{d['vmin']:.2f},{d['vmax']:.2f}]  "
          f"img {img.name + ' ' + str(tuple(img.size)) if img else '-'}  col {col}  objs {len(d['objs'])}")
