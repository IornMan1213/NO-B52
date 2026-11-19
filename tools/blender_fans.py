"""
Engine fans as their own objects, so B52Systems can spin them. Run after blender_pylons.py, before blender_atlas.py:
  blender -b blender/out/B52_parts.blend --python tools/blender_fans.py

bohmerang's pods carry both engines' fan discs and spinners (materials Blades1 / Blades2) inside the pod mesh. They are
split out per engine (each pod holds two, side by side) into fan_<pod>_<n> under the pod, with the origin on the fan's
axis, so the object turns about its own forward axis. They keep their materials; blender_atlas.py packs them as usual.
Blender frame: +X right, +Y forward, +Z up.
"""
import bpy, bmesh, mathutils
from mathutils import Matrix, Vector
from mathutils.bvhtree import BVHTree

FAN_MATS = ('Blades1', 'Blades2')
report = []
for pod in ('pod1_L', 'pod1_R', 'pod2_L', 'pod2_R'):
    o = bpy.data.objects.get(pod)
    if not o: continue
    me = o.data; mw = o.matrix_world
    slots = {i for i, m in enumerate(me.materials) if m and m.name in FAN_MATS}
    if not slots: continue
    bm = bmesh.new(); bm.from_mesh(me); bm.transform(mw)
    fan = [f for f in bm.faces if f.material_index in slots]
    if not fan: bm.free(); continue
    xs = sorted(f.calc_center_median().x for f in fan)
    split = (xs[0] + xs[-1]) / 2                                  # the two engines sit side by side in x
    for n, pick in enumerate((lambda x: x < split, lambda x: x >= split)):
        mine = {f.index for f in fan if pick(f.calc_center_median().x)}
        if not mine: continue
        fbm = bm.copy(); fbm.faces.ensure_lookup_table()
        bmesh.ops.delete(fbm, geom=[f for f in fbm.faces if f.index not in mine], context='FACES')
        lo = Vector((min(v.co.x for v in fbm.verts), min(v.co.y for v in fbm.verts), min(v.co.z for v in fbm.verts)))
        hi = Vector((max(v.co.x for v in fbm.verts), max(v.co.y for v in fbm.verts), max(v.co.z for v in fbm.verts)))
        centre = (lo + hi) / 2
        fbm.transform(Matrix.Translation(-centre))
        name = f'fan_{pod}_{n + 1}'
        old = bpy.data.objects.get(name)
        if old: m_old = old.data; bpy.data.objects.remove(old); bpy.data.meshes.remove(m_old)
        fme = bpy.data.meshes.new(name); fbm.to_mesh(fme); fbm.free()
        for m in me.materials: fme.materials.append(m)
        fo = bpy.data.objects.new(name, fme); bpy.context.scene.collection.objects.link(fo)
        fo.matrix_world = Matrix.Translation(centre); bpy.context.view_layer.update()
        w = fo.matrix_world.copy(); fo.parent = o; fo.matrix_world = w
        report.append(f'{name}: {len(mine)} faces, centre {tuple(round(c, 2) for c in centre)}')
    # take the fan faces out of the pod itself
    bmesh.ops.delete(bm, geom=fan, context='FACES')
    bm.transform(mw.inverted()); bm.to_mesh(me); bm.free(); me.update()

# Engine internals. bohmerang's pods have a short fan cowl (radius ~0.71 m, from the fan back about 1.2 m) and behind
# it the engine core, tapering to a flat end face 4 m aft of the fan. Every inside surface was missing: looking into
# the fan cowl's exit you saw through the pod (the game culls back faces), and the core just stopped. Each engine gets
# nozzle_<pod>_<n>, built against the pod's measured shape (rays cast at the pod mesh), so nothing pokes through the
# paint, even where a pod's two cowls meet:
#   - the fan duct: a dark tube just inside the fan cowl, from its exit forward to a dark back wall behind the fan;
#   - 24 outlet guide vanes across the bypass exit, from the core to the duct;
#   - the turbine on the core's end face: 36 twisted blades on a dark disc, a short recess and the exhaust cone.
# B52Builder.BuildLods keeps them at both detail levels.
import math
SEGS = 40
duct_mat = bpy.data.materials.get('B52_EngineDuct')
if duct_mat is None:
    duct_mat = bpy.data.materials.new('B52_EngineDuct'); duct_mat.use_nodes = True
    p = duct_mat.node_tree.nodes['Principled BSDF']
    p.inputs['Base Color'].default_value = (0.045, 0.045, 0.05, 1); p.inputs['Roughness'].default_value = 0.9
    duct_mat.diffuse_color = (0.045, 0.045, 0.05, 1)
metal_mat = bpy.data.materials.get('B52_EngineMetal')
if metal_mat is None:
    metal_mat = bpy.data.materials.new('B52_EngineMetal'); metal_mat.use_nodes = True
    p = metal_mat.node_tree.nodes['Principled BSDF']
    p.inputs['Base Color'].default_value = (0.30, 0.30, 0.32, 1); p.inputs['Metallic'].default_value = 0.8
    p.inputs['Roughness'].default_value = 0.45
    metal_mat.diffuse_color = (0.30, 0.30, 0.32, 1)
ANG = [2 * math.pi * i / SEGS for i in range(SEGS)]
for fo in [o for o in bpy.data.objects if o.name.startswith('fan_') and o.parent]:
    pod = fo.parent; c = fo.matrix_world.translation.copy()
    fv = [fo.matrix_world @ v.co for v in fo.data.vertices]
    fan_aft = min(p.y for p in fv)
    pbm = bmesh.new(); pbm.from_mesh(pod.data); pbm.transform(pod.matrix_world)
    bvh = BVHTree.FromBMesh(pbm); pbm.free()

    def d_of(a): return Vector((math.cos(a), 0, math.sin(a)))

    def cowl_r(a, y):
        """Axis to the fan cowl in this direction: the first surface out from 0.3 m beyond 0.63 m (skips the hub and
        core), or None where there is no cowl (aft of its exit)."""
        rr = 0.3
        for _ in range(8):
            hit = bvh.ray_cast(Vector((c.x, y, c.z)) + d_of(a) * rr, d_of(a), 1.0)
            if hit[0] is None: return None
            rr += hit[3]
            if rr > 0.85: return None                    # past the cowl (the pylon strut above the core)
            if rr > 0.63: return rr
            rr += 0.01
        return None

    def core_r(a, y, centre=None):
        """Outside of the core here: a ray from 0.9 m out, inward to the axis (or to `centre`)."""
        o = centre if centre is not None else Vector((c.x, y, c.z))
        hit = bvh.ray_cast(o + d_of(a) * 0.9, -d_of(a), 0.9)
        return 0.9 - hit[3] if hit[0] is not None else None

    # fan cowl exit: walk aft from the fan until neither the top nor the bottom ray finds the cowl
    y_fe = fan_aft
    # (the top ray only: the core sits low, and its underside reaches the cowl's radius)
    while y_fe > fan_aft - 3.0 and cowl_r(math.pi / 2, y_fe - 0.02):
        y_fe -= 0.02
    y_back = fan_aft - 0.08
    name = fo.name.replace('fan_', 'nozzle_')
    old = bpy.data.objects.get(name)
    if old: m_old = old.data; bpy.data.objects.remove(old); bpy.data.meshes.remove(m_old)
    nb = bmesh.new()

    def V(p): return nb.verts.new(p - c)                 # world point -> local vertex

    def face(vs, mat, want):
        f = nb.faces.new(vs); f.material_index = mat; f.normal_update()
        if f.normal.dot(want(f.calc_center_median())) < 0: f.normal_flip()
        return f

    def quad2(pts, mat=1):
        for order in (pts, pts[::-1]):
            nb.faces.new([V(p) for p in order]).material_index = mat

    # ---- fan duct, following the cowl ring by ring
    stations = [y_fe + 0.01 + (y_back - y_fe - 0.01) * k / 6 for k in range(7)]
    radii = []
    for y in stations:
        rs = [cowl_r(a, y) for a in ANG]
        ok = [r for r in rs if r] or [0.7]
        radii.append([(r if r else min(ok)) - 0.008 for r in rs])
    rings = [[V(Vector((c.x, y, c.z)) + d_of(a) * radii[k][i]) for i, a in enumerate(ANG)] for k, y in enumerate(stations)]
    inward = lambda m: -Vector((m.x, 0, m.z))
    for k in range(6):
        for i in range(SEGS):
            j = (i + 1) % SEGS
            face((rings[k][i], rings[k + 1][i], rings[k + 1][j], rings[k][j]), 0, inward)
    face(rings[-1], 0, lambda m: Vector((0, -1, 0))).smooth = False          # back wall behind the fan
    for f in nb.faces: f.smooth = f.normal.y > -0.9
    # ---- outlet guide vanes, just inside the exit
    yv = y_fe + 0.12
    for i in range(24):
        a = 2 * math.pi * i / 24
        ro = min(radii[1][round(a / (2 * math.pi) * SEGS) % SEGS], radii[2][round(a / (2 * math.pi) * SEGS) % SEGS]) - 0.004
        ri = (core_r(a, yv) or 0.45) + 0.005
        if ro - ri < 0.04: continue
        sk = math.radians(12)
        pts = [Vector((c.x, yv, c.z)) + d_of(a) * ri, Vector((c.x, yv, c.z)) + d_of(a) * ro]
        pts += [Vector((c.x, yv + 0.16, c.z)) + d_of(a + sk * 0.16 / ro) * ro, Vector((c.x, yv + 0.16, c.z)) + d_of(a + sk * 0.16 / ri) * ri]
        quad2(pts)
    # ---- turbine on the core's end face: find the face, then the core's centre and radius just ahead of it
    near = [pod.matrix_world @ v.co for v in pod.data.vertices]
    near = [p for p in near if (p - c).xz.length < 0.55 and fan_aft - 6.0 < p.y < y_fe - 0.5]
    core_end = min(p.y for p in near) if near else None
    if core_end is not None:
        y_s = core_end + 0.08
        # the core's outline just ahead of the end face: 16 rays in, outliers (the pylon strut) dropped, then a
        # least-squares circle (Kasa fit) for its centre and radius
        pts = []
        for k in range(16):
            a = 2 * math.pi * k / 16; r_ = core_r(a, y_s)
            if r_: pts.append((math.cos(a) * r_, math.sin(a) * r_, r_))
        if len(pts) >= 8:
            med = sorted(p_[2] for p_ in pts)[len(pts) // 2]
            pts = [p_ for p_ in pts if abs(p_[2] - med) < 0.2]      # the core sits ~0.1 m off the fan axis
        fit = None
        if len(pts) >= 6:
            n_ = len(pts); sx = sum(p_[0] for p_ in pts); sz = sum(p_[1] for p_ in pts)
            sxx = sum(p_[0] ** 2 for p_ in pts); szz = sum(p_[1] ** 2 for p_ in pts); sxz = sum(p_[0] * p_[1] for p_ in pts)
            b = [sum(p_[0] * (p_[0] ** 2 + p_[1] ** 2) for p_ in pts), sum(p_[1] * (p_[0] ** 2 + p_[1] ** 2) for p_ in pts),
                 sum(p_[0] ** 2 + p_[1] ** 2 for p_ in pts)]
            M = mathutils.Matrix(((2 * sxx, 2 * sxz, sx), (2 * sxz, 2 * szz, sz), (2 * sx, 2 * sz, n_)))
            try:
                sol = M.inverted() @ Vector(b)
                fx, fz = sol[0], sol[1]; R0 = math.sqrt(max(sol[2] + fx * fx + fz * fz, 0))
                fit = (fx, fz, R0)
            except ValueError:
                fit = None
        rr = {0: fit} if fit else {0: None}
        if all(rr.values()):
            off = Vector((fit[0], 0, fit[1]))
            R = fit[2] - 0.01
            cc = Vector((c.x, core_end, c.z)) + off                          # centre of the end face
            ring = lambda r, a, dy=0.0: cc + Vector((0, dy, 0)) + d_of(a) * r
            disc = [V(ring(R * 0.97, a, -0.003)) for a in ANG]
            face(disc, 0, lambda m: Vector((0, -1, 0))).smooth = False
            for i in range(36):                                               # turbine blades
                a = 2 * math.pi * i / 36; tw = math.radians(35) * 0.05 / (0.7 * R)
                quad2([ring(R * 0.42, a, -0.006), ring(R * 0.9, a, -0.006), ring(R * 0.9, a + tw, -0.056), ring(R * 0.42, a + tw, -0.056)])
            base = [V(ring(R * 0.4, a, -0.008)) for a in ANG[::2]]            # exhaust cone
            tip = V(cc + Vector((0, -0.5, 0)))
            for i in range(len(base)):
                f = face((base[i], base[(i + 1) % len(base)], tip), 1, lambda m: Vector((m.x - (cc - c).x, 0, m.z - (cc - c).z)))
                f.smooth = True
            for side in (1, -1):                                              # short recess around the blades
                r0 = [V(ring(R, a, 0.0)) for a in ANG]; r1 = [V(ring(R, a, -0.12)) for a in ANG]
                for i in range(SEGS):
                    j = (i + 1) % SEGS
                    f = face((r0[i], r1[i], r1[j], r0[j]), 1,
                             lambda m, s=side: s * Vector((m.x - (cc - c).x, 0, m.z - (cc - c).z)))
                    f.smooth = True
            report.append(f'  {name}: fan exit y {y_fe:.2f}, core end y {core_end:.2f}, turbine r {R:.2f} off {off.x:+.2f},{off.z:+.2f}')
        else:
            report.append(f'  {name}: core end y {core_end:.2f} but no core outline: no turbine')
    nme = bpy.data.meshes.new(name); nb.to_mesh(nme); nb.free()
    nme.materials.append(duct_mat); nme.materials.append(metal_mat)
    no = bpy.data.objects.new(name, nme); bpy.context.scene.collection.objects.link(no)
    no.matrix_world = Matrix.Translation(c); bpy.context.view_layer.update()
    w = no.matrix_world.copy(); no.parent = pod; no.matrix_world = w
    report.append(f'{name}: fan duct y {y_fe:.2f}..{y_back:.2f}')

bpy.ops.wm.save_mainfile()
print('FANS', *report, sep='\n  ')
