"""
The B-52J's internal structure: what you see inside a part where it has broken off (docs/DAMAGE_MODEL.md, step 8).

Run:  blender -b blender/out/B52_parts.blend --python tools/blender_structure.py [-- --nosave]

An aircraft is not a tube: its skin is held in shape by a structure inside. For every flight-model part this adds a
child object "<part>_struct" built from the part's own skin:
- Wings, tail surfaces, flaps (thin sections): a truss rib every 0.7 m (cap strips along both skins with uprights and
  diagonals between them, from 6% to 65% chord), the front and rear spars (webs at 15% and 65% chord, capped top and
  bottom) and stringers along the inside of both skins. All are placed by chord fraction on straight lines fitted
  through the sections and hung between the skins measured there, 3 cm in, and only where the wing is over 10 cm
  thick: they used to follow the raw cut outline (with its flap cut-outs, fairings and pylons), which left floating
  rib pieces, spikes at the trailing edges and stringers jumping between ribs.
- Fuselage sections: frames every 0.6 m on the outer skin only (found by rays in from outside, so never round the
  wells, bay or anything else inside the hull), stopping short of the gear wells and bomb bay, with stringers every
  15 degrees and two heavier longerons. Inside the hull the wings are only their front and rear spars, crossing to
  the centre line: the rest of the cabin is open.
- Engine pods (round sections): frames every 0.6 m (a ring a little inside the skin, round each nacelle and the
  pylon) and stringers along them every 15 degrees.
- An inner skin: the part's skin seen from inside, in primer, 2 cm in (less at thin trailing edges), so a break looks into the structure rather than
  out through the far side of the part.
The structure objects ship switched off: B52Systems turns on a part's structure, and its parent's, when it breaks off,
behind the torn skin and spar stubs it builds at the break (BreakStubs). So it costs nothing until then.

The section of a part across its long axis is cut from its skin (bmesh bisect), every cut is split into its closed
loops (a pod's cut is two nacelles and a pylon), and each loop gets its rib or frame. Spars and stringers join the same
points of the largest loop from one cut to the next.
"""
import bpy, bmesh, sys, math
from mathutils import Vector, Matrix
from mathutils.bvhtree import BVHTree

ARGS = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
SAVE = '--nosave' not in ARGS

PARTS = ['B52', 'fuselage_F', 'fuselage_R', 'tail', 'rudder', 'hstab_L', 'hstab_R', 'elevator_L', 'elevator_R',
         'wingroot_L', 'wingroot_R', 'wing1_L', 'wing1_R', 'wing2_L', 'wing2_R', 'wingtip_L', 'wingtip_R',
         'flap1_L', 'flap1_R', 'flap2_L', 'flap2_R', 'spoilers_L', 'spoilers_R', 'pod1_L', 'pod1_R', 'pod2_L', 'pod2_R']
# The cockpit is left out: its inside is the flight deck (blender_cockpit.py).


def material(name, rgb, rough):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.use_nodes = True
    p = m.node_tree.nodes['Principled BSDF']
    p.inputs['Base Color'].default_value = (*rgb, 1); p.inputs['Roughness'].default_value = rough
    p.inputs['Metallic'].default_value = 0.0
    return m


MAT_METAL = material('B52_StructMetal', (0.46, 0.47, 0.48), 0.55)     # bare and anodised aluminium: ribs, spars, frames
MAT_PRIMER = material('B52_StructPrimer', (0.30, 0.33, 0.22), 0.8)    # zinc-chromate primer: the inside of the skin


# ------------------------------------------------------------------ geometry helpers

class Mesh:
    def __init__(self):
        self.v = []; self.f = []; self.m = []

    def add(self, pts, mat, both=True):
        i = len(self.v); self.v += [tuple(p) for p in pts]
        idx = list(range(i, i + len(pts)))
        self.f.append(idx); self.m.append(mat)
        if both: self.f.append(idx[::-1]); self.m.append(mat)

    def box(self, a, b, up, w, h, mat, caps=True):
        """A bar from a to b, w wide (across, perpendicular to up) and h deep (along up). Without caps (lattice members
        that meet others at their ends) only its four long sides."""
        d = (b - a)
        if d.length < 1e-4: return
        side = d.cross(up)
        if side.length < 1e-6: side = d.orthogonal()
        side = side.normalized() * (w / 2); upn = side.cross(d).normalized() * (h / 2)
        c = [a - side - upn, a + side - upn, a + side + upn, a - side + upn]
        e = [p + d for p in c]
        sides = [[c[0], e[0], e[1], c[1]], [c[1], e[1], e[2], c[2]], [c[2], e[2], e[3], c[3]], [c[3], e[3], e[0], c[0]]]
        for q in ([[c[0], c[1], c[2], c[3]], [e[3], e[2], e[1], e[0]]] if caps else []) + sides:
            self.add(q, mat, both=False)

    def write(self, name, parent):
        me = bpy.data.meshes.new(name)
        me.from_pydata(self.v, [], self.f)
        me.materials.append(MAT_METAL); me.materials.append(MAT_PRIMER)
        for poly, m in zip(me.polygons, self.m): poly.material_index = m
        me.validate(); me.update()
        o = bpy.data.objects.new(name, me)
        for c in parent.users_collection: c.objects.link(o)
        o.parent = parent; o.matrix_parent_inverse = parent.matrix_world.inverted()
        return o


def world_bm(o):
    bm = bmesh.new(); bm.from_mesh(o.data); bm.transform(o.matrix_world)
    return bm


def loops_of(edges):
    """Group cut edges into chains of points (closed where they close)."""
    adj = {}
    for e in edges:
        a, b = e.verts
        adj.setdefault(a, []).append(b); adj.setdefault(b, []).append(a)
    seen, loops = set(), []
    for start in adj:
        if start in seen: continue
        chain, prev, cur = [start], None, start
        seen.add(start)
        while True:
            nxt = [n for n in adj[cur] if n is not prev and n not in seen]
            if not nxt: break
            prev, cur = cur, nxt[0]; seen.add(cur); chain.append(cur)
        if len(chain) >= 6: loops.append([v.co.copy() for v in chain])
    return loops


def section(bm_src, axis, at):
    """The part's skin cut by the plane axis = at: its loops of world points."""
    bm = bm_src.copy()
    no = Vector((0, 0, 0)); no[axis] = 1.0
    co = Vector((0, 0, 0)); co[axis] = at
    res = bmesh.ops.bisect_plane(bm, geom=bm.verts[:] + bm.edges[:] + bm.faces[:], plane_co=co, plane_no=no)
    cut = [g for g in res['geom_cut'] if isinstance(g, bmesh.types.BMEdge)]
    loops = loops_of(cut)
    bm.free()
    return loops


def frame2(axis):
    """The two in-plane axes for a cut across `axis`: (chord, thickness) for a wing (axis X): Y, Z; a fin (axis Z): Y, X;
    a fuselage (axis Y): X, Z."""
    return {0: (1, 2), 1: (0, 2), 2: (1, 0)}[axis]


def inset(loop, axis, d):
    """Each point moved d towards the loop's inside (its 2D inward normal in the cut plane), at most 45% of the way to
    the loop's centre so thin trailing edges don't cross over."""
    a, b = frame2(axis)
    c = sum(loop, Vector()) / len(loop)
    n = len(loop); out = []
    for i, p in enumerate(loop):
        q0, q1 = loop[i - 1], loop[(i + 1) % n]
        t = q1 - q0; t2 = Vector((t[a], t[b]))
        nrm = Vector((-t2.y, t2.x))
        if nrm.length < 1e-6: out.append(p.copy()); continue
        nrm.normalize()
        to_c = Vector((c[a] - p[a], c[b] - p[b]))
        if nrm.dot(to_c) < 0: nrm = -nrm
        dd = min(d, 0.45 * to_c.length)
        q = p.copy(); q[a] += nrm.x * dd; q[b] += nrm.y * dd
        out.append(q)
    return out


def radial(loop, a, b, d):
    """Each point moved d straight toward the loop's centre (at most 45% of the way): for round sections, where this
    can't fold over as an inset along the outline's own normals did at kinks."""
    c = sum(loop, Vector()) / len(loop)
    out = []
    for p in loop:
        v = Vector((c[a] - p[a], c[b] - p[b])); L = v.length
        if L < 1e-6: out.append(p.copy()); continue
        dd = min(d, 0.45 * L) / L
        q = p.copy(); q[a] += v.x * dd; q[b] += v.y * dd
        out.append(q)
    return out


def resample(loop, n):
    """n points evenly along the closed loop (by length), starting at its point furthest along the first in-plane axis."""
    pts = loop + [loop[0]]
    seg = [(pts[i + 1] - pts[i]).length for i in range(len(loop))]
    total = sum(seg)
    if total < 1e-4: return [loop[0]] * n
    out, i, acc = [], 0, 0.0
    for k in range(n):
        target = total * k / n
        while i < len(seg) - 1 and acc + seg[i] < target: acc += seg[i]; i += 1
        t = (target - acc) / max(seg[i], 1e-9)
        out.append(pts[i].lerp(pts[i + 1], min(max(t, 0.0), 1.0)))
    return out


def by_angle(loop, a, b, n):
    """n points on the loop at even angles round its centre, starting out to the side (+a): the same angles on every
    section, so stringers run straight from one frame to the next."""
    c = sum(loop, Vector()) / len(loop)
    out = []
    m = len(loop)
    for k in range(n):
        th = 2 * math.pi * k / n
        d = (math.cos(th), math.sin(th))
        best, bt = None, 1e9
        for i in range(m):
            p, q = loop[i], loop[(i + 1) % m]
            px, py = p[a] - c[a], p[b] - c[b]; ex, ey = q[a] - p[a], q[b] - p[b]
            den = d[0] * ey - d[1] * ex
            if abs(den) < 1e-9: continue
            t = (px * ey - py * ex) / den; s_ = (px * d[1] - py * d[0]) / den
            if t > 0 and 0 <= s_ <= 1 and t < bt: bt, best = t, p.lerp(q, s_)
        out.append(best)
    return out


def crossing(loop, ax, val, bx):
    """Where the loop crosses the line (axis ax = val): the lowest and highest points along bx (a wing's bottom and top
    skin at that chord)."""
    hits = []
    n = len(loop)
    for i in range(n):
        p, q = loop[i], loop[(i + 1) % n]
        if (p[ax] - val) * (q[ax] - val) <= 0 and abs(q[ax] - p[ax]) > 1e-9:
            hits.append(p.lerp(q, (val - p[ax]) / (q[ax] - p[ax])))
    if len(hits) < 2: return None
    hits.sort(key=lambda h: h[bx])
    return hits[0], hits[-1]


# ------------------------------------------------------------------ the fuselage

HULL_PARTS = ('B52', 'fuselage_F', 'fuselage_R')
HULL_HALF = 1.50          # the fuselage's half-width at the wing (the centre part's skin reaches x +-1.48)
N_RAYS = 48               # frame resolution round the hull; every second one carries a stringer
FRAME_OUT, FRAME_IN = 0.05, 0.17   # a frame runs from 5 cm inside the skin (clear of the 4 cm bay/well lining) to 17 cm


def _blockers():
    """What the frames stop at inside the hull: the gear well boxes and the bomb bay (real frames are cut there)."""
    bm = bmesh.new()
    for o in bpy.data.objects:
        if o.type == 'MESH' and (o.name.startswith('wellBox_') or o.name == 'bombBay'):
            t = world_bm(o); me = bpy.data.meshes.new('t'); t.to_mesh(me); t.free()
            bm.from_mesh(me); bpy.data.meshes.remove(me)
    return BVHTree.FromBMesh(bm) if bm.faces else None


def _bays():
    """Each gear well box as a BVH, for a point-inside test: the box is open only at the bottom (its door opening), so a
    point inside it meets the box going up and going each way across."""
    out = []
    for o in bpy.data.objects:
        if o.type == 'MESH' and o.name.startswith('wellBox_'):
            t = world_bm(o)
            lo = Vector([min(v.co[i] for v in t.verts) for i in range(3)]); hi = Vector([max(v.co[i] for v in t.verts) for i in range(3)])
            out.append((lo, hi, BVHTree.FromBMesh(t))); t.free()
    return out

BAYS = None
def in_bay(p):
    global BAYS
    if BAYS is None: BAYS = _bays()
    p = Vector(p)
    dirs = (Vector((0, 0, 1)), Vector((1, 0, 0)), Vector((-1, 0, 0)), Vector((0, 1, 0)), Vector((0, -1, 0)))
    return any(all(lo[i] - 0.01 < p[i] < hi[i] + 0.01 for i in range(3))
               and all(t.ray_cast(p, d, 6.0)[0] is not None for d in dirs) for lo, hi, t in BAYS)


def hull_body(part, bm, stations, a, b, axis, mesh):
    """Frames and stringers on the fuselage's OUTER skin only. Each frame station casts N_RAYS rays in the cut plane, in
    from outside towards the hull's centre line, and takes the first skin it meets: the outer skin, never the wells,
    bay walls or wing-root faces inside. The old frames were rings round every loop the cut found inside the hull, and
    the stringers joined whatever loop came first on the next frame, criss-crossing the hull as a web of struts.
    A frame is left out where a well box or the bomb bay comes within 20 cm of the skin (real frames are cut and
    boxed round them), and stringers run only between neighbouring frames that agree within 25 cm."""
    skin = BVHTree.FromBMesh(bm)
    block = _blockers()
    ua, ub = _unit(a), _unit(b)
    rings = []
    for st in stations:
        # the hull's centre at this station: the middle of its extent across and up (vertices within 0.3 m of the cut)
        near = [v.co for v in bm.verts if abs(v.co[axis] - st) < 0.3]
        if len(near) < 8: rings.append(None); continue
        c = Vector((0, 0, 0)); c[axis] = st
        c[a] = (min(p[a] for p in near) + max(p[a] for p in near)) / 2
        c[b] = (min(p[b] for p in near) + max(p[b] for p in near)) / 2
        ring = []
        for i in range(N_RAYS):
            th = 2 * math.pi * i / N_RAYS
            d = ua * math.cos(th) + ub * math.sin(th)
            hit = skin.ray_cast(c + d * 6.0, -d, 6.0)
            if hit[0] is None: ring.append(None); continue
            r = 6.0 - hit[3]                                           # distance from the centre, on this side
            if r < 0.4: ring.append(None); continue                   # through an opening to the far side
            if block:
                bh = block.ray_cast(c, d, r)
                if bh[0] is not None and bh[3] < r - 0.20: ring.append(None); continue
            ring.append((c, d, r))
        rings.append(ring)
    lattice(rings, axis, mesh, FRAME_OUT, FRAME_IN, longerons=True)


def lattice(rings, axis, mesh, f_out, f_in, longerons):
    """Lattice frames and stringers from rings of (centre, direction, skin radius) points, one ring per station, the
    same angles on each. A frame is two thin chords round the ring (f_out and f_in inside the skin) with a zig-zag of
    diagonals between them: small members, nothing solid, so where the skin turns sharply a member can't fan out into
    a blade. Stringers join the same angle on neighbouring frames, just inside the outer chord."""
    n = len(next(r for r in rings if r)) if any(rings) else 0
    fwd = _unit(axis)
    mid_r = (f_out + f_in) / 2
    for ring in rings:
        if not ring: continue
        for i in range(n):
            p, q = ring[i], ring[(i + 1) % n]
            if not p or not q or abs(p[2] - q[2]) > 0.25: continue
            fp = min(f_in, p[3]) if len(p) > 3 else f_in                         # no deeper than the wall allows
            fq = min(f_in, q[3]) if len(q) > 3 else f_in
            fo_p, fo_q = min(f_out, fp * 0.4), min(f_out, fq * 0.4)
            po, pi_ = p[0] + p[1] * (p[2] - fo_p), p[0] + p[1] * (p[2] - fp)
            qo, qi = q[0] + q[1] * (q[2] - fo_q), q[0] + q[1] * (q[2] - fq)
            mesh.box(po, qo, fwd, 0.03, 0.02, 0, caps=False)                     # outer chord
            mesh.box(pi_, qi, fwd, 0.03, 0.02, 0, caps=False)                    # inner chord
            a0, a1 = (po, qi) if i % 2 == 0 else (pi_, qo)
            mesh.box(a0, a1, fwd, 0.02, 0.015, 0, caps=False)                    # diagonal
    for k in range(len(rings) - 1):
        r0, r1 = rings[k], rings[k + 1]
        if not r0 or not r1: continue
        for i in range(0, n, 2):
            p, q = r0[i], r1[i]
            if not p or not q or abs(p[2] - q[2]) > 0.25: continue
            heavy = longerons and i in (0, n // 2)                                # the two longerons along the sides
            s0 = p[0] + p[1] * (p[2] - f_out + 0.01); s1 = q[0] + q[1] * (q[2] - f_out + 0.01)
            mesh.box(s0, s1, -p[1], 0.06 if heavy else 0.03, 0.05 if heavy else 0.02, 0, caps=False)


def pod_body(cuts, a, b, axis, mesh, front):
    """Engine pods: the same lattice round each nacelle. A cut through a pod finds both nacelles and the pylon: the
    pylon (a thin, flat section) gets no frame, and each nacelle's frames and stringers follow that nacelle from one
    station to the next (matched by centre), never jumping across to the other one as they used to."""
    tracks = []                                                       # per nacelle: rings by station
    cuts = [[] if loops and max(p[axis] for p in loops[0]) > front - 1.2 else loops for loops in cuts]  # none in the lips
    def box2(L):
        xs = [p[a] for p in L]; ys = [p[b] for p in L]
        return min(xs), max(xs), min(ys), max(ys)
    def inside2(L, M):                                                # L's outline within M's (bounding boxes)
        l, m = box2(L), box2(M)
        return l is not m and m[0] < l[0] and l[1] < m[1] and m[2] < l[2] and l[3] < m[3]
    for k, loops in enumerate(cuts):
        for L in loops:
            # A nacelle is a hollow tube: its cut is the outer cowl and, inside it, the intake duct. Only the cowl gets a
            # frame, and only as deep as the wall between them allows (the first lattice put rings in the duct's airflow).
            if any(inside2(L, M) for M in loops if M is not L): continue
            ducts = [M for M in loops if M is not L and inside2(M, L)]
            if _flat(L, a, b) or _extent(L, a, b) < 0.15: continue  # the pylon, or a sliver
            xs = [p[a] for p in L]; ys = [p[b] for p in L]
            asp = (max(xs) - min(xs)) / max(max(ys) - min(ys), 1e-6)
            if not 0.6 < asp < 1.6: continue                          # both nacelles in one figure-8 outline (at the
                                                                      # intakes): shrinking that toward its middle
                                                                      # made the hourglass plates across the pod
            c = sum(L, Vector()) / len(L)
            pts = by_angle(L, a, b, 24)
            inner = [by_angle(M, a, b, 24) for M in ducts]
            ring = []
            for i, p in enumerate(pts):
                if p is None or (p - c).length < 0.1: ring.append(None); continue
                r = (p - c).length
                wall = min([r - (q[i] - c).length for q in inner if q[i] is not None] or [1.0])
                # the frame keeps 1 cm clear of the duct; where the cowl is thinner than 5 cm there is none
                ring.append((c, (p - c).normalized(), r, wall - 0.01) if wall >= 0.05 else None)
            t = next((t for t in tracks if t[-1][0] == k - 1 and (t[-1][1] - c).length < 0.6), None)
            if t is None: t = []; tracks.append(t)
            t.append((k, c, ring))
    for t in tracks:
        lattice([r for _, _, r in t], axis, mesh, 0.03, 0.11, longerons=False)


# ------------------------------------------------------------------ one part

def build(part):
    o = bpy.data.objects.get(part)
    if o is None or o.type != 'MESH': return None
    bm = world_bm(o)
    lo = Vector([min(v.co[i] for v in bm.verts) for i in range(3)])
    hi = Vector([max(v.co[i] for v in bm.verts) for i in range(3)])
    size = hi - lo
    # Cut across the span of a lifting surface (X; the fin and rudder: Z), across the length of a body (Y)
    axis = 2 if part in ('tail', 'rudder') else 1 if part in ('B52', 'fuselage_F', 'fuselage_R') or part.startswith('pod') else 0
    a, b = frame2(axis)
    thin = axis != 1                                                 # spars along a wing; stringers round a body
    pitch = 0.7 if thin else 0.6
    n_cuts = max(3, int(size[axis] / pitch))
    stations = [lo[axis] + size[axis] * (k + 0.5) / n_cuts for k in range(n_cuts)]
    mesh = Mesh()
    cuts = [section(bm, axis, st) for st in stations]

    # The main loop of each cut: a wing's own section, not a tip tank's, pylon's or fairing's beside it; a body's largest
    def pick(loops):
        cand = [L for L in loops if _flat(L, a, b)] if thin else loops
        cand = cand or loops
        return max(cand, key=lambda L: _span(L, a) if thin else _extent(L, a, b)) if cand else None
    main = [pick(l) for l in cuts]
    up = _unit(b)

    if thin:
        # Lifting surfaces: everything is placed by chord fraction along straight leading-edge and chord lines fitted
        # through the sections, and hung between the skins measured at that chord (the lowest and highest crossing of
        # the section there), 3 cm in from each. Where the section is under 10 cm thick (toward the trailing edge, and
        # through the cut-outs for flaps and spoilers) nothing is built, so nothing can poke through the skin.
        pts = [(stations[k], min(p[a] for p in L), _span(L, a)) for k, L in enumerate(main) if L]
        if len(pts) < 2:
            bm.free(); return None
        def fit(i):
            xs = [q[0] for q in pts]; ys = [q[i] for q in pts]
            mx, my = sum(xs) / len(xs), sum(ys) / len(ys)
            vx = sum((x - mx) ** 2 for x in xs) or 1e-9
            k_ = sum((x - mx) * (y - my) for x, y in zip(xs, ys)) / vx
            return lambda x: my + k_ * (x - mx)
        le_fit, ch_fit = fit(1), fit(2)

        def skins(k, f):
            """(bottom, top) inner faces of the skins at chord fraction f of station k, or None where too thin."""
            L = main[k]
            if not L: return None
            cr = crossing(L, a, le_fit(stations[k]) + f * ch_fit(stations[k]), b)
            if not cr or (cr[1] - cr[0]).length < 0.10: return None
            return cr[0] + up * 0.03, cr[1] - up * 0.03

        SPARS = (0.15, 0.65)
        RIB = [0.06 + i * (0.65 - 0.06) / 10 for i in range(11)]          # rib stations along the chord, spar to spar
        RIB = sorted(set([round(f, 3) for f in RIB] + list(SPARS)))
        STRINGERS = (0.25, 0.35, 0.45, 0.55)
        def in_hull(k):                                                 # a wing root's stub inside the fuselage
            return part.startswith('wingroot_') and abs(stations[k]) < HULL_HALF
        for k in range(len(stations)):
            if in_hull(k): continue                                         # inside the hull: spars only, no ribs
            sk = [(f, skins(k, f)) for f in RIB]
            sk = [(f, s_) for f, s_ in sk if s_]
            # top and bottom flanges (cap strips following each skin)
            for (f0, s0), (f1, s1) in zip(sk, sk[1:]):
                if f1 - f0 > 0.12: continue                                # a gap (too thin there): leave it open
                for t in (0, 1): mesh.box(s0[t], s1[t], up, 0.05, 0.015, 0)
            # uprights and a Warren truss of diagonals between them: the rib's lightened web
            for i, (f, s_) in enumerate(sk):
                if f in SPARS or i % 2 == 0: mesh.box(s_[0], s_[1], _unit(a), 0.03, 0.02, 0)
                if i + 1 < len(sk) and sk[i + 1][0] - f <= 0.12:
                    lo_, hi_ = (s_[0], sk[i + 1][1][1]) if i % 2 == 0 else (s_[1], sk[i + 1][1][0])
                    mesh.box(lo_, hi_, _unit(a), 0.025, 0.015, 0)
        for k in range(len(stations) - 1):
            for f in SPARS:                                                 # spar webs, with caps along both skins
                c0, c1 = skins(k, f), skins(k + 1, f)
                if not c0 or not c1: continue
                mesh.add([c0[0], c1[0], c1[1], c0[1]], 0)
                for t in (0, 1): mesh.box(c0[t], c1[t], up, 0.10, 0.03, 0)
            for f in STRINGERS:                                             # stringers along the inside of each skin
                if in_hull(k) or in_hull(k + 1): break
                c0, c1 = skins(k, f), skins(k + 1, f)
                if not c0 or not c1: continue
                for t in (0, 1):
                    d = c1[t] - c0[t]
                    if abs(d.dot(up)) > 0.3 * d.length: continue           # never a jump between unlike sections
                    mesh.box(c0[t], c1[t], up, 0.035, 0.025, 0)
        if part.startswith('wingroot_'):
            # The carry-through: inside the fuselage the wing is only its two spars, running on from the root rib to the
            # centre line, where they meet the other wing's (blender_prep.py removes the wing skin inside the hull).
            # Everything else in the hull stays open.
            inner = sorted(range(len(stations)), key=lambda k: abs(stations[k]))
            for f in SPARS:
                k0 = next((k for k in inner if skins(k, f)), None)
                if k0 is None: continue
                s0 = skins(k0, f)
                c0 = [p.copy() for p in s0]
                for p in c0: p.x = 0.0                                      # the same height and chord, at x = 0
                mesh.add([s0[0], c0[0], c0[1], s0[1]], 0)
                for t in (0, 1): mesh.box(s0[t], c0[t], up, 0.10, 0.03, 0)
    elif part in HULL_PARTS:
        hull_body(part, bm, stations, a, b, axis, mesh)
    else:
        pod_body(cuts, a, b, axis, mesh, hi[axis])          # hi[axis]: the front of the intake lips

    # Last check: no member may show through the skin. Any structure face with a corner outside the part's skin (by its
    # nearest skin face and that face's normal, over 5 mm) is dropped; what's left of a member ends inside the skin.
    bm.normal_update()
    near = BVHTree.FromBMesh(bm)
    outside = {}
    def out(i):
        if i not in outside:
            loc, nrm, _, _ = near.find_nearest(Vector(mesh.v[i]))
            outside[i] = loc is not None and (Vector(mesh.v[i]) - loc).dot(nrm) > 0.005
        return outside[i]
    keep = [(f, m) for f, m in zip(mesh.f, mesh.m) if not any(out(i) for i in f)]
    dropped = len(mesh.f) - len(keep)
    mesh.f = [f for f, _ in keep]; mesh.m = [m for _, m in keep]
    if dropped: print(f'STRUCT {part}: {dropped} member faces outside the skin dropped')

    # Inner skin: the part's own faces turned inward, 2 cm in, or a third of the way to the skin opposite where the
    # part is thinner than that (trailing edges), so it never comes out through the other side.
    tree = BVHTree.FromBMesh(bm)
    vbase = len(mesh.v)
    bm.normal_update()
    thinned = 0
    for v in bm.verts:
        n_ = v.normal
        d = 0.02
        hit = tree.ray_cast(v.co - n_ * 1e-3, -n_, 0.2)
        if hit[0] is not None and hit[3] < 0.06: d = hit[3] / 3; thinned += 1
        mesh.v.append(tuple(v.co - n_ * d))
    for f in bm.faces:
        mesh.f.append([vbase + v.index for v in reversed(f.verts)]); mesh.m.append(1)
    moved = thinned
    # Nothing inside a gear bay: the outer wing's inner skin, 2 cm under the top skin, ran through the tip-gear wells
    # (their roof is 4 mm under it) and its frames showed in the open wells.
    inb = {}
    def bay(i):
        if i not in inb: inb[i] = in_bay(mesh.v[i])
        return inb[i]
    keep = [(f, m) for f, m in zip(mesh.f, mesh.m) if not any(bay(i) for i in f)]
    if len(keep) < len(mesh.f): print(f'STRUCT {part}: {len(mesh.f) - len(keep)} faces inside a gear bay dropped')
    mesh.f = [f for f, _ in keep]; mesh.m = [m for _, m in keep]
    bm.free()
    obj = mesh.write(part + '_struct', o)
    obj.hide_render = False
    print(f'STRUCT {part}: inner skin thinned at {moved} vertices')
    return obj, len(stations), thin


def _unit(i):
    v = Vector((0, 0, 0)); v[i] = 1.0
    return v


def _span(L, i):
    xs = [p[i] for p in L]
    return max(xs) - min(xs)


def _flat(L, a, b):
    xs = [p[a] for p in L]; ys = [p[b] for p in L]
    w, h = max(xs) - min(xs), max(ys) - min(ys)
    return min(w, h) < 0.35 * max(w, h)


def _extent(L, a, b):
    xs = [p[a] for p in L]; ys = [p[b] for p in L]
    return (max(xs) - min(xs)) * (max(ys) - min(ys))


# ------------------------------------------------------------------ all parts

for old in [o for o in bpy.data.objects if o.name.endswith('_struct')]:
    bpy.data.objects.remove(old, do_unlink=True)
bpy.context.view_layer.update()
total = 0
for p in PARTS:
    r = build(p)
    if r is None: print('STRUCT', p, 'missing'); continue
    o, cuts, thin = r
    tris = sum(len(poly.vertices) - 2 for poly in o.data.polygons)
    total += tris
    print(f'STRUCT {p}: {"ribs, spars, stringers" if thin else "frames, stringers"}, {cuts} stations, {tris} triangles')
print(f'STRUCTURE {len(PARTS)} parts, {total} triangles')
if SAVE: bpy.ops.wm.save_as_mainfile(filepath=bpy.data.filepath)
