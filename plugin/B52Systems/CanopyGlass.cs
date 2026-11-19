using System.Collections.Generic;
using UnityEngine;

namespace B52Systems
{
    /// <summary>
    /// Makes every pane of the B-52's windscreen the game's clear glass.
    ///
    /// The B-52 uses the donor bomber's glass material, and that shader takes a mask texture (fastBomber1_glass_b) laid
    /// out for the donor's own canopy: four pane shapes in light, semi-clear glass (RGBA 198,166,132,117) and its frames in
    /// dark, nearly opaque brown (66,55,44,209). The B-52's glass faces map across that mask with UVs made for a different
    /// canopy, so parts of every pane picked up the donor's frame areas and drew a dark film: from outside, black patches
    /// round the panes that didn't line up with the B-52's frames.
    ///
    /// The B-52's frames are real geometry and all its glass faces are panes, so it needs no mask: its own copy of the
    /// material gets a plain texture of the donor's pane value. Everything else about the game's glass (shader, colour,
    /// reflections, smoothness) stays as it is. In cockpit view the game hides this glass, so the view from the seats is
    /// unchanged. Found with tests/ingame/b52_glass_texture.txt (glasstex).
    /// </summary>
    internal static class CanopyGlass
    {
        private static readonly Color32 Pane = new Color32(198, 166, 132, 117);
        private static Texture2D paneTex;
        private static readonly Dictionary<Material, Material> fixedMats = new Dictionary<Material, Material>();

        internal static void Apply(Aircraft aircraft)
        {
            foreach (var r in aircraft.GetComponentsInChildren<MeshRenderer>(true))
            {
                var mats = r.sharedMaterials; bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (m == null || !m.name.ToLowerInvariant().Contains("glass")) continue;
                    if (!fixedMats.TryGetValue(m, out var f))
                    {
                        string mask = MaskProperty(m);
                        if (mask == null) { fixedMats[m] = m; continue; }
                        f = new Material(m) { name = m.name + " (B-52 panes)" };
                        f.SetTexture(mask, PaneTexture());
                        fixedMats[m] = f;
                        Plugin.Log.LogInfo($"B-52J canopy glass: {m.name} without the donor's pane mask ({mask})");
                    }
                    if (f != m) { mats[i] = f; changed = true; }
                }
                if (changed) r.sharedMaterials = mats;
            }
        }

        // The shader's one texture property (the mask); its name is a Shader Graph id, so it is found, not hard-coded.
        private static string MaskProperty(Material m)
        {
            var s = m.shader;
            for (int p = 0; p < s.GetPropertyCount(); p++)
            {
                var n = s.GetPropertyName(p);
                if (s.GetPropertyType(p) == UnityEngine.Rendering.ShaderPropertyType.Texture && !n.StartsWith("unity_") && m.GetTexture(n) != null) return n;
            }
            return null;
        }

        private static Texture2D PaneTexture()
        {
            if (paneTex) return paneTex;
            paneTex = new Texture2D(4, 4, TextureFormat.RGBA32, false, false) { name = "B52_glass_pane", wrapMode = TextureWrapMode.Repeat };
            var px = new Color32[16];
            for (int i = 0; i < px.Length; i++) px[i] = Pane;
            paneTex.SetPixels32(px); paneTex.Apply(false, true);
            return paneTex;
        }
    }
}
