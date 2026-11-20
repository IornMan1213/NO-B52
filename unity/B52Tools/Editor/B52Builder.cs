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
    public static partial class B52Builder
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
                { "spoilers_L", (700, 12, 0, 0) },  { "spoilers_R", (700, 12, 0, 0) },   // 7 panels a side; ~11 deg/s roll at 150 m/s
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
            Log.Clear(); Remap.Clear(); MatCache.Clear(); bellyTris = null;
            try
            {
                ConfigureFbx();
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var tmpl = Spawn(TemplatePrefab); tmplRoot = tmpl.transform;
                B52DonorAudit.Begin(tmplRoot);
                var ours = Spawn(Fbx);
                ourRoot = ours.transform.Find("B52") ?? ours.transform;
                if (ourRoot != ours.transform) { ourRoot.SetParent(null, true); Object.DestroyImmediate(ours); }
                ourRoot.name = "B52";
                if (!AssetDatabase.IsValidFolder(Gen)) AssetDatabase.CreateFolder(ModDir, "Generated");
                NormalizeFrames();
                TakeTailGun();

                CopyRoot();
                foreach (var p in Parts) CopyPart(p.ours, p.donor, p.comps);
                foreach (var kv in Fold) MapGO(Find(tmplRoot, kv.Key), Find(ourRoot, kv.Value));
                MoveSubsystems();
                BuildEngines();
                BuildGear();
                BuildBayDoors();
                WireControlSurfaces();
                WireCockpit();
                TuneFlyByWire();
                AddAirbrake();
                TuneJammer();
                ApplyPhysics();
                WireJoints();
                AddColliders();
                SpecParts();
                SpecStructure();
                RemapReferences();
                FixLeftovers();
                SpecSystems();
                SpecAudioAndLights();
                SpecEffects();
                SpecVapor();
                ClearLiveryTargets();
                Object.DestroyImmediate(tmplRoot.gameObject);

                ConvertMaterials();
                SetLiveryTargets();
                BuildLods();
                var prefabPath = ModDir + "/B52.prefab";
                var prefab = PrefabUtility.SaveAsPrefabAsset(ourRoot.gameObject, prefabPath);
                Note("Saved " + prefabPath);
                MakeDefinition(prefab);
                B52DonorAudit.Write(ourRoot, TemplatePrefab, "B52DonorAudit.md", DoNotShip);
                Note("Donor audit: B52DonorAudit.md");
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
            B52DonorAudit.Copied(src, c);
            if (!Remap.ContainsKey(src)) Remap[src] = c;
            return c;
        }

        static SerializedObject SO(Object o) => new SerializedObject(o);

        static void Set(Object o, string prop, Action<SerializedProperty> act)
        {
            var so = SO(o); var p = so.FindProperty(prop);
            if (p == null) { Note($"  ! {o.GetType().Name}.{prop} not found"); return; }
            // A single setting set here is the B-52's choice even where it equals the donor's. For a group (a struct or
            // an array) only the settings the edit actually changed count, so a field left as it was stays the donor's
            // in the audit.
            // (Unity reports object references and vectors as having children too: those are single settings.)
            bool group = p.propertyType == SerializedPropertyType.Generic || (p.isArray && p.propertyType != SerializedPropertyType.String);
            var before = group ? Leaves(p) : null;
            act(p); so.ApplyModifiedPropertiesWithoutUndo();
            if (!group) { B52DonorAudit.Chose(o, prop); return; }
            var after = Leaves(SO(o).FindProperty(prop));
            foreach (var kv in after)
                if (!before.TryGetValue(kv.Key, out var v) || v != kv.Value) B52DonorAudit.Chose(o, kv.Key);
        }

        // Every leaf setting under a property, with a printable value, for telling what an edit changed.
        static Dictionary<string, string> Leaves(SerializedProperty p)
        {
            var d = new Dictionary<string, string>();
            if (p == null) return d;
            var it = p.Copy(); var end = p.GetEndProperty();
            bool enter = true;
            while (it.Next(enter) && !SerializedProperty.EqualContents(it, end))
            {
                enter = it.propertyType == SerializedPropertyType.Generic || (it.isArray && it.propertyType != SerializedPropertyType.String);
                if (it.isArray && it.propertyType != SerializedPropertyType.String) d[it.propertyPath + ".Array.size"] = it.arraySize.ToString();
                if (enter) continue;
                string v;
                switch (it.propertyType)
                {
                    case SerializedPropertyType.ObjectReference: v = it.objectReferenceValue ? it.objectReferenceValue.GetInstanceID().ToString() : "null"; break;
                    case SerializedPropertyType.AnimationCurve: v = it.animationCurveValue == null ? "null" : string.Join(";", it.animationCurveValue.keys.Select(k => $"{k.time},{k.value},{k.inTangent},{k.outTangent}")); break;
                    default: try { v = it.boxedValue?.ToString() ?? "null"; } catch { v = "?"; } break;
                }
                d[it.propertyPath] = v;
            }
            return d;
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
            // The exterior atlas is 8192 px (tools/make_atlas_hd.py: the paintings enlarged and sharpened); Unity would
            // import it at 2048. Crunched DXT keeps the download near the old 4096 BC7 one; the paint is soft colour,
            // so crunch's slight loss doesn't show.
            if (AssetImporter.GetAtPath(ModDir + "/Textures/B52_atlas.png") is TextureImporter ti)
            {
                ti.maxTextureSize = 8192; ti.textureCompression = TextureImporterCompression.Compressed;
                ti.crunchedCompression = true; ti.compressionQuality = 80; ti.anisoLevel = 4;
                ti.mipmapEnabled = true; ti.isReadable = false; ti.SaveAndReimport();
            }
            // The extra liveries (ExtraLiveries): same size, plain DXT1 rather than BC7, so each adds about half as much
            // to the download; flat paint looks the same either way.
            foreach (var (_, file, _, _) in ExtraLiveries)
                if (AssetImporter.GetAtPath(ModDir + "/Textures/" + file) is TextureImporter tc)
                {
                    tc.maxTextureSize = 4096; tc.textureCompression = TextureImporterCompression.Compressed;
                    tc.mipmapEnabled = true; tc.isReadable = false; tc.SaveAndReimport();
                }
            // Skin detail maps on the same atlas layout (tools/blender_ao.py, tools/make_skin_detail.py). No normal map:
            // one made from the atlas embossed the printed markings and dirt, so they looked like dents.
            void Tex(string file, int size, bool srgb)
            {
                if (!(AssetImporter.GetAtPath(ModDir + "/Textures/" + file) is TextureImporter t)) return;
                t.textureType = TextureImporterType.Default;
                t.sRGBTexture = srgb; t.maxTextureSize = size; t.textureCompression = TextureImporterCompression.CompressedHQ;
                t.mipmapEnabled = true; t.isReadable = false; t.SaveAndReimport();
            }
            Tex("B52_atlas_ao.png", 2048, false);
            Tex("B52_atlas_dmg.png", 2048, true);
            Tex("B52_atlas_ms.png", 2048, false);
            if (AssetImporter.GetAtPath(ModDir + "/Textures/B52_atlas_dmg_n.png") is TextureImporter tn)
            {
                // dents round the damage map's holes (not made from the paint, so no markings are embossed)
                tn.textureType = TextureImporterType.NormalMap; tn.maxTextureSize = 2048;
                tn.textureCompression = TextureImporterCompression.CompressedHQ; tn.mipmapEnabled = true; tn.SaveAndReimport();
            }
            // The cockpit's plain-colour palettes (tools/blender_cockpit.py, planner CK3): tiny, read one texel per face,
            // so no filtering, mip-maps or compression.
            foreach (var (file, srgb) in new[] { ("cp_palette.png", true), ("cp_palette_ms.png", false) })
                if (AssetImporter.GetAtPath(ModDir + "/Textures/" + file) is TextureImporter p)
                {
                    p.textureType = TextureImporterType.Default; p.sRGBTexture = srgb; p.filterMode = FilterMode.Point;
                    p.textureCompression = TextureImporterCompression.Uncompressed; p.mipmapEnabled = false;
                    p.isReadable = false; p.SaveAndReimport();
                }
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
                // Lift frame like the fin's tail_liftNormal: forward into the airflow (perpendicular to the hinge), lift
                // normal to the left, local X = cross(left, forward) = up the hinge. An earlier frame had forward pointing
                // aft, so the game saw ~180 deg angle of attack and the rudder made no force at all.
                var fwd = Vector3.ProjectOnPlane(Vector3.forward, hingeAxis).normalized;
                var rudRot = Quaternion.LookRotation(fwd, Vector3.left);
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
                // The donor's collider is its own fuselage's collision mesh: the B-52's centre section gets its own (AddColliders).
                if (tn == "MeshCollider") continue;
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
                // Move keeps the donor's world position (x +-15.75, under our wing): put both at our wingtip's
                // outermost point, the light 5 cm outboard of the skin.
                var mf = tip.GetComponent<MeshFilter>();
                if (mf && mf.sharedMesh)
                {
                    float sgn = s == "R" ? 1f : -1f;
                    var outer = mf.sharedMesh.vertices.Select(v => tip.TransformPoint(v)).OrderByDescending(p => p.x * sgn).First();
                    var light = Find(ourRoot, "navlight_" + s); var vortex = Find(ourRoot, "wingtipvortex_" + s);
                    if (light)
                    {
                        // the light's housing mesh is offset from its pivot: place the housing, not the pivot
                        var lr = light.GetComponent<Renderer>();
                        var offset = lr ? lr.bounds.center - light.position : Vector3.zero;
                        light.position = outer + Vector3.right * sgn * (0.05f + (lr ? lr.bounds.extents.x : 0f)) - offset;
                    }
                    if (vortex) vortex.position = outer;
                    Note($"  navlight_{s} at {outer:F2}");
                }
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
            AddCrew();
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

        // The rest of the B-52H crew, at their stations (eye points in Blender metres, tools/blender_cockpit.py frame:
        // x right, y forward, z up; the pilots' eyes are at (+-0.55, 24.31, 0.77)).
        // - EWO: the aft-facing seat on the left of the upper deck (seat_aft_L at y 22.25; the head leans back toward
        //   the seat back, 0.18 m from the seat as on the pilots' seats). The right aft seat (the old gunner's, now
        //   the instructor's) stays empty: six seats, five crew.
        // - Radar navigator (left) and navigator (right): side by side on the lower deck under the flight deck, facing
        //   forward, on downward ejection seats (B52Systems.DownwardSeats). Their deck floor is at z -2.10, just above the belly skin.
        static readonly (string name, Vector3 eye, float yaw)[] Stations =
        {
            ("EWO", new Vector3(-0.55f, 22.43f, 0.77f), 180f),
            ("RadarNav", new Vector3(-0.45f, 23.70f, -1.05f), 0f),
            ("Navigator", new Vector3(0.45f, 23.70f, -1.05f), 0f),
        };

        static void AddCrew()
        {
            var ac = ourRoot.GetComponent(T("Aircraft"));
            var pilotT = T("Pilot");
            var pilot = Find(ourRoot, "Pilot"); var wso = Find(ourRoot, "WSO");
            var eyeL = Find(ourRoot, "eye_L"); var eyeR = Find(ourRoot, "eye_R");
            if (!ac || !pilot || !wso || !eyeL || !eyeR) { Note("  ! crew: missing Aircraft, Pilot, WSO or eye points"); return; }
            // Blender -> aircraft frame from the two eye points: their difference is Blender +x.
            var right = (eyeR.position - eyeL.position).normalized;
            Vector3 ToWorld(Vector3 b) => eyeL.position + right * (b.x + 0.55f) + ourRoot.forward * (b.y - 24.31f) + ourRoot.up * (b.z - 0.77f);
            var head = Find(wso, "helmetCamPoint2");
            var headLocal = head ? Quaternion.Inverse(wso.rotation) * (head.position - wso.position) : new Vector3(0, 0.75f, 0);
            var crew = new List<Object> { pilot.GetComponent(pilotT), wso.GetComponent(pilotT) };
            foreach (var (name, eye, yaw) in Stations)
            {
                var go = Object.Instantiate(wso.gameObject, wso.parent);
                B52DonorAudit.Cloned(wso.gameObject, go);
                go.name = name;
                var rot = ourRoot.rotation * Quaternion.Euler(0, yaw, 0);
                go.transform.rotation = rot;
                go.transform.position = ToWorld(eye) - rot * headLocal;
                crew.Add(go.GetComponent(pilotT));
                Note($"  crew {name} at {ourRoot.InverseTransformPoint(go.transform.position):F2}, facing {(yaw == 0 ? "forward" : "aft")}");
            }
            // Every B-52 crew station has an ejection seat. The donor bomber's crew bail out (no seats): left like that,
            // all five climbed out of the side one by one, 1.5 s apart.
            foreach (var c in crew) Set(c, "ejectionSeat", p => p.boolValue = true);
            SetRefArray(ac, "pilots", crew);
            Note($"Crew: {crew.Count} ({string.Join(", ", crew.Select(c => ((Component)c).name))})");
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
                    B52DonorAudit.Cloned(srcEngine.gameObject, go);
                    go.name = "engine" + n;
                    go.transform.position = new Vector3(ex, inboard ? -0.6f : -0.2f, inboard ? 11.7f : 6.35f);
                    go.transform.rotation = ourRoot.rotation;
                    var tf = go.GetComponent(T("Turbofan"));
                    SetF(tf, "staticThrust", 75600f);          // F130: ~17,000 lbf
                    Set(tf, "altitudeThrust", q => q.animationCurveValue = F130Altitude());
                    Set(tf, "speedThrust", q => q.animationCurveValue = F130Speed());
                    // Exhaust nozzle at the back of the engine: drives the IR signature and heat haze (no afterburner).
                    var nz = Object.Instantiate(srcNozzle, p);
                    B52DonorAudit.Cloned(srcNozzle, nz);
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
                    // failureEffect stays: the game's turbine fire, which JetNozzle spawns when its engine fails (null threw
                    // there and cut short whatever failed the engine, e.g. a pod coming off).
                    SetRefArray(jn, "vectorTransforms", new Object[0]);
                    SetRefArray(tf, "nozzles", new Object[] { jn });
                    B52DonorAudit.Chose(tf, "nozzles");      // one exhaust nozzle per engine
                    // The pod is the engine's critical part: once the pod is down to 60 hp every further point of damage
                    // to it reaches both its engines, and a pod that comes off takes its engines with it (100 damage).
                    var podPart = p.GetComponent(T("UnitPart"));
                    Set(tf, "criticalParts", q =>
                    {
                        q.arraySize = 1;
                        var c = q.GetArrayElementAtIndex(0);
                        c.FindPropertyRelative("part").objectReferenceValue = podPart;
                        c.FindPropertyRelative("threshold").floatValue = 60f;
                        c.FindPropertyRelative("weight").floatValue = 1f;
                    });
                    // F130 core: a bigger engine than the donor's, still the softest thing in the pod.
                    SetArmor(tf, 5f, 3f, 3f, 2f);
                    SetRefArray(tf, "vectoringTransforms", new Object[0]);
                    B52DonorAudit.Chose(tf, "vectoringTransforms"); B52DonorAudit.Chose(jn, "vectorTransforms");   // fixed nozzles: nothing vectors
                    SetF(tf, "fuelConsumptionMin", 0.08f * 0.7f);
                    SetF(tf, "fuelConsumptionMax", 0.95f * 0.7f);   // CERP: ~30% better than TF33
                    var part = p.GetComponent(T("UnitPart"));
                    SetRef(tf, "part", part);
                    // Sound: every clone keeps the donor's engine whine (Turbofan.turbineAudio) and exhaust roar
                    // (JetNozzle.thrustAudio); sixteen identical loops sound hollow and far louder than a stock bomber
                    // (FastBomber1 has four engines). Keep them on the first engine of each pod, pitched a little
                    // differently per pod, and silence the second (it still runs and burns fuel, it just makes no sound).
                    int podIndex = (n - 1) / 2;
                    if (k != 0)
                    {
                        var turbine = SO(tf).FindProperty("turbineAudio").objectReferenceValue as AudioSource;
                        if (turbine) turbine.volume = 0f;
                    }
                    SpecEngine(tf, jn, n, k != 0, podIndex);
                    var cap = go.GetComponent<CapsuleCollider>();
                    // Hit volume of the engine inside its nacelle: the F130's fan case (about 1.3 m across) and core, 4.5 m long.
                    if (cap)
                    {
                        cap.direction = 2; cap.radius = 0.68f; cap.height = 4.5f; cap.center = Vector3.zero; cap.isTrigger = false;
                        foreach (var f in new[] { "m_Material", "m_IncludeLayers", "m_ExcludeLayers", "m_LayerOverridePriority", "m_IsTrigger",
                                                  "m_ProvidesContacts", "m_Radius", "m_Height", "m_Direction", "m_Center" })
                            B52DonorAudit.Chose(cap, f);
                    }
                    n++;
                }
            }
            Note("Engines: " + (n - 1) + " x Turbofan 75.6 kN");
        }

        /// <summary>
        /// B-52H gear (meshes from tools/blender_gear.py). Each LandingGear sits on gear_<k>_sprung under a fold hinge.
        /// Main trucks: hinge at the trunnion, fold 90 deg (port trucks forward, starboard aft) while the truck swivels
        /// 90 deg on its strut (LandingGear.strutRotation, on its own gear_<k>_swivel node so steering, which writes
        /// the unsprung node every frame, can't fight it) and slides in to the centreline (hingeFoldMotion), so the
        /// wheels end up lying flat side by side in a well under the fuselage, each pair at its own station. Two
        /// One large door per truck, hinged on the fuselage side, open whenever the gear is down (as on the real aircraft).
        /// Outriggers fold inboard about a fore-aft axis into the outer wing (wheel flat). Torque links are IK joints.
        /// </summary>
        static void BuildGear()
        {
            var mainDonor = Find(tmplRoot, "gear_L");
            var noseDonor = Find(tmplRoot, "gear_F");
            foreach (var k in new[] { "FL", "FR", "RL", "RR", "OL", "OR" })
            {
                var strut = Find(ourRoot, "gear_" + k);
                if (!strut) { Note("  ! gear missing " + k); continue; }
                bool outrigger = k[0] == 'O';
                float sgn = k[1] == 'R' ? 1f : -1f;
                var parent = strut.parent;
                var part = parent.GetComponentInParent(T("AeroPart")).transform;
                var unsprung = Find(strut, "gear_unsprung_" + k);
                var wheels = unsprung.Cast<Transform>().Where(t => t.name.StartsWith("wheel_")).ToList();
                float wheelR = outrigger ? 0.405f : 0.71f;
                var pivot = strut.position;

                // Outriggers fold inboard along the swept outer wing (~35 deg) and lie flat inside it. LandingGear folds
                // about the hinge's local X and measures the fold from the hinge's forward vs its parent's forward, so
                // the hinge sits unrotated under a mount turned 125 deg about Y (fold axis across the span). The fold
                // numbers are the ones tools/blender_wingwells.py cut the wing slot for (OutriggerFold etc.).
                var hingeParent = parent;
                if (outrigger) hingeParent = Child(parent, "gearMount_" + k, pivot, part.rotation * Quaternion.Euler(0f, sgn * 125f, 0f));
                var hinge = Child(hingeParent, "gearHinge_" + k, pivot);
                var gear = Child(hinge, "gear_" + k + "_sprung", pivot, part.rotation);
                var swivel = Child(gear, "gear_" + k + "_swivel", pivot, part.rotation);
                strut.SetParent(swivel, true);
                unsprung.SetParent(swivel, true);
                var bump = Child(gear, "bumpstop_" + k, pivot, part.rotation);
                var axle = Child(unsprung, "axle_" + k, unsprung.position);

                var donor = outrigger ? noseDonor : mainDonor;
                var lg = CopyComponent(donor.GetComponent(T("LandingGear")), gear.gameObject);
                CloneDonorRefs(lg, donor, unsprung);   // tyre audio, dust, skid effect, fold sounds
                var box = gear.gameObject.AddComponent<BoxCollider>();
                box.size = new Vector3(outrigger ? 0.3f : 1.0f, 0.4f, outrigger ? 0.85f : 1.5f);
                box.center = gear.InverseTransformPoint(unsprung.position);
                box.enabled = false;   // only enabled by LandingGear.BreakWheel; enabled it would block the gear's own ground ray

                float travel = (bump.position - unsprung.position).magnitude + wheelR;
                SetRef(lg, "attachedPart", part.GetComponent(T("AeroPart")));
                SetRef(lg, "gearCollider", box);
                SetRef(lg, "bumpStop", bump.gameObject);
                SetRef(lg, "unsprung", unsprung.gameObject);
                SetRef(lg, "castPoint", bump);
                SetRef(lg, "axle", axle);
                SetRef(lg, "gearHinge", hinge);
                SetRef(lg, "strutRotationTransform", swivel);
                SetRefArray(lg, "wheels", wheels.Cast<Object>().ToList());
                SetRefArray(lg, "movingParts", new Object[0]);
                Set(lg, "gearDoors", p => p.arraySize = 0);
                SetF(lg, "wheelRadius", wheelR);
                SetF(lg, "suspensionTravel", travel);
                // B-52: ~60 t per main truck at MTOW. The trucks are only 2.5 m apart, so the outriggers hold the
                // wings level: stiff enough to stop the roll, with a long stroke so they don't snap off.
                SetF(lg, "maxCompression", outrigger ? 1.2f : 0.65f);
                SetF(lg, "springRate", outrigger ? 1500000f : 3200000f);
                SetF(lg, "dampingRate", outrigger ? 300000f : 750000f);
                SetF(lg, "mass", outrigger ? 300f : 1500f);
                SetF(lg, "rollingResistance", 0.015f);          // heavily loaded bomber tyres on concrete

                if (outrigger)
                {
                    // Pick the fold sign that swings the wheel inboard, and the swivel sign that leaves its axle
                    // vertical (wheel flat) when stowed: the 0.31 m deep outer wing only takes the wheel lying flat.
                    var down = unsprung.position - pivot;
                    float fold = OutriggerFold;
                    Quaternion Folded(float deg) => hingeParent.rotation * Quaternion.Euler(deg, 0f, 0f) * Quaternion.Inverse(hingeParent.rotation);
                    if (Mathf.Abs((pivot + Folded(fold) * down).x) > Mathf.Abs(pivot.x)) fold = -fold;
                    var wheelAxle = part.right;                                    // wheel axle (along the span when down)
                    float swiv = OutriggerSwivel;
                    if (Mathf.Abs((Folded(fold) * Quaternion.AngleAxis(-swiv, swivel.up) * wheelAxle).y) >
                        Mathf.Abs((Folded(fold) * Quaternion.AngleAxis(swiv, swivel.up) * wheelAxle).y)) swiv = -swiv;
                    SetF(lg, "foldDegrees", fold);
                    SetF(lg, "strutRotation", swiv);
                    Set(lg, "hingeFoldMotion", q => q.vector3Value = new Vector3(0f, OutriggerLift, 0f));
                    Note($"  {k}: fold {fold}, swivel {swiv}, stowed axle {(Folded(fold) * Quaternion.AngleAxis(swiv, swivel.up) * wheelAxle):F2}");
                }
                else
                {
                    SetF(lg, "foldDegrees", sgn < 0 ? -90f : 90f);           // port forward, starboard aft
                    SetF(lg, "strutRotation", 90f);                            // truck swivels so the wheels lie flat
                    // tuck in to x +-0.75 and up 0.92 m (hinge-parent local): the flat wheel pair (D 1.42, 0.9 m
                    // stack) then sits inside the lower fuselage on its own side, under its own door
                    var motion = parent.InverseTransformVector(new Vector3(sgn * 0.75f - pivot.x, 0.92f, 0f));
                    Set(lg, "hingeFoldMotion", q => q.vector3Value = motion);
                }
                SetF(lg, "foldSpeed", outrigger ? 30f : 25f);                   // ~3.6 s fold for the mains
                Set(lg, "steering", p => p.boolValue = k[0] == 'F');   // the forward trucks steer (taxi)
                Set(lg, "braked", p => p.boolValue = !outrigger);
                // Truck steering for taxiing, +-45 deg: at 25 deg over the B-52's 15 m wheelbase it turned on a 32 m
                // radius, and the AI, unable to line up at the runway entrance, circled it for minutes.
                SetF(lg, "steeringLock", 45f);
                SetF(lg, "steeringSpeed", 30f);                 // the main-gear donor has 0, which locks the trucks
                SetF(lg, "aligningStrength", 2f);
                SetF(lg, "differentialBrakeFactor", 0f);        // all trucks are on the centreline; the donor's value braked every truck on any rudder input

                // Torque links: LandingGear's IK joints. Each link pivots about its lug and looks at the elbow with up
                // along the strut, so its pivot node is built in that frame.
                var lu = Find(ourRoot, "tlinkU_" + k); var ll = Find(ourRoot, "tlinkL_" + k); var le = Find(ourRoot, "tlinkE_" + k);
                if (lu && ll && le)
                {
                    var upDir = (lu.position - ll.position).normalized;
                    Transform Pivot(Transform link, Transform under)
                    {
                        var pv = Child(under, link.name + "_pivot", link.position, Quaternion.LookRotation(le.position - link.position, upDir));
                        link.SetParent(pv, true);
                        return pv;
                    }
                    var h1 = Pivot(lu, swivel); var h2 = Pivot(ll, unsprung);
                    le.SetParent(swivel, true);
                    var mid = (lu.position + ll.position) * 0.5f;
                    var side = Vector3.ProjectOnPlane(le.position - mid, upDir).normalized;
                    le.rotation = Quaternion.LookRotation(side, upDir);              // right = Cross(up, side)
                    Set(lg, "joints", p =>
                    {
                        p.arraySize = 1;
                        var e = p.GetArrayElementAtIndex(0);
                        e.FindPropertyRelative("hinge1").objectReferenceValue = h1;
                        e.FindPropertyRelative("hinge2").objectReferenceValue = h2;
                        e.FindPropertyRelative("elbow").objectReferenceValue = le;
                    });
                }
                else Set(lg, "joints", p => p.arraySize = 0);

                if (!outrigger) AddMainDoor(lg, k, part, pivot, sgn);
                else AddOutriggerDoors(lg, k, part, pivot, hinge, unsprung.position);
                Note($"Gear {k}: travel {travel:F2} m, wheel r {wheelR}, fold {(outrigger ? "inboard" : sgn < 0 ? "forward" : "aft")}");
            }
        }

        /// <summary>
        /// The main-gear door, as on the B-52H (reference photos): one large V-shaped door per truck over its well, hinged
        /// along the lower fuselage side (the chine) and hanging open, canted 35 deg outboard of the tyres, whenever the
        /// gear is down. tools/blender_wells.py cut the well opening out of the hull and kept the cut-out skin as
        /// gearDoorPanel_<k>, so the door is exactly the hole's shape, flush and in the skin's paint when shut; it also
        /// built the well box (wellBox_<k>) the truck folds into. Here the panel only gets its hinge: a pivot on the top
        /// (chine) edge, with LandingGear opening it before extending and shutting it after retracting.
        /// </summary>
        static void AddMainDoor(Component lg, string k, Transform part, Vector3 pivot, float sgn)
        {
            const float cant = 35f;
            var panel = Find(ourRoot, "gearDoorPanel_" + k);
            var mf = panel ? panel.GetComponent<MeshFilter>() : null;
            if (!mf || !mf.sharedMesh) { Note("  ! gearDoorPanel_" + k + " missing (run tools/blender_wells.py)"); return; }
            var verts = mf.sharedMesh.vertices.Select(v => panel.TransformPoint(v)).ToList();
            // hinge line: the panel's outboard (chine) edge; its free edge: the vertices nearest the centreline
            float outAbs = verts.Max(v => Mathf.Abs(v.x));
            var hingeVs = verts.Where(v => Mathf.Abs(v.x) > outAbs - 0.04f).ToList();
            var hinge = new Vector3(hingeVs.Average(v => v.x), hingeVs.Max(v => v.y), hingeVs.Average(v => v.z));
            float innerAbs = verts.Min(v => Mathf.Abs(v.x));
            var freeVs = verts.Where(v => Mathf.Abs(v.x) < innerAbs + 0.05f).ToList();
            var free = new Vector3(freeVs.Average(v => v.x), freeVs.Average(v => v.y), freeVs.Average(v => v.z));
            var pivotT = Child(part, "gearDoor_" + k, hinge, part.rotation);
            panel.SetParent(pivotT, true);
            // open angle about the door's local Z (fore-aft): free edge from its shut direction to hanging down, canted outboard
            var shut = pivotT.InverseTransformDirection(free - hinge); shut.z = 0f;
            var open = pivotT.InverseTransformDirection(new Vector3(sgn * Mathf.Sin(cant * Mathf.Deg2Rad), -Mathf.Cos(cant * Mathf.Deg2Rad), 0f)); open.z = 0f;
            float ang = Mathf.Atan2(shut.x * open.y - shut.y * open.x, shut.x * open.x + shut.y * open.y) * Mathf.Rad2Deg;
            Set(lg, "gearDoors", p =>
            {
                p.arraySize = 1;
                var e = p.GetArrayElementAtIndex(0);
                e.FindPropertyRelative("transform").objectReferenceValue = pivotT;
                e.FindPropertyRelative("closedAngle").vector3Value = Vector3.zero;
                e.FindPropertyRelative("openAngle").vector3Value = new Vector3(0f, 0f, ang);
            });
            Note($"  door {k}: hinge {hinge:F2}, free edge {free:F2}, opens {ang:F0} deg");
        }

        const float OutriggerFold = 89.75f, OutriggerSwivel = 35f, OutriggerLift = -0.025f;   // = tools/blender_wingwells.py

        /// <summary>
        /// The tip-gear doors. tools/blender_wingwells.py cut the slot the leg and wheel fold through out of the lower
        /// wing skin and kept the cut-out skin as two doors, so both are exactly the slot's shape and flush in the skin's
        /// paint when shut:
        ///   outDoorStrut_<k>_<n>  over the leg: fixed to the gear hinge, so it folds with the leg and hangs outboard of
        ///                         the oleo when the gear is down (as the real strut doors do);
        ///   outDoorWheel_<k>_<n>  over the wide wheel end: hinged on its aft edge (along the stowed leg), hanging down
        ///                         10 deg out from the slot whenever the gear is not stowed.
        /// </summary>
        static void AddOutriggerDoors(Component lg, string k, Transform part, Vector3 pivot, Transform hinge, Vector3 hub)
        {
            var all = ourRoot.GetComponentsInChildren<Transform>(true);
            var strut = all.Where(t => t.name.StartsWith("outDoorStrut_" + k + "_")).ToList();
            var wheel = all.Where(t => t.name.StartsWith("outDoorWheel_" + k + "_")).ToList();
            if (strut.Count == 0 || wheel.Count == 0) { Note("  ! outDoor panels missing for " + k + " (run tools/blender_wingwells.py)"); return; }
            var so = new SerializedObject(lg);
            float fold = so.FindProperty("foldDegrees").floatValue;
            var motion = so.FindProperty("hingeFoldMotion").vector3Value;

            // strut door: shut in the stowed pose, so attach it to the hinge while the hinge is posed stowed
            var e0 = hinge.localEulerAngles; var p0 = hinge.localPosition;
            hinge.localEulerAngles = e0 + new Vector3(fold, 0f, 0f); hinge.localPosition = p0 + motion;
            foreach (var t in strut) t.SetParent(hinge, true);
            hinge.localEulerAngles = e0; hinge.localPosition = p0;

            // wheel door: hinge line along the stowed leg on the slot's aft edge
            var hp = hinge.parent;
            var d = Vector3.ProjectOnPlane(hp.rotation * Quaternion.Euler(fold, 0f, 0f) * Quaternion.Inverse(hp.rotation) * (hub - pivot), Vector3.up).normalized;
            var aft = Vector3.Cross(Vector3.up, d); if (aft.z > 0f) aft = -aft;
            var verts = wheel.SelectMany(t => t.GetComponent<MeshFilter>().sharedMesh.vertices.Select(v => t.TransformPoint(v))).ToList();
            float aMax = verts.Max(v => Vector3.Dot(v - pivot, aft)), aMin = verts.Min(v => Vector3.Dot(v - pivot, aft));
            var hingeVs = verts.Where(v => Vector3.Dot(v - pivot, aft) > aMax - 0.04f).ToList();
            var freeVs = verts.Where(v => Vector3.Dot(v - pivot, aft) < aMin + 0.04f).ToList();
            var hingePt = hingeVs.OrderBy(v => Vector3.Dot(v - pivot, d)).First();
            hingePt.y = hingeVs.Max(v => v.y);
            var free = new Vector3(freeVs.Average(v => v.x), freeVs.Average(v => v.y), freeVs.Average(v => v.z));
            // LandingGear animates the door's localEulerAngles from zero, so the hinge-line frame is a parent mount
            var mount = Child(part, "gearDoorMount_" + k, hingePt, Quaternion.LookRotation(d, Vector3.up));
            var door = Child(mount, "gearDoor_" + k, hingePt, mount.rotation);
            foreach (var t in wheel) t.SetParent(door, true);
            var shut = mount.InverseTransformDirection(free - hingePt); shut.z = 0f;
            const float cant = 10f;
            var open = mount.InverseTransformDirection(-Vector3.up * Mathf.Cos(cant * Mathf.Deg2Rad) + aft * Mathf.Sin(cant * Mathf.Deg2Rad)); open.z = 0f;
            float ang = Mathf.Atan2(shut.x * open.y - shut.y * open.x, shut.x * open.x + shut.y * open.y) * Mathf.Rad2Deg;
            Set(lg, "gearDoors", p =>
            {
                p.arraySize = 1;
                var e = p.GetArrayElementAtIndex(0);
                e.FindPropertyRelative("transform").objectReferenceValue = door;
                e.FindPropertyRelative("closedAngle").vector3Value = Vector3.zero;
                e.FindPropertyRelative("openAngle").vector3Value = new Vector3(0f, 0f, ang);
            });
            Note($"  doors {k}: {strut.Count} strut pieces on the hinge; wheel door hinge {hingePt:F2} along {d:F2}, opens {ang:F0} deg");
        }

        static List<(Vector3 a, Vector3 b, Vector3 c, Vector2 ua, Vector2 ub, Vector2 uc)> bellyTris;

        /// <summary>Lowest point of the fuselage skin straight below/above (x, z) and its atlas UV (the belly).
        /// Only skin below `below` counts.</summary>
        static bool BellyHit(float x, float z, float below, out float y, out Vector2 uv)
        {
            if (bellyTris == null)
            {
                bellyTris = new List<(Vector3, Vector3, Vector3, Vector2, Vector2, Vector2)>();
                foreach (var name in new[] { "B52", "fuselage_F", "fuselage_R", "cockpit" })
                {
                    var t = name == "B52" ? ourRoot : Find(ourRoot, name);
                    var mf = t ? t.GetComponent<MeshFilter>() : null;
                    if (!mf || !mf.sharedMesh) continue;
                    var m = mf.sharedMesh; var v = m.vertices; var u = m.uv; var tr = m.triangles;
                    if (u.Length != v.Length) continue;
                    for (int i = 0; i < tr.Length; i += 3)
                        bellyTris.Add((t.TransformPoint(v[tr[i]]), t.TransformPoint(v[tr[i + 1]]), t.TransformPoint(v[tr[i + 2]]),
                                       u[tr[i]], u[tr[i + 1]], u[tr[i + 2]]));
                }
            }
            y = float.MaxValue; uv = Vector2.zero;
            foreach (var (a, b, c, ua, ub, uc) in bellyTris)
            {
                // barycentric in the XZ plane
                float d = (b.z - c.z) * (a.x - c.x) + (c.x - b.x) * (a.z - c.z);
                if (Mathf.Abs(d) < 1e-9f) continue;
                float l1 = ((b.z - c.z) * (x - c.x) + (c.x - b.x) * (z - c.z)) / d;
                float l2 = ((c.z - a.z) * (x - c.x) + (a.x - c.x) * (z - c.z)) / d;
                float l3 = 1f - l1 - l2;
                if (l1 < -1e-4f || l2 < -1e-4f || l3 < -1e-4f) continue;
                float hy = l1 * a.y + l2 * b.y + l3 * c.y;
                if (hy < below && hy < y) { y = hy; uv = l1 * ua + l2 * ub + l3 * uc; }
            }
            return y != float.MaxValue;
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
                    // The aero surface stays hidden (it swings both ways); B52Systems.SpoilerDriver raises the seven
                    // panels (spoilerPanels_<side><n>, separate objects in the FBX, each hinged on its own front edge)
                    // only on the wing whose spoilers go up. They are cut from the wing's skin, so they are always
                    // shown: lying flat they are the wing's top surface.
                    var vr2 = vis.GetComponent<MeshRenderer>();
                    if (vr2) vr2.enabled = false;
                    int nPanels = 0;
                    foreach (Transform c in t)
                        if (c.name.StartsWith("spoilerPanels_")) { var r = c.GetComponent<MeshRenderer>(); if (r) r.enabled = true; nPanels++; }
                    if (nPanels != 7) Note($"  ! {name}: {nPanels} spoiler panels in the FBX, expected 7 (run tools/blender_prep.py)");
                }
                SetRef(cs, "attachedSurface", t.GetComponent(T("UnitPart")));
                SetF(cs, "pitchRange", pitch); SetF(cs, "rollRange", roll); SetF(cs, "yawRange", yaw);
                SetF(cs, "brakeRange", 0); SetF(cs, "maxSplit", 0);
                SetF(cs, "servoSpeed", name.StartsWith("spoiler") ? 60f : 30f);
                SetRef(t.GetComponent(T("AeroPart")), "liftNormal", vis);
            }
            // Moving horizontal stabilizer (the real B-52 trims the whole tailplane): each hstab gets a pitch-only
            // ControlSurface at 0.4x the elevator angle. It turns an empty lift frame, not the mesh, so the elevator
            // hinge stays put visually. Lets a 221 t B-52 rotate from ~159 kt (tools/trim_check.py, STAB_RATIO).
            var elevCS = Find(ourRoot, "elevator_L").GetComponent(T("ControlSurface"));
            foreach (var sd in new[] { "L", "R" })
            {
                var hs = Find(ourRoot, "hstab_" + sd);
                if (!hs || !elevCS) continue;
                var trimFrame = Child(hs, "hstab_" + sd + "_trim", hs.position, hs.rotation);
                var cs = CopyComponent(elevCS, hs.gameObject);
                Remap.Remove(elevCS);          // our own elevator, not a template object: references to it must stay
                SetRef(cs, "visibleMesh", trimFrame.gameObject);
                SetRef(cs, "attachedSurface", hs.GetComponent(T("UnitPart")));
                SetF(cs, "pitchRange", -8f); SetF(cs, "rollRange", 0); SetF(cs, "yawRange", 0);
                SetF(cs, "brakeRange", 0); SetF(cs, "maxSplit", 0);
                SetF(cs, "servoSpeed", 12f);
                SetRef(hs.GetComponent(T("AeroPart")), "liftNormal", trimFrame);
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
                // Camber the wing section aft of the CoM-ish line that each flap really sits on. The wing root's
                // aerodynamic centre is 6 m ahead of the CoM, so cambering it pitched the nose up (tools/trim_check.py).
                var wingOf = f.StartsWith("flap1") ? "wing1" + f.Substring(5) : "wing2" + f.Substring(5);
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
        static AnimationCurve F130Speed() => Linear(0, 1.0f, 50, 0.88f, 100, 0.77f, 150, 0.73f, 200, 0.70f, 250, 0.69f, 280, 0.66f, 300, 0.58f, 340, 0.30f);

        /// <summary>The ControlsFilter fly-by-wire came from FastBomber1 with fighter gains: 8 g, a 6 rad/s roll-rate demand
        /// (the B-52 rolls ~0.19 rad/s, so any stick slammed the spoilers fully over and it swung left and right), and a
        /// weak roll-rate gain. Bomber values: ~20 deg/s roll demand with real rate feedback, 2.5 g, gentler pitch loop.</summary>
        static void TuneFlyByWire()
        {
            var all = ourRoot.GetComponentsInChildren(T("ControlsFilter"), true);
            if (all.Length == 0) { Note("  ! no ControlsFilter"); return; }
            foreach (var cf in all) Set(cf, "flyByWire", f =>
            {
                void F(string n, float v) { var q = f.FindPropertyRelative(n); if (q != null) q.floatValue = v; else Note("  ! flyByWire." + n); }
                F("gLimitPositive", 2.5f);
                F("maxPitchAngularVel", 0.15f);
                F("takeoffSpeed", 80f);
                F("alphaLimiter", 10f);
                F("alphaLimiterStrength", 0.15f);
                F("pFactorFast", 4f);
                F("iFactor", 0.1f);
                F("dFactorFast", 1f);
                F("maxRollAngularVel", 0.35f);
                F("rollTightness", 4f);
                F("rollTrimRate", 0.02f);
                F("rollTrimLimit", 0.05f);
                F("yawTightness", 2f);
            });
            Note($"Fly-by-wire tuned for a bomber ({all.Length} filter)");
        }

        /// <summary>Nuclear Option's airbrake (Airbrake component) opens whenever the throttle is at zero and adds a drag
        /// force dragAmount * rho * V^2. FastBomber1 has none, so idle did nothing. The B-52 uses its 14 spoiler panels as
        /// speedbrakes: CdA ~16 m2 -> dragAmount 8 (~200 kN at 300 kt). No transforms: B52Systems.SpoilerDriver raises
        /// the visible spoiler panels from the airbrake's open amount.</summary>
        static void AddAirbrake()
        {
            var ab = ourRoot.gameObject.AddComponent(T("Airbrake"));
            var so = SO(ab);
            so.FindProperty("transforms").arraySize = 0;
            so.FindProperty("constraints").arraySize = 0;
            so.FindProperty("dragAmount").floatValue = 8f;
            so.FindProperty("maxAngle").floatValue = 60f;
            so.FindProperty("openSpeed").floatValue = 0.7f;
            so.FindProperty("part").objectReferenceValue = ourRoot.GetComponent(T("UnitPart"));
            so.FindProperty("aircraft").objectReferenceValue = ourRoot.GetComponent(T("Aircraft"));
            so.FindProperty("volumeMultiplier").floatValue = 0.7f;
            so.ApplyModifiedPropertiesWithoutUndo();
            Note("Airbrake: dragAmount 8, opens at idle");
        }

        /// <summary>The root's RadarJammer came from FastBomber1 with the stock bombers' settings (the Darkreach has the same:
        /// intensity 8, capacitance 100). The B-52 carries the heaviest ECM suite of any bomber (ALQ-155/-172), so it jams
        /// a little harder and a little longer before its capacitor runs down.</summary>
        static void TuneJammer()
        {
            var jam = ourRoot.GetComponent(T("RadarJammer"));
            if (!jam) { Note("  ! no RadarJammer on the root"); return; }
            SetF(jam, "jammingIntensity", 10f);
            SetF(jam, "capacitance", 140f);
            Note("Jammer: intensity 10, capacitance 140 (stock bombers 8 / 100)");
        }

        static void WireCockpit()
        {
            var ci = Find(ourRoot, "cockpit_int");
            var src = Find(tmplRoot, "cockpit_int").GetComponent(T("Cockpit"));
            var ck = CopyComponent(src, ci.gameObject);
            MapGO(Find(tmplRoot, "cockpit_int"), ci);
            B52DonorAudit.Chose(ck, "joysticks"); B52DonorAudit.Chose(ck, "throttles");   // the two yokes and eight throttle levers, set below
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
            if (tac && scr)
            {
                tac.SetParent(ci, true); tac.position = scr.position; tac.rotation = scr.rotation;
                // The donor's own screen mesh is 1.5 m wide and offset from its pivot: moved here it hung through the
                // glareshield and the nose (seen in cockpit view, layer Cockpit). The live map draws on mfd_C_screen
                // (tacScreenRender), so only the object stays, as an anchor. Removed rather than disabled: the game
                // switches every interior renderer back on in cockpit view.
                var tacMesh = tac.GetComponent<MeshRenderer>();
                if (tacMesh) Object.DestroyImmediate(tacMesh);
                var tacFilter = tac.GetComponent<MeshFilter>();
                if (tacFilter) Object.DestroyImmediate(tacFilter);
            }

            // Aircraft renderer lists: interior vs exterior. In cockpit view the game hides every exterior renderer
            // (Aircraft.SetCockpitRenderers), so this lists only the cockpit section's own outer skin, which would cover
            // the windows from inside. Listing every renderer hid the wings and engines from the cockpit.
            var ac = ourRoot.GetComponent(T("Aircraft"));
            var cockpitPart = Find(ourRoot, "cockpit").GetComponent(T("UnitPart"));
            // The game turns cockpitRenderers off outside cockpit view, so listing the interior there left the windows
            // showing an empty, black cabin from outside. The flight deck stays on in every view (the far LOD drops it),
            // so other players and the external camera see the real panels, seats and crew through the glass.
            var interior = new List<Object>();
            var exterior = ourRoot.GetComponentsInChildren<Renderer>(true).Where(r => !r.transform.IsChildOf(ci)
                && r.GetComponentInParent(T("UnitPart")) == cockpitPart
                && !(r is SkinnedMeshRenderer) && !(r is ParticleSystemRenderer)).Cast<Object>().ToList();
            SetRefArray(ac, "cockpitRenderers", interior);
            SetRefArray(ac, "exteriorRenderers", exterior);
            SetRef(ac, "cockpit", cockpitPart);
            SetRef(ac, "weaponManager", Find(ourRoot, "cockpit").GetComponent(T("WeaponManager")));
            SetRefArray(ac, "canopies", new Object[0]);
            Note($"Cockpit: {interior.Count} interior / {exterior.Count} exterior renderers");

            // The game draws cockpits with a second camera that only renders its cockpit layers and has a near plane
            // of a few centimetres; the main camera's is much further out. Our interior came in from the FBX on
            // Default, so only the main camera drew it: from the seat the walls, roof and side-window frames (less
            // than a metre away) were cut away. The stock bomber's interior root is on CockpitAndExternal (14),
            // which the cockpit camera draws in cockpit view and the main camera from outside, through the windows.
            const int CockpitAndExternal = 14;
            int moved = 0;
            foreach (var t in ci.GetComponentsInChildren<Transform>(true))
                if (t.gameObject.layer == 0) { t.gameObject.layer = CockpitAndExternal; moved++; }
            Note($"Cockpit: {moved} interior objects moved to the CockpitAndExternal layer");
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
                if (kv.Value.fuel > 0)
                {
                    BuildTank(t, ft ? ft : Find(tmplRoot, "wing2_L").GetComponent(T("FuelTank")), kv.Value.fuel);
                    totalFuel += kv.Value.fuel;
                }
                if (ft) Object.DestroyImmediate(ft);
            }
            Note($"Physics: structure {totalMass:F0} kg, fuel {totalFuel:F0} kg, wing area {totalArea:F1} m2");

            // Tanks feed through the part inboard of them: a part that comes off tears the line into the next tank in.
            foreach (var kv in TankFeeds)
            {
                var a = Find(ourRoot, "tank_" + kv.Key)?.GetComponent(T("FuelTank"));
                var b = Find(ourRoot, "tank_" + kv.Value)?.GetComponent(T("FuelTank"));
                if (a && b) SetRefArray(a, "connectedTanks", new Object[] { b });
                else Note($"  ! no tank pair {kv.Key} -> {kv.Value}");
            }


            foreach (var kv in Armour)
            {
                var t = Find(ourRoot, kv.Key); if (!t) { Note("  ! no part for armour " + kv.Key); continue; }
                var up = t.GetComponent(T("UnitPart"));
                var a = kv.Value;
                SetArmor(up, a.pierceArmor, a.pierceTol, a.blastTol, a.fireTol);
                SetF(up, "structuralThreshold", a.threshold);
            }
            Note($"Armour: {Armour.Count} parts");
        }

        // Damage resistance per part, from the real structure. The donor bomber (a 30 t aircraft) gives every part
        // pierce armour 20, pierce tolerance 5 and blast tolerance 6. B-52 structure is two to five times heavier part for
        // part; tolerances scale with skin and spar size (about the square root of the mass ratio), highest for the wing
        // box and centre section, lowest for control surfaces. The structural threshold is the hit points at which a
        // part's root has no strength left (B52Systems.Structure): the deep wing-box and fuselage frames keep carrying
        // load far into the negative, a control surface comes off soon after it is shot through.
        // The game applies pierce armour as a deduction and the tolerances as divisors (blast armour is not used).
        static readonly Dictionary<string, (float pierceArmor, float pierceTol, float blastTol, float fireTol, float threshold)> Armour =
            new Dictionary<string, (float, float, float, float, float)>
            {
                { "B52",        (25, 10, 12, 2, -100) },   // centre wing box, bomb bay, main gear: the keel of the aircraft
                { "wingroot_L", (25, 9, 10, 2, -50) },    { "wingroot_R", (25, 9, 10, 2, -50) },
                { "wing1_L",    (22, 8, 9, 1.5f, -40) },  { "wing1_R", (22, 8, 9, 1.5f, -40) },
                { "wing2_L",    (20, 7, 8, 1.5f, -30) },  { "wing2_R", (20, 7, 8, 1.5f, -30) },
                { "wingtip_L",  (20, 6, 7, 1, -25) },     { "wingtip_R", (20, 6, 7, 1, -25) },
                { "fuselage_F", (20, 8, 10, 1.5f, -60) },
                { "cockpit",    (20, 7, 8, 1.5f, -40) },
                { "fuselage_R", (20, 8, 9, 1.5f, -60) },
                { "tail",       (20, 6, 6, 1, -25) },
                { "hstab_L",    (20, 6, 6, 1, -25) },     { "hstab_R", (20, 6, 6, 1, -25) },
                { "rudder",     (15, 4, 4, 1, -25) },
                { "elevator_L", (15, 4, 4, 1, -25) },     { "elevator_R", (15, 4, 4, 1, -25) },
                { "flap1_L",    (15, 5, 5, 1, -25) },     { "flap1_R", (15, 5, 5, 1, -25) },
                { "flap2_L",    (15, 5, 5, 1, -25) },     { "flap2_R", (15, 5, 5, 1, -25) },
                { "spoilers_L", (15, 4, 4, 1, -25) },     { "spoilers_R", (15, 4, 4, 1, -25) },
                { "pod1_L",     (20, 6, 8, 1, -50) },     { "pod1_R", (20, 6, 8, 1, -50) },   // strut and cowl; the engines
                { "pod2_L",     (20, 6, 8, 1, -50) },     { "pod2_R", (20, 6, 8, 1, -50) },   // inside have their own
            };

        // Which tank each tank feeds into (and whose line tears when its part comes off).
        static readonly Dictionary<string, string> TankFeeds = new Dictionary<string, string>
        {
            { "wingtip_L", "wing2_L" }, { "wing2_L", "wing1_L" }, { "wing1_L", "wingroot_L" }, { "wingroot_L", "B52" },
            { "wingtip_R", "wing2_R" }, { "wing2_R", "wing1_R" }, { "wing1_R", "wingroot_R" }, { "wingroot_R", "B52" },
            { "fuselage_F", "B52" }, { "fuselage_R", "B52" },
        };

        // A fuel tank of its own at the centroid of the fuel, so leaks, fires and fireballs start there and a tank fire
        // burns what is around the tank. The game finds a part's tank on the part itself for its fire / rupture message;
        // B52Systems.FuelTanks routes that message to the child tank, and passes crash impacts on to it.
        static void BuildTank(Transform part, Component donor, float capacity)
        {
            var at = part.name == "B52" ? CentreWingTank() : part.name.StartsWith("wing") ? WingTankCentre(part) : MeshCentre(part);
            var go = Child(part, "tank_" + part.name, at ?? part.position);
            var ft = CopyComponent(donor, go.gameObject);
            SetF(ft, "fuelCapacity", capacity);
            SetRef(ft, "part", part.GetComponent(T("UnitPart")));
            SetRefArray(ft, "connectedTanks", new Object[0]);
            B52DonorAudit.Chose(ft, "connectedTanks");   // set below for every tank but the centre one, which feeds nothing further in
            // Sizes from the tank, rates from the hole. A tank torn open empties in about a minute. Integral wing tanks
            // (the skin is the tank wall) leak from the first serious hit; the body tanks are bladders inside the
            // fuselage. With the part at its structural threshold, the holes leak 60% as fast as an open tear.
            bool wing = part.name.StartsWith("wing");
            float maxLeak = Mathf.Max(30f, capacity / 60f);
            float leakAt = wing ? 75f : 55f;
            float threshold = Armour.TryGetValue(part.name, out var a) ? a.threshold : -50f;
            SetF(ft, "maxLeakRate", maxLeak);
            SetF(ft, "leakThreshold", leakAt);
            SetF(ft, "leakPerHP", 0.6f * maxLeak / (leakAt - threshold));
            SetF(ft, "fireIntensity", 1.5f + 1.5f * Mathf.Clamp01(capacity / 17000f));
            Note($"Tank {part.name}: {capacity:F0} kg at {ourRoot.InverseTransformPoint(go.position):F2}, leaks below {leakAt} hp, " +
                 $"max {maxLeak:F0} kg/s");
        }

        static Vector3[] WorldVerts(Transform t)
        {
            var mf = t.GetComponent<MeshFilter>();
            return mf && mf.sharedMesh ? mf.sharedMesh.vertices.Select(v => t.TransformPoint(v)).ToArray() : new Vector3[0];
        }

        static Vector3? MeshCentre(Transform t)
        {
            var v = WorldVerts(t);
            if (v.Length == 0) return null;
            var b = new Bounds(v[0], Vector3.zero);
            foreach (var p in v) b.Encapsulate(p);
            return b.center;
        }

        // Wing tanks fill the box between the spars, so the fuel sits where the chord (and depth) is biggest: strips
        // along the span weighted by chord squared, at 35% chord and mid depth.
        static Vector3? WingTankCentre(Transform t)
        {
            var v = WorldVerts(t);
            if (v.Length == 0) return null;
            float x0 = v.Min(p => p.x), x1 = v.Max(p => p.x);
            const int N = 12;
            Vector3 sum = Vector3.zero; float w = 0;
            for (int i = 0; i < N; i++)
            {
                float a = x0 + (x1 - x0) * i / N, b = x0 + (x1 - x0) * (i + 1) / N;
                var s = v.Where(p => p.x >= a && p.x <= b).ToArray();
                if (s.Length < 4) continue;
                float le = s.Max(p => p.z), te = s.Min(p => p.z), c = le - te;
                var at = new Vector3((a + b) / 2, (s.Min(p => p.y) + s.Max(p => p.y)) / 2, le - 0.35f * c);
                sum += at * c * c; w += c * c;
            }
            return w > 0 ? sum / w : MeshCentre(t);
        }

        // The centre wing tank is the wing box through the fuselage: on the centreline, in line with the wing roots' tanks.
        static Vector3? CentreWingTank()
        {
            var l = WingTankCentre(Find(ourRoot, "wingroot_L")); var r = WingTankCentre(Find(ourRoot, "wingroot_R"));
            if (!l.HasValue || !r.HasValue) return null;
            var c = (l.Value + r.Value) / 2;
            return new Vector3(ourRoot.position.x, c.y, c.z);
        }

        static void SetArmor(Object o, float pierceArmor, float pierceTol, float blastTol, float fireTol)
        {
            SetF(o, "armorProperties.pierceArmor", pierceArmor);
            SetF(o, "armorProperties.pierceTolerance", pierceTol);
            SetF(o, "armorProperties.blastTolerance", blastTol);
            SetF(o, "armorProperties.fireTolerance", fireTol);
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
                var ft = Find(ap.transform, "tank_" + ap.name)?.GetComponent(T("FuelTank"));
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
        // Liveries after the USAF one: asset name, texture (made by the build: tools/make_clean_livery.py, make_liveries.py),
        // name in the game. One missing from Textures is left out.
        // faction: offered only to that side (its Faction asset's name), or to every side when null
        static readonly (string asset, string file, string name, string faction)[] ExtraLiveries =
        {
            ("B52_USAF_clean_livery", "B52_atlas_clean.png", "USAF Gunship Gray, clean", null),
            ("B52_SEA_livery", "B52_livery_sea.png", "Southeast Asia camouflage, black belly", null),
            ("B52_SIOP_livery", "B52_livery_siop.png", "Southeast Asia camouflage, white belly", null),
            ("B52_Boscali_livery", "B52_livery_boscali.png", "Boscali blue-grey", "Boscali"),
            ("B52_Primeva_livery", "B52_livery_primeva.png", "Primeva dark splinter", "Primeva"),
        };
        const float CockpitFill = 0.25f;  // interior paint glow, x its colour: 0 left the frames black, 0.5 washed it flat (tests/ingame/b52_cockpit_fill.txt)

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
            // The skin's own maps: baked contact shadows and a scorched damage texture (normals stay flat).
            var skinAO = AssetDatabase.LoadAssetAtPath<Texture2D>(ModDir + "/Textures/B52_atlas_ao.png");
            var skinDmg = AssetDatabase.LoadAssetAtPath<Texture2D>(ModDir + "/Textures/B52_atlas_dmg.png");
            var skinDmgN = AssetDatabase.LoadAssetAtPath<Texture2D>(ModDir + "/Textures/B52_atlas_dmg_n.png");
            var skinMS = AssetDatabase.LoadAssetAtPath<Texture2D>(ModDir + "/Textures/B52_atlas_ms.png");
            Note($"Skin maps: AO {(skinAO ? "yes" : "MISSING")}, damage {(skinDmg ? "yes" : "MISSING")}, dents {(skinDmgN ? "yes" : "MISSING")}, metal/smoothness {(skinMS ? "yes" : "MISSING")}");
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
                        bool isSkin = src.name == "B52_Skin";
                        m.SetTexture("_Basecolor", tex); m.SetTexture("_Livery", tex);
                        m.SetTexture("_BasecolorDmg", isSkin && skinDmg ? skinDmg : tex);
                        m.SetTexture("_Normal", flatNormal); m.SetTexture("_NormalDmg", isSkin && skinDmgN ? skinDmgN : flatNormal);
                        // The skin shader reads metalness from R and smoothness from A of _Metallic. A plain black map
                        // (alpha 1) made the whole aircraft mirror-smooth. FastBomber1's map averages R 0.08, A 0.46:
                        // painted skin gets about that, bare gear metal more, dark interiors and rubber less.
                        Texture2D ms;
                        if (src.name.Contains("Chrome")) ms = SolidTex("ms_chrome", new Color(0.9f, 0.9f, 0.9f, 0.7f), true);
                        else if (src.name.Contains("GearMetal") || src.name.Contains("EngineMetal")) ms = SolidTex("ms_metal", new Color(0.6f, 0.6f, 0.6f, 0.45f), true);
                        else if (isSkin) ms = skinMS ? skinMS : SolidTex("ms_paint", new Color(0.05f, 0.05f, 0.05f, 0.20f), true);   // tools/make_skin_detail.py: flat weathered paint
                        else ms = SolidTex("ms_matte", new Color(0.02f, 0.02f, 0.02f, 0.16f), true);
                        m.SetTexture("_Metallic", ms); m.SetTexture("_AO", isSkin && skinAO ? skinAO : white);
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
                    else
                    {
                        // Soft fill: the game exposes for the bright outside, so the flight deck went nearly black. A faint
                        // glow of each paint's own colour stands in for light through the windows, at no cost per frame.
                        m.EnableKeyword("_EMISSION");
                        if (src.GetTexture("_BaseMap")) m.SetTexture("_EmissionMap", src.GetTexture("_BaseMap"));
                        m.SetColor("_EmissionColor", src.GetColor("_BaseColor") * CockpitFill);
                        m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
                    }
                    if (src.name == "CP_Atlas" || src.name == "CP_Dials")
                    {
                        // The panel art (tools/make_cockpit_atlas.py) at full size: its lettering is a few pixels high.
                        var atlasPath = ModDir + "/Textures/" + (src.name == "CP_Atlas" ? "cp_atlas.png" : "cp_dials.png");
                        if (AssetImporter.GetAtPath(atlasPath) is TextureImporter ai && (ai.maxTextureSize < 4096 || ai.mipmapEnabled == false))
                        { ai.maxTextureSize = 4096; ai.mipmapEnabled = true; ai.anisoLevel = 8; ai.SaveAndReimport(); }
                        m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(atlasPath));
                        m.SetTexture("_EmissionMap", AssetDatabase.LoadAssetAtPath<Texture2D>(atlasPath));
                        if (src.name == "CP_Atlas")
                        {
                            // its relief: raised sub-panels, sunken gaps, fasteners, nuts and knob skirts
                            var nPath = ModDir + "/Textures/cp_atlas_n.png";
                            if (AssetImporter.GetAtPath(nPath) is TextureImporter ni && (ni.textureType != TextureImporterType.NormalMap || ni.maxTextureSize < 4096))
                            { ni.textureType = TextureImporterType.NormalMap; ni.maxTextureSize = 4096; ni.SaveAndReimport(); }
                            var nTex = AssetDatabase.LoadAssetAtPath<Texture2D>(nPath);
                            if (nTex) { m.SetTexture("_BumpMap", nTex); m.SetFloat("_BumpScale", 1f); m.EnableKeyword("_NORMALMAP"); }
                        }
                    }
                    if (src.name == "CP_Flat")                                   // metal and smoothness per colour, from its palette
                    {
                        m.SetTexture("_MetallicGlossMap", AssetDatabase.LoadAssetAtPath<Texture2D>(ModDir + "/Textures/cp_palette_ms.png"));
                        m.EnableKeyword("_METALLICSPECGLOSSMAP");
                        m.SetFloat("_Metallic", 1f); m.SetFloat("_Smoothness", 1f); m.SetFloat("_SmoothnessTextureChannel", 0f);
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

        // The B-52H's M61A1 barrels (tools/blender_tailgun.py split them out of the tail cone): off the aircraft, kept for
        // the optional tail turret mount (B52Weapons.MakeTailTurret), so the B-52J flies without a gun unless it's loaded.
        static Mesh tailGunMesh; static Vector3 tailGunPos; static Quaternion tailGunRot; static Vector3 tailGunScale;
        static int hardpointSetCount = 2;

        static void TakeTailGun()
        {
            tailGunMesh = null;
            var t = Find(ourRoot, "tailgun");
            var mf = t ? t.GetComponent<MeshFilter>() : null;
            if (!mf || !mf.sharedMesh) { Note("  ! tailgun not in the FBX (run tools/blender_tailgun.py): no tail turret station"); return; }
            tailGunMesh = mf.sharedMesh;
            tailGunPos = ourRoot.InverseTransformPoint(t.position);
            tailGunRot = Quaternion.Inverse(ourRoot.rotation) * t.rotation;
            tailGunScale = t.lossyScale;
            Object.DestroyImmediate(t.gameObject);
            Note($"Tail gun: barrels taken off the hull for the tail turret mount, pivot {tailGunPos:F3}");
        }

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
            var pylons = new List<(Transform mount, Renderer r, Object part)>();
            foreach (var sd in new[] { "L", "R" })
            {
                var wr = Find(ourRoot, "wingroot_" + sd);
                var wb = wr.GetComponent<Renderer>().bounds;
                float x = (sd == "L" ? -1 : 1) * 5.6f;
                Renderer pr; Vector3 mountPos;
                var modelled = Find(ourRoot, "hsabPylon_" + sd);
                if (modelled && modelled.GetComponent<Renderer>())
                {
                    // the modelled pylon and adapter beam (tools/blender_pylons.py): the rack hangs just under the beam
                    pr = modelled.GetComponent<Renderer>();
                    var pb = pr.bounds;
                    mountPos = new Vector3(pb.center.x, pb.min.y - 0.05f, pb.center.z);
                }
                else
                    // A missing pylon means tools/blender_pylons.py didn't run or failed: stop rather than ship the grey
                    // boxes that used to stand in for it.
                    throw new Exception("hsabPylon_" + sd + " is not in B52.fbx: run tools/blender_pylons.py (bash tools/build_all.sh)");
                pr.enabled = false;                                    // shown by the game while a pylon weapon is loaded
                var mt = Child(wr, "pylonMount_" + sd, mountPos);
                pylons.Add((mt, pr, wr.GetComponent(T("UnitPart"))));
            }

            // Tail turret (optional, B-52H): the mount faces aft from the barrels' pivot, on the rear fuselage.
            ScriptableObject tailMount = null; Transform tailPoint = null;
            if (tailGunMesh)
            {
                var hpRot = ourRoot.rotation * Quaternion.Euler(0f, 180f, 0f);
                tailPoint = Child(Find(ourRoot, "fuselage_R"), "tailTurretMount", ourRoot.TransformPoint(tailGunPos), hpRot);
                tailMount = B52Weapons.MakeTailTurret(tailGunMesh, Quaternion.Inverse(hpRot) * (ourRoot.rotation * tailGunRot), tailGunScale, Note);
            }
            hardpointSetCount = tailMount ? 3 : 2;

            B52DonorAudit.Chose(wm, "hardpointSets");      // every station written here: the bay, the two wing pylons and the tail turret
            Set(wm, "hardpointSets", p =>
            {
                p.arraySize = hardpointSetCount;
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

                if (!tailMount) return;
                var s2 = p.GetArrayElementAtIndex(2);
                Fill(s2, "Tail Turret (B-52H)", new List<ScriptableObject> { tailMount });
                var h2 = s2.FindPropertyRelative("hardpoints"); h2.arraySize = 1;
                Point(h2.GetArrayElementAtIndex(0), tailPoint, Find(ourRoot, "fuselage_R").GetComponent(T("UnitPart")), new Object[0], null);
            });
            Note($"Hardpoints: bay {BayMounts.Count} options (doors {doors.Count}), pylons {PylonMounts.Count} options, tail turret {(tailMount ? "yes" : "NO")}");
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
                    if (v is ParticleSystem)
                    {
                        // Adding the ParticleSystem also added a blank renderer, with no material: the tyre dust drew
                        // as pink squares. Copy the donor's renderer onto it.
                        var srcR = go.GetComponent<ParticleSystemRenderer>(); var dstR = holder.GetComponent<ParticleSystemRenderer>();
                        if (srcR && dstR) { EditorUtility.CopySerialized(srcR, dstR); B52DonorAudit.Copied(srcR, dstR); }
                        else if (srcR) CopyComponent(srcR, holder);
                    }
                }
                else if (v is GameObject && !go.GetComponentInChildren<MeshRenderer>())
                {
                    if (!clones.TryGetValue(go, out var copy))
                    {
                        copy = Object.Instantiate(go, newParent); copy.name = go.name; copy.transform.localPosition = Vector3.zero;
                        B52DonorAudit.Cloned(go, copy);
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

        /// <summary>Two levels, like the stock aircraft: everything up close, and once the B-52 is under 5 % of the
        /// screen height (about 1.2 km away) only the decimated <part>_LoD copies (tools/blender_lods.py), which leave
        /// out the gear, wheels and cockpit. Particle effects stay outside the group.</summary>
        static void BuildLods()
        {
            var all = ourRoot.GetComponentsInChildren<Renderer>(true).Where(r => !(r is ParticleSystemRenderer)).ToList();
            var low = all.Where(r => r.name.EndsWith("_LoD")).ToArray();
            if (low.Length == 0) { Note("  ! no _LoD meshes (run tools/blender_lods.py): no LOD group"); return; }
            var high = all.Where(r => !r.name.EndsWith("_LoD")).ToArray();
            // the nozzle liners (tools/blender_fans.py) are a few hundred triangles: keep them far away too, or the
            // distant engines show the sky through them
            low = low.Concat(high.Where(r => r.name.StartsWith("nozzle_"))).ToArray();
            var lod = ourRoot.GetComponent<LODGroup>();                 // not ??: a missing component isn't C# null
            if (!lod) lod = ourRoot.gameObject.AddComponent<LODGroup>();
            lod.SetLODs(new[] { new LOD(0.05f, high), new LOD(0f, low) });
            lod.RecalculateBounds();
            int Tris(IEnumerable<Renderer> rs) => rs.Select(r => r.GetComponent<MeshFilter>()).Where(f => f && f.sharedMesh)
                                                    .Sum(f => (int)f.sharedMesh.triangles.LongLength / 3);
            Note($"LODs: {high.Length} renderers ({Tris(high)} triangles) up close, {low.Length} ({Tris(low)}) from 5 % of the screen");
        }

        /// <summary>The exterior is one atlas (tools/blender_atlas.py), so every part can take the livery and show damage.
        /// UnitPart.SetLivery and the _HitPoints damage shading only touch material slot 0 of each listed renderer, and a
        /// part only lists its own renderer by default (control surfaces keep theirs on a "_visible" child). Each part
        /// lists every skin renderer it owns, with the skin moved to slot 0 (its triangles too).</summary>
        static void SetLiveryTargets()
        {
            var skin = MatCache.Values.FirstOrDefault(m => m && m.name.EndsWith("B52_Skin"));
            if (!skin) { Note("  ! no B52_Skin material: liveries disabled"); return; }
            var interior = Find(ourRoot, "cockpit_int");
            var upType = T("UnitPart");
            int parts = 0, rends = 0, swapped = 0;
            foreach (var up in ourRoot.GetComponentsInChildren(upType, true))
            {
                var list = new List<Object>();
                foreach (var r in up.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (interior && r.transform.IsChildOf(interior)) continue;
                    if (r.GetComponentInParent(upType) != up) continue;          // belongs to a child part
                    var mats = r.sharedMaterials;
                    int k = System.Array.IndexOf(mats, skin);
                    if (k < 0) continue;
                    if (k > 0)
                    {
                        var mf = r.GetComponent<MeshFilter>(); var mesh = mf ? mf.sharedMesh : null;
                        if (mesh && mesh.subMeshCount > k && AssetDatabase.Contains(mesh) && AssetDatabase.GetAssetPath(mesh).StartsWith(Gen))
                        {
                            var t0 = mesh.GetTriangles(0); var tk = mesh.GetTriangles(k);
                            mesh.SetTriangles(tk, 0); mesh.SetTriangles(t0, k);
                            (mats[0], mats[k]) = (mats[k], mats[0]);
                            r.sharedMaterials = mats; EditorUtility.SetDirty(mesh); swapped++;
                        }
                        else continue;                                             // can't reorder: leave it out
                    }
                    list.Add(r); rends++;
                }
                SetRefArray(up, "damageMaterial.renderers", list);
                if (list.Count > 0) parts++;
            }
            Note($"Livery targets: {rends} renderers on {parts} parts ({swapped} reordered so the skin is slot 0)");
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

        // The mod version: the repo's VERSION file, the one place it's set (the plugin's project reads it too). The build
        // scripts pass it in B52_VERSION; run by hand, it's read through the project's link to this folder.
        public static string Version
        {
            get
            {
                var v = System.Environment.GetEnvironmentVariable("B52_VERSION");
                var file = System.IO.Path.Combine("Assets", "Editor", "B52Tools", "..", "..", "..", "VERSION");   // the link's target, on Linux
                if (string.IsNullOrEmpty(v) && System.IO.File.Exists(file)) v = System.IO.File.ReadAllText(file);
                v = v?.Trim();
                if (string.IsNullOrEmpty(v)) throw new Exception("No mod version: run the build through tools/build_all.sh (it sets B52_VERSION from VERSION)");
                return v;
            }
        }
        // tools/build_all.sh sets B52_BUILD_DIR to the repo's build/ folder; otherwise build/ inside the Unity project.
        static string BuildDir => System.Environment.GetEnvironmentVariable("B52_BUILD_DIR") ?? System.IO.Path.GetFullPath("build");

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
                weapons.arraySize = hardpointSetCount;     // one entry per station: the game resets a loadout whose count differs
                weapons.GetArrayElementAtIndex(0).objectReferenceValue = bayM;
                weapons.GetArrayElementAtIndex(1).objectReferenceValue = pylonM;
                for (int i = 2; i < hardpointSetCount; i++) weapons.GetArrayElementAtIndex(i).objectReferenceValue = null;   // the tail turret: off (a B-52J)
            }
            SetWeapons(lo.GetArrayElementAtIndex(0).FindPropertyRelative("weapons"), null, null);
            SetWeapons(lo.GetArrayElementAtIndex(1).FindPropertyRelative("weapons"), Mount("MK82", true), Mount("MK82", false));
            var presets = new (string name, string bay, string pylon, float fuel)[]
            {
                ("Mk 82 x51", "MK82", "MK82", 0.7f),
                ("JASSM-ER x20", "AGM158B", "AGM158B", 0.85f),
                ("JASSM-ER x8 + MALD-J x16 (SEAD)", "AGM158B", "ADM160C", 0.85f),
                ("JASSM-ER x8 + GBU-38 x48", "AGM158B", "GBU38", 0.8f),
                ("LRASM x8 + MALD x16 (anti-ship)", "AGM158C", "ADM160B", 0.85f),
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
            var status = BuildStatusDisplay();                    // the B-52's own damage display (B52Spec.StatusDisplay)
            if (status) ps.FindProperty("StatusDisplay").objectReferenceValue = status;
            ps.FindProperty("HUDExtras").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(DoNotShip + "/GameObject/SFB_HUDExtras_PLACEHOLDER.prefab");
            // The USAF livery and the extra ones (ExtraLiveries), offered to every faction the template had.
            string Livery(string name, string texture)
            {
                var path = Gen + "/" + name + ".asset";
                AssetDatabase.DeleteAsset(path);
                var liv = ScriptableObject.CreateInstance(T("LiveryData")); liv.name = name;
                var ls = SO(liv);
                ls.FindProperty("Texture").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Texture2D>(ModDir + "/Textures/" + texture);
                ls.FindProperty("Glossiness").floatValue = 0f;   // the game's own skins use 0; 0.35 added gloss on top of the smoothness map, too shiny
                ls.ApplyModifiedPropertiesWithoutUndo();
                AssetDatabase.CreateAsset(liv, path);
                return AssetDatabase.AssetPathToGUID(path);
            }
            var livGuid = Livery("B52_USAF_livery", "B52_atlas.png");
            var extra = ExtraLiveries.Where(l => AssetDatabase.LoadAssetAtPath<Texture2D>(ModDir + "/Textures/" + l.file))
                                     .Select(l => (guid: Livery(l.asset, l.file), l.name, l.faction)).ToList();
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
                for (int k = extra.Count - 1; k >= 0; k--)          // copies right after it, same faction, in list order
                {
                    if (extra[k].faction != null && (!f || f.name != extra[k].faction)) continue;   // another side's
                    lv.InsertArrayElementAtIndex(i);
                    var c = lv.GetArrayElementAtIndex(i + 1);
                    c.FindPropertyRelative("name").stringValue = extra[k].name;
                    c.FindPropertyRelative("assetReference.m_AssetGUID").stringValue = extra[k].guid;
                }
            }
            Note($"Liveries: {lv.arraySize} -> {livGuid}, plus {string.Join(", ", extra.Select(e => e.name))}");
            ps.FindProperty("aircraftGLimit").floatValue = 2.5f;
            ps.FindProperty("maxSpeed").floatValue = 290f;
            ps.FindProperty("takeoffSpeed").floatValue = 90f;
            // The shortest runway the B-52 uses. The game's AI lands only on a runway longer than this, and with none
            // in reach it bails out: at 2900 m no runway on Heartland (its longest is 2416 m) would do, and every AI
            // B-52 ejected after takeoff. The B-52 normally flies from 8000 ft (2440 m) runways; at landing weight
            // with its chute it stops in well under 2 km (tests/ingame/b52_flight.txt, rollout).
            ps.FindProperty("takeoffDistance").floatValue = 2200f;
            ps.FindProperty("approachSpeed").floatValue = 82f;
            ps.FindProperty("landingSpeed").floatValue = 72f;
            ps.FindProperty("shortLandingSpeed").floatValue = 68f;
            ps.FindProperty("cruiseThrottle").floatValue = 0.82f;
            // 3000 m: the AI B-52 banks at most 30 deg in the pattern (B52Systems AimFlightPath), and at the pattern's
            // 125 m/s that turns on about 2.8 km. The AI's final starts 3 radii out, so the approach begins 9 km out.
            ps.FindProperty("turningRadius").floatValue = 3000f;
            ps.FindProperty("cornerSpeed").floatValue = 125f;            // see B52Spec.Definition
            ps.FindProperty("groundTurningRadius").floatValue = 45f;
            ps.ApplyModifiedPropertiesWithoutUndo();

            var ds = SO(def);
            ds.FindProperty("jsonKey").stringValue = "B52J";
            ds.FindProperty("unitName").stringValue = "B-52J Stratofortress";
            ds.FindProperty("code").stringValue = "B-52J";
            ds.FindProperty("description").stringValue =
                "Boeing B-52J Stratofortress. The B-52H airframe re-engined with eight Rolls-Royce F130 turbofans " +
                "(17,000 lbf each) and fitted with the AN/APQ-188 AESA radar and the 1760 internal weapons bay. " +
                "Carries up to 70,000 lb of ordnance in the bay and on two wing pylons: 51 Mk 82, 80 GBU-38, 24 GBU-31, " +
                "20 JASSM-ER or LRASM, 20 ALCM, or MALD decoys.";
            ds.FindProperty("length").floatValue = 48.5f;
            ds.FindProperty("width").floatValue = 56.4f;
            ds.FindProperty("height").floatValue = 12.4f;
            ds.FindProperty("mass").floatValue = 83250f;
            ds.FindProperty("value").floatValue = 900f;
            ds.FindProperty("manpower").intValue = 5;
            ds.FindProperty("radarSize").floatValue = 0.1f;          // the B-52's RCS is famously large
            ds.FindProperty("unitPrefab").objectReferenceValue = prefab;
            // The aircraft menu and the map show mapIcon: the B-52's own top-down silhouette (tools/blender_icon.py),
            // not the donor's Alkyon. The HUD's friendly/hostile markers stay the game's generic aircraft icon.
            var iconPath = ModDir + "/Textures/B52_icon.png";
            if (AssetImporter.GetAtPath(iconPath) is TextureImporter iti)
            {
                if (iti.textureType != TextureImporterType.Sprite || !iti.alphaIsTransparency)
                {
                    iti.textureType = TextureImporterType.Sprite; iti.spriteImportMode = SpriteImportMode.Single;
                    iti.alphaIsTransparency = true; iti.mipmapEnabled = true; iti.SaveAndReimport();
                }
                var icon = AssetDatabase.LoadAssetAtPath<Sprite>(iconPath);
                if (icon) ds.FindProperty("mapIcon").objectReferenceValue = icon;
                Note(icon ? "Icon: B52_icon.png" : "  ! B52_icon.png has no sprite: the donor's icon stays");
            }
            else Note("  ! no Textures/B52_icon.png (run tools/blender_icon.py): the donor's icon stays");
            ds.FindProperty("spawnOffset").vector3Value = new Vector3(0, 3.9f, 0);
            ds.FindProperty("aircraftParameters").objectReferenceValue = par;
            ds.FindProperty("aircraftInfo.emptyWeight").floatValue = 83250f;
            ds.FindProperty("aircraftInfo.maxSpeed").floatValue = 1047f;
            ds.FindProperty("aircraftInfo.stallSpeed").floatValue = 240f;
            ds.FindProperty("aircraftInfo.maneuverability").floatValue = 2f;
            ds.FindProperty("aircraftInfo.maxWeight").floatValue = 221350f;
            ds.FindProperty("restRotation").vector3Value = Vector3.zero;
            ds.ApplyModifiedPropertiesWithoutUndo();

            SpecDefinition(def, par);
            AssetDatabase.CreateAsset(par, Gen + "/B52J_Parameters.asset");
            AssetDatabase.CreateAsset(def, Gen + "/B52J.asset");
            B52DonorAudit.CopiedAsset("Aircraft parameters", par, parSrc);
            B52DonorAudit.CopiedAsset("Aircraft definition", def, defSrc);
            // The prefab's Unit.definition points at our definition.
            var root = PrefabUtility.LoadPrefabContents(AssetDatabase.GetAssetPath(prefab));
            SetRef(root.GetComponent(T("Aircraft")), "definition", def);
            PrefabUtility.SaveAsPrefabAsset(root, AssetDatabase.GetAssetPath(prefab));
            PrefabUtility.UnloadPrefabContents(root);
            Note("Definition + parameters written to " + Gen);
        }
    }
}
