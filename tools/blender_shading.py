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
bpy.ops.wm.save_mainfile()
print(f'SHADING total {total_fixed} of {total} corners rebuilt (by angle {ANGLE:.0f} deg)')
