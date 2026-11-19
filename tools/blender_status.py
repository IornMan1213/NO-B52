"""
The B-52J's cockpit damage display (the game's StatusDisplay): a top view of the aircraft and one mask per part.

Run:  blender -b blender/out/B52_parts.blend --python tools/blender_status.py -- <out_dir>

Writes <out_dir>/status_<part>.png (white where the part is, transparent elsewhere: the game tints each part yellow to
red as it takes damage and leaves it invisible while it's healthy) and status_bg.png (the whole aircraft, its outline
and a dim fill), all 512 x 512 in the same frame, nose up, so the masks lie exactly over the background. The parts are
the B-52's flight-model parts by name, which is how the game links each mask to its part (PartStatusDisplay).
"""
import bpy, sys, os
from mathutils import Vector

ARGS = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
OUT = ARGS[0] if ARGS else os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'unity', 'Mods', 'B52', 'Textures', 'Status')
os.makedirs(OUT, exist_ok=True)
SIZE = 512

PARTS = ['B52', 'fuselage_F', 'cockpit', 'fuselage_R', 'tail', 'rudder', 'hstab_L', 'hstab_R', 'elevator_L', 'elevator_R',
         'wingroot_L', 'wingroot_R', 'wing1_L', 'wing1_R', 'wing2_L', 'wing2_R', 'wingtip_L', 'wingtip_R',
         'flap1_L', 'flap1_R', 'flap2_L', 'flap2_R', 'spoilers_L', 'spoilers_R', 'pod1_L', 'pod1_R', 'pod2_L', 'pod2_R']

scene = bpy.context.scene
objs = {n: bpy.data.objects.get(n) for n in PARTS}
missing = [n for n, o in objs.items() if o is None or o.type != 'MESH']
if missing: print('STATUS missing parts:', missing)

# Frame: the whole aircraft from above, square, with a small margin.
lo = Vector((1e9, 1e9, 1e9)); hi = Vector((-1e9, -1e9, -1e9))
for o in objs.values():
    if o is None or o.type != 'MESH': continue
    for c in o.bound_box:
        w = o.matrix_world @ Vector(c)
        lo = Vector((min(lo[i], w[i]) for i in range(3))); hi = Vector((max(hi[i], w[i]) for i in range(3)))
centre = (lo + hi) / 2
extent = max(hi.x - lo.x, hi.y - lo.y) * 1.06

cam_data = bpy.data.cameras.new('status_cam'); cam_data.type = 'ORTHO'; cam_data.ortho_scale = extent
cam = bpy.data.objects.new('status_cam', cam_data); scene.collection.objects.link(cam)
cam.location = (centre.x, centre.y, hi.z + 50); cam.rotation_euler = (0, 0, 0)    # looking down, +Y (nose) up in the image
cam_data.clip_end = 500
scene.camera = cam

scene.render.engine = 'BLENDER_WORKBENCH'
scene.display.shading.light = 'FLAT'
scene.display.shading.color_type = 'SINGLE'
scene.display.shading.single_color = (1, 1, 1)
scene.display.shading.show_object_outline = False
scene.render.film_transparent = True
scene.render.resolution_x = scene.render.resolution_y = SIZE
scene.render.image_settings.file_format = 'PNG'
scene.render.image_settings.color_mode = 'RGBA'
try:
    scene.view_settings.view_transform = 'Standard'
except Exception:
    pass

def only(names):
    for o in scene.objects:
        o.hide_render = not (o.name in names or o == cam)

for n in PARTS:
    if objs[n] is None: continue
    only({n})
    scene.render.filepath = os.path.join(OUT, f'status_{n}.png')
    bpy.ops.render.render(write_still=True)

only(set(n for n in PARTS if objs[n] is not None))
scene.render.filepath = os.path.join(OUT, 'status_all.png')
bpy.ops.render.render(write_still=True)
print('STATUS', len(PARTS) - len(missing), 'part masks and the outline ->', OUT, f'(frame {extent:.1f} m)')
