"""
Ambient occlusion for the exterior skin, baked onto the atlas UVs, so wing roots, pylons, the belly under the wings
and the gaps between parts get soft contact shadowing instead of the flat lighting a solid-white AO map gives.
Run after blender_atlas.py (the skin has its atlas UVs by then):
  blender -b blender/out/B52_parts.blend --python tools/blender_ao.py -- <unity/Mods/B52>
Writes <dir>/Textures/B52_atlas_ao.png (2048 px, linear grey). B52Builder.ConvertMaterials puts it in the skin
material's AO slot. Only the skinned airframe takes part: the gear, doors, cockpit interior and pylons are left out
(they move, sit inside, or only show with pylon weapons). Parts that share texture space (inner and outer pods on one
side) get the average of both.
"""
import bpy, os, sys

OUT = sys.argv[sys.argv.index('--') + 1] if '--' in sys.argv else os.path.join(os.path.dirname(bpy.data.filepath), 'ao')
SIZE, SAMPLES, DISTANCE = 2048, 48, 2.5        # px, rays per texel, metres of occlusion reach
FLOOR = 0.45                                   # darkest the AO map goes: the game adds its own lighting on top

skin = bpy.data.materials.get('B52_Skin')
if skin is None: raise SystemExit('no B52_Skin material: run blender_atlas.py first')
skip = ('gear_', 'wheel_', 'tlink', 'gearDoorPanel_', 'outDoor', 'wellBox', 'cockpit_int', 'spoilerPanels',
        'hsabPylon', 'fan_')             # pylons only show with pylon weapons; fans spin
objs = [o for o in bpy.data.objects
        if o.type == 'MESH' and not o.name.startswith(skip) and any(m == skin for m in o.data.materials)]
for o in bpy.data.objects:
    o.hide_render = o.type == 'MESH' and o not in objs           # occluders = the skinned airframe only
    o.select_set(False)

img = bpy.data.images.new('B52_atlas_ao', SIZE, SIZE, alpha=False, float_buffer=False, is_data=True)
img.generated_color = (1, 1, 1, 1)
nodes = skin.node_tree.nodes
tex = nodes.new('ShaderNodeTexImage'); tex.image = img
nodes.active = tex

scn = bpy.context.scene
scn.render.engine = 'CYCLES'
scn.cycles.device = 'CPU'
scn.cycles.samples = SAMPLES
if scn.world is None: scn.world = bpy.data.worlds.new('World')
scn.world.light_settings.distance = DISTANCE
for o in objs: o.select_set(True)
bpy.context.view_layer.objects.active = objs[0]
bpy.ops.object.bake(type='AO', margin=12, margin_type='EXTEND', use_clear=True)

# Lift the floor so AO shades rather than blackens, then save as a linear grey PNG.
px = list(img.pixels)
for i in range(0, len(px), 4):
    v = FLOOR + (1.0 - FLOOR) * px[i]
    px[i] = px[i + 1] = px[i + 2] = v
img.pixels = px
os.makedirs(os.path.join(OUT, 'Textures'), exist_ok=True)
path = os.path.join(OUT, 'Textures', 'B52_atlas_ao.png')
img.filepath_raw = path; img.file_format = 'PNG'; img.save()
nodes.remove(tex)
print(f'AO baked {len(objs)} parts at {SIZE} px, {SAMPLES} samples -> {path}')
