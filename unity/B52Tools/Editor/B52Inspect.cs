using System.Linq;
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
        /// Renders the built prefab with gear doors open (as on the ground) from a few low angles to B52Doors_*.png.
        public static void RenderDoors()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Blueprinter/Mods/B52/B52.prefab");
            var go = (GameObject)Object.Instantiate(prefab);
            foreach (var t in go.GetComponentsInChildren<Transform>(true))
                if (t.name.StartsWith("gearDoor_") && !t.name.EndsWith("_panel"))
                    t.localEulerAngles = new Vector3(0, 0, t.name.EndsWith("R") ? -100f : 100f);
            var lightGo = new GameObject("sun"); var l = lightGo.AddComponent<Light>(); l.type = LightType.Directional; l.intensity = 1.3f;
            lightGo.transform.rotation = Quaternion.Euler(30, 40, 0);
            var camGo = new GameObject("cam"); var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.55f, 0.62f, 0.7f); cam.fieldOfView = 40;
            var rt = new RenderTexture(1280, 720, 24); cam.targetTexture = rt;
            var views = new (string, Vector3, Vector3)[] {
                ("front_trucks_side", new Vector3(9, -3.2f, 14.3f), new Vector3(0, -2.9f, 14.3f)),
                ("front_trucks_nose", new Vector3(0, -3.0f, 26f), new Vector3(0, -2.9f, 14.3f)),
                ("belly_low", new Vector3(14, -4.5f, 4f), new Vector3(0, -2.6f, 6f)) };
            foreach (var (n, pos, tgt) in views)
            {
                camGo.transform.position = pos; camGo.transform.LookAt(tgt);
                cam.Render(); RenderTexture.active = rt;
                var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); tex.Apply();
                System.IO.File.WriteAllBytes("B52Doors_" + n + ".png", tex.EncodeToPNG());
            }
            RenderTexture.active = null;
            Object.DestroyImmediate(go); Object.DestroyImmediate(camGo); Object.DestroyImmediate(lightGo);
        }
        /// Renders the built prefab from the cameras listed in B52Shots.txt (one per line:
        /// "name px py pz tx ty tz [fov] [hide1,hide2]", world metres, prefab at the origin) to B52Shot_name.png, and lists every
        /// renderer smaller than 1.5 m with its path and bounds in B52Smalls.log.
        public static void RenderShots()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Blueprinter/Mods/B52/B52.prefab");
            var go = (GameObject)Object.Instantiate(prefab);
            var sb = new StringBuilder();
            string PathOf(Transform t) => t.parent && t.parent != go.transform ? PathOf(t.parent) + "/" + t.name : t.name;
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                if (r.bounds.size.magnitude < 1.5f)
                    sb.AppendLine($"{PathOf(r.transform)}  c{r.bounds.center:F2} s{r.bounds.size:F2} {(r.enabled && r.gameObject.activeInHierarchy ? "" : "(hidden)")}");
            System.IO.File.WriteAllText("B52Smalls.log", sb.ToString());
            var lightGo = new GameObject("sun"); var l = lightGo.AddComponent<Light>(); l.type = LightType.Directional; l.intensity = 1.2f;
            lightGo.transform.rotation = Quaternion.Euler(45, 30, 0);
            var camGo = new GameObject("cam"); var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.6f, 0.68f, 0.76f); cam.nearClipPlane = 0.05f;
            var rt = new RenderTexture(1280, 720, 24); cam.targetTexture = rt;
            foreach (var line in System.IO.File.ReadAllLines("B52Shots.txt"))
            {
                var f = line.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
                if (f.Length < 7 || f[0].StartsWith("#")) continue;
                float F(int i) => float.Parse(f[i], System.Globalization.CultureInfo.InvariantCulture);
                cam.fieldOfView = f.Length > 7 ? F(7) : 35f;
                var hidden = new System.Collections.Generic.List<Renderer>();          // optional 9th field: names to hide
                if (f.Length > 8)
                    foreach (var r in go.GetComponentsInChildren<Renderer>())
                        foreach (var n in f[8].Split(','))
                            if (r.transform.name == n || PathOf(r.transform).Contains(n + "/")) { r.enabled = false; hidden.Add(r); }
                camGo.transform.position = new Vector3(F(1), F(2), F(3)); camGo.transform.LookAt(new Vector3(F(4), F(5), F(6)));
                cam.Render(); RenderTexture.active = rt;
                var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); tex.Apply();
                System.IO.File.WriteAllBytes("B52Shot_" + f[0] + ".png", tex.EncodeToPNG());
                foreach (var r in hidden) r.enabled = true;
            }
            RenderTexture.active = null;
            Object.DestroyImmediate(go); Object.DestroyImmediate(camGo); Object.DestroyImmediate(lightGo);
        }

        /// Poses every LandingGear the way LandingGear.MoveGear does at fold fraction f (hinge about local X by
        /// f * foldDegrees, hingeFoldMotion * f, strut swivel f * strutRotation; doors open unless stowed
        /// as GearSystem / LandingGear leave them), then renders the B52Shots.txt cameras for each f in
        /// B52GearFolds.txt (one number per line) to B52Fold_<f>_<shot>.png. Doors are open unless f = 1.
        public static void RenderGearFolds()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Blueprinter/Mods/B52/B52.prefab");
            var go = (GameObject)Object.Instantiate(prefab);
            var lgType = System.AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("LandingGear")).First(t => t != null);
            var gears = go.GetComponentsInChildren(lgType, true);
            var basePose = gears.Select(g =>
            {
                var so = new SerializedObject(g);
                var h = (Transform)so.FindProperty("gearHinge").objectReferenceValue;
                return (so, h, h.localEulerAngles, h.localPosition);
            }).ToList();
            var lightGo = new GameObject("sun"); var l = lightGo.AddComponent<Light>(); l.type = LightType.Directional; l.intensity = 1.2f;
            lightGo.transform.rotation = Quaternion.Euler(45, 30, 0);
            var camGo = new GameObject("cam"); var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.6f, 0.68f, 0.76f); cam.nearClipPlane = 0.05f;
            var rt = new RenderTexture(1280, 720, 24); cam.targetTexture = rt;
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            var foldLog = new StringBuilder();
            foreach (var fl in System.IO.File.ReadAllLines("B52GearFolds.txt"))
            {
                if (!float.TryParse(fl.Trim(), System.Globalization.NumberStyles.Float, inv, out float f)) continue;
                foreach (var (so, h, e0, p0) in basePose)
                {
                    float fold = so.FindProperty("foldDegrees").floatValue;
                    h.localEulerAngles = e0 + new Vector3(fold * f, 0f, 0f);
                    h.localPosition = p0 + so.FindProperty("hingeFoldMotion").vector3Value * f;
                    var sw = (Transform)so.FindProperty("strutRotationTransform").objectReferenceValue;
                    if (sw) sw.localEulerAngles = new Vector3(0f, so.FindProperty("strutRotation").floatValue * f, 0f);
                    var doors = so.FindProperty("gearDoors");
                    for (int i = 0; i < doors.arraySize; i++)
                    {
                        var d = doors.GetArrayElementAtIndex(i);
                        var t = (Transform)d.FindPropertyRelative("transform").objectReferenceValue;
                        bool open = f < 0.999f;                       // doors hang open whenever the gear is not stowed
                        if (t) t.localEulerAngles = open ? d.FindPropertyRelative("openAngle").vector3Value : Vector3.zero;
                    }
                }
                foreach (var (so, h, e0, p0) in basePose)
                {
                    var un = (GameObject)so.FindProperty("unsprung").objectReferenceValue;
                    foldLog.AppendLine($"f {f:F2} {h.name}: hinge {h.position:F2} unsprung {un.transform.position:F2}");
                    var ds = so.FindProperty("gearDoors");
                    for (int i = 0; i < ds.arraySize; i++)
                    {
                        var t = (Transform)ds.GetArrayElementAtIndex(i).FindPropertyRelative("transform").objectReferenceValue;
                        var r = t ? t.GetComponentInChildren<Renderer>() : null;
                        if (r) foldLog.AppendLine($"      door {t.name}: euler {t.localEulerAngles:F0} bounds c{r.bounds.center:F2} min{r.bounds.min:F2} max{r.bounds.max:F2}");
                    }
                }
                foreach (var line in System.IO.File.ReadAllLines("B52Shots.txt"))
                {
                    var s = line.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
                    if (s.Length < 7 || s[0].StartsWith("#")) continue;
                    float F(int i) => float.Parse(s[i], inv);
                    cam.fieldOfView = s.Length > 7 ? F(7) : 35f;
                    camGo.transform.position = new Vector3(F(1), F(2), F(3)); camGo.transform.LookAt(new Vector3(F(4), F(5), F(6)));
                    cam.Render(); RenderTexture.active = rt;
                    var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); tex.Apply();
                    System.IO.File.WriteAllBytes($"B52Fold_{f.ToString("F2", inv)}_{s[0]}.png", tex.EncodeToPNG());
                }
            }
            System.IO.File.WriteAllText("B52GearFolds.log", foldLog.ToString());
            RenderTexture.active = null;
            Object.DestroyImmediate(go); Object.DestroyImmediate(camGo); Object.DestroyImmediate(lightGo);
        }

        /// Renders the built prefab from four angles to B52Ext_*.png (atlas / livery check).
        public static void RenderExterior()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Blueprinter/Mods/B52/B52.prefab");
            var go = (GameObject)Object.Instantiate(prefab);
            var lightGo = new GameObject("sun"); var l = lightGo.AddComponent<Light>(); l.type = LightType.Directional; l.intensity = 1.2f;
            lightGo.transform.rotation = Quaternion.Euler(45, 30, 0);
            var camGo = new GameObject("cam"); var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.6f, 0.68f, 0.76f); cam.fieldOfView = 35;
            var rt = new RenderTexture(1600, 900, 24); cam.targetTexture = rt;
            foreach (var (n, pos) in new[] { ("side", new Vector3(75, 2, 3)), ("top", new Vector3(0, 95, 2)),
                                              ("front34", new Vector3(45, 18, 60)), ("rear34", new Vector3(-45, 15, -55)) })
            {
                camGo.transform.position = pos; camGo.transform.LookAt(new Vector3(0, 0, 3));
                cam.Render(); RenderTexture.active = rt;
                var tex = new Texture2D(1600, 900, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0); tex.Apply();
                System.IO.File.WriteAllBytes("B52Ext_" + n + ".png", tex.EncodeToPNG());
            }
            RenderTexture.active = null;
            Object.DestroyImmediate(go); Object.DestroyImmediate(camGo); Object.DestroyImmediate(lightGo);
        }
    }
}
