"""Pitch trim with real lever arms (B52Systems.AeroCentres): for each flight phase, the angle of attack and
elevator angle that give lift = weight and zero pitching moment about the CoM, plus how much nose-down
authority is left. Uses tools/flightmodel_check.py curves and blender/out/aero_centres.json
(from tools/aero_centres.py). Unity axes: z forward, y up. Elevator delta > 0 = trailing edge down.
Run: python tools/trim_check.py"""
import json, math, os, sys
sys.path.insert(0, os.path.dirname(__file__))
import flightmodel_check as fm

HERE = os.path.dirname(__file__)
AC = json.load(open(os.path.join(HERE, '..', 'blender', 'out', 'aero_centres.json')))['parts']
CG = (5.8, 0.3)                     # (z, y) of the CoM transform
ENG_Y = -0.4                        # mean nozzle height (inboard -0.6, outboard -0.2)
INC = fm.INC
FLAP_PARTS = ('flap1', 'flap2')     # get FLAP_OWN camber and doubled area
CAMBER_PARTS = tuple(os.environ.get('CAMBER_PARTS', 'wingroot,wing1').split(','))   # get FLAP_INNER camber
ELEV_MAX = 20.0


def part_alpha(name, alpha, flaps, delta):
    base = name.rsplit('_', 1)[0] if name[-2] == '_' else name
    if base in ('tail', 'rudder'): return None
    a = alpha
    if base in ('wingroot', 'wing1', 'wing2', 'wingtip', 'flap1', 'flap2'): a += INC
    if flaps and base in CAMBER_PARTS: a += fm.FLAP_INNER
    if flaps and base in FLAP_PARTS: a += fm.FLAP_OWN
    if base == 'elevator': a += delta
    return a


def forces(V, alt, alpha, delta, flaps, thrust_frac):
    rho = fm.interp(fm.DENSITY, alt); q = 0.5 * rho * V * V
    L = D = M = 0.0
    for name, (z, y, S) in AC.items():
        a = part_alpha(name, alpha, flaps, delta)
        if a is None: continue
        base = name.rsplit('_', 1)[0] if name[-2] == '_' else name
        if flaps and base in FLAP_PARTS: S *= 2.0
        r = math.radians(a)
        l = fm.interp(fm.B52_CL, r) * q * S
        d = fm.interp(fm.B52_CD, r) * q * S
        L += l; D += d
        M += l * (z - CG[0]) + d * (y - CG[1])            # nose-up positive
    T = fm.thrust(V, alt) * thrust_frac
    M += T * (CG[1] - ENG_Y)
    return L, D, M, T


def trim(V, alt, mass, flaps, thrust_frac):
    """Solve alpha (lift = weight) and delta (moment = 0) by nested bisection."""
    W = mass * 9.81

    def alpha_for(delta):
        lo, hi = -10.0, 15.0
        for _ in range(50):
            mid = (lo + hi) / 2
            if forces(V, alt, mid, delta, flaps, thrust_frac)[0] < W: lo = mid
            else: hi = mid
        return (lo + hi) / 2

    lo, hi = -40.0, 40.0
    for _ in range(50):
        d = (lo + hi) / 2
        a = alpha_for(d)
        if forces(V, alt, a, d, flaps, thrust_frac)[2] > 0: lo = d      # still nose-up: more TE down
        else: hi = d
    d = (lo + hi) / 2; a = alpha_for(d)
    return a, d


CASES = [
    ('lift-off, 200 t, flaps, full power', 92, 0, 200000, True, 1.0),
    ('climb-out, 200 t, flaps, full power', 105, 300, 200000, True, 1.0),
    ('clean climb, 190 t, 300 kt', 155, 3000, 190000, False, 1.0),
    ('cruise, 155 t, M0.8 at 10.7 km', 240, 10700, 155000, False, 0.75),
    ('approach, 130 t, flaps, 140 kt', 72, 300, 130000, True, 0.35),
]

if __name__ == '__main__':
    print(f'camber on {CAMBER_PARTS}, flap camber {fm.FLAP_OWN}, inner {fm.FLAP_INNER}')
    for label, V, alt, m, flaps, tf in CASES:
        a, d = trim(V, alt, m, flaps, tf)
        # nose-down authority: moment with full TE-down elevator at the trim alpha + 5 deg (a gust or over-rotation)
        _, _, Mdn, _ = forces(V, alt, a + 5, ELEV_MAX, flaps, tf)
        ok = 'OK' if abs(d) <= ELEV_MAX * 0.75 and Mdn < 0 else 'PROBLEM'
        print(f'{label:38s} alpha {a:5.1f}  elevator {d:+6.1f} deg (TE down +)  nose-down moment at +5 deg: {Mdn/1e6:+6.2f} MN.m  {ok}')
