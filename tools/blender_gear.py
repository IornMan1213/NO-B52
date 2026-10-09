"""
B-52H landing gear, modelled to replace the placeholder cylinders that blender_prep.py makes.
Run after blender_prep.py, before blender_cockpit.py:
  blender -b blender/out/B52_parts.blend --python tools/blender_gear.py

Real gear (B-52G/H): four two-wheel main trucks in a quadricycle layout under the fuselage (56x16 tyres,
D 1.42 m, track 2.51 m, wheelbase 15.3 m) and one tip-protection outrigger wheel near each wingtip (D 0.81 m).
Each main truck is a single oleo strut on a trunnion, a two-wheel axle and a torque-link scissor. To retract, each
truck swivels ~90 deg on its strut and folds flat into a well in the lower fuselage, port trucks forward and
starboard trucks aft (so the two wells of a pair sit at different stations and each can use the full width).
The animation is set up in Unity (B52Builder.BuildGear); this script only makes the meshes.

Keeps the object names and pivots the Unity builder relies on:
  gear_<k>            sprung strut (origin = trunnion pivot), parent = its fuselage / wingtip part
  gear_unsprung_<k>   axle, piston and yoke (origin = axle centre); moves with the suspension and steering
  wheel_<k>1 / 2      main tyres + rims (origin = wheel centre, spin about local X); outriggers: wheel_O<s>
  tlinkU_<k> / tlinkL_<k>  torque links (upper on the strut, lower on the piston), wired as IK joints in Unity
Blender frame: +X right, +Y forward, +Z up (Unity: x, z, y).
"""
import bpy, bmesh, math
from mathutils import Vector, Matrix

MAIN = ('FL', 'FR', 'RL', 'RR')
OUT = ('OL', 'OR')


def mat(name, rgb, metal, rough):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.use_nodes = True
    p = m.node_tree.nodes['Principled BSDF']
    p.inputs['Base Color'].default_value = (*rgb, 1); p.inputs['Metallic'].default_value = metal
    p.inputs['Roughness'].default_value = rough
    return m


M_STRUT = mat('B52_GearMetal', (0.80, 0.81, 0.82), 0.15, 0.5)       # white-painted legs
M_CHROME = mat('B52_GearChrome', (0.85, 0.86, 0.88), 1.0, 0.12)
M_RUBBER = mat('B52_Tyre', (0.03, 0.03, 0.03), 0.0, 0.9)
M_RIM = mat('B52_GearRim', (0.88, 0.88, 0.87), 0.1, 0.45)          # white wheels and hub caps
M_DARK = mat('B52_GearDark', (0.12, 0.12, 0.13), 0.5, 0.6)


class Geo:
    """World-space geometry builder; write() puts it into an object whose origin is at `origin`."""

    def __init__(self):
        self.bm = bmesh.new(); self.mats = []

    def _mi(self, m):
        if m not in self.mats: self.mats.append(m)
        return self.mats.index(m)

    def _paint(self, verts, m):
        i = self._mi(m)
        for v in verts:
            for f in v.link_faces: f.material_index = i

    def cyl(self, p0, p1, r, m, seg=16, r1=None):
        p0, p1 = Vector(p0), Vector(p1)
        d = p1 - p0
        mtx = Matrix.Translation((p0 + p1) / 2) @ d.to_track_quat('Z', 'Y').to_matrix().to_4x4()
        res = bmesh.ops.create_cone(self.bm, cap_ends=True, segments=seg, radius1=r, radius2=r if r1 is None else r1,
                                    depth=d.length, matrix=mtx)
        self._paint(res['verts'], m)

    def box(self, c, size, m, rot=None):
        mtx = Matrix.Translation(Vector(c)) @ (rot.to_4x4() if rot else Matrix.Identity(4)) @ Matrix.Diagonal((*size, 1))
        res = bmesh.ops.create_cube(self.bm, size=1.0, matrix=mtx)
        self._paint(res['verts'], m)

    def bar(self, p0, p1, w, h, m):
        """Rectangular link from p0 to p1 (width w across X, depth h)."""
        p0, p1 = Vector(p0), Vector(p1)
        d = p1 - p0
        rot = d.to_track_quat('Z', 'X').to_matrix()
        self.box((p0 + p1) / 2, (w, h, d.length), m, rot)

    def lathe(self, centre, axis, profile, m, seg=32, caps=True):
        """Surface of revolution around `axis` through `centre`; profile = [(offset along axis, radius), ...]."""
        c, a = Vector(centre), Vector(axis).normalized()
        u = a.orthogonal().normalized(); w = a.cross(u)
        rings = []
        for t, r in profile:
            ring = []
            for j in range(seg):
                b = 2 * math.pi * j / seg
                ring.append(self.bm.verts.new(c + a * t + (u * math.cos(b) + w * math.sin(b)) * r))
            rings.append(ring)
        i = self._mi(m)
        for k in range(len(rings) - 1):
            for j in range(seg):
                jj = (j + 1) % seg
                f = self.bm.faces.new((rings[k][j], rings[k][jj], rings[k + 1][jj], rings[k + 1][j])); f.material_index = i
        for ring, flip in ((rings[0], True), (rings[-1], False)):            # caps
            if caps and profile[0 if flip else -1][1] > 1e-4:
                f = self.bm.faces.new(list(reversed(ring)) if flip else ring); f.material_index = i

    def write(self, name, origin, parent, keep_world=True):
        old = bpy.data.objects.get(name)
        children = list(old.children) if old else []
        if old:
            me_old = old.data; bpy.data.objects.remove(old); bpy.data.meshes.remove(me_old)
        bmesh.ops.recalc_face_normals(self.bm, faces=self.bm.faces)
        self.bm.transform(Matrix.Translation(-Vector(origin)))
        me = bpy.data.meshes.new(name); self.bm.to_mesh(me); self.bm.free()
        for m in self.mats: me.materials.append(m)
        o = bpy.data.objects.new(name, me); bpy.context.scene.collection.objects.link(o)
        o.matrix_world = Matrix.Translation(Vector(origin)); bpy.context.view_layer.update()
        if parent:
            mw = o.matrix_world.copy(); o.parent = parent; o.matrix_world = mw
        for ch in children:
            mw = ch.matrix_world.copy(); ch.parent = o; ch.matrix_world = mw
        bpy.context.view_layer.update()
        return o


def tyre(g, centre, r, w, rim_r, cap=True, grooves=4):
    """Tyre with rounded shoulders and a ribbed tread, a dished wheel recessed into it, a bolt ring and a domed hub
    cap; axle along X. Real 56x16 main tyres: rim about half the tyre diameter, tread with straight ribs."""
    h = w / 2
    sh = min(0.09, r * 0.15)                                    # shoulder radius
    prof = [(-h * 0.92, rim_r), (-h, rim_r + 0.03), (-h, r - sh)]
    for i in range(1, 5):                                       # shoulder arcs
        a = math.pi / 2 * i / 5
        prof.append((-h + sh - sh * math.cos(a), r - sh + sh * math.sin(a)))
    x0, x1 = -h + sh, h - sh
    prof.append((x0, r))
    for k in range(1, grooves + 1):                             # grooves between the ribs
        gx = x0 + (x1 - x0) * k / (grooves + 1)
        prof += [(gx - 0.011, r), (gx - 0.007, r - 0.014), (gx + 0.007, r - 0.014), (gx + 0.011, r)]
    prof.append((x1, r))
    for i in range(1, 5):
        a = math.pi / 2 * i / 5
        prof.append((h - sh + sh * math.sin(a), r - sh * (1 - math.cos(a))))
    prof += [(h, r - sh), (h, rim_r + 0.03), (h * 0.92, rim_r)]
    g.lathe(centre, (1, 0, 0), prof, M_RUBBER, seg=40, caps=False)     # open at the bead: the wheel fills it
    # wheel: flat outer disc with a raised flange, dished centre
    g.lathe(centre, (1, 0, 0), [(-h * 0.90, 0.0), (-h * 0.90, rim_r * 0.30), (-h * 0.80, rim_r * 0.55),
                                (-h * 0.88, rim_r * 0.92), (-h * 0.95, rim_r), (h * 0.95, rim_r), (h * 0.88, rim_r * 0.92),
                                (h * 0.80, rim_r * 0.55), (h * 0.90, rim_r * 0.30), (h * 0.90, 0.0)], M_RIM, seg=32)
    for s_ in (-1, 1):
        if cap:
            g.lathe(Vector(centre) + Vector((s_ * h * 0.90, 0, 0)), (s_, 0, 0),
                    [(0, 0.15), (0.03, 0.145), (0.065, 0.115), (0.085, 0.06), (0.09, 0.0)], M_RIM, seg=24)   # hub cap
        n = 20 if cap else 10
        for k in range(n):                                      # bolt ring
            b = 2 * math.pi * k / n
            p = Vector(centre) + Vector((s_ * h * 0.905, math.cos(b) * rim_r * 0.72, math.sin(b) * rim_r * 0.72))
            g.cyl(p, p + Vector((s_ * 0.012, 0, 0)), 0.016, M_DARK, seg=6)


def endpoints(name):
    o = bpy.data.objects[name]
    vs = [o.matrix_world @ v.co for v in o.data.vertices]
    return o, Vector([sum(v[i] for v in vs) / len(vs) for i in range(3)]), max(v.z for v in vs), min(v.z for v in vs)


report = []
# ---------------------------------------------------------------- main trucks
for k in MAIN:
    strut, c, ztop, _ = endpoints(f'gear_{k}')
    parent = strut.parent
    hub = bpy.data.objects[f'gear_unsprung_{k}'].matrix_world.translation.copy()
    pivot = Vector((c.x, c.y, ztop))
    sgn = 1 if k[1] == 'R' else -1
    R, W, OFF, RIM = 0.71, 0.41, 0.24, 0.40

    # sprung (as on the B-52H, front view): a bulbous upper housing on the trunnion, a fat oleo body, a dark gland
    # ring, and the twin steering-actuator cylinders across the front of the strut base; torque links at the back.
    g = Geo()
    g.cyl(pivot + Vector((-0.38, 0, 0)), pivot + Vector((0.38, 0, 0)), 0.11, M_STRUT)          # trunnion
    for s in (-1, 1):
        g.cyl(pivot + Vector((s * 0.38, 0, 0)), pivot + Vector((s * 0.46, 0, 0)), 0.14, M_DARK)  # trunnion bearings
    cyl_bot = hub.z + 0.40
    top_h = pivot.z - 0.32
    g.cyl(Vector((pivot.x, pivot.y, pivot.z + 0.10)), Vector((pivot.x, pivot.y, top_h)), 0.21, M_STRUT, seg=24)   # upper housing
    g.cyl(Vector((pivot.x, pivot.y, pivot.z + 0.10)), Vector((pivot.x, pivot.y, pivot.z + 0.22)), 0.21, M_STRUT, seg=24, r1=0.12)
    g.cyl(Vector((pivot.x, pivot.y, top_h + 0.02)), Vector((pivot.x, pivot.y, cyl_bot)), 0.165, M_STRUT, seg=24)  # oleo body
    g.cyl(Vector((pivot.x, pivot.y, top_h + 0.03)), Vector((pivot.x, pivot.y, top_h - 0.03)), 0.215, M_DARK, seg=24)
    g.cyl(Vector((pivot.x, pivot.y, cyl_bot + 0.05)), Vector((pivot.x, pivot.y, cyl_bot - 0.02)), 0.18, M_DARK, seg=24)
    for s in (-1, 1):                                                                            # steering actuators
        c0 = Vector((pivot.x + s * 0.125, pivot.y + 0.02, cyl_bot + 0.13))
        g.cyl(c0, c0 + Vector((0, 0.30, 0)), 0.105, M_STRUT, seg=20)
        g.cyl(c0 + Vector((0, 0.30, 0)), c0 + Vector((0, 0.32, 0)), 0.085, M_DARK, seg=20)
    g.box((pivot.x, pivot.y - 0.18, cyl_bot + 0.04), (0.10, 0.09, 0.08), M_STRUT)               # torque-link lug
    g.bar(Vector((pivot.x, pivot.y, pivot.z - 0.18)), Vector((pivot.x - sgn * 0.55, pivot.y - 0.35, pivot.z + 0.10)),
          0.07, 0.07, M_STRUT)                                                                    # side-brace stub
    so = g.write(f'gear_{k}', pivot, parent)

    # unsprung: chrome piston, axle housing with its jacking pad, axle, brake housings
    g = Geo()
    g.cyl(Vector((hub.x, hub.y, hub.z + 0.20)), Vector((hub.x, hub.y, cyl_bot + 0.30)), 0.13, M_CHROME, seg=24)
    g.box(hub + Vector((0, 0, 0.12)), (0.42, 0.34, 0.20), M_STRUT)                              # axle housing
    g.box(hub + Vector((0, 0, -0.13)), (0.20, 0.20, 0.07), M_DARK)                               # jacking pad
    g.box(hub + Vector((0, -0.18, 0.07)), (0.10, 0.09, 0.07), M_STRUT)                           # lower link lug
    g.cyl(hub + Vector((-0.48, 0, 0)), hub + Vector((0.48, 0, 0)), 0.09, M_STRUT)                # axle
    for s in (-1, 1):                                                                            # brake housings
        g.cyl(hub + Vector((s * (OFF - W * 0.3), 0, 0)), hub + Vector((s * (OFF + W * 0.05), 0, 0)), 0.28, M_DARK, seg=24)
    uo = g.write(f'gear_unsprung_{k}', hub, so)

    for i, dx in enumerate((-OFF, OFF)):
        g = Geo(); centre = hub + Vector((dx, 0, 0))
        tyre(g, centre, R, W, RIM)
        g.write(f'wheel_{k}{i + 1}', centre, uo)

    # torque links (scissor) behind the strut: upper on the oleo body, lower on the axle housing
    top = Vector((pivot.x, pivot.y - 0.18, cyl_bot + 0.04))
    bot = hub + Vector((0, -0.18, 0.07))
    elbow = (top + bot) / 2 + Vector((0, -0.22, 0))
    g = Geo(); g.bar(top, elbow, 0.07, 0.05, M_STRUT); g.cyl(elbow + Vector((-0.05, 0, 0)), elbow + Vector((0.05, 0, 0)), 0.035, M_DARK, seg=8)
    g.write(f'tlinkU_{k}', top, so)
    g = Geo(); g.bar(bot, elbow, 0.07, 0.05, M_STRUT)
    g.write(f'tlinkL_{k}', bot, uo)
    e = bpy.data.objects.get(f'tlinkE_{k}') or bpy.data.objects.new(f'tlinkE_{k}', None)
    if not e.users_collection: bpy.context.scene.collection.objects.link(e)
    e.matrix_world = Matrix.Translation(elbow); bpy.context.view_layer.update()
    mw = e.matrix_world.copy(); e.parent = so; e.matrix_world = mw
    report.append(f'{k}: pivot {tuple(round(x, 2) for x in pivot)} hub {tuple(round(x, 2) for x in hub)} stroke-visible {cyl_bot - hub.z:.2f}')

# ---------------------------------------------------------------- outriggers
for k in OUT:
    strut, c, ztop, _ = endpoints(f'gear_{k}')
    parent = strut.parent
    hub = bpy.data.objects[f'gear_unsprung_{k}'].matrix_world.translation.copy()
    # trunnion in the wing's mid-plane (lower skin 0.72, upper 1.18 at the tip) so the folded leg lies inside it
    pivot = Vector((c.x, c.y, ztop + 0.30))
    R, W, RIM = 0.405, 0.20, 0.22                              # 32x8.8 tyre
    cyl_bot = hub.z + R + 0.55

    # Real tip gear (reference photo: Commons "B-52B wingtip fuel tank and landing gear"): a straight oleo leg, then an
    # arm angling forward from its foot to a stub axle, the single wheel on the outboard side of the arm (slightly ahead
    # of the leg). It folds inboard along the swept span about an axis 35 deg off fore-aft (FOLD_AXIS, as the Unity
    # builder's gear mount) while the wheel swivels 35 deg on the leg, so it stows flat in the 0.31 m deep outer wing:
    # everything is kept thin in the axle direction, which ends up vertical.
    side = 1 if c.x > 0 else -1
    wheel_c = hub + Vector((side * 0.13, 0.15, 0))             # ahead of and outboard of the leg axis
    fold_axis = Vector((-math.sin(math.radians(35)), -side * math.cos(math.radians(35)), 0))
    g = Geo()
    g.cyl(pivot - fold_axis * 0.08, pivot + fold_axis * 0.08, 0.05, M_STRUT)                    # trunnion on the fold axis
    g.cyl(pivot, Vector((pivot.x, pivot.y, cyl_bot)), 0.07, M_STRUT, seg=16, r1=0.092)           # oleo cylinder, slim at the top
    g.cyl(Vector((pivot.x, pivot.y, cyl_bot + 0.05)), Vector((pivot.x, pivot.y, cyl_bot - 0.02)), 0.096, M_DARK, seg=16)
    so = g.write(f'gear_{k}', pivot, parent)

    g = Geo()
    knee = Vector((hub.x, hub.y, hub.z + 0.42))
    g.cyl(knee + Vector((0, 0, 0.02)), Vector((hub.x, hub.y, cyl_bot + 0.45)), 0.075, M_CHROME)   # piston
    g.box(knee, (0.10, 0.18, 0.14), M_STRUT)                                                      # knee casting
    arm_end = wheel_c + Vector((-side * 0.10, 0, 0))
    g.bar(knee, arm_end, 0.06, 0.10, M_STRUT)                                                     # wheel arm
    g.cyl(arm_end, wheel_c + Vector((side * 0.04, 0, 0)), 0.045, M_STRUT)                         # stub axle
    g.box(knee + Vector((0, 0.10, 0.10)), (0.04, 0.04, 0.22), M_STRUT)                            # torque link
    uo = g.write(f'gear_unsprung_{k}', hub, so)
    g = Geo(); tyre(g, wheel_c, R, W, RIM, cap=False)
    g.write(f'wheel_{k}', wheel_c, uo)
    report.append(f'{k}: pivot {tuple(round(x, 2) for x in pivot)} hub {tuple(round(x, 2) for x in hub)}')

bpy.ops.wm.save_mainfile()
print('GEAR', *report, sep='\n  ')
