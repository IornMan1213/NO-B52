"""Builds the Heavy Aircraft Hangar from scratch and exports it for Unity.
Run: blender -b --factory-startup --python tools/blender_hangar.py -- <unity_mod_folder>
     (textures first: python tools/make_hangar_textures.py)

Axes as in the B-52 pipeline: Blender +Y = front (door side) -> Unity +Z, Blender +Z = up.
The floor top is at z = 0 and the root sits at the floor centre.

Size: 114 m wide x 78 m deep, 22 m eaves, 27 m ridge. Door opening 84 m x 17 m, closed by three telescoping
leaves per side on parallel rails that stack into 14 m pockets in front of the corner pillars. That fits a
B-52 (56.4 m span, 12.4 m tall) with 13.8 m each side and 4.6 m over the fin, and anything smaller.
"""
import bpy, bmesh, math, os, sys
from mathutils import Vector

args = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
OUT = args[0] if args else os.path.join(os.path.dirname(__file__), '..', 'unity', 'Mods', 'HeavyHangar')
TEX = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', 'blender', 'tex_hangar'))
os.makedirs(os.path.join(OUT, 'Textures'), exist_ok=True)

# ---- dimensions (metres) -- keep in sync with HangarBuilder.cs and plugin/HeavyHangar/Placement.cs
W2, D2 = 57.0, 39.0            # half width, half depth (outer faces)
EAVE, RIDGE = 22.0, 27.0
WALL = 0.6
DOOR_W2, DOOR_H = 42.0, 17.0   # half width and height of the opening
LEAF_W, LEAF_H, LEAF_T = 14.4, 17.4, 0.4
RAILS = (40.6, 40.0, 39.4)     # leaf 0 (inner, travels furthest) on the outermost rail
APRON_W2, APRON_LEN = 48.0, 46.0
FOUND = 6.0                    # foundation skirt depth below the floor, hides sloping ground
FRAMES = (-32.5, -19.5, -6.5, 6.5, 19.5, 32.5)
TILE = 8.0                     # metres per texture repeat

bpy.ops.wm.read_factory_settings(use_empty=True)


def mat(name, tex, emit=0.0):
    m = bpy.data.materials.new(name); m.use_nodes = True
    nt = m.node_tree; bsdf = nt.nodes['Principled BSDF']
    img = nt.nodes.new('ShaderNodeTexImage'); img.image = bpy.data.images.load(os.path.join(TEX, tex))
    nt.links.new(img.outputs['Color'], bsdf.inputs['Base Color'])
    bsdf.inputs['Roughness'].default_value = 0.8
    if emit:
        nt.links.new(img.outputs['Color'], bsdf.inputs['Emission Color'])
        bsdf.inputs['Emission Strength'].default_value = emit
    return m


M = {
    'wall': mat('HH_Wall', 'hangar_wall.png'), 'roof': mat('HH_Roof', 'hangar_roof.png'),
    'floor': mat('HH_Floor', 'hangar_floor.png'), 'door': mat('HH_Door', 'hangar_door.png'),
    'beam': mat('HH_Beam', 'hangar_beam.png'), 'lamp': mat('HH_Lamp', 'hangar_lamp.png', emit=4.0),
    'paint': mat('HH_Paint', 'hangar_paint.png'),
}
MAT_ORDER = ['wall', 'roof', 'floor', 'beam', 'lamp', 'paint']


def box_uv(bm, uv, tile=TILE):
    """World-aligned box projection: each face takes the plane its normal points along."""
    for f in bm.faces:
        n = f.normal
        ax = max(range(3), key=lambda i: abs(n[i]))
        for l in f.loops:
            p = l.vert.co
            if ax == 2: l[uv].uv = (p.x / tile, p.y / tile)
            elif ax == 0: l[uv].uv = (p.y / tile, p.z / tile)
            else: l[uv].uv = (p.x / tile, p.z / tile)


class Builder:
    """Collects quads per material into one bmesh, then makes one object."""

    def __init__(self):
        self.bm = bmesh.new(); self.uv = self.bm.loops.layers.uv.new('UVMap')

    def quad(self, pts, m):
        vs = [self.bm.verts.new(p) for p in pts]
        f = self.bm.faces.new(vs); f.material_index = MAT_ORDER.index(m)
        return f

    def box(self, lo, hi, m):
        x0, y0, z0 = lo; x1, y1, z1 = hi
        c = [(x0, y0, z0), (x1, y0, z0), (x1, y1, z0), (x0, y1, z0), (x0, y0, z1), (x1, y0, z1), (x1, y1, z1), (x0, y1, z1)]
        for idx in ((0, 3, 2, 1), (4, 5, 6, 7), (0, 1, 5, 4), (2, 3, 7, 6), (1, 2, 6, 5), (3, 0, 4, 7)):
            self.quad([c[i] for i in idx], m)

    def prism_xz(self, outline, y0, y1, m):
        """Convex polygon in the x-z plane, extruded from y0 to y1 (gable ends)."""
        a = [self.bm.verts.new((x, y0, z)) for x, z in outline]
        b = [self.bm.verts.new((x, y1, z)) for x, z in outline]
        self.bm.faces.new(list(reversed(a))).material_index = MAT_ORDER.index(m)
        self.bm.faces.new(b).material_index = MAT_ORDER.index(m)
        n = len(outline)
        for i in range(n):
            j = (i + 1) % n
            self.bm.faces.new((a[i], a[j], b[j], b[i])).material_index = MAT_ORDER.index(m)

    def finish(self, name, parent=None):
        # every face is wound outward by construction (open roof quads would confuse recalc_face_normals)
        box_uv(self.bm, self.uv)
        me = bpy.data.meshes.new(name); self.bm.to_mesh(me); self.bm.free()
        for k in MAT_ORDER: me.materials.append(M[k])
        ob = bpy.data.objects.new(name, me); bpy.context.scene.collection.objects.link(ob)
        ob.parent = parent
        return ob


def arch_z(x, under=0.0):
    t = max(-1.0, min(1.0, x / W2))
    return EAVE + (RIDGE - EAVE) * (1 - t * t) - under


SEG = 24
XS = [-W2 + 2 * W2 * i / SEG for i in range(SEG + 1)]

root = bpy.data.objects.new('HeavyHangar', None); bpy.context.scene.collection.objects.link(root)
body = Builder()

# floor slab, foundation skirt and apron (one piece of concrete)
body.box((-W2, -D2, -FOUND), (W2, D2, 0), 'floor')
body.box((-APRON_W2, D2, -FOUND), (APRON_W2, D2 + APRON_LEN, 0), 'floor')
# taxi lead-in line
body.box((-0.25, -30, 0.0), (0.25, D2 + APRON_LEN - 1, 0.03), 'paint')

# side walls and back wall
body.box((-W2, -D2, 0), (-W2 + WALL, D2, EAVE), 'wall')
body.box((W2 - WALL, -D2, 0), (W2, D2, EAVE), 'wall')
body.box((-W2, -D2, 0), (W2, -D2 + WALL, EAVE), 'wall')
# front: corner pillars beside the opening, header above it
for s in (-1, 1):
    lo, hi = sorted((s * DOOR_W2, s * W2))
    body.box((lo, D2 - WALL, 0), (hi, D2, EAVE), 'wall')
body.box((-DOOR_W2, D2 - WALL, DOOR_H), (DOOR_W2, D2, EAVE), 'wall')
# gables above the eaves (front and back)
# outline left to right along the arch; the prism closes it along the eave line. Winding gives +y on the y1 face.
gable = [(x, arch_z(x)) for x in XS]
for y0, y1 in ((-D2, -D2 + WALL), (D2 - WALL, D2)):
    body.prism_xz(gable, y0, y1, 'wall')

# arched roof shell with a 0.5 m overhang front and back
T = 0.4
for i in range(SEG):
    xa, xb = XS[i], XS[i + 1]
    za, zb = arch_z(xa), arch_z(xb)
    ya, yb = -D2 - 0.5, D2 + 0.5
    body.quad([(xa, ya, za), (xb, ya, zb), (xb, yb, zb), (xa, yb, za)], 'roof')                     # top
    body.quad([(xa, yb, za - T), (xb, yb, zb - T), (xb, ya, zb - T), (xa, ya, za - T)], 'roof')     # underside
    body.quad([(xa, ya, za - T), (xb, ya, zb - T), (xb, ya, zb), (xa, ya, za)], 'roof')             # back edge
    body.quad([(xa, yb, za), (xb, yb, zb), (xb, yb, zb - T), (xa, yb, za - T)], 'roof')             # front edge

# portal frames: columns at the side walls and arched rafters under the roof
for y in FRAMES:
    for s in (-1, 1):
        x = s * (W2 - WALL - 0.4)
        body.box((x - 0.4, y - 0.4, 0), (x + 0.4, y + 0.4, EAVE - 0.4), 'beam')
    for i in range(SEG):
        xa, xb = XS[i], XS[i + 1]
        if abs(xa) > W2 - WALL - 0.8 and abs(xb) > W2 - WALL - 0.8:
            continue
        za, zb = arch_z(xa, T), arch_z(xb, T)
        body.quad([(xa, y - 0.4, za - 1.4), (xb, y - 0.4, zb - 1.4), (xb, y - 0.4, zb), (xa, y - 0.4, za)], 'beam')   # faces -y
        body.quad([(xa, y + 0.4, za), (xb, y + 0.4, zb), (xb, y + 0.4, zb - 1.4), (xa, y + 0.4, za - 1.4)], 'beam')   # faces +y
        body.quad([(xa, y - 0.4, za - 1.4), (xa, y + 0.4, za - 1.4), (xb, y + 0.4, zb - 1.4), (xb, y - 0.4, zb - 1.4)], 'beam')

# ceiling lamps between the frames: their own mesh, so HangarLighting can switch the glow with the lights
lamps = Builder()
LAMPS = []
for y in ((FRAMES[i] + FRAMES[i + 1]) / 2 for i in range(len(FRAMES) - 1)):
    for x in (-24.0, 0.0, 24.0):
        z = arch_z(x, T) - 2.0
        lamps.box((x - 1.5, y - 0.6, z - 0.2), (x + 1.5, y + 0.6, z), 'lamp')
        LAMPS.append((x, y, z - 0.5))
hangar_body = body.finish('hangar_body', root)
lamps.finish('hangar_lamps', root)
# door warning lamp above the opening (lit while the doors move)
dl = Builder(); dl.box((-1.2, D2, DOOR_H + 0.8), (1.2, D2 + 0.5, DOOR_H + 1.6), 'lamp'); dl.finish('doorLamp', root)

# door leaves: separate objects, origin at the bottom centre of each leaf
for side, s in (('R', 1), ('L', -1)):
    for i in range(3):
        cx = s * (7.0 + 14.0 * i)
        y = RAILS[i]
        b = Builder()
        b.box((-LEAF_W / 2, -LEAF_T / 2, 0.05), (LEAF_W / 2, LEAF_T / 2, LEAF_H), 'wall')
        ob = b.finish(f'door_{side}{i}', root)
        me = ob.data
        me.materials[0] = M['door']
        # front and back faces show the whole leaf texture once
        uvl = me.uv_layers.active.data
        for poly in me.polygons:
            if abs(poly.normal.y) > 0.9:
                for li in poly.loop_indices:
                    co = me.vertices[me.loops[li].vertex_index].co
                    u = (co.x + LEAF_W / 2) / LEAF_W
                    uvl[li].uv = (u if poly.normal.y > 0 else 1 - u, co.z / LEAF_H)
        ob.location = (cx, y, 0)

# markers read by HangarBuilder
def empty(name, loc):
    e = bpy.data.objects.new(name, None); bpy.context.scene.collection.objects.link(e)
    e.parent = root; e.location = loc
    return e

empty('spawnPoint', (0.0, -6.0, 0.0))          # B-52 origin; nose +Y toward the door
for k, p in enumerate(LAMPS):
    if k % 2 == 0: empty(f'light_{k // 2}', p)  # every other lamp gets a real light
empty('doorLight', (0.0, D2 + 0.6, DOOR_H + 1.2))

# export
for img in bpy.data.images:
    if img.filepath:
        src = bpy.path.abspath(img.filepath)
        dst = os.path.join(OUT, 'Textures', os.path.basename(src))
        if os.path.abspath(src) != os.path.abspath(dst):
            import shutil; shutil.copy2(src, dst)
        img.filepath = dst
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(os.path.dirname(__file__), '..', 'blender', 'out', 'HeavyHangar.blend'))
bpy.ops.export_scene.fbx(filepath=os.path.join(OUT, 'HeavyHangar.fbx'), use_selection=False, object_types={'MESH', 'EMPTY'},
                         bake_space_transform=False, apply_scale_options='FBX_SCALE_UNITS', add_leaf_bones=False,
                         mesh_smooth_type='FACE', path_mode='RELATIVE', use_custom_props=False)
print('HANGAR EXPORTED', os.path.join(OUT, 'HeavyHangar.fbx'), len(bpy.data.objects), 'objects,',
      len(hangar_body.data.polygons), 'body faces')
