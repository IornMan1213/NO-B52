# Render preview views of the open .blend. Args after --: outdir [hide_csv]
import bpy, sys, math, mathutils, os
args = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
out = args[0]; os.makedirs(out, exist_ok=True)
hide = args[1].split(',') if len(args) > 1 and args[1] else []
for o in bpy.data.objects:
    if o.name in hide: o.hide_render = True
sc = bpy.context.scene
sc.render.engine = 'BLENDER_EEVEE' if 'BLENDER_EEVEE' in [e.identifier for e in bpy.types.RenderSettings.bl_rna.properties['engine'].enum_items] else 'BLENDER_EEVEE_NEXT'
sc.render.resolution_x, sc.render.resolution_y = 1280, 720
w = bpy.data.worlds.new('w') if not sc.world else sc.world
sc.world = w; w.use_nodes = True
bg = w.node_tree.nodes.get('Background'); bg.inputs[0].default_value = (0.45, 0.5, 0.55, 1); bg.inputs[1].default_value = 1.2
sun = bpy.data.objects.new('sun', bpy.data.lights.new('sun', 'SUN')); sun.data.energy = 4
sun.rotation_euler = (math.radians(50), 0, math.radians(30)); sc.collection.objects.link(sun)
cam = bpy.data.objects.new('cam', bpy.data.cameras.new('cam')); sc.collection.objects.link(cam); sc.camera = cam
cam.data.lens = 35
views = {'front34': (45, -60, 25), 'top': (0, 0, 90), 'side': (75, 0, 3), 'rear34': (-40, 50, 15), 'under': (30, -30, -25), 'nose': (6, -40, 4)}
target = mathutils.Vector((0, 3, 0))
for n, (x, y, z) in views.items():
    d = 70 if n != 'nose' else 1
    pos = mathutils.Vector((x, y, z)) if n != 'top' else mathutils.Vector((0, 3, 75))
    if n == 'nose': pos = mathutils.Vector((7, 34, 2)); tgt = mathutils.Vector((0, 24, 0))
    else: tgt = target
    cam.location = pos
    cam.rotation_euler = (tgt - pos).to_track_quat('-Z', 'Y').to_euler()
    if n == 'top': cam.rotation_euler = (0, 0, 0)
    sc.render.filepath = os.path.join(out, n + '.png')
    bpy.ops.render.render(write_still=True)
print('RENDERED', out)
