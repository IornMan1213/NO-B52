using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace B52Tools
{
    /// <summary>
    /// Builds the Heavy Aircraft Hangar building from HeavyHangar.fbx (tools/blender_hangar.py), using the game's
    /// hangar_med as the component donor: Building, Hangar, UnitPart, NetworkIdentity and the terrain height-map
    /// blocker come from its root, door UnitParts from its doors. References into the template are re-pointed at
    /// our objects; anything left over is cleared and logged.
    /// Run: Unity -batchmode -executeMethod B52Tools.HangarBuilder.Build, then HangarBuilder.BuildMod.
    /// </summary>
    public static class HangarBuilder
    {
        public const string Version = "0.1.0";
        public const string JsonKey = "hangar_heavy";          // also plugin/HeavyHangar/Plugin.cs
        const string ModDir = "Assets/Blueprinter/Mods/HeavyHangar";
        const string Fbx = ModDir + "/HeavyHangar.fbx";
        const string Gen = ModDir + "/Generated";
        const string DoNotShip = "Assets/Blueprinter/_donotship";
        const string TemplatePrefab = DoNotShip + "/GameObject/hangar_med_PLACEHOLDER.prefab";
        const string TemplateDef = DoNotShip + "/MonoBehaviour/hangar_med_PLACEHOLDER.asset";
        const string BuildDir = @"C:\Users\jayea\Documents\GitHub\NO-B52\build";
        const int StaticsLayer = 6;                             // PhysicsLayers.Statics, as hangar_med

        // Door leaves (blender_hangar.py): three per side on parallel rails, sliding into 14 m pockets.
        static readonly (string name, float slide)[] Doors =
        {
            ("door_R0", 42f), ("door_R1", 28f), ("door_R2", 14f),
            ("door_L0", -42f), ("door_L1", -28f), ("door_L2", -14f),
        };
        static readonly string[] RootComponents = { "Building", "Hangar", "UnitPart", "NetworkIdentity", "TerrainHeightMapBlocker" };

        static readonly StringBuilder Log = new StringBuilder();
        static readonly Dictionary<Object, Object> Remap = new Dictionary<Object, Object>();

        static void Note(string s) => Log.AppendLine(s);

        public static void Build()
        {
            Log.Clear(); Remap.Clear();
            GameObject tmpl = null, ours = null;
            try
            {
                if (!AssetDatabase.IsValidFolder(Gen)) AssetDatabase.CreateFolder(ModDir, "Generated");
                ConfigureFbx();
                tmpl = Spawn(TemplatePrefab);
                ours = Spawn(Fbx);
                ours.name = "HeavyHangar";
                var tRoot = tmpl.transform; var root = ours.transform;
                NormalizeFrames(root);
                root.position = Vector3.zero;
                Remap[tmpl] = ours; Remap[tRoot] = root;

                // Root components, copied in the template's order so Building exists before Hangar refers to it.
                foreach (var c in tRoot.GetComponents<Component>())
                {
                    if (c is Transform || !RootComponents.Contains(c.GetType().Name)) continue;
                    CopyComponent(c, ours);
                }
                var building = ours.GetComponent(T("Building"));
                var hangar = ours.GetComponent(T("Hangar"));
                var rootPart = ours.GetComponent(T("UnitPart"));
                var body = Find(root, "hangar_body");
                var bodyRenderer = body.GetComponent<MeshRenderer>();

                // Doors: UnitPart from the template door, a box collider, and the slide distance for Hangar.doors.
                var tDoorPart = Find(tRoot, "door_L").GetComponent(T("UnitPart"));
                var doorList = new List<(Transform t, float slide)>();
                foreach (var (name, slide) in Doors)
                {
                    var d = Find(root, name);
                    if (!d) { Note("  ! missing " + name); continue; }
                    var part = CopyComponent(tDoorPart, d.gameObject);
                    SetRef(part, "parentUnit", building);
                    var bc = d.gameObject.AddComponent<BoxCollider>();
                    bc.size = new Vector3(14.4f, 17.35f, 0.4f); bc.center = new Vector3(0, 8.725f, 0);
                    doorList.Add((d, slide));
                }

                // Static collision: the whole body mesh (floor, apron, walls, roof), like hangar_med's MeshCollider.
                var mc = body.gameObject.AddComponent<MeshCollider>();
                mc.sharedMesh = body.GetComponent<MeshFilter>().sharedMesh;

                // Lights: a point light at each light_N marker, a warning light over the door.
                var lights = new List<(Light light, Renderer r, bool night)>();
                var tLight = Find(tRoot, "hangarLight").GetComponent<Light>();
                foreach (Transform m in root.Cast<Transform>().ToList())
                {
                    if (!m.name.StartsWith("light_")) continue;
                    var l = m.gameObject.AddComponent<Light>();
                    EditorUtility.CopySerialized(tLight, l);
                    l.type = LightType.Point; l.range = 45f; l.intensity = Mathf.Max(l.intensity, 3f);
                    l.shadows = LightShadows.None;
                    lights.Add((l, null, true));
                }
                var lampRenderer = Find(root, "hangar_lamps").GetComponent<MeshRenderer>();
                lights.Add((null, lampRenderer, true));
                var doorLamp = Find(root, "doorLamp").GetComponent<MeshRenderer>();
                var dl = Find(root, "doorLight").gameObject.AddComponent<Light>();
                dl.type = LightType.Point; dl.range = 25f; dl.intensity = 4f; dl.color = new Color(1f, 0.55f, 0.1f);
                dl.shadows = LightShadows.None;
                lights.Add((dl, doorLamp, false));

                // Hangar
                var hs = new SerializedObject(hangar);
                hs.FindProperty("attachedUnit").objectReferenceValue = building;
                hs.FindProperty("criticalPart").objectReferenceValue = rootPart;
                hs.FindProperty("spawnTransform").objectReferenceValue = Find(root, "spawnPoint");
                hs.FindProperty("doorSpeed").floatValue = 0.06f;            // ~17 s for the long leaves
                hs.FindProperty("clearDistance").floatValue = 60f;          // doors wait until a B-52 is well clear
                var doors = hs.FindProperty("doors"); doors.arraySize = doorList.Count;
                for (int i = 0; i < doorList.Count; i++)
                {
                    var e = doors.GetArrayElementAtIndex(i);
                    e.FindPropertyRelative("transform").objectReferenceValue = doorList[i].t;
                    e.FindPropertyRelative("animatedPhysicsSurface").objectReferenceValue = null;
                    e.FindPropertyRelative("openAngle").vector3Value = Vector3.zero;
                    e.FindPropertyRelative("openPos").vector3Value = new Vector3(doorList[i].slide, 0, 0);
                }
                var lp = hs.FindProperty("lights"); lp.arraySize = lights.Count;
                for (int i = 0; i < lights.Count; i++)
                {
                    var e = lp.GetArrayElementAtIndex(i);
                    e.FindPropertyRelative("light").objectReferenceValue = lights[i].light;
                    e.FindPropertyRelative("lightRenderer").objectReferenceValue = lights[i].r;
                    e.FindPropertyRelative("onlyAtNight").boolValue = lights[i].night;
                }
                hs.ApplyModifiedPropertiesWithoutUndo();

                SetRef(rootPart, "parentUnit", building);
                Set(rootPart, "armorProperties", p =>
                {
                    p.FindPropertyRelative("pierceArmor").floatValue = 60f;     // steel frame, 30 % tougher than hangar_med
                    p.FindPropertyRelative("blastArmor").floatValue = 40f;
                });
                var tb = ours.GetComponents<Component>().FirstOrDefault(c => c && c.GetType().Name == "TerrainHeightMapBlocker");   // namespaced type
                if (tb) Set(tb, "Renderers", p =>
                {
                    p.arraySize = 1;
                    p.GetArrayElementAtIndex(0).FindPropertyRelative("Renderer").objectReferenceValue = bodyRenderer;
                });

                foreach (var t in ours.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = StaticsLayer;
                ConvertMaterials(root);
                RemapReferences(ours, tmpl);

                var prefabPath = ModDir + "/HeavyHangar.prefab";
                var prefab = PrefabUtility.SaveAsPrefabAsset(ours, prefabPath);
                MakeDefinition(prefab, building);
                Note("Saved " + prefabPath);
            }
            catch (Exception e) { Note("FAILED: " + e); throw; }
            finally
            {
                if (tmpl) Object.DestroyImmediate(tmpl);
                if (ours) Object.DestroyImmediate(ours);
                AssetDatabase.SaveAssets();
                System.IO.File.WriteAllText("HangarBuild.log", Log.ToString());
            }
        }

        public static void BuildMod()
        {
            System.IO.Directory.CreateDirectory(BuildDir);
            Blueprinter.ModBuilder.Build("HeavyHangar", "Heavy Hangar", Version, BuildDir);
            Debug.Log("[HeavyHangar] mod built to " + BuildDir);
        }

        // ------------------------------------------------------------------ steps
        static void ConfigureFbx()
        {
            var imp = (ModelImporter)AssetImporter.GetAtPath(Fbx) ?? throw new Exception("FBX not imported: " + Fbx);
            imp.isReadable = true;                                   // MeshCollider at runtime
            imp.importAnimation = false; imp.animationType = ModelImporterAnimationType.None;
            imp.importCameras = false; imp.importLights = false;
            imp.useFileScale = true; imp.globalScale = 1f;
            imp.bakeAxisConversion = true;
            imp.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            imp.SaveAndReimport();
        }

        /// <summary>Same as B52Builder.NormalizeFrames: bake the FBX +90 deg X conversion into mesh copies.</summary>
        static void NormalizeFrames(Transform root)
        {
            var all = root.GetComponentsInChildren<Transform>(true);
            var pos = all.ToDictionary(t => t, t => t.position);
            var rot = all.ToDictionary(t => t, t => t.rotation);
            var fix = Quaternion.Euler(-90f, 0f, 0f);
            var meshFix = Matrix4x4.Rotate(Quaternion.Euler(90f, 0f, 0f));
            if (!AssetDatabase.IsValidFolder(Gen + "/Meshes")) AssetDatabase.CreateFolder(Gen, "Meshes");
            foreach (var t in all)
            {
                t.SetPositionAndRotation(pos[t], rot[t] * fix);
                var mf = t.GetComponent<MeshFilter>();
                if (!mf || !mf.sharedMesh) continue;
                var m = Object.Instantiate(mf.sharedMesh); m.name = t.name;
                var v = m.vertices; var n = m.normals; var tg = m.tangents;
                for (int i = 0; i < v.Length; i++) v[i] = meshFix.MultiplyPoint3x4(v[i]);
                for (int i = 0; i < n.Length; i++) n[i] = meshFix.MultiplyVector(n[i]);
                for (int i = 0; i < tg.Length; i++) { var d = meshFix.MultiplyVector(tg[i]); tg[i] = new Vector4(d.x, d.y, d.z, tg[i].w); }
                m.vertices = v; m.normals = n; m.tangents = tg; m.RecalculateBounds();
                var path = Gen + "/Meshes/" + t.name + ".asset";
                AssetDatabase.DeleteAsset(path); AssetDatabase.CreateAsset(m, path);
                mf.sharedMesh = m;
            }
            // Sanity: the floor must end up at y = 0 with the door facing +Z.
            var body = Find(root, "hangar_body");
            if (body)
            {
                var b = body.GetComponent<MeshRenderer>().bounds;
                Note($"Body bounds {b.min:F1} .. {b.max:F1} (expect x +-57, y -6..27, z -39..85)");
            }
            var sp = Find(root, "spawnPoint");
            if (sp) Note($"Spawn {sp.position:F1} fwd {sp.forward:F2} (expect (0,0,-6) fwd +Z)");
        }

        static void ConvertMaterials(Transform root)
        {
            var lit = Shader.Find("Universal Render Pipeline/Lit") ?? throw new Exception("URP Lit shader not found");
            var cache = new Dictionary<Material, Material>();
            foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    var src = mats[i]; if (!src) continue;
                    if (!cache.TryGetValue(src, out var m))
                    {
                        m = new Material(lit) { name = src.name.StartsWith("HH_") ? src.name : "HH_" + src.name };
                        var tex = src.HasProperty("_BaseMap") ? src.GetTexture("_BaseMap") : null;
                        if (tex) m.SetTexture("_BaseMap", tex);
                        m.SetColor("_BaseColor", Color.white);
                        m.SetFloat("_Smoothness", src.name.Contains("Floor") ? 0.25f : 0.35f);
                        m.SetFloat("_Metallic", src.name.Contains("Wall") || src.name.Contains("Roof") || src.name.Contains("Door") ? 0.4f : 0f);
                        if (src.name.Contains("Lamp"))
                        {
                            m.EnableKeyword("_EMISSION");
                            m.SetTexture("_EmissionMap", tex);
                            m.SetColor("_EmissionColor", new Color(1f, 0.95f, 0.85f) * 3f);
                            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
                        }
                        var path = Gen + "/" + m.name + ".mat";
                        AssetDatabase.DeleteAsset(path); AssetDatabase.CreateAsset(m, path);
                        cache[src] = m;
                    }
                    mats[i] = m;
                }
                r.sharedMaterials = mats;
            }
            Note($"Materials: {cache.Count} converted to URP Lit");
        }

        /// <summary>Re-point every reference into the template at our copy, or clear it (it would be a dangling
        /// scene reference in the saved prefab).</summary>
        static void RemapReferences(GameObject ours, GameObject tmpl)
        {
            var tmplSet = new HashSet<Object>(tmpl.GetComponentsInChildren<Component>(true).Cast<Object>()
                .Concat(tmpl.GetComponentsInChildren<Transform>(true).Select(t => (Object)t.gameObject)));
            int remapped = 0, cleared = 0;
            foreach (var c in ours.GetComponentsInChildren<Component>(true))
            {
                if (!c || c is Transform) continue;
                var so = new SerializedObject(c); var it = so.GetIterator(); bool changed = false;
                while (it.Next(true))
                {
                    if (it.propertyType != SerializedPropertyType.ObjectReference) continue;
                    var v = it.objectReferenceValue;
                    if (!v || !tmplSet.Contains(v)) continue;
                    if (Remap.TryGetValue(v, out var to)) { it.objectReferenceValue = to; remapped++; }
                    else { Note($"  cleared {c.GetType().Name}.{it.propertyPath} -> {v.name}"); it.objectReferenceValue = null; cleared++; }
                    changed = true;
                }
                if (changed) so.ApplyModifiedPropertiesWithoutUndo();
            }
            Note($"References: {remapped} remapped, {cleared} cleared");
        }

        static void MakeDefinition(GameObject prefab, Component building)
        {
            var src = AssetDatabase.LoadAssetAtPath<ScriptableObject>(TemplateDef) ?? throw new Exception("Missing " + TemplateDef);
            var def = ScriptableObject.CreateInstance(src.GetType());
            EditorUtility.CopySerialized(src, def);
            def.name = JsonKey;
            var ds = new SerializedObject(def);
            ds.FindProperty("jsonKey").stringValue = JsonKey;
            ds.FindProperty("unitName").stringValue = "Heavy Aircraft Hangar";
            ds.FindProperty("description").stringValue =
                "Steel-framed hangar with an 84 m x 17 m door. Spawns any aircraft, including heavy bombers like the B-52.";
            ds.FindProperty("code").stringValue = "HVY HGR";
            ds.FindProperty("length").floatValue = 78f;
            ds.FindProperty("width").floatValue = 114f;
            ds.FindProperty("height").floatValue = 27f;
            ds.FindProperty("value").floatValue = 10f;
            ds.FindProperty("unitPrefab").objectReferenceValue = prefab;
            ds.ApplyModifiedPropertiesWithoutUndo();
            var path = Gen + "/" + JsonKey + ".asset";
            AssetDatabase.DeleteAsset(path); AssetDatabase.CreateAsset(def, path);

            // Point the prefab's Building at the new definition.
            var root = PrefabUtility.LoadPrefabContents(AssetDatabase.GetAssetPath(prefab));
            var b = root.GetComponent(building.GetType());
            var bs = new SerializedObject(b); bs.FindProperty("definition").objectReferenceValue = def; bs.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root, AssetDatabase.GetAssetPath(prefab));
            PrefabUtility.UnloadPrefabContents(root);
            Note("Definition " + JsonKey + " -> " + path);
        }

        // ------------------------------------------------------------------ helpers (as in B52Builder)
        static Type T(string name)
        {
            var t = Type.GetType(name + ", Assembly-CSharp");
            if (t == null) throw new Exception("Game type not found: " + name);
            return t;
        }

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

        static Component CopyComponent(Component src, GameObject dst)
        {
            var c = dst.AddComponent(src.GetType());
            if (c == null) { Note($"  ! could not add {src.GetType().Name} to {dst.name}"); return null; }
            EditorUtility.CopySerialized(src, c);
            if (!Remap.ContainsKey(src)) Remap[src] = c;
            return c;
        }

        static void Set(Object o, string prop, Action<SerializedProperty> act)
        {
            var so = new SerializedObject(o); var p = so.FindProperty(prop);
            if (p == null) { Note($"  ! {o.GetType().Name}.{prop} not found"); return; }
            act(p); so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void SetRef(Object o, string prop, Object v) => Set(o, prop, p => p.objectReferenceValue = v);
    }
}
