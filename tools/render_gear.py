import bpy, math, mathutils, os
exec(open(os.path.join(os.path.dirname(os.path.abspath(__file__)), 'blend_render.py')).read().split("views = {")[0])
for n, pos, tgt in [('gear_side', (40, 6, -3), (0, 6, -2.5)), ('gear_front', (12, 45, -2.5), (0, 6, -2.5))]:
    cam.location = mathutils.Vector(pos); cam.rotation_euler = (mathutils.Vector(tgt) - cam.location).to_track_quat('-Z', 'Y').to_euler()
    sc.render.filepath = os.path.join(out, n + '.png'); bpy.ops.render.render(write_still=True)
