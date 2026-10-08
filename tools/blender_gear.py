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


M_STRUT = mat('B52_GearMetal', (0.55, 0.56, 0.58), 0.8, 0.35)
M_CHROME = mat('B52_GearChrome', (0.85, 0.86, 0.88), 1.0, 0.12)
M_RUBBER = mat('B52_Tyre', (0.03, 0.03, 0.03), 0.0, 0.9)
M_RIM = mat('B52_GearRim', (0.70, 0.71, 0.72), 0.7, 0.3)
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

    def lathe(self, centre, axis, profile, m, seg=32):
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
            if profile[0 if flip else -1][1] > 1e-4:
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


def tyre(g, centre, r, w, rim_r, cap=True):
    """Tyre with rounded shoulders, a rim recessed into it and a hub cap, axle along X."""
    h = w / 2
    sh = min(0.09, r * 0.15)                                    # shoulder radius
    prof = [(-h * 0.92, rim_r), (-h, rim_r + 0.03), (-h, r - sh)]
    for i in range(1, 5):                                       # shoulder arcs
        a = math.pi / 2 * i / 5
        prof.append((-h + sh - sh * math.cos(a), r - sh + sh * math.sin(a)))
    prof += [(-h + sh, r), (h - sh, r)]
    for i in range(1, 5):
        a = math.pi / 2 * i / 5
        prof.append((h - sh + sh * math.sin(a), r - sh * (1 - math.cos(a))))
    prof += [(h, r - sh), (h, rim_r + 0.03), (h * 0.92, rim_r)]
    g.lathe(centre, (1, 0, 0), prof, M_RUBBER)
    # rim: dished wheel inside the tyre bead
    g.lathe(centre, (1, 0, 0), [(-h * 0.92, 0.0), (-h * 0.92, rim_r * 0.35), (-h * 0.75, rim_r * 0.55),
                                (-h * 0.85, rim_r), (h * 0.85, rim_r), (h * 0.75, rim_r * 0.55),
                                (h * 0.92, rim_r * 0.35), (h * 0.92, 0.0)], M_RIM, seg=24)
    if cap:
        for s in (-1, 1):
            g.lathe(Vector(centre) + Vector((s * h * 0.92, 0, 0)), (s, 0, 0),
                    [(0, 0.12), (0.035, 0.10), (0.05, 0.0)], M_RIM, seg=16)
            for k in range(8):                                  # wheel bolts
                b = 2 * math.pi * k / 8
                p = Vector(centre) + Vector((s * h * 0.93, math.cos(b) * 0.17, math.sin(b) * 0.17))
                g.cyl(p, p + Vector((s * 0.02, 0, 0)), 0.018, M_DARK, seg=6)


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
    R, W, OFF, RIM = 0.71, 0.41, 0.24, 0.43

    # sprung: trunnion, oleo cylinder, gland nut, upper torque link lug, side brace stub
    g = Geo()
    g.cyl(pivot + Vector((-0.38, 0, 0)), pivot + Vector((0.38, 0, 0)), 0.11, M_STRUT)          # trunnion
    for s in (-1, 1):
        g.cyl(pivot + Vector((s * 0.38, 0, 0)), pivot + Vector((s * 0.46, 0, 0)), 0.14, M_DARK)  # trunnion bearings
    cyl_bot = hub.z + 0.47
    g.cyl(pivot + Vector((0, 0, 0.05)), Vector((pivot.x, pivot.y, cyl_bot)), 0.155, M_STRUT, seg=20)
    g.cyl(Vector((pivot.x, pivot.y, cyl_bot + 0.06)), Vector((pivot.x, pivot.y, cyl_bot - 0.02)), 0.175, M_DARK, seg=20)
    g.box((pivot.x, pivot.y + 0.17, cyl_bot + 0.04), (0.10, 0.09, 0.08), M_STRUT)               # torque-link lug
    g.bar(Vector((pivot.x, pivot.y, pivot.z - 0.18)), Vector((pivot.x - sgn * 0.55, pivot.y - 0.35, pivot.z + 0.10)),
          0.07, 0.07, M_STRUT)                                                                    # side-brace stub
    so = g.write(f'gear_{k}', pivot, parent)

    # unsprung: chrome piston, yoke, axle beam, brake housings
    g = Geo()
    g.cyl(Vector((hub.x, hub.y, hub.z + 0.14)), Vector((hub.x, hub.y, cyl_bot + 0.30)), 0.115, M_CHROME, seg=20)
    g.box(hub + Vector((0, 0, 0.10)), (0.30, 0.26, 0.16), M_STRUT)                              # yoke
    g.box(hub + Vector((0, 0.16, 0.07)), (0.10, 0.09, 0.07), M_STRUT)                            # lower link lug
    g.cyl(hub + Vector((-0.48, 0, 0)), hub + Vector((0.48, 0, 0)), 0.085, M_STRUT)               # axle
    for s in (-1, 1):                                                                            # brake housings
        g.cyl(hub + Vector((s * (OFF - W * 0.3), 0, 0)), hub + Vector((s * (OFF + W * 0.05), 0, 0)), 0.30, M_DARK, seg=20)
    uo = g.write(f'gear_unsprung_{k}', hub, so)

    for i, dx in enumerate((-OFF, OFF)):
        g = Geo(); centre = hub + Vector((dx, 0, 0))
        tyre(g, centre, R, W, RIM)
        g.write(f'wheel_{k}{i + 1}', centre, uo)

    # torque links (scissor): upper on the strut, lower on the piston, meeting at an elbow ahead of the strut
    top = Vector((pivot.x, pivot.y + 0.17, cyl_bot + 0.04))
    bot = hub + Vector((0, 0.16, 0.07))
    elbow = (top + bot) / 2 + Vector((0, 0.24, 0))
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
    R, W, RIM = 0.405, 0.25, 0.22
    cyl_bot = hub.z + R + 0.55

    g = Geo()
    g.cyl(pivot + Vector((0, -0.25, 0)), pivot + Vector((0, 0.25, 0)), 0.09, M_STRUT)             # fore-aft trunnion
    g.cyl(pivot, Vector((pivot.x, pivot.y, cyl_bot)), 0.10, M_STRUT, seg=16)
    g.cyl(Vector((pivot.x, pivot.y, cyl_bot + 0.05)), Vector((pivot.x, pivot.y, cyl_bot - 0.02)), 0.12, M_DARK, seg=16)
    so = g.write(f'gear_{k}', pivot, parent)

    g = Geo()
    g.cyl(Vector((hub.x, hub.y, hub.z + R + 0.08)), Vector((hub.x, hub.y, cyl_bot + 0.45)), 0.075, M_CHROME)
    g.box(Vector((hub.x, hub.y, hub.z + R + 0.06)), (0.42, 0.16, 0.08), M_STRUT)                 # fork crown
    for s in (-1, 1):                                                                             # fork legs
        g.bar(Vector((hub.x + s * 0.18, hub.y, hub.z + R + 0.06)), Vector((hub.x + s * 0.18, hub.y, hub.z)), 0.05, 0.12, M_STRUT)
    g.cyl(hub + Vector((-0.21, 0, 0)), hub + Vector((0.21, 0, 0)), 0.04, M_STRUT)
    uo = g.write(f'gear_unsprung_{k}', hub, so)
    g = Geo(); tyre(g, hub, R, W, RIM, cap=False)
    g.write(f'wheel_{k}', hub, uo)
    report.append(f'{k}: pivot {tuple(round(x, 2) for x in pivot)} hub {tuple(round(x, 2) for x in hub)}')

bpy.ops.wm.save_mainfile()
print('GEAR', *report, sep='\n  ')
