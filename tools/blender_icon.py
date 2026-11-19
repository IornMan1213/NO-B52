"""
The B-52's aircraft icon (the aircraft menu and the map use the definition's mapIcon), rendered from the model
itself, in the style of the stock icons: a white top-down silhouette, nose up, on transparent, 256 px.
  blender -b blender/out/B52_parts.blend --python tools/blender_icon.py -- <unity/Mods/B52>
Writes Textures/B52_icon.png. Run after blender_lods.py (it hides the _LoD copies). Gear, the cockpit interior,
engine internals and the wing pylons (shown only with pylon weapons) are left out.
Blender frame: +X right, +Y forward, +Z up.
"""
import bpy, os, sys, mathutils
from mathutils import Vector

out_dir = sys.argv[sys.argv.index('--') + 1] if '--' in sys.argv else os.path.join(os.path.dirname(bpy.data.filepath), '..', '..', 'unity', 'Mods', 'B52')
OUT = os.path.join(out_dir, 'Textures', 'B52_icon.png')
SIZE, MARGIN = 256, 10

skip = ('gear_', 'wheel_', 'tlink', 'wellBox', 'cockpit_int', 'nozzle_', 'innerShell', 'hsabPylon', 'bombBay')
inside = set(bpy.data.objects['cockpit_int'].children_recursive) if 'cockpit_int' in bpy.data.objects else set()
shown = []
for o in bpy.data.objects:
    if o.type != 'MESH': continue
    hide = o.name.endswith('_LoD') or o.name.startswith(skip) or o in inside
    o.hide_render = hide
    if not hide: shown.append(o)

pts = [o.matrix_world @ Vector(c) for o in shown for c in o.bound_box]
lo = Vector((min(p.x for p in pts), min(p.y for p in pts))); hi = Vector((max(p.x for p in pts), max(p.y for p in pts)))
span = max(hi.x - lo.x, hi.y - lo.y)

sc = bpy.context.scene
sc.render.engine = 'BLENDER_WORKBENCH'
sc.render.resolution_x = sc.render.resolution_y = 1024
sc.render.film_transparent = True
sc.render.image_settings.file_format = 'PNG'; sc.render.image_settings.color_mode = 'RGBA'
sh = sc.display.shading; sh.light = 'FLAT'; sh.color_type = 'SINGLE'; sh.single_color = (1, 1, 1)
sh.show_backface_culling = False; sh.show_cavity = False; sh.show_shadows = False
sc.view_settings.view_transform = 'Standard'
cam_data = bpy.data.cameras.new('icon'); cam_data.type = 'ORTHO'
cam_data.ortho_scale = span * 1024 / (1024 - 2 * MARGIN * 4)
cam = bpy.data.objects.new('icon', cam_data); sc.collection.objects.link(cam); sc.camera = cam
cam.location = Vector(((lo.x + hi.x) / 2, (lo.y + hi.y) / 2, 50))
cam.rotation_euler = (0, 0, 0)                     # looking straight down, +Y (the nose) up the image
tmp = OUT + '.big.png'
sc.render.filepath = tmp
bpy.ops.render.render(write_still=True)

# 1024 -> 256: white everywhere, the render's alpha as the shape
img = bpy.data.images.load(tmp)
img.scale(SIZE, SIZE)
px = list(img.pixels)
for i in range(0, len(px), 4):
    px[i] = px[i + 1] = px[i + 2] = 1.0
img.pixels = px
img.filepath_raw = OUT; img.file_format = 'PNG'; img.save()
os.remove(tmp)
print(f'ICON {OUT}: {SIZE} px, span {span:.1f} m')
