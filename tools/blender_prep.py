"""
B-52 prep: turn bohmerang's single-piece B-52 into Nuclear Option flight parts.

Run:  blender -b source_assets/bohmerang/source_blend/source/B-52.blend --python tools/blender_prep.py -- <out_dir>

Blender frame of the source: +Y = nose, +Z = up, +X = right wing, metres.
Output: <out_dir>/B52_parts.blend and <out_dir>/B52.fbx (Unity: +Z forward, +Y up).

Every flight part becomes its own object, parented in the same tree the game's bombers use. Control surfaces
get their origin on the hinge line with local +X along the hinge, because the game rotates them about local X
(ControlSurfaceJob_Math: restingRotation * AngleAxis(angle, Vector3.right)).
"""
import bpy, bmesh, sys, os, math
from mathutils import Vector, Matrix

ARGS = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
OUT = ARGS[0] if ARGS else os.path.join(os.path.dirname(bpy.data.filepath), 'out')
os.makedirs(OUT, exist_ok=True)

# Real B-52H span 56.39 m; the source model spans 55.98 m.
SCALE = 56.39 / 55.98

# Spanwise stations (|x|, source units) between wing parts. Engine pods are centred at |x| 11.4 and 18.6;
# the external tanks sit at |x| ~24.2.
ST_ROOT, ST_INNER, ST_OUTER = 1.8, 7.5, 15.0
ST_TIP = 22.2

# --------------------------------------------------------------------------------------------- helpers

def mesh_objects():
    return [o for o in bpy.data.objects if o.type == 'MESH']


def apply_all_transforms():
    for o in mesh_objects():
        o.hide_set(False)
    bpy.ops.object.select_all(action='DESELECT')
    for o in bpy.data.objects:
        o.select_set(True)
    bpy.context.view_layer.objects.active = mesh_objects()[0]
    # Unparent with transform kept, then bake everything into the vertices.
    for o in bpy.data.objects:
        if o.parent:
            mw = o.matrix_world.copy(); o.parent = None; o.matrix_world = mw
    for o in mesh_objects():
        o.data = o.data.copy() if o.data.users > 1 else o.data
        o.data.transform(Matrix.Scale(SCALE, 4) @ o.matrix_world)
        o.matrix_world = Matrix.Identity(4)


def new_obj(name, bm, mats):
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me); bm.free()
    for m in mats:
        me.materials.append(m)
    ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    return ob


def bm_of(o):
    bm = bmesh.new(); bm.from_mesh(o.data); return bm


def loose_parts(o):
    """Split an object's mesh into connected components. Returns list of (bmesh, centroid)."""
    src = bm_of(o)
    src.verts.ensure_lookup_table()
    seen, comps = set(), []
    for v in src.verts:
        if v.index in seen:
            continue
        stack, comp = [v], []
        while stack:
            a = stack.pop()
            if a.index in seen:
                continue
            seen.add(a.index); comp.append(a.index)
            stack.extend(e.other_vert(a) for e in a.link_edges)
        comps.append(set(comp))
    out = []
    for comp in comps:
        bm = src.copy()
        bm.verts.ensure_lookup_table()
        bmesh.ops.delete(bm, geom=[v for v in bm.verts if v.index not in comp], context='VERTS')
        c = sum((v.co for v in bm.verts), Vector()) / max(1, len(bm.verts))
        out.append((bm, c))
    src.free()
    return out


def merge_into(buckets, key, bm):
    """Append bmesh geometry (keeping material indices) into buckets[key]."""
    me = bpy.data.meshes.new('tmp'); bm.to_mesh(me); bm.free()
    if key not in buckets:
        buckets[key] = bmesh.new()
    buckets[key].from_mesh(me)
    bpy.data.meshes.remove(me)


def bisect(bm, co, no):
    """Return (inner, outer) copies of bm split by plane (co, no); outer is the +no side. Caps the cut."""
    halves = []
    for keep_pos in (False, True):
        b = bm.copy()
        geom = b.verts[:] + b.edges[:] + b.faces[:]
        res = bmesh.ops.bisect_plane(b, geom=geom, plane_co=co, plane_no=no, clear_inner=keep_pos, clear_outer=not keep_pos)
        cut_edges = [e for e in res['geom_cut'] if isinstance(e, bmesh.types.BMEdge)]
        if cut_edges:
            try:
                bmesh.ops.holes_fill(b, edges=cut_edges, sides=0)
            except Exception:
                pass
        halves.append(b)
    return halves[0], halves[1]


def region(bm, test):
    """Split bm into (matching, rest) by a per-face predicate on the face centre."""
    a, b = bm.copy(), bm.copy()
    a.faces.ensure_lookup_table(); b.faces.ensure_lookup_table()
    bmesh.ops.delete(a, geom=[f for f in a.faces if not test(f.calc_center_median())], context='FACES')
    bmesh.ops.delete(b, geom=[f for f in b.faces if test(f.calc_center_median())], context='FACES')
    return a, b


def set_origin(o, point, x_axis=None):
    """Move the object origin to `point`. If x_axis is given, rotate the object frame so local +X runs along it
    (mesh stays put in world space)."""
    rot = Matrix.Identity(3)
    if x_axis is not None:
        x = x_axis.normalized()
        ref = Vector((0, 0, 1)) if abs(x.z) < 0.9 else Vector((-1, 0, 0))
        z = (ref - x * ref.dot(x)).normalized()
        y = z.cross(x)
        rot = Matrix((x, y, z)).transposed()
    m = Matrix.Translation(point) @ rot.to_4x4()
    o.data.transform(m.inverted())
    o.matrix_world = m


# --------------------------------------------------------------------------------------------- main

apply_all_transforms()
S = SCALE
ob = {o.name: o for o in mesh_objects()}
mats_of = {n: list(o.data.materials) for n, o in ob.items()}

# One material list for every generated object keeps material indices valid when merging.
ALL_MATS = []
for o in mesh_objects():
    for m in o.data.materials:
        if m not in ALL_MATS:
            ALL_MATS.append(m)


def remap_to_all(o):
    """Rewrite face material indices of o to index into ALL_MATS; return a bmesh."""
    bm = bm_of(o)
    local = list(o.data.materials)
    for f in bm.faces:
        if f.material_index < len(local) and local[f.material_index] in ALL_MATS:
            f.material_index = ALL_MATS.index(local[f.material_index])
    return bm


parts = {}  # name -> bmesh


def add(name, bm):
    tmp = bpy.data.meshes.new('t'); bm.to_mesh(tmp); bm.free()
    parts.setdefault(name, bmesh.new()).from_mesh(tmp)
    bpy.data.meshes.remove(tmp)


def side(x):
    return 'L' if x < 0 else 'R'


# ---- Fuselage: cockpit / forward / centre / rear, plus bomb bay doors cut from the belly.
fus = remap_to_all(ob['Fuselage'])
BAY_Y0, BAY_Y1 = 1.4 * S, 11.6 * S       # between the forward and aft main gear trucks
BAY_HALF_W = 0.85 * S
belly = lambda c: abs(c.x) < BAY_HALF_W and BAY_Y0 < c.y < BAY_Y1 and c.z < -1.95 * S
bay, fus = region(fus, belly)
bayL, bayR = region(bay, lambda c: c.x < 0)
add('bayDoor_L', bayL); add('bayDoor_R', bayR)

rest, cockpit = bisect(fus, Vector((0, 19.2 * S, 0)), Vector((0, 1, 0)))
rest, fwd = bisect(rest, Vector((0, 8.5 * S, 0)), Vector((0, 1, 0)))
rear, centre = bisect(rest, Vector((0, -3.4 * S, 0)), Vector((0, 1, 0)))
add('cockpit', cockpit); add('fuselage_F', fwd); add('fuselage', centre); add('fuselage_R', rear)
add('cockpit', remap_to_all(ob['Glass']))
add('cockpit', remap_to_all(ob['EVS']))
add('fuselage_R', remap_to_all(ob['RedLights']))

# ---- Tail
add('tail', remap_to_all(ob['VerticalStabilizer']))
add('rudder', remap_to_all(ob['Rudder']))
hs = remap_to_all(ob['HorizontalStabilizers'])
hsL, hsR = region(hs, lambda c: c.x < 0)
# Elevator = aft 28% of the stabiliser chord. Root chord y -10.27..-18.15, tip TE ~ -17.47 at x 7.8.
for name, b, sgn in (('hstab_L', hsL, -1), ('hstab_R', hsR, 1)):
    p_root = Vector((0, -15.9 * S, 0)); p_tip = Vector((sgn * 7.8 * S, -16.95 * S, 0))
    hinge = (p_tip - p_root).normalized()
    no = hinge.cross(Vector((0, 0, 1))).normalized()
    if no.y > 0:
        no = -no      # point aft
    fixed, elev = bisect(b, p_root, no)
    add(name, fixed); add('elevator_' + name[-1], elev)


# ---- Wings: spanwise stations, then flaps (aft of the 72% chord line on the inboard 2/3).
def wing_split(o, sgn):
    bm = remap_to_all(o)
    sd = 'L' if sgn < 0 else 'R'
    n = Vector((sgn, 0, 0))
    root, rest = bisect(bm, Vector((sgn * ST_INNER * S, 0, 0)), n)
    inner, rest = bisect(rest, Vector((sgn * ST_OUTER * S, 0, 0)), n)
    outer, tip = bisect(rest, Vector((sgn * ST_TIP * S, 0, 0)), n)
    # Flap leading-edge line, from the flap texture region (material Wing4: x 2.4..18.9, y up to 7.1) and the
    # measured trailing edge (y 5.1 at |x| 2.4 → -3.3 at |x| 18.9): the flap chord is ~2.0 m at the root and ~1.7 m outboard.
    a = Vector((sgn * 2.4 * S, 7.0 * S, 0)); b = Vector((sgn * 18.9 * S, -1.6 * S, 0))
    hinge = (b - a).normalized()
    no = hinge.cross(Vector((0, 0, 1))).normalized()
    if no.y > 0:
        no = -no
    for nm, piece, flapname in (('wingroot', root, 'flap1'), ('wing1', inner, 'flap2'), ('wing2', outer, None)):
        if flapname:
            fixed, flap = bisect(piece, a, no)
            add(f'{nm}_{sd}', fixed); add(f'{flapname}_{sd}', flap)
        else:
            add(f'{nm}_{sd}', piece)
    add(f'wingtip_{sd}', tip)
    return a, b


hingeL = wing_split(ob['WingLeft'], -1)
hingeR = wing_split(ob['WingRight'], 1)


# ---- Attached loose parts (engines, fans, pylons, antennas, tanks): bin by centroid.
def bin_part(c):
    ax = abs(c.x) / S
    sd = side(c.x)
    if ax > 22.5:
        return f'wingtip_{sd}'                      # external tank + outrigger fairing
    if 9.0 < ax < 14.5 and c.z < 0.6 * S:
        return f'pod1_{sd}'                         # inboard engine pod (2 engines)
    if 16.0 < ax < 21.5 and c.z < 0.9 * S:
        return f'pod2_{sd}'                         # outboard engine pod
    if ax < 2.2:
        y = c.y / S
        if y > 19.2: return 'cockpit'
        if y > 8.5: return 'fuselage_F'
        if y > -3.4: return 'fuselage'
        if y < -9.0 and c.z / S > 1.0: return 'tail'
        return 'fuselage_R'
    if ax < ST_INNER: return f'wingroot_{sd}'
    if ax < ST_OUTER: return f'wing1_{sd}'
    if ax < ST_TIP: return f'wing2_{sd}'
    return f'wingtip_{sd}'


for src in ('Engines', 'EngineBlades', 'RandomStuff', 'RandomStuff2'):
    o = ob[src]
    remapped = remap_to_all(o)
    tmp = bpy.data.meshes.new('tmp'); remapped.to_mesh(tmp); remapped.free()
    o.data = tmp
    for bm, c in loose_parts(o):
        add(bin_part(c), bm)

# ---- Spoilers: 7 panels per wing on the upper surface ahead of the flaps, outboard of the inner pod.
#      Generated as thin plates; one ControlSurface per wing drives them (B52Systems makes them one-way).
SPOILER_MAT = bpy.data.materials.new('B52_SpoilerGray'); SPOILER_MAT.use_nodes = True
SPOILER_MAT.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value = (0.50, 0.52, 0.55, 1)
ALL_MATS.append(SPOILER_MAT)


def wing_top_z(x, y):
    """Highest wing-surface z at (x, y), sampled from the finished wing parts."""
    best = None
    for k, bm in parts.items():
        if not (k.startswith('wing') or k.startswith('flap')):
            continue
        for v in bm.verts:
            if abs(v.co.x - x) < 0.9 * S and abs(v.co.y - y) < 0.9 * S:
                best = v.co.z if best is None else max(best, v.co.z)
    return best if best is not None else 1.0 * S


def make_spoilers(sgn, a, b):
    sd = 'L' if sgn < 0 else 'R'
    bm = bmesh.new()
    hinge = (b - a).normalized()
    fwd = Vector((0, 1, 0))
    n_pan, t0, t1 = 7, 0.33, 0.98          # along the flap line, outboard part only
    for i in range(n_pan):
        s0 = t0 + (t1 - t0) * i / n_pan + 0.004
        s1 = t0 + (t1 - t0) * (i + 1) / n_pan - 0.004
        p0 = a.lerp(b, s0) + fwd * 0.15 * S
        p1 = a.lerp(b, s1) + fwd * 0.15 * S
        chord = 1.05 * S
        quad = [p0, p1, p1 + fwd * chord, p0 + fwd * chord]
        quad = [Vector((q.x, q.y, wing_top_z(q.x, q.y) + 0.012)) for q in quad]
        top = [bm.verts.new(q) for q in quad]
        bot = [bm.verts.new(q - Vector((0, 0, 0.03))) for q in quad]
        f = bm.faces.new(top if sgn > 0 else top[::-1]); f.material_index = ALL_MATS.index(SPOILER_MAT)
        f = bm.faces.new(bot[::-1] if sgn > 0 else bot); f.material_index = ALL_MATS.index(SPOILER_MAT)
        for k in range(4):
            j = (k + 1) % 4
            f = bm.faces.new((top[k], top[j], bot[j], bot[k]) if sgn > 0 else (bot[k], bot[j], top[j], top[k]))
            f.material_index = ALL_MATS.index(SPOILER_MAT)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    add(f'spoilers_{sd}', bm)


make_spoilers(-1, hingeL[0], hingeL[1])
make_spoilers(1, hingeR[0], hingeR[1])

# ---- Build objects
for o in list(bpy.data.objects):
    bpy.data.objects.remove(o)
objs = {}
for name, bm in parts.items():
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)
    if len(bm.verts) == 0:
        print('EMPTY PART', name); bm.free(); continue
    objs[name] = new_obj(name, bm, ALL_MATS)


def centroid(o):
    vs = o.data.vertices
    return sum((v.co for v in vs), Vector()) / max(1, len(vs))


def hinge_of(o, aft_dir):
    """Hinge for a trailing control surface: the forward-most edge line. Returns (point, axis)."""
    vs = [v.co for v in o.data.vertices]
    fwd = -aft_dir
    lead = sorted(vs, key=lambda v: -v.dot(fwd))[:max(4, len(vs) // 6)]
    xs = sorted(lead, key=lambda v: v.x)
    p0, p1 = xs[0], xs[-1]
    axis = (p1 - p0)
    if axis.x < 0:
        axis = -axis
    return (p0 + p1) / 2, axis


# Origins
for name, o in objs.items():
    base = name.split('_')[0]
    if base in ('flap1', 'flap2', 'elevator', 'spoilers'):
        p, axis = hinge_of(o, Vector((0, -1, 0)))
        set_origin(o, p, axis)
    elif base == 'rudder':
        vs = [v.co for v in o.data.vertices]
        lo = min(vs, key=lambda v: v.z); hi = max(vs, key=lambda v: v.z)
        front = max(v.y for v in vs)
        # Rudder hinge: the leading edge, running up the fin (local +X = up along the hinge).
        lead = sorted(vs, key=lambda v: -v.y)[:max(2, len(vs) // 4)]
        p0 = min(lead, key=lambda v: v.z); p1 = max(lead, key=lambda v: v.z)
        set_origin(o, (p0 + p1) / 2, p1 - p0)
    elif base == 'bayDoor':
        vs = [v.co for v in o.data.vertices]
        outer_x = max(vs, key=lambda v: abs(v.x)).x
        top_z = max(v.z for v in vs)
        p = Vector((outer_x, (BAY_Y0 + BAY_Y1) / 2, top_z))
        set_origin(o, p)
    else:
        set_origin(o, centroid(o))

# ---- Hierarchy (child -> parent), FastBomber1 style
PARENT = {
    'cockpit': 'fuselage_F', 'fuselage_F': 'fuselage', 'fuselage_R': 'fuselage', 'tail': 'fuselage_R',
    'rudder': 'tail', 'hstab_L': 'fuselage_R', 'hstab_R': 'fuselage_R', 'elevator_L': 'hstab_L',
    'elevator_R': 'hstab_R', 'bayDoor_L': 'fuselage', 'bayDoor_R': 'fuselage',
}
for sd in 'LR':
    PARENT.update({
        f'wingroot_{sd}': 'fuselage', f'wing1_{sd}': f'wingroot_{sd}', f'wing2_{sd}': f'wing1_{sd}',
        f'wingtip_{sd}': f'wing2_{sd}', f'flap1_{sd}': f'wingroot_{sd}', f'flap2_{sd}': f'wing1_{sd}',
        f'pod1_{sd}': f'wing1_{sd}', f'pod2_{sd}': f'wing2_{sd}', f'spoilers_{sd}': f'wing1_{sd}',
    })
for child, par in PARENT.items():
    if child in objs and par in objs:
        c = objs[child]; mw = c.matrix_world.copy()
        c.parent = objs[par]; c.matrix_world = mw

# ---- New materials for scratch-built parts
def flat_mat(name, rgb, metal=0.0, rough=0.6):
    m = bpy.data.materials.new(name); m.use_nodes = True
    p = m.node_tree.nodes['Principled BSDF']
    p.inputs['Base Color'].default_value = (*rgb, 1); p.inputs['Metallic'].default_value = metal
    p.inputs['Roughness'].default_value = rough
    return m


M_RUBBER = flat_mat('B52_Tyre', (0.03, 0.03, 0.03), 0.0, 0.9)
M_STRUT = flat_mat('B52_GearMetal', (0.55, 0.56, 0.58), 0.8, 0.35)
M_BAY = flat_mat('B52_BayInterior', (0.16, 0.17, 0.16), 0.2, 0.8)


def prim_obj(name, build, mat, parent=None, origin=None):
    bm = bmesh.new(); build(bm)
    me = bpy.data.meshes.new(name); bm.to_mesh(me); bm.free(); me.materials.append(mat)
    o = bpy.data.objects.new(name, me); bpy.context.scene.collection.objects.link(o)
    if origin is not None:
        o.data.transform(Matrix.Translation(-origin)); o.matrix_world = Matrix.Translation(origin)
    if parent is not None:
        bpy.context.view_layer.update()
        mw = o.matrix_world.copy(); o.parent = parent; o.matrix_world = mw
    bpy.context.view_layer.update()
    return o


def cyl(bm, p0, p1, r, seg=16):
    """Cylinder between points p0 and p1."""
    d = p1 - p0
    rot = d.to_track_quat('Z', 'Y').to_matrix().to_4x4()
    m = Matrix.Translation((p0 + p1) / 2) @ rot
    bmesh.ops.create_cone(bm, cap_ends=True, segments=seg, radius1=r, radius2=r, depth=d.length, matrix=m)


def tyre(bm, centre, axis, r, w):
    cyl(bm, centre - axis * w / 2, centre + axis * w / 2, r, 24)


# Real B-52H gear: four two-wheel main trucks (56x16 tyres, D 1.42 m) in a quadricycle layout, track 2.51 m,
# wheelbase 15.3 m, plus single-wheel outriggers (D 0.81 m) ~45 m apart. Ground plane from the real 12.4 m
# height measured to the fin tip.
FIN_TOP = max((o.matrix_world @ v.co).z for o in [objs['tail']] for v in o.data.vertices)
GROUND = FIN_TOP - 12.40
TYRE_R, TYRE_W = 0.71, 0.41
X_AXIS = Vector((1, 0, 0))
gear_parent = {'FL': 'fuselage_F', 'FR': 'fuselage_F', 'RL': 'fuselage', 'RR': 'fuselage'}
gear_pos = {'FL': (-1.255, 14.2), 'FR': (1.255, 14.2), 'RL': (-1.255, -1.0), 'RR': (1.255, -1.0)}
BELLY = -2.55 * S
for k, (gx, gy) in gear_pos.items():
    gx *= 1.0; gy *= S
    wheel_z = GROUND + TYRE_R
    top = Vector((gx, gy, BELLY + 0.3)); hub = Vector((gx, gy, wheel_z))
    g = prim_obj(f'gear_{k}', lambda bm: cyl(bm, top, hub + Vector((0, 0, 0.35)), 0.13), M_STRUT,
                 objs[gear_parent[k]], top)
    un = prim_obj(f'gear_unsprung_{k}', lambda bm: cyl(bm, hub - X_AXIS * 0.45, hub + X_AXIS * 0.45, 0.09), M_STRUT, g, hub)
    for i, dx in enumerate((-0.24, 0.24)):
        c = hub + X_AXIS * dx
        prim_obj(f'wheel_{k}{i + 1}', lambda bm, c=c: tyre(bm, c, X_AXIS, TYRE_R, TYRE_W), M_RUBBER, un, c)

for sd, sgn in (('L', -1), ('R', 1)):
    ox, oy = sgn * 22.6, -3.4 * S
    wing_under = 0.55 * S
    r = 0.405
    hub = Vector((ox, oy, GROUND + 0.15 + r))
    top = Vector((ox, oy, wing_under))
    g = prim_obj(f'gear_O{sd}', lambda bm: cyl(bm, top, hub + Vector((0, 0, 0.3)), 0.09), M_STRUT, objs[f'wingtip_{sd}'], top)
    un = prim_obj(f'gear_unsprung_O{sd}', lambda bm: cyl(bm, hub - X_AXIS * 0.2, hub + X_AXIS * 0.2, 0.06), M_STRUT, g, hub)
    prim_obj(f'wheel_O{sd}', lambda bm: tyre(bm, hub, X_AXIS, r, 0.25), M_RUBBER, un, hub)


# Bomb bay interior: an open-bottom dark box behind the doors, faces pointing inward.
def bay_box(bm):
    x0, x1 = -BAY_HALF_W, BAY_HALF_W
    y0, y1 = BAY_Y0, BAY_Y1
    z0, z1 = -2.5 * S, -0.7 * S
    v = [bm.verts.new(p) for p in [(x0, y0, z0), (x1, y0, z0), (x1, y1, z0), (x0, y1, z0),
                                     (x0, y0, z1), (x1, y0, z1), (x1, y1, z1), (x0, y1, z1)]]
    for f in [(4, 5, 6, 7), (0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)]:
        bm.faces.new([v[i] for i in f])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bmesh.ops.reverse_faces(bm, faces=bm.faces)


prim_obj('bombBay', bay_box, M_BAY, objs['fuselage'], Vector((0, (BAY_Y0 + BAY_Y1) / 2, -1.6 * S)))
print('GROUND z', GROUND)

# Rename root for clarity in Unity
objs['fuselage'].name = 'B52'

bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT, 'B52_parts.blend'))

# Report
print('PARTS')
for name in sorted(objs):
    o = objs[name]
    vs = [o.matrix_world @ v.co for v in o.data.vertices]
    if not vs:
        print(f'  {name:14} EMPTY'); continue
    mn = [min(v[i] for v in vs) for i in range(3)]; mx = [max(v[i] for v in vs) for i in range(3)]
    print(f'  {name:14} v={len(vs):6} parent={o.parent.name if o.parent else "-":12} '
          f'x[{mn[0]:6.1f},{mx[0]:6.1f}] y[{mn[1]:6.1f},{mx[1]:6.1f}] z[{mn[2]:5.1f},{mx[2]:5.1f}]')
print('DONE', OUT)
