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
                { "rudder",     (200, 12, 0, 0) },
                { "hstab_L",    (900, 30, 0.03f, 0) }, { "hstab_R", (900, 30, 0.03f, 0) },
                { "elevator_L", (150, 12, 0, 0) },   { "elevator_R", (150, 12, 0, 0) },
                { "wingroot_L", (5000, 58, 0.05f, 17000) }, { "wingroot_R", (5000, 58, 0.05f, 17000) },
                { "wing1_L",    (4000, 42, 0.04f, 14000) }, { "wing1_R", (4000, 42, 0.04f, 14000) },
                { "wing2_L",    (2500, 27, 0.03f, 8000) },  { "wing2_R", (2500, 27, 0.03f, 8000) },
                { "wingtip_L",  (1200, 12, 0.12f, 2140) },  { "wingtip_R", (1200, 12, 0.12f, 2140) },
                { "flap1_L",    (400, 10, 0, 0) }, { "flap1_R", (400, 10, 0, 0) },
                { "flap2_L",    (450, 11, 0, 0) }, { "flap2_R", (450, 11, 0, 0) },
                { "spoilers_L", (250, 4, 0, 0) },  { "spoilers_R", (250, 4, 0, 0) },
                { "pod1_L",     (4600, 0, 0.35f, 0) }, { "pod1_R", (4600, 0, 0.35f, 0) },
                { "pod2_L",     (4600, 0, 0.35f, 0) }, { "pod2_R", (4600, 0, 0.35f, 0) },
            };

        // our part -> (template donor part, component types to copy)
        static readonly (string ours, string donor, string[] comps)[] Parts =
        {
            ("fuselage_F", "fuselage_F", new[] { "AeroPart", "FuelTank" }),
            ("cockpit", "cockpit", new[] { "AeroPart", "AutopilotPlane", "ControlsFilter", "WeaponManager", "EscapeCapsule" }),
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
                Object.DestroyImmediate(tmplRoot.gameObject);

                if (!AssetDatabase.IsValidFolder(Gen)) AssetDatabase.CreateFolder(ModDir, "Generated");
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
            imp.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            imp.SaveAndReimport();
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
            foreach (var n in new[] { "WSO", "Pilot", "EjectSmoke", "ejectionForceTransform", "Parachute", "DrogueChute",
                                      "cushionTransform_rear", "cushionTransform_front" })
                Move(n, cockpit);
            foreach (var t in FindAll(tmplRoot, "EjectFlame")) t.SetParent(cockpit, true);
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
            if (com) com.position = Find(ourRoot, "wingroot_L").position * 0.5f + Find(ourRoot, "wingroot_R").position * 0.5f;
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
            int n = 1;
            foreach (var pod in new[] { "pod2_L", "pod1_L", "pod1_R", "pod2_R" })
            {
                var p = Find(ourRoot, pod);
                var r = p.GetComponentInChildren<Renderer>();
                var b = r ? r.bounds : new Bounds(p.position, Vector3.one);
                for (int k = 0; k < 2; k++)
                {
                    float side = (k == 0 ? -1 : 1) * b.extents.x * 0.45f;
                    var go = Object.Instantiate(srcEngine.gameObject, p);
                    go.name = "engine" + n;
                    go.transform.position = new Vector3(b.center.x + side, b.center.y, b.center.z);
                    go.transform.rotation = ourRoot.rotation;
                    var tf = go.GetComponent(T("Turbofan"));
                    SetF(tf, "staticThrust", 75600f);          // F130: ~17,000 lbf
                    SetRefArray(tf, "nozzles", new Object[0]);  // no afterburner nozzles on a B-52
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
                var box = gear.gameObject.AddComponent<BoxCollider>();
                box.size = new Vector3(outrigger ? 0.3f : 1.0f, 0.4f, outrigger ? 0.85f : 1.5f);
                box.center = gear.InverseTransformPoint(unsprung.position);

                float travel = (bump.position - unsprung.position).magnitude + wheelR;
                SetRef(lg, "attachedPart", parent.GetComponentInParent(T("AeroPart")));
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
                SetF(lg, "maxCompression", outrigger ? 0.35f : 0.45f);
                // B-52: ~25 t per main truck at MTOW; outriggers only touch when the wing rolls.
                SetF(lg, "springRate", outrigger ? 400000f : 3200000f);
                SetF(lg, "dampingRate", outrigger ? 60000f : 450000f);
                SetF(lg, "mass", outrigger ? 300f : 1500f);
                SetF(lg, "foldDegrees", outrigger ? -95f : 90f);
                Set(lg, "steering", p => p.boolValue = k[0] == 'F');   // the forward trucks steer
                Set(lg, "braked", p => p.boolValue = !outrigger);
                SetF(lg, "steeringLock", 30f);
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
                ("elevator_L", 20f, 0f, 0f), ("elevator_R", 20f, 0f, 0f),
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
                SetF(hld, "deployedArea", Phys[f].area * 0.6f);
                Set(hld, "movingParts", p =>
                {
                    p.arraySize = 1;
                    var e = p.GetArrayElementAtIndex(0);
                    e.FindPropertyRelative("move").boolValue = true;
                    e.FindPropertyRelative("rotate").boolValue = true;
                    e.FindPropertyRelative("transform").objectReferenceValue = vis;
                    e.FindPropertyRelative("positionRetracted").vector3Value = Vector3.zero;
                    e.FindPropertyRelative("positionDeployed").vector3Value = vis.parent.InverseTransformVector(-ourRoot.forward * 0.9f - ourRoot.up * 0.25f);
                    e.FindPropertyRelative("anglesRetracted").vector3Value = Vector3.zero;
                    e.FindPropertyRelative("anglesDeployed").vector3Value = new Vector3(30f, 0, 0);
                });
            }
        }

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
                    e.FindPropertyRelative("range").floatValue = 12f;
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
                    e.FindPropertyRelative("range").floatValue = -40f;
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
            // Each AeroPart is joined to its parent part. Break strength scales with the part mass.
            foreach (var ap in ourRoot.GetComponentsInChildren(T("AeroPart"), true))
            {
                var t = ((Component)ap).transform;
                if (t == ourRoot) { Set(ap, "joints", p => p.arraySize = 0); continue; }
                var parentPart = t.parent ? t.parent.GetComponentInParent(T("AeroPart")) : null;
                var so = SO(ap); var mass = so.FindProperty("mass").floatValue;
                Set(ap, "joints", p =>
                {
                    p.arraySize = parentPart ? 1 : 0;
                    if (!parentPart) return;
                    var j = p.GetArrayElementAtIndex(0);
                    j.FindPropertyRelative("connectedPart").objectReferenceValue = parentPart;
                    j.FindPropertyRelative("tensor").objectReferenceValue = null;
                    j.FindPropertyRelative("anchor").objectReferenceValue = t;
                    j.FindPropertyRelative("breakForce").floatValue = Mathf.Max(200000f, mass * 400f);
                    j.FindPropertyRelative("breakTorque").floatValue = Mathf.Max(200000f, mass * 600f);
                    j.FindPropertyRelative("solverIterations").intValue = 6;
                });
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

        public const string Version = "0.1.0";
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
            ps.FindProperty("rankRequired").intValue = 4;
            ps.FindProperty("loadouts").arraySize = 0;
            ps.FindProperty("StandardLoadouts").arraySize = 0;
            ps.FindProperty("DefaultFuelLevel").floatValue = 0.6f;
            ps.FindProperty("aircraftGLimit").floatValue = 2.5f;
            ps.FindProperty("maxSpeed").floatValue = 290f;
            ps.FindProperty("takeoffSpeed").floatValue = 77f;
            ps.FindProperty("takeoffDistance").floatValue = 2900f;
            ps.FindProperty("approachSpeed").floatValue = 75f;
            ps.FindProperty("landingSpeed").floatValue = 68f;
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
