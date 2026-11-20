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
from mathutils import Vector, Matrix, geometry
from mathutils.bvhtree import BVHTree

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
                caps = bmesh.ops.holes_fill(b, edges=cut_edges, sides=0)['faces']
                # The cap's rim is a hard edge and the cap is flat: otherwise smooth shading averages the cap's
                # sideways normal into the skin's vertices along the cut, and the hull shades as if creased there.
                for f in caps:
                    f.smooth = False
                    for e in f.edges: e.smooth = False
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
# Cut the belly skin along the opening's four edges first: the skin is long triangles, and picking them by their
# centres alone left a saw-tooth edge at both ends of the bay, with gaps past the bay's bulkheads that looked up through
# the hollow fuselage to the sky.
for co, no in (((0, BAY_Y0, 0), (0, 1, 0)), ((0, BAY_Y1, 0), (0, 1, 0)),
               ((-BAY_HALF_W, 0, 0), (1, 0, 0)), ((BAY_HALF_W, 0, 0), (1, 0, 0))):
    low = [f for f in fus.faces if f.calc_center_median().z < -1.5 * S]
    geom = list({e for f in low for e in f.edges}) + list({v for f in low for v in f.verts}) + low
    bmesh.ops.bisect_plane(fus, geom=geom, plane_co=Vector(co), plane_no=Vector(no), dist=1e-4)
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
#      Each panel is cut out of the wing's own top skin, like the gear doors: flush, in the wing's paint (so it takes
#      liveries, damage shading and panel lines), its edges straight cuts. The wing keeps a shallow dark well under
#      each panel (a floor 6 cm down and walls up to the cut), seen only when the panels rise. One ControlSurface per
#      wing drives them (B52Systems makes them one-way); they hinge at their leading edge.
#      The real B-52G/H has seven separate panels a side (an inboard group of three, an outboard group of four), each a
#      flat plate hinged on its own front edge. So besides spoilers_<sd> (all seven, the aero surface, hidden in game)
#      each panel is its own object, spoilerPanels_<sd><n>, its origin on its front edge and its X axis along that edge
#      as it really runs (the wing has dihedral: the old single hinge was level, so the outer panels swung through the
#      skin), with a 1 cm seam to its neighbours. Only the top skin is cut: the old cut also took the flap-cove floor
#      and the skin's own painted vents below it, which rose with the panels as a hooked slab.
SPOILER_WELL_DEPTH = 0.06
SPOILER_WELL = bpy.data.materials.new('B52_SpoilerWell'); SPOILER_WELL.use_nodes = True
SPOILER_WELL.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value = (0.012, 0.013, 0.014, 1)
SPOILER_WELL.diffuse_color = (0.012, 0.013, 0.014, 1)   # near black once the game reads it as sRGB (0.09 showed light grey)
ALL_MATS.append(SPOILER_WELL)


def make_spoilers(sgn, a, b):
    sd = 'L' if sgn < 0 else 'R'
    hinge = (b - a).normalized()
    fwd, up = Vector((0, 1, 0)), Vector((0, 0, 1))
    across = hinge.cross(up).normalized()                  # normal of the vertical planes along the hinge line
    n_pan, t0, t1 = 7, 0.33, 0.98                          # along the flap line, outboard part only
    # The panels' back edge (measured square to the flap hinge line) clears the source skin's row of slots, the
    # "zipper", which stays whole behind them as it is on the model. The row runs only along the outboard third
    # (0.65 to 0.98 of the hinge line) and slants: its centre line is 0.31 m ahead of the hinge at 0.65 and 0.13 m at
    # 0.98 (fitted through its 50 slots, both wings alike). Inboard of it the edge is 0.50 m ahead; alongside it, it
    # keeps 0.19 m ahead of that line (the strays sit up to 13 cm off it), parallel to the zipper. (A first cut at
    # 0.13 m ran through the row and left a ragged staircase, and its open slots looked into the wing; a straight cut
    # at 0.50 m left the zipper running at a slant to the panels.)
    def back_at(t):                                        # along y: the hinge line is swept 27.5 deg
        perp = 0.50 if t <= 0.65 else 0.50 - 0.565 * (t - 0.65)
        return perp / 0.887 * S
    FRONT = 1.20 * S                                       # the front edge stays 1.20 m (along y) ahead of the hinge
    NU, NV = 12, 6                                         # each panel's grid: along the span, along the chord
    keys = [k for k in parts if k.startswith('wing') and k.endswith(sd)]
    # the outer pod's pylon fairing runs on over the wing's top under the skin; under the outboard panels it lay level
    # with them and showed through as dark bars, so it is cut and cleared with the wing's skin
    clear = keys + [k for k in parts if k.startswith('pod') and k.endswith(sd)]
    panels = bmesh.new()
    per = [bmesh.new() for _ in range(n_pan)]
    for i in range(n_pan):
        # neighbouring panels share a cut line: a strip of skin between them was too thin for the livery bake
        # and kept the plain grey under the camouflage
        s0 = t0 + (t1 - t0) * i / n_pan
        s1 = t0 + (t1 - t0) * (i + 1) / n_pan
        # back corners on the edge above, front corners on a line along the hinge; the sides run along y
        p0 = a.lerp(b, s0) + fwd * back_at(s0)
        p1 = a.lerp(b, s1) + fwd * back_at(s1)
        f0 = a.lerp(b, s0) + fwd * FRONT
        f1 = a.lerp(b, s1) + fwd * FRONT
        quad = [p0, p1, f1, f0]
        lo_x, hi_x = min(q.x for q in quad), max(q.x for q in quad)
        lo_y, hi_y = min(q.y for q in quad), max(q.y for q in quad)
        def ahead(p_, q_):                                 # horizontal normal of the line p_-q_, pointing forward
            n_ = Vector((-(q_ - p_).y, (q_ - p_).x, 0)).normalized()
            return n_ if n_.y > 0 else -n_
        n_back, n_front = ahead(p0, p1), ahead(f0, f1)

        def inside(c):
            if not (min(p0.x, p1.x) < c.x < max(p0.x, p1.x)): return False
            return (c - p0).dot(n_back) > 0 and (c - f0).dot(n_front) < 0

        def overlaps(f):
            # the face's extent overlaps the panel's (with 10 cm to spare): a large face can pass right through the well
            # with every corner and its centre far outside, and was never cut
            xs = [v.co.x for v in f.verts]; ys = [v.co.y for v in f.verts]
            return max(xs) > lo_x - 0.1 and min(xs) < hi_x + 0.1 and max(ys) > lo_y - 0.1 and min(ys) < hi_y + 0.1

        def gpt(u, v):                                     # grid point (u, v) of the panel's outline
            return p0.lerp(p1, u / NU).lerp(f0.lerp(f1, u / NU), v / NV)

        # 1. cut each wing part's skin (and the pod fairing over it) along the panel's outline
        for k in clear:
            bm = parts[k]
            for co, no in ((p0, Vector((1, 0, 0))), (p1, Vector((1, 0, 0))), (p0, n_back), (f0, n_front)):
                bm.normal_update()
                near = [f for f in bm.faces if f.normal.z > -0.5 and overlaps(f)]   # not the bottom skin: cutting that
                # changed the tip-gear door cuts; the slots' walls and lips are cut too, or their pieces stayed on top
                if not near: break
                geom = list({e for f in near for e in f.edges}) + list({v for f in near for v in f.verts}) + near
                bmesh.ops.bisect_plane(bm, geom=geom, plane_co=co, plane_no=no, dist=1e-4)

        # 2. the top skin under the panel, sampled on a grid: height, texture coordinates and material. A sample that
        #    falls through one of the skin's slots (a row of them runs along the spoilers) onto the cove below is
        #    replaced by its neighbour's, so the panel is one smooth plate.
        samp = bmesh.new()
        for k in keys:
            me = bpy.data.meshes.new('t'); parts[k].to_mesh(me); samp.from_mesh(me); bpy.data.meshes.remove(me)
        # never the wells already built for the panels before this one: rays at a shared edge hit their dark walls
        bmesh.ops.delete(samp, geom=[f for f in samp.faces if f.material_index == ALL_MATS.index(SPOILER_WELL)], context='FACES')
        bmesh.ops.triangulate(samp, faces=samp.faces[:])
        samp.faces.ensure_lookup_table()
        uvl = samp.loops.layers.uv.active
        tree = BVHTree.FromBMesh(samp)
        grid = {}
        for u in range(NU + 1):
            for v in range(NV + 1):
                q = gpt(u, v)
                # the centre ray, unless it went through a slot: then the highest of four rays 2.5 cm round it (on the
                # sloping skin the highest alone sat 3 mm proud everywhere)
                best = tree.ray_cast(Vector((q.x, q.y, 10.0)), -up, 30.0)
                for ox, oy in ((0.025, 0), (-0.025, 0), (0, 0.025), (0, -0.025)):
                    hit = tree.ray_cast(Vector((q.x + ox, q.y + oy, 10.0)), -up, 30.0)
                    if hit[0] is not None and (best[0] is None or hit[0].z > best[0].z + 0.015): best = hit
                grid[u, v] = None if best[0] is None else (Vector((q.x, q.y, best[0].z)), best[2])
        if sum(1 for g in grid.values() if g) < 6: samp.free(); continue
        # per chordwise column, a straight line through its samples; one more than 1.5 cm under it went through a slot
        # and takes the line's height (a single plane over the whole panel sat up to 3 cm under the curved skin)
        filled = {k_ for k_, g in grid.items() if g is None}
        for u in range(NU + 1):
            col = [(v, grid[u, v]) for v in range(NV + 1)]
            ok = [(v, g) for v, g in col if g]
            for _ in range(3):
                if len(ok) < 2: break
                mv = sum(v for v, _ in ok) / len(ok); mz = sum(g[0].z for _, g in ok) / len(ok)
                sv = sum((v - mv) ** 2 for v, _ in ok) or 1e-9
                k = sum((v - mv) * (g[0].z - mz) for v, g in ok) / sv
                line = lambda v, mv=mv, mz=mz, k=k: mz + k * (v - mv)
                keep = [(v, g) for v, g in ok if g[0].z > line(v) - 0.015]
                if len(keep) == len(ok): break
                ok = keep
            good = dict(ok)
            for v, g in col:
                if v in good: continue
                filled.add((u, v))
                src = good[min(good, key=lambda v2: abs(v2 - v))] if good else None
                if src is None: continue
                q = gpt(u, v)
                grid[u, v] = (Vector((q.x, q.y, line(v) if len(ok) >= 2 else src[0].z)), src[1])
        for key, g in list(grid.items()):                   # a column with nothing at all: its neighbour's
            if g is None:
                u, v = key
                nb = next((grid[u2, v] for u2 in sorted(range(NU + 1), key=lambda x: abs(x - u)) if grid[u2, v]), None)
                q = gpt(u, v)
                grid[key] = (Vector((q.x, q.y, nb[0].z)), nb[1])
        valid = list(grid.values())
        n_ = len(valid); mx = sum(g[0].x for g in valid) / n_; my = sum(g[0].y for g in valid) / n_; mz = sum(g[0].z for g in valid) / n_
        sxx = sum((g[0].x - mx) ** 2 for g in valid); sxy = sum((g[0].x - mx) * (g[0].y - my) for g in valid)
        syy = sum((g[0].y - my) ** 2 for g in valid); sxz = sum((g[0].x - mx) * (g[0].z - mz) for g in valid)
        syz = sum((g[0].y - my) * (g[0].z - mz) for g in valid); det = sxx * syy - sxy * sxy or 1e-9
        gx = (sxz * syy - syz * sxy) / det; gy = (syz * sxx - sxz * sxy) / det
        plane = lambda x, y: mz + gx * (x - mx) + gy * (y - my)        # the panel's mean plane: how deep the well goes

        def uv_at(pt, fi):
            # texture coordinates at pt from triangle fi (extended past its edges: the skin's mapping is smooth there)
            f = samp.faces[fi]
            A, B, C = (l.vert.co for l in f.loops)
            ua, ub, uc = (Vector((l[uvl].uv.x, l[uvl].uv.y, 0)) for l in f.loops)
            r = geometry.barycentric_transform(pt, A, B, C, ua, ub, uc)
            return r.x, r.y

        # 3. the panel: its top is the skin's (texture and paint), 3 cm thick, dark underneath and round its edges
        pm = bmesh.new()
        puv = pm.loops.layers.uv.new('UVMap')
        TH = 0.03 * S
        top = {k_: pm.verts.new(g[0]) for k_, g in grid.items()}
        bot = {k_: pm.verts.new(g[0] - up * TH) for k_, g in grid.items()}
        dark = ALL_MATS.index(SPOILER_WELL)
        for u in range(NU):
            for v in range(NV):
                ks = [(u, v), (u + 1, v), (u + 1, v + 1), (u, v + 1)]
                # each corner's texture from the skin triangle under it; the square takes the material most of its
                # corners have, and a corner on another material borrows a same-material corner's triangle (the
                # source textures are separate images: mixing their coordinates painted dark wedges on the panels)
                mats = [samp.faces[grid[k_][1]].material_index for k_ in ks]
                m = max(set(mats), key=mats.count)
                donor = next(grid[k_][1] for k_, mm in zip(ks, mats) if mm == m)
                f = pm.faces.new([top[k_] for k_ in ks]); f.normal_update()
                flipped = f.normal.z < 0
                if flipped: f.normal_flip()
                f.material_index = m
                for l in f.loops:
                    k_ = next(k2 for k2 in ks if top[k2] is l.vert)
                    if k_ in filled:
                        # a point over one of the skin's slots: the paint of the nearest point on solid skin (worked
                        # out at its own position, the texture there shows the slot drawn in, as dark blocks)
                        src = min((k2 for k2 in grid if k2 not in filled), key=lambda k2: (k2[0] - k_[0]) ** 2 + (k2[1] - k_[1]) ** 2)
                        fi = grid[src][1] if samp.faces[grid[src][1]].material_index == m else donor
                        l[puv].uv = uv_at(grid[src][0], fi)
                    else:
                        fi = grid[k_][1] if samp.faces[grid[k_][1]].material_index == m else donor
                        l[puv].uv = uv_at(l.vert.co, fi)
                g_ = pm.faces.new([bot[k_] for k_ in ks]); g_.normal_update()
                if g_.normal.z > 0: g_.normal_flip()
                g_.material_index = dark
        ring = [(u, 0) for u in range(NU)] + [(NU, v) for v in range(NV)] + [(u, NV) for u in range(NU, 0, -1)] + [(0, v) for v in range(NV, 0, -1)]
        ctr = sum((g[0] for g in grid.values()), Vector()) / len(grid)
        for j in range(len(ring)):
            ka, kb = ring[j], ring[(j + 1) % len(ring)]
            # the edges get points of their own: shared with the top, smooth shading bent the top's edge normals
            # towards the sides and shaded a dark wedge along every panel edge
            f = pm.faces.new([pm.verts.new(top[ka].co), pm.verts.new(top[kb].co), pm.verts.new(bot[kb].co), pm.verts.new(bot[ka].co)])
            f.normal_update()
            if f.normal.dot((top[ka].co + top[kb].co) / 2 - ctr) < 0: f.normal_flip()
            f.material_index = dark
        me = bpy.data.meshes.new('t'); pm.to_mesh(me); panels.from_mesh(me); per[i].from_mesh(me); bpy.data.meshes.remove(me)
        pm.free()

        # 4. the wing's skin under the panel, and everything down to 20 cm below it (the cove, the slots), is
        #    replaced by a clean dark well: a floor 8 cm down and walls up to the skin's cut edge
        DEPTH, FLOOR = 0.20 * S, 0.08 * S
        home = None
        for k in clear:
            bm = parts[k]
            bm.normal_update()
            def under(f):               # how far the face lies under the panel's surface
                c = f.calc_center_median(); return plane(c.x, c.y) - c.z
            # everything in the outline down to DEPTH, but a downward face only near the top (a lip or a slot's
            # roof), never the bottom skin
            # Anything standing up inside the well goes: tall faces (the outer pod's pylon fairing, the wall where the
            # wing is split into two parts) reached below the floor and past the outline, kept their centres outside
            # the rule above and stood in the open well as dark blocks when the panels rose. So everything near the
            # outline is first sliced at the floor's level, and every piece above it inside the outline is removed,
            # whichever way it faces (the bottom skin is far below the floor).
            fn = Vector((-gx, -gy, 1.0)).normalized()
            fco = Vector((mx, my, mz - FLOOR - 0.01 * S))
            near = [f for f in bm.faces if overlaps(f)]
            if near:
                geom = list({e for f in near for e in f.edges}) + list({v for f in near for v in f.verts}) + near
                bmesh.ops.bisect_plane(bm, geom=geom, plane_co=fco, plane_no=fn, dist=1e-4)
                bm.normal_update()
            gone = [f for f in bm.faces if inside(f.calc_center_median())
                    and (under(f) < FLOOR + 0.01 * S or (under(f) < DEPTH and f.normal.z > -0.7))]

            if k.startswith('pod') and gone: print(f'SPOILER {sd}{i + 1}: {len(gone)} faces of {k}\'s fairing under the panel cleared')
            if gone and k.startswith('wing') and (home is None or len(gone) > home[1]): home = (k, len(gone))
            bmesh.ops.delete(bm, geom=gone, context='FACES')
        if home:
            bm = parts[home[0]]
            # the floor reaches 1 cm past the panel's sides along the hinge, so neighbouring wells' floors overlap:
            # ending both on the shared line left a hairline you could see the sky down between two raised panels
            fl = {k_: bm.verts.new(g[0] - up * FLOOR + hinge * ((-1 if k_[0] == 0 else 1 if k_[0] == NU else 0)
                                                               * 0.01 * S * (1 if (p1 - p0).dot(hinge) > 0 else -1)))
                  for k_, g in grid.items()}
            sk = {k_: bm.verts.new(g[0]) for k_, g in grid.items() if k_ in ring}
            # Floor and walls are built as triangles, each with a dark back 1 cm behind it on the same triangle: the
            # floor follows the curved skin, so its squares are slightly bent, and a square and its reversed copy were
            # split along opposite diagonals on export; the back then poked through the front in places, and the game,
            # which draws only fronts, showed the sky through it.
            def two_sided(tri, facing, back_off):
                f = bm.faces.new(tri); f.normal_update()
                if f.normal.dot(facing) < 0: f.normal_flip()
                f.material_index = dark
                g_ = bm.faces.new([bm.verts.new(v.co + back_off) for v in tri]); g_.normal_update()
                if g_.normal.dot(facing) > 0: g_.normal_flip()
                g_.material_index = dark
            for u in range(NU):
                for v in range(NV):
                    q = [fl[(u, v)], fl[(u + 1, v)], fl[(u + 1, v + 1)], fl[(u, v + 1)]]
                    for tri in ((q[0], q[1], q[2]), (q[0], q[2], q[3])):
                        two_sided(list(tri), up, -up * 0.01 * S)
            for j in range(len(ring)):
                ka, kb = ring[j], ring[(j + 1) % len(ring)]
                mid = (sk[ka].co + sk[kb].co) / 2
                inward = (ctr - mid); inward.z = 0; inward.normalize()   # walls face into the well
                q = [sk[ka], sk[kb], fl[kb], fl[ka]]
                for tri in ((q[0], q[1], q[2]), (q[0], q[2], q[3])):
                    two_sided(list(tri), inward, -inward * 0.01 * S)
        samp.free()
    if not panels.faces: raise SystemExit(f'spoilers_{sd}: no wing skin found under the panels')
    add(f'spoilers_{sd}', panels)
    SPOILER_PANELS[sd] = (per, hinge, -across if (-across).y > 0 else across)


SPOILER_PANELS = {}


make_spoilers(-1, hingeL[0], hingeL[1])
make_spoilers(1, hingeR[0], hingeR[1])

# ---- The wings stop inside the fuselage wall
#      The source model's wings run on through the fuselage almost to the centre line (x +-0.23): inside the hull each
#      wing root was 18.5 m2 of skin, the whole 8 m chord in the top 1.2 m of the cabin, with its ribs and stringers
#      built inside it (blender_structure.py). Wing-root and inboard-flap skin is cut along the plane |x| = HULL_CUT and
#      everything inboard of it removed. The cut is a plane, not the fuselage's curved side, so a short stub of wing stays
#      inside the wall (the hull is 1.48 m wide at the wing's foot and 0.83 m at its top), but nothing outside the hull is
#      touched. (A first version removed whole faces whose centre was inside: the source wing is made of long triangles
#      running from the centre line out past the fuselage side, so that cut holes in the wing outside the hull.)
#      What carries the wing's load across the hull, its front and rear spars, blender_structure.py builds as the
#      carry-through.
HULL_CUT = 0.75 * S
def trim_inside_hull():
    for k in [k for k in parts if k.startswith(('wingroot_', 'flap1_'))]:
        bm = parts[k]
        sgn = 1 if sum(v.co.x for v in bm.verts) > 0 else -1
        bmesh.ops.bisect_plane(bm, geom=bm.verts[:] + bm.edges[:] + bm.faces[:], plane_co=Vector((sgn * HULL_CUT, 0, 0)),
                               plane_no=Vector((1, 0, 0)), dist=1e-4)
        gone = [f for f in bm.faces if all(abs(v.co.x) <= HULL_CUT + 1e-3 for v in f.verts)]
        area = sum(f.calc_area() for f in gone)
        bmesh.ops.delete(bm, geom=gone, context='FACES')
        print(f'HULLTRIM {k}: {len(gone)} faces ({area:.1f} m2) inboard of |x| {HULL_CUT:.2f} removed')


trim_inside_hull()

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
        # Hinge axis is known analytically (the cut line), so it is not guessed from vertices: on swept parts the
        # forward-most vertices bunch at one end and gave skewed hinges. Origin = the leading-edge midpoint.
        sd = name[-1]
        if base == 'elevator':
            sgn = -1 if sd == 'L' else 1
            axis = Vector((sgn * 7.8 * S, -16.95 * S, 0)) - Vector((0, -15.9 * S, 0))
        else:
            a_, b_ = hingeL if sd == 'L' else hingeR
            axis = b_ - a_
        if axis.x < 0:
            axis = -axis
        p, _ = hinge_of(o, Vector((0, -1, 0)))
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

# ---- The seven spoiler panels a side, each its own flat plate on its own hinge (see make_spoilers)
SEAM = 0.005 * S                                            # half the gap between neighbouring panels
for sd, (per, hinge, fwd_perp) in SPOILER_PANELS.items():
    for i, bm in enumerate(per):
        if not bm.verts: continue
        bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)
        vs = [v.co for v in bm.verts]
        mid = sum(vs, Vector()) / len(vs)
        lo, hi = min(v.dot(hinge) for v in vs), max(v.dot(hinge) for v in vs)
        k = SEAM / max((hi - lo) / 2, 0.1)
        for v in bm.verts:                                   # pull the side edges in: a hairline seam to the next panel
            v.co -= hinge * ((v.co - mid).dot(hinge) * k)
        vs = [v.co for v in bm.verts]
        # front edge: the forward-most vertices; its two ends (along the hinge) give the hinge line, slope included
        front = max(v.dot(fwd_perp) for v in vs)
        lead = [v for v in vs if v.dot(fwd_perp) > front - 0.06 * S]
        p0 = min(lead, key=lambda v: v.dot(hinge)); p1 = max(lead, key=lambda v: v.dot(hinge))
        axis = p1 - p0
        if axis.x < 0: axis = -axis
        nf = len(bm.faces)
        o = new_obj(f'spoilerPanels_{sd}{i + 1}', bm, ALL_MATS)
        set_origin(o, (p0 + p1) / 2, axis)
        mw = o.matrix_world.copy(); o.parent = objs[f'spoilers_{sd}']; o.matrix_world = mw
        print(f'SPOILER {o.name}: hinge {tuple(round(c, 2) for c in (p0 + p1) / 2)}, {axis.length:.2f} m wide, '
              f'slope {math.degrees(math.atan2(axis.z, axis.xy.length)):.1f} deg, {nf} faces')

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


# Bomb bay interior: an open-bottom dark box behind the doors, faces pointing inward. Like the real bay it is wider
# inside than the door opening: the walls flare from the door edges (BAY_HALF_W) out to BAY_TOP_HALF_W, and the roof
# sits high enough for the rotary launcher with cruise missiles (they reach 1.26 m above the launcher, and +-1.03 m
# across; the old 1.7 x 1.8 m box let them through the roof and walls by up to 0.35 m). The fuselage is +-1.45 m wide
# inside from 1.9 m above the belly up, and the wing carry-through starts at z +0.1, so this stays inside the skin.
BAY_TOP_HALF_W, BAY_FLARE_Z, BAY_ROOF_Z = 1.10 * S, -2.0 * S, -0.30 * S
def bay_box(bm):
    z0 = -2.5 * S
    profile = [(-BAY_HALF_W, z0), (-BAY_TOP_HALF_W, BAY_FLARE_Z), (-BAY_TOP_HALF_W, BAY_ROOF_Z),
               (BAY_TOP_HALF_W, BAY_ROOF_Z), (BAY_TOP_HALF_W, BAY_FLARE_Z), (BAY_HALF_W, z0)]
    ends = [[bm.verts.new((x, y, z)) for x, z in profile] for y in (BAY_Y0, BAY_Y1)]
    for i in range(len(profile) - 1):                                   # flares, walls and roof; the bottom stays open
        bm.faces.new([ends[0][i], ends[0][i + 1], ends[1][i + 1], ends[1][i]])
    for e in ends:                                                      # forward and aft bulkheads
        bm.faces.new(e)
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
