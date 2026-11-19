using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace B52Tools
{
    /// <summary>
    /// How much of the donor bomber (FastBomber1) is still in the B-52: written by every build to
    /// B52DonorAudit.md in the Unity project (tools/build_all.sh copies it to docs/DONOR_AUDIT.md).
    ///
    /// The builder starts from the donor prefab. Wherever it takes something from it (copies a component, moves a whole
    /// object across, or clones one) it tells this class where the thing came from. At the end every such component is
    /// compared, setting by setting, with the untouched donor prefab asset: a setting still equal to the donor's is a
    /// donor value, one the builder changed is the B-52's own. References between the aircraft's own objects are wiring
    /// and not counted. Components the builder added fresh are the B-52's own outright. The aircraft definition and
    /// parameters (copied from the donor's) are compared the same way, and every game asset the aircraft still uses is
    /// listed with who uses it.
    /// </summary>
    internal static class B52DonorAudit
    {
        private struct Origin { public string path; public string type; public int index; public string how; }

        private static readonly Dictionary<int, Origin> template = new Dictionary<int, Origin>();   // donor component id -> where it is in the donor
        private static readonly Dictionary<int, Origin> origin = new Dictionary<int, Origin>();     // our component id -> its donor original
        private static readonly List<(string name, Object ours, Object donor)> assets = new List<(string, Object, Object)>();

        // Runtime state the game sets itself as the aircraft spawns and flies (ids, its rigidbody, hit points reset to
        // 100, speed, g, radar altitude, network sync, fuel level and capacity): serialized, but not a donor choice.
        private static readonly HashSet<string> RuntimeState = new HashSet<string>
        {
            "id", "rb", "hitPoints", "centerOfMass", "SyncSettings", "MapUniqueName", "MapHQ", "MapAirbase", "networked",
            "HasRequestedRearm", "airDensity", "radarAlt", "speed", "spawnedEffects", "accel", "velocityPrev", "gForce",
            "gearState", "fuelCapacity", "fuelMass", "definition", "joints[].joint", "currentState", "dead", "ejected",
            "playerControlled", "player", "flightInfo", "skill", "bravery",
        };

        // Unity's own bookkeeping, not settings.
        private static readonly HashSet<string> Bookkeeping = new HashSet<string>
        {
            "m_ObjectHideFlags", "m_CorrespondingSourceObject", "m_PrefabInstance", "m_PrefabAsset", "m_GameObject",
            "m_Enabled", "m_EditorHideFlags", "m_EditorClassIdentifier", "m_Script", "m_Name",
        };

        // Settings the builder sets on purpose, per object: a path, and everything under it.
        private static readonly Dictionary<int, HashSet<string>> chosen = new Dictionary<int, HashSet<string>>();

        /// <summary>The builder set this setting (and everything under it) for the B-52: it counts as chosen even where
        /// the value equals the donor's. "*": the builder set every setting of this object (or reset it to Unity's
        /// defaults first), so none of it is the donor's.</summary>
        internal static void Chose(Object o, string path)
        {
            if (o == null) return;
            if (!chosen.TryGetValue(o.GetInstanceID(), out var set)) chosen[o.GetInstanceID()] = set = new HashSet<string>();
            set.Add(path);
        }

        private static bool IsChosen(Object o, string path)
        {
            if (!chosen.TryGetValue(o.GetInstanceID(), out var set)) return false;
            foreach (var c in set) if (c == "*" || path == c || path.StartsWith(c + ".") || path.StartsWith(c + "[")) return true;
            return false;
        }

        internal static void Begin(Transform donorRoot)
        {
            template.Clear(); origin.Clear(); assets.Clear(); chosen.Clear();
            foreach (var t in donorRoot.GetComponentsInChildren<Transform>(true))
            {
                var path = PathOf(t, donorRoot);
                foreach (var g in t.GetComponents<Component>().Where(c => c != null && !(c is Transform)).GroupBy(c => c.GetType()))
                {
                    int i = 0;
                    foreach (var c in g) template[c.GetInstanceID()] = new Origin { path = path, type = g.Key.Name, index = i++, how = "moved" };
                }
            }
        }

        /// <summary>A donor component copied onto one of ours (or a copy of a copy).</summary>
        internal static void Copied(Component src, Component dst)
        {
            if (src == null || dst == null) return;
            if (template.TryGetValue(src.GetInstanceID(), out var o) || origin.TryGetValue(src.GetInstanceID(), out o))
            {
                o.how = "copied"; origin[dst.GetInstanceID()] = o;
            }
        }

        /// <summary>A clone of a donor object (or of a moved one): its components pair up with the original's in order.</summary>
        internal static void Cloned(GameObject src, GameObject dst)
        {
            var a = src.GetComponentsInChildren<Component>(true).Where(c => c != null && !(c is Transform)).ToArray();
            var b = dst.GetComponentsInChildren<Component>(true).Where(c => c != null && !(c is Transform)).ToArray();
            for (int i = 0; i < Mathf.Min(a.Length, b.Length); i++)
            {
                if (a[i].GetType() != b[i].GetType()) continue;
                if (chosen.TryGetValue(a[i].GetInstanceID(), out var ch)) chosen[b[i].GetInstanceID()] = new HashSet<string>(ch);
                if (template.TryGetValue(a[i].GetInstanceID(), out var o) || origin.TryGetValue(a[i].GetInstanceID(), out o))
                {
                    o.how = "cloned"; origin[b[i].GetInstanceID()] = o;
                }
            }
        }

        /// <summary>An asset (definition, parameters) made as a copy of a donor asset.</summary>
        internal static void CopiedAsset(string name, Object ours, Object donor) => assets.Add((name, ours, donor));

        internal static void Write(Transform ourRoot, string donorPrefabPath, string outPath, string donotship)
        {
            var donorAsset = AssetDatabase.LoadAssetAtPath<GameObject>(donorPrefabPath);
            var sb = new StringBuilder();
            var rows = new List<(string where, string type, string how, int total, int donor, List<string> fields)>();
            int fresh = 0; var freshTypes = new SortedDictionary<string, int>();
            foreach (var c in ourRoot.GetComponentsInChildren<Component>(true))
            {
                if (c == null || c is Transform) continue;
                bool gameType = c is MonoBehaviour && !(c.GetType().Namespace ?? "").StartsWith("UnityEngine");
                Origin o;
                bool moved = template.TryGetValue(c.GetInstanceID(), out o);
                if (!moved && !origin.TryGetValue(c.GetInstanceID(), out o))
                {
                    if (gameType) { fresh++; freshTypes[c.GetType().Name] = freshTypes.TryGetValue(c.GetType().Name, out var n) ? n + 1 : 1; }
                    continue;
                }
                var dt = string.IsNullOrEmpty(o.path) ? donorAsset.transform : donorAsset.transform.Find(o.path);
                var dc = dt ? dt.GetComponents(c.GetType()).ElementAtOrDefault(o.index) : null;
                if (dc == null) { rows.Add((PathOf(c.transform, ourRoot), c.GetType().Name, o.how + " (donor original not found)", 0, 0, new List<string>())); continue; }
                var (total, same) = Compare(c, dc, ourRoot);
                rows.Add((PathOf(c.transform, ourRoot), c.GetType().Name, $"{o.how} from {(string.IsNullOrEmpty(o.path) ? "(root)" : o.path)}", total, same.Count, same));
            }

            // Game assets the aircraft uses (Blueprinter's placeholders for the game's own).
            var used = new SortedDictionary<string, SortedSet<string>>();
            foreach (var c in ourRoot.GetComponentsInChildren<Component>(true))
            {
                if (c == null) continue;
                var it = new SerializedObject(c).GetIterator();
                while (it.Next(true))
                {
                    if (it.propertyType != SerializedPropertyType.ObjectReference || it.objectReferenceValue == null) continue;
                    var p = AssetDatabase.GetAssetPath(it.objectReferenceValue);
                    if (!p.StartsWith(donotship)) continue;
                    var key = $"{Path.GetFileName(Path.GetDirectoryName(p))}: {Path.GetFileNameWithoutExtension(p).Replace("_PLACEHOLDER", "")}";
                    if (!used.TryGetValue(key, out var s)) used[key] = s = new SortedSet<string>();
                    s.Add($"{c.gameObject.name} ({c.GetType().Name})");
                }
            }

            int compTotal = rows.Count, settingsTotal = rows.Sum(r => r.total), settingsDonor = rows.Sum(r => r.donor);
            sb.AppendLine("# What is left of the donor bomber");
            sb.AppendLine();
            sb.AppendLine("Written by the B-52 build (`B52Tools.B52DonorAudit`); do not edit by hand. The B-52 is built starting from the");
            sb.AppendLine("game's FastBomber1 prefab. This lists everything still taken from it, compared with the donor prefab setting by");
            sb.AppendLine("setting. A setting equal to the donor's is listed as a donor value, even where the value happens to be right for");
            sb.AppendLine("the B-52: it was not chosen for the B-52. References between the B-52's own objects are wiring and are not counted,");
            sb.AppendLine("nor is runtime state the game sets itself at spawn (ids, hit points, speed, g, fuel level and the like).");
            sb.AppendLine();
            sb.AppendLine("## Summary");
            sb.AppendLine();
            sb.AppendLine($"- Components taken from the donor (copied, moved across or cloned): **{compTotal}**; settings in them: {settingsTotal}, of which **{settingsDonor} still at the donor's value** ({(settingsTotal > 0 ? 100.0 * settingsDonor / settingsTotal : 0):F0}%).");
            sb.AppendLine($"- Game components the builder added fresh (B-52 values throughout): {fresh} ({string.Join(", ", freshTypes.Select(k => $"{k.Key} x{k.Value}"))}).");
            foreach (var (name, ours, donor) in assets)
            {
                var (t, same) = Compare(ours, donor, null);
                sb.AppendLine($"- {name}: copied from the donor's; {same.Count} of {t} settings still at the donor's value.");
            }
            sb.AppendLine($"- Game assets used: {used.Count} (below).");
            sb.AppendLine();

            sb.AppendLine("## By component type");
            sb.AppendLine();
            sb.AppendLine("| Component | Count | Settings | Still donor | How |");
            sb.AppendLine("|---|---|---|---|---|");
            foreach (var g in rows.GroupBy(r => r.type).OrderByDescending(g => g.Sum(r => r.donor)))
                sb.AppendLine($"| {g.Key} | {g.Count()} | {g.Sum(r => r.total)} | {g.Sum(r => r.donor)} | {string.Join(", ", g.Select(r => r.how.Split(' ')[0]).Distinct())} |");
            sb.AppendLine();

            sb.AppendLine("## Donor values, component by component");
            sb.AppendLine();
            sb.AppendLine("Grouped where several components share the same donor settings (e.g. the eight engines).");
            sb.AppendLine();
            foreach (var g in rows.GroupBy(r => (r.type, r.how, string.Join("|", r.fields))).OrderBy(g => g.Key.type).ThenBy(g => g.First().where))
            {
                var first = g.First();
                var where = g.Count() == 1 ? $"`{first.where}`" : $"{g.Count()}: " + string.Join(", ", g.Select(r => $"`{r.where}`"));
                sb.AppendLine($"### {first.type}: {where}");
                sb.AppendLine();
                sb.AppendLine($"{first.how}; {first.donor} of {first.total} settings still the donor's.");
                if (first.fields.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine(Collapse(first.fields));
                }
                sb.AppendLine();
            }

            foreach (var (name, ours, donor) in assets)
            {
                var (t, same) = Compare(ours, donor, null);
                sb.AppendLine($"## {name}: donor values");
                sb.AppendLine();
                sb.AppendLine($"{same.Count} of {t} settings still the donor's.");
                if (same.Count > 0) { sb.AppendLine(); sb.AppendLine(Collapse(same)); }
                sb.AppendLine();
            }

            sb.AppendLine("## Game assets used");
            sb.AppendLine();
            sb.AppendLine("Blueprinter swaps these placeholders for the game's own assets at load. Some are the donor bomber's own (names");
            sb.AppendLine("starting fastBomber1 / FastBomber1); the rest are shared by the game's aircraft.");
            sb.AppendLine();
            foreach (var kv in used)
                sb.AppendLine($"- **{kv.Key}**: {string.Join("; ", kv.Value.Take(6))}{(kv.Value.Count > 6 ? $"; and {kv.Value.Count - 6} more" : "")}");

            File.WriteAllText(outPath, sb.ToString());
        }

        // Every setting of ours (leaf properties), and which of them equal the donor's.
        private static (int total, List<string> same) Compare(Object ours, Object donor, Transform ourRoot)
        {
            var a = new SerializedObject(ours); var b = new SerializedObject(donor);
            var same = new List<string>(); int total = 0;
            var it = a.GetIterator();
            bool enter = true;
            while (it.Next(enter))
            {
                enter = it.propertyType == SerializedPropertyType.Generic || (it.isArray && it.propertyType != SerializedPropertyType.String);
                if (enter) continue;
                var path = it.propertyPath;
                if (Bookkeeping.Contains(path)) continue;
                var head = System.Text.RegularExpressions.Regex.Replace(path.Replace(".Array.data", ""), @"\[\d+\]", "[]");
                if (RuntimeState.Contains(head.Split('.')[0]) || RuntimeState.Contains(head)) continue;
                if (it.propertyType == SerializedPropertyType.ObjectReference)
                {
                    var v = it.objectReferenceValue;
                    if (v != null && ourRoot != null && (v is GameObject g ? g.transform : v is Component c ? c.transform : null) is Transform tr && tr.IsChildOf(ourRoot)) continue;   // wiring
                }
                var d = b.FindProperty(path);
                total++;
                if (IsChosen(ours, path)) continue;
                if (d != null && d.propertyType == it.propertyType && Equal(it, d)) same.Add(path);
            }
            return (total, same);
        }

        private static bool Equal(SerializedProperty a, SerializedProperty b)
        {
            switch (a.propertyType)
            {
                case SerializedPropertyType.Float: return Mathf.Approximately(a.floatValue, b.floatValue);
                case SerializedPropertyType.AnimationCurve:
                {
                    var x = a.animationCurveValue?.keys; var y = b.animationCurveValue?.keys;
                    if (x == null || y == null) return x == y;
                    return x.Length == y.Length && x.Zip(y, (p, q) => Mathf.Approximately(p.time, q.time) && Mathf.Approximately(p.value, q.value) && Mathf.Approximately(p.inTangent, q.inTangent) && Mathf.Approximately(p.outTangent, q.outTangent)).All(e => e);
                }
                case SerializedPropertyType.ObjectReference:
                {
                    var x = a.objectReferenceValue; var y = b.objectReferenceValue;
                    if (x == null || y == null) return x == y;
                    var px = AssetDatabase.GetAssetPath(x); var py = AssetDatabase.GetAssetPath(y);
                    return px == py && x.name == y.name;
                }
                default:
                    try { return Equals(a.boxedValue, b.boxedValue); } catch { return false; }
            }
        }

        // field.Array.data[3].x, field.Array.data[4].x ... -> field[].x; list the distinct shapes.
        private static string Collapse(List<string> fields)
        {
            var shapes = fields.Select(f => System.Text.RegularExpressions.Regex.Replace(f.Replace(".Array.data", ""), @"\[\d+\]", "[]")).ToList();
            var counted = shapes.GroupBy(s => s).Select(g => g.Count() > 1 ? $"{g.Key} x{g.Count()}" : g.Key);
            return "`" + string.Join("`, `", counted) + "`";
        }

        private static string PathOf(Transform t, Transform root)
        {
            var parts = new List<string>();
            for (var x = t; x != null && x != root; x = x.parent) parts.Add(x.name);
            parts.Reverse();
            return string.Join("/", parts);
        }
    }
}
