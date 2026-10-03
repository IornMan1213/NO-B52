"""One-off source patch for v0.2.0: hardpoints + loadouts, bay door audio, flap/spoiler tweaks."""
import os
ROOT = os.path.join(os.path.dirname(__file__), '..')
p = os.path.join(ROOT, 'unity/B52Tools/Editor/B52Builder.cs')
s = open(p, encoding='utf-8').read()


def rep(a, b):
    global s
    assert a in s, a[:80]
    s = s.replace(a, b)


rep('''            var wm = Find(ourRoot, "cockpit").GetComponent(T("WeaponManager"));
            Set(wm, "hardpointSets", p => p.arraySize = 0);''', '''            BuildHardpoints();''')

rep('''        static void DropNulls(Object comp, string prop)''', '''        static List<ScriptableObject> BayMounts, PylonMounts;

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

        static void DropNulls(Object comp, string prop)''')

rep('''            var lo = ps.FindProperty("loadouts");
            lo.arraySize = 2;
            for (int i = 0; i < 2; i++) lo.GetArrayElementAtIndex(i).FindPropertyRelative("weapons").arraySize = 0;''', '''            var lo = ps.FindProperty("loadouts");
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
            }''')
rep('''            ps.FindProperty("StandardLoadouts").arraySize = 0;\n''', '')
rep('''                var bd = CopyComponent(donor, h2.gameObject);''', '''                var bd = CopyComponent(donor, h2.gameObject);
                CloneDonorRefs(bd, donor.transform.parent, h2);     // door motor sound''')
rep('''-ourRoot.forward * 0.6f - ourRoot.up * 0.15f''', '''-ourRoot.forward * 0.3f - ourRoot.up * 0.08f''')
rep('''new Vector3(-25f, 0, 0)''', '''new Vector3(-20f, 0, 0)''')
rep('public const string Version = "0.1.4";', 'public const string Version = "0.2.0";')
open(p, 'w', encoding='utf-8').write(s)

p = os.path.join(ROOT, 'tools/blender_prep.py')
s = open(p, encoding='utf-8').read()
s = s.replace("(0.50, 0.52, 0.55, 1)", "(0.36, 0.38, 0.40, 1)")
open(p, 'w', encoding='utf-8').write(s)
print('patched')
