using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace B52Tools
{
    /// <summary>
    /// Generates the B-52 weapon racks (WeaponMount assets + rack prefabs) from the real ordnance list.
    /// Each rack clones the game's own munition (MountedMissile) from a vanilla donor rack and lays out the real
    /// count: clip-in / rotary layouts for the internal bay, HSAB clusters for the wing pylons.
    /// </summary>
    public static class B52Weapons
    {
        const string DoNotShip = "Assets/Blueprinter/_donotship/MonoBehaviour/";
        const string Dir = "Assets/Blueprinter/Mods/B52/Weapons";

        // Game munition family -> vanilla donor rack (its first MountedMissile child is cloned).
        static readonly Dictionary<string, string> Donor = new Dictionary<string, string>
        {
            { "bomb_250", "bomb_250_internalx18" },           { "bomb_500", "bomb_500_internalx8" },
            { "bomb_125", "bomb_125_internalx6" },            { "bomb_250_glide", "bomb_250_glide_internalx18" },
            { "bomb_500_glide", "bomb_500_glide_internalx6" }, { "bomb_glide1", "bomb_glide1_six_internal" },
            { "bomb_penetrator1", "bomb_penetrator1_internalx4" }, { "bomb_cluster1", "bomb_cluster1_dual_internal" },
            { "nuke_tac", "nuclearBomb1_internalx4" },        { "nuke_strat", "nuclearBomb1_strategic_internalx4" },
            { "cruise", "CruiseMissile1_internalx6" },        { "cruise_nuke", "CruiseMissile20kt_internalx2" },
            { "ashm1", "AShM1_internalx6" },                  { "ashm2", "AShM2_internalx4" },
            { "agm_heavy", "AGM_heavy_internalx8" },          { "tbm_nuke", "BallisticMissile1_tacNuke_internalx2" },
            { "mald", "CruiseMissile1_internalx6" },          { "mald_j", "CruiseMissile1_internalx6" },
        };

        // ADM-160 MALD / MALD-J: custom missiles made from the game's ALM-C450 cruise missile (B52Systems.Decoys adds
        // the jamming). Scaled 0.45 (6.3 m -> 2.85 m); 115 kg; thrust/finArea kept at the donor's ratio, so the same
        // subsonic top speed (top speed = sqrt(thrust / (0.5 Cd rho finArea))); 2,400 s burn for ~600+ km.
        // radarSize 0.1 = the B-52J's: enemy radar sees a bomber (signal ~ RCS^0.25).
        const float DecoyScale = 0.45f;
        static readonly Dictionary<string, Object> DecoyInfo = new Dictionary<string, Object>();

        public class W
        {
            public string code, name, family; public int internalCount, perPylon; public float unitKg;
            public W(string code, string name, string family, int internalCount, int perPylon, float unitKg)
            { this.code = code; this.name = name; this.family = family; this.internalCount = internalCount; this.perPylon = perPylon; this.unitKg = unitKg; }
        }

        // The user's B-52H ordnance envelope. internal = bay (clip-in racks or rotary launcher), perPylon = each of
        // the two wing HSAB pylons. Ranges in the source use the upper figure that fits the 31,750 kg payload limit.
        public static readonly W[] Table =
        {
            // Nuclear
            new W("AGM86B",  "AGM-86B ALCM",       "cruise_nuke", 8, 6, 1458),
            new W("AGM69",   "AGM-69 SRAM",        "tbm_nuke",    8, 6, 1010),
            new W("B61",     "B61",                "nuke_tac",    8, 0, 320),
            new W("B83",     "B83",                "nuke_strat",  8, 0, 1100),
            new W("B53",     "B53",                "nuke_strat",  2, 0, 4010),
            new W("AGM181",  "AGM-181 LRSO",       "cruise_nuke", 8, 6, 1500),
            // Conventional standoff & strike
            new W("AGM158A", "AGM-158A JASSM",     "cruise",      8, 6, 1020),
            new W("AGM158B", "AGM-158B JASSM-ER",  "cruise",      8, 6, 1021),
            new W("AGM158C", "AGM-158C LRASM",     "ashm1",       8, 6, 1100),
            new W("AGM86C",  "AGM-86C/D CALCM",    "cruise",      8, 4, 1950),   // 16 x 1,950 kg = 31.2 t: 20 would exceed the payload limit
            new W("AGM84",   "AGM-84 Harpoon",     "ashm2",       0, 4, 691),
            new W("AGM142",  "AGM-142 Have Nap",   "agm_heavy",   0, 2, 1360),
            new W("AGM154",  "AGM-154 JSOW",       "bomb_glide1", 0, 6, 483),
            new W("AGM129",  "AGM-129 ACM",        "cruise_nuke", 8, 0, 1250),    // one rotary ring: two rings of 6.3 m missiles are longer than the bay
            // Conventional gravity
            new W("MK82",    "Mk 82",              "bomb_250",    27, 12, 227),
            new W("M117",    "M117",               "bomb_250",    27, 12, 340),
            new W("MK84",    "Mk 84",              "bomb_500",    12, 9, 907),
            new W("BDU48",   "BDU-48",             "bomb_125",    17, 0, 4.5f),  // 10 lb practice bomb
            // Cluster
            new W("CBU52",   "CBU-52",             "bomb_cluster1", 27, 12, 356),
            new W("CBU58",   "CBU-58",             "bomb_cluster1", 27, 12, 372),
            new W("CBU71",   "CBU-71",             "bomb_cluster1", 27, 12, 372),
            new W("CBU87",   "CBU-87 CEM",         "bomb_cluster1", 24, 8, 430),
            new W("CBU89",   "CBU-89 Gator",       "bomb_cluster1", 24, 9, 322),
            // Laser-guided
            new W("GBU10",   "GBU-10 Paveway II",  "bomb_500_glide", 4, 3, 934),
            new W("GBU12",   "GBU-12 Paveway II",  "bomb_250_glide", 4, 3, 227),
            new W("GBU28",   "GBU-28",             "bomb_penetrator1", 0, 2, 2130),
            // JDAM
            new W("GBU31",   "GBU-31 JDAM",        "bomb_500_glide", 8, 8, 925),
            new W("GBU38",   "GBU-38 JDAM",        "bomb_250_glide", 32, 24, 241),
            new W("GBU54",   "GBU-54 Laser JDAM",  "bomb_500_glide", 8, 0, 907),  // the game's 2,000 lb glide-bomb model, so it fits like GBU-31 (15 hung below the doors)
            // WCMD
            new W("CBU103",  "CBU-103 WCMD",       "bomb_cluster1", 16, 7, 430),
            new W("CBU104",  "CBU-104 WCMD",       "bomb_cluster1", 16, 7, 322),
            new W("CBU105",  "CBU-105 WCMD",       "bomb_cluster1", 16, 7, 417),
            // Decoys (custom missiles, see MakeDecoy)
            new W("ADM160B", "ADM-160B MALD",      "mald",   0, 8, 115),
            new W("ADM160C", "ADM-160C MALD-J",    "mald_j", 0, 8, 115),
            // Naval mines
            new W("MK56",    "Mk 56 Quickstrike",  "bomb_500",    8, 6, 909),
            new W("MK62",    "Mk 62 Quickstrike",  "bomb_250",    32, 24, 227),
            new W("MK63",    "Mk 63 Quickstrike",  "bomb_500",    8, 5, 447),
            new W("MK65",    "Mk 65 Quickstrike",  "bomb_500",    8, 5, 1086),
        };

        static Type T(string n) => Type.GetType(n + ", Assembly-CSharp") ?? throw new Exception("type " + n);

        /// <summary>Builds every rack; returns (internal mounts, pylon mounts) in table order.</summary>
        public static (List<ScriptableObject> bay, List<ScriptableObject> pylon) Generate(Action<string> log)
        {
            if (!AssetDatabase.IsValidFolder(Dir)) AssetDatabase.CreateFolder("Assets/Blueprinter/Mods/B52", "Weapons");
            var bay = new List<ScriptableObject>(); var pylon = new List<ScriptableObject>();
            DecoyInfo.Clear();
            DecoyInfo["mald"] = MakeDecoy("ADM160B", "ADM-160B MALD", "MALD",
                "Miniature Air-Launched Decoy. No warhead: flies the route to its aimpoint with the radar signature of a B-52, " +
                "drawing radars and SAMs away from the bombers.", log);
            DecoyInfo["mald_j"] = MakeDecoy("ADM160C", "ADM-160C MALD-J", "MALD-J",
                "MALD with a jammer: looks like a B-52 on radar and jams enemy radars within ~30 km of its path.", log);
            // Without the decoy, MakeMount would fill the MALD racks with the donor: real, armed cruise missiles.
            foreach (var kv in DecoyInfo)
                if (!kv.Value) throw new Exception($"Decoy '{kv.Key}' could not be built (CruiseMissile1 placeholders missing?); stopping so the MALD racks don't ship armed cruise missiles");
            foreach (var w in Table)
            {
                var donor = AssetDatabase.LoadAssetAtPath<ScriptableObject>(DoNotShip + Donor[w.family] + "_PLACEHOLDER.asset");
                if (!donor) { log("  ! donor rack missing for " + w.code); continue; }
                if (w.internalCount > 0) bay.Add(MakeMount(w, donor, true, w.internalCount, log));
                if (w.perPylon > 0) pylon.Add(MakeMount(w, donor, false, w.perPylon, log));
            }
            // Racks a previous build made that this one didn't (a count changed, e.g. AGM86C hsabx6 -> hsabx4) would still
            // be packed into the mod and offered in game: remove them.
            var made = new HashSet<string>(bay.Concat(pylon).Where(m => m).Select(m => m.name));
            int stale = 0;
            foreach (var guid in AssetDatabase.FindAssets("B52_", new[] { Dir }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var name = System.IO.Path.GetFileNameWithoutExtension(path);
                if (!System.Text.RegularExpressions.Regex.IsMatch(name, @"^B52_.+_(bay|hsab)x\d+$") || made.Contains(name)) continue;
                AssetDatabase.DeleteAsset(path); stale++;
            }
            log($"Weapons: {bay.Count} bay racks, {pylon.Count} pylon racks" + (stale > 0 ? $", {stale} stale rack files removed" : ""));
            return (bay, pylon);
        }

        static ScriptableObject MakeMount(W w, ScriptableObject donor, bool internalBay, int count, Action<string> log)
        {
            var key = $"B52_{w.code}_{(internalBay ? "bay" : "hsab")}x{count}";
            var dso = new SerializedObject(donor);
            var donorPrefab = (GameObject)dso.FindProperty("prefab").objectReferenceValue;
            var unit = donorPrefab.GetComponentsInChildren(T("MountedMissile"), true).FirstOrDefault();
            if (!unit) { log("  ! no MountedMissile in " + donor.name); return null; }

            // Size of one weapon, from its renderers.
            var probe = (GameObject)Object.Instantiate(((Component)unit).gameObject);
            var rs = probe.GetComponentsInChildren<Renderer>();
            var b = rs.Length > 0 ? rs[0].bounds : new Bounds(Vector3.zero, new Vector3(0.4f, 0.4f, 3f));
            foreach (var r in rs) b.Encapsulate(r.bounds);
            Object.DestroyImmediate(probe);
            float len = Mathf.Max(1f, b.size.z), dia = Mathf.Max(0.25f, Mathf.Max(b.size.x, b.size.y));

            DecoyInfo.TryGetValue(w.family, out var decoyInfo);
            if (decoyInfo) { len *= DecoyScale; dia *= DecoyScale; }
            var root = new GameObject(key);
            var positions = internalBay ? BayLayout(count, len, dia, w.family) : PylonLayout(count, len, dia);
            for (int i = 0; i < count; i++)
            {
                var m = (GameObject)Object.Instantiate(((Component)unit).gameObject, root.transform);
                m.name = $"{w.code}_{i + 1}";
                m.transform.localPosition = positions[i];
                m.transform.localRotation = Quaternion.identity;
                if (decoyInfo)
                {
                    m.transform.localScale = Vector3.one * DecoyScale;
                    var mm = new SerializedObject(m.GetComponent(T("MountedMissile")));
                    mm.FindProperty("info").objectReferenceValue = decoyInfo; mm.ApplyModifiedPropertiesWithoutUndo();
                }
            }
            if (!internalBay)
            {
                var meshPath = $"{Dir}/{key}_rack.asset";
                AssetDatabase.DeleteAsset(meshPath);
                var mesh = RackMesh(positions, len, dia, key);
                AssetDatabase.CreateAsset(mesh, meshPath);
                var rack = new GameObject("rack");
                rack.transform.SetParent(root.transform, false);
                rack.AddComponent<MeshFilter>().sharedMesh = mesh;
                rack.AddComponent<MeshRenderer>().sharedMaterial = RackMaterial();
            }
            var prefabPath = $"{Dir}/{key}.prefab";
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            Object.DestroyImmediate(root);

            var mount = ScriptableObject.CreateInstance(donor.GetType());
            EditorUtility.CopySerialized(donor, mount);
            mount.name = key;
            var so = new SerializedObject(mount);
            so.FindProperty("prefab").objectReferenceValue = prefab;
            so.FindProperty("jsonKey").stringValue = key;
            so.FindProperty("mountName").stringValue = $"{w.name} x{count}" + (internalBay ? (IsRotary(w.family) ? " (rotary)" : "") : " (HSAB)");
            so.FindProperty("ammo").intValue = count;
            so.FindProperty("missileBay").boolValue = internalBay;
            so.FindProperty("mass").floatValue = w.unitKg * count;
            so.FindProperty("emptyMass").floatValue = internalBay ? 0f : 450f;            // HSAB beam + MERs
            so.FindProperty("drag").floatValue = internalBay ? 0f : 0.04f * count + 0.1f;
            so.FindProperty("emptyDrag").floatValue = internalBay ? 0f : 0.1f;
            so.FindProperty("RCS").floatValue = internalBay ? 0f : 0.05f * count;
            so.FindProperty("disabled").boolValue = false;
            if (decoyInfo) so.FindProperty("info").objectReferenceValue = decoyInfo;
            so.ApplyModifiedPropertiesWithoutUndo();
            var path = $"{Dir}/{key}.asset";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(mount, path);
            return mount;
        }

        /// <summary>A decoy missile: MissileDefinition + WeaponInfo + missile prefab, cloned from the ALM-C450.</summary>
        static Object MakeDecoy(string code, string name, string shortName, string description, Action<string> log)
        {
            const string Src = "Assets/Blueprinter/_donotship/";
            var srcDef = AssetDatabase.LoadAssetAtPath<ScriptableObject>(Src + "MonoBehaviour/CruiseMissile1_PLACEHOLDER.asset");
            var srcInfo = AssetDatabase.LoadAssetAtPath<ScriptableObject>(Src + "MonoBehaviour/info_CruiseMissile1_PLACEHOLDER.asset");
            var srcPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(Src + "GameObject/CruiseMissile1_PLACEHOLDER.prefab");
            if (!srcDef || !srcInfo || !srcPrefab) { log("  ! decoy donor missing"); return null; }
            string key = "B52_" + code;

            var def = ScriptableObject.CreateInstance(srcDef.GetType()); EditorUtility.CopySerialized(srcDef, def); def.name = key;
            var info = ScriptableObject.CreateInstance(srcInfo.GetType()); EditorUtility.CopySerialized(srcInfo, info); info.name = "info_" + key;
            var defPath = $"{Dir}/{key}.asset"; var infoPath = $"{Dir}/info_{key}.asset";
            AssetDatabase.DeleteAsset(defPath); AssetDatabase.CreateAsset(def, defPath);
            AssetDatabase.DeleteAsset(infoPath); AssetDatabase.CreateAsset(info, infoPath);

            var go = (GameObject)PrefabUtility.InstantiatePrefab(srcPrefab);
            PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            go.name = key;
            go.transform.localScale = Vector3.one * DecoyScale;
            var missile = go.GetComponent(T("Missile"));
            var ms = new SerializedObject(missile);
            ms.FindProperty("definition").objectReferenceValue = def;
            ms.FindProperty("info").objectReferenceValue = info;
            ms.FindProperty("mass").floatValue = 115f;
            float donorFin = ms.FindProperty("finArea").floatValue;
            ms.FindProperty("finArea").floatValue = donorFin * 0.2f;
            var motors = ms.FindProperty("motors");
            for (int i = 0; i < motors.arraySize; i++)
            {
                var m = motors.GetArrayElementAtIndex(i);
                m.FindPropertyRelative("thrust").floatValue *= 0.2f;              // same thrust / finArea as the donor
                m.FindPropertyRelative("fuelMass").floatValue = 45f;
                m.FindPropertyRelative("burnTime").floatValue = 2400f;
            }
            ms.ApplyModifiedPropertiesWithoutUndo();
            var prefabPath = $"{Dir}/{key}.prefab";
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, prefabPath);
            Object.DestroyImmediate(go);

            var ds = new SerializedObject(def);
            ds.FindProperty("jsonKey").stringValue = key;
            ds.FindProperty("unitName").stringValue = name;
            ds.FindProperty("code").stringValue = shortName;
            ds.FindProperty("description").stringValue = description;
            ds.FindProperty("radarSize").floatValue = 0.1f;
            ds.FindProperty("mass").floatValue = 115f;
            ds.FindProperty("value").floatValue = 0.3f;
            ds.FindProperty("unitPrefab").objectReferenceValue = prefab;
            ds.ApplyModifiedPropertiesWithoutUndo();
            var isx = new SerializedObject(info);
            isx.FindProperty("weaponName").stringValue = name;
            isx.FindProperty("shortName").stringValue = shortName;
            isx.FindProperty("description").stringValue = description;
            isx.FindProperty("blastDamage").floatValue = 0f;
            isx.FindProperty("pierceDamage").floatValue = 0f;
            isx.FindProperty("costPerRound").floatValue = 0.3f;
            isx.FindProperty("weaponPrefab").objectReferenceValue = prefab;
            isx.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(def); EditorUtility.SetDirty(info);
            log($"  decoy {name}: 115 kg, fin area {donorFin * 0.2f:F2}, radar size 0.1");
            return info;
        }

        /// <summary>Missiles that load on the bay's rotary launcher rather than clip-in racks.</summary>
        static bool IsRotary(string family) => family.StartsWith("cruise") || family == "tbm_nuke" || family == "ashm1";

        // Bay: 10.3 m long x 1.7 m wide x 1.8 m tall. Cruise missiles go on an 8-round rotary launcher; bombs on
        // clip-in racks in rows x columns x layers. Origin is the bay centre.
        static Vector3[] BayLayout(int n, float len, float dia, string family)
        {
            var p = new Vector3[n];
            if (IsRotary(family))
            {
                int perRing = 8; int rings = Mathf.CeilToInt(n / (float)perRing);
                for (int i = 0; i < n; i++)
                {
                    float a = (i % perRing) * Mathf.PI * 2f / perRing;
                    int ring = i / perRing;
                    float z = rings == 1 ? 0 : (ring - (rings - 1) / 2f) * Mathf.Min(len * 1.02f, 10.3f / rings);
                    // 8 cm above the bay centre: measured in game (tests/ingame bayfit), this keeps every rotary load
                    // above the door line, and the bay (tools/blender_prep.py bay_box) is tall and wide enough for the
                    // finned cruise missiles: 5 cm under the roof, 8 cm inside the walls.
                    p[i] = new Vector3(Mathf.Cos(a) * 0.55f, Mathf.Sin(a) * 0.55f + 0.08f, z);
                }
                return p;
            }
            int rows = Mathf.Max(1, Mathf.FloorToInt(10.0f / (len * 1.05f)));
            int cols = Mathf.Clamp(Mathf.FloorToInt(1.6f / (dia * 1.1f)), 1, 4);
            int perLayer = rows * cols;
            for (int i = 0; i < n; i++)
            {
                int layer = i / perLayer, k = i % perLayer, r = k / cols, c = k % cols;
                p[i] = new Vector3((c - (cols - 1) / 2f) * dia * 1.1f,
                                   0.6f - layer * dia * 1.15f,
                                   (r - (rows - 1) / 2f) * len * 1.05f);
            }
            return p;
        }

        // HSAB: the 6 m adapter beam under the pylon (tools/blender_pylons.py; the mount origin is its underside)
        // carries an ejector rack per tandem station and tier, each holding up to three weapons side by side.
        // Every tier hangs RackTier below the one above: the rack's crossbar, the sway braces, then the weapon.
        const float CrossH = 0.07f, BraceH = 0.05f, TierGap = 0.06f;
        static float TierPitch(float dia) => CrossH + BraceH + dia + TierGap;
        static float CrossTop(int tier, float dia) => -0.02f - tier * TierPitch(dia);

        static Vector3[] PylonLayout(int n, float len, float dia)
        {
            var p = new Vector3[n];
            int along = Mathf.Clamp(Mathf.FloorToInt(6.2f / (len * 1.05f)), 1, 3);
            const int across = 3;
            int perTier = along * across;
            for (int i = 0; i < n; i++)
            {
                int tier = i / perTier, k = i % perTier, a = k / across, c = k % across;
                p[i] = new Vector3((c - 1) * dia * 1.15f, CrossTop(tier, dia) - CrossH - BraceH - dia / 2f,
                                   (a - (along - 1) / 2f) * len * 1.05f);
            }
            return p;
        }

        /// <summary>The rack hardware for a wing load: for each tandem station and tier an ejector-rack crossbar over
        /// its weapons (with fairing ends), two pairs of sway braces and an ejector hook on each weapon, a block up to
        /// the adapter beam for the top tier, and side posts from the lower tiers up to the beam. One mesh, in the
        /// rack prefab, so it stays on the pylon after the weapons are released.</summary>
        static Mesh RackMesh(Vector3[] pos, float len, float dia, string key)
        {
            var v = new List<Vector3>(); var t = new List<int>();
            void Box(Vector3 c, Vector3 s, Quaternion r)
            {
                var h = s / 2f;
                Vector3[] n = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
                foreach (var f in n)
                {
                    // two axes in the face's plane
                    var u = Mathf.Abs(f.y) > 0.5f ? Vector3.right : Vector3.up;
                    var w = Vector3.Cross(f, u);
                    var fc = Vector3.Scale(f, h);
                    var uu = Vector3.Scale(u, h); var ww = Vector3.Scale(w, h);
                    int b = v.Count;
                    v.Add(c + r * (fc - uu - ww)); v.Add(c + r * (fc + uu - ww)); v.Add(c + r * (fc + uu + ww)); v.Add(c + r * (fc - uu + ww));
                    // wind so the face looks along f: Unity's front face is the one whose Cross(b - a, c - a) points
                    // at the viewer (the same rule RecalculateNormals uses)
                    var nrm = Vector3.Cross(v[b + 1] - v[b], v[b + 2] - v[b]);
                    if (Vector3.Dot(nrm, r * f) > 0) { t.Add(b); t.Add(b + 1); t.Add(b + 2); t.Add(b); t.Add(b + 2); t.Add(b + 3); }
                    else { t.Add(b); t.Add(b + 2); t.Add(b + 1); t.Add(b); t.Add(b + 3); t.Add(b + 2); }
                }
            }
            var id = Quaternion.identity;
            float crossLen = Mathf.Clamp(len * 0.5f, 0.8f, 1.8f);
            // group the weapons by station (z) and tier (y)
            foreach (var g in pos.GroupBy(q => (Mathf.Round(q.z * 100), Mathf.Round(q.y * 100))))
            {
                var ws = g.ToList();
                float y = ws[0].y, z = ws[0].z;
                int tier = Mathf.RoundToInt((CrossTop(0, dia) - CrossH - BraceH - dia / 2f - y) / TierPitch(dia));
                float top = CrossTop(tier, dia);
                float x0 = ws.Min(q => q.x), x1 = ws.Max(q => q.x);
                float halfW = (x1 - x0) / 2f + dia * 0.32f, xc = (x0 + x1) / 2f;
                // crossbar, with tapered fairing blocks at both ends
                Box(new Vector3(xc, top - CrossH / 2f, z), new Vector3(halfW * 2f, CrossH, crossLen), id);
                foreach (var e in new[] { -1f, 1f })
                    Box(new Vector3(xc, top - CrossH * 0.35f, z + e * (crossLen / 2f + 0.09f)), new Vector3(halfW * 1.6f, CrossH * 0.6f, 0.18f),
                        Quaternion.Euler(e * 14f, 0, 0));
                foreach (var q in ws)
                {
                    float wTop = q.y + dia / 2f;
                    // ejector hook and housing
                    Box(new Vector3(q.x, top - CrossH - 0.02f, q.z), new Vector3(0.09f, 0.05f, 0.22f), id);
                    // sway braces: two pairs, angled in onto the weapon's shoulders
                    foreach (var dz in new[] { -crossLen * 0.33f, crossLen * 0.33f })
                        foreach (var sx in new[] { -1f, 1f })
                        {
                            var a = new Vector3(q.x + sx * dia * 0.42f, top - CrossH, q.z + dz);
                            var b = new Vector3(q.x + sx * dia * 0.25f, wTop - dia * 0.06f, q.z + dz);
                            Box((a + b) / 2f, new Vector3(0.035f, (a - b).magnitude + 0.01f, 0.05f),
                                Quaternion.FromToRotation(Vector3.up, a - b));
                        }
                }
                if (tier == 0)          // adapter block up to the beam
                    Box(new Vector3(xc, top / 2f, z), new Vector3(0.24f, Mathf.Abs(top) + 0.01f, crossLen * 0.6f), id);
                else                    // side posts up past the tiers above to the beam
                    foreach (var sx in new[] { -1f, 1f })
                        foreach (var dz in new[] { -crossLen * 0.4f, crossLen * 0.4f })
                            Box(new Vector3(xc + sx * (halfW - 0.03f), top / 2f, z + dz), new Vector3(0.05f, Mathf.Abs(top), 0.05f), id);
            }
            var m = new Mesh { name = key + "_rack" };
            m.SetVertices(v); m.SetTriangles(t, 0); m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }

        // ------------------------------------------------------------------ tail turret (B-52H)
        // The B-52H's tail armament, an optional station (the B-52J has none: the guns came off from late 1991): the
        // M61A1 20 mm six-barrel cannon, aimed by the AN/ASG-21 radar fire-control system and worked by the gunner from
        // the upper deck. Built on the game's own 20 mm rotary turret mount (turret_20mm_rotary: Turret + Gun, an AI
        // gunner that engages what the aircraft detects, manual aim along the cockpit view when its station is
        // selected), with the model's own barrels (tools/blender_tailgun.py) and an aft-facing firing cone.
        // Round count: 1,242 is the commonly quoted figure (not confirmed from a primary source); 4,000 rounds/min is
        // the M61A1's low rate.
        public const string TailTurretKey = "B52_M61A1_tail";
        const int TailRounds = 1242;
        const float TailRpm = 4000f;
        const float TailCone = 60f;            // half-angle of the coverage cone behind the tail, deg
        const float TailMuzzle = 0.71f;        // barrel tips, m aft of the pivot (blender_tailgun.py)

        /// <summary>The tail turret mount. <paramref name="barrels"/>: the barrels' mesh with its origin on the
        /// turret's pivot; <paramref name="barrelRot"/> and <paramref name="barrelScale"/> place it in the mount's frame
        /// (the mount faces aft: its +Z is the aircraft's -Z).</summary>
        public static ScriptableObject MakeTailTurret(Mesh barrels, Quaternion barrelRot, Vector3 barrelScale, Action<string> log)
        {
            var srcMount = AssetDatabase.LoadAssetAtPath<ScriptableObject>(DoNotShip + "turret_20mm_rotary_PLACEHOLDER.asset");
            var srcInfo = AssetDatabase.LoadAssetAtPath<ScriptableObject>(DoNotShip + "Gun20mm_Rotary_Turret_PLACEHOLDER.asset");
            var srcPrefab = srcMount ? (GameObject)new SerializedObject(srcMount).FindProperty("prefab").objectReferenceValue : null;
            if (!srcMount || !srcInfo || !srcPrefab) { log("  ! tail turret: donor turret_20mm_rotary missing"); return null; }
            const string key = TailTurretKey;
            const string name = "M61A1 Vulcan 20 mm (B-52H tail turret)";
            const string desc = "The B-52H's tail armament: an M61A1 six-barrel 20 mm cannon aimed by the AN/ASG-21 radar " +
                "fire-control system and worked by the gunner from the upper deck. Engages fighters closing from behind on its " +
                "own, or select it to aim it yourself. The B-52J flies without it: the tail guns came off from 1991.";

            var info = ScriptableObject.CreateInstance(srcInfo.GetType()); EditorUtility.CopySerialized(srcInfo, info);
            info.name = "info_" + key;
            var infoPath = $"{Dir}/info_{key}.asset";
            AssetDatabase.DeleteAsset(infoPath); AssetDatabase.CreateAsset(info, infoPath);
            var isx = new SerializedObject(info);
            isx.FindProperty("weaponName").stringValue = name;
            isx.FindProperty("shortName").stringValue = "M61 TAIL";
            isx.FindProperty("description").stringValue = desc;
            isx.ApplyModifiedPropertiesWithoutUndo();

            var go = (GameObject)PrefabUtility.InstantiatePrefab(srcPrefab);
            PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            go.name = key;
            Transform Kid(string n) => go.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == n);
            var turretT = Kid("turret"); var gunT = Kid("gun"); var muzzleT = Kid("muzzle"); var barrelT = Kid("barrel");
            if (!turretT || !gunT || !muzzleT || !barrelT) { Object.DestroyImmediate(go); log("  ! tail turret: donor layout changed (turret/gun/muzzle/barrel)"); return null; }

            // Visuals: the donor's turret head and gun body go; the boot they sit in is the hull's (it stays on the tail).
            foreach (var t in new[] { turretT, gunT })
            {
                Object.DestroyImmediate(t.GetComponent<MeshRenderer>());
                Object.DestroyImmediate(t.GetComponent<MeshFilter>());
            }
            var colorable = go.GetComponent(T("ColorableMount"));
            if (colorable) Object.DestroyImmediate(colorable);     // nothing left to paint (Hardpoint looks for it with TryGetComponent)
            gunT.localPosition = Vector3.zero;                       // elevation pivot = traverse pivot = the boot's centre
            muzzleT.localPosition = new Vector3(0f, 0f, TailMuzzle);
            muzzleT.localRotation = Quaternion.identity;
            barrelT.localPosition = Vector3.zero;                    // spun by the Gun round its local Z: the barrels' axis
            barrelT.localRotation = barrelRot;
            barrelT.localScale = barrelScale;
            barrelT.GetComponent<MeshFilter>().sharedMesh = barrels;
            barrelT.GetComponent<MeshRenderer>().sharedMaterial = PaintMaterial("B52_GunMetal", "gunmetal", new Color(0.16f, 0.16f, 0.17f, 1f), new Color(0.8f, 0.8f, 0.8f, 0.55f));

            // One firing cone straight aft; the donor's (a chin turret's: forward-down, with up and down exclusions) go.
            foreach (var n in new[] { "firingCone_positiveForward", "firingCone_negativeDown", "firingCone_negativeUp" })
            { var c = Kid(n); if (c) Object.DestroyImmediate(c.gameObject); }
            var cone = new GameObject("firingCone_aft").transform;
            cone.SetParent(go.transform, false);

            var turret = turretT.GetComponent(T("Turret"));
            var ts = new SerializedObject(turret);
            ts.FindProperty("targetAcquisitionMode").enumValueIndex = 0;   // parentUnitTargetDetector: what the B-52 detects
            ts.FindProperty("traverseRange").floatValue = TailCone;        // +-60 deg either side
            ts.FindProperty("traverseRate").floatValue = 90f;              // radar-slaved: fast
            ts.FindProperty("elevationRate").floatValue = 90f;
            ts.FindProperty("minElevation").floatValue = -TailCone;
            ts.FindProperty("maxElevation").floatValue = TailCone;
            ts.FindProperty("lockTime").floatValue = 0.4f;                 // radar lock-on before the first burst
            var fc = ts.FindProperty("firingCones");
            fc.arraySize = 1;
            var c0 = fc.GetArrayElementAtIndex(0);
            c0.FindPropertyRelative("transform").objectReferenceValue = cone;
            c0.FindPropertyRelative("coneAngle").floatValue = TailCone;
            c0.FindPropertyRelative("exclusion").boolValue = false;
            ts.ApplyModifiedPropertiesWithoutUndo();

            var gun = gunT.GetComponent(T("Gun"));
            var gs = new SerializedObject(gun);
            gs.FindProperty("info").objectReferenceValue = info;
            gs.FindProperty("fireRate").floatValue = TailRpm;
            gs.FindProperty("magazineCapacity").intValue = TailRounds;
            gs.FindProperty("recoilTransform").objectReferenceValue = null;   // the donor's kick would push the barrels out of the boot
            gs.ApplyModifiedPropertiesWithoutUndo();

            var prefabPath = $"{Dir}/{key}.prefab";
            AssetDatabase.DeleteAsset(prefabPath);
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, prefabPath);
            Object.DestroyImmediate(go);

            var mount = ScriptableObject.CreateInstance(srcMount.GetType());
            EditorUtility.CopySerialized(srcMount, mount);
            mount.name = key;
            var so = new SerializedObject(mount);
            so.FindProperty("prefab").objectReferenceValue = prefab;
            so.FindProperty("jsonKey").stringValue = key;
            so.FindProperty("info").objectReferenceValue = info;
            so.FindProperty("mountName").stringValue = $"M61A1 tail turret ({TailRounds:N0} rounds, B-52H)";
            so.FindProperty("ammo").intValue = TailRounds;
            so.FindProperty("turret").boolValue = true;
            so.FindProperty("colorable").boolValue = false;
            so.FindProperty("mass").floatValue = TailRounds * 0.30f;     // 20 mm rounds with links
            so.FindProperty("emptyMass").floatValue = 550f;              // gun, drive, ammunition can and ASG-21 electronics
            so.FindProperty("drag").floatValue = 0f; so.FindProperty("emptyDrag").floatValue = 0f;   // already part of the tail cone
            so.FindProperty("RCS").floatValue = 0f; so.FindProperty("emptyRCS").floatValue = 0f;
            so.FindProperty("disabled").boolValue = false;
            so.ApplyModifiedPropertiesWithoutUndo();
            var path = $"{Dir}/{key}.asset";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(mount, path);
            log($"  tail turret {key}: {TailRounds} rounds at {TailRpm} rpm, +-{TailCone} deg cone, barrels {barrels.vertexCount} vertices");
            return mount;
        }

        static Material rackMat;
        static Material RackMaterial() =>
            rackMat ? rackMat : rackMat = PaintMaterial("B52_RackPaint", "rack", new Color(0.36f, 0.37f, 0.38f, 1f), new Color(0.35f, 0.35f, 0.35f, 0.38f));

        /// <summary>A plain-colour skin material (the game's aircraft skin shader): <paramref name="metalSmooth"/> R = metal, A = smoothness.</summary>
        static Material PaintMaterial(string matName, string tex, Color colour, Color metalSmooth)
        {
            var path = $"{Dir}/{matName}.mat";
            AssetDatabase.DeleteAsset(path);
            var shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Blueprinter/_donotship/Shader/Shader Graphs_AircraftSkin_PLACEHOLDER.shader");
            Texture2D Tex(string name, Color c, bool linear)
            {
                var p = $"{Dir}/{name}.png";
                var tx = new Texture2D(4, 4, TextureFormat.RGBA32, false, linear);
                tx.SetPixels(Enumerable.Repeat(c, 16).ToArray()); tx.Apply();
                System.IO.File.WriteAllBytes(p, tx.EncodeToPNG()); Object.DestroyImmediate(tx);
                AssetDatabase.ImportAsset(p);
                var ti = (TextureImporter)AssetImporter.GetAtPath(p); ti.sRGBTexture = !linear; ti.SaveAndReimport();
                return AssetDatabase.LoadAssetAtPath<Texture2D>(p);
            }
            var col = Tex(tex + "_colour", colour, false);
            var nrm = Tex(tex + "_normal", new Color(0.5f, 0.5f, 1f, 1f), true);
            var ms = Tex(tex + "_ms", metalSmooth, true);
            var white = Tex(tex + "_white", Color.white, true);
            var mat = new Material(shader) { name = matName };
            mat.SetTexture("_Basecolor", col); mat.SetTexture("_Livery", col); mat.SetTexture("_BasecolorDmg", col);
            mat.SetTexture("_Normal", nrm); mat.SetTexture("_NormalDmg", nrm);
            mat.SetTexture("_Metallic", ms); mat.SetTexture("_AO", white); mat.SetFloat("_HitPoints", 100f);
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }
    }
}
