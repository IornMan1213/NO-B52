"""
Keep a cut skin's shading (imported by blender_wells.py and blender_wingwells.py).

Cutting a door out of the skin makes two separate meshes, and each recomputes its normals from its own faces only:
along the cut, the door's edge and the hole's rim then shade differently from the unbroken skin, and the outline shows
as a seam. reference() records the skin's smooth normals just before the cut; keep() gives the loops of the pieces
afterwards the normal the skin had at that exact point, as custom split normals (the FBX export carries them to Unity).
"""
from mathutils.kdtree import KDTree


def reference(bm):
    """Smooth vertex normals of a world-space bmesh, before it is split."""
    bm.normal_update()
    kd = KDTree(len(bm.verts)); nrm = []
    for i, v in enumerate(bm.verts):
        kd.insert(v.co, i); nrm.append(v.normal.copy())
    kd.balance()
    return kd, nrm


def keep(obj, ref, select=lambda co: True, tol=1e-4):
    """Custom normals on obj: on smooth-shaded faces, loops whose vertex sits where a reference vertex was (and passes
    select, world coords) take the reference normal; every other loop keeps the normal it has (flat faces never had a
    seam to hide). Returns the number of loops changed."""
    kd, nrm = ref
    me = obj.data
    mw = obj.matrix_world; to_local = mw.to_3x3().inverted()
    me.update()
    normals = [c.vector.copy() for c in me.corner_normals]
    changed = 0
    for poly in me.polygons:
        if not poly.use_smooth: continue
        hits = []
        for li in poly.loop_indices:
            p = mw @ me.vertices[me.loops[li].vertex_index].co
            if not select(p): continue
            _, i, d = kd.find(p)
            if i is not None and d < tol: hits.append((li, i))
        for li, i in hits:
            normals[li] = (to_local @ nrm[i]).normalized(); changed += 1
    me.normals_split_custom_set(normals)
    return changed
