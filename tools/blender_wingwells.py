"""
Outrigger (tip gear) wells in the outer wing, run after blender_gear.py:
  blender -b blender/out/B52_parts.blend --python tools/blender_wingwells.py

The tip gear folds inboard along the swept span (about a horizontal axis 35 deg off fore-aft) while the wheel swivels
35 deg on the leg, so leg and wheel stow flat inside the 0.31 m deep outer wing (FOLD / LIFT / SWIVEL below are the
values the Unity builder gives LandingGear; with them the stowed gear clears both skins to a few mm). Here:
  - The whole fold is simulated, and everywhere the gear is inside the wing at any point of it, in plan, is the slot
    (its convex hull, plus a margin). The slot is cut out of the lower skin.
  - The cut-out skin becomes two doors, flush and in the skin's own paint when shut:
      outDoorStrut_<k>_<n>  over the leg: fixed to the leg (the Unity builder hangs it on the gear hinge), so it folds
                            with it and hangs outboard of the oleo when the gear is down, like the real strut doors;
      outDoorWheel_<k>_<n>  over the wide wheel end: hinged on its aft long edge, opened by LandingGear.
    Each has a dark inner face 1 cm inside it.
  - A well box (wellBox_<k>) closes the slot inside the wing: walls from the slot's edge up to a roof just under the
    upper skin, dark like the main wells, drawn from both sides. The wing's own internal faces in the slot are removed.
Blender frame: +X right, +Y forward, +Z up.
"""
import bpy, bmesh, math
from mathutils import Vector, Quaternion
from mathutils.bvhtree import BVHTree

FOLD, LIFT, SWIVEL = 89.75, -0.025, 35.0      # must match B52Builder.BuildGear (outriggers)
STOW_DEG = 55.0                               # stowed leg direction: 55 deg inboard of straight ahead
MARGIN, DOOR_IN, ROOF_GAP = 0.03, 0.01, 0.004
SPLIT_BACK = 0.12                             # strut / wheel door split, this far before the wheel's first entry

def mat(name, rgb):
    m = bpy.data.materials.get(name)
    if m: return m
    m = bpy.data.materials.new(name); m.use_nodes = True
    m.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value = (*rgb, 1)
    return m

M_WELL = mat('B52_BayInterior', (0.16, 0.17, 0.16))


def anc(o):
    r = []
    while o.parent: o = o.parent; r.append(o)
    return r


def world_bm(objs):
    bm = bmesh.new()
    for o in objs:
        t = bmesh.new(); t.from_mesh(o.data); t.transform(o.matrix_world)
        me = bpy.data.meshes.new('_tmp'); t.to_mesh(me); t.free(); bm.from_mesh(me); bpy.data.meshes.remove(me)
    return bm


def hull2d(pts):
    pts = sorted(set(pts))
    def cross(o, a, b): return (a[0] - o[0]) * (b[1] - o[1]) - (a[1] - o[1]) * (b[0] - o[0])
    lo, up = [], []
    for p in pts:
        while len(lo) >= 2 and cross(lo[-2], lo[-1], p) <= 0: lo.pop()
        lo.append(p)
    for p in reversed(pts):
        while len(up) >= 2 and cross(up[-2], up[-1], p) <= 0: up.pop()
        up.append(p)
    return lo[:-1] + up[:-1]                  # counter-clockwise


def simplify(poly, tol=0.02):
    """Drop hull vertices that sit within tol of the line through their neighbours."""
    changed = True
    while changed and len(poly) > 4:
        changed = False
        for i in range(len(poly)):
            a, b, c = Vector(poly[i - 1]), Vector(poly[i]), Vector(poly[(i + 1) % len(poly)])
            ac = c - a
            if ac.length < 1e-6: continue
            if abs(ac.x * (b - a).y - ac.y * (b - a).x) / ac.length < tol:
                poly.pop(i); changed = True; break
    return poly


report = []
for s, side in (('L', -1), ('R', 1)):
    k = 'O' + s
    g = bpy.data.objects[f'gear_{k}']
    un = bpy.data.objects[f'gear_unsprung_{k}']
    wings = [bpy.data.objects[n] for n in (f'wing2_{s}', f'wingtip_{s}')]
    piv = g.matrix_world.translation.copy(); hub = un.matrix_world.translation.copy()
    d = Vector((-side * math.sin(math.radians(STOW_DEG)), math.cos(math.radians(STOW_DEG)), 0.0))   # stowed, inboard
    u = Vector((0, 0, -1)).cross(d).normalized()                                                    # fold axis
    a_perp = Vector((-d.y, d.x, 0.0))                                                                # plan normal to d

    wbm = world_bm(wings); tree = BVHTree.FromBMesh(wbm); wbm.free()
    def skins(x, y):
        up = tree.ray_cast(Vector((x, y, 4.0)), Vector((0, 0, -1)), 8.0)[0]
        lo = tree.ray_cast(Vector((x, y, -4.0)), Vector((0, 0, 1)), 8.0)[0]
        return (lo.z if lo else None), (up.z if up else None)

    # ---- simulate the fold: plan points of the gear wherever it is inside the wing
    objs = [o for o in bpy.data.objects if o.type == 'MESH' and (o == g or g in anc(o))]
    verts = []                                        # vertices, plus points along long edges (the oleo has only end rings)
    for o in objs:
        mw = o.matrix_world; wh = o.name.startswith('wheel_'); me = o.data
        verts += [(o, mw @ v.co, wh) for v in me.vertices]
        for e in me.edges:
            a, b = mw @ me.vertices[e.vertices[0]].co, mw @ me.vertices[e.vertices[1]].co
            m = int((b - a).length / 0.08)
            verts += [(o, a.lerp(b, (i + 1) / (m + 1)), wh) for i in range(m)]
    # only what passes through the lower skin counts (the trunnion, for one, never leaves the wing)
    pts, wheel_t = [], []
    track = [[] for _ in verts]; below = [False] * len(verts)
    for i in range(41):
        f = i / 40
        q = Quaternion(u, math.radians(FOLD * f)); rz = Quaternion(Vector((0, 0, 1)), math.radians(-side * SWIVEL * f))
        for j, (o, p, is_wheel) in enumerate(verts):
            if o != g: p = hub + rz @ (p - hub)
            p = piv + Vector((0, 0, LIFT * f)) + q @ (p - piv)
            lo, up = skins(p.x, p.y)
            if lo is None or up is None or p.z <= lo - 0.002: below[j] = True; continue
            if p.z < up + 0.05: track[j].append(p)
    for j, (o, p0, is_wheel) in enumerate(verts):
        if not below[j]: continue
        for p in track[j]:
            pts.append((round(p.x, 3), round(p.y, 3)))
            if is_wheel: wheel_t.append((p - piv).dot(d))
    poly = simplify(hull2d(pts))
    # grow by MARGIN: move every edge out along its normal (ccw polygon -> outward = right of the edge)
    n = len(poly); P = [Vector((x, y)) for x, y in poly]
    normals = [Vector(((P[(i + 1) % n] - P[i]).y, -(P[(i + 1) % n] - P[i]).x)).normalized() for i in range(n)]
    grown = []
    for i in range(n):
        n0, n1 = normals[i - 1], normals[i]
        m = (n0 + n1).normalized(); c = max(m.dot(n1), 0.5)
        grown.append(P[i] + m * (MARGIN / c))
    poly = grown
    split_t = min(wheel_t) - SPLIT_BACK
    def inside(x, y):
        for i in range(n):
            a, b = poly[i], poly[(i + 1) % n]
            if (b.x - a.x) * (y - a.y) - (b.y - a.y) * (x - a.x) < -1e-6: return False
        return True
    def outline_dist(x, y):
        q = Vector((x, y)); best = 1e9
        for i in range(n):
            a, b = poly[i], poly[(i + 1) % n]; ab = b - a
            t = max(0.0, min(1.0, (q - a).dot(ab) / ab.length_squared))
            best = min(best, (a + ab * t - q).length)
        return best
    def t_of(x, y): return (Vector((x, y, 0)) - Vector((piv.x, piv.y, 0))).dot(d)
    xs = [p.x for p in poly]; ys = [p.y for p in poly]
    box = (min(xs) - 0.3, max(xs) + 0.3, min(ys) - 0.3, max(ys) + 0.3)

    # ---- cut each wing part along the slot outline (and the door split line), lower skin only
    planes = []
    for i in range(n):
        a, b = poly[i], poly[(i + 1) % n]; e = (b - a).normalized()
        planes.append((Vector((a.x, a.y, 0)), Vector((e.y, -e.x, 0))))
    sp = Vector((piv.x, piv.y, 0)) + d * split_t
    planes.append((sp, d.copy()))
    strut_n = wheel_n = 0; rim_pts = []; ncut = njunk = 0
    for w in wings:
        mw = w.matrix_world; inv = mw.inverted()
        bm = bmesh.new(); bm.from_mesh(w.data); bm.transform(mw)
        def near(f):
            c = f.calc_center_median()
            return box[0] < c.x < box[1] and box[2] < c.y < box[3]
        for co, no in planes:
            faces = [f for f in bm.faces if near(f)]
            if not faces: continue
            geom = list({v for f in faces for v in f.verts}) + list({e for f in faces for e in f.edges}) + faces
            bmesh.ops.bisect_plane(bm, geom=geom, plane_co=co, plane_no=no, dist=1e-4)
        bm.faces.ensure_lookup_table()
        tr = BVHTree.FromBMesh(bm)
        def lower(f):
            c = f.calc_center_median()
            loc, _, idx, _ = tr.ray_cast(Vector((c.x, c.y, c.z - 3.0)), Vector((0, 0, 1)), 3.5)
            return idx is not None and (idx == f.index or (loc - c).length < 0.005)
        cut = [f for f in bm.faces if inside(*f.calc_center_median().xy) and lower(f)]
        if not cut: bm.free(); continue
        cut_idx = {f.index for f in cut}
        ncut += len(cut)
        if M_WELL.name not in [m.name for m in w.data.materials if m]: w.data.materials.append(M_WELL)
        well_slot = [m.name if m else '' for m in w.data.materials].index(M_WELL.name)
        for kind in ('Strut', 'Wheel'):
            mine = {f.index for f in cut if (t_of(*f.calc_center_median().xy) < split_t) == (kind == 'Strut')}
            if not mine: continue
            dbm = bm.copy(); dbm.faces.ensure_lookup_table()
            bmesh.ops.delete(dbm, geom=[f for f in dbm.faces if f.index not in mine], context='FACES')
            n_outer = len(dbm.verts)
            inner = bmesh.ops.duplicate(dbm, geom=list(dbm.faces))['geom']
            ifaces = [x for x in inner if isinstance(x, bmesh.types.BMFace)]
            for v in {v for f in ifaces for v in f.verts}: v.co.z += DOOR_IN
            bmesh.ops.reverse_faces(dbm, faces=ifaces)
            for f in ifaces: f.material_index = well_slot
            idx = strut_n if kind == 'Strut' else wheel_n
            name = f'outDoor{kind}_{k}_{idx}'
            if kind == 'Strut': strut_n += 1
            else: wheel_n += 1
            me = bpy.data.meshes.new(name); dbm.transform(inv); dbm.to_mesh(me); dbm.free()
            for m in w.data.materials: me.materials.append(m)
            ob = bpy.data.objects.new(name, me); bpy.context.scene.collection.objects.link(ob)
            ob.matrix_world = mw.copy(); ob.parent = w; ob.matrix_world = mw.copy()
            # shade like the skin around it: the outer face takes the (still uncut) wing's normals
            vg = ob.vertex_groups.new(name='outer'); vg.add(list(range(n_outer)), 1.0, 'REPLACE')
            mod = ob.modifiers.new('skin_normals', 'DATA_TRANSFER')
            mod.object = w; mod.use_object_transform = True; mod.use_loop_data = True
            mod.data_types_loops = {'CUSTOM_NORMAL'}; mod.loop_mapping = 'POLYINTERP_NEAREST'; mod.vertex_group = 'outer'
            with bpy.context.temp_override(object=ob, active_object=ob, selected_objects=[ob]):
                bpy.ops.object.modifier_apply(modifier=mod.name)
        # the wing's own surfaces between the skins inside the slot would show in the open well
        def junk(f):
            if f.index in cut_idx: return False
            c = f.calc_center_median()
            if not any(inside(v.co.x, v.co.y) and outline_dist(v.co.x, v.co.y) > 0.005 for v in f.verts): return False
            lo, up = skins(c.x, c.y)
            return lo is not None and up is not None and lo + 0.01 < c.z < up - 0.01
        jk = [f for f in bm.faces if junk(f)]
        njunk += len(jk)
        bmesh.ops.delete(bm, geom=cut + jk, context='FACES')
        bm.edges.ensure_lookup_table()
        for e in bm.edges:
            if not e.is_boundary: continue
            m = (e.verts[0].co + e.verts[1].co) / 2
            # slot rim: on the outline (the part's other open edges, e.g. its seam with the next part, are not)
            if outline_dist(m.x, m.y) < 0.005: rim_pts.append((e.verts[0].co.copy(), e.verts[1].co.copy()))
        bm.transform(inv); bm.to_mesh(w.data); bm.free(); w.data.update()

    # ---- well box: walls from the slot rim up to the roof, roof just under the upper skin over the slot
    host = wings[0] if inside(*wings[0].matrix_world.translation.xy) else min(
        wings, key=lambda w: (w.matrix_world.translation.xy - Vector((piv.x, piv.y))).length)
    wb = bmesh.new()
    def roof_z(x, y):
        _, up = skins(x, y)
        return (up if up is not None else piv.z + 0.1) - ROOF_GAP
    # one side first, on shared vertices; the back faces are a reversed copy at the end (built in one go, a merge
    # of doubled vertices would fold each two-sided pair into one face)
    vcache = {}
    def V(p):
        key = (round(p.x, 4), round(p.y, 4), round(p.z, 4))
        if key not in vcache: vcache[key] = wb.verts.new(p)
        return vcache[key]
    def face(pts):
        vs = [V(p) for p in pts]
        if len(set(vs)) < 3: return
        try: wb.faces.new(list(dict.fromkeys(vs)))
        except ValueError: pass                                           # already there
    for a, b in rim_pts:
        if (b - a).length < 1e-4: continue
        ra, rb = Vector((a.x, a.y, roof_z(a.x, a.y))), Vector((b.x, b.y, roof_z(b.x, b.y)))
        face((a, b, rb, ra))
    # roof: the slot outline, subdivided along its length so it follows the upper skin
    ring = [Vector((p.x, p.y, 0)) for p in poly]
    cen = sum(ring, Vector()) / len(ring)
    steps = 6
    for i in range(len(ring)):
        a, b = ring[i], ring[(i + 1) % len(ring)]
        for j in range(steps):
            p00 = cen + (a - cen) * (j / steps); p01 = cen + (b - cen) * (j / steps)
            p10 = cen + (a - cen) * ((j + 1) / steps); p11 = cen + (b - cen) * ((j + 1) / steps)
            quad = [Vector((p.x, p.y, roof_z(p.x, p.y))) for p in (p00, p10, p11, p01)]
            if j == 0: quad = quad[1:]                                    # triangle at the centre
            face(quad)
    back = bmesh.ops.duplicate(wb, geom=list(wb.verts) + list(wb.edges) + list(wb.faces))['geom']
    bmesh.ops.reverse_faces(wb, faces=[f for f in back if isinstance(f, bmesh.types.BMFace)])
    inv = host.matrix_world.inverted(); wb.transform(inv)
    wme = bpy.data.meshes.new(f'wellBox_{k}'); wb.to_mesh(wme); wb.free(); wme.materials.append(M_WELL)
    well = bpy.data.objects.new(f'wellBox_{k}', wme); bpy.context.scene.collection.objects.link(well)
    well.matrix_world = host.matrix_world.copy(); well.parent = host; well.matrix_world = host.matrix_world.copy()
    L = max(t_of(p.x, p.y) for p in poly) - min(t_of(p.x, p.y) for p in poly)
    report.append(f'{k}: slot {len(poly)} sides, {L:.2f} m long, split at t {split_t:.2f}; {ncut} skin faces -> '
                  f'{strut_n} strut + {wheel_n} wheel door pieces, {njunk} internal faces removed, rim {len(rim_pts)} edges')

bpy.ops.wm.save_mainfile()
print('WINGWELLS', *report, sep='\n  ')
