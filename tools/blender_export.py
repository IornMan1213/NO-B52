"""Export B52_parts.blend to Unity FBX: nose to Blender -Y (Unity +Z after FBX axis conversion), root at identity.
Run: blender -b blender/out/B52_parts.blend --python tools/blender_export.py -- <unity_mod_folder>"""
import bpy, sys, os, shutil
from mathutils import Matrix
out = sys.argv[sys.argv.index('--') + 1]
os.makedirs(os.path.join(out, 'Textures'), exist_ok=True)
R = Matrix.Rotation(3.14159265358979, 4, 'Z')
root = bpy.data.objects['B52']
order = []
def walk(o):
    order.append(o); [walk(c) for c in o.children]
walk(root)
worlds = {o.name: R @ o.matrix_world for o in order}
for o in order:                                   # parents first
    if o.parent: o.matrix_parent_inverse = Matrix.Identity(4)
for o in order:
    if o.parent: o.matrix_world = worlds[o.name]
    else:
        if o.type == 'MESH': o.data.transform(R)
        o.matrix_world = Matrix.Identity(4)
    bpy.context.view_layer.update()
# copy textures beside the FBX and point images at them
for img in bpy.data.images:
    if img.filepath:
        src = bpy.path.abspath(img.filepath)
        if os.path.exists(src):
            dst = os.path.join(out, 'Textures', os.path.basename(src)); shutil.copy2(src, dst); img.filepath = dst
        elif img.packed_file:
            dst = os.path.join(out, 'Textures', bpy.path.clean_name(img.name) + '.png'); img.filepath_raw = dst; img.save()
bpy.ops.export_scene.fbx(filepath=os.path.join(out, 'B52.fbx'), use_selection=False, object_types={'MESH', 'EMPTY'},
                         bake_space_transform=True, apply_scale_options='FBX_SCALE_UNITS', add_leaf_bones=False,
                         mesh_smooth_type='FACE', path_mode='RELATIVE', use_custom_props=False)
print('EXPORTED', os.path.join(out, 'B52.fbx'), len(order), 'objects')
