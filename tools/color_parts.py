# Debug render helper: give every object a distinct flat colour, then call blend_render.
import bpy, colorsys, sys, os
objs = sorted([o for o in bpy.data.objects if o.type == 'MESH'], key=lambda o: o.name)
for i, o in enumerate(objs):
    m = bpy.data.materials.new('dbg_' + o.name); m.use_nodes = True
    r, g, b = colorsys.hsv_to_rgb((i * 0.618) % 1, 0.7, 0.9)
    m.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value = (r, g, b, 1)
    o.data.materials.clear(); o.data.materials.append(m)
exec(open(os.path.join(os.path.dirname(os.path.abspath(__file__)), 'blend_render.py')).read())
