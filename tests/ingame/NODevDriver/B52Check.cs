using System;
using System.Linq;
using System.Reflection;
using BepInEx.Bootstrap;
using BepInEx.Logging;
using UnityEngine;

namespace NODevDriverB52
{
    /// <summary>"b52check": is the B-52J mod loaded and set up the way a player gets it? One "[TEST] PASS/FAIL" line each.</summary>
    static class B52Check
    {
        internal static void Run(ManualLogSource log, Action<bool, string> test)
        {
            test(Chainloader.PluginInfos.ContainsKey("com.ironman1213.b52systems"), "B52Systems.dll loaded");
            if (Chainloader.PluginInfos.TryGetValue("com.ironman1213.b52systems", out var info))
            {
                var cfg = info.Instance.Config;
                test(cfg.TryGetEntry<bool>("Telemetry", "Enabled", out var tel) && !(bool)tel.DefaultValue, "telemetry is off by default");
                test(info.Instance.GetComponent("HudReadout") != null, "HUD readout is attached");
            }
            var defs = Resources.FindObjectsOfTypeAll<AircraftDefinition>();
            var b52 = defs.FirstOrDefault(d => d.jsonKey == "B52J");
            test(b52 != null, "B52J aircraft definition registered");
            if (b52 != null) log.LogInfo($"  B52J: {b52.unitName}, span {b52.width} m, description: {b52.description}");
            var keys = Resources.FindObjectsOfTypeAll<ScriptableObject>().Select(o => o.name).ToList();
            foreach (var k in new[] { "B52_ADM160B", "B52_ADM160C" })
                test(keys.Contains(k), $"decoy {k} registered");
            int racks = keys.Count(n => n.StartsWith("B52_") && (n.Contains("_bayx") || n.Contains("_hsabx")));
            test(racks >= 60, $"B-52 weapon racks registered ({racks})");
            test(keys.Contains("B52_AGM86C_hsabx4") && !keys.Contains("B52_AGM86C_hsabx6"), "CALCM pylon rack is x4 (payload limit)");
            var heavy = Chainloader.PluginInfos.ContainsKey("com.ironman1213.heavyhangar");
            log.LogInfo($"  Heavy Hangar installed: {heavy}");
        }

        const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        static Type B52Type(string name) =>
            Chainloader.PluginInfos.TryGetValue("com.ironman1213.b52systems", out var i) ? i.Instance.GetType().Assembly.GetType("B52Systems." + name) : null;

        /// <summary>"b52live": checks on the player's own B-52 while it exists (one PASS/FAIL line each):
        /// the crosswind crab resets for a new aircraft (SY1), one engine sound per pod (SD1), and the altimeter's
        /// three pointers match the altitude (CK1). The fans (EX11) are sampled by the driver over a second.</summary>
        internal static void Live(Aircraft ac, ManualLogSource log, Action<bool, string> test)
        {
            // SY1: CrabSelected outlives the aircraft (static); GearSystem must zero it when its owner isn't this aircraft
            var gear = B52Type("GearSystem");
            var crab = gear?.GetField("CrabSelected", Any); var owner = gear?.GetField("crabOwner", Any);
            if (crab == null || owner == null) test(false, "crab reset: GearSystem fields not found");
            else { owner.SetValue(null, null); crab.SetValue(null, 10f); pendingCrabCheck = (crab, Time.frameCount); }

            // Every particle effect has a material the game can draw (the tyre dust had none: pink squares on dirt)
            var bad = ac.GetComponentsInChildren<ParticleSystemRenderer>(true)
                .Where(r => r.sharedMaterial == null || r.sharedMaterial.shader == null || !r.sharedMaterial.shader.isSupported)
                .Select(r => $"{r.name} ({(r.sharedMaterial ? r.sharedMaterial.name : "no material")} / {(r.sharedMaterial && r.sharedMaterial.shader ? r.sharedMaterial.shader.name : "no shader")})").ToList();
            foreach (var m in Resources.FindObjectsOfTypeAll<Material>().Where(m => bad.Any(b => b.Contains("(" + m.name + " "))))
                log.LogInfo($"  loaded material {m.name}: shader {(m.shader ? m.shader.name + (m.shader.isSupported ? "" : " (not supported)") : "none")}, instance {m.GetInstanceID()}");
            test(bad.Count == 0, bad.Count == 0 ? "particle effects all have a working material"
                                                : $"particle effects without a working material: {string.Join(", ", bad)}");

            // SD1: two turbofans per pod, only the first one's turbine loop audible
            var fans = ac.GetComponentsInChildren<Turbofan>(true);
            var audible = fans.Select(f => typeof(Turbofan).GetField("turbineAudio", Any)?.GetValue(f) as AudioSource)
                              .Where(a => a != null && a.volume > 0f).ToList();
            log.LogInfo($"  engines {fans.Length}, audible turbine loops {audible.Count}: pitch {string.Join(", ", audible.Select(a => a.pitch.ToString("F2")))}");
            test(fans.Length == 8 && audible.Count == 4 && audible.Select(a => Mathf.Round(a.pitch * 100)).Distinct().Count() == 4,
                 $"engine sound: {audible.Count} turbine loops for {fans.Length} engines, each pod pitched differently");

            // CK1: needles turned from their rest pose by the angle the altitude calls for
            var inst = ac.GetComponentInChildren(B52Type("Instruments"), true);
            if (inst == null) { test(false, "altimeter: Instruments not found"); return; }
            float ft = ac.transform.position.GlobalY() * 3.28084f;            // as Instruments does
            string Needle(string field, string rest, float expected)
            {
                var t = B52Type("Instruments").GetField(field, Any)?.GetValue(inst) as Transform;
                var q0 = (Quaternion)(B52Type("Instruments").GetField(rest, Any)?.GetValue(inst) ?? Quaternion.identity);
                if (t == null) return $"{field} missing";
                float got = Quaternion.Angle(q0, t.localRotation), want = Mathf.Min(expected, 360f - expected);
                okNeedles &= Mathf.Abs(got - want) < 3f;
                return $"{field} {got:F0} (want {want:F0})";
            }
            okNeedles = true;
            var line = string.Join(", ", Needle("alt", "alt0", Mathf.Repeat(ft, 1000f) / 1000f * 300f),
                                         Needle("alt1k", "alt1k0", Mathf.Repeat(ft, 10000f) / 10000f * 300f),
                                         Needle("alt10k", "alt10k0", Mathf.Clamp(ft, 0f, 100000f) / 100000f * 300f));
            test(okNeedles, $"altimeter at {ft:F0} ft: {line}");
        }
        static bool okNeedles;
        static (FieldInfo field, int frame)? pendingCrabCheck;

        /// <summary>Called every frame by the driver: the crab check's answer, a few frames after "b52live" set it up.</summary>
        internal static void Tick(Action<bool, string> test)
        {
            if (pendingCrabCheck is var (field, frame) && Time.frameCount > frame + 5)
            {
                float v = (float)field.GetValue(null);
                test(v == 0f, $"crab reset: a crab set in another aircraft is cleared for this one ({v:F0} deg now)");
                pendingCrabCheck = null;
            }
        }
    }
}
