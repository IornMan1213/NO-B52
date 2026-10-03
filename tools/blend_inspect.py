import bpy, mathutils
from collections import Counter
objs = [o for o in bpy.data.objects]
print("OBJECTS", len(objs))
for o in sorted(objs, key=lambda o: o.name):
    if o.type != 'MESH':
        print(f"  {o.name:30} {o.type} parent={o.parent.name if o.parent else None}"); continue
    bb = [o.matrix_world @ mathutils.Vector(c) for c in o.bound_box]
    mn = [min(v[i] for v in bb) for i in range(3)]; mx = [max(v[i] for v in bb) for i in range(3)]
    print(f"  {o.name:30} v={len(o.data.vertices):6} f={len(o.data.polygons):6} parent={o.parent.name if o.parent else None} "
          f"min=({mn[0]:.2f},{mn[1]:.2f},{mn[2]:.2f}) max=({mx[0]:.2f},{mx[1]:.2f},{mx[2]:.2f}) mats={[m.name for m in o.data.materials][:4]}")
allbb = []
for o in objs:
    if o.type == 'MESH': allbb += [o.matrix_world @ mathutils.Vector(c) for c in o.bound_box]
mn = [min(v[i] for v in allbb) for i in range(3)]; mx = [max(v[i] for v in allbb) for i in range(3)]
print("SCENE BOUNDS", mn, mx, "size", [mx[i]-mn[i] for i in range(3)])
print("UNITS", bpy.context.scene.unit_settings.system, bpy.context.scene.unit_settings.scale_length)
