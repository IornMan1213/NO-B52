"""
Scratch-built B-52J upper flight deck (and the lower deck under it), added under the 'cockpit' part of B52_parts.blend.

Run:  blender -b blender/out/B52_parts.blend --python tools/blender_cockpit.py -- <tex_dir>

Everything is fitted to the hull: the B-52's flight deck narrows forward and its walls lean in, so the walls, consoles,
panels and glareshield are laid out from the skin itself (hx(), roof(), nose() below: rays from outside the cockpit
section and forward fuselage) rather than on a box. Big surfaces are grids that follow the skin 4 cm inside it; the
switch panels on the walls are flat panels fitted against it. The last step still pulls anything left within 3 cm of
the skin inside it, and prints how much it had to move (it should be next to nothing).

Layout (Blender frame, metres, +Y forward; pilot x -0.55, copilot x +0.55, floor z -0.50):
  main instrument panel   a face leaning back 12 deg at y 25.55 (Instruments.cs turns the needles about its normal),
                          with what is mounted in it listed once in tools/make_cockpit_atlas.py (INSTR): a primary
                          flight display and a tactical display per pilot, the engine display (EICAS) between them,
                          standby instruments outboard, the engine fire handles along the top
  glareshield             a hood over the panel cut to the windscreen line
  side consoles, wall switch panels, centre aisle stand with the throttle quadrant, overhead panel
Animated objects, wired up in Unity:
  yoke_L / yoke_R              Cockpit.joysticks  (pitch about local X, roll about local Z)
  throttle_1 .. throttle_8     Cockpit.throttles  (rotation about local X)
  mfd_C_screen                 Cockpit.tacScreenRender (live tactical map)
  needle_asi/alt/alt1k/alt10k/vsi, adi_card    B52Systems.Instruments
  eye_L / eye_R                Pilot seat camera positions
"""
import bpy, bmesh, sys, os, math, json
from mathutils import Vector, Matrix
from mathutils.bvhtree import BVHTree

ARGS = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
TEX = ARGS[0] if ARGS else os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'blender', 'tex')

cockpit = bpy.data.objects['cockpit']
root = bpy.data.objects.new('cockpit_int', None)
bpy.context.scene.collection.objects.link(root)
root.matrix_world = Matrix.Translation((0, 24.5, 0))
bpy.context.view_layer.update()
root.parent = cockpit; root.matrix_world = Matrix.Translation((0, 24.5, 0))
bpy.context.view_layer.update()


# ------------------------------------------------------------------ materials
def mat(name, rgb, metal=0.0, rough=0.6, img=None, emit=0.0):
    m = bpy.data.materials.new(name); m.use_nodes = True
    nt = m.node_tree; p = nt.nodes['Principled BSDF']
    p.inputs['Base Color'].default_value = (*rgb, 1)
    p.inputs['Metallic'].default_value = metal; p.inputs['Roughness'].default_value = rough
    if img:
        t = nt.nodes.new('ShaderNodeTexImage'); t.image = bpy.data.images.load(os.path.join(TEX, img))
        nt.links.new(t.outputs['Color'], p.inputs['Base Color'])
        if emit:
            nt.links.new(t.outputs['Color'], p.inputs['Emission Color'])
            p.inputs['Emission Strength'].default_value = emit
    return m


M = {
    'panel': mat('CP_PanelGrey', (1, 1, 1), 0.25, 0.5, 'cp_paint.png'),   # grey panels and consoles, worn (make_cockpit_atlas.py)
    'black': mat('CP_Black', (0.02, 0.02, 0.02), 0.0, 0.5),
    'glare': mat('CP_Glareshield', (1, 1, 1), 0.0, 0.9, 'cp_glare.png'),     # black crinkle finish
    'wall': mat('CP_Wall', (1, 1, 1), 0.0, 0.85, 'wall_quilt.png'),       # quilted insulation (make_cockpit_trim.py)
    'floor': mat('CP_Floor', (1, 1, 1), 0.2, 0.9, 'floor_tread.png'),
    'frame': mat('CP_Frame', (1, 1, 1), 0.2, 0.55, 'cp_frame.png'),      # window frames from inside: painted, riveted metal (make_cockpit_atlas.py)
    'metal': mat('CP_Metal', (0.32, 0.33, 0.35), 0.8, 0.4),            # dark worn metal: columns, pedals, levers
    'knob': mat('CP_Knob', (0.62, 0.62, 0.60), 0.0, 0.45),              # throttle knobs, bezel keys
    'seat': mat('CP_Seat', (1, 1, 1), 0.0, 0.9, 'seat.png'),
    'yellow': mat('CP_EjectHandle', (0.9, 0.75, 0.05), 0.0, 0.5),
    'pfd': mat('CP_MFD_PFD', (1, 1, 1), 0.0, 0.2, 'mfd_pfd.png', 1.5),
    'eng': mat('CP_MFD_ENG', (1, 1, 1), 0.0, 0.2, 'mfd_engines.png', 1.5),
    'tsd': mat('CP_MFD_TSD', (1, 1, 1), 0.0, 0.2, 'mfd_tsd.png', 1.5),
    'gauge': mat('CP_GaugeFace', (0.02, 0.02, 0.02), 0.0, 0.3),
    'asi': mat('CP_DialASI', (1, 1, 1), 0.0, 0.4, 'dial_asi.png', 0.6),
    'alt': mat('CP_DialALT', (1, 1, 1), 0.0, 0.4, 'dial_alt.png', 0.6),
    'vsi': mat('CP_DialVSI', (1, 1, 1), 0.0, 0.4, 'dial_vsi.png', 0.6),
    'adi': mat('CP_ADICard', (1, 1, 1), 0.0, 0.4, 'adi_card.png', 0.6),
    'needle': mat('CP_Needle', (0.95, 0.95, 0.95), 0.0, 0.4),
    'white': mat('CP_White', (0.9, 0.9, 0.9), 0.0, 0.5),
    'ann': mat('CP_MFD_Annunciators', (1, 1, 1), 0.0, 0.3, 'annunciators.png', 1.0),   # CP_MFD_*: emissive in Unity
    # every switch panel (tools/make_cockpit_atlas.py: cp_atlas.png, its normal map, and cp_controls.json)
    'atlas': mat('CP_Atlas', (1, 1, 1), 0.15, 0.55, 'cp_atlas.png'),
    'dials': mat('CP_Dials', (1, 1, 1), 0.0, 0.35, 'cp_dials.png'),        # every round instrument face
    'red': mat('CP_Red', (0.55, 0.04, 0.03), 0.0, 0.45),                   # fire handles, switch guards
    'strap': mat('CP_Strap', (0.20, 0.19, 0.12), 0.0, 0.9),                # harness webbing
    'rubber': mat('CP_Rubber', (0.05, 0.05, 0.05), 0.0, 0.85),             # yoke grips, pedal treads
}
ATLAS = json.load(open(os.path.join(TEX, 'cp_controls.json')))
_n = bpy.data.images.load(os.path.join(TEX, 'cp_atlas_n.png')); _n.use_fake_user = True   # its normal map: carried to Unity (blender_export.py copies the .blend's images)
DIAL = {d['name'] + ('' if d['name'] != '%RPM' else str(d['slot'])): d['uv'] for d in ATLAS['dials']}


# ------------------------------------------------------------------ the hull
# The cockpit section and forward fuselage, in world space. Every ray starts outside them and comes in, so the first
# thing it meets is the skin (the source model also has a few pieces of its own simple interior 0.3-0.7 m inside it).
hull_bm = bmesh.new()
for name in ('cockpit', 'fuselage_F'):
    h = bpy.data.objects[name]
    tmp = bmesh.new(); tmp.from_mesh(h.data); tmp.transform(h.matrix_world)
    # the EVS camera blisters under the chin are separate shells pushed into the skin: their inner faces lie inside the
    # lower deck and are not its walls, so they stay out of the hull the interior is fitted to
    evs = {i for i, m in enumerate(h.data.materials) if m and m.name.startswith('EVS')}
    bmesh.ops.delete(tmp, geom=[f for f in tmp.faces if f.material_index in evs], context='FACES')
    me_tmp = bpy.data.meshes.new('_hull'); tmp.to_mesh(me_tmp); tmp.free()
    hull_bm.from_mesh(me_tmp); bpy.data.meshes.remove(me_tmp)
HULL = BVHTree.FromBMesh(hull_bm)


def hx(y, z):
    """Half-width of the inside of the hull at (y, z)."""
    hit = HULL.ray_cast(Vector((4.0, y, z)), Vector((-1, 0, 0)), 4.0)[0]
    return hit.x if hit else 0.0


def roof(x, y):
    hit = HULL.ray_cast(Vector((x, y, 4.0)), Vector((0, 0, -1)), 4.0)[0]
    return hit.z if hit else 0.0


def nose(x, z):
    """How far forward the cabin goes at (x, z): the windscreen or the nose skin."""
    hit = HULL.ray_cast(Vector((x, 32.0, z)), Vector((0, -1, 0)), 10.0)[0]
    return hit.y if hit else 25.5


def fit_x(x, y, z, inset):
    """x moved in so that it stays `inset` inside the hull's side."""
    lim = max(hx(y, z) - inset, 0.0)
    return math.copysign(min(abs(x), lim), x)


def lerp(a, b, t): return a + (b - a) * t
def lin(a, b, n): return [lerp(a, b, i / (n - 1)) for i in range(n)]


# ------------------------------------------------------------------ primitives
def obj(name, parent, origin=(0, 0, 0)):
    me = bpy.data.meshes.new(name)
    o = bpy.data.objects.new(name, me); bpy.context.scene.collection.objects.link(o)
    o.matrix_world = Matrix.Translation(origin)
    bpy.context.view_layer.update()
    mw = o.matrix_world.copy(); o.parent = parent; o.matrix_world = mw
    bpy.context.view_layer.update()
    return o


def frame3(right, up, n):
    """Rotation whose X, Y, Z axes are right, up, n."""
    return Matrix((right, up, n)).transposed().to_3x3()


class Builder:
    """Accumulates geometry in world coordinates, then writes it into an object relative to its origin."""

    def __init__(self):
        self.bm = bmesh.new(); self.mats = []

    def mi(self, m):
        if m not in self.mats: self.mats.append(m)
        return self.mats.index(m)

    def box(self, c, size, m, rot=None, bevel=None):
        mtx = Matrix.Translation(c) @ (rot.to_4x4() if rot else Matrix.Identity(4)) @ Matrix.Diagonal((*size, 1))
        r = bmesh.ops.create_cube(self.bm, size=1.0, matrix=mtx, calc_uvs=True)
        idx = self.mi(m)
        for v in r['verts']:
            for f in v.link_faces: f.material_index = idx
        if bevel is None: bevel = min(0.012, min(size) * 0.25) if min(size) >= 0.03 else 0.0
        if bevel > 0:
            edges = list({e for v in r['verts'] for e in v.link_edges})
            bmesh.ops.bevel(self.bm, geom=r['verts'] + edges, offset=bevel, segments=1, affect='EDGES',
                            clamp_overlap=True, material=-1, profile=0.5)
        return r

    def quad(self, pts, m, uv=((0, 0), (1, 0), (1, 1), (0, 1))):
        vs = [self.bm.verts.new(p) for p in pts]
        f = self.bm.faces.new(vs); f.material_index = self.mi(m)
        lay = self.bm.loops.layers.uv.verify()
        for l, t in zip(f.loops, uv): l[lay].uv = t
        return f

    def grid(self, rows, m, toward):
        """A surface through rows of points (all rows the same length), each face turned to face toward(centre)."""
        idx = self.mi(m)
        vs = [[self.bm.verts.new(p) for p in r] for r in rows]
        for i in range(len(rows) - 1):
            for j in range(len(rows[i]) - 1):
                q = [vs[i][j], vs[i + 1][j], vs[i + 1][j + 1], vs[i][j + 1]]
                if len({v.co.to_tuple(4) for v in q}) < 3: continue
                f = self.bm.faces.new(q); f.normal_update()
                if f.normal.length < 1e-6: continue
                if f.normal.dot(toward(f.calc_center_median())) < 0: f.normal_flip()
                f.material_index = idx

    def region(self, name, centre, right, up, lift=0.0012):
        """A panel from the atlas (make_cockpit_atlas.py) laid on a surface: centred at centre, its image's right along
        `right` and its top along `up` (unit vectors), at the region's real size, lifted off the surface by `lift`."""
        x, y, W, H, wm, hm = ATLAS['regions'][name]
        S = ATLAS['size']
        u0, u1 = x / S, (x + W) / S
        v1, v0 = 1 - y / S, 1 - (y + H) / S
        right, up = Vector(right).normalized(), Vector(up).normalized()
        n = right.cross(up).normalized()
        c = Vector(centre) + n * lift
        pts = [c - right * wm / 2 - up * hm / 2, c + right * wm / 2 - up * hm / 2, c + right * wm / 2 + up * hm / 2, c - right * wm / 2 + up * hm / 2]
        self.quad(pts, M['atlas'], uv=((u0, v0), (u1, v0), (u1, v1), (u0, v1)))
        PLACED[name] = (c, right, up, n, wm, hm)

    def disc(self, centre, right, up, r, m, uv=(0, 0, 1, 1), seg=24):
        """Round dial face (a fan of triangles), its texture mapped from the square uv rect (u0, v0, u1, v1)."""
        lay = self.bm.loops.layers.uv.verify(); idx = self.mi(m)
        u0, v0, u1, v1 = uv; uc, vc = (u0 + u1) / 2, (v0 + v1) / 2
        ring = [(math.cos(2 * math.pi * i / seg), math.sin(2 * math.pi * i / seg)) for i in range(seg)]
        c = self.bm.verts.new(centre); vs = [self.bm.verts.new(centre + right * (r * a) + up * (r * b)) for a, b in ring]
        for i in range(seg):
            j = (i + 1) % seg
            f = self.bm.faces.new((c, vs[i], vs[j])); f.material_index = idx
            for l, (a, b) in zip(f.loops, ((0, 0), ring[i], ring[j])):
                l[lay].uv = (uc + a * (u1 - u0) / 2, vc + b * (v1 - v0) / 2)

    def cyl(self, p0, p1, r, m, seg=16, r1=None):
        d = Vector(p1) - Vector(p0)
        rot = d.to_track_quat('Z', 'Y').to_matrix().to_4x4()
        mtx = Matrix.Translation((Vector(p0) + Vector(p1)) / 2) @ rot
        res = bmesh.ops.create_cone(self.bm, cap_ends=True, segments=seg, radius1=r, radius2=r if r1 is None else r1,
                                    depth=d.length, matrix=mtx)
        idx = self.mi(m)
        for v in res['verts']:
            for f in v.link_faces: f.material_index = idx

    def tube(self, pts, r, m, seg=12):
        """A bent tube through the points (a cylinder per leg, a ball at each bend)."""
        for a, b_ in zip(pts, pts[1:]): self.cyl(a, b_, r, m, seg)
        for p in pts[1:-1]:
            res = bmesh.ops.create_uvsphere(self.bm, u_segments=seg, v_segments=6, radius=r, matrix=Matrix.Translation(p))
            idx = self.mi(m)
            for v in res['verts']:
                for f in v.link_faces: f.material_index = idx

    def torus(self, centre, normal, R, r, m, seg=28, rseg=8):
        n = Vector(normal).normalized()
        u = n.orthogonal().normalized(); w = n.cross(u)
        rings = []
        for i in range(seg):
            a = 2 * math.pi * i / seg
            cdir = u * math.cos(a) + w * math.sin(a)
            rings.append([self.bm.verts.new(Vector(centre) + cdir * (R + r * math.cos(2 * math.pi * j / rseg)) + n * r * math.sin(2 * math.pi * j / rseg))
                          for j in range(rseg)])
        idx = self.mi(m)
        for i in range(seg):
            for j in range(rseg):
                k, i2 = (j + 1) % rseg, (i + 1) % seg
                f = self.bm.faces.new((rings[i][j], rings[i][k], rings[i2][k], rings[i2][j])); f.material_index = idx

    def write(self, o):
        self.bm.normal_update()
        self.bm.transform(o.matrix_world.inverted())
        self.bm.to_mesh(o.data); self.bm.free()
        for m in self.mats: o.data.materials.append(m)
        return o


PLACED = {}        # atlas regions laid in the cockpit: where each one's controls go (add_controls)


def rx(deg):
    return Matrix.Rotation(math.radians(deg), 3, 'X')


# ------------------------------------------------------------------ crew positions
Y0 = 21.30                     # aft bulkhead (behind the EWO/gunner stations)
FLOOR = -0.50
# Where the pilot's head goes. Measured in game: the pilot model is placed so its helmet camera point is at eye_L, and
# in its seated pose the game camera then sits 0.091 m ahead of and 0.026 m above that point, with the hip joint 0.156 m
# ahead of and 0.716 m below the camera. So everything follows from where the camera should be.
CAM_Y, CAM_Z = 24.40, 0.80
EYE_Y, EYE_Z = CAM_Y - 0.091, CAM_Z - 0.026
HIP_Y, HIP_Z = CAM_Y + 0.156, CAM_Z - 0.716
INSET = 0.04                   # linings sit this far inside the skin (the inner copy of the skin, cp_skin, is at 0.03)


# ------------------------------------------------------------------ shell: linings, floor, bulkhead, ceiling frames
def sill(y):
    """Top of the wall lining: just under the side windows, dropping to the windscreen's lower corners."""
    return 0.42 if y < 25.0 else lerp(0.42, 0.33, min((y - 25.0) / 0.6, 1.0))


ROOF_X = 0.55                  # the ceiling lining spans |x| < ROOF_X; the walls run up to meet it
SIDE_TOP_Y = 23.92             # aft of this the walls run up to the ceiling (no windows)
CEIL_END = 24.20               # the ceiling stops behind the pilots' heads: the eyebrow windows stay clear


def wall_top(y):
    """Height where the wall lining meets the ceiling lining."""
    lo, hi = 0.4, 1.4
    for _ in range(24):
        m = (lo + hi) / 2
        if hx(y, m) - INSET > ROOF_X: lo = m
        else: hi = m
    return lo


def arch(y, inset, n_side=10, n_roof=9, z0=FLOOR + 0.02):
    """The cabin's cross-section at y, `inset` inside the skin: up the left wall, across the ceiling, down the right."""
    zt = wall_top(y)
    left = [Vector((-(hx(y, z) - inset), y, z)) for z in lin(z0, zt, n_side)]
    top = [Vector((x, y, roof(x, y) - inset)) for x in lin(-ROOF_X + 0.02, ROOF_X - 0.02, n_roof)]
    right = [Vector((hx(y, z) - inset, y, z)) for z in lin(zt, z0, n_side)]
    return left + top + right


axis = lambda c: Vector((-c.x, 0, 0.35 - c.z))                       # toward the middle of the cabin
b = Builder()
# aft cabin: walls and ceiling in one piece
b.grid([arch(y, INSET) for y in lin(Y0 + 0.02, SIDE_TOP_Y, 14)], M['wall'], axis)
# ceiling from there to just behind the pilots' heads
b.grid([[Vector((x, y, roof(x, y) - INSET)) for x in lin(-ROOF_X + 0.02, ROOF_X - 0.02, 9)] for y in lin(SIDE_TOP_Y, CEIL_END, 4)],
       M['wall'], lambda c: Vector((0, 0, -1)))
# walls under the side windows, on to the main panel
for s in (-1, 1):
    b.grid([[Vector((s * (hx(y, z) - INSET), y, z)) for z in lin(FLOOR + 0.02, sill(y), 10)] for y in lin(SIDE_TOP_Y, 25.62, 16)],
           M['wall'], axis)
# floor, out to the walls
b.grid([[Vector((x * (hx(y, FLOOR + 0.03) - INSET), y, FLOOR)) for x in lin(-1, 1, 9)] for y in lin(Y0, 25.95, 20)],
       M['floor'], lambda c: Vector((0, 0, 1)))
# aft bulkhead with the crawlway door to the aft fuselage
b.grid([[Vector((s_ * (hx(Y0 + 0.02, z) - INSET), Y0 + 0.02, z)) for s_ in lin(-1, 1, 9)] for z in lin(FLOOR, roof(0, Y0) - INSET, 12)],
       M['wall'], lambda c: Vector((0, 1, 0)))
b.box((0, Y0 + 0.04, FLOOR + 0.55), (0.56, 0.02, 1.0), M['black'])
b.box((0, Y0 + 0.055, FLOOR + 0.55), (0.04, 0.03, 0.16), M['metal'])            # its handle
# ceiling frames: the airframe's formers across the cabin, painted, over the insulation
for yy in (21.75, 22.25, 22.75, 23.25, 23.75):
    inner = arch(yy, INSET + 0.045, z0=FLOOR + 0.35); outer = arch(yy, INSET - 0.005, z0=FLOOR + 0.35)
    b.grid([[p + Vector((0, -0.022, 0)) for p in inner], [p + Vector((0, 0.022, 0)) for p in inner]], M['panel'], axis)
    for dy in (-0.022, 0.022):
        b.grid([[p + Vector((0, dy, 0)) for p in inner], [p + Vector((0, dy, 0)) for p in outer]], M['panel'],
               lambda c, dy=dy: Vector((0, dy, 0)))
# ladder well to the lower deck (radar navigator / navigator), between the two crew rows
b.box((0.0, 22.95, FLOOR + 0.004), (0.7, 0.6, 0.01), M['black'], bevel=0)
for lx in (-0.33, 0.33):
    b.box((lx, 22.95, FLOOR + 0.008), (0.04, 0.6, 0.01), M['yellow'], bevel=0)    # well edge markings
shell = b.write(obj('cp_shell', root, (0, 24.4, 0)))

# Interior skin: the cockpit's own non-glass faces in the cabin region, inset 3 cm and flipped so they face
# inward. This gives real window frames from inside (the exterior skin is culled from behind in Unity).
src = cockpit
glass_idx = {i for i, m in enumerate(src.data.materials) if m and m.name == 'Glass'}
bm = bmesh.new(); bm.from_mesh(src.data); bm.transform(src.matrix_world)
_outer = BVHTree.FromBMesh(bm)
def on_hull(f):
    # Only the hull itself: the source's cockpit section also has a few bits 0.3-0.7 m inside it (simple interior
    # pieces), and copied inward they hung across the cabin. A face is hull if the outermost surface along the ray
    # from the cabin axis through it is within 8 cm.
    c = f.calc_center_median()
    d = Vector((c.x, 0.0, c.z - 0.35))
    if d.length < 0.05: return False
    d.normalize()
    hit = _outer.ray_cast(Vector((0.0, c.y, 0.35)) + d * 6.0, -d, 6.0)[0]
    return hit is not None and (hit - c).length < 0.08
# Forward it runs to the nose tip: the game hides the cockpit section's outer skin in cockpit view, so without the
# inner copy there the pilot saw the runway through the nose under the windscreen.
NOSE_Y = 25.5
# Except the windscreen wipers (small separate pieces on the nose): copied inward, one hung in the pilot's windscreen.
wipers, seen = set(), set()
for f0 in bm.faces:
    if f0 in seen or f0.material_index in glass_idx: continue
    isl, stack = [], [f0]; seen.add(f0)
    while stack:
        f = stack.pop(); isl.append(f)
        stack += [g for e in f.edges for g in e.link_faces if g not in seen and not seen.add(g)]
    co = [v.co for f in isl for v in f.verts]
    lo = Vector([min(c[i] for c in co) for i in range(3)]); hi = Vector([max(c[i] for c in co) for i in range(3)])
    if lo.y > NOSE_Y - 0.2 and max(hi - lo) < 0.5:
        wipers.update(isl)
keep = [f for f in bm.faces if f.material_index not in glass_idx and f not in wipers
        and 21.2 < f.calc_center_median().y < 28.0 and f.calc_center_median().z > -0.55
        and (f.calc_center_median().y > NOSE_Y or on_hull(f))]
bmesh.ops.delete(bm, geom=[f for f in bm.faces if f not in set(keep)], context='FACES')
for v in bm.verts:
    axis_pt = Vector((0, v.co.y, 0.35))
    d = (v.co - axis_pt); d.y = 0
    if d.length > 0.05:
        v.co -= d.normalized() * 0.03
bmesh.ops.reverse_faces(bm, faces=bm.faces)
for f in bm.faces: f.material_index = 0
skin = obj('cp_skin', root, (0, 24.5, 0.35))
bm.transform(skin.matrix_world.inverted()); bm.to_mesh(skin.data); bm.free()
skin.data.materials.append(M['frame'])

# ------------------------------------------------------------------ main instrument panel + glareshield
PANEL_Y, PANEL_TILT = 25.55, 12        # the face's plane through (0, PANEL_Y, 0), leaning back 12 degrees
PROT = rx(-PANEL_TILT)
R_ = Vector((1, 0, 0)); U_ = PROT @ Vector((0, 0, 1)); N_ = PROT @ Vector((0, -1, 0))   # right, up the face, toward the crew
O_ = Vector((0, PANEL_Y, 0))
X0, X1, V0, V1 = ATLAS['main_panel']


def face(x, v, lift=0.0):
    return O_ + R_ * x + U_ * v + N_ * lift


b = Builder()
# the face, from the atlas: everything listed in INSTR is drawn on it where it is built below
x_, y_, W_, H_, wm_, hm_ = ATLAS['regions']['main']; S_ = ATLAS['size']
u0, u1, v1_, v0_ = x_ / S_, (x_ + W_) / S_, 1 - y_ / S_, 1 - (y_ + H_) / S_
b.quad([face(X0, V0), face(X1, V0), face(X1, V1), face(X0, V1)], M['atlas'], uv=((u0, v0_), (u1, v0_), (u1, v1_), (u0, v1_)))
PLACED['main'] = (face((X0 + X1) / 2, (V0 + V1) / 2), R_, U_, N_, X1 - X0, V1 - V0)
# beyond the face's ends, the panel carries on out to the walls
for s in (-1, 1):
    rows = []
    for v in lin(V0, V1, 8):
        p = face(0, v); xe = max(hx(p.y, p.z) - 0.035, X1 + 0.002)
        rows.append([face(s * x, v) for x in lin(X1 - 0.001, xe, 3)])
    b.grid(rows, M['panel'], lambda c: N_)
# under it, a toe board sloping forward to the floor, closing off the nose
b.grid([[Vector((fit_x(x, lerp(face(0, V0).y, 25.95, t), lerp(face(0, V0).z, FLOOR, t), INSET), lerp(face(0, V0).y, 25.95, t),
                 lerp(face(0, V0).z, FLOOR, t))) for t in lin(0, 1, 4)] for x in lin(-1.2, 1.2, 25)],
       M['black'], lambda c: Vector((0, -1, 0.4)))

# Glareshield: a padded lip just aft of the face's top edge, a hood from it forward to the windscreen, and its
# underside back to the face. Its top stays under the windscreen's lower edge (z 0.38 measured) all the way across.
LIP_Y, LIP_Z, HOOD_Z = 25.50, 0.332, 0.352
top_edge = face(0, V1 + 0.004)
rows = []
for x in lin(-1.05, 1.05, 43):
    yf = max(nose(x, HOOD_Z) - 0.035, LIP_Y + 0.02)
    rows.append([Vector((fit_x(x, y, HOOD_Z, 0.035), y, HOOD_Z)) for y in lin(LIP_Y, yf, 7)])
b.grid(rows, M['glare'], lambda c: Vector((0, 0, 1)))
rows = []
for x in lin(-1.05, 1.05, 43):
    a_ = Vector((x, LIP_Y + 0.004, LIP_Z - 0.02)); e_ = Vector((x, top_edge.y, top_edge.z))
    rows.append([Vector((fit_x(p.x, p.y, p.z, 0.035), p.y, p.z)) for p in (a_, (a_ + e_) / 2, e_)])
b.grid(rows, M['glare'], lambda c: Vector((0, -0.3, -1)))
lw = hx(LIP_Y, LIP_Z) - 0.05
b.cyl(Vector((-lw, LIP_Y, LIP_Z)), Vector((lw, LIP_Y, LIP_Z)), 0.021, M['glare'], 14)   # padded lip
for s in (-1, 1):
    b.cyl(Vector((s * lw, LIP_Y, LIP_Z)), Vector((s * (lw + 0.006), LIP_Y, LIP_Z)), 0.017, M['glare'], 14)

# what is mounted in the face (make_cockpit_atlas.py INSTR)
screens = []


def screen(it):
    """A display: a bezel frame with its keys standing 18 mm proud of the face, the screen 8 mm back inside it, as a
    separate object (so Unity can target its renderer)."""
    x, v, w, h = it['x'], it['v'], it['w'], it['h']
    m = 0.022                                                      # bezel width
    b.box(face(x, v, 0.003), (w + 2 * m, 0.006, h + 2 * m), M['black'], rot=PROT, bevel=0.002)   # back plate
    for dx, dv, bw, bh in ((0, (h + m) / 2, w + 2 * m, m), (0, -(h + m) / 2, w + 2 * m, m),
                           ((w + m) / 2, 0, m, h), (-(w + m) / 2, 0, m, h)):
        b.box(face(x + dx, v + dv, 0.009), (bw, 0.018, bh), M['black'], rot=PROT, bevel=0.003)
    for i in range(5):                                             # option keys down each side and along the bottom
        for sx in (-1, 1):
            b.box(face(x + sx * (w + m) / 2, v - h / 2 + h * (i + 0.5) / 5, 0.0195), (0.012, 0.004, 0.017), M['knob'], rot=PROT, bevel=0.001)
        b.box(face(x - w / 2 + w * (i + 0.5) / 5, v - (h + m) / 2, 0.0195), (0.020, 0.004, 0.010), M['knob'], rot=PROT, bevel=0.001)
    for sx in (-1, 1):                                             # brightness rockers in the top corners
        b.box(face(x + sx * (w / 2 + m * 0.5), v + (h + m) / 2, 0.0195), (0.010, 0.004, 0.010), M['knob'], rot=PROT, bevel=0.001)
    c = face(x, v, 0.010)
    s_ = obj(it['name'], root, tuple(c))
    sb = Builder()
    sb.quad([c - R_ * w / 2 - U_ * h / 2, c + R_ * w / 2 - U_ * h / 2, c + R_ * w / 2 + U_ * h / 2, c - R_ * w / 2 + U_ * h / 2], M[it['mat']])
    sb.write(s_)
    screens.append(s_)


def gauge_case(x, v, r):
    """A round instrument's case: a flange 6 mm proud of the face with a bezel ring, the dial 1.5 mm inside the ring."""
    c = face(x, v)
    b.cyl(c, c + N_ * 0.006, r + 0.009, M['black'], 28)
    b.torus(c + N_ * 0.0075, N_, r + 0.004, 0.0028, M['metal'], seg=28, rseg=6)
    b.box(face(x + r + 0.005, v - r - 0.005, 0.007), (0.008, 0.004, 0.008), M['black'], rot=PROT, bevel=0)   # set knob
    return c + N_ * 0.0065


def needle(name, f, r, length, width, depth):
    nd = obj('needle_' + name, root, tuple(f + N_ * depth))
    nb = Builder()
    nb.box(f + N_ * depth + U_ * r * length / 2 * 0.9, (width, 0.0015, r * length), M['needle'], rot=PROT, bevel=0)
    nb.cyl(f + N_ * (depth - 0.0008), f + N_ * (depth + 0.0012), 0.004, M['black'], 10)
    nb.write(nd)


for it in ATLAS['instruments']:
    if it['kind'] == 'screen':
        screen(it)
    elif it['kind'] == 'ann':
        c = face((it['x0'] + it['x1']) / 2, it['v'], 0.0015); hw, hh = (it['x1'] - it['x0']) / 2, it['h'] / 2
        b.quad([c - R_ * hw - U_ * hh, c + R_ * hw - U_ * hh, c + R_ * hw + U_ * hh, c - R_ * hw + U_ * hh], M['ann'])
    elif it['kind'] == 'dial':
        f = gauge_case(it['x'], it['v'], it['r'])
        b.disc(f, R_, U_, it['r'], M['dials'], uv=DIAL[it['name']])
        b.box(f + N_ * 0.001 + U_ * it['r'] * 0.3, (0.0025, 0.0015, it['r'] * 0.6), M['needle'], rot=PROT, bevel=0)
    elif it['kind'] == 'gauge':
        name, r = it['name'], it['r']
        f = gauge_case(it['x'], it['v'], r)
        if name == 'adi':
            if it['live']:
                card = obj('adi_card', root, tuple(f))
                cb = Builder(); cb.disc(f, R_, U_, r, M['adi'], uv=(0, 0.25, 1, 0.75), seg=32); cb.write(card)
            else:
                b.disc(f, R_, U_, r, M['adi'], uv=(0, 0.25, 1, 0.75), seg=32)
            b.box(f + N_ * 0.002, (r * 1.1, 0.002, 0.004), M['yellow'], rot=PROT, bevel=0)      # fixed aircraft symbol
            b.box(f + N_ * 0.002 - U_ * 0.004, (0.006, 0.002, 0.008), M['yellow'], rot=PROT, bevel=0)
            continue
        b.disc(f, R_, U_, r, M[name], seg=28)
        if it['live']:
            needle(name, f, r, 0.85, 0.0035, 0.0012)
            if name == 'alt':
                # three-pointer altimeter on the same 0-10 dial: long needle hundreds, short wide needle thousands,
                # thin pointer to the rim ten-thousands (B52Systems.Instruments turns all three)
                needle('alt1k', f, r, 0.55, 0.008, 0.0022)
                needle('alt10k', f, r, 0.95, 0.0016, 0.0030)
        else:
            b.box(f + N_ * 0.0012 + U_ * r * 0.38, (0.0035, 0.0015, r * 0.76), M['needle'], rot=PROT, bevel=0)
panelo = b.write(obj('cp_panel', root, (0, PANEL_Y, 0)))

# ------------------------------------------------------------------ side consoles and the switch panels on the walls
CT = 0.08                                    # console top
XIN = 0.86                                   # its inner edge, clear of the seat
CY0, CY1 = 23.72, 25.40
b = Builder()
for s in (-1, 1):
    ys = lin(CY0, CY1, 18)
    xo = lambda y, z: hx(y, z) - 0.045
    b.grid([[Vector((s * x, y, CT)) for x in lin(XIN, xo(y, CT), 4)] for y in ys], M['panel'], lambda c: Vector((0, 0, 1)))
    b.grid([[Vector((s * XIN, y, CT)), Vector((s * XIN, y, FLOOR))] for y in ys], M['panel'], lambda c, s=s: Vector((-s, 0, 0)))
    for yy, d in ((CY0, -1), (CY1, 1)):
        b.grid([[Vector((s * XIN, yy, z)), Vector((s * xo(yy, z), yy, z))] for z in lin(FLOOR, CT, 6)], M['panel'],
               lambda c, d=d: Vector((0, d, 0)))
    b.box((s * XIN, (CY0 + CY1) / 2, CT - 0.012), (0.022, CY1 - CY0, 0.03), M['black'], bevel=0.006)   # edge trim
    # the switch panels on its top, inboard (it narrows forward with the hull)
    wm, hm = ATLAS['regions']['console_L'][4:6]
    cy = CY1 - 0.24 - hm / 2
    cx = XIN + 0.022 + wm / 2
    b.region('console_L' if s < 0 else 'console_R', (s * cx, cy, CT), (1, 0, 0), (0, 1, 0))
    for yy in (cy - hm / 2, cy + hm / 2):
        if cx + wm / 2 > xo(yy, CT): print(f'COCKPIT warning: console panel past the wall at y {yy:.2f}')

    # Wall switch panel between the console and the window: a flat panel leaned like the wall, fitted 4.5 cm inside it.
    wm, hm = ATLAS['regions']['wall_L'][4:6]
    WY0, WY1, WZ0, WZ1 = 24.42 - wm / 2, 24.42 + wm / 2, 0.15, 0.40
    pts = {(yy, zz): Vector((s * (hx(yy, zz) - 0.06), yy, zz)) for yy in (WY0, WY1) for zz in (WZ0, WZ1)}
    c = sum(pts.values(), Vector()) / 4
    up = ((pts[(WY0, WZ1)] + pts[(WY1, WZ1)]) - (pts[(WY0, WZ0)] + pts[(WY1, WZ0)])).normalized()
    fwd = ((pts[(WY1, WZ0)] + pts[(WY1, WZ1)]) - (pts[(WY0, WZ0)] + pts[(WY0, WZ1)])).normalized()
    fwd = (fwd - up * fwd.dot(up)).normalized()
    right = fwd if s < 0 else -fwd
    n = right.cross(up).normalized()
    # inward until every corner (and the 3 cm the controls need) is inside the lining
    for _ in range(40):
        worst = min(hx(q.y, q.z) - INSET - abs(q.x) for q in (c + right * (sx_ * wm / 2) + up * (sz * hm / 2) for sx_ in (-1, 1) for sz in (-1, 1)))
        if worst >= 0.012: break
        c += n * 0.004
    b.box(c - n * 0.008, (wm + 0.02, hm + 0.02, 0.016), M['panel'], rot=frame3(right, up, n), bevel=0.004)
    b.region('wall_L' if s < 0 else 'wall_R', c, right, up)
consoles = b.write(obj('cp_consoles', root, (0, 24.5, -0.2)))

# ------------------------------------------------------------------ centre aisle stand and throttle quadrant
# Between the seats from behind the pilots to the main panel (as on the real aircraft): switch panels on its top aft,
# the throttle quadrant on it under the engine display, a panel ahead of the quadrant, and switch panels on its sides.
PX, PY0, PY1, PZ = 0.20, 24.10, 25.47, -0.13
QY0, QY1 = 24.80, 25.22                     # the quadrant's housing
qb = Builder()
qb.box((0, (PY0 + PY1) / 2, (PZ + FLOOR) / 2), (2 * PX, PY1 - PY0, PZ - FLOOR), M['panel'], bevel=0.01)
wm, hm = ATLAS['regions']['centre'][4:6]
qb.region('centre', (0, PY0 + 0.02 + hm / 2, PZ), (1, 0, 0), (0, 1, 0))
wm, hm = ATLAS['regions']['ped_front'][4:6]
qb.region('ped_front', (0, PY1 - 0.01 - hm / 2, PZ), (1, 0, 0), (0, 1, 0))
for sx in (-1, 1):
    wm, hm = ATLAS['regions']['ped_L'][4:6]
    qb.region('ped_L' if sx < 0 else 'ped_R', (sx * PX, 24.62, PZ - 0.025 - hm / 2), (0, sx, 0), (0, 0, 1))
QZ = PZ + 0.075                              # housing top
qb.box((0, (QY0 + QY1) / 2, (PZ + QZ) / 2), (0.36, QY1 - QY0, QZ - PZ), M['panel'], bevel=0.008)
qb.box((0, (QY0 + QY1) / 2, QZ + 0.002), (0.30, QY1 - QY0 - 0.04, 0.006), M['black'], bevel=0)   # top plate
LEVER_X = [-0.135 + i * (0.27 / 7) + (0.008 if i % 2 else -0.008) for i in range(8)]
QPY = (QY0 + QY1) / 2                        # the levers' pivot line, inside the housing
for x in LEVER_X:
    qb.box((x, QPY, QZ + 0.006), (0.010, QY1 - QY0 - 0.08, 0.004), M['metal'], bevel=0)    # gate slots
for sx in (-1, 1):
    qb.box((sx * 0.172, QPY, QZ + 0.03), (0.016, QY1 - QY0 - 0.02, 0.06), M['panel'], bevel=0.004)   # cheeks
# speed brake lever (left, outboard of the throttles) and flap lever (right)
for sx, knob in ((-1, 'black'), (1, 'white')):
    p0 = Vector((sx * 0.192, QPY - 0.03, QZ))
    qb.cyl(p0, p0 + Vector((0, -0.03, 0.12)), 0.006, M['metal'], 10)
    qb.box(p0 + Vector((0, -0.035, 0.135)), (0.03, 0.02, 0.03), M[knob], bevel=0.006)
qb.write(obj('cp_pedestal', root, (0, QPY, PZ)))
for i, x in enumerate(LEVER_X):
    pivot = Vector((x, QPY, QZ - 0.05))
    t = obj(f'throttle_{i + 1}', root, tuple(pivot))
    tb = Builder()
    lean = rx(18)                                                    # idle: tipped back toward the pilots
    top = pivot + lean @ Vector((0, 0, 0.21))
    tb.box((pivot + top) / 2, (0.008, 0.016, 0.21), M['metal'], rot=lean, bevel=0.002)
    tb.box(top + lean @ Vector((0, -0.012, 0.008)), (0.030, 0.034, 0.024), M['knob'], rot=lean, bevel=0.007)   # knob
    tb.write(t)

# ------------------------------------------------------------------ yokes (B-52 control wheels) and rudder pedals
# Placed for the pilot model's seated pose (measured in game, relative to the camera): hands ~0.46 m ahead and
# ~0.48 m below the eye, feet 0.70-0.92 m ahead and 1.10-1.16 m below it, 0.17 m either side of the centre line.
for side, sx in (('L', -0.55), ('R', 0.55)):
    base = Vector((sx, 25.14, FLOOR))
    hub = Vector((sx, CAM_Y + 0.46, CAM_Z - 0.48))
    col = obj(f'yokeColumn_{side}', root, tuple(base))
    cb = Builder()
    cb.box(base + Vector((0, 0.02, 0.05)), (0.17, 0.22, 0.10), M['rubber'], bevel=0.02)                # boot
    cb.cyl(base + Vector((0, 0, 0.09)), hub + Vector((0, 0.08, -0.02)), 0.032, M['black'], 16)         # column
    cb.cyl(base + Vector((0, 0, 0.09)), base + Vector((0, 0, 0.22)), 0.045, M['rubber'], 16)          # gaiter
    cb.write(col)
    y = obj(f'yoke_{side}', root, tuple(hub))
    yb = Builder()
    yb.cyl(hub + Vector((0, 0.09, 0)), hub + Vector((0, -0.03, 0)), 0.028, M['black'], 16)            # shaft
    yb.box(hub + Vector((0, -0.04, 0)), (0.12, 0.035, 0.075), M['black'], bevel=0.012)                 # hub
    yb.cyl(hub + Vector((0, -0.058, 0)), hub + Vector((0, -0.062, 0)), 0.03, M['knob'], 20)           # emblem
    yb.torus(hub + Vector((0, -0.062, 0)), (0, 1, 0), 0.03, 0.003, M['white'], seg=20, rseg=4)
    for gx in (-1, 1):                                                                                  # ram's horns
        arm = [hub + Vector((gx * 0.05, -0.045, -0.01)), hub + Vector((gx * 0.15, -0.05, -0.045)),
               hub + Vector((gx * 0.175, -0.05, 0.03))]
        yb.tube(arm, 0.014, M['black'])
        g0, g1 = hub + Vector((gx * 0.175, -0.05, 0.03)), hub + Vector((gx * 0.185, -0.05, 0.125))
        yb.cyl(g0, g1, 0.019, M['rubber'], 14)                                                          # grip
        yb.box(g1 + Vector((0, 0, 0.012)), (0.036, 0.042, 0.024), M['black'], bevel=0.008)             # grip head
        yb.cyl(g1 + Vector((0, -0.01, 0.024)), g1 + Vector((0, -0.01, 0.030)), 0.006, M['red'] if gx < 0 else M['knob'], 10)  # A/P disconnect / trim
    yb.write(y)
    pb = Builder()
    py, pz = CAM_Y + 0.90, CAM_Z - 1.12                                                    # under the balls of the feet
    for px in (-0.165, 0.165):
        pb.box((sx + px, py, pz), (0.11, 0.03, 0.20), M['metal'], rot=rx(-20))             # pedal plate
        pb.box((sx + px, py - 0.017, pz), (0.09, 0.006, 0.16), M['rubber'], rot=rx(-20), bevel=0)   # tread
        pb.box((sx + px, py + 0.06, (pz - 0.08 + FLOOR) / 2), (0.04, 0.04, pz - 0.08 - FLOOR), M['black'])   # pedal arm
    pb.write(obj(f'pedals_{side}', root, (sx, py, FLOOR)))


# ------------------------------------------------------------------ seats
RECLINE = 13
def seat(name, sx, sy, floor, fz, face_=1):
    """ACES II style ejection seat. sy, floor + fz: as built for the pilots (the cushion top at floor + fz + 0.52);
    face_ -1 for a seat facing aft. The back is reclined like the pilot model's own back."""
    sb = Builder(); f = face_
    P = lambda dx, dy, dz: Vector((sx + dx, sy + f * dy, floor + fz + dz))
    lean = rx(RECLINE * f)
    sb.box(P(0, 0.02, 0.37), (0.42, 0.46, 0.12), M['black'])                                   # pan
    sb.box(P(0, 0.04, 0.475), (0.40, 0.42, 0.09), M['seat'])                                   # cushion
    for rs in (-1, 1):
        sb.box(P(rs * 0.228, 0.0, 0.47), (0.03, 0.48, 0.26), M['black'], bevel=0.01)           # bucket sides
        h0 = P(rs * 0.25, 0.08, 0.48)                                                          # ejection handle loops
        sb.tube([h0, h0 + Vector((0, 0, 0.05)), h0 + Vector((0, f * 0.10, 0.05)), h0 + Vector((0, f * 0.10, 0))], 0.008, M['yellow'], 8)
    sb.box(P(0, 0.02, (0.31 - fz) / 2), (0.34, 0.32, max(fz + 0.31, 0.02)), M['metal'])   # riser to the rails
    back = P(0, -0.22, 0.98)
    sb.box(back, (0.44, 0.12, 0.86), M['seat'], rot=lean)                                       # back cushion
    sb.box(back + lean @ Vector((0, f * -0.05, -0.02)), (0.48, 0.06, 0.96), M['black'], rot=lean)   # back shell
    sb.box(back + lean @ Vector((0, f * -0.01, 0.56)), (0.34, 0.18, 0.24), M['black'], rot=lean)   # headbox
    sb.box(back + lean @ Vector((0, f * 0.075, 0.56)), (0.20, 0.02, 0.12), M['seat'], rot=lean)    # head pad
    for rs in (-1, 1):
        sb.box(back + lean @ Vector((rs * 0.26, f * -0.06, -0.10)), (0.035, 0.07, 1.20), M['metal'], rot=lean)   # rails
        sb.box(back + lean @ Vector((rs * 0.10, f * 0.065, 0.04)), (0.045, 0.008, 0.78), M['strap'], rot=lean, bevel=0)   # shoulder harness
        sb.box(P(rs * 0.14, 0.13, 0.523), (0.045, 0.28, 0.008), M['strap'], bevel=0)           # lap belt
    sb.box(P(0, 0.26, 0.527), (0.07, 0.05, 0.016), M['metal'], bevel=0.003)                    # harness buckle
    return sb.write(obj(name, root, (sx, sy, floor)))


for side, sx in (('L', -0.55), ('R', 0.55)):
    seat(f'seat_{side}', sx, HIP_Y - 0.07, FLOOR, (HIP_Z - 0.11) - (FLOOR + 0.52))

# ------------------------------------------------------------------ EWO (left) and gunner/instructor (right) stations
# Aft-facing seats on the upper deck behind the pilots, each with a console of displays against the aft bulkhead.
for side, sx, scr in (('L', -0.55, ('tsd', 'eng')), ('R', 0.55, ('eng', 'pfd'))):
    seat(f'seat_aft_{side}', sx, 22.25, FLOOR, 0.0, -1)
    cb = Builder()
    cy = Y0 + 0.2
    cw = min(0.95, 2 * (hx(cy, 0.8) - INSET - abs(sx)) + 0.0)                 # as wide as the narrowing wall allows
    cx = sx - math.copysign(max(0.0, 0.95 - cw) / 2, sx)
    cb.box((cx, cy, -0.1), (0.95, 0.32, 0.8), M['panel'])                       # console body
    cb.box((cx, cy + 0.2, 0.05), (0.8, 0.12, 0.03), M['panel'])                  # keyboard shelf
    cb.region('shelf', (cx, cy + 0.2, 0.0655), (-1, 0, 0), (0, -1, 0))
    back = rx(12)                                                               # screens lean back, away from the seat
    for i, (dx, m) in enumerate(zip((-0.2, 0.2), scr)):
        c = Vector((cx + dx, cy + 0.17, 0.42))
        cb.box(c, (0.26, 0.03, 0.30), M['black'], rot=back)
        n = back @ Vector((0, 1, 0))
        f = c + n * 0.017
        u = back @ Vector((0, 0, 1)); r_ = Vector((-1, 0, 0))                   # faces +Y (toward the aft-facing seat)
        cb.quad([f - r_ * 0.11 - u * 0.13, f + r_ * 0.11 - u * 0.13, f + r_ * 0.11 + u * 0.13, f - r_ * 0.11 + u * 0.13], M[m])
    cb.box((cx, cy + 0.05, 0.75), (0.80, 0.2, 0.25), M['panel'])                # upper switch panel
    cb.region('aft_L' if side == 'L' else 'aft_R', (cx, cy + 0.15, 0.75), (-1, 0, 0), (0, 0, 1))
    cb.write(obj(f'console_aft_{side}', root, (sx, cy, 0)))

# ------------------------------------------------------------------ lower deck: radar navigator (left) and navigator (right)
# Side by side under the flight deck, facing forward at their consoles, on downward ejection seats that leave through
# hatches in the floor (B52Systems.DownwardSeats). Their eyes are at (+-0.45, 23.70, -1.25) (B52Builder.Stations); the seat
# is placed from the eye as the pilots' are. The deck is a fitted floor just above the belly skin (which rises toward
# the nose, z -2.44 aft to -2.10 under the consoles); everything on it is kept clear of the curving lower walls.
LF = -2.08
LY0, LY1 = 22.60, 24.85
LCZ = FLOOR - 0.06                                                              # its ceiling: under the flight deck floor
low_w = lambda y, z: hx(y, z) - INSET
for side, sx in (('L', -0.45), ('R', 0.45)):
    eye_y, eye_z = 23.70, -1.05
    sy = eye_y + 0.18
    hip_z = eye_z - (EYE_Z - HIP_Z)
    seat(f'seat_low_{side}', sx, sy, LF, (hip_z - 0.11) - (LF + 0.52))
    s_ = 1 if sx > 0 else -1
    hb = Builder()
    # the hatch the seat leaves through: a hinged panel in the floor under the seat, outlined in yellow
    hb.box((sx, sy, LF + 0.004), (0.56, 0.56, 0.008), M['black'], bevel=0)
    for dx, dy, w, d in ((0, 0.28, 0.56, 0.03), (0, -0.28, 0.56, 0.03), (0.28, 0, 0.03, 0.56), (-0.28, 0, 0.03, 0.56)):
        hb.box((sx + dx, sy + dy, LF + 0.010), (w, d, 0.004), M['yellow'], bevel=0)
    # console ahead of the knees: a sloped desk whose outer side follows the lower wall
    cy0, cy1 = sy + 0.42, LY1 - 0.04
    xi = sx - s_ * 0.38                                                        # inner edge, by the centre aisle
    def xo(y, z): return s_ * min(abs(sx) + 0.40, low_w(y, z) - 0.02)
    zs = lin(LF, LF + 0.62, 6)
    hb.grid([[Vector((xi, cy0, z)), Vector((xo(cy0, z), cy0, z))] for z in zs], M['panel'], lambda c: Vector((0, -1, 0)))     # front face
    hb.grid([[Vector((xi, y, LF + 0.62)), Vector((xo(y, LF + 0.62), y, LF + 0.62))] for y in lin(cy0, cy1, 4)], M['panel'], lambda c: Vector((0, 0, 1)))   # desk top
    hb.grid([[Vector((xi, y, z)) for z in zs] for y in (cy0, cy1)], M['panel'], lambda c: Vector((-s_, 0, 0)))                 # aisle side
    hb.grid([[Vector((xo(y, z), y, z)) for z in zs] for y in lin(cy0, cy1, 4)], M['panel'], lambda c: Vector((-s_, 0, 0)))   # wall side
    kz = LF + 0.625
    hb.region('low_L' if side == 'L' else 'low_R', (sx, cy0 + 0.07, kz), (1, 0, 0), (0, 1, 0))
    # instrument panel above the desk, leaning back, with the radar / navigation display
    pz = LF + 0.95
    pw = min(0.72, 2 * (low_w(cy1 - 0.1, pz) - abs(sx)) - 0.04)
    c = Vector((sx, cy1 - 0.10, pz))
    hb.box(c, (pw, 0.04, 0.62), M['panel'], rot=rx(-15))
    sc = c + rx(-15) @ Vector((0, -0.021, 0.04))
    hb.box(sc, (0.36, 0.012, 0.28), M['black'], rot=rx(-15))
    f = sc + rx(-15) @ Vector((0, -0.007, 0))
    u = rx(-15) @ Vector((0, 0, 1)); r_ = Vector((1, 0, 0))                   # faces -Y (towards the seat)
    hb.quad([f - r_ * 0.16 - u * 0.12, f + r_ * 0.16 - u * 0.12, f + r_ * 0.16 + u * 0.12, f - r_ * 0.16 + u * 0.12], M['tsd' if side == 'L' else 'pfd'])
    hb.write(obj(f'console_low_{side}', root, (sx, cy0, LF)))
fb = Builder()
# the deck: fitted to the lower walls a little above it, with a kick strip along each edge
fb.grid([[Vector((x * (low_w(y, LF + 0.06) - 0.04), y, LF)) for x in lin(-1, 1, 9)] for y in lin(LY0, LY1, 12)],
        M['floor'], lambda c: Vector((0, 0, 1)))
for s_ in (-1, 1):
    fb.tube([Vector((s_ * (low_w(y, LF + 0.06) - 0.06), y, LF + 0.02)) for y in lin(LY0, LY1, 8)], 0.015, M['black'], 6)
# linings: walls following the skin from the deck up to the ceiling, the ceiling under the flight deck floor, and a
# bulkhead at each end, so from the lower deck no outside shows through the single-sided hull
for s_ in (-1, 1):
    fb.grid([[Vector((s_ * (hx(y, z) - INSET), y, z)) for z in lin(LF - 0.02, LCZ, 12)] for y in lin(LY0, LY1, 14)],
            M['wall'], lambda c, s_=s_: Vector((-s_, 0, 0)))
fb.grid([[Vector((x * (hx(y, LCZ) - INSET), y, LCZ)) for x in lin(-1, 1, 9)] for y in lin(LY0, LY1, 12)],
        M['wall'], lambda c: Vector((0, 0, -1)))
for yb, d in ((LY0, 1), (LY1, -1)):
    fb.grid([[Vector((xs * (hx(yb, z) - INSET), yb, z)) for xs in lin(-1, 1, 9)] for z in lin(LF - 0.02, LCZ, 12)],
            M['panel'], lambda c, d=d: Vector((0, d, 0)))
# the ladder up to the flight deck, under the well in its floor
fb.box((0.0, 22.95, LCZ - 0.004), (0.7, 0.6, 0.01), M['black'], bevel=0)
for lx in (-0.2, 0.2):
    fb.cyl(Vector((lx, 22.78, LF)), Vector((lx, 22.78, LCZ)), 0.018, M['metal'], 10)
for z in lin(LF + 0.28, LCZ - 0.15, 5):
    fb.cyl(Vector((-0.2, 22.78, z)), Vector((0.2, 22.78, z)), 0.012, M['metal'], 8)
fb.write(obj('cp_lowerdeck', root, (0, 23.95, LF)))

# ------------------------------------------------------------------ overhead panel
OY = 23.78
oz = roof(0, OY) - 0.10
while hx(OY, oz + 0.04) - INSET < 0.42 and oz > 0.9: oz -= 0.01
ob = Builder()
otilt = rx(-8)
ob.box((0, OY, oz + 0.03), (0.82, 0.66, 0.06), M['panel'], rot=otilt)
ob.region('overhead', Vector((0, OY, oz)) - otilt @ Vector((0, 0, 0.0)), (-1, 0, 0), otilt @ Vector((0, 1, 0)))
ob.write(obj('cp_overhead', root, (0, OY, oz)))


# ------------------------------------------------------------------ the switches, guards, knobs and fire handles
# Every control drawn in the atlas gets its real counterpart where it is drawn: a toggle lever leaning up or down, a
# guard cover over its switch, a knob with a pointer, a red T-handle. One object, so they cost one draw.
def add_controls():
    kb = Builder()
    for c in ATLAS['controls']:
        if c['region'] not in PLACED: continue
        ctr, right, up, n, wm, hm = PLACED[c['region']]
        p = ctr + right * ((c['u'] - 0.5) * wm) + up * ((0.5 - c['v']) * hm)
        rot = frame3(right, up, n)
        if c['type'] in ('toggle', 'guard'):
            kb.cyl(p, p + n * 0.004, 0.0055, M['metal'], 10)                                   # panel nut
            tip = p + n * 0.020 + up * (0.008 if c['up'] else -0.008)
            kb.cyl(p + n * 0.003, tip, 0.0022, M['metal'], 8)                                  # lever
            kb.cyl(tip, tip + (tip - p).normalized() * 0.004, 0.0032, M['metal'], 8)           # its ball end
            if c['type'] == 'guard':
                gm = M['red'] if c['colour'] == 'red' else M['yellow']
                kb.box(p + n * 0.026, (0.016, 0.026, 0.002), gm, rot=rot, bevel=0)
                for sd in (-1, 1):
                    kb.box(p + right * (sd * 0.008) + n * 0.013, (0.002, 0.026, 0.026), gm, rot=rot, bevel=0.0)
        elif c['type'] == 'knob':
            r = max(c.get('r', 0.018), 0.012)
            kb.cyl(p, p + n * 0.006, r * 1.15, M['black'], 16)                                  # skirt
            kb.cyl(p + n * 0.006, p + n * 0.016, r * 0.8, M['black'], 14)                       # grip
            kb.box(p + n * 0.0162 + up * r * 0.4, (0.002, r * 0.8, 0.001), M['white'], rot=rot, bevel=0.0)
        elif c['type'] == 'gear':
            kb.box(p + n * 0.004, (0.03, 0.09, 0.008), M['black'], rot=rot)                    # lever slot
            tip = p + n * 0.07 - up * 0.02
            kb.cyl(p + n * 0.006, tip, 0.006, M['metal'], 10)                                  # lever, down (gear down)
            kb.torus(tip, n, 0.022, 0.007, M['white'], seg=18, rseg=6)                         # the wheel-shaped knob
            kb.cyl(tip - n * 0.006, tip + n * 0.006, 0.016, M['white'], 14)
        elif c['type'] == 'fire':
            kb.cyl(p, p + n * 0.022, 0.0045, M['metal'], 8)                                     # shaft
            kb.cyl(p + n * 0.022 - right * 0.020, p + n * 0.022 + right * 0.020, 0.0075, M['red'], 12)   # T-handle
    kb.write(obj('cp_controls', root, (0, 24.5, 0)))
add_controls()


# ------------------------------------------------------------------ the things that make it a working flight deck
# A fire extinguisher and two oxygen bottles in their brackets on the aft walls, a map light by each pilot, and tubing
# and wiring looms along the upper walls: each placed against the wall where it is.
def add_details():
    db = Builder()
    for s_ in (-1, 1):
        wx = lambda y, z, off: s_ * (hx(y, z) - INSET - off)
        if s_ < 0:                                                                        # fire extinguisher
            y = 22.55; x = wx(y, 0.9, 0.07)
            db.cyl(Vector((x, y, 0.40)), Vector((x, y, 0.78)), 0.055, M['red'], 16)
            db.cyl(Vector((x, y, 0.78)), Vector((x, y, 0.84)), 0.022, M['black'], 10)
            db.box((x, y + 0.05, 0.85), (0.03, 0.10, 0.015), M['black'])
            for zz in (0.48, 0.70):
                db.box((x - s_ * 0.0, y, zz), (0.12, 0.13, 0.025), M['metal'], bevel=0.005)
        else:                                                                             # oxygen bottles
            for y in (22.35, 22.62):
                x = wx(y, 0.9, 0.08)
                db.cyl(Vector((x, y, 0.38)), Vector((x, y, 0.80)), 0.07, M['strap'], 16)
                db.cyl(Vector((x, y, 0.80)), Vector((x, y, 0.85)), 0.02, M['metal'], 10)
                for zz in (0.46, 0.72):
                    db.box((x, y, zz), (0.15, 0.15, 0.022), M['metal'], bevel=0.005)
        # map light by the pilot's shoulder, on a short arm from the wall aft of the side window
        a = Vector((wx(23.90, 0.95, 0.035), 23.90, 0.95)); m_ = Vector((s_ * 0.98, 24.0, 0.98))
        db.cyl(a, m_, 0.008, M['black'], 8)
        db.cyl(m_, m_ + Vector((-s_ * 0.05, 0.04, -0.03)), 0.024, M['black'], 12)
        # tubing and a wiring loom along the upper wall, from the aft bulkhead to the windows
        for z, off, r, m in ((0.95, 0.06, 0.012, 'metal'), (1.02, 0.07, 0.009, 'yellow'), (0.88, 0.06, 0.02, 'black')):
            pts = [Vector((wx(y, z, off), y, z)) for y in lin(Y0 + 0.08, 23.85, 6)]
            db.tube(pts, r, M[m], 8)
    db.write(obj('cp_details', root, (0, 22.8, 0.8)))
add_details()

# Pilot eye points (Unity: Pilot seats / camera)
for side, sx in (('L', -0.55), ('R', 0.55)):
    e = bpy.data.objects.new(f'eye_{side}', None); bpy.context.scene.collection.objects.link(e)
    e.matrix_world = Matrix.Translation((sx, EYE_Y, EYE_Z)); bpy.context.view_layer.update()
    mw = e.matrix_world.copy(); e.parent = root; e.matrix_world = mw

bpy.context.view_layer.update()

# Last check: keep the interior inside the hull. Each interior vertex is tested along the ray from the cabin axis
# (0, y, -0.6) through it; if it lies within 3 cm of the skin or outside it, it moves to 3 cm inside. With everything
# fitted above this should move next to nothing; the count is printed so a layout change that pokes out shows up.
MARGIN, AXIS_Z = 0.03, -0.6
moved = 0; moved_by = {}
stack = list(root.children)
while stack:
    o = stack.pop(); stack.extend(o.children)
    if o.type != 'MESH' or o.name == 'cp_skin': continue
    mw, inv = o.matrix_world, o.matrix_world.inverted()
    for v in o.data.vertices:
        p = mw @ v.co
        a = Vector((0.0, p.y, AXIS_Z))
        d = p - a
        if d.length < 0.05: continue
        hit, _, _, dist = HULL.ray_cast(a, d.normalized(), d.length + MARGIN)
        if hit is not None and dist < d.length + MARGIN:
            v.co = inv @ (a + d.normalized() * max(dist - MARGIN, 0.0)); moved += 1
            moved_by[o.name] = moved_by.get(o.name, 0) + 1
            if os.environ.get('CP_DEBUG') and moved_by[o.name] % 25 == 1: print('COCKPIT moved', o.name, tuple(round(c, 2) for c in p))
hull_bm.free()
print('COCKPIT clamp: moved', moved, 'interior vertices inside the hull', moved_by)

# Trim textures in real-world size: wall and floor faces get planar UVs from their dominant axis, in metres over the
# 0.5 m tile, so the quilting and tread keep their size on every wall instead of stretching across each box face.
TILE = 0.5
trimmed = 0
for o in [c for c in root.children_recursive if c.type == 'MESH']:
    idx = {i for i, m in enumerate(o.data.materials) if m in (M['wall'], M['floor'], M['frame'], M['panel'], M['glare'])}
    if not idx: continue
    bm = bmesh.new(); bm.from_mesh(o.data)
    uv = bm.loops.layers.uv.verify()
    mw = o.matrix_world
    for f in bm.faces:
        if f.material_index not in idx: continue
        n = (mw.to_3x3() @ f.normal); ax = max(range(3), key=lambda i: abs(n[i]))
        for l in f.loops:
            p = mw @ l.vert.co
            a, b_ = [(1, 2), (0, 2), (0, 1)][ax]
            l[uv].uv = (p[a] / TILE, p[b_] / TILE)
        trimmed += 1
    bm.to_mesh(o.data); bm.free()
print('COCKPIT trim UVs on', trimmed, 'faces')

# One material for the plain colours (planner CK3): each face in a plain-colour material points its UVs at that colour's
# cell in a small palette, so an object draws all of them in one go instead of once per colour. A second palette holds
# metalness (R) and smoothness (A) for Unity's metallic map. A face's UVs are all one point, so the palette never blurs
# between cells. Textured, tiling and glowing materials stay as they are.
FLAT = ['black', 'metal', 'knob', 'yellow', 'needle', 'white', 'gauge', 'red', 'strap', 'rubber']
CELL, COLS = 8, 4
def srgb(c): return 12.92 * c if c <= 0.0031308 else 1.055 * c ** (1 / 2.4) - 0.055
size = CELL * COLS
col_px = [0.0] * (size * size * 4); ms_px = [0.0] * (size * size * 4)
for i, key in enumerate(FLAT):
    p_ = M[key].node_tree.nodes['Principled BSDF']
    rgb = [srgb(c) for c in p_.inputs['Base Color'].default_value[:3]]
    metal, smooth = p_.inputs['Metallic'].default_value, 1 - p_.inputs['Roughness'].default_value
    cx, cy = i % COLS, i // COLS
    for y in range(cy * CELL, cy * CELL + CELL):
        for x in range(cx * CELL, cx * CELL + CELL):
            k = (y * size + x) * 4
            col_px[k:k + 4] = rgb + [1.0]; ms_px[k:k + 4] = [metal, 0.0, 0.0, smooth]
for name, px, data in (('cp_palette.png', col_px, False), ('cp_palette_ms.png', ms_px, True)):
    im = bpy.data.images.new(name, size, size, alpha=True, is_data=data)
    im.pixels = px; im.filepath_raw = os.path.join(TEX, name); im.file_format = 'PNG'; im.save()
    im.use_fake_user = True          # kept in the .blend, so blender_export.py copies it to Unity (the _ms one has no user)
M['flat'] = mat('CP_Flat', (1, 1, 1), 0.0, 0.6, 'cp_palette.png')
next(n for n in M['flat'].node_tree.nodes if n.type == 'TEX_IMAGE').interpolation = 'Closest'
cell_uv = {M[k]: ((i % COLS + 0.5) / COLS, (i // COLS + 0.5) / COLS) for i, k in enumerate(FLAT)}
merged = 0; slots_before = slots_after = 0
for o in [c for c in root.children_recursive if c.type == 'MESH']:
    mats = list(o.data.materials)
    if not any(m in cell_uv for m in mats): continue
    bm = bmesh.new(); bm.from_mesh(o.data); uv = bm.loops.layers.uv.verify()
    slots_before += len({f.material_index for f in bm.faces})
    if M['flat'] not in mats: mats.append(M['flat'])
    flat_i = mats.index(M['flat'])
    for f in bm.faces:
        m = mats[f.material_index]
        if m in cell_uv:
            for l in f.loops: l[uv].uv = cell_uv[m]
            f.material_index = flat_i; merged += 1
    keep = sorted({f.material_index for f in bm.faces})           # drop the slots nothing uses any more
    remap = {old: new for new, old in enumerate(keep)}
    for f in bm.faces: f.material_index = remap[f.material_index]
    slots_after += len(keep)
    o.data.materials.clear()                                     # slots first: clearing them moves the faces' indices
    for i in keep: o.data.materials.append(mats[i])
    bm.to_mesh(o.data); bm.free()
print(f'COCKPIT plain colours on one material: {merged} faces; material slots on those objects {slots_before} -> {slots_after}')

bpy.ops.wm.save_as_mainfile(filepath=bpy.data.filepath)
print('COCKPIT OK', len([o for o in bpy.data.objects if o.parent == root]), 'objects under cockpit_int')
