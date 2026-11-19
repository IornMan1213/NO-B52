using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace B52Tools
{
    /// <summary>
    /// The B-52's cockpit damage display (the game's StatusDisplay, shown in the HUD's corner when the aircraft is hit).
    ///
    /// The donor's showed the Alkyon: its silhouette and its parts, matched to the aircraft's parts by name (so most of
    /// the B-52's never lit up), and four engine-fire lights. This one is built from the donor's display (the game's
    /// script, HUD layout, font and UI material) with everything aircraft-specific replaced:
    /// - the background is the B-52 from above with its 28 sections outlined (tools/blender_status.py,
    ///   tools/make_status_display.py);
    /// - one mask per B-52 part, named as the part, which the game tints yellow to red as it takes damage and shows
    ///   crimson once it has come off;
    /// - eight engine-fire lights, one on each engine, named as the engines' fire messages ("ENGINE n FIRE").
    /// </summary>
    public static partial class B52Builder
    {
        static readonly Type UiImage = Type.GetType("UnityEngine.UI.Image, UnityEngine.UI");
        static readonly Type UiText = Type.GetType("UnityEngine.UI.Text, UnityEngine.UI");
        const float DisplaySize = 290f;     // the donor display's square, in HUD units

        static GameObject BuildStatusDisplay()
        {
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(DoNotShip + "/GameObject/StatusDisplay_FastBomber1_PLACEHOLDER.prefab");
            if (!src || UiImage == null || UiText == null) { Note("  ! status display: donor prefab or UI types missing"); return null; }
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(src);
            PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            inst.name = "StatusDisplay_B52";
            var root = inst.transform;
            var sd = inst.GetComponent(T("StatusDisplay"));

            // The donor's part images and fire lights: keep one of each as a template for the game's material and font.
            var children = root.Cast<Transform>().ToList();
            var partTemplate = children.FirstOrDefault(c => c.name == "wing1_L");
            var fireTemplate = children.FirstOrDefault(c => c.name == "ENGINE 1 FIRE");
            Object partMaterial = partTemplate ? SO(partTemplate.GetComponent(UiImage)).FindProperty("m_Material").objectReferenceValue : null;
            if (fireTemplate) fireTemplate.SetParent(null, false);
            foreach (var c in children) if (c && c != fireTemplate) Object.DestroyImmediate(c.gameObject);

            // Background: the B-52.
            var bgImage = inst.GetComponent(UiImage);
            Set(bgImage, "m_Sprite", p => p.objectReferenceValue = StatusSprite("status_bg"));

            // The display's frame (tools/blender_status.py): the parts' bounds from above, square, 6% margin, nose up.
            var partNames = Phys.Keys.ToList();
            var b = new Bounds(); bool first = true;
            foreach (var n in partNames)
            {
                var t = Find(ourRoot, n); var mf = t ? t.GetComponent<MeshFilter>() : null;
                if (!mf) { var vis = t ? t.Find(n + "_visible") : null; mf = vis ? vis.GetComponent<MeshFilter>() : null; }
                if (!mf || !mf.sharedMesh) continue;
                foreach (var v in mf.sharedMesh.vertices) { var w = ourRoot.InverseTransformPoint(mf.transform.TransformPoint(v)); if (first) { b = new Bounds(w, Vector3.zero); first = false; } else b.Encapsulate(w); }
            }
            float extent = Mathf.Max(b.size.x, b.size.z) * 1.06f;
            // The masks show the right wing on the right; the same here whichever way the import mirrored x.
            var rightWing = Find(ourRoot, "wing1_R");
            float xs = rightWing && ourRoot.InverseTransformPoint(rightWing.position).x < 0f ? -1f : 1f;
            Vector2 ToDisplay(Vector3 local) => new Vector2(xs * (local.x - b.center.x) / extent * DisplaySize, (local.z - b.center.z) / extent * DisplaySize);

            // One mask per part, filling the display, named as the part.
            var entries = new List<Object>();
            foreach (var n in partNames)
            {
                var sprite = StatusSprite("status_" + n);
                if (!sprite) { Note($"  ! status display: no mask for {n}"); continue; }
                var go = new GameObject(n, typeof(RectTransform));
                go.transform.SetParent(root, false);
                var rt = (RectTransform)go.transform;
                rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
                var img = go.AddComponent(UiImage);
                Set(img, "m_Sprite", p => p.objectReferenceValue = sprite);
                Set(img, "m_Color", p => p.colorValue = new Color(1f, 1f, 0f, 1f));
                Set(img, "m_RaycastTarget", p => p.boolValue = false);
                if (partMaterial) Set(img, "m_Material", p => p.objectReferenceValue = partMaterial);
                entries.Add(img);
            }

            // Engine-fire lights, one on each engine, the label outboard of it.
            var fires = new List<Object>();
            if (fireTemplate)
            {
                for (int i = 1; i <= 8; i++)
                {
                    var eng = Find(ourRoot, "engine" + i);
                    if (!eng) continue;
                    var go = Object.Instantiate(fireTemplate.gameObject, root, false);
                    go.name = $"ENGINE {i} FIRE";                        // = Turbofan.failureMessage (B52Spec.Engines)
                    var rt = (RectTransform)go.transform;
                    rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
                    rt.anchoredPosition = ToDisplay(ourRoot.InverseTransformPoint(eng.position));
                    rt.sizeDelta = new Vector2(5f, 14f);                  // a short red bar on the engine
                    var label = go.transform.Cast<Transform>().FirstOrDefault();
                    if (label)
                    {
                        label.name = $"ENG {i} FIRE";
                        var txt = label.GetComponent(UiText);
                        if (txt) { Set(txt, "m_Text", p => p.stringValue = $"ENG {i}"); Set(txt, "m_FontData.m_FontSize", p => p.intValue = 18); }
                        var lr = (RectTransform)label;
                        lr.anchorMin = lr.anchorMax = lr.pivot = new Vector2(0.5f, 0.5f);
                        lr.sizeDelta = new Vector2(60f, 20f);
                        lr.anchoredPosition = new Vector2(0f, -22f);         // just behind the engine
                    }
                    fires.Add(go);
                }
                Object.DestroyImmediate(fireTemplate.gameObject);
            }

            Set(sd, "statusDisplays", p =>
            {
                p.arraySize = entries.Count;
                for (int i = 0; i < entries.Count; i++)
                {
                    var e = p.GetArrayElementAtIndex(i);
                    e.FindPropertyRelative("partImage").objectReferenceValue = entries[i];
                    e.FindPropertyRelative("redStatusThreshold").floatValue = 30f;   // red from 30 hp down, as the game's
                }
            });
            SetRefArray(sd, "failureIndicators", fires);
            SetRef(sd, "aircraftBackground", bgImage);

            var path = Gen + "/StatusDisplay_B52.prefab";
            var prefab = PrefabUtility.SaveAsPrefabAsset(inst, path);
            Object.DestroyImmediate(inst);
            Note($"Status display: {entries.Count} part masks, {fires.Count} engine-fire lights, frame {extent:F1} m -> {path}");
            return prefab;
        }

        static Sprite StatusSprite(string name)
        {
            var path = ModDir + "/Textures/Status/" + name + ".png";
            if (AssetImporter.GetAtPath(path) is TextureImporter ti)
            {
                if (ti.textureType != TextureImporterType.Sprite || ti.mipmapEnabled || !ti.alphaIsTransparency)
                {
                    ti.textureType = TextureImporterType.Sprite; ti.spriteImportMode = SpriteImportMode.Single;
                    ti.alphaIsTransparency = true; ti.mipmapEnabled = false; ti.textureCompression = TextureImporterCompression.CompressedHQ;
                    ti.SaveAndReimport();
                }
            }
            else
            {
                AssetDatabase.ImportAsset(path);
                if (AssetImporter.GetAtPath(path) is TextureImporter t2) { t2.textureType = TextureImporterType.Sprite; t2.alphaIsTransparency = true; t2.mipmapEnabled = false; t2.SaveAndReimport(); }
            }
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }
    }
}
