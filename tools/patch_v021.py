"""v0.2.1 patch: wing incidence, clean flaps/spoilers, realistic CG."""
import os
ROOT = os.path.join(os.path.dirname(__file__), '..')
p = os.path.join(ROOT, 'unity/B52Tools/Editor/B52Builder.cs')
s = open(p, encoding='utf-8').read()


def rep(a, b):
    global s
    assert a in s, a[:80]
    s = s.replace(a, b)


# 1. Wing incidence: the B-52 rotates very little on its quadricycle gear and lifts off nearly level because the
#    wing is set at ~6 deg incidence. Each wing part gets a lift-normal frame pitched 6 deg nose-up.
rep('''            // Fin and rudder: lift acts sideways''', '''            foreach (var w in new[] { "wingroot", "wing1", "wing2", "wingtip", "flap1", "flap2" })
                foreach (var sd in new[] { "_L", "_R" })
                {
                    var t = Find(ourRoot, w + sd); if (!t) continue;
                    var ln = Child(t, w + sd + "_liftNormal", t.position, ourRoot.rotation * Quaternion.Euler(-WingIncidence, 0, 0));
                    SetRef(t.GetComponent(T("AeroPart")), "liftNormal", ln);
                }
            // Fin and rudder: lift acts sideways''')
rep('''        static void WireCockpit()''', '''        const float WingIncidence = 6f;

        static void WireCockpit()''')

# 2. Flaps: keep the extra lift area but no visual sliding (it opened gaps in the wing).
rep('''                Set(hld, "movingParts", p =>
                {
                    p.arraySize = 1;''', '''                Set(hld, "movingParts", p =>
                {
                    p.arraySize = 0;
                    return;''')

# 3. Spoiler plates are invisible for now (aero only) - they read as loose panels on the wing.
rep('''                SetRef(cs, "visibleMesh", vis.gameObject);''', '''                SetRef(cs, "visibleMesh", vis.gameObject);
                if (name.StartsWith("spoilers")) { var vr2 = vis.GetComponent<MeshRenderer>(); if (vr2) vr2.enabled = false; }''')

# 4. CG at ~25% MAC (MAC ~7.5 m, its leading edge ~z 7.3) -> z ~5.5, slightly ahead for stability.
rep('''            if (com) com.position = Find(ourRoot, "wingroot_L").position * 0.5f + Find(ourRoot, "wingroot_R").position * 0.5f;''',
    '''            if (com) com.position = new Vector3(0f, 0.3f, 5.8f);''')

rep('public const string Version = "0.2.0";', 'public const string Version = "0.2.1";')
open(p, 'w', encoding='utf-8').write(s)
print('patched builder')
