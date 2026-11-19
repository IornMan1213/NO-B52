using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace B52Systems
{
    /// <summary>
    /// Dims the flight deck for night flying. The interior paints carry a faint glow of their own colour (it stands in
    /// for daylight through the windows; B52Builder.CockpitFill) and the displays glow at full brightness. At night
    /// both are far too bright, and under the game's night vision, which turns exposure right up in the dark, the
    /// whole cockpit flared white. This follows the scene's ambient light, as the night vision's own gain does: by
    /// day nothing changes; at night the paint glow fades to a trace and the displays dim to night level. With night
    /// vision on, the paint glow goes out and the displays dim further, as crews turn them down for goggles.
    /// The interior materials are shared by every B-52J and the light is the same for all of them, so the
    /// materials are set once per change (about twice a second at most), not per aircraft per frame.
    /// </summary>
    public class CockpitLighting : MonoBehaviour
    {
        private const float FillNight = 0.04f, ScreenNight = 0.30f;   // share of the daytime glow left at full night
        private const float FillNvg = 0f, ScreenNvg = 0.10f;          // with night vision on

        private static readonly Dictionary<string, Color> dayEmission = new Dictionary<string, Color>();
        private static readonly FieldInfo NvgVolume = AccessTools.Field(typeof(NightVision), "postProcessing");
        private static float lastFill = -1f, lastScreen = -1f;

        private readonly List<Material> mats = new List<Material>();
        private float next;

        private bool ready;

        // On the first update rather than in Start: Instruments makes the ADI card's own material copy in its Start.
        private void Collect()
        {
            ready = true;
            Transform interior = null;
            foreach (var t in GetComponentsInChildren<Transform>(true)) if (t.name == "cockpit_int") { interior = t; break; }
            if (!interior) { enabled = false; return; }
            foreach (var r in interior.GetComponentsInChildren<Renderer>(true))
                foreach (var m in r.sharedMaterials)
                {
                    if (!m || !m.HasProperty("_EmissionColor") || !m.IsKeywordEnabled("_EMISSION") || mats.Contains(m)) continue;
                    var key = Key(m);
                    if (!dayEmission.ContainsKey(key)) dayEmission[key] = m.GetColor("_EmissionColor");
                    mats.Add(m);
                }
            lastFill = lastScreen = -1f;   // a new aircraft's materials may be fresh copies: set them once
        }

        // The ADI card is a per-aircraft copy ("... (Instance)") of a shared material: both take the shared one's daytime glow.
        private static string Key(Material m) => m.name.Replace(" (Instance)", "");

        private static bool NightVisionOn()
        {
            var nv = NightVision.i;
            if (!nv || NvgVolume == null) return false;
            return NvgVolume.GetValue(nv) is Behaviour b && b && b.enabled;
        }

        private void Update()
        {
            if (!ready) { Collect(); if (!enabled) return; }
            if (Time.unscaledTime < next) return;
            next = Time.unscaledTime + 0.5f;
            var level = NetworkSceneSingleton<LevelInfo>.i;
            float day = level ? Mathf.InverseLerp(0.02f, 0.35f, level.GetAmbientLight()) : 1f;
            bool nvg = NightVisionOn();
            float fill = nvg ? FillNvg : Mathf.Lerp(FillNight, 1f, day);
            float screen = nvg ? ScreenNvg : Mathf.Lerp(ScreenNight, 1f, day);
            if (Mathf.Abs(fill - lastFill) < 0.01f && Mathf.Abs(screen - lastScreen) < 0.01f) return;
            lastFill = fill; lastScreen = screen;
            foreach (var m in mats)
            {
                if (!m || !dayEmission.TryGetValue(Key(m), out var c)) continue;
                m.SetColor("_EmissionColor", c * (m.name.Contains("CP_MFD") ? screen : fill));
            }
        }
    }
}
