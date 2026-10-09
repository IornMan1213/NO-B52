"""
Main-gear wells, run after blender_gear.py:
  blender -b blender/out/B52_parts.blend --python tools/blender_wells.py

The trucks used to fold through solid skin. Now each main truck gets a real well:
  - The hull is cut along the door outline: the lower fuselage side from the chine (x +-1.46, z -1.78) in to the centreline
    (x +-0.03), a trapezoid in plan running from just behind the trunnion to 2.0 m ahead of it (port trucks fold forward)
    or behind it (starboard trucks fold aft), narrowing to 1.2 m along its inner edge (the V-shaped door of the B-52H).
  - The skin that comes out becomes the door (gearDoorPanel_<k>): exactly the hole's shape, flush and with the skin's own
    atlas UVs when shut, plus a dark inner face 2.5 cm inside it.
  - A well box (wellBox_<k>) closes the hole from inside: walls from the hole's edge up to a roof at z -0.75, above the
    stowed wheel pair (hub z -1.35, wheels flat, 0.9 m stack), dark like the real wells. Drawn from both sides.
The Unity builder hinges the door at the chine and lets LandingGear open it before the gear comes down.
Blender frame: +X right, +Y forward, +Z up.
"""
import bpy, bmesh, math
from mathutils import Vector
from mathutils.bvhtree import BVHTree

HINGE_X, INNER_X, CHINE_Z, ROOF_Z = 1.46, 0.03, -1.78, -0.75
S0_HINGE, S1_HINGE, S0_FREE, S1_FREE = -0.35, 2.00, 0.25, 1.45     # along the fuselage from the trunnion (fold direction)
DOOR_IN = 0.025

def mat(name, rgb):
    m = bpy.data.materials.get(name)
    if m: return m
    m = bpy.data.materials.new(name); m.use_nodes = True
    m.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value = (*rgb, 1)
    return m

M_WELL = mat('B52_BayInterior', (0.16, 0.17, 0.16))


def outline(sgn, py, dirn):
    """Region test in plan + the four cutting planes for one well."""
    def t_of(x): return (HINGE_X - sgn * x) / (HINGE_X - INNER_X)
    def s_rng(t): return (S0_HINGE + (S0_FREE - S0_HINGE) * t, S1_HINGE + (S1_FREE - S1_HINGE) * t)
    def inside(p):
        if p.z > CHINE_Z or not (INNER_X <= sgn * p.x <= HINGE_X + 0.05): return False
        a, b = s_rng(min(max(t_of(p.x), 0.0), 1.0)); s = (p.y - py) * dirn
        return a <= s <= b
    # edges of the trapezoid in plan: points (x, y)
    def P(t, s): return Vector((sgn * (HINGE_X + (INNER_X - HINGE_X) * t), py + dirn * s, 0.0))
    planes = [(Vector((sgn * INNER_X, 0, 0)), Vector((1, 0, 0))), (Vector((0, 0, CHINE_Z)), Vector((0, 0, 1)))]
    for s_h, s_f in ((S0_HINGE, S0_FREE), (S1_HINGE, S1_FREE)):
        a, b = P(0, s_h), P(1, s_f)
        d = (b - a).normalized()
        planes.append((a, Vector((-d.y, d.x, 0)).normalized()))
    return inside, planes


report = []
for k in ('FL', 'FR', 'RL', 'RR'):
    gear = bpy.data.objects[f'gear_{k}']
    part = gear.parent
    while part.parent and not part.name.startswith(('B52', 'fuselage')): part = part.parent
    pivot = gear.matrix_world.translation
    sgn = 1.0 if k[1] == 'R' else -1.0
    dirn = 1.0 if sgn < 0 else -1.0                                     # port wells ahead of the trunnion
    inside, planes = outline(sgn, pivot.y, dirn)
    ylo = pivot.y + min(dirn * S0_HINGE, dirn * S1_HINGE) - 0.3
    yhi = pivot.y + max(dirn * S0_HINGE, dirn * S1_HINGE) + 0.3

    mw = part.matrix_world; inv = mw.inverted()
    bm = bmesh.new(); bm.from_mesh(part.data); bm.transform(mw)
    def near(f):
        c = f.calc_center_median()
        return ylo < c.y < yhi and sgn * c.x > -0.05 and c.z < CHINE_Z + 0.4
    for co, no in planes:
        faces = [f for f in bm.faces if near(f)]
        geom = list({v for f in faces for v in f.verts}) + list({e for f in faces for e in f.edges}) + faces
        bmesh.ops.bisect_plane(bm, geom=geom, plane_co=co, plane_no=no, dist=1e-4)
    bm.faces.ensure_lookup_table()
    # only the outer skin: the first face a ray from outside the hull, aimed at the fuselage axis, meets (the model also
    # has surfaces inside the fuselage here, which must stay)
    tree = BVHTree.FromBMesh(bm)
    def outer(f):
        c = f.calc_center_median(); r = Vector((c.x, 0.0, c.z + 0.63))
        if r.length < 0.2: return False
        r.normalize()
        loc, _, idx, _ = tree.ray_cast(c + r * 4.0, -r, 4.5)
        return idx is not None and (idx == f.index or (loc - c).length < 0.01)
    cut = [f for f in bm.faces if inside(f.calc_center_median()) and outer(f)]
    cut_idx = {f.index for f in cut}

    # door panel: a copy of the hull with everything but the cut faces removed
    dbm = bm.copy()
    dbm.faces.ensure_lookup_table()
    bmesh.ops.delete(dbm, geom=[f for f in dbm.faces if f.index not in cut_idx], context='FACES')
    if M_WELL.name not in [m.name for m in part.data.materials if m]: part.data.materials.append(M_WELL)
    well_slot = [m.name if m else '' for m in part.data.materials].index(M_WELL.name)
    inner = bmesh.ops.duplicate(dbm, geom=list(dbm.faces))['geom']
    ifaces = [g for g in inner if isinstance(g, bmesh.types.BMFace)]
    dbm.normal_update()
    for v in {v for f in ifaces for v in f.verts}:
        v.co -= v.normal * DOOR_IN
    bmesh.ops.reverse_faces(dbm, faces=ifaces)
    for f in ifaces: f.material_index = well_slot
    door_me = bpy.data.meshes.new(f'gearDoorPanel_{k}')
    dbm.transform(inv); dbm.to_mesh(door_me); dbm.free()
    for m in part.data.materials: door_me.materials.append(m)
    door = bpy.data.objects.new(f'gearDoorPanel_{k}', door_me); bpy.context.scene.collection.objects.link(door)
    door.matrix_world = mw.copy(); door.parent = part; door.matrix_world = mw.copy()

    # hull: remove the cut faces, and any of the model's internal surfaces inside the well volume (they would show in the
    # open well); keep the hole's edge loop for the well walls
    def in_well(f):
        c = f.calc_center_median()
        return c.z < ROOF_Z and inside(Vector((c.x, c.y, min(c.z, CHINE_Z - 0.001))))
    junk = [f for f in bm.faces if f.index not in cut_idx and in_well(f) and not outer(f)]
    bmesh.ops.delete(bm, geom=cut + junk, context='FACES')
    bm.edges.ensure_lookup_table()
    rim = [e for e in bm.edges if e.is_boundary and inside((e.verts[0].co + e.verts[1].co) / 2 + Vector((0, 0, -0.002)))
           or (e.is_boundary and ylo < e.verts[0].co.y < yhi and sgn * e.verts[0].co.x > -0.05 and e.verts[0].co.z < CHINE_Z + 0.01)]
    rim_pts = [(e.verts[0].co.copy(), e.verts[1].co.copy()) for e in rim]
    bm.transform(inv); bm.to_mesh(part.data); bm.free(); part.data.update()

    # well box: walls from the rim up to the roof, roof over the opening; both windings so it shows from any side
    wb = bmesh.new()
    def V(p): return wb.verts.new(p)
    roof_pts = []
    for a, b in rim_pts:
        if (b - a).length < 1e-4: continue
        ra, rb = Vector((a.x, a.y, ROOF_Z)), Vector((b.x, b.y, ROOF_Z))
        for quad in ((a, b, rb, ra), (ra, rb, b, a)):
            f = wb.faces.new([V(p) for p in quad])
        roof_pts += [ra, rb]
    # roof: the opening's plan outline at roof height (convex hull of the rim in plan)
    pts2 = sorted({(round(p.x, 4), round(p.y, 4)) for p in roof_pts})
    def cross(o, a, b): return (a[0] - o[0]) * (b[1] - o[1]) - (a[1] - o[1]) * (b[0] - o[0])
    lower, upper = [], []
    for p in pts2:
        while len(lower) >= 2 and cross(lower[-2], lower[-1], p) <= 0: lower.pop()
        lower.append(p)
    for p in reversed(pts2):
        while len(upper) >= 2 and cross(upper[-2], upper[-1], p) <= 0: upper.pop()
        upper.append(p)
    hull2 = lower[:-1] + upper[:-1]
    if len(hull2) >= 3:
        ring = [Vector((x, y, ROOF_Z)) for x, y in hull2]
        wb.faces.new([V(p) for p in ring]); wb.faces.new([V(p) for p in reversed(ring)])
    bmesh.ops.remove_doubles(wb, verts=wb.verts, dist=1e-4)
    wb.transform(inv)
    wme = bpy.data.meshes.new(f'wellBox_{k}'); wb.to_mesh(wme); wb.free(); wme.materials.append(M_WELL)
    well = bpy.data.objects.new(f'wellBox_{k}', wme); bpy.context.scene.collection.objects.link(well)
    well.matrix_world = mw.copy(); well.parent = part; well.matrix_world = mw.copy()
    report.append(f'{k}: {len(cut)} skin faces -> door, {len(junk)} internal faces removed, rim {len(rim_pts)} edges, part {part.name}, '
                  f'y {pivot.y + dirn * S0_HINGE:.2f}..{pivot.y + dirn * S1_HINGE:.2f}')

bpy.ops.wm.save_mainfile()
print('WELLS', *report, sep='\n  ')
