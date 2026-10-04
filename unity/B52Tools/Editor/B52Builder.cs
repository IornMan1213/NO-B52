using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace B52Tools
{
    /// <summary>
    /// Builds the B-52J aircraft prefab from B52.fbx, using the game's FastBomber1 as the component donor.
    /// Our FBX supplies the hierarchy and meshes. For every flight part we copy the tuned components from the
    /// matching FastBomber1 part, then overwrite the physical values with real B-52 numbers. Working sub-systems
    /// (pilots, cockpit logic, effects, lights) are moved across whole, and every leftover reference into the
    /// template is remapped or cleared.
    /// Run: Unity -batchmode -executeMethod B52Tools.B52Builder.Build
    /// </summary>
    public static class B52Builder
    {
        const string ModDir = "Assets/Blueprinter/Mods/B52";
        const string Fbx = ModDir + "/B52.fbx";
        const string Gen = ModDir + "/Generated";
        const string DoNotShip = "Assets/Blueprinter/_donotship";
        const string TemplatePrefab = DoNotShip + "/GameObject/FastBomber1_PLACEHOLDER.prefab";
        const string TemplateDef = DoNotShip + "/MonoBehaviour/FastBomber1_PLACEHOLDER.asset";
        const string TemplateParams = DoNotShip + "/MonoBehaviour/FastBomber1_Parameters_PLACEHOLDER.asset";

        static readonly StringBuilder Log = new StringBuilder();
        static readonly Dictionary<Object, Object> Remap = new Dictionary<Object, Object>();
        static Transform tmplRoot, ourRoot;

        static Type T(string name)
        {
            var t = Type.GetType(name + ", Assembly-CSharp");
            if (t == null) throw new Exception("Game type not found: " + name);
            return t;
        }

        // ------------------------------------------------------------------ physical data (real B-52H/J, SI units)
        // name: mass kg, wingArea m², dragArea m², fuel kg
        static readonly Dictionary<string, (float mass, float area, float drag, float fuel)> Phys =
            new Dictionary<string, (float, float, float, float)>
            {
                { "B52",        (9000, 36, 0.55f, 30000) },
                { "fuselage_F", (7000, 4, 0.45f, 20000) },
                { "cockpit",    (4000, 1, 0.30f, 0) },
                { "fuselage_R", (7500, 3, 0.35f, 10000) },
                { "tail",       (1800, 38, 0.06f, 0) },
                { "rudder",     (600, 12, 0, 0) },
                { "hstab_L",    (900, 30, 0.03f, 0) }, { "hstab_R", (900, 30, 0.03f, 0) },
                { "elevator_L", (500, 12, 0, 0) },   { "elevator_R", (500, 12, 0, 0) },
                { "wingroot_L", (5000, 58, 0.05f, 17000) }, { "wingroot_R", (5000, 58, 0.05f, 17000) },
                { "wing1_L",    (4000, 42, 0.04f, 14000) }, { "wing1_R", (4000, 42, 0.04f, 14000) },
                { "wing2_L",    (2500, 27, 0.03f, 8000) },  { "wing2_R", (2500, 27, 0.03f, 8000) },
                { "wingtip_L",  (1200, 12, 0.12f, 2140) },  { "wingtip_R", (1200, 12, 0.12f, 2140) },
                { "flap1_L",    (800, 10, 0, 0) }, { "flap1_R", (800, 10, 0, 0) },
                { "flap2_L",    (900, 11, 0, 0) }, { "flap2_R", (900, 11, 0, 0) },
                { "spoilers_L", (700, 4, 0, 0) },  { "spoilers_R", (700, 4, 0, 0) },
                { "pod1_L",     (4600, 0, 0.35f, 0) }, { "pod1_R", (4600, 0, 0.35f, 0) },
                { "pod2_L",     (4600, 0, 0.35f, 0) }, { "pod2_R", (4600, 0, 0.35f, 0) },
            };

        // our part -> (template donor part, component types to copy)
        static readonly (string ours, string donor, string[] comps)[] Parts =
        {
            ("fuselage_F", "fuselage_F", new[] { "AeroPart", "FuelTank" }),
            ("cockpit", "cockpit", new[] { "AeroPart", "AutopilotPlane", "ControlsFilter", "WeaponManager" }),
            ("fuselage_R", "fuselage_R", new[] { "AeroPart", "FuelTank" }),
            ("tail", "tail", new[] { "AeroPart" }),
            ("rudder", "rudder_L", new[] { "AeroPart", "ControlSurface" }),
            ("hstab_L", "nozzles_L", new[] { "AeroPart" }), ("hstab_R", "nozzles_R", new[] { "AeroPart" }),
            ("elevator_L", "elevator_L", new[] { "AeroPart", "ControlSurface" }),
            ("elevator_R", "elevator_R", new[] { "AeroPart", "ControlSurface" }),
            ("wingroot_L", "wingroot_L", new[] { "AeroPart", "FuelTank" }), ("wingroot_R", "wingroot_R", new[] { "AeroPart", "FuelTank" }),
            ("wing1_L", "wing1_L", new[] { "AeroPart", "FuelTank" }), ("wing1_R", "wing1_R", new[] { "AeroPart", "FuelTank" }),
            ("wing2_L", "wing2_L", new[] { "AeroPart", "FuelTank" }), ("wing2_R", "wing2_R", new[] { "AeroPart", "FuelTank" }),
            ("wingtip_L", "wingtip_L", new[] { "AeroPart" }), ("wingtip_R", "wingtip_R", new[] { "AeroPart" }),
            ("flap2_L", "flap_L", new[] { "AeroPart", "HighLiftDevice" }), ("flap2_R", "flap_R", new[] { "AeroPart", "HighLiftDevice" }),
            ("flap1_L", "flap_L", new[] { "AeroPart", "HighLiftDevice" }), ("flap1_R", "flap_R", new[] { "AeroPart", "HighLiftDevice" }),
            ("spoilers_L", "aileron_L", new[] { "AeroPart", "ControlSurface" }),
            ("spoilers_R", "aileron_R", new[] { "AeroPart", "ControlSurface" }),
            ("pod1_L", "engines_L", new[] { "AeroPart" }), ("pod1_R", "engines_R", new[] { "AeroPart" }),
            ("pod2_L", "engines_L", new[] { "AeroPart" }), ("pod2_R", "engines_R", new[] { "AeroPart" }),
        };

        // Template parts with no B-52 counterpart: references to them are redirected here.
        static readonly Dictionary<string, string> Fold = new Dictionary<string, string>
        {
            { "fuselage_FF", "fuselage_F" }, { "nose", "cockpit" }, { "cheek_FL", "fuselage_F" }, { "cheek_FR", "fuselage_F" },
            { "cheek_RL", "fuselage_R" }, { "cheek_RR", "fuselage_R" }, { "intake_L", "B52" }, { "intake_R", "B52" },
            { "gearbay_L", "B52" }, { "gearbay_R", "B52" }, { "aileron_L", "spoilers_L" }, { "aileron_R", "spoilers_R" },
            { "rudder_R", "rudder" }, { "engines_L", "pod1_L" }, { "engines_R", "pod1_R" },
        };

        public static void Build()
        {
            Log.Clear(); Remap.Clear();
            try
            {
                ConfigureFbx();
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var tmpl = Spawn(TemplatePrefab); tmplRoot = tmpl.transform;
                var ours = Spawn(Fbx);
                ourRoot = ours.transform.Find("B52") ?? ours.transform;
                if (ourRoot != ours.transform) { ourRoot.SetParent(null, true); Object.DestroyImmediate(ours); }
                ourRoot.name = "B52";
                if (!AssetDatabase.IsValidFolder(Gen)) AssetDatabase.CreateFolder(ModDir, "Generated");
                NormalizeFrames();

                CopyRoot();
                foreach (var p in Parts) CopyPart(p.ours, p.donor, p.comps);
                foreach (var kv in Fold) MapGO(Find(tmplRoot, kv.Key), Find(ourRoot, kv.Value));
                MoveSubsystems();
                BuildEngines();
                BuildGear();
                BuildBayDoors();
                WireControlSurfaces();
                WireCockpit();
                ApplyPhysics();
                WireJoints();
                AddColliders();
                RemapReferences();
                FixLeftovers();
                ClearLiveryTargets();
                Object.DestroyImmediate(tmplRoot.gameObject);

                ConvertMaterials();
                var prefabPath = ModDir + "/B52.prefab";
                var prefab = PrefabUtility.SaveAsPrefabAsset(ourRoot.gameObject, prefabPath);
                Note("Saved " + prefabPath);
                MakeDefinition(prefab);
                MakeOps();
                AssetDatabase.SaveAssets();
            }
            catch (Exception e)
            {
                Note("FAILED: " + e);
                throw;
            }
            finally
            {
                System.IO.File.WriteAllText("B52Build.log", Log.ToString());
                Debug.Log("[B52] build log:\n" + Log);
            }
        }

        // ------------------------------------------------------------------ helpers
        static void Note(string s) => Log.AppendLine(s);

        static GameObject Spawn(string path)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path) ?? throw new Exception("Missing " + path);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(asset);
            PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            return go;
        }

        static Transform Find(Transform root, string name)
        {
            if (root.name == name) return root;
            foreach (Transform c in root) { var f = Find(c, name); if (f) return f; }
            return null;
        }

        static void MapGO(Transform from, Transform to)
        {
            if (!from || !to) return;
            if (!Remap.ContainsKey(from.gameObject)) Remap[from.gameObject] = to.gameObject;
            if (!Remap.ContainsKey(from)) Remap[from] = to;
        }

        static Component CopyComponent(Component src, GameObject dst)
        {
            var c = dst.AddComponent(src.GetType());
            if (c == null) { Note($"  ! could not add {src.GetType().Name} to {dst.name}"); return null; }
            EditorUtility.CopySerialized(src, c);
            if (!Remap.ContainsKey(src)) Remap[src] = c;
            return c;
        }

        static SerializedObject SO(Object o) => new SerializedObject(o);

        static void Set(Object o, string prop, Action<SerializedProperty> act)
        {
            var so = SO(o); var p = so.FindProperty(prop);
            if (p == null) { Note($"  ! {o.GetType().Name}.{prop} not found"); return; }
            act(p); so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void SetF(Object o, string prop, float v) => Set(o, prop, p => p.floatValue = v);
        static void SetRef(Object o, string prop, Object v) => Set(o, prop, p => p.objectReferenceValue = v);

        static void SetRefArray(Object o, string prop, IList<Object> vals)
        {
            Set(o, prop, p =>
            {
                p.arraySize = vals.Count;
                for (int i = 0; i < vals.Count; i++) p.GetArrayElementAtIndex(i).objectReferenceValue = vals[i];
            });
        }

        static Transform Child(Transform parent, string name, Vector3 worldPos, Quaternion? worldRot = null)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = worldPos;
            go.transform.rotation = worldRot ?? parent.rotation;
            return go.transform;
        }

        // ------------------------------------------------------------------ steps
        static void ConfigureFbx()
        {
            var imp = (ModelImporter)AssetImporter.GetAtPath(Fbx) ?? throw new Exception("FBX not imported: " + Fbx);
            imp.isReadable = true;
            imp.importAnimation = false;
            imp.animationType = ModelImporterAnimationType.None;
            imp.importCameras = false; imp.importLights = false;
            imp.useFileScale = true; imp.globalScale = 1f;
            imp.bakeAxisConversion = true;
            imp.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            imp.SaveAndReimport();
        }

        /// <summary>The FBX nodes arrive with a +90 deg X axis-conversion rotation on every frame. Bake it into
        /// copies of the meshes so each part's local axes are Unity-native (Y up, Z forward; control surfaces keep
        /// local X along their hinge).</summary>
        static void NormalizeFrames()
        {
            var all = ourRoot.GetComponentsInChildren<Transform>(true);
            var pos = all.ToDictionary(t => t, t => t.position);
            var rot = all.ToDictionary(t => t, t => t.rotation);
            var fix = Quaternion.Euler(-90f, 0f, 0f);
            var meshFix = Matrix4x4.Rotate(Quaternion.Euler(90f, 0f, 0f));
            if (!AssetDatabase.IsValidFolder(Gen + "/Meshes")) AssetDatabase.CreateFolder(Gen, "Meshes");
            foreach (var t in all)            // GetComponentsInChildren is parent-first
            {
                t.SetPositionAndRotation(pos[t], rot[t] * fix);
                var mf = t.GetComponent<MeshFilter>();
                if (!mf || !mf.sharedMesh) continue;
                var m = Object.Instantiate(mf.sharedMesh);
                m.name = t.name;
                var v = m.vertices; var n = m.normals; var tg = m.tangents;
                for (int i = 0; i < v.Length; i++) v[i] = meshFix.MultiplyPoint3x4(v[i]);
                for (int i = 0; i < n.Length; i++) n[i] = meshFix.MultiplyVector(n[i]);
                for (int i = 0; i < tg.Length; i++) { var d = meshFix.MultiplyVector(tg[i]); tg[i] = new Vector4(d.x, d.y, d.z, tg[i].w); }
                m.vertices = v; m.normals = n; m.tangents = tg; m.RecalculateBounds();
                var path = Gen + "/Meshes/" + t.name + ".asset";
                AssetDatabase.DeleteAsset(path);
                AssetDatabase.CreateAsset(m, path);
                mf.sharedMesh = m;
            }
            Note("Normalized " + all.Length + " frames");
            // Rudder: the hinge is the swept leading edge running up the fin (local X), lift normal sideways (local Y).
            // A vertical axis made the top of the rudder, metres aft of it, swing sideways like a wiper.
            var rud = Find(ourRoot, "rudder");
            if (rud && rud.GetComponent<MeshFilter>())
            {
                var w = rud.GetComponent<MeshFilter>().sharedMesh.vertices.Select(v => rud.TransformPoint(v)).ToList();
                float y0 = w.Min(v => v.y), y1 = w.Max(v => v.y), band = (y1 - y0) * 0.12f;
                Vector3 Lead(IEnumerable<Vector3> vs) => vs.OrderByDescending(v => v.z).First();
                var bot = Lead(w.Where(v => v.y < y0 + band)); var top = Lead(w.Where(v => v.y > y1 - band));
                var hingeAxis = (top - bot).normalized;
                var side = Vector3.ProjectOnPlane(Vector3.right, hingeAxis).normalized;
                var rudRot = Quaternion.LookRotation(Vector3.Cross(hingeAxis, side), side);
                ReFrame(rud, rudRot, (bot + top) * 0.5f);
                Note($"Rudder hinge {bot:F2} -> {top:F2}, sweep {Vector3.Angle(hingeAxis, Vector3.up):F0} deg");
            }
        }

        /// <summary>Give t a new world rotation without moving its mesh or children in world space.</summary>
        static void ReFrame(Transform t, Quaternion newRot, Vector3? newPos = null)
        {
            if (!t) return;
            var kids = t.Cast<Transform>().Select(c => (c, c.position, c.rotation)).ToList();
            var mf = t.GetComponent<MeshFilter>();
            var pos = newPos ?? t.position;
            // vertex: old local -> world -> new local
            var delta = Matrix4x4.TRS(pos, newRot, Vector3.one).inverse * Matrix4x4.TRS(t.position, t.rotation, Vector3.one);
            if (mf && mf.sharedMesh)
            {
                var m = mf.sharedMesh;
                var v = m.vertices; var n = m.normals;
                for (int i = 0; i < v.Length; i++) v[i] = delta.MultiplyPoint3x4(v[i]);
                for (int i = 0; i < n.Length; i++) n[i] = delta.MultiplyVector(n[i]);
                m.vertices = v; m.normals = n; m.RecalculateTangents(); m.RecalculateBounds();
                EditorUtility.SetDirty(m);
            }
            t.SetPositionAndRotation(pos, newRot);
            foreach (var (c, p, r) in kids) c.SetPositionAndRotation(p, r);
        }

        static void CopyRoot()
        {
            MapGO(tmplRoot, ourRoot);
            foreach (var c in tmplRoot.GetComponents<Component>())
            {
                if (c is Transform) continue;
                var tn = c.GetType().Name;
                if (tn == "SwingWingController") continue;
                CopyComponent(c, ourRoot.gameObject);
            }
            Note("Root components: " + string.Join(", ", ourRoot.GetComponents<Component>().Select(c => c.GetType().Name)));
        }

        static void CopyPart(string ours, string donor, string[] comps)
        {
            var o = Find(ourRoot, ours); var d = Find(tmplRoot, donor);
            if (!o) { Note($"  ! our part missing: {ours}"); return; }
            if (!d) { Note($"  ! donor missing: {donor}"); return; }
            MapGO(d, o);
            foreach (var tn in comps)
            {
                var src = d.GetComponent(T(tn));
                if (src == null) { Note($"  ! {donor} has no {tn}"); continue; }
                CopyComponent(src, o.gameObject);
            }
        }

        static void MoveSubsystems()
        {
            // Root-level effect objects
            foreach (var n in new[] { "vaporCone", "CoM", "contactSparks", "downwash", "aimTarget", "weaponBay_forward", "weaponBay_combined" })
                Move(n, ourRoot);
            var cockpit = Find(ourRoot, "cockpit");
            foreach (var n in new[] { "WSO", "Pilot" })
                Move(n, cockpit);
            Move("targetCamPoint_F", cockpit);
            var tail = Find(ourRoot, "tail");
            Move("targetCamPoint_R", tail); Move("landingCam", tail);
            foreach (var s in new[] { "L", "R" })
            {
                var tip = Find(ourRoot, "wingtip_" + s);
                Move("navlight_" + s, tip); Move("wingtipvortex_" + s, tip);
            }
            // Nose sensors live on the cockpit part in the B-52 (one AeroPart covers the nose).
            var nose = Find(tmplRoot, "nose");
            var radarHost = Child(cockpit, "nose_sensors", Find(ourRoot, "cockpit").position + cockpit.forward * 3.5f);
            foreach (var tn in new[] { "Radar", "TargetCam", "RadarLocator" })
            {
                var src = nose.GetComponent(T(tn));
                if (src) CopyComponent(src, radarHost.gameObject);
            }
            MapGO(nose, radarHost);

            // Place the crew at the pilot / copilot eye points (Pilot = left seat, WSO = right seat).
            PlaceAt("Pilot", "eye_L"); PlaceAt("WSO", "eye_R");
            // Put the CoM at ~25% MAC (between the forward and aft main gear, nearer the wing box).
            var com = Find(ourRoot, "CoM");
            if (com) com.position = new Vector3(0f, 0.3f, 5.8f);
        }

        static IEnumerable<Transform> FindAll(Transform root, string name)
        {
            var list = new List<Transform>();
            void W(Transform t) { if (t.name == name) list.Add(t); foreach (Transform c in t) W(c); }
            W(root); return list;
        }

        static void Move(string name, Transform newParent)
        {
            var t = Find(tmplRoot, name);
            if (!t) { Note($"  ! subsystem missing: {name}"); return; }
            t.SetParent(newParent, true);
        }

        static void PlaceAt(string crew, string eye)
        {
            var c = Find(ourRoot, crew); var e = Find(ourRoot, eye);
            if (!c || !e) { Note($"  ! cannot place {crew} at {eye}"); return; }
            // The pilot model's helmet camera point sits ~0.75 m above the Pilot transform.
            var head = Find(c, crew == "Pilot" ? "helmetCamPoint" : "helmetCamPoint2");
            var offset = head ? head.position - c.position : new Vector3(0, 0.75f, 0);
            c.position = e.position - offset;
            c.rotation = ourRoot.rotation;
        }

        static void BuildEngines()
        {
            // Eight F130s, two per pod. Template engine1..4 are moved and cloned to 8.
            var srcEngine = Find(tmplRoot, "engine1");
            var srcNozzle = Find(tmplRoot, "nozzle1").gameObject;
            int n = 1;
            foreach (var pod in new[] { "pod2_L", "pod1_L", "pod1_R", "pod2_R" })
            {
                var p = Find(ourRoot, pod);
                var r = p.GetComponentInChildren<Renderer>();
                var b = r ? r.bounds : new Bounds(p.position, Vector3.one);
                for (int k = 0; k < 2; k++)
                {
                    bool inboard = pod.StartsWith("pod1");
                    float sgn = pod.EndsWith("_L") ? -1f : 1f;
                    float ex = sgn * (inboard ? (k == 0 ? 10.93f : 12.21f) : (k == 0 ? 18.08f : 19.39f));
                    var go = Object.Instantiate(srcEngine.gameObject, p);
                    go.name = "engine" + n;
                    go.transform.position = new Vector3(ex, inboard ? -0.6f : -0.2f, inboard ? 11.7f : 6.35f);
                    go.transform.rotation = ourRoot.rotation;
                    var tf = go.GetComponent(T("Turbofan"));
                    SetF(tf, "staticThrust", 75600f);          // F130: ~17,000 lbf
                    Set(tf, "altitudeThrust", q => q.animationCurveValue = F130Altitude());
                    Set(tf, "speedThrust", q => q.animationCurveValue = F130Speed());
                    SetF(tf, "spoolRate", 260f);                 // big high-bypass fans spool slowly
                    // Exhaust nozzle at the back of the engine: drives the IR signature and heat haze (no afterburner).
                    var nz = Object.Instantiate(srcNozzle, p);
                    nz.name = "nozzle" + n;
                    nz.transform.position = go.transform.position - ourRoot.forward * 2.6f;
                    nz.transform.rotation = srcNozzle.transform.rotation;
                    foreach (Transform c in nz.transform.Cast<Transform>().ToList())
                        if (c.name.StartsWith("afterburner")) Object.DestroyImmediate(c.gameObject);
                    var jn = nz.GetComponent(T("JetNozzle"));
                    Set(jn, "afterburners", q => q.arraySize = 0);
                    SetRef(jn, "part", p.GetComponent(T("UnitPart")));
                    SetRef(jn, "engine", go);
                    SetRef(jn, "turbojet", null);
                    SetRef(jn, "failureEffect", null);
                    SetRefArray(jn, "vectorTransforms", new Object[0]);
                    SetRefArray(tf, "nozzles", new Object[] { jn });
                    Set(tf, "criticalParts", q => q.arraySize = 0);
                    SetRefArray(tf, "vectoringTransforms", new Object[0]);
                    SetF(tf, "fuelConsumptionMin", 0.08f * 0.7f);
                    SetF(tf, "fuelConsumptionMax", 0.95f * 0.7f);   // CERP: ~30% better than TF33
                    var part = p.GetComponent(T("UnitPart"));
                    SetRef(tf, "part", part);
                    var cap = go.GetComponent<CapsuleCollider>();
                    if (cap) { cap.direction = 2; cap.radius = 0.75f; cap.height = 4.5f; cap.center = Vector3.zero; }
                    n++;
                }
            }
            Note("Engines: " + (n - 1) + " x Turbofan 75.6 kN");
        }

        static void BuildGear()
        {
            var mainDonor = Find(tmplRoot, "gear_L");
            var noseDonor = Find(tmplRoot, "gear_F");
            foreach (var k in new[] { "FL", "FR", "RL", "RR", "OL", "OR" })
            {
                var strut = Find(ourRoot, "gear_" + k);
                if (!strut) { Note("  ! gear missing " + k); continue; }
                bool outrigger = k[0] == 'O';
                var parent = strut.parent;
                var unsprung = Find(strut, "gear_unsprung_" + k);
                var wheels = unsprung.Cast<Transform>().ToList();
                float wheelR = outrigger ? 0.405f : 0.71f;

                var hinge = Child(parent, "gearHinge_" + k, strut.position);
                var gear = new GameObject("gear_" + k + "_sprung").transform;
                gear.SetParent(hinge, false); gear.localPosition = Vector3.zero; gear.localRotation = Quaternion.identity;
                strut.SetParent(gear, true);
                unsprung.SetParent(gear, true);
                var bump = Child(gear, "bumpstop_" + k, strut.position);
                var axle = Child(unsprung, "axle_" + k, unsprung.position);

                var donor = outrigger ? noseDonor : mainDonor;
                var lg = CopyComponent(donor.GetComponent(T("LandingGear")), gear.gameObject);
                CloneDonorRefs(lg, donor, unsprung);   // tyre audio, dust, skid effect, fold sounds
                var box = gear.gameObject.AddComponent<BoxCollider>();
                box.size = new Vector3(outrigger ? 0.3f : 1.0f, 0.4f, outrigger ? 0.85f : 1.5f);
                box.center = gear.InverseTransformPoint(unsprung.position);
                box.enabled = false;   // only enabled by LandingGear.BreakWheel; enabled it would block the gear's own ground ray

                float travel = (bump.position - unsprung.position).magnitude + wheelR;
                SetRef(lg, "attachedPart", parent.GetComponentInParent(T("AeroPart")));
                SetRef(lg, "gearCollider", box);
                SetRef(lg, "bumpStop", bump.gameObject);
                SetRef(lg, "unsprung", unsprung.gameObject);
                SetRef(lg, "castPoint", bump);
                SetRef(lg, "axle", axle);
                SetRef(lg, "gearHinge", hinge);
                SetRef(lg, "strutRotationTransform", unsprung);
                SetRefArray(lg, "wheels", wheels.Cast<Object>().ToList());
                SetRefArray(lg, "movingParts", new Object[0]);
                SetRefArray(lg, "joints", new Object[0]);
                Set(lg, "gearDoors", p => p.arraySize = 0);
                SetF(lg, "wheelRadius", wheelR);
                SetF(lg, "suspensionTravel", travel);
                // B-52: ~60 t per main truck at MTOW. The trucks are only 2.5 m apart, so the outriggers hold the
                // wings level: stiff enough to stop the roll, with a long stroke so they don't snap off.
                SetF(lg, "maxCompression", outrigger ? 1.2f : 0.65f);
                SetF(lg, "springRate", outrigger ? 1500000f : 3200000f);
                SetF(lg, "dampingRate", outrigger ? 300000f : 750000f);
                SetF(lg, "mass", outrigger ? 300f : 1500f);
                SetF(lg, "foldDegrees", outrigger ? 90f : 90f);
                // Mains fold forward and rise into the belly; outriggers fold flat under the wingtip.
                Set(lg, "hingeFoldMotion", q => q.vector3Value = outrigger ? Vector3.zero : new Vector3(0f, 1.2f, 0f));
                Set(lg, "steering", p => p.boolValue = k[0] == 'F');   // the forward trucks steer
                Set(lg, "braked", p => p.boolValue = !outrigger);
                SetF(lg, "steeringLock", 25f);
                SetF(lg, "steeringSpeed", 30f);                 // the main-gear donor has 0, which locks the trucks
                SetF(lg, "aligningStrength", 2f);
                SetF(lg, "differentialBrakeFactor", 0f);        // all trucks are on the centreline; the donor's value braked every truck on any rudder input
                Note($"Gear {k}: travel {travel:F2} m, wheel r {wheelR}");
            }
        }

        static void BuildBayDoors()
        {
            var donor = Find(tmplRoot, "bayDoorHinge_RL2").GetComponent(T("BayDoor"));
            foreach (var s in new[] { "L", "R" })
            {
                var door = Find(ourRoot, "bayDoor_" + s);
                if (!door) continue;
                var parent = door.parent;
                var h1 = Child(parent, "bayDoorHinge_" + s + "1", door.position);
                var h2 = Child(h1, "bayDoorHinge_" + s + "2", door.position);
                door.SetParent(h2, true);
                var bd = CopyComponent(donor, h2.gameObject);
                CloneDonorRefs(bd, donor.transform.parent, h2);     // door motor sound
                // Doors hinge along the fuselage (local Z) and swing up into the bay.
                SetF(bd, "hingeAngle", s == "L" ? 95f : -95f);
            }
        }

        static void WireControlSurfaces()
        {
            // Each control surface rotates its visible mesh about local X. Our FBX gives each surface its own
            // hinge-aligned transform, so a child "visible" pivot is inserted and the mesh moved under it.
            foreach (var (name, pitch, roll, yaw) in new[]
            {
                ("elevator_L", -20f, 0f, 0f), ("elevator_R", -20f, 0f, 0f),   // +pitch input moves the trailing edge down, like the donor
                ("rudder", 0f, 0f, -25f),
                ("spoilers_L", 0f, -45f, 0f), ("spoilers_R", 0f, 45f, 0f),
            })
            {
                var t = Find(ourRoot, name);
                var cs = t.GetComponent(T("ControlSurface"));
                if (!cs) continue;
                var vis = Child(t, name + "_visible", t.position, t.rotation);
                var mf = t.GetComponent<MeshFilter>(); var mr = t.GetComponent<MeshRenderer>();
                if (mf)
                {
                    var vf = vis.gameObject.AddComponent<MeshFilter>(); vf.sharedMesh = mf.sharedMesh;
                    var vr = vis.gameObject.AddComponent<MeshRenderer>(); vr.sharedMaterials = mr.sharedMaterials;
                    Object.DestroyImmediate(mr); Object.DestroyImmediate(mf);
                }
                SetRef(cs, "visibleMesh", vis.gameObject);
                if (name.StartsWith("spoilers"))
                {
                    // The aero surface stays hidden (it swings both ways); B52Systems.SpoilerDriver raises this
                    // visible copy only on the wing whose spoilers go up.
                    var vr2 = vis.GetComponent<MeshRenderer>();
                    var panel = Child(t, "spoilerPanels_" + name.Substring(name.Length - 1), vis.position, vis.rotation);
                    panel.gameObject.AddComponent<MeshFilter>().sharedMesh = vis.GetComponent<MeshFilter>().sharedMesh;
                    var pr2 = panel.gameObject.AddComponent<MeshRenderer>(); pr2.sharedMaterials = vr2.sharedMaterials; pr2.enabled = false;
                    if (vr2) vr2.enabled = false;
                }
                SetRef(cs, "attachedSurface", t.GetComponent(T("UnitPart")));
                SetF(cs, "pitchRange", pitch); SetF(cs, "rollRange", roll); SetF(cs, "yawRange", yaw);
                SetF(cs, "brakeRange", 0); SetF(cs, "maxSplit", 0);
                SetF(cs, "servoSpeed", name.StartsWith("spoiler") ? 60f : 30f);
                SetRef(t.GetComponent(T("AeroPart")), "liftNormal", vis);
            }
            // Fin and rudder: lift acts sideways, so the fin needs a lift normal rotated onto the Y-Z plane.
            var tail = Find(ourRoot, "tail");
            var ln = Child(tail, "tail_liftNormal", tail.position, ourRoot.rotation * Quaternion.Euler(0, 0, 90));
            SetRef(tail.GetComponent(T("AeroPart")), "liftNormal", ln);
            foreach (var w in new[] { "wingroot", "wing1", "wing2", "wingtip", "flap1", "flap2" })
                foreach (var sd in new[] { "_L", "_R" })
                {
                    var t = Find(ourRoot, w + sd); if (!t) continue;
                    var wln = Child(t, w + sd + "_liftNormal", t.position, ourRoot.rotation * Quaternion.Euler(-WingIncidence, 0, 0));
                    SetRef(t.GetComponent(T("AeroPart")), "liftNormal", wln);
                }
            // Flaps: HighLiftDevice adds area when deployed and animates a visible child (Fowler: back and down).
            foreach (var f in new[] { "flap1_L", "flap1_R", "flap2_L", "flap2_R" })
            {
                var t = Find(ourRoot, f); var hld = t.GetComponent(T("HighLiftDevice"));
                if (!hld) continue;
                var vis = Child(t, f + "_visible", t.position, t.rotation);
                var mf = t.GetComponent<MeshFilter>(); var mr = t.GetComponent<MeshRenderer>();
                if (mf)
                {
                    vis.gameObject.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
                    vis.gameObject.AddComponent<MeshRenderer>().sharedMaterials = mr.sharedMaterials;
                    Object.DestroyImmediate(mr); Object.DestroyImmediate(mf);
                }
                SetRef(hld, "aeroPart", t.GetComponent(T("AeroPart")));
                SetRef(hld, "swingWingController", null);
                SetF(hld, "deployedArea", Phys[f].area * 1.0f);     // Fowler flaps slide aft: about double the area
                SetF(hld, "speedDeployed", 105f);                    // fully down below 204 kt (heavy lift-off ~180 kt)
                SetF(hld, "speedRetracted", 130f);                   // fully up by 253 kt
                var wingOf = f.StartsWith("flap1") ? "wingroot" + f.Substring(5) : "wing1" + f.Substring(5);
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
                });
            }
        }

        const float WingIncidence = 6f;
        const float FlapInnerCamber = 10f, FlapOwnCamber = 10f;   // 6 + 10 = 16 deg, just under the 17 deg CLmax

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
        static AnimationCurve F130Speed() => Linear(0, 1.0f, 50, 0.93f, 100, 0.86f, 150, 0.80f, 200, 0.74f, 250, 0.70f, 280, 0.66f, 300, 0.58f, 340, 0.30f);

        static void WireCockpit()
        {
            var ci = Find(ourRoot, "cockpit_int");
            var src = Find(tmplRoot, "cockpit_int").GetComponent(T("Cockpit"));
            var ck = CopyComponent(src, ci.gameObject);
            MapGO(Find(tmplRoot, "cockpit_int"), ci);
            Set(ck, "joysticks", p =>
            {
                var yokes = new[] { "yoke_L", "yoke_R" };
                p.arraySize = yokes.Length;
                for (int i = 0; i < yokes.Length; i++)
                {
                    var e = p.GetArrayElementAtIndex(i);
                    e.FindPropertyRelative("transform").objectReferenceValue = Find(ourRoot, yokes[i]);
                    e.FindPropertyRelative("range").floatValue = 10f;
                }
            });
            Set(ck, "throttles", p =>
            {
                p.arraySize = 8;
                for (int i = 0; i < 8; i++)
                {
                    var e = p.GetArrayElementAtIndex(i);
                    e.FindPropertyRelative("transform").objectReferenceValue = Find(ourRoot, "throttle_" + (i + 1));
                    e.FindPropertyRelative("rotation").boolValue = true;
                    e.FindPropertyRelative("motion").boolValue = false;
                    e.FindPropertyRelative("range").floatValue = 45f;     // + tips the lever forward (stock: +54)
                }
            });
            var scr = Find(ourRoot, "mfd_C_screen");
            if (scr) SetRef(ck, "tacScreenRender", scr.GetComponent<Renderer>());
            // Template tacScreen object (UI anchor) goes onto our centre display.
            var tac = Find(tmplRoot, "tacScreen");
            if (tac && scr) { tac.SetParent(ci, true); tac.position = scr.position; tac.rotation = scr.rotation; }

            // Aircraft renderer lists: interior vs exterior (cockpit view culls the exterior nose skin).
            var ac = ourRoot.GetComponent(T("Aircraft"));
            var interior = ci.GetComponentsInChildren<Renderer>(true).Cast<Object>().ToList();
            var exterior = ourRoot.GetComponentsInChildren<Renderer>(true).Where(r => !r.transform.IsChildOf(ci)
                && !(r is SkinnedMeshRenderer) && !(r is ParticleSystemRenderer)).Cast<Object>().ToList();
            SetRefArray(ac, "cockpitRenderers", interior);
            SetRefArray(ac, "exteriorRenderers", exterior);
            SetRef(ac, "cockpit", Find(ourRoot, "cockpit").GetComponent(T("UnitPart")));
            SetRef(ac, "weaponManager", Find(ourRoot, "cockpit").GetComponent(T("WeaponManager")));
            SetRefArray(ac, "canopies", new Object[0]);
            Note($"Cockpit: {interior.Count} interior / {exterior.Count} exterior renderers");
        }

        static void ApplyPhysics()
        {
            float totalMass = 0, totalFuel = 0, totalArea = 0;
            foreach (var kv in Phys)
            {
                var t = Find(ourRoot, kv.Key); if (!t) { Note("  ! no part " + kv.Key); continue; }
                var ap = t.GetComponent(T("AeroPart"));
                if (!ap) { Note("  ! no AeroPart on " + kv.Key); continue; }
                SetF(ap, "mass", kv.Value.mass);
                SetF(ap, "wingArea", kv.Value.area);
                SetF(ap, "dragArea", kv.Value.drag);
                totalMass += kv.Value.mass; totalArea += kv.Value.area;
                var ft = t.GetComponent(T("FuelTank"));
                if (ft) { SetF(ft, "fuelCapacity", kv.Value.fuel); totalFuel += kv.Value.fuel; }
                else if (kv.Value.fuel > 0)
                {
                    var donor = Find(tmplRoot, "wing2_L").GetComponent(T("FuelTank"));
                    var nf = CopyComponent(donor, t.gameObject);
                    SetF(nf, "fuelCapacity", kv.Value.fuel); totalFuel += kv.Value.fuel;
                }
            }
            Note($"Physics: structure {totalMass:F0} kg, fuel {totalFuel:F0} kg, wing area {totalArea:F1} m2");
        }

        static void WireJoints()
        {
            // Each AeroPart is joined to its parent part. A joint must carry everything outboard of it (structure
            // plus full fuel), so it is sized from the subtree's weight and lever arm with a 40x margin. The game
            // multiplies these by 10 again when it creates the FixedJoint.
            var apType = T("AeroPart");
            float PartMass(Component ap)
            {
                float m = SO(ap).FindProperty("mass").floatValue;
                var ft = ap.GetComponent(T("FuelTank"));
                if (ft) m += SO(ft).FindProperty("fuelCapacity").floatValue;
                return m;
            }
            foreach (var ap in ourRoot.GetComponentsInChildren(apType, true))
            {
                var t = ap.transform;
                if (t == ourRoot) { Set(ap, "joints", p => p.arraySize = 0); continue; }
                var parentPart = t.parent ? t.parent.GetComponentInParent(apType) : null;
                float sub = 0; Vector3 com = Vector3.zero;
                foreach (var c in t.GetComponentsInChildren(apType, true))
                {
                    var m = PartMass(c); sub += m; com += c.transform.position * m;
                }
                com /= Mathf.Max(1f, sub);
                float arm = (com - t.position).magnitude + 3f;
                float force = Mathf.Max(2e6f, sub * 9.81f * 40f);
                float torque = Mathf.Max(2e6f, sub * 9.81f * 40f * arm);
                Set(ap, "joints", p =>
                {
                    p.arraySize = parentPart ? 1 : 0;
                    if (!parentPart) return;
                    var j = p.GetArrayElementAtIndex(0);
                    j.FindPropertyRelative("connectedPart").objectReferenceValue = parentPart;
                    j.FindPropertyRelative("tensor").objectReferenceValue = null;
                    j.FindPropertyRelative("anchor").objectReferenceValue = null;
                    j.FindPropertyRelative("breakForce").floatValue = force;
                    j.FindPropertyRelative("breakTorque").floatValue = torque;
                    j.FindPropertyRelative("solverIterations").intValue = 20;
                });
                Note($"Joint {t.name} -> {(parentPart ? parentPart.name : "-")}: carries {sub / 1000f:F1} t, F {force:E1} N, T {torque:E1} Nm");
            }
        }

        static void AddColliders()
        {
            // Convex mesh colliders on every flight part that has a mesh (the game uses MeshColliders per part).
            foreach (var ap in ourRoot.GetComponentsInChildren(T("AeroPart"), true))
            {
                var go = ((Component)ap).gameObject;
                var mf = go.GetComponent<MeshFilter>();
                if (!mf) { var vis = go.transform.Find(go.name + "_visible"); if (vis) mf = vis.GetComponent<MeshFilter>(); }
                if (!mf || go.GetComponent<MeshCollider>()) continue;
                var mc = go.AddComponent<MeshCollider>(); mc.sharedMesh = mf.sharedMesh; mc.convex = true;
            }
            foreach (var r in ourRoot.Find("cockpit") ? Find(ourRoot, "cockpit_int").GetComponentsInChildren<Collider>(true) : new Collider[0])
                Object.DestroyImmediate(r);
        }

        static void RemapReferences()
        {
            int remapped = 0, cleared = 0;
            foreach (var comp in ourRoot.GetComponentsInChildren<Component>(true))
            {
                if (comp == null || comp is Transform) continue;
                var so = new SerializedObject(comp);
                var it = so.GetIterator();
                bool changed = false;
                while (it.Next(true))
                {
                    if (it.propertyType != SerializedPropertyType.ObjectReference) continue;
                    var v = it.objectReferenceValue;
                    if (v == null) continue;
                    if (Remap.TryGetValue(v, out var to)) { it.objectReferenceValue = to; changed = true; remapped++; continue; }
                    Transform owner = v is GameObject g ? g.transform : v is Component c ? c.transform : null;
                    if (owner && tmplRoot && owner.IsChildOf(tmplRoot))
                    {
                        // Try a same-named object in our hierarchy, else clear.
                        var alt = Find(ourRoot, owner.name);
                        Object rep = null;
                        if (alt) rep = v is GameObject ? alt.gameObject : v is Transform ? (Object)alt : alt.GetComponent(v.GetType());
                        it.objectReferenceValue = rep; changed = true;
                        if (rep) remapped++; else { cleared++; Note($"  cleared {comp.GetType().Name}.{it.propertyPath} on {comp.name} (was {owner.name})"); }
                    }
                }
                if (changed) so.ApplyModifiedPropertiesWithoutUndo();
            }
            Note($"References: {remapped} remapped, {cleared} cleared");
        }

        // ------------------------------------------------------------------ materials
        // Exterior uses the game's own AircraftSkin shader graph (restored from the placeholder at runtime) so the
        // B-52 is lit, damaged and fogged like stock aircraft. Canopy glass uses the game's glass material.
        // The interior keeps URP/Lit (screens need emission).
        static readonly Dictionary<Material, Material> MatCache = new Dictionary<Material, Material>();

        static Texture2D SolidTex(string name, Color c, bool linear = false)
        {
            var path = Gen + "/" + name + ".png";
            if (!System.IO.File.Exists(path))
            {
                var t = new Texture2D(4, 4, TextureFormat.RGBA32, false, linear);
                t.SetPixels(Enumerable.Repeat(c, 16).ToArray()); t.Apply();
                System.IO.File.WriteAllBytes(path, t.EncodeToPNG());
                AssetDatabase.ImportAsset(path);
                if (linear)
                {
                    var ti = (TextureImporter)AssetImporter.GetAtPath(path);
                    ti.sRGBTexture = false; ti.SaveAndReimport();
                }
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static void ConvertMaterials()
        {
            var skinShader = AssetDatabase.LoadAssetAtPath<Shader>(DoNotShip + "/Shader/Shader Graphs_AircraftSkin_PLACEHOLDER.shader");
            var glassMat = AssetDatabase.LoadAssetAtPath<Material>(DoNotShip + "/Material/FastBomber1_glass_PLACEHOLDER.mat");
            var flatNormal = SolidTex("flat_normal", new Color(0.5f, 0.5f, 1f, 1f), true);
            var black = SolidTex("black", Color.black, true);
            var white = SolidTex("white", Color.white, true);
            var interior = Find(ourRoot, "cockpit_int");
            int converted = 0;
            foreach (var r in ourRoot.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (interior && r.transform.IsChildOf(interior)) continue;
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    var src = mats[i]; if (!src) continue;
                    // Only convert our FBX materials; game materials (seats, tac screen, pilots) stay as placeholders.
                    if (AssetDatabase.GetAssetPath(src).StartsWith(DoNotShip)) continue;
                    if (MatCache.TryGetValue(src, out var done)) { mats[i] = done; continue; }
                    Material m;
                    if (src.name.StartsWith("Glass") && glassMat) m = glassMat;   // placeholder: restored to the game's glass
                    else
                    {
                        m = new Material(skinShader) { name = "B52_" + src.name };
                        var tex = src.HasProperty("_BaseMap") ? src.GetTexture("_BaseMap") as Texture2D : null;
                        if (!tex)
                        {
                            var col = src.HasProperty("_BaseColor") ? src.GetColor("_BaseColor") : Color.gray;
                            tex = SolidTex("col_" + src.name.Replace(' ', '_'), col);
                        }
                        m.SetTexture("_Basecolor", tex); m.SetTexture("_Livery", tex); m.SetTexture("_BasecolorDmg", tex);
                        m.SetTexture("_Normal", flatNormal); m.SetTexture("_NormalDmg", flatNormal);
                        m.SetTexture("_Metallic", black); m.SetTexture("_AO", white);
                        m.SetFloat("_HitPoints", 100f);
                        AssetDatabase.CreateAsset(m, Gen + "/" + m.name + ".mat");
                        converted++;
                    }
                    MatCache[src] = m; mats[i] = m;
                }
                r.sharedMaterials = mats;
            }
            // Interior materials are FBX sub-assets; extract them so they ship in the bundle as normal assets.
            foreach (var r in interior ? interior.GetComponentsInChildren<MeshRenderer>(true) : new MeshRenderer[0])
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    var src = mats[i]; if (!src) continue;
                    if (AssetDatabase.GetAssetPath(src).StartsWith(DoNotShip)) continue;
                    if (MatCache.TryGetValue(src, out var done)) { mats[i] = done; continue; }
                    var m = new Material(src) { name = "B52_" + src.name };
                    if (src.name.StartsWith("CP_MFD"))
                    {
                        m.EnableKeyword("_EMISSION");
                        m.SetTexture("_EmissionMap", src.GetTexture("_BaseMap"));
                        m.SetColor("_EmissionColor", Color.white * 1.2f);
                        m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
                    }
                    AssetDatabase.CreateAsset(m, Gen + "/" + m.name + ".mat");
                    MatCache[src] = m; mats[i] = m;
                }
                r.sharedMaterials = mats;
            }
            Note($"Materials: {converted} exterior skins, {MatCache.Count} total");
        }

        /// <summary>Repairs template components whose references were cleared by the remap (they NRE at runtime).</summary>
        static void FixLeftovers()
        {
            var ac = ourRoot.GetComponent(T("Aircraft"));
            var cockpitPart = Find(ourRoot, "cockpit").GetComponent(T("UnitPart"));

            // Plain reference arrays: drop the cleared (null) entries.
            foreach (var (comp, prop) in new[] { (ac, "dopplerSounds"), (ac, "groundEquipment"),
                     (ourRoot.GetComponent(T("SetGlobalParticles")), "systems") })
                DropNulls(comp, prop);

            // Nav lights: keep only entries whose renderer survived.
            var nav = ourRoot.GetComponent(T("NavLights"));
            if (nav) Set(nav, "navLights", p =>
            {
                for (int i = p.arraySize - 1; i >= 0; i--)
                {
                    var e = p.GetArrayElementAtIndex(i);
                    var r = e.FindPropertyRelative("renderer");
                    var part = e.FindPropertyRelative("part");
                    if ((r != null && r.objectReferenceValue == null) || (part != null && part.objectReferenceValue == null))
                        p.DeleteArrayElementAtIndex(i);
                }
                Note("NavLights kept: " + p.arraySize);
            });

            // Flares: the B-52's dispensers are in the aft fuselage.
            var fl = ourRoot.GetComponent(T("FlareEjector"));
            var aft = Find(ourRoot, "fuselage_R");
            if (fl) Set(fl, "ejectionPoints", p =>
            {
                for (int i = 0; i < p.arraySize; i++)
                {
                    var e = p.GetArrayElementAtIndex(i);
                    float side = i % 2 == 0 ? -1.4f : 1.4f;
                    var pt = Child(aft, "flareEjector_" + i, aft.position + new Vector3(side, -1.5f, -4f), Quaternion.LookRotation(new Vector3(side, -1f, -1f)));
                    e.FindPropertyRelative("part").objectReferenceValue = aft.GetComponent(T("UnitPart"));
                    e.FindPropertyRelative("transform").objectReferenceValue = pt;
                }
            });

            // Radar locator on the nose sensors.
            var rl = Find(ourRoot, "nose_sensors")?.GetComponent(T("RadarLocator"));
            if (rl)
            {
                SetRef(rl, "aircraft", ac);
                SetRefArray(rl, "essentialParts", new Object[] { cockpitPart });
            }

            // Nose sensors (radar, target camera): part references point at the cockpit part.
            foreach (var comp in Find(ourRoot, "nose_sensors").GetComponents<Component>())
            {
                if (comp is Transform) continue;
                var so = SO(comp); var it = so.GetIterator();
                while (it.Next(true))
                    if (it.propertyType == SerializedPropertyType.ObjectReference && it.objectReferenceValue == null
                        && (it.name == "attachedPart" || it.name == "part" || it.name == "unitPart"))
                        it.objectReferenceValue = cockpitPart;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            var tc = Find(ourRoot, "nose_sensors").GetComponent(T("TargetCam"));
            if (tc)
            {
                var cpF = Find(ourRoot, "targetCamPoint_F"); var cpR = Find(ourRoot, "targetCamPoint_R"); var lc = Find(ourRoot, "landingCam");
                if (cpF) { cpF.position = Find(ourRoot, "nose_sensors").position + new Vector3(0, -1.6f, -1.5f); }   // Sniper pod / EVS window under the nose
                if (SO(tc).FindProperty("camMountForward").objectReferenceValue == null && cpF) SetRef(tc, "camMountForward", cpF);
                if (SO(tc).FindProperty("camMountRear").objectReferenceValue == null && cpR) SetRef(tc, "camMountRear", cpR);
                if (SO(tc).FindProperty("camMountLanding").objectReferenceValue == null && lc) SetRef(tc, "camMountLanding", lc);
                SetRef(tc, "attachedPart", cockpitPart);
            }

            // Weapons come in a later version: no hardpoint sets yet (the template's point at FastBomber bays).
            BuildHardpoints();

            // Downwash (jet-wash over water) referenced FastBomber engines; the B-52 does without it.
            var dw = Find(ourRoot, "downwash");
            if (dw) Object.DestroyImmediate(dw.gameObject);

            // Any remaining null entries in UnitPart.damageEffects / hostedParticles lists.
            foreach (var up in ourRoot.GetComponentsInChildren(T("UnitPart"), true))
            {
                DropNulls(up, "hostedParticles");
                DropNulls(up, "disintegrationEffects");
                DropNulls(up, "disintegrateObjects");
            }
        }

        static List<ScriptableObject> BayMounts, PylonMounts;

        static ScriptableObject Mount(string code, bool bay) =>
            (bay ? BayMounts : PylonMounts).FirstOrDefault(m => m && m.name.StartsWith("B52_" + code + "_"));

        static void BuildHardpoints()
        {
            (BayMounts, PylonMounts) = B52Weapons.Generate(Note);
            var wm = Find(ourRoot, "cockpit").GetComponent(T("WeaponManager"));
            var root = ourRoot.GetComponent(T("UnitPart"));
            var bay = Find(ourRoot, "bombBay");
            var bayMount = Child(ourRoot, "bayMount", bay ? bay.position : ourRoot.position + new Vector3(0, -1.6f, 6.5f));
            var doors = new[] { "bayDoorHinge_L2", "bayDoorHinge_R2" }.Select(n => Find(ourRoot, n)).Where(t => t)
                        .Select(t => (Object)t.GetComponent(T("BayDoor"))).ToList();

            // HSAB pylons between the fuselage and the inboard engine pods, one per wing.
            var pylonMat = Find(ourRoot, "pod1_L").GetComponent<Renderer>()?.sharedMaterial;
            var pylons = new List<(Transform mount, Renderer r, Object part)>();
            foreach (var sd in new[] { "L", "R" })
            {
                var wr = Find(ourRoot, "wingroot_" + sd);
                var wb = wr.GetComponent<Renderer>().bounds;
                float x = (sd == "L" ? -1 : 1) * 5.6f;
                var py = GameObject.CreatePrimitive(PrimitiveType.Cube);
                py.name = "hsabPylon_" + sd;
                Object.DestroyImmediate(py.GetComponent<Collider>());
                py.transform.SetParent(wr, false);
                py.transform.position = new Vector3(x, wb.min.y - 0.35f, wb.center.z - 0.8f);
                py.transform.rotation = ourRoot.rotation;
                py.transform.localScale = new Vector3(0.32f, 0.9f, 5.2f);
                var pr = py.GetComponent<Renderer>(); if (pylonMat) pr.sharedMaterial = pylonMat; pr.enabled = false;
                var mt = Child(wr, "pylonMount_" + sd, new Vector3(x, wb.min.y - 0.85f, wb.center.z - 0.8f));
                pylons.Add((mt, pr, wr.GetComponent(T("UnitPart"))));
            }

            Set(wm, "hardpointSets", p =>
            {
                p.arraySize = 2;
                void Fill(SerializedProperty set, string name, List<ScriptableObject> options)
                {
                    set.FindPropertyRelative("name").stringValue = name;
                    set.FindPropertyRelative("precludingHardpointSets").arraySize = 0;
                    set.FindPropertyRelative("SymmetryWithPrev").boolValue = false;
                    set.FindPropertyRelative("SymmetryName").stringValue = "";
                    var wo = set.FindPropertyRelative("weaponOptions");
                    wo.arraySize = options.Count;
                    for (int i = 0; i < options.Count; i++) wo.GetArrayElementAtIndex(i).objectReferenceValue = options[i];
                    set.FindPropertyRelative("weaponMount").objectReferenceValue = null;
                }
                void Point(SerializedProperty hp, Transform t, Object part, IList<Object> bayDoors, Renderer pylon)
                {
                    hp.FindPropertyRelative("transform").objectReferenceValue = t;
                    hp.FindPropertyRelative("part").objectReferenceValue = part;
                    var bd = hp.FindPropertyRelative("bayDoors"); bd.arraySize = bayDoors.Count;
                    for (int i = 0; i < bayDoors.Count; i++) bd.GetArrayElementAtIndex(i).objectReferenceValue = bayDoors[i];
                    hp.FindPropertyRelative("doorOpenDuration").floatValue = bayDoors.Count > 0 ? 2.5f : 0f;
                    var po = hp.FindPropertyRelative("pylonOptions");
                    po.arraySize = pylon ? 1 : 0;
                    if (pylon)
                    {
                        var e = po.GetArrayElementAtIndex(0);
                        e.FindPropertyRelative("cargo").boolValue = false;
                        e.FindPropertyRelative("mount").objectReferenceValue = null;     // any mount shows the pylon
                        e.FindPropertyRelative("renderer").objectReferenceValue = pylon;
                    }
                    hp.FindPropertyRelative("Pylon").objectReferenceValue = null;
                    hp.FindPropertyRelative("Plug").objectReferenceValue = null;
                    hp.FindPropertyRelative("BuiltInWeapons").arraySize = 0;
                    hp.FindPropertyRelative("BuiltInTurrets").arraySize = 0;
                }
                var s0 = p.GetArrayElementAtIndex(0);
                Fill(s0, "Internal Bay", BayMounts);
                var h0 = s0.FindPropertyRelative("hardpoints"); h0.arraySize = 1;
                Point(h0.GetArrayElementAtIndex(0), bayMount, root, doors, null);

                var s1 = p.GetArrayElementAtIndex(1);
                Fill(s1, "Wing Pylons (HSAB)", PylonMounts);
                var h1 = s1.FindPropertyRelative("hardpoints"); h1.arraySize = 2;
                for (int i = 0; i < 2; i++) Point(h1.GetArrayElementAtIndex(i), pylons[i].mount, pylons[i].part, new Object[0], pylons[i].r);
            });
            Note($"Hardpoints: bay {BayMounts.Count} options (doors {doors.Count}), pylons {PylonMounts.Count} options");
        }

        static void DropNulls(Object comp, string prop)
        {
            if (!comp) return;
            var so = SO(comp); var p = so.FindProperty(prop);
            if (p == null || !p.isArray) return;
            int removed = 0;
            for (int i = p.arraySize - 1; i >= 0; i--)
            {
                var e = p.GetArrayElementAtIndex(i);
                if (e.propertyType == SerializedPropertyType.ObjectReference && e.objectReferenceValue == null)
                {
                    p.DeleteArrayElementAtIndex(i);
                    if (i < p.arraySize && p.GetArrayElementAtIndex(i).objectReferenceValue == null) p.DeleteArrayElementAtIndex(i);
                    removed++;
                }
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            if (removed > 0) Note($"  {comp.GetType().Name}.{prop}: dropped {removed} null entries");
        }

        /// <summary>Clone every object a copied component references inside the donor's subtree, so audio sources,
        /// particle effects and similar helpers come along (e.g. LandingGear tyre noise, dust, skid effect).</summary>
        static void CloneDonorRefs(Component comp, Transform donorRoot, Transform newParent)
        {
            var clones = new Dictionary<GameObject, GameObject>();
            var so = SO(comp); var it = so.GetIterator();
            while (it.Next(true))
            {
                if (it.propertyType != SerializedPropertyType.ObjectReference || it.objectReferenceValue == null) continue;
                var v = it.objectReferenceValue;
                var go = v is GameObject g ? g : v is Component c ? c.gameObject : null;
                if (!go || !go.transform.IsChildOf(donorRoot) || go.transform == donorRoot) continue;
                Object rep = null;
                if (v is AudioSource || v is ParticleSystem)
                {
                    // Copy just the component (not the donor's wheel mesh) onto a fresh helper object.
                    if (!clones.TryGetValue(go, out var holder))
                    {
                        holder = new GameObject(go.name + "_fx");
                        holder.transform.SetParent(newParent, false);
                        clones[go] = holder;
                    }
                    var existing = holder.GetComponent(v.GetType());
                    rep = existing ? existing : CopyComponent((Component)v, holder);
                    if (v is ParticleSystem && !holder.GetComponent<ParticleSystemRenderer>())
                        CopyComponent(go.GetComponent<ParticleSystemRenderer>(), holder);
                }
                else if (v is GameObject && !go.GetComponentInChildren<MeshRenderer>())
                {
                    if (!clones.TryGetValue(go, out var copy))
                    {
                        copy = Object.Instantiate(go, newParent); copy.name = go.name; copy.transform.localPosition = Vector3.zero;
                        clones[go] = copy;
                    }
                    rep = copy;
                }
                else continue;     // structural refs (wheels, hinge, bumpstop...) are set explicitly afterwards
                it.objectReferenceValue = rep;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void ClearLiveryTargets()
        {
            // The B-52 keeps bohmerang's per-panel textures, so no renderer takes the single livery texture yet.
            foreach (var up in ourRoot.GetComponentsInChildren(T("UnitPart"), true))
                Set(up, "damageMaterial.renderers", p => p.arraySize = 0);
            var wm = Find(ourRoot, "cockpit").GetComponent(T("WeaponManager"));
            Set(wm, "skinnables", p => p.arraySize = 0);
            Set(wm, "colorables", p => p.arraySize = 0);
            var lod = ourRoot.GetComponent<LODGroup>();
            if (lod) Object.DestroyImmediate(lod);
        }

        static void MakeOps()
        {
            // Spawn from medium hangars (the ones that host the Darkreach); shelters are too small for a 56 m span.
            var path = ModDir + "/OpAddAircraftToHangars.asset";
            AssetDatabase.DeleteAsset(path);
            var op = ScriptableObject.CreateInstance<Blueprinter.OpAddAircraftToHangars>();
            op.aircraftJsonKey = "B52J";
            op.hangars.Add(new Blueprinter.OpAddAircraftToHangars.HangarTarget
            {
                hangarUnitJsonKey = "hangar_med", hangarNames = new List<string> { "hangar_med" }
            });
            AssetDatabase.CreateAsset(op, path);
            Note("Op: OpAddAircraftToHangars -> hangar_med");
        }

        public const string Version = "0.2.9";
        const string BuildDir = @"C:\Users\jayea\Documents\GitHub\NO-B52\build";

        public static void BuildMod()
        {
            System.IO.Directory.CreateDirectory(BuildDir);
            Blueprinter.ModBuilder.Build("B52", "B-52J Stratofortress", Version, BuildDir);
            Debug.Log("[B52] mod built to " + BuildDir);
        }

        static void MakeDefinition(GameObject prefab)
        {
            var defSrc = AssetDatabase.LoadAssetAtPath<ScriptableObject>(TemplateDef);
            var parSrc = AssetDatabase.LoadAssetAtPath<ScriptableObject>(TemplateParams);
            var def = ScriptableObject.CreateInstance(defSrc.GetType()); EditorUtility.CopySerialized(defSrc, def);
            var par = ScriptableObject.CreateInstance(parSrc.GetType()); EditorUtility.CopySerialized(parSrc, par);
            def.name = "B52J"; par.name = "B52J_Parameters";

            var ps = SO(par);
            ps.FindProperty("aircraftName").stringValue = "B52J";
            var af = ps.FindProperty("airfoils");
            for (int i = 0; i < af.arraySize; i++)
            {
                var a = af.GetArrayElementAtIndex(i);
                a.FindPropertyRelative("name").stringValue = "B52_wing";
                a.FindPropertyRelative("liftCoef").animationCurveValue = B52Lift();
                a.FindPropertyRelative("dragCoef").animationCurveValue = B52Drag();
            }
            ps.FindProperty("rankRequired").intValue = 4;
            // LoadoutSelector.LoadDefaults reads loadouts[1]; with no hardpoints yet both are empty.
            var lo = ps.FindProperty("loadouts");
            lo.arraySize = 2;
            void SetWeapons(SerializedProperty weapons, ScriptableObject bayM, ScriptableObject pylonM)
            {
                weapons.arraySize = 2;
                weapons.GetArrayElementAtIndex(0).objectReferenceValue = bayM;
                weapons.GetArrayElementAtIndex(1).objectReferenceValue = pylonM;
            }
            SetWeapons(lo.GetArrayElementAtIndex(0).FindPropertyRelative("weapons"), null, null);
            SetWeapons(lo.GetArrayElementAtIndex(1).FindPropertyRelative("weapons"), Mount("MK82", true), Mount("MK82", false));
            var presets = new (string name, string bay, string pylon, float fuel)[]
            {
                ("Mk 82 x51", "MK82", "MK82", 0.7f),
                ("JASSM-ER x20", "AGM158B", "AGM158B", 0.85f),
                ("LRASM x20 (anti-ship)", "AGM158C", "AGM158C", 0.85f),
                ("GBU-31 JDAM x24", "GBU31", "GBU31", 0.7f),
                ("GBU-38 JDAM x80", "GBU38", "GBU38", 0.7f),
                ("CBU-87 x40", "CBU87", "CBU87", 0.7f),
                ("Mk 62 Quickstrike x80", "MK62", "MK62", 0.75f),
                ("ALCM x20 (nuclear)", "AGM86B", "AGM86B", 0.9f),
                ("B83 x8 (nuclear)", "B83", null, 0.9f),
            };
            var sl = ps.FindProperty("StandardLoadouts");
            sl.arraySize = presets.Length;
            for (int i = 0; i < presets.Length; i++)
            {
                var e = sl.GetArrayElementAtIndex(i);
                e.FindPropertyRelative("disabled").boolValue = false;
                e.FindPropertyRelative("Name").stringValue = presets[i].name;
                e.FindPropertyRelative("FuelRatio").floatValue = presets[i].fuel;
                SetWeapons(e.FindPropertyRelative("loadout.weapons"), Mount(presets[i].bay, true),
                           presets[i].pylon == null ? null : Mount(presets[i].pylon, false));
            }
            ps.FindProperty("DefaultFuelLevel").floatValue = 0.6f;
            ps.FindProperty("HUDExtras").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(DoNotShip + "/GameObject/SFB_HUDExtras_PLACEHOLDER.prefab");
            // One USAF livery, offered to every faction the template had.
            var livPath = Gen + "/B52_USAF_livery.asset";
            AssetDatabase.DeleteAsset(livPath);
            var liv = ScriptableObject.CreateInstance(T("LiveryData")); liv.name = "B52_USAF_livery";
            var ls = SO(liv);
            ls.FindProperty("Texture").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Texture2D>(ModDir + "/Textures/middle_fuselage_png.png");
            ls.FindProperty("Glossiness").floatValue = 0.35f;
            ls.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.CreateAsset(liv, livPath);
            var livGuid = AssetDatabase.AssetPathToGUID(livPath);
            var lv = ps.FindProperty("liveries");
            var seen = new HashSet<Object>();
            for (int i = lv.arraySize - 1; i >= 0; i--)
            {
                var f = lv.GetArrayElementAtIndex(i).FindPropertyRelative("faction").objectReferenceValue;
                if (seen.Contains(f)) { lv.DeleteArrayElementAtIndex(i); continue; }
                seen.Add(f);
                var e = lv.GetArrayElementAtIndex(i);
                e.FindPropertyRelative("name").stringValue = "USAF Gunship Gray";
                e.FindPropertyRelative("assetReference.m_AssetGUID").stringValue = livGuid;
                e.FindPropertyRelative("assetReference.m_SubObjectName").stringValue = "";
            }
            Note($"Liveries: {lv.arraySize} -> {livGuid}");
            ps.FindProperty("aircraftGLimit").floatValue = 2.5f;
            ps.FindProperty("maxSpeed").floatValue = 290f;
            ps.FindProperty("takeoffSpeed").floatValue = 90f;
            ps.FindProperty("takeoffDistance").floatValue = 2900f;
            ps.FindProperty("approachSpeed").floatValue = 82f;
            ps.FindProperty("landingSpeed").floatValue = 72f;
            ps.FindProperty("shortLandingSpeed").floatValue = 68f;
            ps.FindProperty("cruiseThrottle").floatValue = 0.82f;
            ps.FindProperty("turningRadius").floatValue = 3500f;
            ps.FindProperty("cornerSpeed").floatValue = 180f;
            ps.FindProperty("groundTurningRadius").floatValue = 45f;
            ps.ApplyModifiedPropertiesWithoutUndo();

            var ds = SO(def);
            ds.FindProperty("jsonKey").stringValue = "B52J";
            ds.FindProperty("unitName").stringValue = "B-52J Stratofortress";
            ds.FindProperty("code").stringValue = "B-52J";
            ds.FindProperty("description").stringValue =
                "Boeing B-52J Stratofortress. The B-52H airframe re-engined with eight Rolls-Royce F130 turbofans " +
                "(17,000 lbf each) and fitted with the AN/APQ-188 AESA radar and the 1760 internal weapons bay. " +
                "Carries up to 70,000 lb of ordnance: 51 Mk 82, 20 JASSM-ER, 20 GBU-31 or 4 hypersonic missiles.";
            ds.FindProperty("length").floatValue = 48.5f;
            ds.FindProperty("width").floatValue = 56.4f;
            ds.FindProperty("height").floatValue = 12.4f;
            ds.FindProperty("mass").floatValue = 83250f;
            ds.FindProperty("value").floatValue = 900f;
            ds.FindProperty("manpower").intValue = 5;
            ds.FindProperty("radarSize").floatValue = 0.1f;          // the B-52's RCS is famously large
            ds.FindProperty("unitPrefab").objectReferenceValue = prefab;
            ds.FindProperty("spawnOffset").vector3Value = new Vector3(0, 3.9f, 0);
            ds.FindProperty("aircraftParameters").objectReferenceValue = par;
            ds.FindProperty("aircraftInfo.emptyWeight").floatValue = 83250f;
            ds.FindProperty("aircraftInfo.maxSpeed").floatValue = 1047f;
            ds.FindProperty("aircraftInfo.stallSpeed").floatValue = 240f;
            ds.FindProperty("aircraftInfo.maneuverability").floatValue = 2f;
            ds.FindProperty("aircraftInfo.maxWeight").floatValue = 221350f;
            ds.FindProperty("restRotation").vector3Value = Vector3.zero;
            ds.ApplyModifiedPropertiesWithoutUndo();

            AssetDatabase.CreateAsset(par, Gen + "/B52J_Parameters.asset");
            AssetDatabase.CreateAsset(def, Gen + "/B52J.asset");
            // The prefab's Unit.definition points at our definition.
            var root = PrefabUtility.LoadPrefabContents(AssetDatabase.GetAssetPath(prefab));
            SetRef(root.GetComponent(T("Aircraft")), "definition", def);
            PrefabUtility.SaveAsPrefabAsset(root, AssetDatabase.GetAssetPath(prefab));
            PrefabUtility.UnloadPrefabContents(root);
            Note("Definition + parameters written to " + Gen);
        }
    }
}
