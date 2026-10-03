import bpy, bmesh, mathutils
# probe: wing trailing/leading edge y at several x, hstab edges, loose parts counts
def edges(objname, xs):
    o = bpy.data.objects[objname]; me = o.data
    vs = [o.matrix_world @ v.co for v in me.vertices]
    for x in xs:
        band = [v for v in vs if abs(v.x - x) < 0.6]
        if band: print(objname, 'x=%.1f' % x, 'LE y=%.2f TE y=%.2f zmin=%.2f zmax=%.2f' % (max(v.y for v in band), min(v.y for v in band), min(v.z for v in band), max(v.z for v in band)))
edges('WingLeft', [-2, -5, -8, -12, -16, -20, -24, -27])
edges('HorizontalStabilizers', [-1, -3, -5, -7])
for n in ['Engines', 'EngineBlades', 'RandomStuff', 'RandomStuff2', 'WingLeft', 'Fuselage', 'HorizontalStabilizers']:
    o = bpy.data.objects[n]; bm = bmesh.new(); bm.from_mesh(o.data)
    bm.verts.ensure_lookup_table()
    seen = set(); parts = []
    for v in bm.verts:
        if v.index in seen: continue
        stack = [v]; comp = []
        while stack:
            a = stack.pop()
            if a.index in seen: continue
            seen.add(a.index); comp.append(a)
            for e in a.link_edges: stack.append(e.other_vert(a))
        c = sum((o.matrix_world @ a.co for a in comp), mathutils.Vector()) / len(comp)
        parts.append((len(comp), c))
    print(n, 'loose parts', len(parts))
    if n in ('RandomStuff', 'RandomStuff2', 'Engines'):
        for k, c in sorted(parts, key=lambda p: -p[0])[:60]:
            print('    %5d  (%.1f, %.1f, %.1f)' % (k, c.x, c.y, c.z))
