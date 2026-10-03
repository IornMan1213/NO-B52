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
    }
}
