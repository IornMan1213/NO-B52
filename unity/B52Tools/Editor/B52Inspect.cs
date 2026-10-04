using System.Text;
using UnityEditor;
using UnityEngine;

namespace B52Tools
{
    /// Dumps the built prefab's hierarchy with world positions, rotations and renderer bounds to B52Inspect.log.
    public static class B52Inspect
    {
        public static void Dump()
        {
            var root = PrefabUtility.LoadPrefabContents("Assets/Blueprinter/Mods/B52/B52.prefab");
            var sb = new StringBuilder();
            void W(Transform t, int d)
            {
                var r = t.GetComponent<Renderer>();
                sb.Append(new string(' ', d * 2)).Append(t.name)
                  .Append($"  pos{t.position:F2} rot{t.eulerAngles:F0} ls{t.localScale:F2}");
                if (r) sb.Append($"  bounds c{r.bounds.center:F1} s{r.bounds.size:F1}");
                sb.AppendLine();
                if (d < 6) foreach (Transform c in t) W(c, d + 1);
            }
            W(root.transform, 0);
            System.IO.File.WriteAllText("B52Inspect.log", sb.ToString());
            PrefabUtility.UnloadPrefabContents(root);
        }
        /// For every ControlSurface in the template and in ours: which way the trailing edge moves for +1 input on each axis.
        public static void Surfaces()
        {
            var sb = new StringBuilder();
            foreach (var path in new[] { "Assets/Blueprinter/_donotship/GameObject/FastBomber1_PLACEHOLDER.prefab", "Assets/Blueprinter/Mods/B52/B52.prefab" })
            {
                var root = PrefabUtility.LoadPrefabContents(path);
                sb.AppendLine("== " + path);
                foreach (var mb in root.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (!mb || mb.GetType().Name != "ControlSurface") continue;
                    var so = new SerializedObject(mb);
                    var vis = so.FindProperty("visibleMesh").objectReferenceValue as GameObject;
                    if (!vis) { sb.AppendLine(mb.name + " no visible"); continue; }
                    var rs = vis.GetComponentsInChildren<Renderer>(true);
                    if (rs.Length == 0) { sb.AppendLine(mb.name + " no renderer"); continue; }
                    var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
                    var t = vis.transform;
                    var arm = b.center - t.position;
                    // trailing edge: the far aft point of the bounds along the arm
                    var te = new Vector3(b.center.x, b.center.y, b.min.z) - t.position;
                    Vector3 Move(float deg) => Quaternion.AngleAxis(deg, t.right) * te - te;
                    float pr = so.FindProperty("pitchRange").floatValue, rr = so.FindProperty("rollRange").floatValue, yr = so.FindProperty("yawRange").floatValue;
                    sb.AppendLine($"{mb.name,-14} axisX{t.right:F2} te{te:F1} pitch{pr} roll{rr} yaw{yr}  " +
                        $"TE move: +pitch{Move(pr * 0.5f):F2} +roll{Move(rr * 0.5f):F2} +yaw{Move(yr * 0.5f):F2}");
                }
                PrefabUtility.UnloadPrefabContents(root);
            }
            System.IO.File.WriteAllText("B52Surfaces.log", sb.ToString());
        }
        /// For every AeroPart: its lift frame's forward (must point roughly +Z into the airflow, or the part sees ~180 deg
        /// angle of attack and makes no lift), and its lift-normal direction.
        public static void LiftFrames()
        {
            var root = PrefabUtility.LoadPrefabContents("Assets/Blueprinter/Mods/B52/B52.prefab");
            var sb = new StringBuilder();
            foreach (var mb in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (!mb || mb.GetType().Name != "AeroPart") continue;
                var ln = new SerializedObject(mb).FindProperty("liftNormal").objectReferenceValue as Transform;
                var t = ln ? ln : mb.transform;
                float fz = Vector3.Dot(t.forward, Vector3.forward);
                sb.AppendLine($"{mb.name,-14} frame {t.name,-24} fwd{t.forward:F2} up{t.up:F2} right{t.right:F2}  {(fz < 0.5f ? "BAD FORWARD" : "ok")}");
            }
            System.IO.File.WriteAllText("B52LiftFrames.log", sb.ToString());
            PrefabUtility.UnloadPrefabContents(root);
        }
    }
}
