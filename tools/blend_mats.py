import bpy, mathutils
for o in bpy.data.objects:
    if o.type != 'MESH': continue
    me = o.data
    print('==', o.name, 'loose parts?')
    for mi, m in enumerate(me.materials):
        imgs = [n.image.name for n in m.node_tree.nodes if n.type == 'TEX_IMAGE' and n.image] if m and m.use_nodes else []
        ps = [p for p in me.polygons if p.material_index == mi]
        if not ps: continue
        vs = [o.matrix_world @ me.vertices[v].co for p in ps for v in p.vertices]
        mn = [min(v[i] for v in vs) for i in range(3)]; mx = [max(v[i] for v in vs) for i in range(3)]
        print(f'   [{mi}] {m.name:10} imgs={imgs} faces={len(ps)} min=({mn[0]:.1f},{mn[1]:.1f},{mn[2]:.1f}) max=({mx[0]:.1f},{mx[1]:.1f},{mx[2]:.1f})')
