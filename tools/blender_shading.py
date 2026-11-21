"""
Shading normals rebuilt across sharp edges, run last before the FBX export:
  blender -b blender/out/B52_parts.blend --python tools/blender_shading.py

Each corner's shading normal came out as the mean over every face at its vertex. Where faces meet at a sharp edge that
bends the light across the edge: the flaps, rudder and other thin slabs (top, bottom and ends sharing corners), the pod
and wing-root creases, and worst of all the distant models (*_LoD), whose decimation smoothed every face, so on
fuselage_R_LoD half the skin had normals over 45 deg off its faces. In the game that shows as dark smears and shadow
streaks. The MiG-29 had the same fault on its flaps and ailerons (MiG29-NuclearOption tools/normals_by_angle.py).

As there, each corner's target normal is the corner-angle-weighted mean of the face normals at its position (welded, so
UV seams stay smooth) that lie within ANGLE of its own face's: smooth across curvature, sharp across creases. But only
corners whose normal is more than KEEP off that target are replaced. The rest keep what they have, so the custom normals
set on purpose (blender_wells.py and blender_wingwells.py keep the skin's shading across door cuts) still hide the seams.
"""
import bpy, math
import numpy as np

ANGLE = 50.0          # between the skin's curvature (faces meet at up to ~40 deg) and its creases (70-120 deg)
KEEP = 25.0           # a corner this close to its target is left alone
# Shaded flat: the rudder is 8 triangles from the source model, a thin slab with flat sides, and smoothing that few
# faces bent the light across a third of it whatever the angle
FLAT = {'rudder'}

cos_a = math.cos(math.radians(ANGLE)); cos_k = math.cos(math.radians(KEEP))
total_fixed = total = 0
for o in bpy.data.objects:
    if o.type != 'MESH' or not o.data.polygons: continue
    me = o.data
    me.update()
    nl = len(me.loops)
    cur = np.empty(nl * 3); me.corner_normals.foreach_get('vector', cur); cur = cur.reshape(-1, 3)
    lv = np.empty(nl, int); me.loops.foreach_get('vertex_index', lv)
    co = np.empty(len(me.vertices) * 3); me.vertices.foreach_get('co', co); co = co.reshape(-1, 3)
    np_ = len(me.polygons)
    fn = np.empty(np_ * 3); me.polygons.foreach_get('normal', fn); fn = fn.reshape(-1, 3)
    ls = np.empty(np_, int); me.polygons.foreach_get('loop_start', ls)
    lt = np.empty(np_, int); me.polygons.foreach_get('loop_total', lt)
    if o.name.split('_LoD')[0] in FLAT:
        me.polygons.foreach_set('use_smooth', np.zeros(np_, bool))
    sm = np.empty(np_, bool); me.polygons.foreach_get('use_smooth', sm)
    lp = np.repeat(np.arange(np_), lt)                                   # loop -> polygon
    # corner angle at each loop: its weight
    prev = np.arange(nl) - 1; nxt = np.arange(nl) + 1
    first = ls[lp]; last = ls[lp] + lt[lp] - 1
    prev[np.arange(nl) == first] = last[np.arange(nl) == first]
    nxt[np.arange(nl) == last] = first[np.arange(nl) == last]
    u = co[lv[nxt]] - co[lv]; w = co[lv[prev]] - co[lv]
    c = np.einsum('ij,ij->i', u, w) / np.maximum(np.linalg.norm(u, axis=1) * np.linalg.norm(w, axis=1), 1e-12)
    cw = np.arccos(np.clip(c, -1, 1))
    _, pid = np.unique(np.round(co, 4), axis=0, return_inverse=True); pid = pid.ravel()[lv]
    order = np.argsort(pid, kind='stable')
    starts = np.flatnonzero(np.r_[True, np.diff(pid[order]) != 0, True])
    tgt = cur.copy()
    for a, b in zip(starts[:-1], starts[1:]):
        L = order[a:b]
        f = fn[lp[L]]; wt = cw[L]
        n = ((f @ f.T) >= cos_a) @ (f * wt[:, None])
        ln = np.linalg.norm(n, axis=1)
        ok = ln > 1e-9
        tgt[L[ok]] = n[ok] / ln[ok][:, None]
    flat = ~sm[lp]
    tgt[flat] = fn[lp[flat]]                    # flat faces: their own normal (a custom normal still overrides flat)
    fix = np.einsum('ij,ij->i', cur, tgt) < cos_k
    total += nl
    if not fix.any(): continue
    new = cur.copy(); new[fix] = tgt[fix]
    me.normals_split_custom_set([tuple(v) for v in new])
    total_fixed += int(fix.sum())
    if fix.sum() > 20: print(f'SHADING {o.name}: {int(fix.sum())} of {nl} corners rebuilt across sharp edges')
# Gaps in the top skin. Along the flaps' front edges (and a few other seams) the source model's top skin stops short of
# the next part's, so from above the only surface there is the bottom skin, seen from behind: the game draws only the
# front of a face, and the sky showed through as a white hairline (EX20). Each wing bottom-skin face with no top skin
# anywhere above it gets an upward-facing copy 1 mm above: from above, a line of the wing's own paint.
import bmesh
from mathutils import Vector
from mathutils.bvhtree import BVHTree
WING = ('wingroot', 'wing1', 'wing2', 'wingtip', 'flap1', 'flap2', 'aileron', 'hstab', 'elevator')
wing_objs = [o for o in bpy.data.objects if o.type == 'MESH' and o.name.split('_')[0] in WING
             and not o.name.endswith(('_LoD', '_struct')) and o.data.polygons]
allb = bmesh.new()
for o in wing_objs:
    t = bmesh.new(); t.from_mesh(o.data); t.transform(o.matrix_world)
    me_ = bpy.data.meshes.new('_t'); t.to_mesh(me_); t.free(); allb.from_mesh(me_); bpy.data.meshes.remove(me_)
allb.normal_update(); allb.faces.ensure_lookup_table()
tree = BVHTree.FromBMesh(allb)
up = Vector((0, 0, 1)); gap_total = 0
def covered_above(q):
    """Is there an upward-facing face somewhere above the point (seen from above, does something hide it)?"""
    p0 = q + up * 0.002
    for _ in range(8):
        loc, n, idx, dist = tree.ray_cast(p0, up, 3.0)
        if loc is None: return False
        if allb.faces[idx].normal.z > 0.3: return True
        p0 = loc + up * 0.002
    return False
for o in wing_objs:
    bm = bmesh.new(); bm.from_mesh(o.data); bm.transform(o.matrix_world); bm.normal_update()
    exposed = []
    for f in bm.faces:
        if f.normal.z > -0.7: continue
        c = f.calc_center_median()
        # the centre and points near each corner and edge middle: a big face can be covered at its centre and open
        # along one edge
        pts = [c] + [c.lerp(v.co, 0.85) for v in f.verts] + [c.lerp((e.verts[0].co + e.verts[1].co) / 2, 0.85) for e in f.edges]
        if any(not covered_above(q) for q in pts): exposed.append(f)
    if exposed:
        new_faces = bmesh.ops.duplicate(bm, geom=exposed)['geom']
        nf = [g for g in new_faces if isinstance(g, bmesh.types.BMFace)]
        bmesh.ops.reverse_faces(bm, faces=nf)
        for v in {v for f in nf for v in f.verts}: v.co += up * 0.001
        bm.transform(o.matrix_world.inverted()); bm.to_mesh(o.data); o.data.update()
        area = sum(f.calc_area() for f in exposed)
        gap_total += len(exposed)
        print(f'SHADING {o.name}: {len(exposed)} bottom-skin faces open from above ({area:.2f} m2) given a top side')
    bm.free()
allb.free()
print('SHADING top-skin gaps closed:', gap_total, 'faces')

# Cut ends. The source model's wing is cut spanwise into parts (wingroot, wing1, wing2, wingtip and the flaps) and each
# part is an open shell: at the cut, its skin just stops. Where two parts meet, the gap between their skins, seen at a
# grazing angle, looked through both open ends into the sky: a fore-and-aft hairline (EX20). Each part's outline at
# its own innermost and outermost span station is filled with a flat cap facing out of the part, so the crack shows
# the wing's own paint. Its loose ends (the outline has small gaps where the skin's edges don't meet) are joined to
# their nearest neighbour first.
from collections import defaultdict
CAP_TOL = 0.02          # m: how far from the station plane a cut-end edge may lie
cap_total = 0
for o in wing_objs:
    bm = bmesh.new(); bm.from_mesh(o.data); bm.transform(o.matrix_world)
    bm.verts.ensure_lookup_table()
    uv = bm.loops.layers.uv.active
    # stations: span positions where the open edges lying across the span add up to a wing section (over 1 m of edge
    # and 1.5 m of chord); smaller ones are fairings and pods, closed or not, left alone
    by_x = defaultdict(list)
    for e in bm.edges:
        a, b = e.verts
        if e.is_boundary and abs(a.co.x - b.co.x) < CAP_TOL: by_x[round((a.co.x + b.co.x) / 2 / CAP_TOL)].append(e)
    stations = []
    for k in sorted(by_x):
        es = by_x[k] + by_x.get(k + 1, [])                                  # a station may straddle two bins
        ys = [v.co.y for e in es for v in e.verts]
        if sum(e.calc_length() for e in es) >= 1.0 and max(ys) - min(ys) >= 1.5 and not any(abs(k - s) <= 1 for s, _ in stations):
            stations.append((k, sum(v.co.x for e in es for v in e.verts) / (2 * len(es))))
    made = 0
    for _, station in stations:
        if abs(station) < 1.0: continue                                     # the centre line: the fuselage covers it
        es = [e for e in bm.edges if e.is_boundary and all(abs(v.co.x - station) < CAP_TOL for v in e.verts)]
        if sum(e.calc_length() for e in es) < 1.0: continue               # no real section here
        # out of the part: away from the skin that the cut edges belong to
        side = sum(f.calc_center_median().x - station for e in es for f in e.link_faces)
        out_x = -1.0 if side > 0 else 1.0
        vs = {v for e in es for v in e.verts}
        deg = defaultdict(int)
        for e in es:
            for v in e.verts: deg[v] += 1
        loose = [v for v in vs if deg[v] == 1]
        # join loose ends pairwise, nearest first
        while len(loose) >= 2:
            a = loose.pop(0)
            b = min(loose, key=lambda v: (v.co - a.co).length)
            loose.remove(b)
            if bm.edges.get((a, b)) is None: es.append(bm.edges.new((a, b)))
        n_before = len(bm.faces)
        res = bmesh.ops.triangle_fill(bm, use_beauty=True, use_dissolve=False, edges=es)
        faces = [f for f in res['geom'] if isinstance(f, bmesh.types.BMFace)]
        if not faces: continue
        for f in faces:
            f.normal_update()
            if f.normal.x * out_x < 0: f.normal_flip()
            f.smooth = False
            if f.material_index is not None and f.verts[0].link_faces:
                nb = [g for g in f.verts[0].link_faces if g not in faces]
                if nb: f.material_index = nb[0].material_index
            if uv:   # each corner takes the UV its vertex has on the skin next to it: the paint at the cut
                for l in f.loops:
                    skin = [m for m in l.vert.link_loops if m.face not in faces]
                    if skin: l[uv].uv = skin[0][uv].uv
        made += len(faces)
    if made:
        bm.transform(o.matrix_world.inverted()); bm.to_mesh(o.data); o.data.update()
        print(f'SHADING {o.name}: cut ends capped with {made} faces')
        cap_total += made
    bm.free()
print('SHADING cut ends capped:', cap_total, 'faces')
bpy.ops.wm.save_mainfile()
print(f'SHADING total {total_fixed} of {total} corners rebuilt (by angle {ANGLE:.0f} deg)')
