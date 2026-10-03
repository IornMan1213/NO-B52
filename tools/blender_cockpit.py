"""
Scratch-built B-52J upper flight deck, added under the 'cockpit' part of B52_parts.blend.

Run:  blender -b blender/out/B52_parts.blend --python tools/blender_cockpit.py -- <tex_dir>

Layout (Blender frame, metres, +Y forward):
  windshield lower edge z 0.38-0.42 at y 25.6-26.1; side window sill z 0.46; roof z 1.31 at y 24.5
  floor z -0.50; pilot x -0.55, copilot x +0.55; eye point y 24.55, z 0.78
Animated objects, wired up in Unity:
  yoke_L / yoke_R              Cockpit.joysticks  (pitch about local X, roll about local Z)
  throttle_1 .. throttle_8     Cockpit.throttles  (rotation about local X)
  mfd_C_screen                 Cockpit.tacScreenRender (live tactical map)
  eye_L / eye_R                Pilot seat camera positions
"""
import bpy, bmesh, sys, os, math
from mathutils import Vector, Matrix

ARGS = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
TEX = ARGS[0] if ARGS else r'C:\Users\jayea\Documents\GitHub\NO-B52\blender\tex'

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
    'panel': mat('CP_PanelGrey', (0.20, 0.21, 0.22), 0.1, 0.7),
    'panelTex': mat('CP_Switches', (1, 1, 1), 0.1, 0.6, 'panel_a.png'),
    'panelTex2': mat('CP_Switches2', (1, 1, 1), 0.1, 0.6, 'panel_b.png'),
    'black': mat('CP_Black', (0.02, 0.02, 0.02), 0.0, 0.5),
    'glare': mat('CP_Glareshield', (0.03, 0.03, 0.035), 0.0, 0.95),
    'wall': mat('CP_Wall', (0.33, 0.36, 0.33), 0.0, 0.85),
    'floor': mat('CP_Floor', (0.10, 0.10, 0.10), 0.2, 0.9),
    'metal': mat('CP_Metal', (0.6, 0.6, 0.62), 0.9, 0.3),
    'seat': mat('CP_Seat', (1, 1, 1), 0.0, 0.9, 'seat.png'),
    'yellow': mat('CP_EjectHandle', (0.9, 0.75, 0.05), 0.0, 0.5),
    'pfd': mat('CP_MFD_PFD', (1, 1, 1), 0.0, 0.2, 'mfd_pfd.png', 1.5),
    'eng': mat('CP_MFD_ENG', (1, 1, 1), 0.0, 0.2, 'mfd_engines.png', 1.5),
    'tsd': mat('CP_MFD_TSD', (1, 1, 1), 0.0, 0.2, 'mfd_tsd.png', 1.5),
    'gauge': mat('CP_GaugeFace', (0.02, 0.02, 0.02), 0.0, 0.3),
    'white': mat('CP_White', (0.9, 0.9, 0.9), 0.0, 0.5),
}


# ------------------------------------------------------------------ primitives
def obj(name, parent, origin=(0, 0, 0)):
    me = bpy.data.meshes.new(name)
    o = bpy.data.objects.new(name, me); bpy.context.scene.collection.objects.link(o)
    o.matrix_world = Matrix.Translation(origin)
    bpy.context.view_layer.update()
    mw = o.matrix_world.copy(); o.parent = parent; o.matrix_world = mw
    bpy.context.view_layer.update()
    return o


class Builder:
    """Accumulates geometry in world coordinates, then writes it into an object relative to its origin."""

    def __init__(self):
        self.bm = bmesh.new(); self.mats = []

    def mi(self, m):
        if m not in self.mats: self.mats.append(m)
        return self.mats.index(m)

    def box(self, c, size, m, rot=None, uv_scale=1.0):
        mtx = Matrix.Translation(c) @ (rot.to_4x4() if rot else Matrix.Identity(4)) @ Matrix.Diagonal((*size, 1))
        r = bmesh.ops.create_cube(self.bm, size=1.0, matrix=mtx, calc_uvs=True)
        idx = self.mi(m)
        for v in r['verts']:
            for f in v.link_faces: f.material_index = idx
        return r

    def quad(self, pts, m, uv=((0, 0), (1, 0), (1, 1), (0, 1))):
        vs = [self.bm.verts.new(p) for p in pts]
        f = self.bm.faces.new(vs); f.material_index = self.mi(m)
        lay = self.bm.loops.layers.uv.verify()
        for l, t in zip(f.loops, uv): l[lay].uv = t
        return f

    def cyl(self, p0, p1, r, m, seg=16):
        d = Vector(p1) - Vector(p0)
        rot = d.to_track_quat('Z', 'Y').to_matrix().to_4x4()
        mtx = Matrix.Translation((Vector(p0) + Vector(p1)) / 2) @ rot
        res = bmesh.ops.create_cone(self.bm, cap_ends=True, segments=seg, radius1=r, radius2=r, depth=d.length, matrix=mtx)
        idx = self.mi(m)
        for v in res['verts']:
            for f in v.link_faces: f.material_index = idx

    def torus(self, centre, normal, R, r, m, arc=(0, 360), seg=28, rseg=8):
        n = Vector(normal).normalized()
        u = n.orthogonal().normalized(); w = n.cross(u)
        rings = []
        a0, a1 = map(math.radians, arc)
        steps = seg
        for i in range(steps + 1):
            a = a0 + (a1 - a0) * i / steps
            cdir = u * math.cos(a) + w * math.sin(a)
            ring = []
            for j in range(rseg):
                b = 2 * math.pi * j / rseg
                p = Vector(centre) + cdir * (R + r * math.cos(b)) + n * r * math.sin(b)
                ring.append(self.bm.verts.new(p))
            rings.append(ring)
        idx = self.mi(m)
        for i in range(steps):
            for j in range(rseg):
                k = (j + 1) % rseg
                f = self.bm.faces.new((rings[i][j], rings[i][k], rings[i + 1][k], rings[i + 1][j])); f.material_index = idx

    def write(self, o):
        self.bm.transform(o.matrix_world.inverted())
        self.bm.to_mesh(o.data); self.bm.free()
        for m in self.mats: o.data.materials.append(m)
        return o


def rx(deg):
    return Matrix.Rotation(math.radians(deg), 3, 'X')


# ------------------------------------------------------------------ shell (floor, walls, ceiling, bulkhead)
Y0, Y1 = 23.15, 25.75          # aft bulkhead .. panel base
FLOOR, SILL, ROOF = -0.50, 0.44, 1.24
b = Builder()
b.box((0, (Y0 + Y1) / 2, FLOOR - 0.03), (2.7, Y1 - Y0, 0.06), M['floor'])
for s in (-1, 1):
    # lower side wall up to the window sill, and console cheek
    b.box((s * 1.36, (Y0 + Y1) / 2, (FLOOR + SILL) / 2), (0.04, Y1 - Y0, SILL - FLOOR), M['wall'])
    # upper wall strip aft of the side windows
    b.box((s * 1.20, (Y0 + 23.95) / 2, (SILL + ROOF) / 2), (0.04, 23.95 - Y0, ROOF - SILL), M['wall'])
    # window frame posts (interior side)
    for py in (24.0, 24.45, 24.95):
        b.box((s * 1.13, py, 0.74), (0.05, 0.06, 0.62), M['wall'], rot=Matrix.Rotation(math.radians(-s * 18), 3, 'Y'))
# ceiling with overhead panel
b.box((0, (Y0 + 24.95) / 2, ROOF + 0.03), (2.3, 24.95 - Y0, 0.05), M['wall'])
b.box((0, 24.5, ROOF - 0.04), (0.9, 0.85, 0.08), M['panelTex2'], rot=rx(-8))
# aft bulkhead with EWO hatchway
b.box((-0.75, Y0, (FLOOR + ROOF) / 2), (1.2, 0.05, ROOF - FLOOR), M['wall'])
b.box((0.95, Y0, (FLOOR + ROOF) / 2), (0.8, 0.05, ROOF - FLOOR), M['wall'])
b.box((0.2, Y0, ROOF - 0.25), (0.7, 0.05, 0.5), M['wall'])
b.box((0.2, Y0 - 0.05, FLOOR + 0.6), (0.7, 0.02, 1.2), M['black'])         # dark hatchway beyond
# ladder well to the lower deck (radar navigator / navigator)
b.box((0.2, Y0 + 0.3, FLOOR - 0.01), (0.6, 0.5, 0.03), M['black'])
shell = b.write(obj('cp_shell', root, (0, 24.4, 0)))

# Interior skin: the cockpit's own non-glass faces in the cabin region, inset 3 cm and flipped so they face
# inward. This gives real window frames from inside (the exterior skin is culled from behind in Unity).
src = cockpit
glass_idx = {i for i, m in enumerate(src.data.materials) if m and m.name == 'Glass'}
bm = bmesh.new(); bm.from_mesh(src.data); bm.transform(src.matrix_world)
keep = [f for f in bm.faces if f.material_index not in glass_idx
        and 23.1 < f.calc_center_median().y < 26.3 and f.calc_center_median().z > -0.55]
bmesh.ops.delete(bm, geom=[f for f in bm.faces if f not in set(keep)], context='FACES')
for v in bm.verts:
    axis_pt = Vector((0, v.co.y, 0.35))
    d = (v.co - axis_pt); d.y = 0
    if d.length > 0.05:
        v.co -= d.normalized() * 0.03
bmesh.ops.reverse_faces(bm, faces=bm.faces)
wall_i = 0
for f in bm.faces: f.material_index = 0
skin = obj('cp_skin', root, (0, 24.5, 0.35))
bm.transform(skin.matrix_world.inverted()); bm.to_mesh(skin.data); bm.free()
skin.data.materials.append(M['wall'])

# ------------------------------------------------------------------ main instrument panel + glareshield
b = Builder()
PANEL_Y, PANEL_TILT = 25.55, 12        # panel face plane, leaning back 12 degrees
panel_rot = rx(-PANEL_TILT)
b.box((0, PANEL_Y + 0.05, -0.02), (2.1, 0.08, 0.72), M['panel'], rot=panel_rot)
b.box((0, PANEL_Y - 0.04, 0.33), (1.9, 0.24, 0.04), M['glare'], rot=rx(4))          # glareshield
b.box((0, PANEL_Y - 0.16, 0.31), (1.9, 0.03, 0.06), M['glare'])                       # glareshield lip
# centre pedestal under the panel
b.box((0, PANEL_Y - 0.1, -0.35), (0.42, 0.25, 0.32), M['panel'])


def screen(cx, cz, w, h, m, name):
    """MFD bezel on the panel, with its screen as a separate object (so Unity can target the renderer)."""
    off = panel_rot @ Vector((0, -1, 0))
    c = Vector((cx, PANEL_Y, cz)) + off * 0.0
    b.box(c + off * 0.015, (w + 0.06, 0.03, h + 0.06), M['black'], rot=panel_rot)
    # bezel buttons
    for i in range(5):
        for sx in (-1, 1):
            p = c + off * 0.03 + panel_rot @ Vector((sx * (w / 2 + 0.018), 0, -h / 2 + h * (i + 0.5) / 5))
            b.box(p, (0.014, 0.012, 0.022), M['white'], rot=panel_rot)
    s = obj(name, root, tuple(c + off * 0.032))
    sb = Builder()
    corners = [Vector((-w / 2, 0, -h / 2)), Vector((w / 2, 0, -h / 2)), Vector((w / 2, 0, h / 2)), Vector((-w / 2, 0, h / 2))]
    sb.quad([c + off * 0.032 + panel_rot @ q for q in corners], m)
    sb.write(s)
    return s


# CONECT layout: two 8x10 MFDs per pilot plus a large centre tactical display.
screens = [
    screen(-0.78, 0.02, 0.20, 0.25, M['pfd'], 'mfd_L1_screen'),
    screen(-0.50, 0.02, 0.20, 0.25, M['eng'], 'mfd_L2_screen'),
    screen(0.0, 0.02, 0.30, 0.30, M['tsd'], 'mfd_C_screen'),
    screen(0.50, 0.02, 0.20, 0.25, M['eng'], 'mfd_R2_screen'),
    screen(0.78, 0.02, 0.20, 0.25, M['pfd'], 'mfd_R1_screen'),
]
# Standby instruments (round) and switch strip under the displays
off = panel_rot @ Vector((0, -1, 0))
for gx, gz in ((-0.25, 0.20), (-0.25, -0.08), (0.25, 0.20), (0.25, -0.08)):
    c = Vector((gx, PANEL_Y, gz)) + off * 0.02
    b.cyl(c - off * 0.01, c + off * 0.025, 0.055, M['black'], 24)
    b.cyl(c + off * 0.025, c + off * 0.027, 0.047, M['gauge'], 24)
b.box(Vector((0, PANEL_Y, -0.27)) + off * 0.02, (1.9, 0.02, 0.14), M['panelTex'], rot=panel_rot)
panelo = b.write(obj('cp_panel', root, (0, PANEL_Y, 0)))

# ------------------------------------------------------------------ side consoles, centre console, throttles
b = Builder()
for s in (-1, 1):
    b.box((s * 1.15, 24.35, -0.22), (0.38, 1.9, 0.56), M['panel'])
    b.box((s * 1.15, 24.35, 0.065), (0.38, 1.9, 0.02), M['panelTex' if s < 0 else 'panelTex2'])
b.box((0, 24.75, -0.25), (0.40, 1.0, 0.50), M['panel'])
b.box((0, 24.75, 0.005), (0.40, 1.0, 0.02), M['panelTex'])
b.box((0, 24.58, 0.02), (0.30, 0.34, 0.02), M['black'])           # throttle quadrant slot plate
consoles = b.write(obj('cp_consoles', root, (0, 24.5, -0.2)))

# Eight throttle levers in 4 pairs, pivot below the quadrant (rotate about local X).
for i in range(8):
    x = -0.13 + i * (0.26 / 7)
    pivot = Vector((x, 24.55, -0.05))
    t = obj(f'throttle_{i + 1}', root, tuple(pivot))
    tb = Builder()
    tb.box(pivot + Vector((0, 0.0, 0.13)), (0.012, 0.016, 0.26), M['metal'])
    tb.box(pivot + Vector((0, -0.01, 0.27)), (0.022, 0.05, 0.028), M['black'])
    tb.write(t)

# ------------------------------------------------------------------ yokes (B-52 control wheels) and rudder pedals
for side, sx in (('L', -0.55), ('R', 0.55)):
    base = Vector((sx, 25.12, FLOOR))
    col = obj(f'yokeColumn_{side}', root, tuple(base))
    cb = Builder()
    cb.box(base + Vector((0, 0.02, 0.06)), (0.16, 0.22, 0.12), M['black'])
    cb.cyl(base + Vector((0, 0, 0.1)), base + Vector((0, -0.14, 0.56)), 0.03, M['metal'])
    cb.write(col)
    hub = base + Vector((0, -0.15, 0.60))
    y = obj(f'yoke_{side}', root, tuple(hub))
    yb = Builder()
    yb.cyl(hub + Vector((0, 0.03, 0)), hub + Vector((0, -0.05, 0)), 0.035, M['black'])
    yb.torus(hub + Vector((0, -0.05, 0)), (0, 1, 0), 0.165, 0.016, M['black'], arc=(-20, 200))   # open-top wheel
    yb.cyl(hub + Vector((-0.16, -0.05, 0)), hub + Vector((0.16, -0.05, 0)), 0.012, M['black'])
    yb.box(hub + Vector((-0.17, -0.05, 0.05)), (0.03, 0.03, 0.06), M['black'])
    yb.box(hub + Vector((0.17, -0.05, 0.05)), (0.03, 0.03, 0.06), M['black'])
    yb.box(hub + Vector((0, -0.07, 0.02)), (0.06, 0.01, 0.04), M['white'])
    yb.write(y)
    pb = Builder()
    for px in (-0.13, 0.13):
        pb.box((sx + px, 25.45, FLOOR + 0.12), (0.10, 0.03, 0.18), M['metal'], rot=rx(-25))
    pb.write(obj(f'pedals_{side}', root, (sx, 25.45, FLOOR + 0.1)))

# ------------------------------------------------------------------ ACES II seats
for side, sx in (('L', -0.55), ('R', 0.55)):
    sb = Builder()
    sy = 24.12
    sb.box((sx, sy + 0.02, FLOOR + 0.22), (0.50, 0.48, 0.44), M['black'])                 # bucket
    sb.box((sx, sy + 0.05, FLOOR + 0.48), (0.46, 0.46, 0.08), M['seat'])                 # cushion
    sb.box((sx, sy - 0.24, FLOOR + 0.98), (0.48, 0.12, 0.90), M['seat'], rot=rx(-12))     # back
    sb.box((sx, sy - 0.33, FLOOR + 1.50), (0.30, 0.14, 0.22), M['black'], rot=rx(-12))    # headrest / canopy breaker
    for rs in (-1, 1):
        sb.box((sx + rs * 0.27, sy - 0.25, FLOOR + 0.85), (0.04, 0.10, 1.25), M['metal'], rot=rx(-12))   # rails
        sb.box((sx + rs * 0.24, sy + 0.18, FLOOR + 0.58), (0.03, 0.18, 0.05), M['yellow'])               # handles
    sb.write(obj(f'seat_{side}', root, (sx, sy, FLOOR)))

# Pilot eye points (Unity: Pilot seats / camera)
for side, sx in (('L', -0.55), ('R', 0.55)):
    e = bpy.data.objects.new(f'eye_{side}', None); bpy.context.scene.collection.objects.link(e)
    e.matrix_world = Matrix.Translation((sx, 24.55, 0.78)); bpy.context.view_layer.update()
    mw = e.matrix_world.copy(); e.parent = root; e.matrix_world = mw

bpy.context.view_layer.update()
bpy.ops.wm.save_as_mainfile(filepath=bpy.data.filepath)
print('COCKPIT OK', len([o for o in bpy.data.objects if o.parent == root]), 'objects under cockpit_int')
