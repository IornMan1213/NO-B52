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
        };

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
            new W("AGM86C",  "AGM-86C/D CALCM",    "cruise",      8, 6, 1950),
            new W("AGM84",   "AGM-84 Harpoon",     "ashm2",       0, 4, 691),
            new W("AGM142",  "AGM-142 Have Nap",   "agm_heavy",   0, 2, 1360),
            new W("AGM154",  "AGM-154 JSOW",       "bomb_glide1", 0, 6, 483),
            new W("AGM129",  "AGM-129 ACM",        "cruise_nuke", 12, 0, 1250),
            // Conventional gravity
            new W("MK82",    "Mk 82",              "bomb_250",    27, 12, 227),
            new W("M117",    "M117",               "bomb_250",    27, 12, 340),
            new W("MK84",    "Mk 84",              "bomb_500",    12, 9, 907),
            new W("BDU48",   "BDU-48",             "bomb_125",    17, 0, 907),
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
            new W("GBU54",   "GBU-54 Laser JDAM",  "bomb_500_glide", 15, 0, 907),
            // WCMD
            new W("CBU103",  "CBU-103 WCMD",       "bomb_cluster1", 16, 7, 430),
            new W("CBU104",  "CBU-104 WCMD",       "bomb_cluster1", 16, 7, 322),
            new W("CBU105",  "CBU-105 WCMD",       "bomb_cluster1", 16, 7, 417),
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
            foreach (var w in Table)
            {
                var donor = AssetDatabase.LoadAssetAtPath<ScriptableObject>(DoNotShip + Donor[w.family] + "_PLACEHOLDER.asset");
                if (!donor) { log("  ! donor rack missing for " + w.code); continue; }
                if (w.internalCount > 0) bay.Add(MakeMount(w, donor, true, w.internalCount, log));
                if (w.perPylon > 0) pylon.Add(MakeMount(w, donor, false, w.perPylon, log));
            }
            log($"Weapons: {bay.Count} bay racks, {pylon.Count} pylon racks");
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

            var root = new GameObject(key);
            var positions = internalBay ? BayLayout(count, len, dia, w.family) : PylonLayout(count, len, dia);
            for (int i = 0; i < count; i++)
            {
                var m = (GameObject)Object.Instantiate(((Component)unit).gameObject, root.transform);
                m.name = $"{w.code}_{i + 1}";
                m.transform.localPosition = positions[i];
                m.transform.localRotation = Quaternion.identity;
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
            so.FindProperty("mountName").stringValue = $"{w.name} x{count}" + (internalBay ? (count == 8 && w.family.StartsWith("cruise") ? " (rotary)" : "") : " (HSAB)");
            so.FindProperty("ammo").intValue = count;
            so.FindProperty("missileBay").boolValue = internalBay;
            so.FindProperty("mass").floatValue = w.unitKg * count;
            so.FindProperty("emptyMass").floatValue = internalBay ? 0f : 450f;            // HSAB beam + MERs
            so.FindProperty("drag").floatValue = internalBay ? 0f : 0.04f * count + 0.1f;
            so.FindProperty("emptyDrag").floatValue = internalBay ? 0f : 0.1f;
            so.FindProperty("RCS").floatValue = internalBay ? 0f : 0.05f * count;
            so.FindProperty("disabled").boolValue = false;
            so.ApplyModifiedPropertiesWithoutUndo();
            var path = $"{Dir}/{key}.asset";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(mount, path);
            return mount;
        }

        // Bay: 10.3 m long x 1.7 m wide x 1.8 m tall. Cruise missiles go on an 8-round rotary launcher; bombs on
        // clip-in racks in rows x columns x layers. Origin is the bay centre.
        static Vector3[] BayLayout(int n, float len, float dia, string family)
        {
            var p = new Vector3[n];
            if (family.StartsWith("cruise") || family == "tbm_nuke" || family == "ashm1")
            {
                int perRing = 8; int rings = Mathf.CeilToInt(n / (float)perRing);
                for (int i = 0; i < n; i++)
                {
                    float a = (i % perRing) * Mathf.PI * 2f / perRing;
                    int ring = i / perRing;
                    float z = rings == 1 ? 0 : (ring - (rings - 1) / 2f) * Mathf.Min(len * 1.02f, 10.3f / rings);
                    p[i] = new Vector3(Mathf.Cos(a) * 0.55f, Mathf.Sin(a) * 0.55f, z);
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

        // HSAB: a 6 m beam under the pylon carrying triple racks in tandem, extra tiers below.
        static Vector3[] PylonLayout(int n, float len, float dia)
        {
            var p = new Vector3[n];
            int along = Mathf.Clamp(Mathf.FloorToInt(6.2f / (len * 1.05f)), 1, 3);
            const int across = 3;
            int perTier = along * across;
            for (int i = 0; i < n; i++)
            {
                int tier = i / perTier, k = i % perTier, a = k / across, c = k % across;
                p[i] = new Vector3((c - 1) * dia * 1.15f, -dia * 0.6f - tier * dia * 1.2f,
                                   (a - (along - 1) / 2f) * len * 1.05f);
            }
            return p;
        }
    }
}
