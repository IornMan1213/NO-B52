"""Aerodynamic centre of each B-52 part, from the split model, and the resulting pitch neutral point.
Run: blender -b blender/out/B52_parts.blend --python tools/aero_centres.py
Lifting surfaces: quarter chord at mid-span (fin and rudder span vertically). Body parts: vertex centroid.
Uses the same rule as B52Systems.AeroCentres at runtime. Blender axes: x right, y forward, z up."""
import bpy, json, os
from mathutils import Vector

AREA = {'B52': 36, 'fuselage_F': 4, 'cockpit': 1, 'fuselage_R': 3, 'tail': 38, 'rudder': 12, 'hstab_L': 30, 'hstab_R': 30,
        'elevator_L': 12, 'elevator_R': 12, 'wingroot_L': 58, 'wingroot_R': 58, 'wing1_L': 42, 'wing1_R': 42,
        'wing2_L': 27, 'wing2_R': 27, 'wingtip_L': 12, 'wingtip_R': 12, 'flap1_L': 10, 'flap1_R': 10,
        'flap2_L': 11, 'flap2_R': 11, 'spoilers_L': 4, 'spoilers_R': 4}
CG_Z = 5.8   # Unity z of the CoM transform (MassSync pins rb.centerOfMass there)


def ac_of(name, vs):
    if name.startswith(('B52', 'fuselage', 'cockpit')):
        c = sum(vs, Vector()) / len(vs)
        return c.y, c.z
    vertical = name in ('tail', 'rudder')
    sp = [v.z if vertical else abs(v.x) for v in vs]
    lo, hi = min(sp), max(sp); mid = (lo + hi) / 2
    for frac in (0.15, 0.3, 0.6):
        band = [v for v, s in zip(vs, sp) if abs(s - mid) <= frac * (hi - lo)]
        if len(band) >= 4: break
    le = max(v.y for v in band); te = min(v.y for v in band)
    return le - 0.25 * (le - te), sum(v.z for v in band) / len(band)


res = {}
for n, S in AREA.items():
    o = bpy.data.objects.get(n)
    if not o: print('missing', n); continue
    vs = [o.matrix_world @ v.co for v in o.data.vertices]
    z, y = ac_of(n, vs)
    res[n] = (z, y, S)

# Neutral point: lift-slope-weighted mean AC of the surfaces that lift in pitch (all but fin/rudder).
pitch = {n: v for n, v in res.items() if n not in ('tail', 'rudder')}
np_z = sum(z * S for z, _, S in pitch.values()) / sum(S for _, _, S in pitch.values())
wing = {n: v for n, v in res.items() if n.startswith(('wing', 'flap', 'spoil'))}
wing_ac = sum(z * S for z, _, S in wing.values()) / sum(S for _, _, S in wing.values())
for n, (z, y, S) in sorted(res.items(), key=lambda kv: -kv[1][0]):
    print(f'AC {n:12s} z {z:7.2f}  y {y:5.2f}  S {S:3d}')
print(f'AC wing-only centre z {wing_ac:.2f}   neutral point z {np_z:.2f}   CG z {CG_Z}   margin {CG_Z - np_z:+.2f} m')
json.dump({'parts': res, 'np_z': np_z, 'wing_ac': wing_ac},
          open(os.path.join(os.path.dirname(bpy.data.filepath), 'aero_centres.json'), 'w'), indent=1)
