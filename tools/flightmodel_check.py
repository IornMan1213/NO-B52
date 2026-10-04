"""
Offline check of the B-52J flight model using Nuclear Option's own aero equations (AeroJob_Math, Turbofan).

Per AeroPart:  lift = CL(alpha) * 0.5*rho*V^2 * wingArea      (alpha measured in the part's lift-normal frame)
               drag = CD(alpha) * 0.5*rho*V^2 * wingArea + 0.25*rho*V^2 * dragArea
Engine thrust: staticThrust * altitudeThrust(alt) * speedThrust(V)
Airfoil curves are sampled in radians. The density curve is the game's GameAssets.airDensityAltitude.

Prints the lift-off speed, the trim attitude in cruise, and the maximum level speed at several altitudes.
Run: python tools/flightmodel_check.py
"""
import math

# ---------------------------------------------------------------- curves (piecewise linear between keys)
def interp(keys, x):
    if x <= keys[0][0]: return keys[0][1]
    for (x0, y0), (x1, y1) in zip(keys, keys[1:]):
        if x <= x1: return y0 + (y1 - y0) * (x - x0) / (x1 - x0)
    return keys[-1][1]

DENSITY = [(0, 1.225), (10000, 0.403), (15000, 0.192), (20000, 0.0833), (35000, 0.0)]       # game curve (km keys)

# B-52 airfoil (B52Builder: airfoils[0]).
B52_CL = [(-3.14159, 0), (-1.57, 0), (-0.785, -1.1), (-0.45, -1.0), (-0.30, -1.45), (-0.22, -1.1), (0, 0),
          (0.22, 1.1), (0.30, 1.45), (0.45, 1.0), (0.785, 1.1), (1.57, 0), (3.14159, 0)]
B52_CD = [(-3, 0), (-1.49, 0.6), (-0.30, 0.13), (-0.20, 0.06), (-0.10, 0.022), (0, 0.010),
          (0.10, 0.022), (0.20, 0.06), (0.30, 0.13), (1.49, 0.6), (3, 0)]

F130_ALT = [(0, 1.0), (3000, 0.80), (6000, 0.62), (9000, 0.47), (12000, 0.33), (15000, 0.21), (17500, 0.12)]
F130_SPD = [(0, 1.0), (50, 0.88), (100, 0.77), (150, 0.73), (200, 0.70), (250, 0.69), (280, 0.66), (300, 0.58), (340, 0.30)]
STATIC = 75600.0 * 8

INC = 6.0          # wing incidence, degrees
FLAP_INNER = 10.0   # extra camber on the inner wing when the flaps are down
FLAP_OWN = 10.0    # extra camber on the flap panels

# name: (wingArea, dragArea, incidence_deg, inner_wing)  - B52Builder.Phys
PARTS = {
    'B52': (36, 0.55, 0, False), 'fuselage_F': (4, 0.45, 0, False), 'cockpit': (1, 0.30, 0, False),
    'fuselage_R': (3, 0.35, 0, False), 'hstab': (60, 0.06, 0, False), 'elevator': (24, 0, 0, False),
    'wingroot': (116, 0.10, INC, False), 'wing1': (84, 0.08, INC, True), 'wing2': (54, 0.06, INC, True),
    'wingtip': (24, 0.24, INC, False), 'flaps': (42, 0, INC, None), 'spoilers': (8, 0, 0, False),
    'pods': (0, 1.40, 0, False),
}
TAIL_SIDE = (50, 0.06)   # fin + rudder: lift normal is sideways, only CD0 in level flight


def forces(V, alt, pitch_deg, flaps):
    rho = interp(DENSITY, alt); q = 0.5 * rho * V * V
    L = D = 0.0
    for name, (S, Sd, inc, inner) in PARTS.items():
        a_deg = pitch_deg + inc
        if flaps and inner: a_deg += FLAP_INNER
        if name == 'flaps':
            S = S * 2.0 if flaps else S
            if flaps: a_deg += FLAP_OWN
        a = math.radians(a_deg)
        L += interp(B52_CL, a) * q * S
        D += interp(B52_CD, a) * q * S + 0.25 * rho * V * V * Sd
    D += interp(B52_CD, 0) * q * TAIL_SIDE[0] + 0.25 * rho * V * V * TAIL_SIDE[1]
    return L, D


def thrust(V, alt):
    return STATIC * interp(F130_ALT, alt) * interp(F130_SPD, V)


def liftoff_speed(mass, flaps=True):
    for V in range(40, 200):
        L, _ = forces(V, 0, 0.0, flaps)
        if L >= mass * 9.81: return V
    return None


def trim(V, alt, mass, flaps=False):
    """Pitch attitude (deg) for lift = weight, by bisection."""
    lo, hi = -15.0, 20.0
    for _ in range(60):
        mid = (lo + hi) / 2
        if forces(V, alt, mid, flaps)[0] < mass * 9.81: lo = mid
        else: hi = mid
    return (lo + hi) / 2


def max_level_speed(alt, mass):
    best = None
    for V in range(60, 360, 2):
        p = trim(V, alt, mass)
        if p > 14: continue
        _, D = forces(V, alt, p, False)
        if thrust(V, alt) >= D: best = (V, p, D)
    return best


if __name__ == '__main__':
    empty, fuel, mk82 = 84200, 142280, 51 * 227
    for label, m in [('light (30% fuel, clean)', empty + 0.3 * fuel),
                     ('medium (60% fuel, 51 x Mk 82)', empty + 0.6 * fuel + mk82),
                     ('MTOW 221 t', 221350)]:
        v = liftoff_speed(m)
        acc = (thrust(v / 2, 0) - forces(v / 2, 0, 0, True)[1]) / m - 0.02 * 9.81
        print(f"{label:32s} {m/1000:6.1f} t  lift-off {v} m/s ({v*1.944:.0f} kt)  ~ground roll {v*v/(2*acc):.0f} m")
    m = empty + 0.5 * fuel
    print(f"\ncruise @ {m/1000:.0f} t")
    for alt in (0, 6000, 10700, 13000):
        r = max_level_speed(alt, m)
        if r:
            V, p, D = r
            Vc = 240 if alt >= 10000 else 200
            print(f"  {alt:6d} m  max level {V} m/s ({V*2.237:.0f} mph)  attitude {p:+.1f} deg | "
                  f"cruise {Vc} m/s attitude {trim(Vc, alt, m):+.1f} deg, L/D {forces(Vc, alt, trim(Vc, alt, m), False)[0]/forces(Vc, alt, trim(Vc, alt, m), False)[1]:.1f}")
        else:
            print(f"  {alt:6d} m  cannot hold level flight")
