"""
Adds (or replaces) the artifact for one release in nomnom/B52J.json, the NOMNOM manifest for the B-52
(https://github.com/KopterBuzz/NOMNOM, the registry the NOMM mod manager reads; schema in its SCHEMA.md).
Run by tools/package.sh:  python tools/nomnom_manifest.py <version> <zip name> <sha256> <release|preRelease>
The release on GitHub must be tagged v<version> with the zip as its first asset (NOMNOM takes the first asset).
"""
import json, os, sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
PATH = os.path.join(ROOT, 'nomnom', 'B52J.json')
GAME_VERSION = '0.34.3'          # the game build the release was tested on
version, zip_name, sha, category = sys.argv[1:5]

with open(PATH, encoding='utf-8') as f:
    m = json.load(f)
artifact = {
    'fileName': zip_name,
    'version': version,
    'category': category,
    'type': 'addon',
    'gameVersion': GAME_VERSION,
    'downloadUrl': f"https://github.com/{m['githubOwner']}/{m['githubRepoName']}/releases/download/v{version}/{zip_name}",
    'hash': f'sha256:{sha}',
    'extends': {'id': 'com.nikkorap.blueprinter', 'version': '2.1.1'},
}
m['artifacts'] = [artifact] + [a for a in m.get('artifacts', []) if a.get('version') != version]
with open(PATH, 'w', encoding='utf-8', newline='\n') as f:
    json.dump(m, f, indent=2, ensure_ascii=False); f.write('\n')
print(f'NOMNOM manifest: {PATH} ({category} {version}, {len(m["artifacts"])} artifact(s))')
