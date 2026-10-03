"""v0.2.2 patch: B-52 airfoil, F130 thrust curves, flaps as camber (lift-normal rotation)."""
import os
ROOT = os.path.join(os.path.dirname(__file__), '..')
p = os.path.join(ROOT, 'unity/B52Tools/Editor/B52Builder.cs')
s = open(p, encoding='utf-8').read()


def rep(a, b):
    global s
    assert a in s, a[:80]
    s = s.replace(a, b)


# Curves shared by the builder (kept identical to tools/flightmodel_check.py).
rep('''        const float WingIncidence = 6f;''', '''        const float WingIncidence = 6f;
        const float FlapInnerCamber = 8f, FlapOwnCamber = 20f;

        static AnimationCurve Linear(params float[] kv)
        {
            var keys = new Keyframe[kv.Length / 2];
            for (int i = 0; i < keys.Length; i++) keys[i] = new Keyframe(kv[2 * i], kv[2 * i + 1]);
            var c = new AnimationCurve(keys);
            for (int i = 0; i < keys.Length; i++)
            {
                AnimationUtility.SetKeyLeftTangentMode(c, i, AnimationUtility.TangentMode.Linear);
                AnimationUtility.SetKeyRightTangentMode(c, i, AnimationUtility.TangentMode.Linear);
            }
            return c;
        }

        // High-aspect-ratio B-52 wing (AR 8.5): CL slope ~5/rad, CLmax ~1.45, induced drag rising with lift.
        static AnimationCurve B52Lift() => Linear(-3.14159f, 0, -1.57f, 0, -0.785f, -1.1f, -0.45f, -1.0f, -0.30f, -1.45f, -0.22f, -1.1f, 0, 0,
                                                  0.22f, 1.1f, 0.30f, 1.45f, 0.45f, 1.0f, 0.785f, 1.1f, 1.57f, 0, 3.14159f, 0);
        static AnimationCurve B52Drag() => Linear(-3f, 0, -1.49f, 0.6f, -0.30f, 0.13f, -0.20f, 0.06f, -0.10f, 0.022f, 0, 0.010f,
                                                  0.10f, 0.022f, 0.20f, 0.06f, 0.30f, 0.13f, 1.49f, 0.6f, 3f, 0);
        // Rolls-Royce F130 (high bypass): lapse with altitude and airspeed.
        static AnimationCurve F130Altitude() => Linear(0, 1.0f, 3000, 0.80f, 6000, 0.62f, 9000, 0.47f, 12000, 0.33f, 15000, 0.21f, 17500, 0.12f);
        static AnimationCurve F130Speed() => Linear(0, 1.0f, 50, 0.93f, 100, 0.86f, 150, 0.80f, 200, 0.74f, 250, 0.70f, 280, 0.66f, 300, 0.58f, 340, 0.30f);''')

# Engines get F130 curves.
rep('''                    SetF(tf, "staticThrust", 75600f);          // F130: ~17,000 lbf''', '''                    SetF(tf, "staticThrust", 75600f);          // F130: ~17,000 lbf
                    Set(tf, "altitudeThrust", q => q.animationCurveValue = F130Altitude());
                    Set(tf, "speedThrust", q => q.animationCurveValue = F130Speed());
                    SetF(tf, "spoolRate", 260f);                 // big high-bypass fans spool slowly''')

# Flaps: no visual travel; deploying rotates the lift frames (camber) of the flap and of the inner wing it sits on.
old_start = s.index('''                Set(hld, "movingParts", p =>''')
old_end = s.index('''                });''', old_start) + len('''                });''')
s = s[:old_start] + '''                var wingOf = f.StartsWith("flap1") ? "wingroot" + f.Substring(5) : "wing1" + f.Substring(5);
                var flapLn = Find(t, f + "_liftNormal");
                var wingLn = Find(Find(ourRoot, wingOf), wingOf + "_liftNormal");
                Set(hld, "movingParts", p =>
                {
                    var parts = new[] { (flapLn, FlapOwnCamber), (wingLn, FlapInnerCamber) }.Where(x => x.Item1).ToList();
                    p.arraySize = parts.Count;
                    for (int i = 0; i < parts.Count; i++)
                    {
                        var e = p.GetArrayElementAtIndex(i);
                        e.FindPropertyRelative("move").boolValue = false;
                        e.FindPropertyRelative("rotate").boolValue = true;
                        e.FindPropertyRelative("transform").objectReferenceValue = parts[i].Item1;
                        e.FindPropertyRelative("anglesRetracted").vector3Value = new Vector3(-WingIncidence, 0, 0);
                        e.FindPropertyRelative("anglesDeployed").vector3Value = new Vector3(-WingIncidence - parts[i].Item2, 0, 0);
                    }
                });''' + s[old_end:]

# Flap lift normals must exist before the flaps are wired: move the incidence block ahead of the flap loop.
inc_block_start = s.index('''            foreach (var w in new[] { "wingroot", "wing1", "wing2", "wingtip", "flap1", "flap2" })''')
inc_block_end = s.index('''            // Fin and rudder: lift acts sideways''')
inc_block = s[inc_block_start:inc_block_end]
s = s[:inc_block_start] + s[inc_block_end:]
rep('''            // Flaps: HighLiftDevice adds area when deployed''', inc_block + '''            // Flaps: HighLiftDevice adds area when deployed''')

# Airfoil + AI speeds in the parameters.
rep('''            ps.FindProperty("aircraftName").stringValue = "B52J";''', '''            ps.FindProperty("aircraftName").stringValue = "B52J";
            var af = ps.FindProperty("airfoils");
            for (int i = 0; i < af.arraySize; i++)
            {
                var a = af.GetArrayElementAtIndex(i);
                a.FindPropertyRelative("name").stringValue = "B52_wing";
                a.FindPropertyRelative("liftCoef").animationCurveValue = B52Lift();
                a.FindPropertyRelative("dragCoef").animationCurveValue = B52Drag();
            }''')
rep('''ps.FindProperty("takeoffSpeed").floatValue = 77f;''', '''ps.FindProperty("takeoffSpeed").floatValue = 90f;''')
rep('''ps.FindProperty("approachSpeed").floatValue = 75f;''', '''ps.FindProperty("approachSpeed").floatValue = 82f;''')
rep('''ps.FindProperty("landingSpeed").floatValue = 68f;''', '''ps.FindProperty("landingSpeed").floatValue = 72f;''')
rep('public const string Version = "0.2.1";', 'public const string Version = "0.2.2";')
open(p, 'w', encoding='utf-8').write(s)
print('patched')
