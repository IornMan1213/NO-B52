# Render the flight deck from the pilot's eye and from behind the seats (backface culling on, as in Unity).
import bpy, math, mathutils, os, sys
out = sys.argv[sys.argv.index('--') + 1]
os.makedirs(out, exist_ok=True)
sc = bpy.context.scene
for m in bpy.data.materials:
    m.use_backface_culling = True
eng = [e.identifier for e in bpy.types.RenderSettings.bl_rna.properties['engine'].enum_items]
sc.render.engine = 'BLENDER_EEVEE' if 'BLENDER_EEVEE' in eng else 'BLENDER_EEVEE_NEXT'
sc.render.resolution_x, sc.render.resolution_y = 1280, 720
w = bpy.data.worlds.new('w'); sc.world = w; w.use_nodes = True
w.node_tree.nodes['Background'].inputs[0].default_value = (0.5, 0.65, 0.85, 1)
w.node_tree.nodes['Background'].inputs[1].default_value = 1.5
sun = bpy.data.objects.new('sun', bpy.data.lights.new('sun', 'SUN')); sun.data.energy = 4
sun.rotation_euler = (math.radians(35), 0, math.radians(160)); sc.collection.objects.link(sun)
fill = bpy.data.objects.new('fill', bpy.data.lights.new('fill', 'POINT')); fill.data.energy = 60
fill.location = (0, 24.4, 0.9); sc.collection.objects.link(fill)
cam = bpy.data.objects.new('cam', bpy.data.cameras.new('cam')); sc.collection.objects.link(cam); sc.camera = cam
shots = {
    'eye_L': ((-0.55, 24.45, 0.80), (-0.35, 26.5, 0.15), 20),
    'overview': ((0.35, 23.45, 1.05), (0.0, 25.4, -0.05), 16),
    'quadrant': ((0.25, 24.1, 0.55), (0.0, 24.7, -0.05), 22),
}
for n, (p, t, lens) in shots.items():
    cam.location = mathutils.Vector(p); cam.data.lens = lens; cam.data.clip_start = 0.02
    cam.rotation_euler = (mathutils.Vector(t) - cam.location).to_track_quat('-Z', 'Y').to_euler()
    sc.render.filepath = os.path.join(out, n + '.png'); bpy.ops.render.render(write_still=True)
print('RENDERED', out)
