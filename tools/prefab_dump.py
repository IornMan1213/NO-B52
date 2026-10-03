"""Summarise a Unity YAML prefab: hierarchy, script components and selected fields.
Usage: python prefab_dump.py <prefab> <scripts_root> [field,field,...]"""
import os, re, sys, glob

def guid_map(root):
    m = {}
    for meta in glob.glob(os.path.join(root, '**', '*.cs.meta'), recursive=True):
        g = re.search(r'guid: (\w+)', open(meta, encoding='utf-8').read())
        if g: m[g.group(1)] = os.path.basename(meta)[:-8]
    return m

def docs(path):
    txt = open(path, encoding='utf-8').read()
    for part in re.split(r'^--- ', txt, flags=re.M)[1:]:
        hdr, _, body = part.partition('\n')
        h = re.match(r'!u!(\d+) &(-?\d+)', hdr)
        if h: yield int(h.group(1)), h.group(2), body

def main():
    prefab, root = sys.argv[1], sys.argv[2]
    fields = sys.argv[3].split(',') if len(sys.argv) > 3 else []
    gm = guid_map(root)
    gos, tr, comps = {}, {}, {}
    for cls, fid, body in docs(prefab):
        if cls == 1:
            gos[fid] = re.search(r'm_Name: (.*)', body).group(1)
        elif cls == 4:
            go = re.search(r'm_GameObject: \{fileID: (-?\d+)', body).group(1)
            fa = re.search(r'm_Father: \{fileID: (-?\d+)', body).group(1)
            tr[fid] = (go, fa)
        else:
            go = re.search(r'm_GameObject: \{fileID: (-?\d+)', body)
            if not go: continue
            name = {54:'Rigidbody',65:'BoxCollider',64:'MeshCollider',136:'CapsuleCollider',135:'SphereCollider',23:'MeshRenderer',33:'MeshFilter',137:'SkinnedMeshRenderer',82:'AudioSource',198:'ParticleSystem',199:'PSRenderer',108:'Light',20:'Camera',95:'Animator'}.get(cls, f'cls{cls}')
            if cls == 114:
                g = re.search(r'm_Script: \{fileID: \d+, guid: (\w+)', body)
                name = gm.get(g.group(1), '?' + g.group(1)[:6]) if g else 'MB?'
            vals = []
            for f in fields:
                mm = re.search(r'^  ' + re.escape(f) + r': (.*)$', body, re.M)
                if mm: vals.append(f'{f}={mm.group(1).strip()[:60]}')
            comps.setdefault(go.group(1), []).append(name + (' {' + ', '.join(vals) + '}' if vals else ''))
    by_go = {go: t for t, (go, fa) in tr.items()}
    children = {}
    for t, (go, fa) in tr.items(): children.setdefault(fa, []).append(t)
    def walk(t, d):
        go = tr[t][0]
        skip = {'MeshFilter', 'MeshRenderer', 'PSRenderer'}
        cs = [c for c in comps.get(go, []) if c.split(' ')[0] not in skip]
        print('  ' * d + gos.get(go, '?') + (' : ' + '; '.join(cs) if cs else ''))
        for c in children.get(t, []): walk(c, d + 1)
    for r in children.get('0', []): walk(r, 0)

if __name__ == '__main__': main()
